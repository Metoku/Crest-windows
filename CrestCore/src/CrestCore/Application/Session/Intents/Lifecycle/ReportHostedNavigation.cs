using CrestCore.Application;
using CrestCore.Domain;

namespace CrestCore.Contracts;

/// Records a navigation or a later title update from a platform-hosted
/// WebView that has no registered Crest engine yet. This bridge exists for
/// early platform ports. A fully integrated engine should use the existing
/// NavigationFinished and PageStateChanged reports instead.
///
/// The report is accepted only for a known tab in a readable Space, uses the
/// same tab-appearance and history rules as a page report, and is saved by
/// CrestCore as part of its normal session revision.
/// A title-only update may not change the tab's address.
public sealed record ReportHostedNavigation(
    Guid WorkspaceId, Guid SpaceId, Guid TabId, string Url, string? Title, bool NewVisit)
    : SessionIntent(WorkspaceId) {
    #region Actions - Session

    internal override SessionEdit? Edit(NativeSessionAuthority workspace, SessionTurn turn) {
        AddressResolution.ValidateUrl(Url);

        var space = workspace.Editable(turn.Basis, SpaceId);
        var index = -1;
        for (var i = 0; i < space.Tabs.Count; i++)
            if (space.Tabs[i].Id == TabId) {
                index = i;
                break;
            }
        if (index < 0) throw new Rejected(new UnknownTab(TabId));

        var previous = space.Tabs[index];
        if (!NewVisit && previous.Url != Url)
            return new(turn.Basis, SyncStaging.PageReport); // Late title from an older document.

        var address = new Uri(Url);
        var givenTitle = string.IsNullOrWhiteSpace(Title)
            ? null : Title.Trim()[..Math.Min(Title.Trim().Length, BrowserTab.MaximumTitleLength)];
        var title = givenTitle ?? (address.Host.Length > 0 ? address.Host : Url);

        var tab = BrowserTab.Restore(previous);
        tab.ObserveAppearance(NewVisit ? Url : null, title);

        var editedSpace = tab.State == previous
            ? space
            : space with { Tabs = [.. space.Tabs.Select((old, position) => position == index ? tab.State : old)] };

        var owner = turn.Basis.RecordsOwner(space);
        var recorded = NewVisit
            ? HistoryEntryState.Visit(owner.History, Url, title, turn.Now, turn.Ids.Next())
            : (givenTitle is null ? null : HistoryEntryState.Retitle(owner.History, Url, givenTitle));

        if (recorded is null)
            return new(turn.Basis.Replacing(editedSpace), SyncStaging.PageReport);

        if (owner.Id == space.Id)
            return new(turn.Basis.Replacing(editedSpace with { History = recorded }), SyncStaging.PageReport);

        return new(turn.Basis.Replacing([editedSpace, owner with { History = recorded }]), SyncStaging.PageReport);
    }

    #endregion
}
