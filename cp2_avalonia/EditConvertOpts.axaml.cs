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
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AppCommon;
using CommonUtil;
using FileConv;
using cp2_avalonia.Tools;
using static cp2_avalonia.Tools.ConfigOptCtrl;
using static FileConv.Converter;

namespace cp2_avalonia {
    /// <summary>
    /// Edit the options passed to the import/export converters.
    /// </summary>
    public partial class EditConvertOpts : Window, INotifyPropertyChanged {
        // INotifyPropertyChanged implementation
        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string DescriptionText {
            get { return mDescriptionText; }
            set { mDescriptionText = value; OnPropertyChanged(); }
        }
        private string mDescriptionText = string.Empty;

        /// <summary>
        /// List of import or export converters.
        /// </summary>
        public class ConverterListItem {
            public string Tag { get; private set; }
            public string Label { get; private set; }
            public string Description { get; private set; }
            public List<Converter.OptionDefinition> OptionDefs { get; protected set; }

            public ConverterListItem(string tag, string label, string description,
                    List<Converter.OptionDefinition> optionDefs) {
                Tag = tag;
                Label = label;
                Description = description;
                OptionDefs = optionDefs;
            }
        }

        public List<ConverterListItem> ConverterList { get; } = new List<ConverterListItem>();

        private SettingsHolder mSettings;
        private string mSettingPrefix;

        private List<ControlMapItem> mCustomCtrls = new List<ControlMapItem>();
        private Dictionary<string, string> mConvOptions = new Dictionary<string, string>();
        private bool mIsConfiguring;


        public EditConvertOpts() {
            // Parameterless constructor for AXAML previewer.
            mSettings = new SettingsHolder(AppSettings.Global);
            mSettingPrefix = string.Empty;
            InitializeComponent();
            DataContext = this;
        }

        public EditConvertOpts(bool isExport, SettingsHolder settings) {
            mSettings = new SettingsHolder();  // empty: only accumulates user changes

            if (isExport) {
                Title = "Edit Export Conversion Options";
                mSettingPrefix = AppSettings.EXPORT_SETTING_PREFIX;
                for (int i = 0; i < ExportFoundry.GetCount(); i++) {
                    ExportFoundry.GetConverterInfo(i, out string tag, out string label,
                        out string description, out List<OptionDefinition> optionDefs);
                    ConverterList.Add(new ConverterListItem(tag, label, description, optionDefs));
                }
            } else {
                Title = "Edit Import Conversion Options";
                mSettingPrefix = AppSettings.IMPORT_SETTING_PREFIX;
                for (int i = 0; i < ImportFoundry.GetCount(); i++) {
                    ImportFoundry.GetConverterInfo(i, out string tag, out string label,
                        out string description, out List<OptionDefinition> optionDefs);
                    ConverterList.Add(new ConverterListItem(tag, label, description, optionDefs));
                }
            }

            ConverterList.Sort(delegate (ConverterListItem item1, ConverterListItem item2) {
                return string.Compare(item1.Label, item2.Label);
            });

            InitializeComponent();
            DataContext = this;

            // InitializeComponent() resets Title from the AXAML default; re-apply it.
            Title = isExport ? "Edit Export Conversion Options"
                             : "Edit Import Conversion Options";

            // Build the control map BEFORE setting SelectedIndex so that if SelectionChanged
            // fires synchronously, ConfigureControls() finds a populated map.
            CreateControlMap();
            converterCombo.SelectedIndex = 0;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) {
            // Commit only the converter option keys the user changed (mSettings is
            // empty-initialized, so only explicitly set keys will be merged).
            AppSettings.Global.MergeSettings(mSettings);
            Close(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            Close(false);
        }

        private void ConverterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            ConverterListItem? selItem = ((ComboBox)sender).SelectedItem as ConverterListItem;
            if (selItem == null) {
                Debug.WriteLine("No selection?");
                return;
            }

            // Set the description, replacing any newline chars with the system's preferred EOL.
            DescriptionText = selItem.Description.Replace("\n", Environment.NewLine);

            // Guard against firing before CreateControlMap() runs (belt-and-suspenders).
            if (mCustomCtrls.Count > 0) {
                ConfigureForConverter(selItem.Tag, selItem.OptionDefs);
            }
        }

        /// <summary>
        /// Creates a map of the configurable controls.  The controls are defined in the
        /// "options" section of the AXAML.
        /// </summary>
        private void CreateControlMap() {
            mCustomCtrls.Add(new ToggleButtonMapItem(UpdateOption, checkBox1));
            mCustomCtrls.Add(new ToggleButtonMapItem(UpdateOption, checkBox2));
            mCustomCtrls.Add(new ToggleButtonMapItem(UpdateOption, checkBox3));
            mCustomCtrls.Add(new TextBoxMapItem(UpdateOption, stringInput1, stringInput1_Label,
                stringInput1_Box));
            mCustomCtrls.Add(new RadioButtonGroupItem(UpdateOption, multiGroup1,
                new RadioButton[] { radioButton1_1, radioButton1_2, radioButton1_3,
                    radioButton1_4 }));
            mCustomCtrls.Add(new RadioButtonGroupItem(UpdateOption, multiGroup2,
                new RadioButton[] { radioButton2_1, radioButton2_2, radioButton2_3,
                    radioButton2_4 }));
        }

        /// <summary>
        /// Configures the controls for a specific converter.
        /// </summary>
        private void ConfigureForConverter(string convTag, List<OptionDefinition> optDefs) {
            Debug.WriteLine("Configure controls for " + convTag);
            mIsConfiguring = true;

            mConvOptions = ConfigOptCtrl.LoadExportOptions(optDefs, mSettingPrefix, convTag);

            ConfigOptCtrl.HideConvControls(mCustomCtrls);

            // Show or hide the "no options" message.
            noOptions.IsVisible = (optDefs.Count == 0);

            ConfigOptCtrl.ConfigureControls(mCustomCtrls, optDefs, mConvOptions);

            // Avalonia fires TextChanged asynchronously via the dispatcher, so we must
            // defer resetting the flag until after those queued events have been processed.
            Dispatcher.UIThread.Post(() => mIsConfiguring = false);
        }

        /// <summary>
        /// Updates an option as the result of UI interaction.
        /// </summary>
        private void UpdateOption(string tag, string newValue) {
            if (mIsConfiguring) {
                Debug.WriteLine("Ignoring initial set '" + tag + "' = '" + newValue + "'");
                return;
            }

            // Get converter tag, so we can form the settings file key.
            ConverterListItem? selItem = converterCombo.SelectedItem as ConverterListItem;
            Debug.Assert(selItem != null);
            string settingKey = mSettingPrefix + selItem.Tag;

            // Update the setting and generate the new config string.
            if (string.IsNullOrEmpty(newValue)) {
                mConvOptions.Remove(tag);
            } else {
                mConvOptions[tag] = newValue;
            }
            string optStr = ConvConfig.GenerateOptString(mConvOptions);

            // Save it to our local copy of the settings.
            mSettings.SetString(settingKey, optStr);
        }
    }
}
