namespace CrestWindows;

/// Persists only whether the single Crest Windows preview window was
/// maximized. The operating system remains responsible for monitor-specific
/// bounds and placement; never persist a window handle or screen coordinates.
public sealed class WindowPresentationStateStore
{
    #region Variables

    private readonly string path;

    #endregion

    #region Constructors

    public WindowPresentationStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    #endregion

    #region Actions - Window state

    /// The preview opens maximized on its first run. Once the user restores
    /// the window, a recorded "restored" setting takes precedence.
    public bool ReadMaximized()
    {
        try
        {
            if (!File.Exists(path))
                return true;

            return File.ReadAllText(path).Trim() switch
            {
                "Maximized" => true,
                "Restored" => false,
                _ => true
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void SaveMaximized(bool maximized)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, maximized ? "Maximized" : "Restored");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A window-placement preference must not interrupt the browser.
        }
    }

    #endregion
}
