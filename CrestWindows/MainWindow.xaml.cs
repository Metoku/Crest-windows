using CrestCore.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace CrestWindows;

/// The first CrestCore-connected WinUI view. CrestCore owns the Space/tab
/// records and typed navigation commands. A single shared WebView2 still
/// renders the selected URL until the full Windows engine adapter is ready.
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
    private bool refreshing;

    #endregion

    #region Constructors

    public MainWindow()
    {
        InitializeComponent();

        core = new CoreWorkspace();
        Closed += (_, _) => core.Dispose();

        BrowserView.NavigationCompleted += BrowserView_NavigationCompleted;
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

    private void ShowSelectedPage()
    {
        var url = core.SelectedTab?.Url ?? HomeUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        UrlBox.Text = url;
        // Do not navigate again if this same WebView2 already shows the page.
        // A future engine binding will retain one live page per tab.
        if (BrowserView.Source is null || BrowserView.Source.AbsoluteUri != uri.AbsoluteUri)
            BrowserView.Source = uri;

        StatusText.Text = "CrestCore tabs & Spaces · WebView2 shared profile (not isolated)";
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

    private void BrowserView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender.Source is { } source)
            UrlBox.Text = source.AbsoluteUri;

        StatusText.Text = e.IsSuccess
            ? "CrestCore session preview · live-page synchronization and profile isolation are not yet implemented"
            : "The page could not be loaded.";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserView.CanGoBack)
            BrowserView.GoBack();
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserView.CanGoForward)
            BrowserView.GoForward();
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        BrowserView.Reload();
    }

    #endregion
}
