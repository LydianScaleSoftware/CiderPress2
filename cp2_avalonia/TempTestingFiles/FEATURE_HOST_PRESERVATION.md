# Feature note: "Host" preservation (macOS) in the Avalonia sidebar

Status: **not started.**  This is a write-up for a later pass.  No code was changed.
WPF parity is a baseline, not a ceiling, so adding this to the Avalonia app only is fine.

## 1. What it is

A Mac stores files in a way that happens to match an Apple II / classic Mac OS file:

- **Resource fork**: lives inside the same file, at the path `FILE/..namedfork/rsrc`.
- **File type and creator** (4-char codes): live in the `com.apple.FinderInfo` extended
  attribute (32 bytes; type in bytes 0-3, creator in bytes 4-7).

"Host preservation" means: when exporting, write a real Mac file (data fork, resource fork,
Finder type/creator) instead of wrapping the extras in AppleSingle, AppleDouble or a NAPS
file name.  When adding, read those two things back from the Mac file.

It is macOS-only because only macOS has the `..namedfork/rsrc` path convention and the
`getxattr`/`setxattr` calls for FinderInfo.  The code makes no attempt elsewhere.  Windows
and Linux hosts would write a plain file plus a bogus `..namedfork` directory.

It is already in the **CLI and in the shared library** (`AppCommon`).  Only the GUIs lack it.
The GUI manual says so: "since the GUI app was written for Windows, this feature is only
available from the CLI tool" (`ndocs/gui-manual/aeie.html`).

## 2. What already exists

| Piece | Where | Notes |
|---|---|---|
| Mode enum | `AppCommon/ExtractFileWorker.cs:36` | `PreserveMode { Unknown, None, ADF, AS, Host, NAPS }` |
| Extract to disk | `AppCommon/ExtractFileWorker.cs:542` | Creates the file, writes the data fork, writes the resource fork to `FILE/..namedfork/rsrc`, then calls `SetFileAttributes` |
| Finder type/creator on extract | `AppCommon/ExtractFileWorker.cs:959` | `SystemXAttr.SetXAttr(..., "com.apple.FinderInfo", ...)`.  Returns false if it fails |
| xattr P/Invoke | `CommonUtil/SystemXAttr.cs` | `getxattr`/`setxattr` via libc.  Returns -1 on non-macOS |
| Clip/drag file set | `AppCommon/ClipFileSet.cs:537` | Host case: one foreign entry for the data fork, one for `FILE/..namedfork/rsrc` |
| Add side | `AppCommon/AddFileSet.cs:98,108,251,319` | `AddOpts.CheckNamed` (resource fork) and `AddOpts.CheckFinderInfo` (type/creator); `ScanForHostAttr()` runs on every file |
| CLI | `cp2/ParamsBag.cs:39,163,420`, `cp2/Add.cs:230` | `--preserve=host` / `-ph`; `--from-host`.  `HasHostPreservation` is `IsOSPlatform(OSX)`.  `--from-host` is forced off on non-macOS |
| CLI test | `cp2/Tests/TestExtract.cs:92` | Host extract test, macOS only |
| Docs | `ndocs/Manual-cp2.md` (Host-Specific, Pairing Resource Forks) | |

Both GUIs deliberately switch the Add side off: WPF `MainController.cs:1417` says
"not for Windows".  Avalonia `MainViewModel.ConfigureAddOpts` (`MainViewModel.cs:1957`) has
`CheckNamed = false; CheckFinderInfo = false;`.

## 3. What the Avalonia app has today (the places to touch)

- **Sidebar UI**, `Views/MainWindow.axaml`:
  - Add side, "Preservation Handling": three `CheckBox`es (AS, ADF, NAPS), lines ~866-868.
  - Extract side, "Preservation Mode": four `RadioButton`s in group `ExtPreserve` (None, AS,
    ADF, NAPS), lines ~891-894.
- **View model**, `ViewModels/OptionsPanelViewModel.cs`:
  - `AddPreserveAS/ADF/NAPS` are bool settings (default true), lines ~132-155.
  - `ExtPreserveNone/AS/ADF/NAPS` read and write one backing field `mExtPreserveMode`,
    persisted as an enum under `ext-preserve-mode`; `RaiseAllExtPreserve()`;
    `RefreshFromSettings()` re-reads it (lines ~241-291, 378-396).
- **Settings keys**, `Models/AppSettings.cs:63-70`: `add-preserve-adf/as/naps`,
  `ext-preserve-mode`.
- **Add options**, `MainViewModel.ConfigureAddOpts(bool isImport)` (~line 1948).  Used by file
  drops from the OS, the Add Files menu, and paste of file URLs.
- **Extract and copy**, `MainViewModel.BuildClipSetup` (~line 2079) reads
  `EXT_PRESERVE_MODE` and hands it to `ClipFileSet`.  This path covers copy, cut and drag-out.
- **Temp-file writing**, `Services/ClipTransferFiles.cs` and `Services/ClipFileTimestamps.cs`.
  `ClipFileTimestamps` already knows about `..namedfork/rsrc`.

## 4. Design (decided: one Add check box, default off; see section 8)

### Extract side
Add a fifth radio button, **"Host"**, to the "Preservation Mode" group.  Add
`ExtPreserveHost` to `OptionsPanelViewModel` and to `RaiseAllExtPreserve()`.  The enum value
already exists, so the stored setting needs no new key.

### Add side
The CLI uses a single `--from-host` switch that sets both `CheckNamed` and `CheckFinderInfo`.
Mirror that in the sidebar: one check box **"Host"** (macOS only), bound to a new setting such
as `add-preserve-host`.  Keep the `isImport` branch forcing it off, as for the other three.

**Which flags it turns on (decided by the user):** a new **"Host" area in the Settings dialog**
(`Views/EditAppSettings`, General tab, with its `EditAppSettingsViewModel`) holds a combo box
that chooses what the sidebar check box means:
- Resource fork **and** file type (default): `CheckNamed = CheckFinderInfo = true`
- Resource fork only: `CheckNamed = true`, `CheckFinderInfo = false`
- File type only: `CheckNamed = false`, `CheckFinderInfo = true`

So the check box is the on/off switch and the combo is for the user who wants something other
than the default.  "Neither" is not offered, since that is the same as leaving the check box
off.  The combo is stored as an enum under a new key (for example `host-add-flags`).  It affects
**adding only**: the Extract side has no equivalent, because `Host` extraction always writes the
resource fork and the Finder info.  The area's label should say so ("when adding files").

### Showing it only on macOS
**Decided by the user:** if the software detects it is running on **Windows or Linux**, it
ignores every host setting internally, regardless of whether they exist in the settings file or
what their values are.  That means the Host check box, the Host extract mode, and the Settings
combo are all treated as off / `None` on those platforms (see 5.1).  Only macOS honours them.

**Decided by the user:** the new controls are also hidden when `!OperatingSystem.IsMacOS()`
(matching the CLI's `HasHostPreservation`): the Settings-dialog Host area and the sidebar Host
check box.  The Extract side's Host radio button is hidden by the same rule (assumed from the
earlier decision to hide both new controls off macOS).  Options for the plumbing:
- an `IsHostPreserveAvailable` property on `OptionsPanelViewModel` (testable, no platform
  call in the view), bound to `IsVisible`; or
- `OperatingSystem.IsMacOS()` evaluated once in the view.

The first is easier to unit test and matches how other platform checks sit in this app.
`SystemXAttr.IsMacOS` / `ParamsBag.HasHostPreservation` are the existing definitions.

### Default
Default **off** for both.  Existing users should see no change.  The CLI defaults
`--from-host` to on for macOS, which is a different tradeoff; for a GUI where the preserve
check boxes are visible, off-by-default is the safer start.  (Worth deciding explicitly.)

## 5. Ramifications and risks

### 5.1 The saved setting can outlive the platform
`ext-preserve-mode = Host` is stored in the settings JSON.  The settings file can sit beside
the executable ("portable install", `SettingsService.GetSettingsFilePath`) and be copied to
another machine.  If it reaches Windows or Linux, `BuildClipSetup` would pass `Host` to
`ClipFileSet`, which then builds paths like `FILE\..namedfork\rsrc` and fails or writes junk.
**Decided by the user:** on Windows and Linux every host setting is ignored internally,
whatever its value.  Implement it as one central rule rather than scattered checks: an
"effective" value that is `None` / off unless `OperatingSystem.IsMacOS()`, used by the view
model getters, `BuildClipSetup` and `ConfigureAddOpts`.  The stored value is left alone in the
file (so a settings file moved back to a Mac still has the user's choice), only the effective
value changes.  Apply the same rule to the add-side check box and the new Settings combo.
Because the rule is a pure function of platform and stored value, it can be unit tested without
a Mac if the platform check is passed in.

### 5.2 The clip/drag path does not set the Finder type and creator
`ExtractFileWorker` (extract to disk, used by the CLI) sets `com.apple.FinderInfo`.  The
**clipboard and drag-out path does not**: `ClipTransferFiles.Extract` opens each foreign entry
with `FileMode.Create` and calls `ClipFileEntry.OutputToStream`, which just copies the fork.
Nothing in that path calls `SystemXAttr.SetXAttr`.  So with a Host radio button, copy/drag would
produce a Mac file with the right forks and dates but **no file type or creator**, which is
half of the feature.  Fixing it means a small addition after the files are written (a host-path
step, probably next to `ClipFileTimestamps.Apply()`).

**Decided by the user:** this step may live in `AppCommon`.  Suggested shape: move the existing
Finder-info block in `ExtractFileWorker.SetFileAttributes` (line ~959) into a small public
static helper (for example `SetHostFinderInfo(path, attrs)`), have `ExtractFileWorker` call it
(no behaviour change), and have `ClipTransferFiles` call it after the Host-mode files are
written.  Because `AppCommon` is shared with the WPF app and the CLI, the change must be
purely additive for them: same results, same failure behaviour, and the existing CLI Host test
(`cp2/Tests/TestExtract.cs`, macOS only) must still pass.  WPF source is not edited.

### 5.3 Entries with a resource fork but no data fork
`ExtractFileWorker` always creates the file first.  `ClipFileSet.CreateForExtract` (Host case)
only creates a data-fork entry when `dataPart != Unknown`, then adds the
`..namedfork/rsrc` entry.  Writing `FILE/..namedfork/rsrc` when `FILE` doesn't exist probably
fails on macOS.  Not confirmed; needs a test on a Mac with a resource-fork-only file.

### 5.4 Temp directory and volume support
Drag and copy write to `Path.GetTempPath()`.  On macOS that is on the APFS system volume, so
forks and xattrs work there.  The final copy to the destination is done by Finder, which
carries forks and xattrs along.  If the destination is FAT/exFAT, Finder converts to `._`
files itself; this app cannot control that.  The direct "Extract to a folder" path writes
straight to the user's chosen location, so the destination volume matters there.

**Correction to my first draft:** I said a FAT/exFAT target would fail outright.  From memory
of how macOS works (not tested here), the kernel emulates `..namedfork/rsrc` and xattrs on
volumes without native support by storing them in a hidden `._name` AppleDouble file.  So the
likely outcome is not a failed write but a *silent downgrade*: the data fork and a `._` sidecar
land on the volume.  That is arguably worse for the user than an error, which makes a warning
more valuable.  Verify on the Intel Mac with a FAT/exFAT stick and an SMB share.

#### Warning the user (design sketch)

Where this can and cannot work:
- **Extract Files... to a folder**: the app knows the destination, so a contextual warning is
  possible.
- **Drag-out and copy/paste**: the files are written to the system temp folder; Finder does the
  real copy later, to a place the app never sees.  No destination warning is possible.  (This
  path also swallows write errors, see below.)

How it is reported today (Avalonia `Actions/ProgressUtil.cs`, same as WPF):
- A failed fork write in `ExtractFileWorker` is an `IOException` caught at line ~368, reported
  as `CallbackFacts.Reasons.Failure` with the text "Error during extraction of 'X': " plus the
  .NET exception text.  The operation stops.  The reason is a free-text string, not a
  machine-readable cause, so the app cannot reliably tell "volume has no forks" from any other
  I/O error without string matching.
- A failed `setxattr` for the Finder info is `Reasons.AttrFailure`, a warning that raises one
  modal OK box **per file**, and `ExtractFileWorker` (line ~617) **does not set `FailMessage`**,
  so the box text is empty.  This exists today and would show up as soon as Host is selectable.
  Extracting 500 files to a bad volume could give 500 empty boxes.
- The clip/drag writer (`ClipTransferFiles.Extract`) catches every exception per entry, logs
  with `AppLog.E` and continues.  The user sees nothing.

Recommended approach for the warnings, inside `cp2_avalonia/` (no `AppCommon` change is needed
for these, and no new `CallbackFacts.Reasons` value; adding one would also affect WPF and the
CLI):
1. **Pre-flight check** before the extract dialog opens, only when the mode is Host.  Decide
   whether the output folder's volume stores forks natively.  Two ways, which can be combined:
   - `new DriveInfo(path).DriveFormat` (no P/Invoke): treat `apfs`/`hfs` as native; `msdos`,
     `exfat`, `smbfs`, `nfs`, `ntfs` and anything unknown as "emulated or unknown".
   - A probe: create a tiny temp file in the folder, write its `..namedfork/rsrc`, then see
     whether a `._probe` file appeared.  Filesystem-agnostic, but it writes to the user's
     folder and needs cleanup.
2. If not native, show **one** dialog with specific wording, e.g. "The destination volume
   (exFAT) can't store resource forks or Finder info directly.  macOS will keep them in hidden
   '._' files.  [Continue] [Use AppleDouble instead] [Cancel]".  This is a decision for the
   user, so it should not be a bare error.
3. In the extract worker's callback wrapper (`ExtractProgress.DoWork`'s `CbFunc`), when the mode
   is Host and the reason is `Failure` or `AttrFailure`, add the Host-specific hint to the text
   and make `AttrFailure` show **once per run** (a flag in `ProgressUtil.PersistentChoices`),
   with a non-empty message.  `AttrFailure` is only raised by `ExtractFileWorker` (extract and
   export sites), and `SetFileAttributes` returns false only when the Host-mode Finder-info
   write fails, so in practice this only affects Host mode.  It still departs from WPF's
   per-file box.  Pros, cons and a recommendation are in the section below.
4. For clip/drag, at minimum count the failures in `ClipTransferFiles.Extract` and show a toast
   or message ("N files could not be written with resource forks"), instead of silently
   skipping.  That is a general fix, not Host-specific.

Open questions: exact wording and button set; whether "Use AppleDouble instead" should switch
the saved mode or just apply to this run; the real `DriveFormat` strings and the `._` behaviour
on your Mac.

#### Once-per-run attribute-failure box: pros and cons

Pros: no flood of modal boxes (each blocks the worker until dismissed); the box gets real text
instead of today's blank "Failed"; a few lines in an Avalonia-only file; effectively Host-only;
fits the library's "warning only" intent.

Cons: deviates from WPF's per-file box (weak parity argument, since WPF can't reach this case);
later, different failures could be hidden unless an end-of-run count is shown; the files are
still left without type/creator; adds a new field to `PersistentChoices`; the CLI
(`cp2/Misc.cs`) handles the reason differently, so behaviours differ; it may guard a case that
never happens if macOS emulates xattrs on FAT/exFAT.

Recommendation: fill in the message, show it once per run with an end-of-run count, keep it to
the Host path.  If `setxattr` never fails on the Mac, do only the message text.

#### Preferred design (from the user): end-of-run summary with an optional details box

Instead of any box during the run, collect the failures and show **one** message when the run
finishes: "N files could not have their Finder type/creator set" with a **Details...** button
that opens a second box listing the file names, with a Copy-to-clipboard button.

Why it's better than the plain once-per-run box: the user gets the full list if they want it,
nothing blocks the worker mid-run, and it removes the "later failures get hidden" con, because
every failed file is listed.

How it would fit the existing code:
- **Collect on the worker thread.**  The callback wrapper in `ExtractProgress.DoWork` (`CbFunc`)
  sees every `AttrFailure` with `OrigPathName` set to the host path.  Add it to a list and
  return `Continue` without calling `ProgressUtil.HandleCallback` for that reason, so no modal
  box appears.  (`ExtractProgress` is Avalonia-only; no `AppCommon` change.)
- **Show after the run.**  `DoWork` currently returns a bool, and `RunWorkerCompleted(object?)`
  returns a bool for the dialog.  Return a small result object (success flag + failure list)
  instead, then show the summary on the UI thread after the progress dialog closes, in
  `MainViewModel` where `ExtractProgress` is run (~line 1617).  Show it even if the run was
  cancelled, for the files that failed before cancel.
- **Details box.**  `Tools/ShowText` already exists: a resizable read-only monospaced text
  window.  Its `DisplayText` could take the path list.  It has no Copy button (select-all and
  copy by keyboard work), so a Copy button is a small addition, or the summary box could carry
  a "Copy list" button.  An `Expander` inside one dialog would avoid a second window; the
  user asked for a second box, so that is only an alternative.
- **Wording.**  Include the cause and the consequence: the files were extracted, but their
  Finder type/creator was not set.  Combine with the pre-flight volume warning so the user is
  not told the same thing twice.
- **Reusable.**  The same collector and summary could report the clip/drag write failures that
  `ClipTransferFiles.Extract` swallows today (section 5.2/5.4), and any other partial failure,
  as a generic "completed with N problems" box.  That would be a general improvement, not
  Host-specific.

Cost: a result type, a summary dialog (view + view model), the Details/Copy wiring, and tests
for the collector and the message text.  Decided by the user: the details list is **grouped by failure type** (each type a heading with
its files indented underneath), is **not capped**, and **scrolls** when it doesn't fit.  A
monospaced text view (`ShowText`) handles this with indentation; a `TreeView` would add
collapsible groups but is more work.

**Buttons (decided by the user):**

- **Pre-flight volume warning (before the run):** **Continue** and **Cancel**, with **Cancel as
  the default** (Enter and Esc both cancel).  The text says it appears Host mode is not
  available on the destination and that exports are likely to fail.  No "use AppleDouble
  instead" button.
  - Wording caution: on macOS the kernel may emulate resource forks on FAT/exFAT in `._` files
    (section 5.4, unverified), so "likely to fail" may overstate it; the message could say
    "exports are likely to fail or lose resource forks".  Decide after the Mac test.
  - Implementation note: the existing message box (`Views/MessageBoxView`, `MBButton`
    = OK / OKCancel / YesNo / YesNoCancel) can't do this as is.  It has no custom button labels
    ("Continue"), and its `IsDefault` is bound to the OK/Yes button, so Cancel can't be the
    default.  **Decided by the user:** changing the message box is acceptable.  Add optional
    button labels and a default-button choice (touches `IDialogService`, `MessageBoxView` and
    its view model, and any tests), with existing callers unchanged.
  - **Suggested wording (not yet chosen).**  Warning icon.  Title: "Host mode may not work
    here".  Body, version A (short):
    > The destination folder is on a volume ("exFAT") that does not appear to support Mac
    > resource forks and Finder file types.  Host preservation will probably fail, or the
    > resource forks and file types may be lost or kept in hidden "._" files.
    >
    > Continue anyway?

    Version B adds a way out in the text, since there is no substitute button:
    > ... To avoid this, cancel and choose AppleDouble or AppleSingle as the preservation
    > mode, or pick a folder on a Mac-formatted (APFS or HFS+) volume.

    Use the volume name when it is known, and "could not be confirmed to support" instead of
    "does not appear to support" when the check was inconclusive.  Keep "Continue" and
    "Cancel" as the only buttons, Cancel the default.  **Chosen by the user: version B.**
- **End-of-run summary box:** **OK** and **Details...** only.  No Copy list button here.
- **Details box:** **Copy** (to the clipboard) and **Close** only.  Copying is only available
  here.  No Save button.  `ShowText` has neither button today, so it needs a Copy button and a
  Close button (or a new small window that reuses its text view).

**Details list format (decided by the user):** a plain indented list, not collapsible, so
`ShowText` can be used as is for the text.  Each failure type is a heading with its files
indented underneath.  The groups are sorted with the **fewest files first**, so rare failures
are at the top and a large common group doesn't push them out of sight.  Groups with equal
counts are ordered **alphabetically by reason** (decided by the user).  The files inside a group
keep extraction order.  Each heading shows its file count.

Nothing is left open in this section.

### 5.5 Add side: what actually changes for the user

**Measured on the Intel Mac with the built CLI** (`cp2 add`, host checks on by default there, vs
`--no-from-host`; this is the same library code the Avalonia app would use).  Test files:
`withfork` (data fork + 12-byte resource fork + Finder info TEXT/ttxt), `typed` (Finder info
TEXT/ttxt only), `plain` (nothing).

| Destination | Host checks on | Host checks off (what the Avalonia app does today) |
|---|---|---|
| ProDOS | `withfork` added as a forked file, 23 bytes (data 11 + rsrc 12), shown as `TEXT+ ttxt`; `typed` is `NON $0000`; `plain` is `NON $0000` | all three are `NON $0000`; the resource fork is dropped, `withfork` is 11 bytes |
| HFS | `withfork` forked, 23 bytes, `TEXT+ ttxt`; `typed` is `TEXT ttxt`; `plain` is `BINA CPII` (default) | all three are `BINA CPII`; resource fork dropped |

What this shows:
- **Resource fork (`CheckNamed`)** is the visible one: the fork is added alongside the data fork
  and, on ProDOS, the file becomes an extended (forked) file.  Without it the fork is silently
  lost.  A file with no fork is **not** given an empty one (`plain` stayed unforked), so the
  `File.Exists` check on `..namedfork/rsrc` does not create phantom forks.
- **Finder type/creator (`CheckFinderInfo`)** is only kept where the destination can store HFS
  types.  On HFS it is kept for every file.  On ProDOS it survived only on the forked file
  (the extended-file entry stores the HFS type and creator); the data-only `typed` file lost it
  (`NON $0000`), because ProDOS has no TEXT type to map to.  So on ProDOS the "file type only"
  combo choice mostly does nothing for data-only files.  Worth knowing when naming that choice.
- Nothing here warns the user; with the checks off the fork and types simply vanish.
With "Host" on, dragging a Mac file with a resource fork into an archive adds both forks, and
a file's Finder type/creator is used when no other scheme supplied a type (ADF, AS and NAPS have
higher priority; `ScanForHostAttr` says so).  Without it, a Finder file's resource fork is
dropped silently, which is the current behaviour.  Cost: one `getxattr` and one `File.Exists`
per added file.  This is fine for normal use; it could matter for adding tens of thousands of
files.

The manual says "mixing and matching is not advisable" (NAPS name plus `..namedfork`
checking).  The three existing check boxes are independent, so the new one raises the same
question.  The order of precedence is already fixed in `AddFileSet`; the UI should not try to
enforce exclusivity.

### 5.6 Clipboard URI list
`REVIEW_FINDINGS_Services.md` item 3 already notes that, in the Linux clipboard path, host mode
would put `..namedfork/rsrc` URIs in the list.  The drag path correctly offers top-level items
only.  On macOS the clipboard paste to Finder offers files in the same manner as drag; check
that the macOS copy path builds the URI list from top-level entries only, otherwise Finder gets
a stray `..namedfork/rsrc` item.  (Linux never reaches Host mode once 5.1 is fixed.)

### 5.7 Cross-instance copy/paste
Copy between two CiderPress II instances uses the private serialised entries, not the foreign
files, so Host does not affect it.

### 5.8 Behaviour that is *not* affected
- Import/Export (the converter mode) is unaffected.
- ADF/AS/NAPS behaviour is untouched.
- The CLI is untouched.

### 5.9 Documentation
`ndocs/gui-manual/aeie.html` says Host is CLI-only.  Update that sentence and the "Preservation
Mode" bullet when this ships.  **Decided by the user:** the GUI manual needs revisions
regardless of this feature, so it is deferred and not part of this work.

### 5.10 Import and export (converters)

**Import (host file -> converter -> Apple II file): the Host settings do not apply.**
`AddFileSet` asserts `Importer == null` before it scans for host attributes, and
`MainViewModel.ConfigureAddOpts(isImport: true)` already forces every preservation flag off
("anything stored with preserved attributes should be added, not imported").  Keep that:
`CheckNamed` and `CheckFinderInfo` stay false for imports no matter what the Host check box or
the Settings combo say, and a unit test should pin it.  The Host check box sits in the sidebar's
"Add/Import Options" section, so in Import mode it is ignored, exactly like the three existing
preservation check boxes.  Dropping or pasting files from Finder uses the same
`ConfigureAddOpts`, so the Host check box does apply there when the app is in Add mode.

**Export (Apple II file -> converter -> host file): needs a decision.**  An export writes one
converted file (a PNG, text, RTF, CSV...), never a resource fork, so the Host radio button has
nothing to do with forks.  But `ExtractFileWorker.DoExport` (line ~778) calls
`SetFileAttributes`, and in Host mode that writes `com.apple.FinderInfo` from the **source**
file's HFS type and creator onto the converted file.  A PNG tagged with a ProDOS-derived type
and creator is probably wrong, and Finder can prefer a type code over the extension.  **Measured on
the Intel Mac with the built CLI** (`cp2 export -ph` vs `-p0`, files in a scratch folder):
- Source on an HFS (GS/OS) volume: with `-ph` the exported `HardPressed.FXT.txt` got Finder info
  type `p` $B6 $00 $00, creator `pdos`; `SIZES/L1.txt` got `p` $06 $20 $00, `pdos`.  With
  `-p0` there is no Finder info.  So the CLI does tag converted files with the source's
  ProDOS-style type and creator.
- Source on a ProDOS volume (`BARS.HR`, `MULTICOLOR`): no Finder info was written at all (the
  source has no HFS type, so nothing is set).  Extract with `-ph` of the same file also sets
  none.
So the problem is real for HFS-sourced files only, and the CLI has the same behaviour.  The other modes (ADF, AS,
NAPS) appear to do nothing special for exports.  **Decided by the user:** **exports never set
the Finder info**, even with Host selected (dates and the read-only flag still apply).  The
`AppCommon` helper from 5.2 must follow the same rule on the clip/drag path: skip entries that
have an export spec.  Whether to also change `DoExport` in `AppCommon` to stop it is a separate
decision, since it would change the CLI's `cp2 export -ph` behaviour.

## 6. Testing

Automated (no Mac required):
- View model: `ExtPreserveHost` set/get, mutual exclusion with the other radio properties,
  persistence, and `RefreshFromSettings`.
- View model: `IsHostPreserveAvailable` false off macOS, and a stored `Host` coerced to `None`
  there (this needs the platform check to be injectable, or the coercion in a pure function).
- `ConfigureAddOpts`: with the host setting on, `CheckNamed` and `CheckFinderInfo` are both
  true; with `isImport` true, both are false.
- The extract-path coercion function.

Manual, macOS only (smoke-test list):
- Export a file with a resource fork and a type/creator to a folder with Host; confirm forks
  with `ls -l@` / `xattr -l` and `GetFileInfo` (or Finder's Get Info).
- Drag the same file to Finder with Host; check forks, type/creator (see 5.2) and dates.
- Copy and paste to Finder; check that no stray `..namedfork` item appears.
- Add a Finder file that has a resource fork and type/creator via drag from Finder with Host on
  and off; compare.
- Export to an exFAT or FAT volume and check the error.
- A resource-fork-only file (5.3).
- Intel x86_64 on Sequoia is the only Mac available; Apple Silicon is untested.
- Windows and Linux: confirm the controls are hidden and a copied settings file with `Host`
  does not break anything.

Existing CLI test `TestExtract.cs` already covers the library's Host extract on macOS, so the
library side has some coverage.  The clip path (5.2, 5.3) has none.

## 7. Rough size

Small and mostly mechanical for the UI and settings: one radio button, one check box, three
properties, one setting key, one `ConfigureAddOpts` line, and the platform guard.  The real work
is 5.1 (coercion), 5.2 (Finder info on the clip/drag path), 5.3 (rsrc-only files) and the macOS
manual testing.  5.2 needs a small additive change in `AppCommon` (approved, see 5.2).

## 8. Decisions

Open:
- Where the "exports never set the Finder info" rule is enforced: only in the app's own paths
  (the clip/drag helper), or also in `ExtractFileWorker.DoExport` in `AppCommon`.  The CLI has
  the same behaviour today (measured, see 5.10), so changing `DoExport` also changes
  `cp2 export -ph`.

Decided:
- Exports never set the Finder info, even with Host selected (see 5.10).
- Add side: **one "Host" check box** in the sidebar, plus a **Host area in the Settings dialog**
  with a combo box (resource fork and file type [default] / resource fork only / file type
  only) that decides which of `CheckNamed` and `CheckFinderInfo` the check box turns on.
- On **Windows and Linux** all host settings are ignored internally, whatever they contain.
- Default: **off**, for both the Add check box and the Extract radio button.
- Pre-flight warning wording: **version B** (see 5.4), with the volume name when known.
- Finder type/creator step on the clip/drag path may live in `AppCommon` (additive only).
- The message box may be extended (button labels, default button).
- `ndocs/gui-manual` is deferred; it needs revision regardless.
- Pre-flight box: Continue / Cancel, Cancel default.  End-of-run summary: OK / Details...
  Details box: Copy / Close.  Details list: indented, fewest files first, ties alphabetical,
  no cap, scrolls.

## 9. AppCommon changes to make

`AppCommon` is shared with the WPF app and the CLI, so every item must keep their results the
same except where stated.  The existing CLI tests (`cp2/Tests/TestExtract.cs`, Host cases on
macOS) must still pass.  Approved by the user: the first item.  The second follows from the
"exports never set the Finder info" decision, and is listed here on the understanding that
"note the AppCommon changes" includes it; it changes `cp2 export -ph`, so say if that is not
wanted.

1. **Shared Finder-info helper.**  Move the `setxattr` block in
   `ExtractFileWorker.SetFileAttributes` (line ~959) into a public static helper, and call it
   from `ExtractFileWorker` (no behaviour change) and from the clip/drag writer.  Today the
   clip/drag path never sets the Finder type and creator (5.2).
2. **Exports must not set the Finder info.**  `ExtractFileWorker.DoExport` (line ~778) calls
   `SetFileAttributes` in Host mode and tags converted files with the source's HFS type and
   creator (measured, 5.10).  Skip the Finder-info part for exports.  Dates and the read-only
   flag stay.  The clip/drag helper from item 1 must skip export entries too.
3. **Resource-fork-only entries on the clip/drag path (verify first).**  `ClipFileSet`'s Host
   case writes the `..namedfork/rsrc` entry but only creates the data-fork entry when there is
   one.  Writing the fork of a file that doesn't exist probably fails on macOS (5.3, not
   tested).  Test on the Mac with a resource-fork-only file; if it fails, create the empty data
   file first, as `ExtractFileWorker` already does for the direct extract.
4. **Attribute-failure message (optional).**  The two `AttrFailure` callbacks
   (`ExtractFileWorker` lines ~617 and ~778) leave `FailMessage` empty, so every consumer that
   shows it shows a blank message.  The planned Avalonia summary only needs the path, so this
   can wait; setting a default message would also help the CLI and WPF.

5. **HFS `TEXT` should map to ProDOS `TXT` on a plain ProDOS file (likely bug).**  In 5.5 the
   data-only Finder-typed file became `NON $0000` on a ProDOS disk.  `NON` is an accident, not a
   deliberate default.  `AddFileWorker.CopyAttributes` (line ~684) has the comment "just a handy
   conversion (any 'TEXT' becomes TXT)" and `FileAttribs.ProDOSFromHFS` does map `TEXT` to TXT
   for any creator.  But the call passes `newEntry.HFSFileType` and `newEntry.HFSCreator`
   (values read back from the new entry), and a plain ProDOS entry has nowhere to store HFS
   types: `ProDOS_FileEntry.HFSFileType` returns 0 when there is no extended-file info, and its
   setter silently does nothing.  So `ProDOSFromHFS(0, 0)` yields NON.  On HFS destinations the
   read-back works, which is why only ProDOS (and any other format with ProDOS types but no HFS
   storage) is affected.  Suggested fix: pass `addEnt.HFSFileType` / `addEnt.HFSCreator` (the
   values that were actually found) to `ProDOSFromHFS` when the entry can't hold them.  It
   changes WPF and CLI results too (an HFS-`TEXT` file from an AppleDouble or AppleSingle file
   added to ProDOS would become TXT instead of NON), so it is best raised with Andy McFadden as
   a probable upstream bug.  Measured with the CLI for all three sources (Finder info,
   AppleDouble, AppleSingle): the data-only `TEXT`/`ttxt` file becomes `NON $0000` on ProDOS
   each time, while the same AppleDouble and AppleSingle files added to an HFS disk keep
   `TEXT ttxt` (so the type is in the files and is lost only by the ProDOS mapping).  Forked
   files keep it, because the extended-file entry can store it.  See section 10.  Caveat: the mapping sets the type
   only; it does not convert LF line endings to the CR that Apple II text expects.

## 10. Items to raise with Andy McFadden

Findings in shared code (`AppCommon`, `DiskArc`, manual) that affect the CLI and WPF too, so they
are better discussed upstream than patched quietly in this port.  Measured on the Intel Mac
with the built `cp2` CLI; none of these were found in the Avalonia code.

1. **HFS `TEXT` type becomes `NON $0000`, not `TXT`, on plain ProDOS files (likely bug).**
   - Intended behaviour: `AddFileWorker.CopyAttributes` comment "any 'TEXT' becomes TXT";
     `FileAttribs.ProDOSFromHFS` maps `TEXT` to TXT for any creator.
   - Cause: the call passes the new entry's `HFSFileType`/`HFSCreator`, which a plain
     `ProDOS_FileEntry` can't store (getter returns 0, setter is a no-op), so the input is
     (0, 0) and the result is NON.
   - Reproduction: a file with `TEXT`/`ttxt` and no resource fork, added to a ProDOS image.
     Same result from a Finder-typed host file (`add`), an AppleDouble pair (`add` of the
     extracted data file plus `._` header), and an AppleSingle `.as` file.  The same files
     added to an HFS image keep `TEXT ttxt`, which shows the type reached the library.
   - Suggested fix: pass `addEnt.HFSFileType`/`addEnt.HFSCreator` when the entry can't hold
     them.
2. **`export -ph` tags converted files with the source's HFS type and creator.**  Converting a
   file from a GS/OS HFS volume gave a `.txt` tagged type `p`$B6$00$00, creator `pdos`.  Probably
   unintended: the output is a PNG/text/RTF file, not a ProDOS file.  Proposed: skip the Finder
   info for exports (section 9, item 2).
3. **Blank attribute-failure message.**  The two `AttrFailure` callbacks in
   `ExtractFileWorker` (lines ~617 and ~778) never set `FailMessage`, so a GUI that shows it
   shows an empty box.
4. **Manual typo.**  `ndocs/Manual-cp2.md` (and `docs/Manual-cp2.md`), the heading near line
   1466 reads "`--from-host` (default), `--no-host`", but the option is `--no-from-host`.
5. **Design question: line endings when a `TEXT` file is added.**  Once item 1 is fixed, a
   `TEXT` file added to ProDOS is typed TXT but its bytes are untouched, so a host file with
   LF endings is labelled as Apple II text and still has LF.  Measured: `add` of an LF file
   gives `NON` and LF bytes; `import ... text` gives `TXT` and CR bytes.  So the conversion
   already exists as the `text` import converter.  Questions for Andy: is typing the file TXT
   without converting the right behaviour for Add (Add stays byte-for-byte), and does he want
   anything beyond "use Import for text files"?

My view on item 5 (not decided): leave Add byte-for-byte.  Files typed `TEXT` by classic Mac
software are already CR, so converting would not help them; modern macOS rarely sets a `TEXT`
type at all; and conversion would also break a mistyped binary file.  The Import mode's `text`
converter is the right tool.  A new Settings option in the Avalonia app, or a CLI switch such
as a `--text-eol` option, would duplicate it and change what "Add" means, so I'd only add one
if Andy asks for it.

## 11. Avalonia work list

Everything below is in `cp2_avalonia/` and `cp2_avalonia.Tests/`.

1. **Add side: one Host check box** (sidebar, "Preservation Handling" group), default off, plus
   the Settings-dialog Host area with the combo (resource fork and file type / resource fork
   only / file type only).  It is needed, not optional: the measurements in 5.5 show that
   without it a Finder file's resource fork is silently dropped on every add.
2. **Extract side: a Host radio button** in "Preservation Mode".  It is independent of the Add
   check box; it only controls what extraction and drag-out write.
3. **Central "effective value" rule** so Windows and Linux ignore all host settings (5.1);
   controls hidden except on macOS.
4. **Import stays off.**  `ConfigureAddOpts(isImport: true)` keeps forcing the Host flags false;
   add a test (5.10).
5. **Clip/drag path:** call the new `AppCommon` helper after writing Host files, skipping
   export entries; stop swallowing write errors in `ClipTransferFiles.Extract` and count them.
6. **Pre-flight volume warning** (version B wording, Continue / Cancel, Cancel default) for
   Extract to a folder in Host mode, which needs the message box extended (button labels and
   default button).
7. **End-of-run summary and details box:** collect attribute failures in the extract callback
   wrapper, show a summary (OK / Details...) after the run, and a details box (Copy / Close)
   with an indented list grouped by reason, fewest files first, ties alphabetical, no cap,
   scrolling.
8. **Tests:** effective-value rule, `ConfigureAddOpts` flags for each combo choice and for
   import, the radio group, the grouping and sort of the details list, the message-box button
   changes.  macOS smoke tests as listed in section 6, plus the 5.3 resource-fork-only case.
