CREST WINDOWS PREVIEW -- EARLY TEST BUILD

This is a WinUI 3 + WebView2 prototype, NOT the finished Crest browser.
Tabs and Work/Personal groups are UI previews and do NOT isolate browser accounts
or site data. Do not use it for passwords or private browsing.

REQUIREMENTS
- Windows 10 2004+ (x64) or Windows 11 x64
- Microsoft Edge WebView2 Evergreen Runtime
- Microsoft Visual C++ Redistributable 2015-2022 (x64), if missing

START
1. Extract EVERY file from the GitHub Actions artifact ZIP into one folder.
2. Double-click CrestWindows.exe.
3. If it silently exits, double-click Start-Crest-Windows.cmd for diagnostics.
   The window will remain open after the application exits.
4. Report the displayed error message and/or startup.log from
   %LOCALAPPDATA%\CrestWindowsPreview\startup.log

The Windows App SDK and .NET dependencies are shipped in the publish folder,
so never copy or run CrestWindows.exe alone.

WebView2 runtime: https://developer.microsoft.com/en-us/microsoft-edge/webview2/
Visual C++ runtime: https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist
