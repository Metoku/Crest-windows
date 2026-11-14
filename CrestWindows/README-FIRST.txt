CREST WINDOWS PREVIEW -- DOWNLOADS MANAGER

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
- Use Split in the toolbar to choose another Open tab in the current Space.
  CrestCore keeps the two pages together as a Split View.
- Both webpages stay live side by side. Click either page to make it the
  active address-bar/Back/Forward/Reload target.
- Drag the narrow vertical divider between the two webpages to resize them.
  CrestCore saves the two column-width shares with the window record.
- Click Unsplit to separate the tabs without closing either page.
- A Downloads button in the toolbar shows files being downloaded by WebView2
  in the currently selected Space, with received bytes, progress, and status.
- Downloads started in Personal or Work remain associated with their original
  Space even when you switch Spaces or tabs. Downloads in another Space do
  not appear in this Space's Downloads panel.
- WebView2 retains control of the save prompt, download folder, and browser
  safety decisions. Crest never overrides ResultFilePath, silently overwrites
  an existing file, or automatically opens a downloaded executable.
- You can cancel a transfer from the Downloads panel. After successful
  completion, Open folder launches the containing folder in File Explorer.
- Recent download rows are session-only (up to 50 per window); the actual
  saved files remain on disk. Interrupted transfers are not automatically
  resumed when the preview closes.
- Both split membership and the divider ratio should survive restarting.
- The Windows prototype displays up to two split members. Advanced splits,
  pinned/saved tab copy semantics, popup integration and split drag targets
  are not supported yet. Split requires two separate ordinary Open tabs.
- Cookies and other website data can now survive restart *in the same Space*.

IMPORTANT LIMITATIONS
- Successful HTTP(S) link clicks, redirects, and address-bar navigation
  now report their landing URLs and document titles to CrestCore.
- CrestCore records per-Space browsing history in session.sqlite. Use the
  History toolbar button to view recent visits in the selected Space.
- Favorites tiles and ordinary tab rows now display real website favicons
  obtained from their own WebView2 browser engine. Pages without an icon
  display their initial (Favorites) or standard tab symbol (Saved/Open).
- Favorites now show the icon at 16 layout pixels, rather than stretching the
  commonly small engine favicon to 26. This should look sharper on standard
  DPI displays; high-DPI icons are a separate future improvement.
- Crest opens maximized on the first launch of this version. After that, it
  saves whether you left the window maximized or restored to its own file at:
  %LOCALAPPDATA%\CrestWindowsPreview\window-presentation.state
  Minimize does not overwrite that choice. CrestCore session state is not
  affected by this window-only setting.
- Icons are cached by CrestCore tab identity and the exact visited URL at:
  %LOCALAPPDATA%\CrestWindowsPreview\Favicons\
  They can appear after restart even before their pages are loaded.
- Icons change as webpages navigate; a stale cached icon must not be shown
  for a different visited URL. The cache is presentation-only, not tab state.
- Saved and Open tabs remain grouped below the Favorites tile grid.
- Drag a tab from the list to Favorites to pin it, or drag a favorite down to
  Open/Saved to change its section. Drop on a folder to file the tab there,
  or on another tab to move it ahead of that tab.
- All drag moves go through CrestCore and are saved in its session. A dragged
  tab cannot move across Spaces in this preview.
- Select a folder to expand or collapse it. Right-click a tab to pin, save,
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
%LOCALAPPDATA%\CrestWindowsPreview\Profiles and
%LOCALAPPDATA%\CrestWindowsPreview\Favicons folders TOGETHER.
This deletes saved tabs and website data. Do not do this unless you intend to
reset the preview.

This does not modify installed Edge, Arc, Arcium, or macOS Crest data.

WebView2: https://developer.microsoft.com/en-us/microsoft-edge/webview2/
VC++ runtime: https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist

DRAG TEST
1. Pin a few tabs. Their tiles should appear in the Favorites grid.
2. Drag a tab from Open onto the Favorites grid. It should become pinned.
3. Drag a favorite to the Open section. It should move out of Favorites.
4. Drag an Open tab onto a folder, then onto another tab within that folder.
   The new folder placement and ordering should persist after restart.
5. Hover a pinned tile to see its full title. Website icons replace
   its initial as soon as the site publishes a favicon.

FAVICON TEST
1. Open a familiar site such as https://www.youtube.com and pin its tab.
   Its real favicon should replace the title initial in Favorites.
2. Open another site that publishes a favicon and check the Open list icon.
3. Switch tabs and Spaces. Icons should remain attached to the right tab.
4. Close and reopen the app. Previously visited pinned tabs should show
   cached icons before they load again.
5. Navigate a tab to a different website. The previous favicon must not
   be shown for the new URL; the next site's favicon replaces it once ready.
6. Sites that provide no favicon should keep the readable fallback icon.

WINDOW-STATE CHECK
1. Launch Crest: the window should open maximized by default.
2. Click Restore Down to use a normal window, then close Crest.
3. Relaunch: it should remain restored, not force maximized.
4. Maximize the window, close, and relaunch. It should open maximized.
5. The Favorites icon should look sharper than the earlier 26px enlargement.

SPLIT VIEW TEST
1. In one Space, open two different webpages as regular Open tabs.
2. Select the first and click Split. Choose the second tab from the menu.
3. Verify both webpages remain visible and independently scroll and play.
4. Click the right webpage; Back/Forward/Reload and the address bar should
   now operate on the right page. Click left to switch the active target.
5. Drag the divider to change widths, then close and reopen CrestWindows.exe.
6. Return to the Space: both split pages and your width ratio should restore.
7. Click Unsplit: both tabs should remain available independently.
8. Verify other Spaces still show their own tabs and isolated cookies.

DOWNLOADS TEST
1. Open a regular tab in Personal and download a small file from a trusted
   website (for example the source ZIP from a public GitHub repository).
2. Open Downloads in Crest's toolbar. The filename and progress should
   appear; after completion it should show Completed.
3. Click Open folder and verify File Explorer opens the containing directory.
   Crest should NOT execute the downloaded file.
4. Start another download and use Cancel before it finishes. Its status
   should change to Canceled or Interrupted.
5. Start a download in Work. Switch to Personal: Work's transfer must NOT
   appear in Personal's list, even if it is still downloading.
6. Restart Crest. The download list is expected to reset, but completed
   downloaded files should remain in their saved destination.
7. Downloads should continue normally in a two-pane Split View, with each
   transfer attributed to the correct originating Space.
