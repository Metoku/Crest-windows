using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace CrestWindows;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteStartupFailure("Unhandled .NET exception", args.ExceptionObject?.ToString() ?? "Unknown error");

        UnhandledException += (_, args) =>
            WriteStartupFailure("Unhandled WinUI exception", args.Exception?.ToString() ?? "Unknown error");

        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            WriteStartupFailure("App.InitializeComponent failed", exception.ToString());
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception exception)
        {
            WriteStartupFailure("MainWindow startup failed", exception.ToString());
            throw;
        }
    }

    private static void WriteStartupFailure(string description, string details)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CrestWindowsPreview");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "startup.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {description}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Never hide the original startup error when diagnostics cannot be saved.
        }
    }
}
