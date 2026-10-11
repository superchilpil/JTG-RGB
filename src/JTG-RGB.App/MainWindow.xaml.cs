using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Net.Sockets;
using JtgRgb.App.Hardware;

namespace JtgRgb.App;

public partial class MainWindow : Window
{
    private string _selectedColor = "#8B6CFF";
    private string _selectedEffect = "Static Color";
    private string _selectedProfile = "Default";
    private readonly OpenRgbBackendManager _backend = new();
    private IReadOnlyList<OpenRgbDevice> _devices = [];
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
        };
        Closed += async (_, _) => await _backend.DisposeAsync();
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
        root.Children.Add(SectionHeading("Effects library", "Start with simple, customizable lighting effects. Audio-reactive effects are intentionally not a priority."));
        var palette = new WrapPanel { Margin = new Thickness(0, 4, 0, 18) };
        foreach (var effect in new[] { ("Static Color", "A clean, consistent color across your devices.", "#8B6CFF"), ("Color Cycle", "A smooth transition through your chosen colors.", "#36C5F0"), ("Breathing", "A gentle pulse between dim and bright.", "#42D392"), ("Rainbow Wave", "A moving spectrum across the lighting canvas.", "#FF637D"), ("Gradient", "Blend two or more colors across your setup.", "#F4B94F"), ("Ambient", "A subtle low-motion lighting preset.", "#F4F5FA") })
        {
            var button = new Button
            {
                Width = 218,
                Height = 154,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(14),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(27, 30, 41))
            };
            var panel = new StackPanel();
            var swatch = new Border { Height = 48, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(effect.Item3)), Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(swatch);
            panel.Children.Add(new TextBlock { Text = effect.Item1, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Brushes.White });
            panel.Children.Add(new TextBlock { Text = effect.Item2, TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = MakeBrush("#969EB1"), Margin = new Thickness(0, 5, 0, 0) });
            button.Content = panel;
            button.Click += (_, _) =>
            {
                _selectedEffect = effect.Item1;
                ActiveEffectText.Text = _selectedEffect;
                FooterStatus.Text = $"Effect selected: {_selectedEffect} • Rendering and hardware output are not connected yet";
            };
            palette.Children.Add(button);
        }
        root.Children.Add(palette);
        var note = Card();
        note.Child = new TextBlock { Text = "COMMUNITY EFFECTS  •  We'll investigate compatible effect imports after the built-in effects engine is working. Imported effects will be checked for redistribution and licensing requirements.", TextWrapping = TextWrapping.Wrap, Foreground = MakeBrush("#B4B9CA"), FontSize = 12 };
        root.Children.Add(note);
        return root;
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
            if (!_backend.Client.IsConnected || _devices.Count == 0)
            {
                FooterStatus.Text = $"Color selected: {_selectedColor} • Connect to OpenRGB to apply it";
                return;
            }

            await _backend.Client.SetAllDevicesColorAsync(_devices, rgb.R, rgb.G, rgb.B);
            FooterStatus.Text = $"Applied {_selectedColor} to {_devices.Count} controller(s)";
        }
        catch (Exception ex) when (ex is FormatException or IOException or SocketException or TimeoutException or InvalidOperationException or InvalidDataException)
        {
            FooterStatus.Text = "Could not apply color to all devices";
            MessageBox.Show(ex.Message, "JTG-RGB — Apply color", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
