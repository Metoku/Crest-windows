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

        // First startup: install the normal Crest initial Space in SQLite.
        // Later startups: opening the stored workspace preserves the core's
        // Space, tab and browsing-profile identities.
        var database = Path.Combine(StorageDirectory, "session.sqlite");
        WasRestored = File.Exists(database) && new FileInfo(database).Length > 0;
        if (!WasRestored)
        {
            var seed = core.Query(new FirstInstallSession()) with { DisposableSeedMarker = null };
            Apply(core.Send(new AdoptLegacySession(
                new LegacySession(Core: null, WholeGraph: null, History: [], Journal: null), seed)));
        }

        Apply(core.Send(new OpenWorkspace(WorkspaceKind.Persistent, Seed: null)));
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

    public void OpenTab(string address = "https://example.com/")
    {
        var spaceId = ShownSpaceId ?? throw new InvalidOperationException("No Space is open.");
        Apply(core.Send(new OpenTab(workspaceId, windowId, spaceId, Guid.NewGuid(),
            new TabContent(address, View: null, Title: null), TabPlacement.Current,
            AfterTabId: ShownTabId, Shows: true)));
    }

    /// Crest's own address rules choose whether the input is a URL or search.
    /// OpenAddress uses the shown Start Page, or opens a new tab otherwise.
    /// Later, an engine adapter will report in-tab navigations to the core.
    public bool OpenAddress(string input)
    {
        var spaceId = ShownSpaceId ?? throw new InvalidOperationException("No Space is open.");
        var resolved = core.Query(new ResolveAddress(workspaceId, spaceId, input));
        if (resolved.Url is not { } address) return false;
        Apply(core.Send(new OpenAddress(workspaceId, windowId, spaceId, Guid.NewGuid(), address)));
        return true;
    }

    public TabState? SelectedTab => SelectedSpace?.Tabs.FirstOrDefault(tab => tab.Id == ShownTabId);

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

                case WindowChanged changed when changed.Window.Id == windowId:
                    window = changed.Window;
                    break;
            }
        }
        core.EndTurn();
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
