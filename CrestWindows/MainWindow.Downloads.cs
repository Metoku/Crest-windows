using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace CrestWindows;

/// A transient Downloads panel for the Windows preview. Each transfer
/// belongs to its originating CrestCore Space and remains controlled by its
/// WebView2 download operation. No URL, file path, or download history is
/// written into another Space's browser state.
public sealed partial class MainWindow
{
    #region Static Variables

    private const int MaximumRecentDownloads = 50;

    #endregion

    #region Variables

    private readonly List<BrowserDownload> downloads = [];
    private readonly ObservableCollection<BrowserDownload> visibleDownloads = [];

    #endregion

    #region Actions - Downloads

    private void InitializeDownloads()
    {
        // Flyout content may be created lazily. Do not touch named controls
        // inside that flyout until their Loaded event has fired.
        browserTabs.DownloadStarted += BrowserTabs_DownloadStarted;
        Closed += (_, _) =>
        {
            browserTabs.DownloadStarted -= BrowserTabs_DownloadStarted;
            foreach (var item in downloads)
            {
                item.PropertyChanged -= Download_PropertyChanged;
                item.Dispose();
            }
            downloads.Clear();
            visibleDownloads.Clear();
        };

        RefreshDownloads();
    }

    private void DownloadsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        DownloadsList.ItemsSource = visibleDownloads;
        RefreshDownloads();
    }

    private void BrowserTabs_DownloadStarted(Guid tabId, CoreWebView2DownloadOperation operation)
    {
        if (windowClosed) return;

        // The event belongs to its initiating tab, not the currently
        // selected tab. Downloads from a hidden split pane or a background
        // Space must never be attributed to the active Space by mistake.
        var owner = core.Spaces.FirstOrDefault(space =>
            space.Tabs.Any(tab => tab.Id == tabId));
        if (owner is null) return;

        var item = new BrowserDownload(owner.Id, tabId, owner.Settings.Name, operation);
        item.PropertyChanged += Download_PropertyChanged;
        downloads.Insert(0, item);

        // Retain a small recent in-memory list, without dropping active
        // transfers. This preview does not yet persist download history.
        while (downloads.Count > MaximumRecentDownloads)
        {
            var older = downloads.LastOrDefault(entry => entry.IsFinished);
            if (older is null) break;
            downloads.Remove(older);
            older.PropertyChanged -= Download_PropertyChanged;
            older.Dispose();
        }

        RefreshDownloads();
        if (core.ShownSpaceId == owner.Id)
            StatusText.Text = "Download started · open Downloads for progress";
    }

    private void Download_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        // ProgressBar and TextBlock bindings update themselves. Only a
        // transfer-state change can alter the active-download badge.
        if (args.PropertyName == nameof(BrowserDownload.IsActive))
            UpdateDownloadsBadge();
    }

    private void RefreshDownloads()
    {
        if (windowClosed) return;

        visibleDownloads.Clear();
        if (core.ShownSpaceId is { } currentSpaceId)
        {
            foreach (var item in downloads.Where(item => item.SpaceId == currentSpaceId))
                visibleDownloads.Add(item);
        }

        if (NoDownloadsText is not null)
            NoDownloadsText.Visibility = visibleDownloads.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        UpdateDownloadsBadge();
    }

    private void UpdateDownloadsBadge()
    {
        if (windowClosed) return;

        var count = visibleDownloads.Count(item => item.IsActive);
        DownloadsButton.Content = count > 0 ? $"Downloads ({count})" : "Downloads";
    }

    /// "Save Page As" is intentionally not a DownloadStarting operation.
    /// WebView2's native downloads dialog is the only complete view of those
    /// saved pages, so expose it without pretending they are file transfers
    /// in Crest's custom per-Space list.
    private void ShowBrowserDownloads_Click(object sender, RoutedEventArgs e)
    {
        var view = browserTabs.ActiveView;
        if (view?.CoreWebView2 is not { } browser)
        {
            StatusText.Text = "Select a loaded webpage to view its saved pages.";
            return;
        }

        try
        {
            DownloadsButton.Flyout?.Hide();
            // This dialog uses the selected WebView2's profile. It includes
            // Save Page As results not available through DownloadStarting.
            browser.OpenDefaultDownloadDialog();
        }
        catch (Exception error) when (error is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            StatusText.Text = "Could not open WebView2 downloads: " + error.Message;
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BrowserDownload item } ||
            item.SpaceId != core.ShownSpaceId || !item.CanCancel)
            return;

        try
        {
            item.Cancel();
        }
        catch (Exception error)
        {
            StatusText.Text = "Could not cancel download: " + error.Message;
        }
    }

    private void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BrowserDownload item } ||
            item.SpaceId != core.ShownSpaceId || !item.CanOpenFolder)
            return;

        var folder = Path.GetDirectoryName(item.FilePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        try
        {
            // Opening the containing directory is deliberate: never
            // automatically execute a file obtained from an untrusted site.
            // ArgumentList prevents the path from becoming a shell command.
            var start = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false
            };
            start.ArgumentList.Add(folder);
            Process.Start(start);
        }
        catch (Exception error) when (error is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "Could not open downloads folder: " + error.Message;
        }
    }

    #endregion
}
