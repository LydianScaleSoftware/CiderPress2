# Iteration 7 Blueprint: File Viewer with AvaloniaEdit

> **First:** Read `cp2_avalonia/guidance/Pre-Iteration-Notes.md` to familiarize yourself with the
> project context, conventions, and architecture decisions before proceeding.

---

## Goal

Port the File Viewer dialog — the most complex dialog in the application. It allows viewing
files in text, hex-dump, and bitmap (image) modes, with format converters, search, export,
and prev/next navigation. The WPF version uses both a plain `TextBox` and a `RichTextBox`
for rich text; in Avalonia, both are replaced by `AvaloniaEdit.TextEditor`.

---

## Prerequisites

- Iteration 6 is complete: extract/add working.
- Key WPF source files to read (read thoroughly — this is the hardest dialog):
  - `cp2_wpf/FileViewer.xaml` — note: at root of cp2_wpf, NOT in Tools/
  - `cp2_wpf/FileViewer.xaml.cs` (~1,374 lines) — the entire file, at root of cp2_wpf
  - `cp2_wpf/ConfigOptCtrl.cs` — converter option control mapping class
  - `FileConv/FancyText.cs`
  - `FileConv/RTFGenerator.cs` — understand the RTF pipeline you are replacing
  - `cp2_wpf/WinUtil.cs` — search for `ConvertToBitmapSource`
  - `cp2_wpf/MainController.cs` — search for `ViewFiles` method

  > The WPF FileViewer lives at the root of `cp2_wpf/`, not in a `Tools/` subdirectory.
  > The Avalonia port places it in `cp2_avalonia/Tools/` as an organizational improvement.

---

## Architecture Overview

The WPF FileViewer uses three overlapping controls switched by visibility:
1. **TextBox** — for simple/plain text (monospace)
2. **RichTextBox** — for FancyText (RTFGenerator → RTF → RichTextBox.Load)
3. **ScrollViewer + Image** — for bitmap display

In Avalonia, this becomes:
1. **AvaloniaEdit `TextEditor`** — for both simple and fancy text (read-only mode)
2. **ScrollViewer + Avalonia `Image`** — for bitmap display

The cross-platform `FancyText` model (in `FileConv/`) provides annotations (bold, italic,
font changes, colors, etc.) as position-based metadata. The WPF version converts this to
RTF via `RTFGenerator` and loads it into RichTextBox. For Avalonia, you must write a new
converter that applies `FancyText` annotations directly as AvaloniaEdit highlighting.

---

## Step-by-Step Instructions

### Step 0: Add `ViewerMonoFont` and `CheckerBackground` Resources to `App.axaml`

Both resources are defined in WPF's `App.xaml` (lines 32 and 38) and referenced by
`FileViewer.axaml` in Steps 1 and 4. Neither was added in any prior iteration.

Add to `cp2_avalonia/App.axaml`, inside `<Application.Resources>`:
```xml
<FontFamily x:Key="ViewerMonoFont">Consolas</FontFamily>

<!-- Checkerboard background for bitmap image viewer -->
<DrawingBrush x:Key="CheckerBackground" TileMode="Tile"
              DestinationRect="0,0,16,16">
    <DrawingBrush.Drawing>
        <DrawingGroup>
            <GeometryDrawing Geometry="M0,0 H2 V2 H0Z" Brush="#e8e8e8"/>
            <GeometryDrawing Geometry="M0,0 H1 V1 H2 V2 H1 V1 H0Z" Brush="#f0f0f0"/>
        </DrawingGroup>
    </DrawingBrush.Drawing>
</DrawingBrush>
```

**Avalonia translation notes:** WPF uses `Viewport="0,0,16,16" ViewportUnits="Absolute"`;
Avalonia uses `DestinationRect="0,0,16,16"` (absolute pixel units by default).
`ViewportUnits` does not exist in Avalonia. The geometry coordinates are in `[0,2]×[0,2]`
units and scale to the 16×16 tile via the DrawingBrush's implicit transform.

Without these resources, `FileViewer.axaml` will throw `ResourceNotFoundException`
at parse time.

### Step 1: Create `cp2_avalonia/Tools/FileViewer.axaml`

Port the layout from `cp2_wpf/FileViewer.xaml` (289 lines, at root of cp2_wpf).
The window is 801×700, MinWidth=700, MinHeight=400.

Overall structure — a Grid with 2 rows × 3 columns:
- **Columns 0 and 2, spanning rows 0-1:** Prev (`<`) and Next (`>`) navigation buttons.
  Bound to `HasPrevFile`/`HasNextFile` for `IsEnabled`.
- **Row 0, Column 1:** A `TabControl` with `TabStripPlacement="Bottom"`, three tabs:
  - **"Data Fork"** tab — contains:
    - `avaloniaEdit:TextEditor` (Name="dataForkTextEditor") — readonly, mono font,
      visible when showing text (simple or fancy). Bind `IsVisible` to `IsTextVisible`.
    - `ScrollViewer` > `Border` (Background=CheckerBackground) > `Image`
      (Name="previewImage") — visible when showing bitmaps. Bind `IsVisible` to
      `IsBitmapVisible`. Use `BitmapInterpolationMode.None` for pixel-art rendering.
  - **"Resource Fork"** tab — single `avaloniaEdit:TextEditor` (readonly, mono font)
  - **"Notes"** tab — single `avaloniaEdit:TextEditor` (readonly, mono font)
- **Row 1, Column 1:** Bottom panel with conversion controls (left) and view controls
  (right):
  - **Left (Conversion):** ComboBox for converter selection (width ~240), GroupBox
    "Options" with dynamic controls (3 CheckBoxes, 1 TextBox input, 2 RadioButton groups
    of 4 each), "Save as Default Configuration" button.
  - **Right (View):** Three buttons (Text, Hex Dump, Best), find bar (TextBox + Prev/Next
    buttons), graphics zoom (Slider 0-4 snap-to-tick + TextBox), "Open raw" CheckBox,
    bottom row: Done/Export/Copy buttons.

Key AXAML differences from WPF:
```xml
<!-- Namespace for AvaloniaEdit -->
xmlns:avaloniaEdit="clr-namespace:AvaloniaEdit;assembly=AvaloniaEdit"

<!-- TextEditor (replaces both TextBox and RichTextBox) -->
<avaloniaEdit:TextEditor Name="dataForkTextEditor"
    IsReadOnly="True"
    FontFamily="{StaticResource ViewerMonoFont}"
    FontSize="13"
    ShowLineNumbers="False"
    WordWrap="True"
    IsVisible="{Binding IsTextVisible}" />

<!-- Image with pixel-art scaling -->
<Image Name="previewImage"
    RenderOptions.BitmapInterpolationMode="None"
    Source="{Binding PreviewBitmap}" />
```

For the tab header bold-when-enabled styles, the WPF version uses `DataTrigger`-based
styles (`boldHeaderTextBlock`, `boldHeaderText`). In Avalonia, convert these to
`Styles` with `Selector` pseudo-classes (e.g., `:disabled` selector to unbold).

For the find bar, bind Enter key to find-next:
```xml
<TextBox Name="findTextBox" KeyDown="FindTextBox_KeyDown" />
```

### Step 2: Create `cp2_avalonia/Tools/FileViewer.axaml.cs`

Port from `cp2_wpf/FileViewer.xaml.cs` (~1,374 lines, at root of cp2_wpf).
This is a large, careful port.

**Property changes:**
- Replace `Visibility` enum properties (`SimpleTextVisibility`, `FancyTextVisibility`,
  `BitmapVisibility`) with two `bool` properties: `IsTextVisible`, `IsBitmapVisible`.
- `DataPlainText` / `RsrcPlainText` / `NotePlainText` → set via
  `textEditor.Document = new TextDocument(text)` rather than binding strings.
- Image property: `PreviewBitmap` of type `Avalonia.Media.Imaging.Bitmap?`

**Display type system:**
```csharp
private enum DisplayItemType { SimpleText, FancyText, Bitmap }

private void SetDisplayType(DisplayItemType type) {
    IsTextVisible = (type != DisplayItemType.Bitmap);
    IsBitmapVisible = (type == DisplayItemType.Bitmap);
}
```

**FormatFile() changes — the core method:**
- **SimpleText / ErrorText:** Set `dataForkTextEditor.Document = new TextDocument(text)`
  with no highlighting.
- **FancyText:** Call new `ApplyFancyText()` method (see Step 3) which populates the
  TextEditor document and applies highlighting.
- **CellGrid:** Use `CSVGenerator.GenerateString()` → plain text in TextEditor.
- **IBitmap at various zooms:** See Step 4.
- **HostConv (GIF/JPEG/PNG):** Use `new Bitmap(stream)` to decode standard image formats.
- **HostConv (PDF/RTF/Word):** Launch external viewer via `Process.Start` with temp file
  (same as WPF). Port the temp file lifecycle management: `mTmpFiles` list,
  `LaunchExternalViewer()`, `DeleteTempFiles()` (called on window close), and the static
  `FindStaleTempFiles()` method.

**Error display note:** The WPF version centers error text in the TextBox via
`HorizontalContentAlignment`/`VerticalContentAlignment`. AvaloniaEdit's TextEditor
does not have direct content alignment — use left-aligned error text, or wrap in a
centered container for error messages.

**`SelectEnabledTab()` method:** Port this ~15-line utility that switches to an enabled
tab when the current one becomes disabled (checks each tab's enabled state in priority
order: Data → Rsrc → Note).

> ⚠️ **Temporal binding risk (Pitfall #11):** The `Init()` method sets all
> AXAML-bound properties (`IsTextVisible`, `IsBitmapVisible`, `HasPrevFile`,
> `HasNextFile`, `PreviewBitmap`, converter lists, `IsSaveDefaultsEnabled`) after
> construction. Because `DataContext = this` is set in the constructor, `Init()` MUST
> use **property setters** (not backing fields) so that `OnPropertyChanged` fires for
> every bound property. If backing fields are set directly, the bindings will still
> show their default values.

**`Init()` must call `ShowFile(true)` at the end.** The WPF version wires
`SourceInitialized="Window_SourceInitialized"` which calls `magnificationSlider.Value = 1`,
`UpdatePrevNextControls()`, and `ShowFile(true)`. Avalonia has no `SourceInitialized`
event. Place these calls at the end of `Init()` — this runs after `InitializeComponent()`
and after all fields are set, which is the equivalent timing.

**No Dispatcher calls needed:** The WPF FileViewer has zero Dispatcher calls — everything
runs on the UI thread. Do not add unnecessary Dispatcher.UIThread.InvokeAsync wrappers.

**Dialog results:**
- Modal dialog opened via `dialog.ShowDialog(parentWindow)`
- Close with `this.Close()`

### Step 3: Create `cp2_avalonia/Tools/FancyTextHelper.cs`

This is the NEW converter that replaces the RTFGenerator→RichTextBox pipeline. It
converts `FancyText` annotations into AvaloniaEdit styled text.

```csharp
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Avalonia.Media;
using FileConv;

namespace cp2_avalonia.Tools;

/// <summary>
/// Converts FancyText annotations into AvaloniaEdit TextDocument with highlighting.
/// </summary>
public static class FancyTextHelper {
    /// <summary>
    /// Populates a TextEditor with FancyText content and formatting.
    /// </summary>
    public static void Apply(AvaloniaEdit.TextEditor editor, FancyText fancyText) {
        string plainText = fancyText.Text.ToString();
        editor.Document = new TextDocument(plainText);

        // Create and register a custom line transformer for formatting
        var transformer = new FancyTextTransformer(fancyText);
        editor.TextArea.TextView.LineTransformers.Clear();
        editor.TextArea.TextView.LineTransformers.Add(transformer);
    }
}

/// <summary>
/// AvaloniaEdit line transformer that applies FancyText annotations as visual formatting.
/// </summary>
internal class FancyTextTransformer : DocumentColorizingTransformer {
    private readonly List<FancyText.Annotation> mAnnotations;

    // Running formatting state — carried across ColorizeLine() calls.
    // Reset before displaying a new document via ResetState().
    private int mAnnotationIndex;
    private bool mBold;
    private bool mItalic;
    private bool mUnderline;
    private IBrush mForeground = Brushes.Black;
    // (add mFontFamily, mFontSize as needed)

    public FancyTextTransformer(FancyText fancyText) {
        mAnnotations = new List<FancyText.Annotation>(fancyText);
    }

    /// <summary>Call before the first ColorizeLine to reset running state.</summary>
    public void ResetState() {
        mAnnotationIndex = 0;
        mBold = false;
        mItalic = false;
        mUnderline = false;
        mForeground = Brushes.Black;
    }

    protected override void ColorizeLine(DocumentLine line) {
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        // Annotations are state transitions at character offsets, NOT ranges.
        // The cursor (mAnnotationIndex) advances through the sorted list as
        // ColorizeLine is called in order.  State accumulated from prior lines
        // is already in the instance fields.
        //
        // Process all annotations whose Offset falls within [lineStart, lineEnd).
        // After each state change, apply accumulated state to the remainder of
        // the line via ChangeLinePart.
        //
        // Annotation.ExtraLen records how many characters the Append*() call
        // inserted (e.g. NewParagraph inserts \r\n, ExtraLen=2).  It is used
        // by RTFGenerator for offset re-sync.  Do NOT use ExtraLen as a
        // formatting span length — it would mis-apply formatting to inserted
        // whitespace and cascade offset errors.
        //
        // Exotic annotation types (NewPage, Tab, Superscript, Subscript,
        // Outline, Shadow, Justification, LeftMargin, RightMargin) have no
        // AvaloniaEdit equivalent and must be silently skipped.
        //
        // Correct AvaloniaEdit API for formatting (NO Set*() methods):
        //   element.TextRunProperties.ForegroundBrush = new SolidColorBrush(color);
        //   element.TextRunProperties.Typeface = new Typeface(
        //       element.TextRunProperties.Typeface.FontFamily,
        //       FontStyle.Normal,
        //       mBold ? FontWeight.Bold : FontWeight.Normal);
        //   element.TextRunProperties.FontRenderingEmSize = size;
    }
}
```

**Implementation notes for the transformer:**
- Annotations are state transitions at offsets, NOT ranges. Each `FancyText.Annotation`
  records a formatting *change* (e.g., `AnnoType.Bold` with `data = true` means "bold
  starts here"). There is no end offset — the end is implied by the next annotation of
  the same type.
- Maintain running formatting state (`mBold`, `mForeground`, etc.) as instance fields.
  `ColorizeLine()` is called in document order — the cursor (`mAnnotationIndex`)
  advances through annotations across calls. Call `ResetState()` before displaying a
  new document.
- Use `ChangeLinePart(start, end, action)` to apply visual properties
- Correct AvaloniaEdit property-assignment API (there are NO `Set*()` methods):
  - `element.TextRunProperties.ForegroundBrush = new SolidColorBrush(color)` for colors
  - `element.TextRunProperties.Typeface = new Typeface(family, style, weight)` for bold/font
  - `element.TextRunProperties.FontRenderingEmSize = size` for size changes
- `Annotation.ExtraLen` is for RTFGenerator offset re-sync; do NOT use it as a span length
- Silently skip exotic annotation types (NewPage, Tab, Superscript, Subscript, Outline,
  Shadow, Justification, LeftMargin, RightMargin) — AvaloniaEdit has no equivalents
- This replaces the entire `RTFGenerator` → `RichTextBox.Load(rtfStream)` pipeline

### Step 4: Bitmap Display & Magnification

Port the magnification system from `ConfigureMagnification()`:

The zoom slider has 5 ticks: 0=0.5×, 1=1×, 2=2×, 3=3×, 4=4×.

**Key changes from WPF:**
- Replace `WinUtil.ConvertToBitmapSource(IBitmap)` (in `cp2_wpf/WinUtil.cs`) with a new
  Avalonia conversion method. The WPF version has **two pixel format branches**:
  - **Bitmap8** (indexed color): creates a BitmapPalette from `GetColors()`, uses
    `PixelFormats.Indexed8`, stride = width
  - **Direct color**: uses `Bgra32`, stride = (width × bitsPerPixel + 7) / 8

  Avalonia's WriteableBitmap does not support indexed palette formats. For Bitmap8,
  expand the palette-indexed pixels to BGRA before writing to the WriteableBitmap:
  ```csharp
  // In cp2_avalonia/Common/BitmapUtil.cs:
  public static Avalonia.Media.Imaging.Bitmap ConvertToBitmap(IBitmap src) {
      if (src.IsIndexed8) {
          // Expand indexed pixels to BGRA using GetColors() palette
          int[] palette = src.GetColors()!;
          byte[] indexed = src.GetPixels();
          byte[] bgra = new byte[src.Width * src.Height * 4];
          for (int i = 0; i < indexed.Length; i++) {
              int argb = palette[indexed[i]];
              int j = i * 4;
              bgra[j + 0] = (byte)(argb);       // B
              bgra[j + 1] = (byte)(argb >> 8);   // G
              bgra[j + 2] = (byte)(argb >> 16);  // R
              bgra[j + 3] = (byte)(argb >> 24);  // A
          }
          // Create WriteableBitmap with Bgra8888, AlphaFormat.Unpremul from expanded data
      } else {
          // Direct color: GetPixels() returns BGRA data directly
          // Create WriteableBitmap with Bgra8888, AlphaFormat.Unpremul
      }
      // IMPORTANT: When writing to WriteableBitmap via Lock()/Framebuffer,
      // the framebuffer may have padding (fb.RowBytes != width * 4).
      // Copy row-by-row: Marshal.Copy(src, 0, fb.Address + y * fb.RowBytes, width * 4)
  }
  ```
- For zoom >1×: Use `IBitmap.ScaleUp(factor)` then convert (same as WPF)
- For zoom <1×: Convert at full size, let Avalonia scale with
  `BitmapInterpolationMode.LowQuality`
- For zoom =1×: Add +1 pixel trick to avoid NearestNeighbor edge artifacts
- Set `RenderOptions.BitmapInterpolationMode="None"` on the Image control for pixel-art
  display

**`ConfigureMagnification()` has two distinct branches:**
- **HostConv images**: The bitmap is already decoded and set on `previewImage.Source`;
  magnification just resizes width/height on the existing image.
- **IBitmap images**: Must call `ConvertToBitmap()` (with optional `ScaleUp()`) and set
  the result as the image source.

**HostConv images (GIF/JPEG/PNG):**
```csharp
var bitmap = new Avalonia.Media.Imaging.Bitmap(memoryStream);
PreviewBitmap = bitmap;
```

**Animated GIF:** `cp2_wpf` has `AnimatedGifEncoder.cs`, but it is used
by `MainController.DoConvertANI()`, not by FileViewer. Deferred to **Iteration 15**
(Polish & Packaging). FileViewer itself does not need it.

### Step 5: Find/Search System

Port the find system. The WPF version dispatches to `FindInTextBox()`
(simple `string.IndexOf` with selection) or `FindInRichTextBox()` (complex
`TextPointer` traversal across RTF runs — cannot match across run boundaries).

AvaloniaEdit unifies both find paths: since both SimpleText and FancyText use the same
TextEditor control, a single `string.IndexOf`-based search works for both. This
eliminates the WPF RichTextBox run-boundary limitation where search couldn't find
text spanning multiple formatting runs.

For AvaloniaEdit, use a simple text search:
```csharp
private void DoFind(bool forward) {
    string searchText = SearchString;
    if (string.IsNullOrEmpty(searchText)) return;

    var editor = dataForkTextEditor; // Select active tab's editor:
    // var editor = tabControl.SelectedIndex switch {
    //     0 => dataForkTextEditor,
    //     1 => rsrcForkTextEditor,
    //     _ => noteTextEditor
    // };
    // The hardcoded `dataForkTextEditor` only searches the Data Fork tab.
    // When the user is on the Resource Fork or Notes tab and presses F3,
    // the search runs on the invisible Data Fork content instead of the
    // current tab. Use the switch expression above to search the active tab.
    var doc = editor.Document;
    // For forward search, start at CaretOffset (end of previous selection).
    // For backward search, use editor.SelectionStart (start of current selection)
    // to avoid re-finding the same match.  CaretOffset - 1 is wrong because after
    // a forward find, CaretOffset = index + len, and CaretOffset - 1 falls inside
    // the current match, causing LastIndexOf to find it again.
    int startPos = forward
        ? editor.CaretOffset
        : Math.Max(0, editor.SelectionStart - 1);

    // Simple string search in document text
    string text = doc.Text;
    int index;
    if (forward) {
        index = text.IndexOf(searchText, startPos,
            StringComparison.OrdinalIgnoreCase);
        if (index < 0) // wrap
            index = text.IndexOf(searchText, 0, StringComparison.OrdinalIgnoreCase);
    } else {
        index = text.LastIndexOf(searchText, startPos,
            StringComparison.OrdinalIgnoreCase);
        if (index < 0) // wrap
            index = text.LastIndexOf(searchText, text.Length - 1,
                StringComparison.OrdinalIgnoreCase);
    }

    if (index >= 0) {
        editor.Select(index, searchText.Length);
        editor.CaretOffset = index + searchText.Length;
        editor.ScrollTo(editor.TextArea.Caret.Line, editor.TextArea.Caret.Column);
    }
}
```

### Step 6: Export & Copy

Port export and clipboard operations:

**Export:** Replace `SaveFileDialog` with Avalonia `StorageProvider.SaveFilePickerAsync`:
```csharp
var topLevel = TopLevel.GetTopLevel(this);
var file = await topLevel!.StorageProvider.SaveFilePickerAsync(
    new FilePickerSaveOptions {
        Title = "Export...",
        SuggestedFileName = defaultName,
        FileTypeChoices = new[] {
            new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } },
            new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } },
        }
    });
if (file != null) {
    using var stream = await file.OpenWriteAsync();
    CopyViewToStream(stream);
}
```

**Copy to clipboard:** Use Avalonia clipboard:
```csharp
var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
if (clipboard != null) {
    if (currentMode == DisplayItemType.SimpleText ||
        currentMode == DisplayItemType.FancyText) {
        await clipboard.SetTextAsync(textContent);
    }
    // For images, Avalonia clipboard bitmap support varies by platform
}
```

> **FancyText export format:** `RTFGenerator.Generate()` is pure C# with no WPF
> dependency — reuse it for file export. The file picker should offer `.rtf` when the
> current display is FancyText. For clipboard, `DataFormats.Rtf` is WPF-only;
> Avalonia's portable clipboard is text-only, so fall back to
> `clipboard.SetTextAsync(doc.Text)` (plain text) for FancyText clipboard copy.

> **Export error handling:** The WPF `ExportButton_Click` has a `MessageBox.Show()` on
> failure. Replace with Avalonia `ShowDialog` using the `MBButton`/`MBIcon`/`MBResult`
> enums from `cp2_avalonia/Common/MessageBoxEnums.cs` (defined in Iteration 3, Step 2).

**CellGrid clipboard:** The WPF version puts both CSV (`DataFormats.CommaSeparatedValue`)
and UnicodeText on the clipboard for CellGrid output. Avalonia's clipboard API is
text-only for portable use; place the CSV string as plain text. If richer clipboard
formats are needed later, use platform-specific clipboard APIs.

### Step 7: Dynamic Converter Options Controls

Port the option control system. This requires porting `cp2_wpf/ConfigOptCtrl.cs` (~423
lines), which is a substantial standalone class with:
- Abstract base `ControlMapItem` (with `INotifyPropertyChanged`, visibility binding,
  `AssignControl`/`HideControl` methods)
- Three concrete subclasses: `ToggleButtonMapItem` (CheckBox/RadioButton),
  `TextBoxMapItem` (label + text input), `RadioButtonGroupItem` (GroupBox with 4 buttons)
- Static utility methods: `LoadExportOptions()`, `ConfigureControls()`,
  `HideConvControls()`, `FindFirstAvailable()`

> **`IsAvailable` inversion warning:** The WPF property
> `IsAvailable { get => ItemVis != Visibility.Visible; }` returns `true` when the
> control is **hidden** (i.e., "available for assignment"). The Avalonia port must
> translate to `!IsVisible`. An agent inverting this makes `FindFirstAvailable()`
> return already-assigned (visible) controls instead of free (hidden) ones.

The WPF version uses programmatic `Binding` objects (`new Binding("BoolValue")
{ Source = this }` → `SetBinding(ToggleButton.IsCheckedProperty, binding)`). These are
WPF-only (`System.Windows.Data.Binding`, `FrameworkElement.VisibilityProperty`,
`SetBinding()`). **Drop all programmatic bindings.** Instead, update control properties
imperatively in `AssignControl()` and `HideControl()`:
- `ToggleButtonMapItem.AssignControl()`: set `ctrl.IsChecked = BoolValue` and subscribe
  to `ctrl.IsCheckedChanged` to write back.
- `ControlMapItem.AssignControl()`: set `visElem.IsVisible = true`.
- `ControlMapItem.HideControl()`: set `visElem.IsVisible = false`.
- Since `INotifyPropertyChanged` auto-update is lost, call `ConfigureControls()`
  explicitly after every converter option change.

Also port the `IsSaveDefaultsEnabled` property and `SaveDefaultsButton_Click` handler
. This compares the current option string against app settings and
enables/disables the "Save as Default Configuration" button.

In Avalonia:
- The 3 CheckBoxes, TextBox, and RadioButton groups are defined in AXAML (same as WPF)
- `ConfigureControls()` iterates converter options and sets `IsVisible`/`Content`/`IsChecked`
  on each control
- Replace `Visibility.Visible`/`Collapsed` → `IsVisible = true`/`false`
- Control map items reference controls by `Name` → use `this.FindControl<T>("name")`

### Step 8: Wire ViewFiles Command

In `MainController.cs`, ensure `ViewFiles()` creates the dialog correctly:

```csharp
public async Task ViewFiles() {
    if (!GetFileSelection(omitDir:true, omitOpenArc:true, closeOpenArc:false,
            oneMeansAll:true, out object? archiveOrFileSystem, out IFileEntry unusedDir,
            out List<IFileEntry>? selected, out int firstSel)) {
        return;
    }
    if (selected.Count == 0 || firstSel < 0) {
        // Show info dialog
        return;
    }
    var dialog = new FileViewer();
    // Init signature mirrors the WPF constructor parameters:
    //   void Init(Window owner, object archiveOrFileSystem, List<IFileEntry> selected,
    //             int firstSel, AppHook appHook)
    dialog.Init(mMainWin, archiveOrFileSystem, selected, firstSel, AppHook);
    await dialog.ShowDialog<object?>(mMainWin); // Avalonia modal — must be awaited
}
```

> **`ShowDialog` must be awaited.** `ShowDialog<T>()` in Avalonia returns `Task<T>`.
> Without `await`, the dialog opens non-modally and `ViewFiles()` returns immediately.
> The calling `RelayCommand` lambda must be `async void` with a try/catch wrapper
> (per Iteration 6 pattern).

> **`MessageBox.Show()` for the empty-selection case** has no Avalonia equivalent.
> Replace with Avalonia `ShowDialog` using the `MBButton`/`MBIcon`/`MBResult` enums
> from `cp2_avalonia/Common/MessageBoxEnums.cs` (defined in Iteration 3, Step 2).
```

Also wire double-click on file list to `ViewFiles()` (from Iteration 4, make sure
`HandleFileListDoubleClick()` calls `ViewFiles()` when appropriate).

---

## Files Created/Modified in This Iteration

| Action | File |
|---|---|
| **Create** | `cp2_avalonia/Tools/FileViewer.axaml` |
| **Create** | `cp2_avalonia/Tools/FileViewer.axaml.cs` |
| **Create** | `cp2_avalonia/Tools/FancyTextHelper.cs` |
| **Create** | `cp2_avalonia/Tools/ConfigOptCtrl.cs` (port of cp2_wpf/ConfigOptCtrl.cs, ~423 lines) |
| **Create** | `cp2_avalonia/Common/BitmapUtil.cs` (IBitmap→Avalonia Bitmap, handles both Bitmap8 and direct color) |
| **Modify** | `cp2_avalonia/MainController.cs` (ViewFiles method) |
| **Modify** | `cp2_avalonia/MainWindow.axaml.cs` (wire view commands) |

---

## Verification Checklist

- [ ] `dotnet build` succeeds
- [ ] Double-clicking a text file in the file list opens the File Viewer
- [ ] Plain text files display correctly with mono font
- [ ] FancyText files (e.g., AppleWorks word processor) show bold/color formatting
- [ ] Hex dump mode works (Text → Hex Dump button)
- [ ] Image files display with checkerboard background
- [ ] Zoom slider scales images (0.5× to 4×)
- [ ] Pixel art (Apple II graphics) renders with nearest-neighbor scaling
- [ ] Standard image formats (GIF/JPEG/PNG) display correctly
- [ ] Prev/Next buttons navigate between selected files
- [ ] Find (F3 / Shift+F3) works in text mode
- [ ] Export button saves correct format to disk
- [ ] Copy puts text or image on clipboard
- [ ] Converter combo box switches between available converters
- [ ] Converter options (checkboxes, radios) update the display
- [ ] Resource fork tab shows resource fork text when available
- [ ] Notes tab shows file notes when available
- [ ] Tab enabled/disabled states are correct
