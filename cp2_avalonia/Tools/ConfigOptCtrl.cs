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

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using cp2_avalonia.Common;

using CommonUtil;
using FileConv;
using static FileConv.Converter;

namespace cp2_avalonia.Tools {
    /// <summary>
    /// Implements the controls that underlie the converter configuration in the file viewer and
    /// file import/export.  The client code creates a mapping of the controls in the GUI, and
    /// then we create a mapping between the converter option definitions and the available
    /// controls.
    ///
    /// Avalonia port: WPF data bindings are replaced with direct property assignment and
    /// event handler subscriptions.
    /// </summary>
    internal class ConfigOptCtrl {
        /// <summary>
        /// Base class for mappable control items.
        /// </summary>
        internal abstract class ControlMapItem {
            /// <summary>Option tag for config file option handling.</summary>
            public string OptTag { get; protected set; } = string.Empty;

            /// <summary>Outermost element, for visibility control.</summary>
            protected Control VisElem { get; }

            /// <summary>Function to call when the user updates the state of the control.</summary>
            protected UpdateOption Updater { get; }
            public delegate void UpdateOption(string tag, string newValue);

            /// <summary>True if this item is available for assignment (i.e., currently hidden).</summary>
            public bool IsAvailable { get => !VisElem.IsVisible; }

            protected ControlMapItem(UpdateOption updater, Control visElem) {
                Updater = updater;
                VisElem = visElem;
                visElem.IsVisible = false;   // start hidden; AssignControl() will show
            }

            /// <summary>Configures the control and makes it active.</summary>
            public abstract void AssignControl(string tag, string uiString, string defVal,
                string? radioVal = null);

            /// <summary>Hides the control.</summary>
            public void HideControl() {
                VisElem.IsVisible = false;
            }

            public override string ToString() {
                return "[CMI: Tag=" + OptTag + "]";
            }
        }

        /// <summary>
        /// ToggleButton item (CheckBox or RadioButton).  Single control.
        /// </summary>
        internal class ToggleButtonMapItem : ControlMapItem {
            private readonly ToggleButton mCtrl;

            /// <summary>
            /// For radio buttons: the value string to send to Updater when this button
            /// is selected.  Null for plain checkboxes.
            /// </summary>
            public string? RadioVal { get; private set; }

            public ToggleButtonMapItem(UpdateOption updater, ToggleButton ctrl)
                    : base(updater, ctrl) {
                mCtrl = ctrl;
                ctrl.IsCheckedChanged += OnIsCheckedChanged;
            }

            private void OnIsCheckedChanged(object? sender, RoutedEventArgs e) {
                if (RadioVal != null) {
                    // Radio buttons only send an update when they become true, with their tag.
                    if (mCtrl.IsChecked == true) {
                        Updater(OptTag, RadioVal);
                    }
                } else {
                    // Checkboxes always update.
                    Updater(OptTag, (mCtrl.IsChecked == true).ToString());
                }
            }

            public override void AssignControl(string tag, string uiString, string defVal,
                    string? radioVal = null) {
                OptTag = tag;
                RadioVal = radioVal;
                mCtrl.Content = uiString;
                VisElem.IsVisible = true;
                if (bool.TryParse(defVal, out bool value)) {
                    mCtrl.IsChecked = value;
                }
            }

            public override string ToString() {
                return "[TBMI tag=" + OptTag + "]";
            }
        }

        /// <summary>
        /// String input item.  Has three parts: a StackPanel wrapping a TextBlock label and
        /// a TextBox for input.
        /// </summary>
        internal class TextBoxMapItem : ControlMapItem {
            private readonly TextBlock mLabel;
            private readonly TextBox mBox;

            private bool mIgnoreChange;

            public TextBoxMapItem(UpdateOption updater, StackPanel panel, TextBlock label,
                    TextBox box) : base(updater, panel) {
                mLabel = label;
                mBox = box;
                box.TextChanged += OnTextChanged;
            }

            private void OnTextChanged(object? sender, TextChangedEventArgs e) {
                if (!mIgnoreChange) {
                    Updater(OptTag, mBox.Text ?? string.Empty);
                }
            }

            public override void AssignControl(string tag, string uiString, string defVal,
                    string? unused = null) {
                OptTag = tag;
                mLabel.Text = uiString + ':';
                VisElem.IsVisible = true;
                // Suppress the TextChanged event fired by the initial value assignment.
                mIgnoreChange = true;
                mBox.Text = defVal;
                mIgnoreChange = false;
            }

            public override string ToString() {
                return "[TBXI tag=" + OptTag + "]";
            }
        }

        /// <summary>
        /// Radio button group.  Contains a GroupBox and multiple ToggleButtonMapItems.
        /// </summary>
        internal class RadioButtonGroupItem : ControlMapItem {
            private readonly GroupBox mGroup;

            public ToggleButtonMapItem[] ButtonItems { get; }

            public RadioButtonGroupItem(UpdateOption updater, GroupBox groupBox,
                    RadioButton[] buttons) : base(updater, groupBox) {
                mGroup = groupBox;

                ButtonItems = new ToggleButtonMapItem[buttons.Length];
                for (int i = 0; i < buttons.Length; i++) {
                    ButtonItems[i] = new ToggleButtonMapItem(updater, buttons[i]);
                }
            }

            public override void AssignControl(string tag, string uiString, string defVal,
                    string? unused = null) {
                OptTag = tag;
                mGroup.Header = uiString;
                VisElem.IsVisible = true;
            }

            public override string ToString() {
                return "[RGBI tag=" + OptTag + "]";
            }
        }

        // ---------------------------------------------------------------------------
        // Static utility methods

        /// <summary>
        /// Loads the option dictionary with default values and saved app settings.
        /// </summary>
        internal static Dictionary<string, string> LoadExportOptions(
                List<OptionDefinition> optDefs, string configPrefix, string convTag) {
            Dictionary<string, string> dict = new Dictionary<string, string>();

            // Seed with defaults.
            foreach (OptionDefinition optDef in optDefs) {
                dict[optDef.OptTag] = optDef.DefaultVal;
            }

            // Overlay with saved settings.
            string optStr = AppSettings.Global.GetString(configPrefix + convTag, string.Empty);
            if (!string.IsNullOrEmpty(optStr)) {
                if (!ConvConfig.ParseOptString(optStr, dict)) {
                    Debug.Assert(false, "failed to parse option string for " + convTag +
                        ": '" + optStr + "'");
                    // keep going with defaults
                }
            }
            return dict;
        }

        /// <summary>
        /// Finds the first available (hidden) control of the specified type.
        /// </summary>
        internal static ControlMapItem FindFirstAvailable(List<ControlMapItem> ctrls,
                Type ctrlType) {
            foreach (ControlMapItem item in ctrls) {
                if (item.GetType() == ctrlType && item.IsAvailable) {
                    return item;
                }
            }
            throw new NotImplementedException("Not enough instances of " + ctrlType);
        }

        /// <summary>
        /// Configures controls to match the option definitions for a specific converter.
        /// </summary>
        internal static void ConfigureControls(List<ControlMapItem> controls,
                List<OptionDefinition> optionDefs, Dictionary<string, string> convOptions) {
            foreach (OptionDefinition optDef in optionDefs) {
                string defaultVal = convOptions[optDef.OptTag];

                ControlMapItem item;
                switch (optDef.Type) {
                    case OptionDefinition.OptType.Boolean:
                        item = FindFirstAvailable(controls, typeof(ToggleButtonMapItem));
                        item.AssignControl(optDef.OptTag, optDef.OptLabel, defaultVal);
                        break;
                    case OptionDefinition.OptType.IntValue:
                        item = FindFirstAvailable(controls, typeof(TextBoxMapItem));
                        item.AssignControl(optDef.OptTag, optDef.OptLabel, defaultVal);
                        break;
                    case OptionDefinition.OptType.Multi:
                        RadioButtonGroupItem rbg =
                            (RadioButtonGroupItem)FindFirstAvailable(controls,
                                typeof(RadioButtonGroupItem));
                        rbg.AssignControl(optDef.OptTag, optDef.OptLabel, defaultVal);
                        // If we run out of buttons we'll crash, same as WPF version.
                        for (int i = 0; i < optDef.MultiTags!.Length; i++) {
                            string rbTag = optDef.MultiTags[i];
                            string rbLabel = optDef.MultiDescrs![i];
                            // First button always starts set; later buttons set iff tag matches.
                            string isSet = (i == 0 || rbTag == defaultVal).ToString();
                            rbg.ButtonItems[i].AssignControl(optDef.OptTag, rbLabel, isSet, rbTag);
                        }
                        break;
                    default:
                        throw new NotImplementedException("Unknown optdef type " + optDef.Type);
                }
            }
        }

        /// <summary>
        /// Hides all custom controls so they can be reassigned for the next converter.
        /// </summary>
        internal static void HideConvControls(List<ControlMapItem> controls) {
            foreach (ControlMapItem item in controls) {
                item.HideControl();

                if (item is RadioButtonGroupItem rbg) {
                    foreach (ToggleButtonMapItem tbItem in rbg.ButtonItems) {
                        tbItem.HideControl();
                    }
                }
            }
        }
    }
}
