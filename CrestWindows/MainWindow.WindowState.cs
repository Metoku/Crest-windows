using Microsoft.UI.Windowing;

namespace CrestWindows;

/// Restores and tracks native Windows maximization independently from
/// CrestCore's saved tab/Space/window selection. Only presentation state
/// belongs here; no browsing state is duplicated.
public sealed partial class MainWindow
{
    #region Variables

    private WindowPresentationStateStore? presentationState;
    private bool? lastSavedMaximized;
    private bool trackingPresentation;

    #endregion

    #region Actions - Window presentation

    /// Called after Window.Activate so the native AppWindow is ready.
    public void RestoreWindowPresentation()
    {
        if (trackingPresentation || AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        var directory = Path.GetDirectoryName(core.StorageDirectory)!;
        presentationState = new WindowPresentationStateStore(
            Path.Combine(directory, "window-presentation.state"));

        var maximize = presentationState.ReadMaximized();
        lastSavedMaximized = maximize;

        // On the first launch, default to maximized. On later launches,
        // restore the user's last maximized/restored choice.
        if (maximize)
            presenter.Maximize();

        AppWindow.Changed += AppWindow_Changed;
        // AppWindow.Closing runs before the native HWND is destroyed.
        AppWindow.Closing += (_, _) => SavePresentationState();
        trackingPresentation = true;
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange)
            SavePresentationState();
    }

    private void SavePresentationState()
    {
        if (presentationState is null || AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        // Minimizing is temporary and should not erase the last maximized
        // choice. Windows fires presenter/size changes during transitions.
        bool? maximized = presenter.State switch
        {
            OverlappedPresenterState.Maximized => true,
            OverlappedPresenterState.Restored => false,
            _ => null
        };
        if (maximized is null || lastSavedMaximized == maximized)
            return;

        presentationState.SaveMaximized(maximized.Value);
        lastSavedMaximized = maximized;
    }

    #endregion
}
