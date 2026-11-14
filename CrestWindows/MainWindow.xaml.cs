using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace CrestWindows;

// Proof-of-concept shell. Workspace names organize links in memory only.
// They deliberately do not pretend to provide Crest's cookie/profile isolation.
public sealed partial class MainWindow : Window
{
    private const string HomeUrl = "https://example.com/";
    private readonly Dictionary<string, ObservableCollection<PreviewTab>> _spaces = new()
    {
        ["Work"] = new(),
        ["Personal"] = new()
    };

    private bool _switching;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();

        _spaces["Work"].Add(new PreviewTab("Welcome", HomeUrl));
        _spaces["Personal"].Add(new PreviewTab("Welcome", HomeUrl));

        BrowserView.NavigationCompleted += BrowserView_NavigationCompleted;
        SpaceBox.Items.Add("Work");
        SpaceBox.Items.Add("Personal");
        _ready = true;
        SpaceBox.SelectedIndex = 0;
    }

    private void SpaceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || SpaceBox.SelectedItem is not string name || !_spaces.TryGetValue(name, out var tabs))
            return;
        _switching = true;
        TabList.ItemsSource = tabs;
        _switching = false;
        TabList.SelectedIndex = tabs.Count > 0 ? 0 : -1;
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switching || TabList.SelectedItem is not PreviewTab tab)
            return;

        UrlBox.Text = tab.Address;
        if (Uri.TryCreate(tab.Address, UriKind.Absolute, out var uri))
            BrowserView.Source = uri;
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        if (SpaceBox.SelectedItem is not string name)
            return;

        var tab = new PreviewTab("New tab", HomeUrl);
        _spaces[name].Add(tab);
        TabList.SelectedItem = tab;
        UrlBox.Focus(FocusState.Programmatic);
    }

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
        var text = input.Trim();
        if (text.Length == 0)
            return;

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            if (text.Contains(' ') || !text.Contains('.'))
                text = "https://www.bing.com/search?q=" + Uri.EscapeDataString(text);
            else
                text = "https://" + text;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            BrowserView.Source = uri;
            UrlBox.Text = uri.AbsoluteUri;
        }
        else
        {
            StatusText.Text = "Enter an HTTP(S) address or a search term.";
        }
    }

    private void BrowserView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var uri = sender.Source;
        if (uri is null)
            return;

        UrlBox.Text = uri.AbsoluteUri;
        if (TabList.SelectedItem is PreviewTab tab)
        {
            tab.Address = uri.AbsoluteUri;
            tab.Title = sender.CoreWebView2?.DocumentTitle is { Length: > 0 } title
                ? title : uri.Host;
        }

        StatusText.Text = e.IsSuccess
            ? "WebView2 preview • Workspaces do not isolate cookies yet"
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
}

public sealed class PreviewTab : INotifyPropertyChanged
{
    private string _title;

    public PreviewTab(string title, string address)
    {
        _title = title;
        Address = address;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    public string Address { get; set; }

    public override string ToString() => Title;
}
