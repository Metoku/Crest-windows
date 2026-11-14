using CrestWindows;

using var workspace = new CoreWorkspace();
var personal = workspace.Spaces.Single(space => space.Settings.Name == "Personal");
var work = workspace.Spaces.Single(space => space.Settings.Name == "Work");

Require(workspace.ShownSpaceId == personal.Id, "Personal should be selected at launch.");
Require(workspace.Spaces.Count == 2, "Both template Spaces should exist.");
Require(personal.ProfileId != work.ProfileId,
    "CrestCore must give independent Spaces distinct browsing profiles.");

// A shared ProfileId (Sub-Spaces later) has the same directory, while
// independent Spaces cannot share WebView2 cookies or local storage.
var directories = new SpaceProfileDirectories(Path.Combine(Path.GetTempPath(), "crest-windows-profile-contract"));
Require(directories.For(personal.ProfileId) == directories.For(personal.ProfileId),
    "The same Crest browsing profile must always resolve to the same folder.");
Require(!string.Equals(directories.For(personal.ProfileId), directories.For(work.ProfileId),
        StringComparison.OrdinalIgnoreCase),
    "Independent Crest profiles must never share the same WebView2 data folder.");


Require(workspace.OpenAddress("example.org"), "First URL should be accepted.");
Require(workspace.SelectedTab?.Url == "https://example.org", "First URL should replace the Start Page.");

workspace.OpenTab("https://example.net/");
Require(workspace.SelectedTab?.Url == "https://example.net/", "New tab should belong to CrestCore.");
var previousTab = workspace.SelectedTab!.Id;

workspace.ShowSpace(work.Id);
Require(workspace.ShownSpaceId == work.Id, "Switching Space must update CrestCore's window state.");
Require(workspace.SelectedTab?.Url is null, "The Work Space should start with an empty Start Page.");

Require(workspace.OpenAddress("https://example.com/"), "Work Space URL should be accepted.");
Require(workspace.SelectedTab?.Url == "https://example.com/", "Work Space tab should be in CrestCore state.");

workspace.ShowTab(personal.Id, previousTab);
Require(workspace.ShownSpaceId == personal.Id, "Showing the Personal tab should restore its Space.");
Require(workspace.SelectedTab?.Id == previousTab, "The earlier Personal tab should remain in CrestCore.");

Console.WriteLine("CrestCore Windows smoke test passed: Spaces, tabs, selection and navigation.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
