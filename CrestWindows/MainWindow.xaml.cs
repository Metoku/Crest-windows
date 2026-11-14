using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CrestWindows;

/// Renders CrestCore's current Space and tab, while WebViewTabHost owns the
/// live engine views. The view never mutates CrestCore's browser records.
public sealed partial class MainWindow : Window
{
    #region Static Variables

    private const string HomeUrl = "https://example.com/";

    #endregion

    #region Types

    private sealed record SpaceRow(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record TabRow(Guid Id, string Title)
    {
        public override string ToString() => Title;
    }

    #endregion

    #region Variables

    private readonly CoreWorkspace core;
    private readonly WebViewTabHost browserTabs;
    private bool refreshing;
    private bool browserHostLoaded;

    #endregion

    #region Constructors

    public MainWindow()
    {
        InitializeComponent();

        core = new CoreWorkspace();
        browserTabs = new WebViewTabHost(BrowserHost,
            Path.Combine(Path.GetDirectoryName(core.StorageDirectory)!, "Profiles"));
        browserTabs.NavigationCompleted += BrowserTabs_NavigationCompleted;
        BrowserHost.Loaded += (_, _) =>
        {
            browserHostLoaded = true;
            ShowSelectedPage();
        };

        Closed += (_, _) =>
        {
            browserTabs.Dispose();
            core.Dispose();
        };

        Refresh();
    }

    #endregion

    #region Actions - Core presentation

    private void Refresh()
    {
        refreshing = true;
        try
        {
            SpaceBox.Items.Clear();
            foreach (var space in core.Spaces)
                SpaceBox.Items.Add(new SpaceRow(space.Id, space.Settings.Name));

            SpaceBox.SelectedItem = SpaceBox.Items.OfType<SpaceRow>()
                .FirstOrDefault(space => space.Id == core.ShownSpaceId);

            TabList.ItemsSource = core.SelectedSpace?.Tabs
                .Select(tab => new TabRow(tab.Id, tab.DisplayTitle)).ToArray()
                ?? [];

            TabList.SelectedItem = (TabList.ItemsSource as TabRow[])?
                .FirstOrDefault(tab => tab.Id == core.ShownTabId);
        }
        finally
        {
            refreshing = false;
        }

        ShowSelectedPage();
    }

    /// A tab gets its own live WebView2 instance, so switching between tabs
    /// doesn't reset navigation, forms, scroll position or page JavaScript.
    private async void ShowSelectedPage()
    {
        if (!browserHostLoaded || core.SelectedSpace is not { } space || core.SelectedTab is not { } tab)
            return;

        try
        {
            await browserTabs.ShowAsync(tab.Id, space.ProfileId, tab.Url);
            if (core.ShownTabId != tab.Id)
                return;

            UrlBox.Text = browserTabs.ActiveView?.Source?.AbsoluteUri ?? tab.Url ?? HomeUrl;
            StatusText.Text = core.WasRestored
                ? "CrestCore session restored • Space profiles persisted (preview)"
                : "New CrestCore session saved • Space profiles persisted (preview)";
        }
        catch (Exception error)
        {
            StatusText.Text = "WebView2 could not initialize: " + error.Message;
        }
    }

    private void BrowserTabs_NavigationCompleted(Guid tabId, Uri? address, bool succeeded)
    {
        if (core.ShownTabId != tabId)
            return;

        if (address is not null)
            UrlBox.Text = address.AbsoluteUri;

        StatusText.Text = succeeded
            ? "Tab loaded • live URL/history reporting to CrestCore still pending"
            : "The page could not be loaded.";
    }

    #endregion

    #region Actions - Spaces and tabs

    private void SpaceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || SpaceBox.SelectedItem is not SpaceRow selected)
            return;

        try
        {
            core.ShowSpace(selected.Id);
            Refresh();
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || TabList.SelectedItem is not TabRow selected || core.ShownSpaceId is not { } spaceId)
            return;

        try
        {
            core.ShowTab(spaceId, selected.Id);
            Refresh();
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            core.OpenTab();
            Refresh();
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    #endregion

    #region Actions - Navigation

    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(UrlBox.Text);

    private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        Navigate(UrlBox.Text);
        e.Handled = true;
    }

    private void Navigate(string input)
    {
        try
        {
            if (!core.OpenAddress(input))
            {
                StatusText.Text = "Enter an address or search phrase.";
                return;
            }

            Refresh();
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (browserTabs.ActiveView is { CanGoBack: true } active)
            active.GoBack();
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (browserTabs.ActiveView is { CanGoForward: true } active)
            active.GoForward();
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        browserTabs.ActiveView?.Reload();
    }

    #endregion
}
