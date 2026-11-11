# CiderPress II (Avalonia) smoke test: Linux

Manual pass for the fixes in `REVIEW_FINDINGS_Consolidated.md` (streams A–L and the ad hoc
fixes AH1–AH4), plus a short end-to-end check. The ID on each step points at the item in that
plan; record failures there as new unchecked items.

CP2 (Avalonia 11) is an **X11 client in both sessions**: on an Xorg session directly, on a Wayland session through XWayland. So one list covers both, with a result mark for each. Run every step on X11; on Wayland the steps that matter most are sections 5, 7 and 8 (keyboard, clipboard, drag and drop) and window placement in section 1. Note the desktop (GNOME/KDE), the file manager and the session type (`echo $XDG_SESSION_TYPE`). Nothing in this list has been run on Linux yet; all of the native drag code (`Platform/LinuxDrag.cs`) was written without being run.

## Setup

- Build and run: `dotnet run --project cp2_avalonia/cp2_avalonia.csproj` from the repository
  root, or use a published build. Note which one you ran.
- Work on a **copy** of `TestData/` (for example `cp -R TestData /tmp/cp2-smoke`); several steps
  modify images. Paths below are relative to that copy.
- Settings file: `~/.config/ciderpress2/CiderPress2-settings.json (or $XDG_CONFIG_HOME)`. Section 1 deletes it; back it up first if you want to keep it.
- The Debug menu is on by default in Debug builds; otherwise enable it in Settings.
- Debug log: Debug → Show Debug Log is the easiest way to see what happened.
- Keys are written for Linux: **Ctrl** is the command key, **Alt** the option key.
- Some steps are marked **Confirm (open item)**: they check an item that has not been fixed yet
  (H4, B7, B9). A "failure" there confirms the bug; just note the result.

Each step ends with **X11 [ ] · Wayland [ ]**: mark the session(s) where it passed (`[x]`), or note the failure.

## Known issues (ignore)

- Under KDE Plasma Wayland (XWayland), the cursor shows a "no drop" glyph inside the CP2
  window during a drag; the drag still works (`KNOWN_ISSUES.md`).
- No native Wayland drag and drop (Avalonia 11 has no Wayland backend).
- Dragging a file onto the window when a file is already open does not open it.

## 1. Launch, settings and window placement
- **H1** Quit CP2. Delete the settings folder (see Setup). Launch, change any option in Settings, quit.
  → The folder and `CiderPress2-settings.json` are recreated; relaunch keeps the option.
  **X11 [ ] · Wayland [ ]**
- **H1** Copy the settings file into the folder that holds the executable (the build output folder, or beside the binary in a published build). Change an option, quit, relaunch.
  → The file beside the executable is the one used and updated; the per-user file is untouched. Delete the copy afterwards.
  **X11 [ ] · Wayland [ ]**
- **H1** Make the per-user settings folder read-only. Launch, change an option, quit.
  → One "Settings could not be saved" toast per session at most; the app keeps working and quit is not blocked. Restore permissions afterwards.
  **X11 [ ] · Wayland [ ]**
- **H1** Help → About.
  → The legal text is shown (in a dev build and in a published build).
  **X11 [ ] · Wayland [ ]**
- **H2** Minimize the main window, then quit (window list or keyboard). Relaunch.
  → Opens as a normal window, on screen.
  **X11 [ ] · Wayland [ ]**
- **H2** Maximize, quit, relaunch.
  → Opens maximized.
  **X11 [ ] · Wayland [ ]**
- **H3** With two monitors: move the window to the second one, quit, disconnect it (or quit, edit the `main-window-placement` value in the settings file to put the window at X = 10000). Relaunch.
  → The window is visible on the main screen.
  **X11 [ ] · Wayland [ ]**
- **H4** **Confirm (open item):** size the window small, maximize, quit, relaunch, un-maximize.
  → Returns to the small size and position. If it comes back at the maximized size, H4 is confirmed: note it.
  **X11 [ ] · Wayland [ ]**

## 2. Opening files
- **P3-2** Open each of: `dos33/new-init.do`, `prodos/simple-dir-test.po`, `hfs/simple-gsos.po`, `nufx/Samples.BXY`, `zip/GSHK11.zip`, `woz/dos33master_1.woz`, `2img/prodos-disk.2mg`, `turducken/MultiPart.hdv`.
  → Each opens; the archive tree shows the expected nodes; the file list is filled.
  **X11 [ ] · Wayland [ ]**
- **G1** Launch with `-r <copy>/prodos/simple-dir-test.po` from a shell.
  → Opens read-only.
  **X11 [ ] · Wayland [ ]**
- **G1** While a large file is loading (`turducken/MultiPart.hdv` or a big image), choose File → Recent Files → another file.
  → The current file is not closed by the second request.
  **X11 [ ] · Wayland [ ]**
- **G1** Add a file to Recent Files, quit, rename that file on disk, relaunch, open something else, then choose the renamed entry in Recent Files.
  → "File not found" is shown and the file that was open stays open (deliberate change from WPF, which closed it first).
  **X11 [ ] · Wayland [ ]**
- **G2** Drag a disk image onto the launch panel (no file open).
  → It opens. (Known limitation: dragging onto the window when a file is already open is not supported.)
  **X11 [ ] · Wayland [ ]**

## 3. File list: sort, selection and mouse input
- **D1** Open `zip/fancy-names.zip` and an HFS image with mixed-case, numbered and accented names (make one with Add if needed: `FILE2`, `file10`, `apple`, `Banana`, `Café`, `Cafe`, `7`, `007`). Sort Filename, then Pathname.
  → Case-insensitive, numbers in numeric order (`FILE2` before `file10`), accented names next to their base letter, folder contents together (`DIR`, `DIR/FILE`, `DIR.BAK`). Compare with the file manager (Nautilus or Dolphin) after extracting the same names.
  **X11 [ ] · Wayland [ ]**
- **D1** Sort every other column ascending and descending (Type, Auxtype, Mod Date, Data Len, Raw Len, Data Fmt, Rsrc Len, Rsrc Fmt, Total Size, Access).
  → Each sorts sensibly; Auxtype in hex order (`$1000`, `$2A00`, `$FF00`).
  **X11 [ ] · Wayland [ ]**
- **D1** Open `zip/GSHK11.zip` (MacZip) and sort Rsrc Len.
  → Rows order by the resource lengths shown, not all equal.
  **X11 [ ] · Wayland [ ]**
- **D2** Create a 32 MB ProDOS image (File → New Disk Image) and Add a few thousand files (for example a large source tree). Full-list mode; click several column headers.
  → No visible stall on each click.
  **X11 [ ] · Wayland [ ]**
- **D2** Select two or three rows, then click a column header.
  → The same rows stay selected in their new positions (WPF behaviour).
  **X11 [ ] · Wayland [ ]**
- **D2** With a sort active, edit a row's attributes (rename it so it moves).
  → The row moves to its sorted place and stays selected.
  **X11 [ ] · Wayland [ ]**
- **D2** With a sort active, delete a row.
  → The row above it in the sorted list is selected.
  **X11 [ ] · Wayland [ ]**
- **D2** Edit → Select All on the large image.
  → Fast.
  **X11 [ ] · Wayland [ ]**
- **D3** Drag the Filename column divider wider, then change directory.
  → The width is kept.
  **X11 [ ] · Wayland [ ]**
- **D3** Double-click a column divider.
  → The column to its left auto-sizes.
  **X11 [ ] · Wayland [ ]**
- **D3** Double-click the middle of a column header.
  → It sorts (twice); it does not auto-size.
  **X11 [ ] · Wayland [ ]**
- **D3** Click a header; then drag a divider.
  → The click sorts; the divider drag does not.
  **X11 [ ] · Wayland [ ]**
- **D4** With a file selected, double-click a scrollbar arrow, and double-click the blank area below the rows.
  → No viewer opens.
  **X11 [ ] · Wayland [ ]**
- **D4** Press on the scrollbar track, or on blank space below the rows, and drag.
  → Scrolls (or nothing); no file drag starts.
  **X11 [ ] · Wayland [ ]**
- **D4** Double-click a row; drag a row.
  → The viewer opens; the drag starts.
  **X11 [ ] · Wayland [ ]**
- **D4** Open `turducken/MultiPart.hdv`, select the top node (partition layout), double-click a header and the blank area, then a row.
  → Only the row double-click opens the partition.
  **X11 [ ] · Wayland [ ]**
- **D5** Full-list mode on `prodos/simple-dir-test.po` (a copy): double-click a subdirectory, then drag a root-level file to blank grid space.
  → The file stays at (or moves to) the root, not into the subdirectory.
  **X11 [ ] · Wayland [ ]**
- **D5** Click a directory row and move the mouse a few pixels before releasing (click jitter).
  → No "Bad Move" dialog.
  **X11 [ ] · Wayland [ ]**
- **D6** View a file in directory A; in the main window go to directory B and select several files; click the viewer window.
  → B's selection is unchanged.
  **X11 [ ] · Wayland [ ]**

## 4. Archive and directory trees
- **E1** Open `turducken/MultiPart.hdv`, select a nested archive (for example `Samples.BXY` inside the HFS partition), Actions → Close File Source.
  → The parent node is selected, its contents are listed, commands are enabled correctly; Extract works.
  **X11 [ ] · Wayland [ ]**
- **E2** Make the window short so the archive tree is taller than its panel; open `turducken/MultiPart.hdv`.
  → The tree scrolls to the selected (best) node.
  **X11 [ ] · Wayland [ ]**
- **E2** Same with a deep directory tree (HFS image with nested folders).
  → The directory tree scrolls to the selection.
  **X11 [ ] · Wayland [ ]**
- **E3** Full tree regression: open nested archives, select, expand and collapse; Close File Source; open a sub-archive; arrow keys in both trees; double-click a directory in the file list.
  → All behave as before; no stale selection.
  **X11 [ ] · Wayland [ ]**
- **F1** Select files in the list, put focus in the archive tree, press Enter. Repeat in the directory tree.
  → The viewer opens with the selected files in both cases (WPF behaviour). With nothing selected, Enter does nothing.
  **X11 [ ] · Wayland [ ]**

## 5. Keyboard
- **F1** Ctrl+F, type a name that matches a file (it becomes selected in the list), then press Delete, Ctrl+A, Ctrl+C, Ctrl+V in the find box.
  → Text editing only: no delete prompt, no select-all of files, no file copy, no paste into the archive.
  **X11 [ ] · Wayland [ ]**
- **F1** Click a file row, then press Delete, Ctrl+A, Ctrl+C.
  → They act on files.
  **X11 [ ] · Wayland [ ]**
- **F1** Focus on the file list, press Enter.
  → The viewer opens.
  **X11 [ ] · Wayland [ ]**
- **F1** Tab to a button (for example the find box ✕) and press Enter.
  → The button acts; no viewer opens.
  **X11 [ ] · Wayland [ ]**
- **J5** Actions → Edit Sectors on a disk image, select some bytes, Ctrl+C.
  → The sector text is copied.
  **X11 [ ] · Wayland [ ]**
- **nav** Alt+Up in a subdirectory.
  → Goes to the parent; the menu bar does not stay in access-key mode.
  **X11 [ ] · Wayland [ ]**

## 6. Actions, progress dialogs and errors
- **A1** Extract files to a folder, and remove that folder (or choose one without write permission) before confirming.
  → An error box with the reason, then a "Failed" toast.
  **X11 [ ] · Wayland [ ]**
- **A1** Start a long extract and press Cancel.
  → "Cancelled" toast; no error box.
  **X11 [ ] · Wayland [ ]**
- **A2** Trigger a worker error as above.
  → The box has the error icon; Enter and Esc dismiss it.
  **X11 [ ] · Wayland [ ]**
- **A3** (Debug build, optional) Temporarily throw in `OverwriteQueryViewModel`'s constructor; extract over an existing file.
  → The worker stops as cancelled; the window closes; no hang. Remove the throw.
  **X11 [ ] · Wayland [ ]**
- **A5** ProDOS image copy: Rename / Edit Attributes on a file, change its type and rename it to the name of another file in the same directory, OK.
  → An error is shown; after dismissing it the list still shows the old type and name, and the list did not refresh before the box closed (A4). Close and reopen the image: still the old values.
  **X11 [ ] · Wayland [ ]**
- **A6** Open `nib/aw-init.nib` (a copy), Actions → Scan for Bad Blocks.
  → The progress bar advances.
  **X11 [ ] · Wayland [ ]**
- **A7** Add files to a disk image and to a ShrinkIt archive; paste into both; cancel mid-way in each.
  → Works as before; cancel leaves the target consistent.
  **X11 [ ] · Wayland [ ]**
- **A8** Test Files on a large archive.
  → Works as before.
  **X11 [ ] · Wayland [ ]**
- **AH1** Extract a file over an existing file.
  → "Overwrite Existing File?": both buttons readable, same width, text not cut off; Escape = Cancel.
  **X11 [ ] · Wayland [ ]**
- **misc** Run Add, Extract, Delete, Move (drag onto a directory), Edit Attributes, Create Directory, Test Files and Defragment on copies of the test images.
  → All work; one forced error per action (read-only target, name clash) is reported clearly.
  **X11 [ ] · Wayland [ ]**

## 7. Copy and paste
- **B1** Copy files in one CP2 instance, paste into a second instance (Add/Extract mode).
  → All files arrive with the right data.
  **X11 [ ] · Wayland [ ]**
- **B1/B7** Copy files in CP2, paste in the file manager (Nautilus or Dolphin).
  → The files appear. **If nothing appears, B7 is confirmed on this platform: note it.**
  **X11 [ ] · Wayland [ ]**
- **B2** Copy a directory `DIR` containing `SUB/FILE1` and an empty `EMPTY`; paste in the file manager (Nautilus or Dolphin).
  → `DIR/SUB/FILE1` and `DIR/EMPTY` exist (the tree is kept).
  **X11 [ ] · Wayland [ ]**
- **B3** Copy a DOS 3.3 file named `X` and paste in the file manager (Nautilus or Dolphin); copy a directory `X` containing `E0000` and paste into a second CP2.
  → The file is present; the second instance gets the right data.
  **X11 [ ] · Wayland [ ]**
- **B4** Copy text in another app; in CP2 open a file, then open another (or close).
  → The other app's text can still be pasted.
  **X11 [ ] · Wayland [ ]**
- **B4** Copy files in CP2, then close the work file; try to paste in a second CP2.
  → Nothing to paste.
  **X11 [ ] · Wayland [ ]**
- **B5** Open an archive, Ctrl+C some files, quit.
  → No `cp2clip_*` folder remains in the temp directory.
  **X11 [ ] · Wayland [ ]**
- **B5** Modify a disk image (add a file), quit without closing it first; reopen.
  → The change was saved.
  **X11 [ ] · Wayland [ ]**
- **B6** (Optional, needs a crafted clipboard string pointing outside the temp folder) paste into a disk image.
  → That entry is skipped.
  **X11 [ ] · Wayland [ ]**
- **B8** Select all on the 32 MB image, Ctrl+C.
  → A progress dialog with Cancel; the UI repaints. Paste into another instance and into the file manager (Nautilus or Dolphin) works.
  **X11 [ ] · Wayland [ ]**
- **B8** Ctrl+C on many files, then Cancel.
  → The clipboard still holds what it held before; no `cp2clip_*` folder left.
  **X11 [ ] · Wayland [ ]**
- **B8** Ctrl+C on one small file.
  → The dialog flashes briefly at most (known UX follow-up).
  **X11 [ ] · Wayland [ ]**
- **B9** **Confirm (open item, Debug build):** in `turducken/MultiPart.hdv` (a copy), change a file inside the nested disk image, then close the work file.
  → If a `ProDOS.InvalidateFileEntries` assert fires, B9 is confirmed: note it.
  **X11 [ ] · Wayland [ ]**

## 8. Drag and drop
- **DnD** Drag files from CP2 to the file manager (Nautilus or Dolphin).
  → The files are written there (extract or export per the Settings mode).
  **X11 [ ] · Wayland [ ]**
- **DnD** Drag files from the file manager (Nautilus or Dolphin) onto the file list and onto a directory in the directory tree.
  → They are added to the current directory / that directory.
  **X11 [ ] · Wayland [ ]**
- **DnD** Drag files between two CP2 windows (Add/Extract mode).
  → They are copied.
  **X11 [ ] · Wayland [ ]**
- **DnD** Internal drag: drag files onto a directory row in the same list.
  → They move into it.
  **X11 [ ] · Wayland [ ]**
- **C10** Drag one selected file from the list to an open viewer.
  → The viewer shows it.
  **X11 [ ] · Wayland [ ]**
- **C1** Select many large files in an HFS or ProDOS image (copy), drag onto a directory in the same window, release immediately.
  → Files moved correctly, no errors; Test Files (or reopen) shows no damage.
  **X11 [ ] · Wayland [ ]**
- **C2** Release a CP2 drag over a terminal that overlaps CP2.
  → Nothing moves.
  **X11 [ ] · Wayland [ ]**
- **C2** Release a CP2 drag over a viewer window that overlaps the main window.
  → The viewer shows the file; nothing moves.
  **X11 [ ] · Wayland [ ]**
- **C2** Fast flick-and-release onto the file manager (Nautilus or Dolphin).
  → The drop goes there.
  **X11 [ ] · Wayland [ ]**
- **C3** Drag a file from the file manager (Nautilus or Dolphin) (one that offers many types) onto CP2.
  → Accepted.
  **X11 [ ] · Wayland [ ]**
- **C4** Close the main window during an export drag; open and close Debug → Drop/Paste Target 20 times.
  → No crash.
  **X11 [ ] · Wayland [ ]**
- **C5** Drag from CP2 over an accepting file manager, then quickly onto a terminal, release.
  → The drag ends within a few seconds; a second drag works.
  **X11 [ ] · Wayland [ ]**
- **C5** Drag a file large enough that extraction takes more than 10 s.
  → The target gets nothing and CP2 shows a failure, not success.
  **X11 [ ] · Wayland [ ]**
- **C6** Make another X client own the clipboard without answering (`xclip -i` then `kill -STOP` it); paste in CP2.
  → Falls back; no exception.
  **X11 [ ] · Wayland [ ]**
- **C7** Copy several hundred entries in one CP2 (clipboard data over 256 KB), paste in a second instance.
  → All entries pasted.
  **X11 [ ] · Wayland [ ]**
- **C11** Leave CP2 idle; watch CPU in `top`.
  → Near zero; drag still works afterwards.
  **X11 [ ] · Wayland [ ]**

## 9. File viewer
- **view** Open `fileconv/test-files.sdk` (a copy); view text, graphics (zoom up and down several steps), and a spreadsheet-type file.
  → All display; zoom is smooth.
  **X11 [ ] · Wayland [ ]**
- **I1** Open a viewer on an RTF or PDF (external viewer), then click other directories in the main tree.
  → The external viewer is not relaunched.
  **X11 [ ] · Wayland [ ]**
- **I1** Delete the viewed file in the main window.
  → The viewer reacts (closes or shows the next file).
  **X11 [ ] · Wayland [ ]**
- **I1** Select 200 files and view; then change the main list selection.
  → The main list stays responsive.
  **X11 [ ] · Wayland [ ]**
- **I2** In a text file with three hits of a word: Find Next twice, then Find Previous three times.
  → Hits 2, then 1, 3, 2 (wrapping).
  **X11 [ ] · Wayland [ ]**
- **I3** Random-Access Text converter: clear "Record length", view another file, press Back. Same in Settings → Configure export options.
  → No exception; the default is shown.
  **X11 [ ] · Wayland [ ]**
- **I4** Type a non-digit into an integer converter option.
  → Ignored; no reformat flicker.
  **X11 [ ] · Wayland [ ]**
- **I5** View a hi-res and a super-hi-res image; step the zoom up and down repeatedly while watching memory (Activity Monitor / Task Manager / top).
  → Output looks the same as before; memory is steady.
  **X11 [ ] · Wayland [ ]**
- **I6** Close a viewer immediately after opening a large file.
  → No error. (Windows: the archive can then be closed — no leaked handle.)
  **X11 [ ] · Wayland [ ]**
- **I6** View a spreadsheet-type file (e.g. AppleWorks SS), Export.
  → Export offers and writes `.csv`.
  **X11 [ ] · Wayland [ ]**
- **I6** Export to a read-only folder.
  → An error is shown.
  **X11 [ ] · Wayland [ ]**
- **I6** View a file that fails to convert, then a good one, then Back.
  → History works.
  **X11 [ ] · Wayland [ ]**
- **I8** Open a viewer on a disk image, start a long Add from the main window, try Next/Prev in the viewer.
  → The viewer's controls are disabled during the Add and work again afterwards, with correct data (also if the viewed file was replaced). Viewers are also disabled while Settings or About is open (expected).
  **X11 [ ] · Wayland [ ]**

## 10. Dialogs
- **J1** `turducken/MultiPart.hdv` (copy): Actions → Replace Partition Contents with a compatible image.
  → Copy works; a busy indicator shows; a write-protected target shows an error.
  **X11 [ ] · Wayland [ ]**
- **J2** Edit Blocks: change a byte in block 5, Next, answer No.
  → Still on block 5 with the edit; Write writes block 5.
  **X11 [ ] · Wayland [ ]**
- **J2** Same, answer Yes.
  → Block 6 loaded; the edit discarded. Repeat with a sector-order change in track/sector mode.
  **X11 [ ] · Wayland [ ]**
- **J3** Settings → Configure export options → change one → OK → OK → reopen.
  → The change is kept. Again but Cancel the Settings dialog: the change is discarded.
  **X11 [ ] · Wayland [ ]**
- **J4** File → New Disk Image: volume number `abc`, an invalid volume name, an invalid custom size.
  → Create is disabled. Valid input enables it and works for each file system type.
  **X11 [ ] · Wayland [ ]**
- **AH2** File → New Disk Image (choose `.po`), Save As Disk Image, New File Archive, viewer Export, Debug → Convert ANI to GIF.
  → Each save dialog shows `Name.ext` once with the base name selected; the saved file has one extension; choosing "All Files" still saves with the extension.
  **X11 [ ] · Wayland [ ]**

## 11. Theme
- **L1** Dark theme; open `woz/dos33master_1.woz` or `2img/prodos-disk.2mg`; show the Information panel.
  → Metadata is readable; read-only entries are dimmed.
  **X11 [ ] · Wayland [ ]**
- **L1** Switch to Light with the panel open.
  → Still readable.
  **X11 [ ] · Wayland [ ]**

## 12. Debug menu and tools
- **K2** Debug → Show Debug Log; open a large archive and run an extract.
  → Entries in order, no gaps, no exceptions.
  **X11 [ ] · Wayland [ ]**
- **K3** Debug log: Save to a read-only file.
  → An error box; the app keeps running.
  **X11 [ ] · Wayland [ ]**
- **AH3** Debug log: Clear; let new entries arrive; Save.
  → The log empties; new entries appear; the saved file holds only them.
  **X11 [ ] · Wayland [ ]**
- **AH4** Debug → Drop/Paste Target: drop something, Clear, drop again.
  → The text empties; the second drop shows.
  **X11 [ ] · Wayland [ ]**
- **I7** Debug → Convert ANI to GIF on a Windows animated cursor (`.ani`) added to an archive; open the GIF in a browser.
  → Plays at the expected speed.
  **X11 [ ] · Wayland [ ]**
- **lib** Debug → DiskArc Library Tests, then FileConv Library Tests.
  → All pass.
  **X11 [ ] · Wayland [ ]**

## Results

Date, build (dev or published, commit), machine and OS version:

Failures (copy into `REVIEW_FINDINGS_Consolidated.md` as new items):
