using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private bool disposed;

    public event Action<Guid, Uri?, string?, bool>? NavigationCompleted;
    public event Action<Guid, Uri?, string>? DocumentTitleChanged;
    public event Action<Guid, string, BitmapImage?>? FaviconChanged;

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

    /// Shows the tab's existing WebView2 without reloading it. Only a change
    /// in the URL commanded by CrestCore navigates an already-live page.
    public async Task ShowAsync(Guid tabId, Guid profileId, string? coreAddress)
    {
        if (disposed) return;

        if (!pages.TryGetValue(tabId, out var page))
        {
            page = new TabPage(tabId, profileId, coreAddress);
            var capturedTabId = tabId;
            page.View.NavigationCompleted += (sender, args) =>
            {
                if (disposed) return;
                var source = sender.Source;
                var title = sender.CoreWebView2?.DocumentTitle;
                if (args.IsSuccess && source is not null)
                {
                    // The actual landing URL wins over the originally
                    // requested URL (redirects, links, back/forward).
                    // Otherwise selecting this tab would navigate it back.
                    page.CoreAddress = source.AbsoluteUri;
                    page.CompletedAddress = source.AbsoluteUri;
                    page.CompletedTitle = title;
                }

                NavigationCompleted?.Invoke(capturedTabId, source, title, args.IsSuccess);
            };
            pages.Add(tabId, page);
            host.Children.Add(page.View);
        }
        else if (page.ProfileId != profileId)
        {
            throw new InvalidOperationException("CrestCore changed a tab's profile without an engine rehost.");
        }

        var changedAddress = !string.Equals(page.CoreAddress, coreAddress, StringComparison.Ordinal);
        page.CoreAddress = coreAddress;
        activeTabId = tabId;

        foreach (var (id, entry) in pages)
            entry.View.Visibility = id == tabId ? Visibility.Visible : Visibility.Collapsed;

        if (page.Initialization is null)
            page.Initialization = InitializeAsync(page);
        else if (page.IsReady && changedAddress)
            Navigate(page);

        await page.Initialization;
    }

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
