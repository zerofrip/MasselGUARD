using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MasselGUARD.Models;

namespace MasselGUARD.Views
{
    public partial class ThemeBuilderWindow : Window
    {
        // ── State ─────────────────────────────────────────────────────────────
        private readonly MainWindow   _main;
        private ThemeDefinition       _draft    = new();
        private string                _editingName  = "";   // folder name being edited
        private bool                  _isBuiltin    = false;
        private bool                  _loading      = false;
        private string                _previewPrev  = "";   // theme name before preview
        private bool                  _isPreviewing = false;

        // Color-picker controls built in BuildColorRows()
        private readonly Dictionary<string, TextBox> _colorBoxes   = new();
        private readonly Dictionary<string, Border>  _colorSwatches = new();

        // Color keys + friendly labels in display order
        private static readonly (string key, string label)[] ColorFields =
        {
            ("ColorWindowBg",    "Window background"),
            ("ColorSurface",     "Surface (title/footer)"),
            ("ColorCard",        "Card / list panel"),
            ("ColorBorder",      "Border / divider"),
            ("ColorAccent",      "Accent"),
            ("ColorTextPrimary", "Text — primary"),
            ("ColorTextMuted",   "Text — muted"),
            ("ColorSuccess",     "Success (green)"),
            ("ColorDanger",      "Danger (red)"),
            ("ColorHighlight",   "Highlight"),
            ("ColorError",       "Error text"),
            ("ColorErrorBg",     "Error background"),
            ("ColorWarning",     "Warning text"),
            ("ColorWarningBg",   "Warning background"),
            ("ColorListHover",   "List row — hover"),
            ("ColorListSelected","List row — selected"),
            ("ColorLogTimestamp","Log timestamp"),
            ("ColorTrayBg",      "Tray menu background"),
            ("ColorTrayHover",   "Tray menu hover"),
            ("ColorTrayText",    "Tray menu text"),
            ("ColorTrayBorder",  "Tray menu border"),
        };

        // ── Constructor ───────────────────────────────────────────────────────
        public ThemeBuilderWindow(MainWindow main)
        {
            _main = main;
            InitializeComponent();
            BuildColorRows();
            PopulateThemeList();
        }

        // ── Color rows (built programmatically) ───────────────────────────────
        private void BuildColorRows()
        {
            foreach (var (key, label) in ColorFields)
            {
                if (key == "ColorTrayBg")
                {
                    ColorsPanel.Children.Add(new TextBlock
                    {
                        Text       = "Tray menu",
                        FontFamily = (FontFamily)FindResource("Theme.FontFamily"),
                        FontSize   = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("TextPrimary"),
                        Margin     = new Thickness(0, 10, 0, 6),
                    });
                }
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var lbl = new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = (FontFamily)FindResource("Theme.FontFamily"),
                    FontSize   = 10,
                    Foreground = (Brush)FindResource("TextMuted"),
                };
                Grid.SetColumn(lbl, 0);

                var box = new TextBox
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize   = 10,
                    Padding    = new Thickness(6, 3, 4, 3),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Tag = key,
                };
                box.TextChanged += ColorBox_TextChanged;
                Grid.SetColumn(box, 1);
                _colorBoxes[key] = box;

                var swatch = new Border
                {
                    Width           = 28,
                    Height          = 22,
                    CornerRadius    = new CornerRadius(3),
                    Margin          = new Thickness(6, 0, 0, 0),
                    Cursor          = Cursors.Hand,
                    BorderBrush     = (Brush)FindResource("BorderColor"),
                    BorderThickness = new Thickness(1),
                    Tag = key,
                    ToolTip = "Click to pick a color",
                };
                swatch.MouseLeftButtonUp += Swatch_Click;
                Grid.SetColumn(swatch, 2);
                _colorSwatches[key] = swatch;

                row.Children.Add(lbl);
                row.Children.Add(box);
                row.Children.Add(swatch);
                ColorsPanel.Children.Add(row);
            }
        }

        // ── Theme list ────────────────────────────────────────────────────────
        private void PopulateThemeList()
        {
            BuiltinList.Items.Clear();
            CustomList.Items.Clear();

            var active = ThemeManager.Instance.CurrentThemeName;

            // Built-in themes — those whose folder lives in the exe's theme\ directory
            foreach (var name in ThemeManager.BuiltinThemeNames.OrderBy(n => n))
            {
                if (!File.Exists(Path.Combine(ThemeManager.ThemeFolder(name), "theme.json"))) continue;
                var display = ThemeManager.GetThemeDisplayName(name);
                BuiltinList.Items.Add(BuildListItem(name, display, isBuiltin: true, isActive: name == active));
            }

            // Custom (AppData themes folder)
            var userRoot = ThemeManager.UserThemeRoot;
            List<string> customNames = new();
            if (Directory.Exists(userRoot))
                customNames = Directory.GetDirectories(userRoot)
                    .Select(d => Path.GetFileName(d)!)
                    .Where(n => File.Exists(Path.Combine(userRoot, n, "theme.json")))
                    .OrderBy(n => n)
                    .ToList();

            foreach (var name in customNames)
            {
                var display = ThemeManager.GetThemeDisplayName(name);
                var item    = BuildListItem(name, display, isBuiltin: false, isActive: name == active);
                CustomList.Items.Add(item);
            }

            NoCustomLabel.Visibility = customNames.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private ListBoxItem BuildListItem(string name, string display, bool isBuiltin, bool isActive)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            if (isActive)
            {
                panel.Children.Add(new TextBlock
                {
                    Text       = "● ",
                    FontSize   = 8,
                    Foreground = (Brush)FindResource("Accent"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            panel.Children.Add(new TextBlock
            {
                Text       = display,
                FontFamily = (FontFamily)FindResource("Theme.FontFamily"),
                FontSize   = 11,
                Foreground = (Brush)FindResource(isActive ? "Accent" : "TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (isBuiltin)
                panel.Children.Add(new TextBlock
                {
                    Text       = "  🔒",
                    FontSize   = 9,
                    Foreground = (Brush)FindResource("TextMuted"),
                    VerticalAlignment = VerticalAlignment.Center,
                });

            return new ListBoxItem { Content = panel, Tag = name };
        }

        // ── List selection ────────────────────────────────────────────────────
        private void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Deselect the other list
            if (sender == BuiltinList && BuiltinList.SelectedItem != null)
                CustomList.SelectedItem = null;
            else if (sender == CustomList && CustomList.SelectedItem != null)
                BuiltinList.SelectedItem = null;

            var selected = (sender as ListBox)?.SelectedItem as ListBoxItem;
            if (selected == null) return;

            var name = selected.Tag as string ?? "";
            LoadTheme(name);
        }

        private void LoadTheme(string name)
        {
            _editingName = name;
            _isBuiltin   = ThemeManager.IsBuiltinTheme(name);

            // Load definition
            ThemeDefinition? def = null;
            if (name != "__system__")
                def = ThemeManager.GetThemeMetadata(name);
            _draft = def ?? ThemeDefinition.Default;

            _loading = true;
            try { PopulateEditor(); }
            finally { _loading = false; }

            // Show/hide panels
            EmptyStateLabel.Visibility = Visibility.Collapsed;
            EditorPanel.Visibility     = Visibility.Visible;
            ReadOnlyBanner.Visibility  = _isBuiltin ? Visibility.Visible : Visibility.Collapsed;

            // Buttons
            SetEditorReadOnly(_isBuiltin);
            DuplicateBtn.IsEnabled  = name != "__system__";
            DeleteThemeBtn.IsEnabled = !_isBuiltin;
            SaveBtn.IsEnabled        = !_isBuiltin;
            PreviewBtn.IsEnabled     = true;
        }

        private void PopulateEditor()
        {
            var d = _draft;

            // Identity
            ThemeName.Text    = d.Name;
            AppNameBox.Text   = d.AppName;
            CreatorBox.Text   = d.Creator;
            DescriptionBox.Text = d.Description;
            TypeDark.IsChecked  = !d.Type.Equals("light", StringComparison.OrdinalIgnoreCase);
            TypeLight.IsChecked =  d.Type.Equals("light", StringComparison.OrdinalIgnoreCase);

            // Colors (use reflection to map property names to TextBoxes)
            foreach (var (key, _) in ColorFields)
            {
                var prop = typeof(ThemeDefinition).GetProperty(key);
                var val  = prop?.GetValue(d) as string ?? "";
                if (_colorBoxes.TryGetValue(key, out var box)) box.Text = val;
                UpdateSwatch(key, val);
            }

            // Typography
            FontFamilyBox.Text          = d.FontFamily;
            FontSizeSlider.Value        = d.FontSize;
            CornerRadiusSlider.Value    = d.CornerRadius;

            // Window
            OpacitySlider.Value         = d.WindowOpacity;
            TitleBarHeightSlider.Value  = d.TitleBarHeight;
            ShowTitleBarIconCheck.IsChecked    = d.ShowTitleBarIcon;
            ShowTitleBarAppNameCheck.IsChecked = d.ShowTitleBarAppName;
            ShowResizeGripCheck.IsChecked      = d.ShowResizeGrip;

            // Status bar
            ShowStatusBarCheck.IsChecked    = d.ShowStatusBar;
            StatusBarHeightSlider.Value     = d.StatusBarHeight;
            ShowStatusWifiCheck.IsChecked   = d.ShowStatusWifi;
            ShowStatusTunnelCheck.IsChecked = d.ShowStatusTunnel;

            // Assets
            LogoPathBox.Text    = d.Logo;
            LogoWidthBox.Text   = d.LogoWidth.ToString();
            LogoHeightBox.Text  = d.LogoHeight.ToString();
            AppIconPathBox.Text = d.AppIcon;
            RefreshLogoPreview();

            // Background
            BgImagePathBox.Text   = d.BackgroundImage;
            BgOpacitySlider.Value = d.BackgroundOpacity;
            (d.BackgroundStretch?.ToLowerInvariant() switch
            {
                "center"  => StretchCenter,
                "tile"    => StretchTile,
                "topleft" => StretchTopLeft,
                _         => StretchFill,
            }).IsChecked = true;

            UpdateSliderLabels();
        }

        private void SetEditorReadOnly(bool readOnly)
        {
            // Identity
            ThemeName.IsReadOnly    = readOnly;
            AppNameBox.IsReadOnly   = readOnly;
            CreatorBox.IsReadOnly   = readOnly;
            DescriptionBox.IsReadOnly = readOnly;
            TypeDark.IsEnabled      = !readOnly;
            TypeLight.IsEnabled     = !readOnly;

            // Color boxes
            foreach (var box in _colorBoxes.Values)
                box.IsReadOnly = readOnly;
            foreach (var sw in _colorSwatches.Values)
                sw.Cursor = readOnly ? Cursors.Arrow : Cursors.Hand;

            // Other controls
            FontFamilyBox.IsReadOnly = readOnly;
            FontSizeSlider.IsEnabled = !readOnly;
            CornerRadiusSlider.IsEnabled = !readOnly;
            OpacitySlider.IsEnabled  = !readOnly;
            TitleBarHeightSlider.IsEnabled = !readOnly;
            ShowTitleBarIconCheck.IsEnabled    = !readOnly;
            ShowTitleBarAppNameCheck.IsEnabled = !readOnly;
            ShowResizeGripCheck.IsEnabled      = !readOnly;
            ShowStatusBarCheck.IsEnabled    = !readOnly;
            StatusBarHeightSlider.IsEnabled = !readOnly;
            ShowStatusWifiCheck.IsEnabled   = !readOnly;
            ShowStatusTunnelCheck.IsEnabled = !readOnly;
            LogoPathBox.IsReadOnly    = readOnly;
            LogoWidthBox.IsReadOnly   = readOnly;
            LogoHeightBox.IsReadOnly  = readOnly;
            AppIconPathBox.IsReadOnly = readOnly;
            BgImagePathBox.IsReadOnly = readOnly;
            BgOpacitySlider.IsEnabled = !readOnly;
            StretchFill.IsEnabled    = !readOnly;
            StretchCenter.IsEnabled  = !readOnly;
            StretchTile.IsEnabled    = !readOnly;
            StretchTopLeft.IsEnabled = !readOnly;
        }

        // ── Color picker handlers ─────────────────────────────────────────────
        private void ColorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            if (sender is not TextBox box || box.Tag is not string key) return;
            UpdateSwatch(key, box.Text);
        }

        private void UpdateSwatch(string key, string hex)
        {
            if (!_colorSwatches.TryGetValue(key, out var swatch)) return;
            try
            {
                var color = string.IsNullOrWhiteSpace(hex)
                    ? Colors.Transparent
                    : (Color)ColorConverter.ConvertFromString(hex);
                swatch.Background = new SolidColorBrush(color);
            }
            catch
            {
                swatch.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        private void Swatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isBuiltin) return;
            if (sender is not Border swatch || swatch.Tag is not string key) return;

            // Get current color
            Color current = Colors.White;
            if (_colorBoxes.TryGetValue(key, out var box) && !string.IsNullOrWhiteSpace(box.Text))
            {
                try { current = (Color)ColorConverter.ConvertFromString(box.Text); }
                catch { }
            }

            using var dlg = new System.Windows.Forms.ColorDialog
            {
                Color            = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
                FullOpen         = true,
                AllowFullOpen    = true,
                AnyColor         = true,
                SolidColorOnly   = false,
            };

            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var chosen = dlg.Color;
            var hex    = $"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}";
            if (_colorBoxes.TryGetValue(key, out var tb))
                tb.Text = hex;   // triggers ColorBox_TextChanged → UpdateSwatch
        }

        // ── Field change handlers ─────────────────────────────────────────────
        private void Field_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            // Nothing to do live — draft is read on Save/Preview
        }

        private void Type_Changed(object sender, RoutedEventArgs e)  { /* read on save */ }
        private void Toggle_Changed(object sender, RoutedEventArgs e) { /* read on save */ }
        private void BgStretch_Changed(object sender, RoutedEventArgs e) { /* read on save */ }

        private void FontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => FontSizeLabel.Text = $"{(int)FontSizeSlider.Value} pt";

        private void CornerRadius_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => CornerRadiusLabel.Text = $"{(int)CornerRadiusSlider.Value} px";

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => OpacityLabel.Text = $"{OpacitySlider.Value:P0}";

        private void TitleBarHeight_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => TitleBarHeightLabel.Text = $"{(int)TitleBarHeightSlider.Value} px";

        private void StatusBarHeight_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => StatusBarHeightLabel.Text = $"{(int)StatusBarHeightSlider.Value} px";

        private void BgOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
            => BgOpacityLabel.Text = $"{BgOpacitySlider.Value:P0}";

        private void UpdateSliderLabels()
        {
            FontSizeLabel.Text        = $"{(int)FontSizeSlider.Value} pt";
            CornerRadiusLabel.Text    = $"{(int)CornerRadiusSlider.Value} px";
            OpacityLabel.Text         = $"{OpacitySlider.Value:P0}";
            TitleBarHeightLabel.Text  = $"{(int)TitleBarHeightSlider.Value} px";
            StatusBarHeightLabel.Text = $"{(int)StatusBarHeightSlider.Value} px";
            BgOpacityLabel.Text       = $"{BgOpacitySlider.Value:P0}";
        }

        // ── Asset browsers ────────────────────────────────────────────────────
        private void BrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseImage("Logo image|*.png;*.jpg;*.jpeg;*.bmp;*.svg");
            if (path != null) { LogoPathBox.Text = CopyAssetToTheme(path, "logo"); RefreshLogoPreview(); }
        }
        private void ClearLogo_Click(object sender, RoutedEventArgs e)
        {
            LogoPathBox.Text = "";
            LogoPreviewBorder.Visibility = Visibility.Collapsed;
        }

        private void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseImage("App icon|*.ico;*.png;*.bmp;*.jpg;*.jpeg");
            if (path != null) AppIconPathBox.Text = CopyAssetToTheme(path, "icon");
        }
        private void ClearIcon_Click(object sender, RoutedEventArgs e) => AppIconPathBox.Text = "";

        private void BrowseBg_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseImage("Background image|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff");
            if (path != null) BgImagePathBox.Text = CopyAssetToTheme(path, "bg");
        }
        private void ClearBg_Click(object sender, RoutedEventArgs e) => BgImagePathBox.Text = "";

        private static string? BrowseImage(string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        /// <summary>
        /// Copies a picked file into the current theme's folder (creating it if needed)
        /// and returns just the filename so theme.json stores a relative path.
        /// </summary>
        private string CopyAssetToTheme(string srcPath, string destBaseName)
        {
            if (string.IsNullOrEmpty(_editingName) || _isBuiltin) return Path.GetFileName(srcPath);
            try
            {
                var ext      = Path.GetExtension(srcPath);
                var fileName = destBaseName + ext;
                var themeDir = Path.Combine(ThemeManager.UserThemeRoot, _editingName);
                Directory.CreateDirectory(themeDir);
                File.Copy(srcPath, Path.Combine(themeDir, fileName), overwrite: true);
                return fileName;
            }
            catch { return Path.GetFileName(srcPath); }
        }

        private void RefreshLogoPreview()
        {
            var logoFile = LogoPathBox?.Text ?? "";
            if (string.IsNullOrWhiteSpace(logoFile) || string.IsNullOrEmpty(_editingName))
            {
                LogoPreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }
            var full = Path.Combine(ThemeManager.UserThemeRoot, _editingName, logoFile);
            if (!File.Exists(full)) full = Path.Combine(ThemeManager.ThemeFolder(_editingName), logoFile);
            if (!File.Exists(full)) { LogoPreviewBorder.Visibility = Visibility.Collapsed; return; }

            try
            {
                var bmp = new BitmapImage(new Uri(full, UriKind.Absolute));
                LogoPreviewImg.Source        = bmp;
                LogoPreviewBorder.Visibility = Visibility.Visible;
            }
            catch { LogoPreviewBorder.Visibility = Visibility.Collapsed; }
        }

        // ── Collect draft from UI ─────────────────────────────────────────────
        private ThemeDefinition CollectDraft()
        {
            var d = new ThemeDefinition
            {
                Name        = ThemeName.Text.Trim(),
                AppName     = AppNameBox.Text.Trim(),
                Creator     = CreatorBox.Text.Trim(),
                Description = DescriptionBox.Text.Trim(),
                Type        = TypeLight.IsChecked == true ? "light" : "dark",

                FontFamily    = FontFamilyBox.Text.Trim(),
                FontSize      = FontSizeSlider.Value,
                CornerRadius  = (int)CornerRadiusSlider.Value,

                WindowOpacity      = Math.Round(OpacitySlider.Value, 2),
                TitleBarHeight     = (int)TitleBarHeightSlider.Value,
                ShowTitleBarIcon   = ShowTitleBarIconCheck.IsChecked == true,
                ShowTitleBarAppName= ShowTitleBarAppNameCheck.IsChecked == true,
                ShowResizeGrip     = ShowResizeGripCheck.IsChecked == true,

                ShowStatusBar     = ShowStatusBarCheck.IsChecked == true,
                StatusBarHeight   = (int)StatusBarHeightSlider.Value,
                ShowStatusWifi    = ShowStatusWifiCheck.IsChecked == true,
                ShowStatusTunnel  = ShowStatusTunnelCheck.IsChecked == true,

                Logo         = LogoPathBox.Text.Trim(),
                LogoWidth    = int.TryParse(LogoWidthBox.Text,  out var lw) ? lw : 28,
                LogoHeight   = int.TryParse(LogoHeightBox.Text, out var lh) ? lh : 28,
                AppIcon      = AppIconPathBox.Text.Trim(),

                BackgroundImage   = BgImagePathBox.Text.Trim(),
                BackgroundStretch = GetCheckedTag(StretchFill, StretchCenter, StretchTile, StretchTopLeft),
                BackgroundOpacity = Math.Round(BgOpacitySlider.Value, 2),
            };

            // Colors via reflection
            foreach (var (key, _) in ColorFields)
            {
                if (!_colorBoxes.TryGetValue(key, out var box)) continue;
                var prop = typeof(ThemeDefinition).GetProperty(key);
                prop?.SetValue(d, box.Text.Trim());
            }

            return d;
        }

        private static string GetCheckedTag(params RadioButton[] buttons)
        {
            foreach (var rb in buttons)
                if (rb.IsChecked == true) return rb.Tag as string ?? "";
            return "";
        }

        // ── New theme ─────────────────────────────────────────────────────────
        private void NewTheme_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("New theme", "Enter a folder name for the new theme\n(lowercase, no spaces — e.g. my-dark):", "");
            dlg.Owner = this;
            if (dlg.ShowDialog() != true) return;

            var folderName = dlg.Value.Trim().ToLowerInvariant()
                .Replace(' ', '-')
                .Replace('\\', '-').Replace('/', '-');
            if (string.IsNullOrEmpty(folderName)) return;

            // Seed from the currently active theme definition
            var seed = ThemeManager.Instance.Current ?? ThemeDefinition.Default;
            seed.Name    = "New Theme";
            seed.Creator = "";
            seed.Description = "";

            CreateAndEditTheme(folderName, seed);
        }

        // ── Duplicate ─────────────────────────────────────────────────────────
        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_editingName)) return;

            var dlg = new InputDialog("Duplicate theme", "Enter a folder name for the copy:", _editingName + "-copy");
            dlg.Owner = this;
            if (dlg.ShowDialog() != true) return;

            var folderName = dlg.Value.Trim().ToLowerInvariant()
                .Replace(' ', '-')
                .Replace('\\', '-').Replace('/', '-');
            if (string.IsNullOrEmpty(folderName)) return;

            var seed = _isBuiltin ? (ThemeManager.GetThemeMetadata(_editingName) ?? _draft)
                                  : CollectDraft();
            seed.Name = ThemeManager.GetThemeDisplayName(_editingName) + " (copy)";

            CreateAndEditTheme(folderName, seed);
        }

        private void CreateAndEditTheme(string folderName, ThemeDefinition seed)
        {
            var dir = Path.Combine(ThemeManager.UserThemeRoot, folderName);
            if (Directory.Exists(dir))
            {
                MessageBox.Show($"A theme named '{folderName}' already exists.",
                    "Theme Builder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Directory.CreateDirectory(dir);
            WriteThemeJson(dir, seed);
            PopulateThemeList();

            // Select the new theme in custom list
            foreach (ListBoxItem item in CustomList.Items)
                if (item.Tag as string == folderName)
                {
                    CustomList.SelectedItem = item;
                    break;
                }
        }

        // ── Delete theme ──────────────────────────────────────────────────────
        private void DeleteTheme_Click(object sender, RoutedEventArgs e)
        {
            if (_isBuiltin || string.IsNullOrEmpty(_editingName)) return;

            var display = ThemeManager.GetThemeDisplayName(_editingName);
            if (MessageBox.Show($"Delete theme '{display}'?\n\nThis cannot be undone.",
                    "Theme Builder", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            var dir = Path.Combine(ThemeManager.UserThemeRoot, _editingName);
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not delete theme folder:\n{ex.Message}", "Theme Builder");
                return;
            }

            // Switch away if this was the active theme
            if (ThemeManager.Instance.CurrentThemeName == _editingName)
            {
                _main.ConfigSvc.Config.ActiveTheme = "__system__";
                ThemeManager.Instance.LoadSystem(ThemeManager.GetSystemIsDark());
                _main.ConfigSvc.Save();
            }

            _editingName = "";
            EditorPanel.Visibility     = Visibility.Collapsed;
            EmptyStateLabel.Visibility = Visibility.Visible;
            DeleteThemeBtn.IsEnabled   = false;
            SaveBtn.IsEnabled          = false;
            PreviewBtn.IsEnabled       = false;
            DuplicateBtn.IsEnabled     = false;

            PopulateThemeList();
        }

        // ── Preview ───────────────────────────────────────────────────────────
        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPreviewing)
            {
                _previewPrev       = ThemeManager.Instance.CurrentThemeName;
                _isPreviewing      = true;
                PreviewBtn.Content = "Revert preview";

                var draft  = CollectDraft();
                var folder = string.IsNullOrEmpty(_editingName)
                    ? string.Empty
                    : ThemeManager.ThemeFolder(_editingName);
                ThemeManager.Instance.ApplyPreview(draft, folder);
            }
            else
            {
                _isPreviewing      = false;
                PreviewBtn.Content = "Preview";
                ThemeManager.Instance.Load(_previewPrev);
            }
        }

        // ── Save ──────────────────────────────────────────────────────────────
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_isBuiltin || string.IsNullOrEmpty(_editingName))
            {
                MessageBox.Show("Built-in themes cannot be saved. Use Duplicate to create an editable copy.",
                    "Theme Builder", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var draft = CollectDraft();

            // Validate name
            if (string.IsNullOrWhiteSpace(draft.Name))
            {
                MessageBox.Show("Theme name cannot be empty.", "Theme Builder");
                return;
            }

            var dir = Path.Combine(ThemeManager.UserThemeRoot, _editingName);
            Directory.CreateDirectory(dir);
            WriteThemeJson(dir, draft);

            // Revert preview if active, then reload the saved theme
            if (_isPreviewing) { _isPreviewing = false; PreviewBtn.Content = "Preview"; }
            ThemeManager.Instance.Load(_editingName, ThemeManager.GetSystemIsDark());

            _main.ConfigSvc.Config.ActiveTheme = _editingName;
            _main.ConfigSvc.Save();

            PopulateThemeList();
            MessageBox.Show($"Theme '{draft.Name}' saved and applied.", "Theme Builder",
                MessageBoxButton.OK, MessageBoxImage.None);
        }

        // ── Write theme.json ──────────────────────────────────────────────────
        private static void WriteThemeJson(string dir, ThemeDefinition def)
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented        = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            var json = JsonSerializer.Serialize(def, opts);
            File.WriteAllText(Path.Combine(dir, "theme.json"), json, System.Text.Encoding.UTF8);
        }

        // ── Window chrome ─────────────────────────────────────────────────────
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPreviewing)
                ThemeManager.Instance.Load(_previewPrev);
            Close();
        }
    }

    // ── Simple text-input dialog ──────────────────────────────────────────────
    internal class InputDialog : Window
    {
        private readonly TextBox _box;
        public string Value => _box.Text;

        public InputDialog(string title, string prompt, string initial)
        {
            Title               = title;
            Width               = 400;
            SizeToContent       = SizeToContent.Height;
            WindowStyle         = WindowStyle.None;
            AllowsTransparency  = true;
            Background          = Brushes.Transparent;
            ResizeMode          = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var border = new Border
            {
                Background      = (Brush)Application.Current.Resources["WindowBg"],
                BorderBrush     = (Brush)Application.Current.Resources["Accent"],
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(20),
            };

            _box = new TextBox
            {
                Text    = initial,
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 11,
                Margin  = new Thickness(0, 8, 0, 12),
            };

            var ok = new Button
            {
                Content             = "OK",
                IsDefault           = true,
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding             = new Thickness(20, 6, 20, 6),
                FontSize            = 11,
                Style               = Application.Current.TryFindResource("SuccessBtn") as Style,
            };
            ok.Click += (_, _) => { DialogResult = true; Close(); };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text         = prompt,
                TextWrapping = TextWrapping.Wrap,
                FontSize     = 11,
                Foreground   = (Brush)Application.Current.Resources["TextPrimary"],
                Margin       = new Thickness(0, 0, 0, 4),
            });
            stack.Children.Add(_box);
            stack.Children.Add(ok);

            border.Child = stack;
            Content      = border;

            Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        }
    }
}
