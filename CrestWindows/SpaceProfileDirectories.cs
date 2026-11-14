namespace CrestWindows;

/// Maps CrestCore's browsing profile identities to separate WebView2 user
/// data directories. Sub-Spaces with the same ProfileId deliberately share
/// a directory; independent Spaces have different profile identities.
public sealed class SpaceProfileDirectories
{
    #region Variables

    public string Root { get; }

    #endregion

    #region Constructors

    public SpaceProfileDirectories(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    #endregion

    #region Actions - Profiles

    public string For(Guid profileId)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("A WebView2 profile must have a CrestCore identity.", nameof(profileId));

        return Path.Combine(Root, profileId.ToString("N"));
    }

    #endregion
}
