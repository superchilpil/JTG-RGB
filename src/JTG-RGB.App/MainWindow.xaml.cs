using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JtgRgb.App;

public partial class MainWindow : Window
{
    private string _selectedColor = "#8B6CFF";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => FooterStatus.Text = "Ready • Backend integration is the next milestone";
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string page }) return;
        PageTitle.Text = page;
        PageSubtitle.Text = page switch
        {
            "Dashboard" => "Your lighting setup at a glance",
            "Devices" => "Discover and configure supported hardware",
            "Effects" => "Choose and customize lighting animations",
            "Layouts" => "Arrange device LEDs on your shared canvas",
            "Profiles" => "Save and switch between lighting setups",
            "Settings" => "Configure JTG-RGB and its hardware backend",
            _ => "JTG-RGB"
        };

        FooterStatus.Text = page switch
        {
            "Devices" => "Device discovery will be enabled after OpenRGB integration.",
            "Effects" => "Built-in effects engine is planned for milestone 3.",
            "Layouts" => "Device layout editor is planned for milestone 3.",
            "Profiles" => "Profiles will be persisted locally.",
            "Settings" => "OpenRGB and PawnIO setup checks are not yet implemented.",
            _ => "Ready • Backend integration is the next milestone"
        };

        if (page != "Dashboard")
            MessageBox.Show($"{page} is part of the JTG-RGB roadmap. The current build establishes the application shell; hardware integration is being added next.", "JTG-RGB — Early Development", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        BackendStatusText.Text = "Not connected";
        BackendDetailText.Text = "Hardware discovery is not implemented in this shell build.";
        DeviceCountText.Text = "—";
        FooterStatus.Text = "Refresh requested • OpenRGB backend integration is pending";
        MessageBox.Show("The interface is ready, but this initial commit does not yet bundle or launch OpenRGB. Device discovery will be added in the hardware integration milestone.", "JTG-RGB", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExploreEffects_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "Effects";
        PageSubtitle.Text = "Lighting animations for your setup";
        FooterStatus.Text = "Effects engine planned • Audio-reactive effects are not part of the initial scope";
    }

    private void ViewDevices_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "Devices";
        PageSubtitle.Text = "Discover and configure supported hardware";
        FooterStatus.Text = "OpenRGB integration pending • No hardware commands are issued by this build";
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color }) return;
        _selectedColor = color;
        SelectedColorText.Text = $"Selected: {_selectedColor}";
        FooterStatus.Text = $"Color selected: {_selectedColor} • Not applied: hardware control is not connected";
    }
}
