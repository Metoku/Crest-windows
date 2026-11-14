using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Microsoft.Web.WebView2.Core;

namespace CrestWindows;

/// A preview engine-view adapter. Each tab keeps its own live WebView2,
/// and each distinct CrestCore ProfileId gets a stable data directory. The
/// browser engine-to-core navigation bridge is still under development.
public sealed class WebViewTabHost : IDisposable
{
    #region Static Variables

    private const string PreviewStartPage = "https://example.com/";

    #endregion

    #region Types

    public sealed record TabViewport(Guid TabId, Guid ProfileId, string? Address);

    private sealed class TabPage(Guid tabId, Guid profileId, string? address)
    {
        public Guid TabId { get; } = tabId;
        public int FaviconRevision { get; set; }
        public Guid ProfileId { get; } = profileId;
        public string? CoreAddress { get; set; } = address;
        public WebView2 View { get; } = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        public Task? Initialization { get; set; }
        public bool IsReady { get; set; }
        public string? CompletedAddress { get; set; }
        public string? CompletedTitle { get; set; }
    }

    #endregion

    #region Variables

    private readonly Grid host;
    private readonly SpaceProfileDirectories directories;
    private readonly TabFaviconStore faviconStore;
    private readonly Dictionary<Guid, TabPage> pages = [];
    private readonly Dictionary<Guid, Task<CoreWebView2Environment>> environments = [];
    private Guid? activeTabId;
    private Border? divider;
    private double splitShare = 0.5;
    private bool draggingDivider;
    private bool showingSplit;
    private bool disposed;

    public event Action<Guid>? TabActivated;
    public event Action<double>? SplitRatioCommitted;
    public event Action<Guid, Uri?, string?, bool>? NavigationCompleted;
    public event Action<Guid, Uri?, string>? DocumentTitleChanged;
    public event Action<Guid, string, BitmapImage?>? FaviconChanged;
    /// Download operations stay attached to the exact tab they originated
    /// from, even after another page or Space becomes active.
    public event Action<Guid, CoreWebView2DownloadOperation>? DownloadStarted;

    public WebView2? ActiveView =>
        activeTabId is { } id && pages.TryGetValue(id, out var page) && page.IsReady ? page.View : null;

    #endregion

    #region Constructors

    public WebViewTabHost(Grid host, string profileRoot)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));

        // ProfileId comes from CrestCore's durable session. Reusing the same
        // id on restart restores only its own cookies and site data.
        directories = new SpaceProfileDirectories(profileRoot);
        faviconStore = new TabFaviconStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profileRoot))!, "Favicons"));
    }

    #endregion

    #region Actions - Tabs

    /// Render the exact cards CrestCore publishes for the active window.
    /// A single card occupies the full host; two cards keep independent live
    /// WebView2 pages on either side of a draggable divider. The core owns
    /// split membership and saved width shares.
    public async Task ShowCardsAsync(IReadOnlyList<TabViewport> cards,
        Guid? selectedTabId, double firstColumnShare)
    {
        if (disposed) return;
        if (cards.Count > 2)
            throw new NotSupportedException("This Windows preview currently supports two split panes.");

        var shown = cards.Select(card => card.TabId).ToHashSet();
        activeTabId = selectedTabId is { } id && shown.Contains(id)
            ? selectedTabId : cards.FirstOrDefault()?.TabId;

        foreach (var card in cards)
        {
            if (!pages.TryGetValue(card.TabId, out var page))
            {
                page = new TabPage(card.TabId, card.ProfileId, card.Address);
                var capturedTabId = card.TabId;
                page.View.NavigationCompleted += (sender, args) =>
                {
                    if (disposed) return;
                    var source = sender.Source;
                    var title = sender.CoreWebView2?.DocumentTitle;
                    if (args.IsSuccess && source is not null)
                    {
                        page.CoreAddress = source.AbsoluteUri;
                        page.CompletedAddress = source.AbsoluteUri;
                        page.CompletedTitle = title;
                    }

                    NavigationCompleted?.Invoke(capturedTabId, source, title, args.IsSuccess);
                };
                page.View.GotFocus += (_, _) =>
                {
                    if (disposed || activeTabId == capturedTabId ||
                        page.View.Visibility != Visibility.Visible)
                        return;
                    activeTabId = capturedTabId;
                    TabActivated?.Invoke(capturedTabId);
                };
                pages.Add(card.TabId, page);
                host.Children.Add(page.View);
            }
            else if (page.ProfileId != card.ProfileId)
            {
                throw new InvalidOperationException("CrestCore changed a tab's profile without an engine rehost.");
            }
        }

        SetColumns(cards.Count == 2, firstColumnShare);
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var page = pages[card.TabId];
            var changedAddress = !string.Equals(page.CoreAddress, card.Address, StringComparison.Ordinal);
            page.CoreAddress = card.Address;
            Grid.SetColumn(page.View, cards.Count == 2 && i == 1 ? 2 : 0);
            page.View.Visibility = Visibility.Visible;
            if (page.Initialization is not null && page.IsReady && changedAddress)
                Navigate(page);
        }

        foreach (var (tabId, entry) in pages)
        {
            if (!shown.Contains(tabId))
                entry.View.Visibility = Visibility.Collapsed;
        }

        var tasks = new List<Task>(cards.Count);
        foreach (var card in cards)
        {
            var page = pages[card.TabId];
            page.Initialization ??= InitializeAsync(page);
            tasks.Add(page.Initialization);
        }
        await Task.WhenAll(tasks);
    }

    #endregion

    #region Actions - Split layout

    /// The pointer changes only transient layout until the drag ends. Only
    /// the release sends a typed resize intent back to CrestCore.
    private void SetColumns(bool split, double ratio)
    {
        splitShare = Math.Clamp(ratio, 0.2, 0.8);
        showingSplit = split;
        if (!split)
        {
            draggingDivider = false;
            if (divider is not null)
                host.Children.Remove(divider);
            host.ColumnDefinitions.Clear();
            host.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            return;
        }

        if (host.ColumnDefinitions.Count != 3)
        {
            host.ColumnDefinitions.Clear();
            host.ColumnDefinitions.Add(new ColumnDefinition());
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            host.ColumnDefinitions.Add(new ColumnDefinition());
        }

        SetSplitShare(splitShare);

        if (divider is null)
        {
            var lineBrush = Application.Current.Resources.TryGetValue(
                "DividerStrokeColorDefaultBrush", out var resource)
                ? resource as Brush : null;

            divider = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Child = new Border
                {
                    Width = 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Background = lineBrush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray)
                }
            };
            divider.PointerPressed += Divider_PointerPressed;
            divider.PointerMoved += Divider_PointerMoved;
            divider.PointerReleased += Divider_PointerReleased;
            divider.PointerCanceled += (_, _) => draggingDivider = false;
        }

        Grid.SetColumn(divider, 1);
        if (!host.Children.Contains(divider))
            host.Children.Add(divider);
    }

    private void SetSplitShare(double ratio)
    {
        splitShare = Math.Clamp(ratio, 0.2, 0.8);
        if (host.ColumnDefinitions.Count == 3)
        {
            host.ColumnDefinitions[0].Width = new GridLength(splitShare, GridUnitType.Star);
            host.ColumnDefinitions[2].Width = new GridLength(1 - splitShare, GridUnitType.Star);
        }
    }

    private void Divider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!showingSplit || divider is null ||
            !e.GetCurrentPoint(divider).Properties.IsLeftButtonPressed)
            return;
        draggingDivider = divider.CapturePointer(e.Pointer);
        e.Handled = draggingDivider;
    }

    private void Divider_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!draggingDivider || !showingSplit || host.ActualWidth <= 48)
            return;
        SetSplitShare(e.GetCurrentPoint(host).Position.X / (host.ActualWidth - 8));
        e.Handled = true;
    }

    private void Divider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!draggingDivider || divider is null)
            return;
        draggingDivider = false;
        divider.ReleasePointerCapture(e.Pointer);
        SplitRatioCommitted?.Invoke(splitShare);
        e.Handled = true;
    }

    #endregion

    #region Actions - Tabs

    /// A favicon for a tab restored by CrestCore can be shown before its
    /// WebView2 is loaded. The address must match exactly, so a changed site
    /// never inherits an unrelated old icon.
    public Task<BitmapImage?> LoadFaviconAsync(Guid tabId, string? address) =>
        faviconStore.ReadAsync(tabId, address);

    /// A dismissed tab's live engine must not stay attached after CrestCore
    /// archives the tab or puts away its saved/pinned page. Future selection
    /// of a retained durable tab creates a fresh engine for its saved URL.
    public void ReleaseTab(Guid tabId)
    {
        if (!pages.Remove(tabId, out var page)) return;
        if (activeTabId == tabId) activeTabId = null;
        page.View.Close();
        host.Children.Remove(page.View);
    }

    /// Navigate the currently live tab. Its successful navigation report
    /// will update CrestCore; no new tab needs to be created.
    public bool NavigateActive(string url)
    {
        if (ActiveView is not { } active
            || !Uri.TryCreate(url, UriKind.Absolute, out var requested))
            return false;

        active.Source = requested;
        return true;
    }

    #endregion

    #region Actions - Environments

    private Task<CoreWebView2Environment> EnvironmentFor(Guid profileId)
    {
        if (environments.TryGetValue(profileId, out var existing))
            return existing;

        var folder = directories.For(profileId);
        Directory.CreateDirectory(folder);
        var creating = CreateEnvironmentAsync(folder);
        environments.Add(profileId, creating);
        return creating;
    }

    // WinUI 3 uses the WinRT async environment factory. Await it in this
    // method so the adapter caches an ordinary .NET Task per Crest profile.
    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync(string folder) =>
        await CoreWebView2Environment.CreateWithOptionsAsync(null, folder, null);

    private async Task InitializeAsync(TabPage page)
    {
        var environment = await EnvironmentFor(page.ProfileId);
        if (disposed) return;

        // The view is in the visual tree, and currently selected views are
        // visible before initialization starts. Do not set Source earlier:
        // that would cause WebView2 to choose its default shared profile.
        await page.View.EnsureCoreWebView2Async(environment);
        if (disposed) return;

        page.View.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            if (disposed || page.CompletedAddress is null
                || page.View.Source?.AbsoluteUri != page.CompletedAddress)
                return;

            var title = page.View.CoreWebView2.DocumentTitle;
            if (string.IsNullOrWhiteSpace(title) || title == page.CompletedTitle)
                return;
            page.CompletedTitle = title;
            var tabId = pages.FirstOrDefault(pair => ReferenceEquals(pair.Value, page)).Key;
            if (tabId != Guid.Empty)
                DocumentTitleChanged?.Invoke(tabId, page.View.Source, title);
        };

        page.View.CoreWebView2.FaviconChanged += (_, _) => _ = ReadFaviconAsync(page);

        page.View.CoreWebView2.DownloadStarting += (_, args) =>
        {
            if (disposed) return;
            // Keep the browser's normal download path and default
            // save/security UI. Never set ResultFilePath: replacing an
            // existing path can silently overwrite a user's file.
            // This Windows preview only observes the transfer.
            DownloadStarted?.Invoke(page.TabId, args.DownloadOperation);
        };

        page.IsReady = true;
        Navigate(page);
    }

    /// Read the icon through the current WebView2 environment rather than
    /// fetching a favicon URL with a separate unauthenticated HTTP client.
    /// Late results are ignored when their page has navigated or closed.
    private async Task ReadFaviconAsync(TabPage page)
    {
        if (disposed || !pages.TryGetValue(page.TabId, out var livePage) ||
            !ReferenceEquals(page, livePage))
            return;

        var revision = ++page.FaviconRevision;
        var address = page.View.Source?.AbsoluteUri;
        var web = page.View.CoreWebView2;
        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;

        bool Current() => !disposed &&
            pages.TryGetValue(page.TabId, out var current) && ReferenceEquals(current, page) &&
            page.FaviconRevision == revision &&
            page.View.Source?.AbsoluteUri == address;

        if (string.IsNullOrWhiteSpace(web.FaviconUri))
        {
            if (Current())
                FaviconChanged?.Invoke(page.TabId, address, null);
            return;
        }

        try
        {
            using var imageStream = await web.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (imageStream is null || imageStream.Size == 0 ||
                imageStream.Size > TabFaviconStore.MaximumIconBytes)
                return;

            var length = (uint)imageStream.Size;
            var png = new byte[length];
            using (var reader = new DataReader(imageStream.GetInputStreamAt(0)))
            {
                if (await reader.LoadAsync(length) != length)
                    return;
                reader.ReadBytes(png);
            }

            if (!Current()) return;
            var bitmap = await TabFaviconStore.ImageFromPngAsync(png);
            if (!Current() || bitmap is null) return;

            FaviconChanged?.Invoke(page.TabId, address, bitmap);
            await faviconStore.SaveAsync(page.TabId, address, png);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // Some pages publish no usable image or cancel an in-flight
            // favicon fetch. An icon failure cannot interrupt navigation.
        }
    }

    private static void Navigate(TabPage page)
    {
        var requested = page.CoreAddress ?? PreviewStartPage;
        if (Uri.TryCreate(requested, UriKind.Absolute, out var address))
            page.View.Source = address;
    }

    #endregion

    #region Actions - Lifetime

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        foreach (var page in pages.Values)
            page.View.Close();

        host.Children.Clear();
        pages.Clear();
        environments.Clear();

        // Persist the user-data directories. They are owned by CrestCore's
        // stable ProfileIds, not by individual window or page lifetimes. The
        // user may remove the preview's Profiles directory manually when
        // they want a complete website-data reset.
    }

    #endregion
}
