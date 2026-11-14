using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
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

    #endregion

    #region Variables

    private readonly CoreWorkspace core;
    private readonly WebViewTabHost browserTabs;
    private bool refreshing;
    private bool browserHostLoaded;
    private bool windowClosed;

    #endregion

    #region Constructors

    public MainWindow()
    {
        InitializeComponent();

        core = new CoreWorkspace();
        browserTabs = new WebViewTabHost(BrowserHost,
            Path.Combine(Path.GetDirectoryName(core.StorageDirectory)!, "Profiles"));
        browserTabs.NavigationCompleted += BrowserTabs_NavigationCompleted;
        browserTabs.DocumentTitleChanged += BrowserTabs_DocumentTitleChanged;
        browserTabs.FaviconChanged += BrowserTabs_FaviconChanged;
        browserTabs.TabActivated += BrowserTabs_TabActivated;
        browserTabs.SplitRatioCommitted += BrowserTabs_SplitRatioCommitted;
        InitializeDownloads();
        BrowserHost.Loaded += (_, _) =>
        {
            browserHostLoaded = true;
            ShowSelectedPage();
        };

        Closed += (_, _) =>
        {
            windowClosed = true;
            browserTabs.Dispose();
            core.Dispose();
        };

        Refresh();
        RestoreCachedFavicons();
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

            var pinned = PinnedEntries(core.SelectedSpace);
            PinnedPanel.Visibility = pinned.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            PinnedList.ItemsSource = pinned;
            PinnedList.SelectedItem = pinned.FirstOrDefault(entry => entry.TabId == core.ShownTabId);

            var entries = SidebarEntries(core.SelectedSpace);
            TabList.ItemsSource = entries;
            TabList.SelectedItem = entries.FirstOrDefault(entry => entry.TabId == core.ShownTabId);

            var split = core.CurrentCards?.SplitGroupId is not null;
            SplitButton.Content = split ? "Unsplit" : "Split";
            SplitButton.IsEnabled = split || (core.SelectedTab is { } chosen &&
                chosen.Placement == CrestCore.Contracts.TabPlacement.Current && !chosen.IsStartPage &&
                core.SelectedSpace?.Tabs.Any(tab => tab.Id != chosen.Id && !tab.IsStartPage &&
                    tab.Placement == CrestCore.Contracts.TabPlacement.Current &&
                    tab.SplitGroupId is null) == true);
        }
        finally
        {
            refreshing = false;
        }

        RefreshDownloads();
        ShowSelectedPage();
    }

    /// Presentation-only favicon cache: restore an icon for every tab that
    /// still has the same persisted page address, without opening that tab's
    /// WebView2 or affecting its CrestCore session ownership.
    private async void RestoreCachedFavicons()
    {
        var tabs = core.Spaces.SelectMany(space => space.Tabs)
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Url))
            .Select(tab => (tab.Id, Address: tab.Url!)).ToArray();

        foreach (var (tabId, address) in tabs)
        {
            if (windowClosed) return;
            try
            {
                var image = await browserTabs.LoadFaviconAsync(tabId, address);
                if (windowClosed) return;
                if (image is not null && core.Spaces.SelectMany(space => space.Tabs)
                    .Any(tab => tab.Id == tabId && tab.Url == address))
                {
                    tabFavicons[tabId] = (address, image);
                }
            }
            catch (Exception error)
            {
                // A cache failure should not interrupt the user's browsing.
                System.Diagnostics.Debug.WriteLine("Favicon cache: " + error.Message);
            }
        }

        if (!windowClosed)
            Refresh();
    }

    private void BrowserTabs_FaviconChanged(Guid tabId, string address, BitmapImage? image)
    {
        if (windowClosed) return;

        if (image is null)
        {
            if (tabFavicons.TryGetValue(tabId, out var previous) && previous.Address == address)
                tabFavicons.Remove(tabId);
        }
        else
        {
            tabFavicons[tabId] = (address, image);
        }

        Refresh();
    }

    /// The saved CrestCore WindowState determines the cards in the browser.
    /// WebView2 owns the live pages, but never invents its own split membership.
    private async void ShowSelectedPage()
    {
        if (!browserHostLoaded || core.SelectedSpace is not { } space || core.SelectedTab is not { } tab)
            return;

        var selectedId = core.ShownTabId;
        var cards = core.CurrentVisibleTabs.Take(2).Select(visible =>
            new WebViewTabHost.TabViewport(visible.Id, space.ProfileId, visible.Url)).ToArray();

        try
        {
            await browserTabs.ShowCardsAsync(cards, selectedId, core.CurrentFirstColumnShare);
            if (core.ShownTabId != selectedId || windowClosed)
                return;

            UrlBox.Text = browserTabs.ActiveView?.Source?.AbsoluteUri ?? tab.Url ?? HomeUrl;
            StatusText.Text = cards.Length == 2
                ? "Split View • drag the divider to resize • widths saved in CrestCore"
                : core.WasRestored
                    ? "CrestCore session restored • Space profiles persisted (preview)"
                    : "New CrestCore session saved • Space profiles persisted (preview)";
        }
        catch (Exception error)
        {
            if (!windowClosed)
                StatusText.Text = "WebView2 could not initialize: " + error.Message;
        }
    }

    private void BrowserTabs_NavigationCompleted(Guid tabId, Uri? address, string? title, bool succeeded)
    {
        if (!succeeded || address is null)
        {
            if (core.ShownTabId == tabId)
                StatusText.Text = "The page could not be loaded.";
            return;
        }

        // Ignore the preview's empty Start Page and engine-internal
        // resources. CrestCore only records normal supported web URLs here.
        if (address.Scheme is not ("http" or "https"))
            return;

        try
        {
            if (core.ReportNavigation(tabId, address.AbsoluteUri, title, newVisit: true))
            {
                Refresh();
                if (core.ShownTabId == tabId)
                {
                    UrlBox.Text = address.AbsoluteUri;
                    StatusText.Text = "Page and visit saved to CrestCore";
                }
            }
        }
        catch (Exception error)
        {
            StatusText.Text = "Could not save navigation: " + error.Message;
        }
    }

    private void BrowserTabs_DocumentTitleChanged(Guid tabId, Uri? address, string title)
    {
        if (address?.Scheme is not ("http" or "https"))
            return;

        try
        {
            if (core.ReportNavigation(tabId, address.AbsoluteUri, title, newVisit: false))
                Refresh();
        }
        catch (Exception error)
        {
            StatusText.Text = "Could not save page title: " + error.Message;
        }
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

    private void PinnedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || PinnedList.SelectedItem is not SidebarEntry { TabId: { } tabId }
            || core.ShownSpaceId is not { } spaceId)
            return;

        try
        {
            core.ShowTab(spaceId, tabId);
            Refresh();
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || TabList.SelectedItem is not SidebarEntry selected
            || core.ShownSpaceId is not { } spaceId)
            return;

        try
        {
            if (selected.TabId is { } tabId)
                core.ShowTab(spaceId, tabId);
            else if (selected.FolderId is { } folderId
                && core.SelectedSpace?.Folders.FirstOrDefault(folder => folder.Id == folderId) is { } folder)
                core.CollapseFolder(spaceId, folderId, !folder.IsCollapsed);

            // Headers are informational. Folder clicks expand/collapse.
            // Restoring the selected tab prevents a section header from
            // becoming a fake active tab in the sidebar.
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
            var resolved = core.ResolveAddressInput(input);
            if (resolved is null)
            {
                StatusText.Text = "Enter an address or search phrase.";
                return;
            }

            // A typed URL replaces the active tab's live page. The
            // NavigationCompleted event records its eventual landing URL.
            // Without a ready page, fall back to Crest's OpenAddress intent.
            if (browserTabs.NavigateActive(resolved))
            {
                UrlBox.Text = resolved;
                return;
            }

            if (core.OpenAddress(input))
                Refresh();
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        var visits = core.CurrentHistory.Take(25).ToArray();

        if (visits.Length == 0)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "No recorded visits",
                Icon = new FontIcon { Glyph = "\uE8A4" },
                IsEnabled = false
            });
        }
        else
        {
            foreach (var visit in visits)
            {
                var address = visit.Url;
                var title = string.IsNullOrWhiteSpace(visit.Title) ? address : visit.Title;
                var item = new MenuFlyoutItem
                {
                    Text = title.Length > 90 ? title[..90] + "…" : title,
                    Icon = new FontIcon { Glyph = "\uE8A5" }
                };
                item.Click += (_, _) => Navigate(address);
                menu.Items.Add(item);
            }
        }

        menu.ShowAt(HistoryButton);
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
