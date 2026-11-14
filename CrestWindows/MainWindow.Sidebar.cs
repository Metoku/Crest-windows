using CrestCore.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CrestWindows;

/// Sidebar presentation and user actions. CrestCore owns tab placement,
/// folders, selection, and archives; this WinUI file only renders that state.
public sealed partial class MainWindow
{
    #region Types

    public sealed record SidebarEntry(Guid? TabId, Guid? FolderId,
        string Label, string Glyph, Thickness Indent)
    {
        public bool IsHeader => TabId is null && FolderId is null;
    }

    #endregion

    #region Actions - Sidebar presentation

    /// Sections and folder nesting are projected from CrestCore's placement,
    /// FolderId and ParentId records. There is no second Windows tab model.
    private static SidebarEntry[] SidebarEntries(SpaceState? space)
    {
        if (space is null) return [];

        List<SidebarEntry> entries = [];
        foreach (var placement in TabPlacement.All)
        {
            var tabs = space.Tabs.Where(tab => tab.Placement == placement).ToArray();
            var folders = space.Folders.Where(folder => folder.Location == placement).ToArray();
            if (tabs.Length == 0 && folders.Length == 0) continue;

            var icon = placement == TabPlacement.Pinned ? "\uE718"
                : placement == TabPlacement.Saved ? "\uE8A4" : "\uE8A5";
            entries.Add(new(null, null, placement.Title.ToUpperInvariant(), icon, new Thickness(2, 9, 0, 4)));

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
            entries.Add(new(null, folder.Id,
                (folder.IsCollapsed ? "  " : "  ") + folder.Title,
                folder.IsCollapsed ? "\uE76C" : "\uE8B7",
                new Thickness(12 + 14 * level, 0, 0, 0)));
            if (!folder.IsCollapsed)
                AppendFolderRows(entries, space, placement, folder.Id, level + 1);
        }

        foreach (var tab in space.Tabs.Where(tab =>
            tab.Placement == placement && tab.FolderId == parentId))
        {
            var name = string.IsNullOrWhiteSpace(tab.DisplayTitle) ? "New Tab" : tab.DisplayTitle;
            entries.Add(new(tab.Id, null, name, "\uE8A5",
                new Thickness(12 + 14 * level, 0, 0, 0)));
        }
    }

    #endregion

    #region Actions - Sidebar commands

    private void TabList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        SidebarEntry? entry = null;
        var target = e.OriginalSource as DependencyObject;
        while (target is not null)
        {
            if (target is FrameworkElement element && element.DataContext is SidebarEntry row)
            {
                entry = row;
                break;
            }
            if (ReferenceEquals(target, TabList)) break;
            target = VisualTreeHelper.GetParent(target);
        }
        entry ??= TabList.SelectedItem as SidebarEntry;

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
        menu.ShowAt(TabList);
        e.Handled = true;
    }

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
