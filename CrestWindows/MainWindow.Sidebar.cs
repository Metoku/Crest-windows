using CrestCore.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace CrestWindows;

/// Sidebar presentation and user actions. CrestCore owns tab placement,
/// folders, selection, and archives; this WinUI file only renders that state.
public sealed partial class MainWindow
{
    #region Types

    public sealed record SidebarEntry(Guid? TabId, Guid? FolderId,
        TabPlacement Placement, string Label, string Glyph, Thickness Indent)
    {
        public bool IsHeader => TabId is null && FolderId is null;

        // Until the Windows adapter reports proper favicon images, pinned
        // favorites show a recognisable initial and their full title on hover.
        public string Initial => string.IsNullOrWhiteSpace(Label)
            ? "•" : char.ToUpperInvariant(Label.TrimStart()[0]).ToString();
    }

    #endregion

    #region Actions - Sidebar presentation

    /// The favorites grid is a projection of CrestCore's pinned tabs,
    /// preserving its current ordering and identity.
    private static SidebarEntry[] PinnedEntries(SpaceState? space) =>
        space is null ? [] :
        [.. space.Tabs.Where(tab => tab.Placement == TabPlacement.Pinned)
            .Select(tab => new SidebarEntry(tab.Id, null, TabPlacement.Pinned,
                string.IsNullOrWhiteSpace(tab.DisplayTitle) ? "New Tab" : tab.DisplayTitle,
                "\uE718", new Thickness(0)))];

    /// Saved/Open sections and folder nesting come from the same CrestCore
    /// read model as the pinned grid, without duplicating tab ownership.
    private static SidebarEntry[] SidebarEntries(SpaceState? space)
    {
        if (space is null) return [];

        List<SidebarEntry> entries = [];
        foreach (var placement in TabPlacement.All.Where(section => section != TabPlacement.Pinned))
        {
            var tabs = space.Tabs.Where(tab => tab.Placement == placement).ToArray();
            var folders = space.Folders.Where(folder => folder.Location == placement).ToArray();
            if (tabs.Length == 0 && folders.Length == 0) continue;

            var icon = placement == TabPlacement.Saved ? "\uE8A4" : "\uE8A5";
            entries.Add(new(null, null, placement, placement.Title.ToUpperInvariant(),
                icon, new Thickness(2, 9, 0, 4)));
            AppendFolderRows(entries, space, placement, parentId: null, level: 0);
        }
        return [.. entries];
    }

    private static void AppendFolderRows(List<SidebarEntry> entries, SpaceState space,
        TabPlacement placement, Guid? parentId, int level)
    {
        // CrestCore defines the folders' hierarchy. This initial Windows
        // renderer groups folders first and their tabs afterward.
        foreach (var folder in space.Folders.Where(folder =>
            folder.Location == placement && folder.ParentId == parentId))
        {
            entries.Add(new(null, folder.Id, placement,
                folder.Title,
                folder.IsCollapsed ? "\uE76C" : "\uE8B7",
                new Thickness(12 + 14 * level, 0, 0, 0)));
            if (!folder.IsCollapsed)
                AppendFolderRows(entries, space, placement, folder.Id, level + 1);
        }

        foreach (var tab in space.Tabs.Where(tab =>
            tab.Placement == placement && tab.FolderId == parentId))
        {
            var name = string.IsNullOrWhiteSpace(tab.DisplayTitle) ? "New Tab" : tab.DisplayTitle;
            entries.Add(new(tab.Id, null, placement, name, "\uE8A5",
                new Thickness(12 + 14 * level, 0, 0, 0)));
        }
    }

    #endregion

    #region Actions - Sidebar commands

    private static SidebarEntry? FindSidebarEntry(DependencyObject? source, DependencyObject host)
    {
        while (source is not null)
        {
            if (source is FrameworkElement element && element.DataContext is SidebarEntry row)
                return row;
            if (ReferenceEquals(source, host)) break;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void TabList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var host = sender as FrameworkElement ?? TabList;
        var entry = FindSidebarEntry(e.OriginalSource as DependencyObject, host)
            ?? (sender is GridView ? PinnedList.SelectedItem : TabList.SelectedItem) as SidebarEntry;

        if (entry is null || entry.IsHeader || core.ShownSpaceId is not { } spaceId) return;

        var menu = new MenuFlyout();
        if (entry.TabId is { } tabId
            && core.SelectedSpace?.Tabs.FirstOrDefault(tab => tab.Id == tabId) is { } tab)
        {
            menu.Items.Add(Item(tab.Placement == TabPlacement.Pinned ? "Unpin" : "Pin",
                "\uE718", () => SidebarAction(() => core.PinTab(spaceId, tabId))));

            menu.Items.Add(Item(tab.Placement == TabPlacement.Saved ? "Move to Open" : "Save tab",
                "\uE8A4", () => SidebarAction(() =>
                    core.SaveTab(spaceId, tabId, tab.Placement != TabPlacement.Saved))));

            menu.Items.Add(Item("Rename tab", "\uE70F",
                () => _ = RenameTabAsync(spaceId, tabId, tab.DisplayTitle)));

            menu.Items.Add(Item("New folder with tab", "\uE8B7",
                () => _ = NewFolderForAsync(spaceId, tabId)));

            foreach (var folder in core.SelectedSpace!.Folders.Where(folder => folder.Id != tab.FolderId))
            {
                var destinationId = folder.Id;
                var title = folder.Title;
                menu.Items.Add(Item("Move to " + title, "\uE8B7",
                    () => SidebarAction(() => core.MoveTabToFolder(spaceId, tabId, destinationId))));
            }

            if (tab.FolderId is not null)
                menu.Items.Add(Item("Remove from folder", "\uE8A5",
                    () => SidebarAction(() => core.MoveTabOutOfFolder(spaceId, tabId))));

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item(tab.Placement.IsDurable ? "Close page" : "Close tab",
                "\uE8BB", () => SidebarAction(() =>
                {
                    core.CloseTab(spaceId, tabId);
                    browserTabs.ReleaseTab(tabId);
                })));
        }
        else if (entry.FolderId is { } folderId
            && core.SelectedSpace?.Folders.FirstOrDefault(folder => folder.Id == folderId) is { } folder)
        {
            menu.Items.Add(Item(folder.IsCollapsed ? "Expand folder" : "Collapse folder", "\uE8B7",
                () => SidebarAction(() => core.CollapseFolder(spaceId, folderId, !folder.IsCollapsed))));
            menu.Items.Add(Item("Rename folder", "\uE70F",
                () => _ = RenameFolderAsync(spaceId, folderId, folder.Title)));
            menu.Items.Add(Item("Remove folder, keep tabs", "\uE8BB",
                () => SidebarAction(() => core.DeleteFolder(spaceId, folderId))));
        }

        if (menu.Items.Count == 0) return;
        menu.ShowAt(host);
        e.Handled = true;
    }

    #region Actions - Drag and drop

    private Guid? draggingTabId;
    private Guid? draggingSpaceId;

    /// Only a locally initiated tab drag is accepted. External text/URLs are
    /// never interpreted as tab identities or shared as browser addresses.
    private void Sidebar_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is not SidebarEntry { TabId: { } tabId }
            || core.ShownSpaceId is not { } spaceId)
        {
            e.Cancel = true;
            return;
        }

        draggingTabId = tabId;
        draggingSpaceId = spaceId;
        e.Data.SetText(tabId.ToString("D"));
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void Sidebar_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        draggingTabId = null;
        draggingSpaceId = null;
    }

    private void Sidebar_DragOver(object sender, DragEventArgs e)
    {
        if (draggingTabId is { } tabId && draggingSpaceId == core.ShownSpaceId
            && core.SelectedSpace?.Tabs.Any(tab => tab.Id == tabId) == true)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.Handled = true;
        }
    }

    private void Sidebar_Drop(object sender, DragEventArgs e)
    {
        if (draggingTabId is not { } tabId || draggingSpaceId is not { } spaceId
            || core.ShownSpaceId != spaceId || core.SelectedSpace is not { } space)
            return;

        // Determine the destination from the item under the pointer.
        // Blank space in the ordinary list means the Open top-level.
        var host = sender as FrameworkElement ?? TabList;
        var row = FindSidebarEntry(e.OriginalSource as DependencyObject, host);
        var placement = sender is GridView ? TabPlacement.Pinned
            : row?.Placement ?? TabPlacement.Current;
        Guid? folderId = null;
        Guid? beforeId = null;

        if (sender is not GridView)
        {
            if (row?.FolderId is { } folder && space.Folders.Any(item => item.Id == folder))
                folderId = folder;
            else if (row?.TabId is { } target && target != tabId
                && space.Tabs.FirstOrDefault(item => item.Id == target) is { } targetTab)
            {
                folderId = targetTab.FolderId;
                beforeId = target;
            }
        }
        else if (row?.TabId is { } target && target != tabId)
        {
            beforeId = target;
        }

        // This is a GUID-only in-process move. The core handles folder
        // validation, pinned capacity, ordering, and split constraints.
        SidebarAction(() => core.DropTab(spaceId, tabId, placement, folderId, beforeId));
        draggingTabId = null;
        draggingSpaceId = null;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;
    }

    #endregion

    private static MenuFlyoutItem Item(string label, string glyph, Action action)
    {
        var item = new MenuFlyoutItem
        {
            Text = label,
            Icon = new FontIcon { Glyph = glyph }
        };
        item.Click += (_, _) => action();
        return item;
    }

    private void SidebarAction(Action action)
    {
        try
        {
            action();
            Refresh();
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    #endregion

    #region Actions - Folder and rename dialogs

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (core.ShownSpaceId is not { } spaceId || core.SelectedTab is not { } tab)
        {
            StatusText.Text = "Select a tab to place in a new folder.";
            return;
        }
        await NewFolderForAsync(spaceId, tab.Id);
    }

    private async Task NewFolderForAsync(Guid spaceId, Guid tabId)
    {
        var title = await RequestNameAsync("New Folder", "New Folder");
        if (string.IsNullOrWhiteSpace(title)) return;
        SidebarAction(() => core.CreateFolder(spaceId, tabId, title));
    }

    private async Task RenameTabAsync(Guid spaceId, Guid tabId, string current)
    {
        var title = await RequestNameAsync("Rename Tab", current);
        if (title is null) return;
        SidebarAction(() => core.RenameTab(spaceId, tabId, title));
    }

    private async Task RenameFolderAsync(Guid spaceId, Guid folderId, string current)
    {
        var title = await RequestNameAsync("Rename Folder", current);
        if (string.IsNullOrWhiteSpace(title)) return;
        SidebarAction(() => core.RenameFolder(spaceId, folderId, title));
    }

    private async Task<string?> RequestNameAsync(string caption, string current)
    {
        var field = new TextBox { Text = current, MaxLength = 120 };
        var dialog = new ContentDialog
        {
            XamlRoot = BrowserHost.XamlRoot,
            Title = caption,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = field
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? field.Text.Trim() : null;
    }

    #endregion
}
