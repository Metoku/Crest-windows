using CrestCore.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrestWindows;

/// Windows-only appearance and a CrestCore-backed per-Space search choice.
/// The Settings panel is loaded lazily; no flyout controls are touched
/// before its Loaded event. No second Windows search preference is stored.
public sealed partial class MainWindow
{
    #region Variables

    private WindowAppearanceStore? appearanceStore;
    private bool updatingSettings;
    private bool settingsLoaded;

    #endregion

    #region Actions - Appearance

    private void InitializeSettings()
    {
        var directory = Path.GetDirectoryName(core.StorageDirectory)!;
        appearanceStore = new WindowAppearanceStore(
            Path.Combine(directory, "window-appearance.state"));
        ApplyAppearance(appearanceStore.Read());
    }

    private void ApplyAppearance(WindowAppearanceStore.Appearance appearance)
    {
        RootLayout.RequestedTheme = appearance switch
        {
            WindowAppearanceStore.Appearance.Dark => ElementTheme.Dark,
            WindowAppearanceStore.Appearance.Light => ElementTheme.Light,
            _ => ElementTheme.Default
        };
    }

    private void AppearanceCombo_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updatingSettings || AppearanceCombo?.SelectedItem is not ComboBoxItem { Tag: string name }
            || !Enum.TryParse<WindowAppearanceStore.Appearance>(name, out var appearance))
            return;

        appearanceStore?.Save(appearance);
        ApplyAppearance(appearance);
    }

    #endregion

    #region Actions - Search

    private void SettingsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        settingsLoaded = true;
        RefreshSettingsChoices();
    }

    private void RefreshSettingsChoices()
    {
        if (!settingsLoaded || SearchEngineCombo is null || AppearanceCombo is null)
            return;

        updatingSettings = true;
        try
        {
            var themeName = (appearanceStore?.Read() ??
                WindowAppearanceStore.Appearance.System).ToString();
            AppearanceCombo.SelectedItem = AppearanceCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => (string?)item.Tag == themeName);

            var space = core.SelectedSpace;
            SearchSpaceText.Text = space is null
                ? "Search engine" : $"Search engine · {space.Settings.Name}";

            SearchEngineCombo.Items.Clear();
            var chosen = core.SelectedSearchProvider;
            var defaultItem = new ComboBoxItem
            {
                Content = $"Follow default ({(core.SearchFollowsDefault ? chosen?.Title : "Google") ?? "Google"})"
            };
            SearchEngineCombo.Items.Add(defaultItem);

            foreach (var provider in core.SearchProviders)
            {
                SearchEngineCombo.Items.Add(new ComboBoxItem
                {
                    Content = provider.Title,
                    Tag = provider
                });
            }

            SearchEngineCombo.SelectedItem = core.SearchFollowsDefault
                ? defaultItem
                : SearchEngineCombo.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => (item.Tag as SearchProvider)?.Name == chosen?.Name)
                  ?? defaultItem;
        }
        finally
        {
            updatingSettings = false;
        }
    }

    private void SearchEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updatingSettings || SearchEngineCombo?.SelectedItem is not ComboBoxItem item)
            return;

        try
        {
            core.SetSpaceSearchProvider(item.Tag as SearchProvider);
            Refresh();
            StatusText.Text = "Search preference saved in CrestCore";
        }
        catch (Exception error)
        {
            StatusText.Text = "Could not change search engine: " + error.Message;
            RefreshSettingsChoices();
        }
    }

    #endregion
}
