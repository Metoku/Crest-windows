using CrestCore.Application;
using CrestCore.Contracts;

namespace CrestWindows;

/// A bridge to Crest's real browser-state authority. The Windows shell reads
/// its published state and sends typed intents; it never edits a Space or tab.
/// This preview intentionally keeps the seed in memory until persistence and
/// WebView2 profile isolation have a proper Windows engine adapter.
public sealed class CoreWorkspace : IDisposable
{
    #region Variables

    private readonly CrestApp core;
    private SessionState? session;
    private WindowState? window;
    private Guid workspaceId;
    // One-window preview: use a stable device window identity, so CrestCore
    // restores the Space/tab selection instead of creating a new window.
    private static readonly Guid windowId = Guid.Parse("94475118-49b8-430d-8495-91f49b65017c");
    public string StorageDirectory { get; }
    public bool WasRestored { get; private set; }

    public IReadOnlyList<SpaceState> Spaces => session?.Spaces ?? [];
    public Guid? ShownSpaceId => window?.ShownSpaceId;
    public Guid? ShownTabId => window?.ShownTabs.FirstOrDefault(tab => tab.SpaceId == ShownSpaceId)?.TabId;

    #endregion

    #region Constructors

    public CoreWorkspace(string? storageDirectory = null)
    {
        StorageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrestWindowsPreview", "Core");

        // CrestCore owns durable tab/Space records and window selection.
        // Its SQLite session lives only in our preview-specific directory;
        // it never touches a person's existing Crest/Arc/Chromium data.
        core = new CrestApp(new AppConfiguration(StorageDirectory, DevicePlatform.Desktop));

        // Distinguish an existing session from the empty SQLite file that
        // CrestApp creates on first use. Never infer this from File.Exists:
        // an empty initialized database is already on disk.
        try
        {
            Apply(core.Send(new OpenWorkspace(WorkspaceKind.Persistent, Seed: null)));
            WasRestored = true;
        }
        catch (Rejected refused) when (refused.Rejection is NoStoredSession)
        {
            WasRestored = false;
            var seed = core.Query(new FirstInstallSession()) with { DisposableSeedMarker = null };
            Apply(core.Send(new AdoptLegacySession(
                new LegacySession(Core: null, WholeGraph: null, History: [], Journal: null), seed)));
            Apply(core.Send(new OpenWorkspace(WorkspaceKind.Persistent, Seed: null)));
        }
        if (session?.Spaces.FirstOrDefault() is not { } initial)
            throw new InvalidOperationException("CrestCore did not open a persistent Space.");

        Apply(core.Send(new OpenWindow(windowId, workspaceId, Saved: true, CopyingWindowId: null,
            ShowingSpaceId: initial.Id, ShowingTabs: [], RestoresTabs: true)));

        // Configure the fresh two-Space preview once. If the app quit after
        // adopting the initial session but before making Work, finish setup on
        // the next launch without recreating existing Space identities.
        if (session.Spaces.Count == 1 && session.Spaces[0].Settings.Name != "Work")
        {
            var personal = session.Spaces[0];
            if (personal.Settings.Name != "Personal")
                Apply(core.Send(new SetSpaceIdentity(workspaceId, personal.Id, "Personal",
                    personal.Settings.Symbol, SpaceAccent.Indigo)));

            var workId = Guid.NewGuid();
            Apply(core.Send(new CreateSpace(workspaceId, windowId, workId)));
            Apply(core.Send(new SetSpaceIdentity(workspaceId, workId,
                "Work", "briefcase.fill", SpaceAccent.Teal)));
            Apply(core.Send(new ShowSpace(windowId, personal.Id)));
        }
    }

    #endregion

    #region Actions - Spaces

    public SpaceState? SelectedSpace =>
        Spaces.FirstOrDefault(space => space.Id == ShownSpaceId);

    public void ShowSpace(Guid spaceId) => Apply(core.Send(new ShowSpace(windowId, spaceId)));

    #endregion

    #region Actions - Tabs

    public void ShowTab(Guid spaceId, Guid tabId) =>
        Apply(core.Send(new ShowTab(windowId, spaceId, tabId)));

    /// The core's WindowState determines which live pages are shown together.
    /// No local Windows-only split membership is maintained.
    public ShownCards? CurrentCards =>
        window?.Cards.FirstOrDefault(cards => cards.SpaceId == ShownSpaceId);

    public IReadOnlyList<TabState> CurrentVisibleTabs
    {
        get
        {
            var space = SelectedSpace;
            if (space is null) return [];
            var shown = CurrentCards?.TabIds;
            if (shown is null || shown.Count == 0)
                return SelectedTab is { } single ? [single] : [];
            return [.. shown.Select(id => space.Tabs.FirstOrDefault(tab => tab.Id == id))
                .OfType<TabState>()];
        }
    }

    public double CurrentFirstColumnShare
    {
        get
        {
            var groupId = CurrentCards?.SplitGroupId;
            var shares = window?.SplitColumnShares.FirstOrDefault(x => x.GroupId == groupId)?.Shares;
            return shares is { Count: >= 2 } ? shares[0] : 0.5;
        }
    }

    /// Join two ordinary Open tabs in the active Space. Pinned/Saved tabs
    /// require CrestCore's copy semantics and are deliberately excluded from
    /// this initial two-pane Windows UI.
    public void SplitWith(Guid otherTabId)
    {
        var space = SelectedSpace ?? throw new InvalidOperationException("No active Space.");
        var selected = SelectedTab ?? throw new InvalidOperationException("No selected tab.");
        var other = space.Tabs.FirstOrDefault(tab => tab.Id == otherTabId);
        if (other is null || other.Id == selected.Id || other.IsStartPage ||
            selected.IsStartPage || other.Placement != TabPlacement.Current ||
            selected.Placement != TabPlacement.Current || selected.SplitGroupId is not null ||
            other.SplitGroupId is not null)
            throw new InvalidOperationException("Choose two separate Open tabs to split.");

        Apply(core.Send(new JoinSplit(workspaceId, windowId, space.Id,
            otherTabId, selected.Id, Index: null)));
    }

    public void CloseCurrentSplit()
    {
        var spaceId = ShownSpaceId ?? throw new InvalidOperationException("No active Space.");
        var groupId = CurrentCards?.SplitGroupId
            ?? throw new InvalidOperationException("The selected tab is not in a split.");
        Apply(core.Send(new DissolveSplit(workspaceId, spaceId, groupId)));
    }

    /// Persist the shares through CrestCore's window intent, not a parallel
    /// preference file. The managed core normalizes and validates the values.
    public void ResizeCurrentSplit(double firstShare)
    {
        if (CurrentCards is not { SplitGroupId: { } groupId, TabIds.Count: >= 2 })
            return;

        firstShare = Math.Clamp(firstShare, 0.2, 0.8);
        Apply(core.Send(new ResizeSplitColumns(windowId, groupId,
            [firstShare, 1.0 - firstShare])));
    }

    public void OpenTab(string address = "https://example.com/")
    {
        var spaceId = ShownSpaceId ?? throw new InvalidOperationException("No Space is open.");
        Apply(core.Send(new OpenTab(workspaceId, windowId, spaceId, Guid.NewGuid(),
            new TabContent(address, View: null, Title: null), TabPlacement.Current,
            AfterTabId: ShownTabId, Shows: true)));
    }

    /// Resolve the address bar input under the selected Space's real search
    /// preferences without changing its tab. WebView2 navigates the live page
    /// and reports the committed address when that navigation succeeds.
    public string? ResolveAddressInput(string input)
    {
        var spaceId = ShownSpaceId;
        return spaceId is { } id
            ? core.Query(new ResolveAddress(workspaceId, id, input)).Url
            : null;
    }

    /// The temporary WebView2 host reports the URL/title actually reached.
    /// CrestCore validates and persists the tab/history state; Windows never
    /// edits the session records directly.
    public bool ReportNavigation(Guid tabId, string url, string? title, bool newVisit)
    {
        var space = Spaces.FirstOrDefault(space => space.Tabs.Any(tab => tab.Id == tabId));
        if (space is null) return false;
        Apply(core.Send(new ReportHostedNavigation(workspaceId, space.Id, tabId, url, title, newVisit)));
        return true;
    }

    /// The Space's persistent browsing history, newest visit first.
    public IReadOnlyList<HistoryEntryState> CurrentHistory => SelectedSpace?.History ?? [];

    /// Crest's own address rules choose whether input is a URL or a search.
    /// This opens a tab when no live WebView2 is ready for in-tab navigation.
    public bool OpenAddress(string input)
    {
        var spaceId = ShownSpaceId ?? throw new InvalidOperationException("No Space is open.");
        var resolved = core.Query(new ResolveAddress(workspaceId, spaceId, input));
        if (resolved.Url is not { } address) return false;
        Apply(core.Send(new OpenAddress(workspaceId, windowId, spaceId, Guid.NewGuid(), address)));
        return true;
    }

    public TabState? SelectedTab => SelectedSpace?.Tabs.FirstOrDefault(tab => tab.Id == ShownTabId);

    public void PinTab(Guid spaceId, Guid tabId) =>
        Apply(core.Send(new TogglePin(workspaceId, spaceId, tabId)));

    /// Crest defines Saved as a durable tab section and Open as non-durable.
    /// This does not destroy the live page while merely moving its section.
    public void SaveTab(Guid spaceId, Guid tabId, bool saved) =>
        Apply(core.Send(new MoveTab(workspaceId, spaceId, tabId,
            saved ? TabPlacement.Saved : TabPlacement.Current,
            FolderId: null, BeforeTabId: null, LeavesSplit: false)));

    public void CloseTab(Guid spaceId, Guid tabId) =>
        Apply(core.Send(new CloseTab(workspaceId, windowId, spaceId, tabId)));

    public void RenameTab(Guid spaceId, Guid tabId, string? title) =>
        Apply(core.Send(new RenameTab(workspaceId, spaceId, tabId, title)));

    public void CreateFolder(Guid spaceId, Guid tabId, string title)
    {
        var tab = Spaces.First(space => space.Id == spaceId).Tabs.First(tab => tab.Id == tabId);
        // Pinned cannot contain folders; save it before filing.
        var placement = tab.Placement == TabPlacement.Pinned ? TabPlacement.Saved : tab.Placement;
        Apply(core.Send(new CreateFolder(workspaceId, spaceId, Guid.NewGuid(), placement,
            ParentId: null, Title: title, Color: null, Symbol: null,
            TabIds: [tabId], LeavesSplits: true)));
    }

    public void MoveTabToFolder(Guid spaceId, Guid tabId, Guid folderId)
    {
        var folder = Spaces.First(space => space.Id == spaceId).Folders.First(folder => folder.Id == folderId);
        Apply(core.Send(new MoveTab(workspaceId, spaceId, tabId, folder.Location,
            FolderId: folderId, BeforeTabId: null, LeavesSplit: true)));
    }

    public void MoveTabOutOfFolder(Guid spaceId, Guid tabId)
    {
        var tab = Spaces.First(space => space.Id == spaceId).Tabs.First(tab => tab.Id == tabId);
        Apply(core.Send(new MoveTab(workspaceId, spaceId, tabId, tab.Placement,
            FolderId: null, BeforeTabId: null, LeavesSplit: false)));
    }

    /// Drop a tab within a Space, including a section, a folder or before
    /// another tab. The core validates capacity, split rules and ordering.
    public void DropTab(Guid spaceId, Guid tabId, TabPlacement placement,
        Guid? folderId = null, Guid? beforeTabId = null) =>
        Apply(core.Send(new MoveTab(workspaceId, spaceId, tabId, placement,
            FolderId: folderId, BeforeTabId: beforeTabId, LeavesSplit: !placement.HoldsSplits)));

    public void CollapseFolder(Guid spaceId, Guid folderId, bool collapsed) =>
        Apply(core.Send(new CollapseFolder(workspaceId, spaceId, folderId, collapsed)));

    public void RenameFolder(Guid spaceId, Guid folderId, string title) =>
        Apply(core.Send(new RenameFolder(workspaceId, spaceId, folderId, title)));

    public void DeleteFolder(Guid spaceId, Guid folderId) =>
        Apply(core.Send(new DeleteFolder(workspaceId, spaceId, folderId)));

    #endregion

    #region Actions - Changes

    /// Apply the exact read-model updates CrestCore publishes. The core remains
    /// the only writer of browser state, including the identity/order of tabs.
    private void Apply(IReadOnlyList<Change> changes)
    {
        foreach (var change in changes)
        {
            switch (change)
            {
                case WorkspaceOpened opened:
                    workspaceId = opened.WorkspaceId;
                    session = opened.Session;
                    break;

                case SpacesChanged spaces when spaces.WorkspaceId == workspaceId && session is not null:
                    session = session with
                    {
                        Spaces = Merge(session.Spaces, spaces.Added, spaces.Removed, spaces.Order, space => space.Id)
                    };
                    break;

                case SpaceSettingsChanged settings when settings.WorkspaceId == workspaceId && session is not null:
                    session = session with
                    {
                        Spaces = [.. session.Spaces.Select(space => space.Id == settings.SpaceId
                            ? space with { Settings = settings.Settings } : space)]
                    };
                    break;

                case TabsChanged tabs when tabs.WorkspaceId == workspaceId && session is not null:
                    session = session with
                    {
                        Spaces = [.. session.Spaces.Select(space => space.Id == tabs.SpaceId
                            ? space with { Tabs = Merge(space.Tabs, tabs.Updated, tabs.Removed, tabs.Order, tab => tab.Id) }
                            : space)]
                    };
                    break;

                case FoldersChanged folders when folders.WorkspaceId == workspaceId && session is not null:
                    session = session with
                    {
                        Spaces = [.. session.Spaces.Select(space => space.Id == folders.SpaceId
                            ? space with { Folders = Merge(space.Folders, folders.Updated, folders.Removed, folders.Order, folder => folder.Id) }
                            : space)]
                    };
                    break;

                case ArchiveChanged archive when archive.WorkspaceId == workspaceId && session is not null:
                    session = session with
                    {
                        Spaces = [.. session.Spaces.Select(space => space.Id == archive.SpaceId
                            ? space with { ArchivedTabs = Merge(space.ArchivedTabs,
                                archive.Archived, archive.Removed, archive.Order, entry => entry.Tab.Id) }
                            : space)]
                    };
                    break;

                case HistoryChanged history when history.WorkspaceId == workspaceId && session is not null:
                    session = session with
                    {
                        Spaces = [.. session.Spaces.Select(space => space.Id == history.SpaceId
                            ? space with { History = MergeHistory(space.History, history) }
                            : space)]
                    };
                    break;

                case WindowChanged changed when changed.Window.Id == windowId:
                    window = changed.Window;
                    break;
            }
        }
        core.EndTurn();
    }

    /// HistoryChanged records updated visits at the front (unlike tab
    /// changes, which retain their old positions), then optionally orders all
    /// ids explicitly.
    private static IReadOnlyList<HistoryEntryState> MergeHistory(
        IReadOnlyList<HistoryEntryState> previous, HistoryChanged change)
    {
        var changedIds = change.Recorded.Select(entry => entry.Id).ToHashSet();
        var removedIds = change.Removed.ToHashSet();
        var result = change.Recorded
            .Concat(previous.Where(entry => !changedIds.Contains(entry.Id) && !removedIds.Contains(entry.Id)))
            .ToList();

        if (change.Order is not null)
        {
            var positions = change.Order.Select((id, position) => (id, position))
                .ToDictionary(item => item.id, item => item.position);
            result.Sort((left, right) => positions.GetValueOrDefault(left.Id, int.MaxValue)
                .CompareTo(positions.GetValueOrDefault(right.Id, int.MaxValue)));
        }
        return result;
    }

    private static IReadOnlyList<T> Merge<T>(IReadOnlyList<T> oldRows, IReadOnlyList<T> incoming,
        IReadOnlyList<Guid> removed, IReadOnlyList<Guid>? order, Func<T, Guid> identity) where T : class
    {
        var changes = incoming.ToDictionary(identity);
        var removedIds = removed.ToHashSet();
        List<T> rows = [];

        foreach (var previous in oldRows)
        {
            var id = identity(previous);
            if (removedIds.Contains(id)) continue;
            rows.Add(changes.Remove(id, out var updated) ? updated : previous);
        }
        rows.AddRange(changes.Values);

        if (order is not null)
        {
            var position = order.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
            rows.Sort((left, right) => position.GetValueOrDefault(identity(left), int.MaxValue)
                .CompareTo(position.GetValueOrDefault(identity(right), int.MaxValue)));
        }

        return rows;
    }

    #endregion

    #region Actions - Lifetime

    public void Dispose() => core.Dispose();

    #endregion
}
