CREST WINDOWS PREVIEW -- TAB AND FOLDER SIDEBAR BUILD

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
- Successful HTTP(S) link clicks, redirects, and address-bar navigation
  now report their landing URLs and document titles to CrestCore.
- CrestCore records per-Space browsing history in session.sqlite. Use the
  History toolbar button to view recent visits in the selected Space.
- The sidebar now groups tabs under Pinned, Saved, and Open and shows folders.
  Select a folder to expand or collapse it. Right-click a tab to pin, save,
  rename, move it into a folder or close it. Right-click a folder to rename
  or remove it while retaining its tabs.
- The + Folder button creates a folder containing the selected tab.
- Closing an Open tab puts it in CrestCore's archive (an archive/restore
  sidebar interface is not implemented yet). Closing a pinned or Saved tab
  only puts its live webpage away, as CrestCore intends.
- CrestCore remembers folders, pinning, custom names and section placement
  across restarts. The sidebar is an early grouped view, not yet a full
  replica of Crest's pinned tile grid or nested sidebar ordering.
- Address-bar navigation now uses the active tab when its WebView2 is ready.
- Back, forward and reload can record new visits when they finish.
- Full native engine events, single-page app client-side route changes,
  complete back/forward restoration after restart, native Chromium
  integration, browser import and password management are NOT implemented.
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

NAVIGATION AND HISTORY TEST
1. Open Personal. Navigate to https://example.org using the address bar.
2. Click a link to another page on the same site, or follow a redirect.
   The selected tab and address bar should reflect the final URL.
3. Open History: the visited URL should appear with its page title.
4. Navigate in Work to https://example.com, then open History there. Work
   should not show Personal's visits.
5. Return to Personal, select the linked page and close CrestWindows.exe.
6. Relaunch the same EXE. The last landed URL, page title, per-Space
   histories and selected tab should still be present.
7. Use only disposable test cookies to verify website isolation.
8. Right-click a tab, choose Pin, then Unpin. Save it to Saved and create
   a folder for it. Rename the folder and expand/collapse it.
9. Close and reopen Crest, checking that its Saved section and folder remain.
10. Open a temporary tab and close it. It should disappear from Open, while
    the existing pinned/saved tabs are retained.

RESET THE DEVELOPMENT PREVIEW
Close the app, then back up or delete the entire
%LOCALAPPDATA%\CrestWindowsPreview\Core and
%LOCALAPPDATA%\CrestWindowsPreview\Profiles folders TOGETHER.
This deletes saved tabs and website data. Do not do this unless you intend to
reset the preview.

This does not modify installed Edge, Arc, Arcium, or macOS Crest data.

WebView2: https://developer.microsoft.com/en-us/microsoft-edge/webview2/
VC++ runtime: https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist
