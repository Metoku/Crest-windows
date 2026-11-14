using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace CrestWindows;

/// Stores only WebView2-provided PNG favicon bytes and the page URL they
/// belong to. Tab identity comes from CrestCore; this is a presentation cache,
/// never another source of browser or navigation state.
public sealed class TabFaviconStore
{
    #region Static Variables

    public const uint MaximumIconBytes = 1024 * 1024;

    #endregion

    #region Variables

    private readonly string directory;

    #endregion

    #region Constructors

    public TabFaviconStore(string directory)
    {
        this.directory = Path.GetFullPath(directory);
    }

    #endregion

    #region Actions - Icons

    public async Task<BitmapImage?> ReadAsync(Guid tabId, string? address)
    {
        if (tabId == Guid.Empty || string.IsNullOrWhiteSpace(address))
            return null;

        try
        {
            var savedAddress = await File.ReadAllTextAsync(AddressPath(tabId));
            if (!string.Equals(savedAddress, address, StringComparison.Ordinal))
                return null;

            var path = IconPath(tabId);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaximumIconBytes)
                return null;

            var bytes = await File.ReadAllBytesAsync(path);
            return await ImageFromPngAsync(bytes);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or ArgumentException or
                System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // A missing or invalid cached image must never prevent browsing.
            return null;
        }
    }

    public async Task SaveAsync(Guid tabId, string address, byte[] png)
    {
        if (tabId == Guid.Empty || string.IsNullOrWhiteSpace(address) ||
            png.Length is 0 or > MaximumIconBytes)
            return;

        try
        {
            Directory.CreateDirectory(directory);
            // Write the URL last. If storage is interrupted, a prior icon
            // cannot be accepted for a different page URL on the next run.
            await File.WriteAllBytesAsync(IconPath(tabId), png);
            await File.WriteAllTextAsync(AddressPath(tabId), address);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Cache persistence is optional; the current UI icon still works.
        }
    }

    public static async Task<BitmapImage?> ImageFromPngAsync(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaximumIconBytes)
            return null;

        try
        {
            using var memory = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(memory))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            memory.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(memory);
            return bitmap;
        }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException or
                System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    #endregion

    #region Actions - Paths

    private string IconPath(Guid tabId) => Path.Combine(directory, tabId.ToString("N") + ".png");

    private string AddressPath(Guid tabId) => Path.Combine(directory, tabId.ToString("N") + ".url");

    #endregion
}
