CREST WINDOWS PREVIEW -- SESSION RESTORE TEST BUILD

This is a WinUI 3 + WebView2 preview using CrestCore's real C# session
authority, NOT a finished Crest Windows browser. Do not use sensitive accounts.

WHAT IS NEW
- CrestCore keeps a durable SQLite session in:
  %LOCALAPPDATA%\CrestWindowsPreview\Core\session.sqlite
- Reopening the app restores Space identities, tabs opened through CrestCore,
  their original addresses, and the last selected Space/tab.
- Independent Crest Space profiles use separate persistent WebView2 folders
  rooted at %LOCALAPPDATA%\CrestWindowsPreview\Profiles\
- Each tab still has its own live WebView2 while the app is running. Switching
  between tabs keeps the live page instead of always reloading it.
- Cookies and other website data can now survive restart *in the same Space*.

IMPORTANT LIMITATIONS
- Hyperlinks and redirects followed INSIDE a webpage are not yet reported to
  CrestCore. They may not be restored to their latest URL after restarting.
  Enter addresses through Crest's address bar to test session restoration.
- Browsing history, page titles, true Crest Chromium integration, browser
  import and password management are NOT implemented.
- This preview is not security-audited. Automated tests check that different
  CrestCore ProfileIds use different WebView2 folders; test cookie isolation
  manually with disposable test cookies, not real logins.
- This build stores cookies and other site data until explicitly cleared.
  Do NOT delete the Core folder alone: doing so creates new profile IDs and
  leaves older WebView2 data folders unused.
- An older pre-persistence preview's temporary session is not migrated.

REQUIREMENTS
- Windows 10 2004+ (x64) or Windows 11 x64
- Microsoft Edge WebView2 Evergreen Runtime
- Visual C++ Redistributable 2015-2022 (x64) if missing

HOW TO START
1. Extract EVERY file from the GitHub Actions artifact ZIP into a NEW folder.
2. Double-click CrestWindows.exe (do not move the EXE out of its folder).
3. If it silently closes, run Start-Crest-Windows.cmd from the same folder.
4. Send the error or the startup log at:
   %LOCALAPPDATA%\CrestWindowsPreview\startup.log

RESTORE TEST
1. Open the preview. In Personal, enter https://example.org via the address
   bar and open another tab with https://example.net.
2. In Work, enter https://example.com in its address bar.
3. Switch back to Personal, select the example.net tab, then CLOSE the app.
4. Reopen CrestWindows.exe. Check that all tabs, the Work and Personal Spaces,
   and the selected Personal tab remain.
5. Use test-only cookies to confirm that Work does not see Personal's cookies.

RESET THE DEVELOPMENT PREVIEW
Close the app, then back up or delete the entire
%LOCALAPPDATA%\CrestWindowsPreview\Core and
%LOCALAPPDATA%\CrestWindowsPreview\Profiles folders TOGETHER.
This deletes saved tabs and website data. Do not do this unless you intend to
reset the preview.

This does not modify installed Edge, Arc, Arcium, or macOS Crest data.

WebView2: https://developer.microsoft.com/en-us/microsoft-edge/webview2/
VC++ runtime: https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist
