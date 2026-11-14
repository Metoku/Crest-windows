using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace CrestWindows;

/// Native WinUI keyboard accelerators for the browser chrome. These route
/// only to CrestCore intents or the existing live WebView2 page. Input that
/// WebView2 consumes while it owns focus can differ from XAML accelerators;
/// the Windows acceptance test must cover both focus cases.
public sealed partial class MainWindow
{
    #region Actions - Shortcuts

    private void Shortcut_NewTab(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RunShortcut(() => core.OpenTab());
    }

    private void Shortcut_CloseTab(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (core.ShownSpaceId is not { } spaceId || core.SelectedTab is not { } tab)
            return;

        RunShortcut(() =>
        {
            core.CloseTab(spaceId, tab.Id);
            browserTabs.ReleaseTab(tab.Id);
        });
    }

    private void Shortcut_FocusAddress(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        UrlBox.Focus(FocusState.Programmatic);
        UrlBox.SelectAll();
    }

    private void Shortcut_Reload(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        browserTabs.ActiveView?.Reload();
    }

    private void Shortcut_NextTab(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectAdjacentTab(forward: true);
    }

    private void Shortcut_PreviousTab(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        SelectAdjacentTab(forward: false);
    }

    private void Shortcut_Downloads(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        DownloadsButton.Flyout?.ShowAt(DownloadsButton);
    }

    private void SelectAdjacentTab(bool forward)
    {
        var space = core.SelectedSpace;
        if (space is null || space.Tabs.Count < 2)
            return;

        var index = -1;
        for (var i = 0; i < space.Tabs.Count; i++)
        {
            if (space.Tabs[i].Id == core.ShownTabId)
            {
                index = i;
                break;
            }
        }

        var next = forward
            ? (index + 1) % space.Tabs.Count
            : (index + space.Tabs.Count - 1) % space.Tabs.Count;
        RunShortcut(() => core.ShowTab(space.Id, space.Tabs[next].Id));
    }

    private void RunShortcut(Action action)
    {
        try
        {
            action();
            Refresh();
        }
        catch (Exception error)
        {
            StatusText.Text = "Shortcut could not complete: " + error.Message;
        }
    }

    #endregion
}
