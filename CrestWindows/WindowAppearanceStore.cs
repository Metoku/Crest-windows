namespace CrestWindows;

/// A Windows-only visual preference. CrestCore remains the sole owner of
/// browsing state and search preferences; theme is local presentation.
public sealed class WindowAppearanceStore
{
    #region Types

    public enum Appearance { System, Light, Dark }

    #endregion

    #region Variables

    private readonly string path;

    #endregion

    #region Constructors

    public WindowAppearanceStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    #endregion

    #region Actions - Persistence

    public Appearance Read()
    {
        try
        {
            return File.Exists(path) &&
                Enum.TryParse<Appearance>(File.ReadAllText(path).Trim(), out var saved) &&
                Enum.IsDefined(saved) ? saved : Appearance.System;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Appearance.System;
        }
    }

    public void Save(Appearance choice)
    {
        if (!Enum.IsDefined(choice))
            throw new ArgumentOutOfRangeException(nameof(choice));

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, choice.ToString());
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A presentation preference must never interrupt browsing.
        }
    }

    #endregion
}
