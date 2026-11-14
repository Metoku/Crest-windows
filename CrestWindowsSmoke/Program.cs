using CrestWindows;

var storage = Path.Combine(Path.GetTempPath(), "crest-windows-persistence-smoke", Guid.NewGuid().ToString("N"));
Guid personalSpaceId;
Guid workSpaceId;
Guid personalProfileId;
Guid workProfileId;
Guid personalTabId;
Guid workSplitGroupId;
try
{
    // Windows must remember the user's maximized choice independently of the
    // durable CrestCore tab/Space database. This is a standalone store test;
    // only desktop testing can verify the actual WinUI presenter transition.
    var presentationPath = Path.Combine(storage, "window-presentation.state");
    var presentation = new WindowPresentationStateStore(presentationPath);
    Require(presentation.ReadMaximized(), "First launch should default to maximized.");
    presentation.SaveMaximized(false);
    Require(new WindowPresentationStateStore(presentationPath).ReadMaximized() == false,
        "A restored window state must survive another application instance.");
    presentation.SaveMaximized(true);
    Require(new WindowPresentationStateStore(presentationPath).ReadMaximized(),
        "A maximized window state must survive another application instance.");

    var appearancePath = Path.Combine(storage, "window-appearance.state");
    var appearance = new WindowAppearanceStore(appearancePath);
    Require(appearance.Read() == WindowAppearanceStore.Appearance.System,
        "Windows appearance must follow the system on first launch.");
    appearance.Save(WindowAppearanceStore.Appearance.Light);
    Require(new WindowAppearanceStore(appearancePath).Read() ==
        WindowAppearanceStore.Appearance.Light,
        "Windows appearance preference must survive restart.");
    appearance.Save(WindowAppearanceStore.Appearance.Dark);
    Require(new WindowAppearanceStore(appearancePath).Read() ==
        WindowAppearanceStore.Appearance.Dark,
        "Windows appearance must preserve the chosen Dark mode.");

    using (var workspace = new CoreWorkspace(storage))
    {
        Require(!workspace.WasRestored, "A brand-new core must install an initial session.");
        var personal = workspace.Spaces.Single(space => space.Settings.Name == "Personal");
        var work = workspace.Spaces.Single(space => space.Settings.Name == "Work");

        personalSpaceId = personal.Id;
        workSpaceId = work.Id;
        personalProfileId = personal.ProfileId;
        workProfileId = work.ProfileId;

        Require(workspace.ShownSpaceId == personal.Id, "Personal should be selected at launch.");
        Require(workspace.Spaces.Count == 2, "Both template Spaces should exist.");
        Require(personal.ProfileId != work.ProfileId,
            "CrestCore must give independent Spaces distinct browsing profiles.");

        // Search preferences are CrestCore-owned and per-Space. Choosing
        // DuckDuckGo for Personal must not change Work's search engine.
        Require(workspace.SearchProviders.Any(provider => provider.Name == "duckDuckGo"),
            "CrestCore should publish its enabled search engines.");
        var duckDuckGo = workspace.SearchProviders.Single(provider => provider.Name == "duckDuckGo");
        workspace.SetSpaceSearchProvider(duckDuckGo);
        Require(workspace.SelectedSearchProvider?.Name == "duckDuckGo" && !workspace.SearchFollowsDefault,
            "Personal must use the chosen CrestCore search engine.");
        Require(workspace.ResolveAddressInput("crest windows search preference test")?
            .Contains("duckduckgo", StringComparison.OrdinalIgnoreCase) == true,
            "Address resolution must use Personal's new search engine.");
        workspace.ShowSpace(work.Id);
        Require(workspace.SelectedSearchProvider?.Name == "google" && workspace.SearchFollowsDefault,
            "Work must continue to follow the default search engine.");
        workspace.ShowSpace(personal.Id);

        var directories = new SpaceProfileDirectories(Path.Combine(storage, "Profiles"));
        Require(directories.For(personal.ProfileId) == directories.For(personal.ProfileId),
            "The same Crest browsing profile must always resolve to the same folder.");
        Require(!string.Equals(directories.For(personal.ProfileId), directories.For(work.ProfileId),
                StringComparison.OrdinalIgnoreCase),
            "Independent Crest profiles must never share the same WebView2 data folder.");

        Require(workspace.OpenAddress("example.org"), "First URL should be accepted.");
        Require(workspace.SelectedTab?.Url == "https://example.org",
            "First URL should replace the Start Page.");

        workspace.OpenTab("https://example.net/");
        Require(workspace.SelectedTab?.Url == "https://example.net/",
            "New tab should belong to CrestCore.");
        personalTabId = workspace.SelectedTab!.Id;

        // A hyperlink/redirect lands within the same WebView2 tab.
        Require(workspace.ReportNavigation(personalTabId, "https://example.net/docs", "Docs", newVisit: true),
            "Core must accept a registered tab's completed navigation.");
        Require(workspace.SelectedTab?.Url == "https://example.net/docs"
            && workspace.SelectedTab?.Title == "Docs", "Reported URL and title must update the tab.");
        Require(workspace.CurrentHistory.Count == 1
            && workspace.CurrentHistory[0].Url == "https://example.net/docs",
            "A completed navigation must create a Space history entry.");

        Require(workspace.ReportNavigation(personalTabId, "https://example.net/docs", "Docs updated", newVisit: false),
            "Document title changes must be handled by the core.");
        Require(workspace.CurrentHistory.Count == 1
            && workspace.CurrentHistory[0].Title == "Docs updated"
            && workspace.CurrentHistory[0].VisitCount == 1,
            "A title update must not add another visit.");

        Require(workspace.ReportNavigation(personalTabId, "https://example.net/guide", "Guide", newVisit: true),
            "A second hyperlink must update the same tab.");
        Require(workspace.CurrentHistory.Count == 2
            && workspace.CurrentHistory[0].Url == "https://example.net/guide",
            "History must keep newest visits first.");
        workspace.ReportNavigation(personalTabId, "https://example.net/docs", "Stale", newVisit: false);
        Require(workspace.SelectedTab?.Title == "Guide",
            "A late title from an old document must be ignored.");

        // The tab's section and folder are CrestCore state, never a Windows
        // sidebar-only preference. Verify pinning, saving and folder edits.
        workspace.PinTab(personal.Id, personalTabId);
        Require(workspace.SelectedTab?.Placement == CrestCore.Contracts.TabPlacement.Pinned,
            "Pin must move the tab into CrestCore's Pinned section.");
        workspace.PinTab(personal.Id, personalTabId);
        Require(workspace.SelectedTab?.Placement == CrestCore.Contracts.TabPlacement.Current,
            "Unpin must return the tab to Open.");
        workspace.SaveTab(personal.Id, personalTabId, saved: true);
        Require(workspace.SelectedTab?.Placement == CrestCore.Contracts.TabPlacement.Saved,
            "Save must make the tab durable.");

        workspace.RenameTab(personal.Id, personalTabId, "Reading list");
        Require(workspace.SelectedTab?.DisplayTitle == "Reading list",
            "A custom tab name must override the webpage title.");

        workspace.CreateFolder(personal.Id, personalTabId, "Bookmarks");
        var folder = workspace.Spaces.Single(space => space.Id == personal.Id).Folders
            .Single(folder => folder.Title == "Bookmarks");
        Require(workspace.SelectedTab?.FolderId == folder.Id,
            "CreateFolder should put the selected tab in the folder.");

        workspace.CollapseFolder(personal.Id, folder.Id, collapsed: true);
        Require(workspace.Spaces.Single(space => space.Id == personal.Id).Folders
            .Single(candidate => candidate.Id == folder.Id).IsCollapsed,
            "Folder collapse state must be owned by CrestCore.");
        workspace.CollapseFolder(personal.Id, folder.Id, collapsed: false);
        workspace.MoveTabOutOfFolder(personal.Id, personalTabId);
        Require(workspace.SelectedTab?.FolderId is null, "A tab should move back to the section.");
        workspace.MoveTabToFolder(personal.Id, personalTabId, folder.Id);
        Require(workspace.SelectedTab?.FolderId == folder.Id, "Moving the tab into a folder must persist.");

        workspace.OpenTab("https://example.net/temporary");
        var temporaryId = workspace.SelectedTab!.Id;

        // Drag/drop targets use the exact MoveTab contract, including the
        // pinned tile grid, folder rows, and ordering before another tab.
        workspace.DropTab(personal.Id, temporaryId, CrestCore.Contracts.TabPlacement.Pinned);
        Require(workspace.SelectedTab?.Placement == CrestCore.Contracts.TabPlacement.Pinned,
            "Dragging to favorites must move the tab into Pinned.");

        workspace.DropTab(personal.Id, temporaryId, CrestCore.Contracts.TabPlacement.Saved,
            folderId: folder.Id);
        Require(workspace.SelectedTab?.Placement == CrestCore.Contracts.TabPlacement.Saved
            && workspace.SelectedTab?.FolderId == folder.Id,
            "Dragging into a folder must retain its owning section.");

        var firstOpenId = workspace.Spaces.Single(space => space.Id == personal.Id).Tabs
            .First(tab => tab.Placement == CrestCore.Contracts.TabPlacement.Current).Id;
        workspace.DropTab(personal.Id, temporaryId, CrestCore.Contracts.TabPlacement.Current,
            beforeTabId: firstOpenId);
        var openOrder = workspace.Spaces.Single(space => space.Id == personal.Id).Tabs
            .Where(tab => tab.Placement == CrestCore.Contracts.TabPlacement.Current)
            .Select(tab => tab.Id).ToArray();
        Require(Array.IndexOf(openOrder, temporaryId) < Array.IndexOf(openOrder, firstOpenId),
            "Dragging before a tab must be reflected in CrestCore ordering.");
        Require(workspace.SelectedTab?.FolderId is null,
            "Dragging to an Open section header must remove the folder affiliation.");

        workspace.CloseTab(personal.Id, temporaryId);
        Require(workspace.Spaces.Single(space => space.Id == personal.Id).Tabs.All(tab => tab.Id != temporaryId),
            "Closing an Open tab must remove it from the active sidebar.");
        Require(workspace.Spaces.Single(space => space.Id == personal.Id).ArchivedTabs.Any(archive => archive.Tab.Id == temporaryId),
            "Closing an Open tab must preserve its archive record.");
        workspace.ShowTab(personal.Id, personalTabId);

        workspace.ShowSpace(work.Id);
        Require(workspace.ShownSpaceId == work.Id, "Switching Space must update CrestCore's window state.");
        Require(workspace.SelectedTab?.Url is null, "Work should start with an empty Start Page.");

        Require(workspace.OpenAddress("https://example.com/"), "Work URL should be accepted.");
        Require(workspace.SelectedTab?.Url == "https://example.com/",
            "Work tab should be in CrestCore state.");
        var workTabId = workspace.SelectedTab!.Id;
        workspace.ReportNavigation(workTabId, "https://example.com/product", "Work product", newVisit: true);
        Require(workspace.CurrentHistory.Count == 1 && workspace.CurrentHistory[0].Title == "Work product",
            "Work must record its own history without inheriting Personal history.");

        // Two independent Open tabs become a real CrestCore split. Both must
        // remain live members in WindowState.Cards, with resize shares owned
        // by CrestCore's saved window record.
        workspace.OpenTab("https://example.org/split");
        var secondWorkTabId = workspace.SelectedTab!.Id;
        workspace.SplitWith(workTabId);
        Require(workspace.CurrentCards is { SplitGroupId: not null, TabIds.Count: 2 },
            "Joining two Open tabs must publish a two-card split.");
        workSplitGroupId = workspace.CurrentCards!.SplitGroupId!.Value;
        Require(workspace.CurrentVisibleTabs.Select(tab => tab.Id).ToHashSet()
            .SetEquals(new[] { workTabId, secondWorkTabId }),
            "Both tabs must remain visible in CrestCore's current split.");

        workspace.ResizeCurrentSplit(0.35);
        Require(Math.Abs(workspace.CurrentFirstColumnShare - 0.35) < 0.001,
            "Resizing the split must update the CrestCore saved window.");

        workspace.ShowTab(work.Id, workTabId);
        Require(workspace.CurrentCards?.SplitGroupId == workSplitGroupId &&
            workspace.CurrentVisibleTabs.Count == 2,
            "Selecting either split member must keep both cards visible.");

        workspace.ShowTab(personal.Id, personalTabId);
        Require(workspace.ShownSpaceId == personal.Id,
            "Showing the Personal tab should restore its Space.");
        Require(workspace.SelectedTab?.Id == personalTabId,
            "The earlier Personal tab should remain in CrestCore.");
    }

    // A distinct CrestApp/process opening the same SQLite directory must
    // find the same identities, URLs, and the selected saved window/tab.
    using (var reopened = new CoreWorkspace(storage))
    {
        Require(reopened.WasRestored, "The second launch must open the durable session.");
        Require(reopened.SelectedSearchProvider?.Name == "duckDuckGo" &&
            !reopened.SearchFollowsDefault,
            "Personal's search preference must survive restarting CrestCore.");
        var personal = reopened.Spaces.Single(space => space.Settings.Name == "Personal");
        var work = reopened.Spaces.Single(space => space.Settings.Name == "Work");

        Require(personal.Id == personalSpaceId && work.Id == workSpaceId,
            "Space identities must persist across restarts.");
        Require(personal.ProfileId == personalProfileId && work.ProfileId == workProfileId,
            "WebView2 browsing-profile identities must persist across restarts.");
        Require(personal.Tabs.Any(tab => tab.Id == personalTabId
                && tab.Url == "https://example.net/guide" && tab.Title == "Guide"),
            "The latest reported Personal navigation and title must be restored from SQLite.");
        Require(work.Tabs.Any(tab => tab.Url == "https://example.com/product"),
            "The latest reported Work navigation must be restored from SQLite.");
        Require(personal.History.Count == 2
            && personal.History[0].Url == "https://example.net/guide"
            && personal.History[1].Title == "Docs updated",
            "Per-Space visited URLs and updated titles must be restored in order.");
        var restoredTab = personal.Tabs.Single(tab => tab.Id == personalTabId);
        Require(restoredTab.Placement == CrestCore.Contracts.TabPlacement.Saved
            && restoredTab.DisplayTitle == "Reading list",
            "Saved placement and custom title must survive restart.");
        var restoredFolder = personal.Folders.Single(folder => folder.Title == "Bookmarks");
        Require(restoredTab.FolderId == restoredFolder.Id && !restoredFolder.IsCollapsed,
            "Folder nesting and expansion must survive restart.");
        Require(personal.ArchivedTabs.Any(archive => archive.Tab.Url == "https://example.net/temporary"),
            "Dismissed Open tabs must persist in CrestCore's archive.");
        Require(work.History.Count == 1 && work.History[0].Url == "https://example.com/product",
            "Work history must persist independently.");
        Require(reopened.ShownSpaceId == personalSpaceId
            && reopened.SelectedTab?.Id == personalTabId,
            "The saved window must restore the selected Space and tab.");
        Require(reopened.Spaces.Count == 2,
            "A restored session must not create duplicate default Spaces.");

        // Confirm a fresh process restores group membership, both pages and
        // the window's divider share; dissolving returns to one active card.
        reopened.ShowSpace(workSpaceId);
        Require(reopened.CurrentCards?.SplitGroupId == workSplitGroupId &&
            reopened.CurrentVisibleTabs.Count == 2,
            "The split and both live page identities must survive restart.");
        Require(Math.Abs(reopened.CurrentFirstColumnShare - 0.35) < 0.001,
            "The saved divider share must survive restart.");
        reopened.CloseCurrentSplit();
        Require(reopened.CurrentCards?.SplitGroupId is null &&
            reopened.CurrentVisibleTabs.Count == 1,
            "Unsplit must restore a single-pane window without deleting tabs.");
        Require(reopened.Spaces.Single(space => space.Id == workSpaceId)
            .Tabs.Count >= 2,
            "Unsplit must preserve both tab records.");
    }

    Console.WriteLine("PASS: CrestCore session, split cards, saved column widths, navigation, folders and restart.");
}
finally
{
    try
    {
        if (Directory.Exists(storage)) Directory.Delete(storage, recursive: true);
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
