using CrestWindows;

var storage = Path.Combine(Path.GetTempPath(), "crest-windows-persistence-smoke", Guid.NewGuid().ToString("N"));
Guid personalSpaceId;
Guid workSpaceId;
Guid personalProfileId;
Guid workProfileId;
Guid personalTabId;
try
{
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
        Require(work.History.Count == 1 && work.History[0].Url == "https://example.com/product",
            "Work history must persist independently.");
        Require(reopened.ShownSpaceId == personalSpaceId
            && reopened.SelectedTab?.Id == personalTabId,
            "The saved window must restore the selected Space and tab.");
        Require(reopened.Spaces.Count == 2,
            "A restored session must not create duplicate default Spaces.");
    }

    Console.WriteLine("PASS: CrestCore Windows navigation, title, isolated history, persistence, and saved window selection.");
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
