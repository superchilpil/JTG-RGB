using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using JtgRgb.App.Hardware;

namespace JtgRgb.App;

public partial class MainWindow : Window
{
    private string _selectedColor = "#8B6CFF";
    private string _selectedEffect = "Static Color";
    private string _effectColorA = "#8B6CFF";
    private string _effectColorB = "#36C5F0";
    private string _msiConnectionStatus = "Not connected to MSI Mystic Light.";
    private StackPanel? _effectEditorHost;
    private string _selectedProfile = "Default";
    private readonly OpenRgbBackendManager _backend = new();
    private readonly MsiMysticLightClient _msi = new();
    private IReadOnlyList<OpenRgbDevice> _devices = [];
    private IReadOnlyList<MsiLightingDevice> _msiDevices = [];
    private string? _msiSdkPath = MsiMysticLightClient.LoadSavedSdkPath();
    private StackPanel? _deviceListHost;
    private TextBlock? _pawnIoStatusText;

    private static readonly string[] Palette =
    [
        "#8B6CFF", "#36C5F0", "#42D392", "#FF637D", "#F4B94F", "#F4F5FA"
    ];

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            FooterStatus.Text = "Ready • OpenRGB integration available";
            await UpdatePawnIoStatusAsync();
            await HandleMsiMotherboardStartupAsync();
        };
        Closed += async (_, _) => { _msi.Dispose(); await _backend.DisposeAsync(); };
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page }) return;
        ShowPage(page);
    }

    private void ShowPage(string page)
    {
        PageTitle.Text = page;
        PageSubtitle.Text = page switch
        {
            "Dashboard" => "Your lighting setup at a glance",
            "Devices" => "Discover and configure supported hardware",
            "Effects" => "Choose the look and feel of your setup",
            "Layouts" => "Arrange devices on your shared lighting canvas",
            "Profiles" => "Save and switch between lighting setups",
            "Settings" => "Configure JTG-RGB and its behavior",
            _ => "JTG-RGB"
        };

        if (page == "Dashboard")
        {
            SecondaryPageScrollViewer.Visibility = Visibility.Collapsed;
            DashboardScroll.Visibility = Visibility.Visible;
            return;
        }

        DashboardScroll.Visibility = Visibility.Collapsed;
        SecondaryPageScrollViewer.Visibility = Visibility.Visible;
        SecondaryPageHost.Content = page switch
        {
            "Devices" => BuildDevicesPage(),
            "Effects" => BuildEffectsPage(),
            "Layouts" => BuildLayoutsPage(),
            "Profiles" => BuildProfilesPage(),
            "Settings" => BuildSettingsPage(),
            _ => BuildDevicesPage()
        };
    }

    private UIElement BuildDevicesPage()
    {
        var root = PageStack();
        root.Children.Add(SectionHeading("Connected devices", "Connect to the local OpenRGB SDK server to discover supported RGB hardware."));

        var connection = Card();
        var connectionStack = new StackPanel();
        connectionStack.Children.Add(SmallHeading("OPENRGB BACKEND"));
        connectionStack.Children.Add(Heading(_backend.IsBundledBackendAvailable ? "Bundled backend available" : "Connect to OpenRGB"));
        connectionStack.Children.Add(Body("JTG-RGB connects to the OpenRGB SDK at 127.0.0.1:6742. If a compatible OpenRGB.exe is bundled beside the app, JTG-RGB can start it automatically. No driver is installed silently."));
        var buttons = new WrapPanel();
        buttons.Children.Add(ActionButton("Connect / refresh devices", async (_, _) => await ConnectAndRefreshDevicesAsync(), true));
        buttons.Children.Add(ActionButton("Rescan hardware", async (_, _) => await RescanDevicesAsync(), false));
        connectionStack.Children.Add(buttons);
        connection.Child = connectionStack;
        root.Children.Add(connection);

        var devicesCard = Card();
        var devicesStack = new StackPanel();
        devicesStack.Children.Add(Heading("Detected hardware"));
        devicesStack.Children.Add(Body(_devices.Count == 0 ? "No devices have been loaded yet. Connect to OpenRGB to populate this list." : $"{_devices.Count} controller(s) found."));
        _deviceListHost = new StackPanel();
        devicesStack.Children.Add(_deviceListHost);
        devicesCard.Child = devicesStack;
        root.Children.Add(devicesCard);
        RenderDeviceCards();

        var msi = Card();
        var msiStack = new StackPanel();
        msiStack.Children.Add(SmallHeading("MSI MYSTIC LIGHT BACKEND"));
        msiStack.Children.Add(Heading("MSI motherboard lighting"));
        msiStack.Children.Add(Body("Uses MSI's native Mystic Light SDK when available. MSI Center must be installed with the Mystic Light feature enabled and running. A bundled SDK DLL is loaded automatically when included with the app."));
        msiStack.Children.Add(Body(_msiSdkPath is null ? "SDK path not configured." : $"SDK: {_msiSdkPath}"));
        var msiButtons = new WrapPanel();
        msiButtons.Children.Add(ActionButton("Download MSI SDK", async (_, _) => await DownloadMsiSdkAsync(connectAfterDownload: true), true));
        msiButtons.Children.Add(ActionButton("Choose MSI SDK DLL…", (_, _) => ChooseMsiSdkDll(), false));
        msiButtons.Children.Add(ActionButton("Connect / refresh MSI devices", async (_, _) => await ConnectMsiAsync(), false));
        msiStack.Children.Add(msiButtons);
        msiStack.Children.Add(Body(_msiConnectionStatus));
        msiStack.Children.Add(BuildMsiLedControls());
        msi.Child = msiStack;
        root.Children.Add(msi);

        var pawn = Card();
        pawn.Child = new StackPanel
        {
            Children =
            {
                SmallHeading("LOW-LEVEL ACCESS"),
                Heading("PawnIO"),
                Body("PawnIO is an optional driver dependency used by compatible OpenRGB builds for some motherboard and memory devices. Some hardware access may require running OpenRGB with administrator privileges; JTG-RGB will not silently elevate or install drivers.")
            }
        };
        root.Children.Add(pawn);
        return root;
    }

    private UIElement BuildEffectsPage()
    {
        var root = PageStack();
        root.Children.Add(SectionHeading("Effects library", "Choose an effect to open its settings. Configure colors before applying; dynamic animation support is being built in stages."));
        var palette = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        var effects = new[]
        {
            ("Static Color", "One color across supported devices.", "#8B6CFF"),
            ("Color Cycle", "Cycle smoothly through your chosen colors.", "#36C5F0"),
            ("Breathing", "Pulse between dim and bright.", "#42D392"),
            ("Rainbow Wave", "Move a spectrum across supported LEDs.", "#FF637D"),
            ("Gradient", "Blend two colors across available LED entries.", "#F4B94F"),
            ("Ambient", "A calm, low-motion color preset.", "#F4F5FA")
        };
        foreach (var effect in effects)
        {
            var button = new Button
            {
                Width = 218,
                Height = 154,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(14),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Background = MakeBrush("#1B1E29"),
                BorderBrush = MakeBrush(_selectedEffect == effect.Item1 ? "#8B6CFF" : "#292E3B"),
                BorderThickness = new Thickness(_selectedEffect == effect.Item1 ? 2 : 1)
            };
            var panel = new StackPanel();
            panel.Children.Add(new Border { Height = 48, CornerRadius = new CornerRadius(8), Background = MakeBrush(effect.Item3), Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(new TextBlock { Text = effect.Item1, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Brushes.White });
            panel.Children.Add(new TextBlock { Text = effect.Item2, TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = MakeBrush("#969EB1"), Margin = new Thickness(0, 5, 0, 0) });
            button.Content = panel;
            button.Click += (_, _) =>
            {
                _selectedEffect = effect.Item1;
                ActiveEffectText.Text = _selectedEffect;
                ShowPage("Effects");
                FooterStatus.Text = $"Editing effect: {_selectedEffect}";
            };
            palette.Children.Add(button);
        }
        root.Children.Add(palette);

        var editorCard = Card();
        var editor = new StackPanel();
        editor.Children.Add(SmallHeading("EFFECT SETTINGS"));
        editor.Children.Add(Heading(_selectedEffect));
        editor.Children.Add(Body(_selectedEffect switch
        {
            "Static Color" => "Pick a color and apply it to connected hardware.",
            "Gradient" => "Choose the start and end colors. Apply creates a linear color blend across each device's reported color entries. Actual results depend on the controller's LED mapping.",
            "Color Cycle" => "Choose the colors for the cycle. Dynamic cycling is not implemented yet; Apply currently sets the first color.",
            "Breathing" => "Choose the pulse color. Animated brightness is not implemented yet; Apply currently sets the selected color.",
            "Rainbow Wave" => "Choose the base color preview. Animated rainbow output is not implemented yet.",
            _ => "Choose a calm ambient color. Dynamic ambient behavior is not implemented yet; Apply currently sets the selected color."
        }));

        if (_selectedEffect == "Gradient")
        {
            editor.Children.Add(BuildEffectColorPicker("Start color", true));
            editor.Children.Add(BuildEffectColorPicker("End color", false));
            var preview = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(0, 8, 0, 10),
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString(_effectColorA),
                    (Color)ColorConverter.ConvertFromString(_effectColorB),
                    new System.Windows.Point(0, 0),
                    new System.Windows.Point(1, 0))
            };
            editor.Children.Add(preview);
            editor.Children.Add(ActionButton("Apply gradient", async (_, _) => await ApplyGradientEffectAsync(), true));
        }
        else
        {
            editor.Children.Add(BuildEffectColorPicker("Effect color", true));
            editor.Children.Add(ActionButton("Apply effect", async (_, _) => await ApplyColorAsync(_effectColorA), true));
        }

        _effectEditorHost = editor;
        editorCard.Child = editor;
        root.Children.Add(editorCard);

        var note = Card();
        note.Child = new TextBlock { Text = "EFFECT STATUS  •  Static Color and Gradient have hardware output paths. Color Cycle, Breathing, Rainbow Wave, and Ambient currently expose editable settings but their animated renderers are not implemented yet.", TextWrapping = TextWrapping.Wrap, Foreground = MakeBrush("#B4B9CA"), FontSize = 12 };
        root.Children.Add(note);
        var community = Card();
        community.Child = new TextBlock { Text = "COMMUNITY EFFECTS  •  Effect imports will be considered after the built-in rendering engine is in place, with licensing and redistribution checked first.", TextWrapping = TextWrapping.Wrap, Foreground = MakeBrush("#B4B9CA"), FontSize = 12 };
        root.Children.Add(community);
        return root;
    }

    private UIElement BuildEffectColorPicker(string label, bool isFirstColor)
    {
        var current = isFirstColor ? _effectColorA : _effectColorB;
        var (hue, saturation, brightness) = RgbToHsv((Color)ColorConverter.ConvertFromString(current));
        var section = new StackPanel { Margin = new Thickness(0, 8, 0, 14) };
        section.Children.Add(Heading(label));

        var previewRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var preview = new Border
        {
            Width = 76,
            Height = 76,
            CornerRadius = new CornerRadius(12),
            Background = MakeBrush(current),
            BorderBrush = MakeBrush("#555B70"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 0)
        };
        previewRow.Children.Add(preview);
        var previewInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var hexLabel = new TextBlock { Text = current, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
        previewInfo.Children.Add(hexLabel);
        previewInfo.Children.Add(new TextBlock { Text = "Selected color preview", FontSize = 11, Foreground = MakeBrush("#969EB1"), Margin = new Thickness(0, 4, 0, 0) });
        previewRow.Children.Add(previewInfo);
        section.Children.Add(previewRow);

        var hueSlider = new Slider { Minimum = 0, Maximum = 359, Value = hue, TickFrequency = 30, IsSnapToTickEnabled = false, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        hueSlider.Background = BuildHueBrush();
        AddColorSlider(section, "Hue", hueSlider);

        var saturationSlider = new Slider { Minimum = 0, Maximum = 100, Value = saturation * 100, TickFrequency = 10, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        AddColorSlider(section, "Saturation", saturationSlider);

        var brightnessSlider = new Slider { Minimum = 0, Maximum = 100, Value = brightness * 100, TickFrequency = 10, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        AddColorSlider(section, "Brightness", brightnessSlider);

        var hexRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var hex = new TextBox
        {
            Text = current,
            Width = 110,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = MakeBrush("#11141B"),
            Foreground = Brushes.White,
            BorderBrush = MakeBrush("#34394A"),
            ToolTip = "HEX color, for example #8B6CFF"
        };
        hexRow.Children.Add(hex);
        hexRow.Children.Add(ActionButton("Use HEX", (_, _) =>
        {
            var candidate = hex.Text.Trim();
            if (!candidate.StartsWith('#')) candidate = "#" + candidate;
            if (candidate.Length != 7 || !candidate.Skip(1).All(Uri.IsHexDigit))
            {
                MessageBox.Show("Enter a HEX color such as #8B6CFF.", "JTG-RGB — Invalid color", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var parsed = (Color)ColorConverter.ConvertFromString(candidate);
            var hsv = RgbToHsv(parsed);
            hueSlider.Value = hsv.Hue;
            saturationSlider.Value = hsv.Saturation * 100;
            brightnessSlider.Value = hsv.Brightness * 100;
            UpdateSelectedColor();
        }, false));
        section.Children.Add(hexRow);

        var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var color in Palette)
        {
            var swatch = ActionButton(" ", (_, _) =>
            {
                var parsed = (Color)ColorConverter.ConvertFromString(color);
                var hsv = RgbToHsv(parsed);
                hueSlider.Value = hsv.Hue;
                saturationSlider.Value = hsv.Saturation * 100;
                brightnessSlider.Value = hsv.Brightness * 100;
                UpdateSelectedColor();
            }, false, MakeBrush(color));
            swatch.Width = 30;
            swatch.Height = 28;
            swatch.Padding = new Thickness(0);
            swatch.ToolTip = color;
            swatches.Children.Add(swatch);
        }
        section.Children.Add(swatches);

        hueSlider.ValueChanged += (_, _) => UpdateSelectedColor();
        saturationSlider.ValueChanged += (_, _) => UpdateSelectedColor();
        brightnessSlider.ValueChanged += (_, _) => UpdateSelectedColor();
        UpdateSelectedColor();

        void UpdateSelectedColor()
        {
            var color = HsvToRgb(hueSlider.Value, saturationSlider.Value / 100, brightnessSlider.Value / 100);
            var hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            preview.Background = new SolidColorBrush(color);
            hexLabel.Text = hexColor;
            hex.Text = hexColor;
            saturationSlider.Background = BuildSaturationBrush(hueSlider.Value, brightnessSlider.Value / 100);
            brightnessSlider.Background = BuildBrightnessBrush(hueSlider.Value, saturationSlider.Value / 100);
            SetEffectColor(isFirstColor, hexColor);
        }

        return section;
    }

    private static void AddColorSlider(Panel parent, string label, Slider slider)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = MakeBrush("#C7CCDA"), FontSize = 11, Margin = new Thickness(0, 4, 0, 1) });
        parent.Children.Add(slider);
    }

    private static LinearGradientBrush BuildHueBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0.5), EndPoint = new System.Windows.Point(1, 0.5) };
        for (var i = 0; i <= 6; i++)
            brush.GradientStops.Add(new GradientStop(HsvToRgb(i * 60, 1, 1), i / 6d));
        return brush;
    }

    private static LinearGradientBrush BuildSaturationBrush(double hue, double brightness)
    {
        var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0.5), EndPoint = new System.Windows.Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop(HsvToRgb(hue, 0, brightness), 0));
        brush.GradientStops.Add(new GradientStop(HsvToRgb(hue, 1, brightness), 1));
        return brush;
    }

    private static LinearGradientBrush BuildBrightnessBrush(double hue, double saturation)
    {
        var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0.5), EndPoint = new System.Windows.Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop(Colors.Black, 0));
        brush.GradientStops.Add(new GradientStop(HsvToRgb(hue, saturation, 1), 1));
        return brush;
    }

    private static (double Hue, double Saturation, double Brightness) RgbToHsv(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        double hue;
        if (delta == 0) hue = 0;
        else if (max == r) hue = 60 * (((g - b) / delta) % 6);
        else if (max == g) hue = 60 * (((b - r) / delta) + 2);
        else hue = 60 * (((r - g) / delta) + 4);
        if (hue < 0) hue += 360;
        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static Color HsvToRgb(double hue, double saturation, double brightness)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        brightness = Math.Clamp(brightness, 0, 1);
        var chroma = brightness * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = brightness - chroma;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private void SetEffectColor(bool isFirstColor, string color)
    {
        if (isFirstColor) _effectColorA = color;
        else _effectColorB = color;
        _selectedColor = color;
        SelectedColorText.Text = $"Selected: {color}";
        FooterStatus.Text = $"Effect color selected: {color}";
    }

    private async Task ApplyGradientEffectAsync()
    {
        try
        {
            var start = (Color)ColorConverter.ConvertFromString(_effectColorA);
            var end = (Color)ColorConverter.ConvertFromString(_effectColorB);
            var appliedOpenRgb = false;
            var appliedMsi = false;

            if (_backend.Client.IsConnected && _devices.Count > 0)
            {
                foreach (var device in _devices)
                {
                    var count = Math.Max(1, device.ColorCount > 0 ? device.ColorCount : device.LedCount);
                    var colors = Enumerable.Range(0, count)
                        .Select(i => InterpolateColor(start, end, count <= 1 ? 0 : (double)i / (count - 1)))
                        .Select(c => (c.R, c.G, c.B))
                        .ToArray();
                    await _backend.Client.SetDeviceColorsAsync(device, colors);
                }
                appliedOpenRgb = true;
            }

            if (_msi.IsInitialized && _msiDevices.Count > 0)
            {
                await Task.Run(() =>
                {
                    foreach (var device in _msiDevices)
                    {
                        for (uint index = 0; index < device.LedCount; index++)
                        {
                            var c = InterpolateColor(start, end, device.LedCount <= 1 ? 0 : (double)index / (device.LedCount - 1));
                            _msi.SetLedColor(device.Type, index, c.R, c.G, c.B);
                        }
                    }
                });
                appliedMsi = true;
            }

            if (!appliedOpenRgb && !appliedMsi)
            {
                FooterStatus.Text = "Gradient configured • Connect OpenRGB or MSI Mystic Light to apply it";
                return;
            }

            FooterStatus.Text = $"Applied gradient {_effectColorA} → {_effectColorB}";
        }
        catch (Exception ex) when (ex is FormatException or IOException or SocketException or TimeoutException or InvalidOperationException or InvalidDataException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException or ArgumentException)
        {
            FooterStatus.Text = "Could not apply gradient";
            MessageBox.Show(ex.Message, "JTG-RGB — Apply gradient", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static Color InterpolateColor(Color start, Color end, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(start.R + (end.R - start.R) * amount),
            (byte)Math.Round(start.G + (end.G - start.G) * amount),
            (byte)Math.Round(start.B + (end.B - start.B) * amount));
    }

    private UIElement BuildLayoutsPage()
    {
        var root = PageStack();
        root.Children.Add(SectionHeading("Lighting canvas", "The canvas will map each LED to a position so effects can move naturally across devices."));
        var canvasCard = Card();
        var canvas = new Grid { MinHeight = 300, Background = MakeBrush("#12151D") };
        for (var i = 0; i < 12; i++) canvas.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 7; i++) canvas.RowDefinitions.Add(new RowDefinition());
        var title = new TextBlock { Text = "PREVIEW CANVAS", Foreground = MakeBrush("#6E768B"), FontSize = 10, Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        canvas.Children.Add(title);
        var board = new Border { BorderBrush = MakeBrush("#53506F"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = MakeBrush("#1B1D2A"), Margin = new Thickness(70, 65, 70, 65) };
        Grid.SetRow(board, 1); Grid.SetRowSpan(board, 5); Grid.SetColumn(board, 2); Grid.SetColumnSpan(board, 8);
        canvas.Children.Add(board);
        var keys = new UniformGrid { Rows = 3, Columns = 12, Margin = new Thickness(88, 78, 88, 78) };
        foreach (var c in Palette)
            for (var i = 0; i < 6; i++)
                keys.Children.Add(new Border { Margin = new Thickness(2), CornerRadius = new CornerRadius(3), Background = MakeBrush(c), Opacity = 0.9 });
        Grid.SetRow(keys, 1); Grid.SetRowSpan(keys, 5); Grid.SetColumn(keys, 2); Grid.SetColumnSpan(keys, 8);
        canvas.Children.Add(keys);
        canvasCard.Child = canvas;
        root.Children.Add(canvasCard);
        root.Children.Add(Body("This is a decorative interface preview only. Drag-and-drop positioning, LED coordinates, saved layouts, and real-time effect mapping will be added with the effects engine."));
        return root;
    }

    private UIElement BuildProfilesPage()
    {
        var root = PageStack();
        root.Children.Add(SectionHeading("Lighting profiles", "Keep different looks ready for work, gaming, or everyday use."));
        foreach (var profile in new[] { ("Default", "Your everyday lighting setup", "#8B6CFF"), ("Gaming", "A more vivid gaming look", "#36C5F0"), ("Low Light", "Dim and understated", "#42D392") })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var card = Card();
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = MakeBrush(profile.Item3), Margin = new Thickness(0, 0, 14, 0) });
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = profile.Item1, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
            texts.Children.Add(new TextBlock { Text = profile.Item2, Foreground = MakeBrush("#969EB1"), FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
            line.Children.Add(texts);
            card.Child = line;
            Grid.SetColumn(card, 0);
            row.Children.Add(card);
            var select = ActionButton(_selectedProfile == profile.Item1 ? "Selected" : "Select", (_, _) =>
            {
                _selectedProfile = profile.Item1;
                ShowPage("Profiles");
                FooterStatus.Text = $"Profile selected: {_selectedProfile} • Profile persistence is not implemented yet";
            }, false);
            select.VerticalAlignment = VerticalAlignment.Center;
            select.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(select, 1);
            row.Children.Add(select);
            root.Children.Add(row);
        }
        root.Children.Add(ActionButton("Create profile", (_, _) => MessageBox.Show("Custom profile creation and persistence will be added after the settings and profile model are implemented.", "JTG-RGB", MessageBoxButton.OK, MessageBoxImage.Information), true));
        return root;
    }

    private UIElement BuildSettingsPage()
    {
        var root = PageStack();
        root.Children.Add(SectionHeading("Application settings", "Configure the hardware backend and inspect low-level driver readiness."));

        var appearance = Card();
        var appearanceStack = new StackPanel();
        appearanceStack.Children.Add(SmallHeading("APPEARANCE"));
        appearanceStack.Children.Add(Heading("Accent color"));
        var swatches = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        foreach (var color in Palette)
            swatches.Children.Add(ActionButton("●", (_, _) => { _selectedColor = color; SelectedColorText.Text = $"Selected: {color}"; FooterStatus.Text = $"Accent preview selected: {color}"; }, false, MakeBrush(color)));
        appearanceStack.Children.Add(swatches);
        appearanceStack.Children.Add(Body("Accent selection is currently a UI preview; saving the theme is not implemented."));
        appearance.Child = appearanceStack;
        root.Children.Add(appearance);

        var msiCard = Card();
        var msiStack = new StackPanel();
        msiStack.Children.Add(SmallHeading("MSI MYSTIC LIGHT BACKEND"));
        msiStack.Children.Add(Heading("MSI SDK integration"));
        msiStack.Children.Add(Body("JTG-RGB can use MSI's Mystic Light SDK independently of OpenRGB. If the SDK DLL is packaged beside JTG-RGB, it is detected automatically; otherwise choose the DLL from MSI's official SDK package. MSI Center must be installed with the Mystic Light feature enabled and running."));
        msiStack.Children.Add(Body(_msiSdkPath is null ? "SDK path not configured." : $"SDK: {_msiSdkPath}"));
        var msiButtons = new WrapPanel();
        msiButtons.Children.Add(ActionButton("Download MSI SDK", async (_, _) => await DownloadMsiSdkAsync(connectAfterDownload: true), true));
        msiButtons.Children.Add(ActionButton("Choose MSI SDK DLL…", (_, _) => ChooseMsiSdkDll(), false));
        msiButtons.Children.Add(ActionButton("Connect / refresh MSI devices", async (_, _) => await ConnectMsiAsync(), false));
        msiStack.Children.Add(msiButtons);
        msiStack.Children.Add(Body(_msiConnectionStatus));
        msiStack.Children.Add(BuildMsiLedControls());
        msiCard.Child = msiStack;
        root.Children.Add(msiCard);

        var backendCard = Card();
        var backendStack = new StackPanel();
        backendStack.Children.Add(SmallHeading("HARDWARE BACKEND"));
        backendStack.Children.Add(Heading("OpenRGB SDK"));
        backendStack.Children.Add(Body("Endpoint: 127.0.0.1:6742. JTG-RGB uses OpenRGB's local SDK protocol for device discovery and color commands."));
        backendStack.Children.Add(Body(_backend.IsBundledBackendAvailable
            ? "A bundled OpenRGB executable was found. Connect to an existing SDK server or start the bundled backend."
            : "No bundled OpenRGB executable is present in this build. Start OpenRGB with its SDK server enabled, or place a compatible OpenRGB.exe at vendor\\OpenRGB\\OpenRGB.exe."));
        var backendButtons = new WrapPanel();
        backendButtons.Children.Add(ActionButton("Connect / refresh devices", async (_, _) => await ConnectAndRefreshDevicesAsync(), true));
        backendButtons.Children.Add(ActionButton("Rescan hardware", async (_, _) => await RescanDevicesAsync(), false));
        backendStack.Children.Add(backendButtons);
        backendCard.Child = backendStack;
        root.Children.Add(backendCard);

        var pawnCard = Card();
        var pawnStack = new StackPanel();
        pawnStack.Children.Add(SmallHeading("LOW-LEVEL ACCESS"));
        pawnStack.Children.Add(Heading("PawnIO driver"));
        _pawnIoStatusText = Body("Checking whether the PawnIO driver service is registered…");
        pawnStack.Children.Add(_pawnIoStatusText);
        pawnStack.Children.Add(ActionButton("Check PawnIO status", async (_, _) => await UpdatePawnIoStatusAsync(), false));
        pawnStack.Children.Add(Body("This check only looks for the Windows driver service registration; it does not guarantee the driver is loaded or that a particular device is supported. PawnIO will not be installed automatically. Some OpenRGB devices may require the backend to be run as administrator."));
        pawnCard.Child = pawnStack;
        root.Children.Add(pawnCard);
        _ = UpdatePawnIoStatusAsync();

        var licensing = Card();
        licensing.Child = new StackPanel { Children = { SmallHeading("THIRD-PARTY COMPONENTS"), Heading("OpenRGB + PawnIO"), Body("OpenRGB and PawnIO are separate components with separate distribution terms. The bundled OpenRGB release and its corresponding source/notices must be pinned before redistributing it; PawnIO bundling also requires a separate license review.") } };
        root.Children.Add(licensing);
        return root;
    }

    private static CheckBox SettingToggle(string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 9, 0, 9) };
        panel.Children.Add(new CheckBox { Content = title, Foreground = Brushes.White, FontSize = 13, IsChecked = false });
        panel.Children.Add(new TextBlock { Text = description, Foreground = MakeBrush("#969EB1"), FontSize = 10, Margin = new Thickness(24, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        return new CheckBox { Content = panel, IsChecked = false, Margin = new Thickness(0, 3, 0, 3) };
    }

    private static StackPanel PageStack() => new() { Orientation = Orientation.Vertical };
    private static Border Card() => new() { Background = MakeBrush("#181B24"), BorderBrush = MakeBrush("#292E3B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 14) };

    private static UIElement SectionHeading(string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        panel.Children.Add(Body(description));
        return panel;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
    private static TextBlock SmallHeading(string text) => new() { Text = text, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = MakeBrush("#969EB1"), Margin = new Thickness(0, 0, 0, 10) };
    private static TextBlock Body(string text) => new() { Text = text, FontSize = 11, Foreground = MakeBrush("#969EB1"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12), MaxWidth = 850 };
    private static Border IconTile(string symbol, string background, string foreground) => new() { Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = MakeBrush(background), Child = new TextBlock { Text = symbol, FontSize = 24, Foreground = MakeBrush(foreground), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Margin = new Thickness(0, 0, 0, 12) };
    private static SolidColorBrush MakeBrush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static Button ActionButton(string text, RoutedEventHandler click, bool primary, Brush? background = null)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 7, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        if (primary) { button.Background = MakeBrush("#8B6CFF"); button.BorderBrush = MakeBrush("#8B6CFF"); }
        if (background is not null) button.Background = background;
        button.Click += click;
        return button;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ConnectAndRefreshDevicesAsync();
    }

    private async Task ConnectAndRefreshDevicesAsync()
    {
        FooterStatus.Text = "Connecting to OpenRGB SDK…";
        BackendStatusText.Text = "Connecting…";
        BackendDetailText.Text = "Connecting to 127.0.0.1:6742";
        try
        {
            await _backend.ConnectAsync();
            _devices = await _backend.Client.GetDevicesAsync();
            BackendStatusText.Text = "Connected";
            BackendDetailText.Text = $"OpenRGB SDK protocol {_backend.Client.ProtocolVersion}";
            DeviceCountText.Text = _devices.Count.ToString();
            FooterStatus.Text = $"OpenRGB connected • {_devices.Count} controller(s) detected";
            RenderDeviceCards();
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException)
        {
            BackendStatusText.Text = "Not connected";
            BackendDetailText.Text = ex.Message;
            DeviceCountText.Text = "—";
            FooterStatus.Text = "OpenRGB connection failed";
            MessageBox.Show(ex.Message, "JTG-RGB — OpenRGB connection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task RescanDevicesAsync()
    {
        try
        {
            if (!_backend.Client.IsConnected) await _backend.ConnectAsync();
            await _backend.Client.RequestRescanAsync();
            await Task.Delay(800);
            await ConnectAndRefreshDevicesAsync();
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException)
        {
            FooterStatus.Text = "OpenRGB rescan failed";
            MessageBox.Show(ex.Message, "JTG-RGB — Rescan", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RenderDeviceCards()
    {
        if (_deviceListHost is null) return;
        _deviceListHost.Children.Clear();
        foreach (var device in _devices)
        {
            var card = new Border
            {
                Background = MakeBrush("#202431"),
                BorderBrush = MakeBrush("#34394A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var details = new StackPanel();
            details.Children.Add(new TextBlock { Text = device.Name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap });
            details.Children.Add(new TextBlock { Text = $"{device.Vendor} • {device.LedCount} LED(s)", Foreground = MakeBrush("#969EB1"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(device.Description))
                details.Children.Add(new TextBlock { Text = device.Description, Foreground = MakeBrush("#777F94"), FontSize = 10, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(details, 0);
            row.Children.Add(details);
            var apply = ActionButton("Apply selected color", async (_, _) => await ApplyColorToDeviceAsync(device), false);
            apply.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(apply, 1);
            row.Children.Add(apply);
            card.Child = row;
            _deviceListHost.Children.Add(card);
        }
        if (_devices.Count == 0)
            _deviceListHost.Children.Add(Body("No controllers detected. Make sure OpenRGB is running and its SDK server is enabled."));
    }

    private async Task ApplyColorToDeviceAsync(OpenRgbDevice device)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(_selectedColor);
            await _backend.Client.SetDeviceColorAsync(device, color.R, color.G, color.B);
            FooterStatus.Text = $"Applied {_selectedColor} to {device.Name}";
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or InvalidOperationException or InvalidDataException)
        {
            FooterStatus.Text = $"Could not update {device.Name}";
            MessageBox.Show(ex.Message, "JTG-RGB — Apply color", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task UpdatePawnIoStatusAsync()
    {
        var status = await _backend.GetPawnIoStatusAsync();
        if (_pawnIoStatusText is not null) _pawnIoStatusText.Text = status;
    }

    private void ExploreEffects_Click(object sender, RoutedEventArgs e) => ShowPage("Effects");
    private void ViewDevices_Click(object sender, RoutedEventArgs e) => ShowPage("Devices");

    private async void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color }) return;
        await ApplyColorAsync(color);
        CustomColorText.Text = _selectedColor;
    }

    private async void ApplyCustomColor_Click(object sender, RoutedEventArgs e)
    {
        var candidate = CustomColorText.Text.Trim();
        if (!candidate.StartsWith('#')) candidate = "#" + candidate;
        if (candidate.Length is not (7 or 9) ||
            !candidate.Skip(1).All(Uri.IsHexDigit))
        {
            MessageBox.Show("Enter a HEX color such as #8B6CFF.", "JTG-RGB — Invalid color", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ApplyColorAsync(candidate[..7].ToUpperInvariant());
    }

    private async Task ApplyColorAsync(string colorText)
    {
        try
        {
            var rgb = (Color)ColorConverter.ConvertFromString(colorText);
            _selectedColor = $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
            SelectedColorText.Text = $"Selected: {_selectedColor}";

            var appliedToOpenRgb = false;
            var appliedToMsi = false;
            if (_backend.Client.IsConnected && _devices.Count > 0)
            {
                await _backend.Client.SetAllDevicesColorAsync(_devices, rgb.R, rgb.G, rgb.B);
                appliedToOpenRgb = true;
            }

            if (_msi.IsInitialized && _msiDevices.Count > 0)
            {
                await Task.Run(() => _msi.SetAllDevicesColor(rgb.R, rgb.G, rgb.B));
                appliedToMsi = true;
            }

            if (appliedToOpenRgb || appliedToMsi)
            {
                var targets = new List<string>();
                if (appliedToOpenRgb) targets.Add($"{_devices.Count} OpenRGB controller(s)");
                if (appliedToMsi) targets.Add($"{_msiDevices.Count} MSI lighting device(s)");
                FooterStatus.Text = $"Sent {_selectedColor} to {string.Join(" + ", targets)}. SDK accepted the command; visual output still needs confirmation.";
            }
            else
            {
                FooterStatus.Text = $"Color selected: {_selectedColor} • Connect OpenRGB or MSI Mystic Light to apply it";
            }
        }
        catch (Exception ex) when (ex is FormatException or IOException or SocketException or TimeoutException or InvalidOperationException or InvalidDataException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException)
        {
            FooterStatus.Text = "Could not apply color to all devices";
            MessageBox.Show(ex.Message, "JTG-RGB — Apply color", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task HandleMsiMotherboardStartupAsync()
    {
        if (!IsMsiMotherboardDetected()) return;

        var sdkToUse = MsiMysticLightClient.FindBundledSdkPath() ?? _msiSdkPath;
        if (!string.IsNullOrWhiteSpace(sdkToUse) && File.Exists(sdkToUse))
        {
            _msiSdkPath = sdkToUse;
            try
            {
                _msiDevices = await Task.Run(() => _msi.Connect(sdkToUse));
                FooterStatus.Text = $"MSI motherboard detected • Mystic Light connected ({_msiDevices.Count} device type(s))";
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or PlatformNotSupportedException or MarshalDirectiveException or ArgumentException)
            {
                FooterStatus.Text = "MSI motherboard detected • Mystic Light setup required";
            }
        }

        // If an SDK path is already configured (or a bundled DLL was found), do not
        // show the first-run download prompt again just because connecting failed.
        if (!string.IsNullOrWhiteSpace(sdkToUse))
        {
            FooterStatus.Text = "MSI motherboard detected • Mystic Light connection failed; check MSI Center/Mystic Light and retry from Devices or Settings.";
            return;
        }

        var answer = MessageBox.Show(
            "JTG-RGB detected an MSI motherboard. It can download the Mystic Light SDK ZIP from MSI's official download host and install the x64 DLL into your local AppData folder. This does not install MSI Center. MSI Center must also be installed with the Mystic Light feature enabled and running.\n\nYes: Download SDK now\nNo: Open MSI's page to install MSI Center\nCancel: Do this later",
            "MSI motherboard detected",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Information);

        if (answer == MessageBoxResult.Yes)
        {
            await DownloadMsiSdkAsync(connectAfterDownload: true);
        }
        else if (answer == MessageBoxResult.No)
        {
            OpenMsiMysticLightPage();
        }
    }

    private static bool IsMsiMotherboardDetected()
    {
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var manufacturer = bios?.GetValue("BaseBoardManufacturer")?.ToString() ?? string.Empty;
            return manufacturer.Contains("MSI", StringComparison.OrdinalIgnoreCase) ||
                   manufacturer.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
    private async Task DownloadMsiSdkAsync(bool connectAfterDownload)
    {
        FooterStatus.Text = "Downloading MSI Mystic Light SDK from MSI…";
        try
        {
            _msiSdkPath = await MsiMysticLightClient.DownloadSdkToAppDataAsync();
            FooterStatus.Text = "MSI SDK downloaded to your local AppData folder.";
            if (connectAfterDownload)
            {
                await ConnectMsiAsync();
            }
            else
            {
                ShowPage("Settings");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            FooterStatus.Text = "MSI SDK download failed";
            MessageBox.Show(
                $"JTG-RGB couldn't download or install the MSI SDK. No DLL was installed unless the download completed successfully.\n\nDetails: {ex.Message}\n\nYou can retry or select the SDK DLL manually.",
                "JTG-RGB — MSI SDK download",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void OpenMsiMysticLightPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.msi.com/Landing/mystic-light-rgb-gaming-pc/download") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show("Open MSI's Mystic Light download page manually: https://www.msi.com/Landing/mystic-light-rgb-gaming-pc/download", "JTG-RGB", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ChooseMsiSdkDll()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select MSI Mystic Light SDK DLL",
            Filter = "MSI Mystic Light SDK (MysticLight_SDK_x64.dll;MysticLight_SDK.dll)|MysticLight_SDK_x64.dll;MysticLight_SDK.dll|DLL files (*.dll)|*.dll",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _msiSdkPath = dialog.FileName;
            MsiMysticLightClient.SaveSdkPath(_msiSdkPath);
            FooterStatus.Text = "MSI SDK path saved. Connect to discover its lighting devices.";
            ShowPage("Settings");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "JTG-RGB — Save MSI SDK path", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private UIElement BuildMsiLedControls()
    {
        var host = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (_msiDevices.Count == 0)
        {
            host.Children.Add(Body("No MSI lighting zones loaded. Connect to the MSI SDK to see the individual zones it exposes."));
            return host;
        }

        host.Children.Add(Body("The MSI SDK reports controllable LED zones, not necessarily each physical fan or header. Use a zone's button to apply the currently selected color to that zone only."));
        foreach (var device in _msiDevices)
        {
            for (uint index = 0; index < device.LedCount; index++)
            {
                var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = Body($"{device.Type} • {device.GetLedName(index)}");
                label.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(label, 0);
                row.Children.Add(label);
                var apply = ActionButton("Apply selected color", async (_, _) =>
                {
                    try
                    {
                        var rgb = (Color)ColorConverter.ConvertFromString(_selectedColor);
                        await Task.Run(() => _msi.SetLedColor(device.Type, index, rgb.R, rgb.G, rgb.B));
                        FooterStatus.Text = $"Applied {_selectedColor} to {device.GetLedName(index)}";
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException or ArgumentException)
                    {
                        FooterStatus.Text = "Could not control the selected MSI lighting zone";
                        MessageBox.Show(ex.Message, "JTG-RGB — MSI zone control", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }, false);
                Grid.SetColumn(apply, 1);
                row.Children.Add(apply);
                host.Children.Add(row);
            }
        }

        if (_msiDevices.Count == 1 && _msiDevices[0].LedCount == 1)
            host.Children.Add(Body("Only one MSI lighting zone was reported. If your fans and RGB headers are not listed separately here, this SDK/board combination is not exposing them as separate controllable zones."));
        return host;
    }

    private async Task ConnectMsiAsync()
    {
        if (string.IsNullOrWhiteSpace(_msiSdkPath))
        {
            _msiConnectionStatus = "No MSI SDK path is configured. Choose the SDK DLL or use Download MSI SDK first.";
            FooterStatus.Text = _msiConnectionStatus;
            ShowPage(PageTitle.Text);
            MessageBox.Show(_msiConnectionStatus, "JTG-RGB — MSI Mystic Light", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(_msiSdkPath))
        {
            _msiConnectionStatus = $"The saved MSI SDK file no longer exists: {_msiSdkPath}";
            FooterStatus.Text = _msiConnectionStatus;
            ShowPage(PageTitle.Text);
            MessageBox.Show(_msiConnectionStatus + "\n\nChoose the DLL again or download it again.", "JTG-RGB — MSI Mystic Light", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var pageToReturnTo = PageTitle.Text;
        _msiConnectionStatus = "Connecting to MSI Mystic Light…";
        FooterStatus.Text = _msiConnectionStatus;
        ShowPage(pageToReturnTo);

        try
        {
            var path = _msiSdkPath;
            _msiDevices = await Task.Run(() => _msi.Connect(path));
            _msiConnectionStatus = _msiDevices.Count == 0
                ? "MSI SDK loaded but reported no lighting zones."
                : $"Connected to MSI Mystic Light: {_msiDevices.Count} device type(s), {_msiDevices.Sum(d => d.LedCount)} LED zone(s).";
            FooterStatus.Text = _msiConnectionStatus;
            ShowPage(pageToReturnTo);
        }
        catch (Exception ex)
        {
            _msiConnectionStatus = $"MSI connection failed: {ex.Message}";
            FooterStatus.Text = _msiConnectionStatus;
            ShowPage(pageToReturnTo);
            MessageBox.Show(
                $"JTG-RGB couldn't connect to the MSI Mystic Light SDK.\n\n{ex.GetType().Name}: {ex.Message}\n\nConfirm MSI Center is installed, the Mystic Light feature is installed and running, and try running JTG-RGB as administrator. If the SDK reports status -2, it timed out waiting for MSI's service.",
                "JTG-RGB — MSI Mystic Light",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
