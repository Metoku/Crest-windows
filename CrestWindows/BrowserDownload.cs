using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace CrestWindows;

/// Transient UI projection of a WebView2-owned download. Its immutable SpaceId
/// keeps the download list scoped to the Space in which it began, even if the
/// person switches Spaces or closes its originating tab. WebView2 remains the
/// authority for transfer state and destination paths.
public sealed class BrowserDownload : INotifyPropertyChanged, IDisposable
{
    #region Variables

    private readonly CoreWebView2DownloadOperation operation;
    private bool disposed;

    public Guid SpaceId { get; }
    public Guid TabId { get; }
    public string SpaceName { get; }

    public string FilePath => operation.ResultFilePath ?? string.Empty;
    public string FileName
    {
        get
        {
            var name = Path.GetFileName(FilePath);
            return string.IsNullOrWhiteSpace(name) ? "Download" : name;
        }
    }

    public bool IsActive => operation.State == CoreWebView2DownloadState.InProgress;
    public bool IsFinished => !IsActive;
    public bool CanCancel => IsActive;
    public bool CanOpenFolder => operation.State == CoreWebView2DownloadState.Completed &&
        !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
    public Visibility ProgressVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public bool IsIndeterminate => IsActive && operation.TotalBytesToReceive <= 0;

    public double Percent
    {
        get
        {
            if (operation.State == CoreWebView2DownloadState.Completed) return 100;
            var total = operation.TotalBytesToReceive;
            return total > 0
                ? Math.Clamp(100.0 * operation.BytesReceived / total, 0, 100)
                : 0;
        }
    }

    public string StatusText
    {
        get
        {
            var received = FormatSize(operation.BytesReceived);
            var total = operation.TotalBytesToReceive;
            return operation.State switch
            {
                CoreWebView2DownloadState.Completed =>
                    $"{SpaceName} · Completed · {received}",
                CoreWebView2DownloadState.Interrupted
                    when operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled =>
                    $"{SpaceName} · Canceled",
                CoreWebView2DownloadState.Interrupted =>
                    $"{SpaceName} · Interrupted ({operation.InterruptReason})",
                _ when total > 0 =>
                    $"{SpaceName} · {received} / {FormatSize(total)} · {Percent:0}%",
                _ => $"{SpaceName} · {received} downloaded"
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Constructors

    public BrowserDownload(Guid spaceId, Guid tabId, string spaceName,
        CoreWebView2DownloadOperation operation)
    {
        if (spaceId == Guid.Empty || tabId == Guid.Empty)
            throw new ArgumentException("A download must belong to a Crest Space and tab.");

        SpaceId = spaceId;
        TabId = tabId;
        SpaceName = spaceName;
        this.operation = operation ?? throw new ArgumentNullException(nameof(operation));

        // Download events are delivered on the WebView2 UI thread. Never
        // poll the engine from a background task or block WinUI's dispatcher.
        operation.BytesReceivedChanged += Operation_Changed;
        operation.StateChanged += Operation_Changed;
    }

    #endregion

    #region Actions - Download

    public void Cancel()
    {
        if (!disposed && CanCancel)
            operation.Cancel();
    }

    private void Operation_Changed(CoreWebView2DownloadOperation sender, object args)
    {
        if (disposed) return;
        Changed(nameof(FilePath));
        Changed(nameof(FileName));
        Changed(nameof(StatusText));
        Changed(nameof(Percent));
        Changed(nameof(IsIndeterminate));
        Changed(nameof(IsActive));
        Changed(nameof(IsFinished));
        Changed(nameof(CanCancel));
        Changed(nameof(CanOpenFolder));
        Changed(nameof(ProgressVisibility));
    }

    private static string FormatSize(long bytes)
    {
        var size = Math.Max(0, (double)bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion

    #region Actions - Lifetime

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        operation.BytesReceivedChanged -= Operation_Changed;
        operation.StateChanged -= Operation_Changed;
    }

    #endregion
}
