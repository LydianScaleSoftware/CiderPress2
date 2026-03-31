/*
 * Copyright 2023 faddenSoft
 * Copyright 2026 Lydian Scale Software
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;

using Avalonia.Media;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

using FileConv;

namespace cp2_avalonia.Tools {
    /// <summary>
    /// Converts FancyText annotations into AvaloniaEdit TextDocument with highlighting.
    /// Replaces the RTFGenerator → RichTextBox.Load pipeline.
    /// </summary>
    public static class FancyTextHelper {
        /// <summary>
        /// Populates a TextEditor with FancyText content and formatting.
        /// </summary>
        public static void Apply(TextEditor editor, FancyText fancyText) {
            string plainText = fancyText.Text.ToString();
            editor.Document = new TextDocument(plainText);

            var transformer = new FancyTextTransformer(fancyText);
            editor.TextArea.TextView.LineTransformers.Clear();
            editor.TextArea.TextView.LineTransformers.Add(transformer);
        }
    }

    /// <summary>
    /// AvaloniaEdit line transformer that applies FancyText annotations as visual formatting.
    /// Annotations are state transitions at character offsets, NOT ranges; state is carried
    /// across ColorizeLine() calls.
    /// </summary>
    internal class FancyTextTransformer : DocumentColorizingTransformer {
        // Sorted annotation list (sorted by construction — FancyText appends in offset order).
        private readonly List<FancyText.Annotation> mAnnotations;

        // Annotation cursor: advances monotonically across ColorizeLine() calls.
        private int mAnnotationIndex;

        // Running formatting state.
        private bool mBold;
        private bool mItalic;
        private bool mUnderline;
        private IBrush mForeground = Brushes.Black;
        private Avalonia.Media.FontFamily mFontFamily =
            new Avalonia.Media.FontFamily("Consolas");
        private double mFontSize = 13.0;   // points; matches the TextEditor FontSize

        public FancyTextTransformer(FancyText fancyText) {
            mAnnotations = new List<FancyText.Annotation>(fancyText);
        }

        /// <summary>
        /// Resets running state.  Call before displaying a new document.
        /// </summary>
        public void ResetState() {
            mAnnotationIndex = 0;
            mBold = false;
            mItalic = false;
            mUnderline = false;
            mForeground = Brushes.Black;
            mFontFamily = new Avalonia.Media.FontFamily("Consolas");
            mFontSize = 13.0;
        }

        /// <inheritdoc/>
        protected override void ColorizeLine(DocumentLine line) {
            int lineStart = line.Offset;
            int lineEnd = line.EndOffset;       // exclusive, excludes line terminator

            int segStart = lineStart;

            // Process all annotations within this line (including line terminator region).
            int lineTotalEnd = lineStart + line.TotalLength;
            while (mAnnotationIndex < mAnnotations.Count &&
                   mAnnotations[mAnnotationIndex].Offset < lineTotalEnd) {

                FancyText.Annotation anno = mAnnotations[mAnnotationIndex];

                // If the annotation falls within the visible text range and there is a segment
                // before it that needs the current formatting applied, emit ChangeLinePart.
                int annoPos = Math.Min(anno.Offset, lineEnd);
                if (annoPos > segStart && segStart < lineEnd) {
                    ApplyCurrentFormatting(segStart, annoPos);
                    segStart = annoPos;
                }

                // Apply state change from annotation.
                ApplyAnnotationToState(anno);
                mAnnotationIndex++;
            }

            // Apply formatting to any remaining segment on this line.
            if (segStart < lineEnd) {
                ApplyCurrentFormatting(segStart, lineEnd);
            }
        }

        private void ApplyCurrentFormatting(int start, int end) {
            // Capture current state in locals for the lambda.
            bool bold = mBold;
            bool italic = mItalic;
            bool underline = mUnderline;
            IBrush fg = mForeground;
            Avalonia.Media.FontFamily family = mFontFamily;
            double size = mFontSize;

            ChangeLinePart(start, end, element => {
                element.TextRunProperties.SetForegroundBrush(fg);

                element.TextRunProperties.SetTypeface(new Typeface(
                    family,
                    italic ? FontStyle.Italic : FontStyle.Normal,
                    bold ? FontWeight.Bold : FontWeight.Normal));

                element.TextRunProperties.SetFontRenderingEmSize(size);

                if (underline) {
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                }
            });
        }

        private void ApplyAnnotationToState(FancyText.Annotation anno) {
            switch (anno.Type) {
                case FancyText.AnnoType.Bold:
                    mBold = (bool)anno.Data!;
                    break;
                case FancyText.AnnoType.Italic:
                    mItalic = (bool)anno.Data!;
                    break;
                case FancyText.AnnoType.Underline:
                    mUnderline = (bool)anno.Data!;
                    break;
                case FancyText.AnnoType.ForeColor: {
                    int argb = (int)anno.Data!;
                    byte a = (byte)(argb >> 24);
                    byte r = (byte)(argb >> 16);
                    byte g = (byte)(argb >> 8);
                    byte b = (byte)argb;
                    // FancyText stores colors as RGB (alpha is often 0 for fully opaque).
                    if (a == 0) a = 255;
                    mForeground = new SolidColorBrush(new Color(a, r, g, b));
                    break;
                }
                case FancyText.AnnoType.FontFamily: {
                    FancyText.FontFamily family = (FancyText.FontFamily)anno.Data!;
                    // Map FancyText font family to an Avalonia font family name.
                    string name;
                    if (family.Name.Equals("Symbol", StringComparison.OrdinalIgnoreCase)) {
                        name = "Symbol";
                    } else if (family.IsMono) {
                        name = family.IsSerif
                            ? "Courier New"
                            : "Cascadia Mono, Consolas, Menlo, monospace";
                    } else {
                        name = family.IsSerif ? "Times New Roman" : "Arial";
                    }
                    mFontFamily = new Avalonia.Media.FontFamily(name);
                    break;
                }
                case FancyText.AnnoType.FontSize: {
                    int pts = (int)anno.Data!;
                    if (pts > 0) {
                        mFontSize = pts;
                    }
                    break;
                }

                // NewParagraph / NewPage / Tab / Outline / Shadow / Superscript / Subscript /
                // Justification / LeftMargin / RightMargin / BackColor:
                // No AvaloniaEdit equivalent — silently skip.
                default:
                    break;
            }
        }
    }
}
