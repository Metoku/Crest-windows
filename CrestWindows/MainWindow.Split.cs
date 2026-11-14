using CrestCore.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrestWindows;

/// Split View controls send CrestCore intents; they do not own a separate
/// group or a second browsing session. The host displays the WindowState
/// cards and returns the divider's final width to CrestCore.
public sealed partial class MainWindow
{
    #region Actions - Split View

    private void SplitButton_Click(object sender, RoutedEventArgs e)
    {
        if (core.CurrentCards?.SplitGroupId is not null)
        {
            ApplySplitAction(core.CloseCurrentSplit);
            return;
        }

        var current = core.SelectedTab;
        var space = core.SelectedSpace;
        if (current is null || space is null || current.IsStartPage)
            return;

        var menu = new MenuFlyout();
        foreach (var tab in space.Tabs.Where(tab =>
            tab.Id != current.Id && !tab.IsStartPage &&
            tab.Placement == TabPlacement.Current && tab.SplitGroupId is null))
        {
            var candidateId = tab.Id;
            var item = new MenuFlyoutItem
            {
                Text = tab.DisplayTitle,
                Icon = new FontIcon { Glyph = "\uE8A5" }
            };
            item.Click += (_, _) => ApplySplitAction(() => core.SplitWith(candidateId));
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "Open another tab to split",
                Icon = new FontIcon { Glyph = "\uE8A5" },
                IsEnabled = false
            });

        menu.ShowAt(SplitButton);
    }

    private void BrowserTabs_TabActivated(Guid tabId)
    {
        if (windowClosed || core.ShownTabId == tabId || core.ShownSpaceId is not { } spaceId)
            return;

        // Clicking a page makes that card the active navigation target.
        // CrestCore retains both split members in WindowState.Cards.
        ApplySplitAction(() => core.ShowTab(spaceId, tabId));
    }

    private void BrowserTabs_SplitRatioCommitted(double firstShare)
    {
        if (windowClosed || core.CurrentCards?.SplitGroupId is null)
            return;

        ApplySplitAction(() => core.ResizeCurrentSplit(firstShare));
    }

    private void ApplySplitAction(Action action)
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
}
