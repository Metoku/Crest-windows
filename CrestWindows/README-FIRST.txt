CREST WINDOWS PREVIEW -- EARLY DEVELOPMENT BUILD

This is a WinUI 3 + WebView2 prototype using Crest's real C# session core,
NOT the finished Crest browser. Do not use it for sensitive browsing yet.

CURRENT PREVIEW
- Each tab has a separate WebView2 control, kept alive when switching tabs.
- Independent Crest Spaces have different WebView2 user-data directories,
  mapped from CrestCore ProfileId. Same-profile Sub-Spaces can share one.
- Personal and Work currently have separate website cookies/cache/storage.
- These profiles are session-scoped; a new run creates a new session folder.
- The app attempts to delete its temporary WebView2 data on clean exit.
  If Edge processes still hold files open, abandoned folders can remain under
  %LOCALAPPDATA%\CrestWindowsPreview\Sessions\
- CrestCore has no persistent Windows session store yet. New launches begin
  with default tabs and Spaces. Browser-internal navigation and page titles
  are not yet reported back to CrestCore.
- Profile separation has automated path checks. Actual cross-Space browser
  cookie behavior has NOT been verified by an interactive desktop test.

REQUIREMENTS
- Windows 10 2004+ (x64) or Windows 11 x64
- Microsoft Edge WebView2 Evergreen Runtime
- Microsoft Visual C++ Redistributable 2015-2022 (x64), if missing

START
1. Extract EVERY file from the GitHub Actions artifact ZIP into one folder.
2. Double-click CrestWindows.exe.
3. If it silently exits, double-click Start-Crest-Windows.cmd for diagnostics.
4. Report the displayed error message and/or startup.log from
   %LOCALAPPDATA%\CrestWindowsPreview\startup.log

MANUAL CHECK
1. Open a different web address in each Personal and Work Space.
2. Switch tabs repeatedly: content, scroll position, and navigation history
   should stay on each tab without automatically reloading.
3. For cookie isolation, use a disposable TEST login on a site in Personal.
   Work must not inherit that login. Do not use real credentials in preview.

The Windows App SDK and .NET dependencies are shipped in the publish folder.
Do not copy or run CrestWindows.exe alone.

WebView2 runtime: https://developer.microsoft.com/en-us/microsoft-edge/webview2/
Visual C++ runtime: https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist
