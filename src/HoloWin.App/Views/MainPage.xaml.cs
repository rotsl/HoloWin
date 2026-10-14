using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HoloWin.App.Views.Pages;

namespace HoloWin.App.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (NavigationView.SelectedItem is null)
        {
            NavigationView.SelectedItem = NavigationView.MenuItems[0];
        }
        else if (ContentFrame.Content is null)
        {
            Navigate("Dashboard");
        }
    }

    private void OnSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item &&
            item.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        string? section = tag switch
        {
            "Dashboard"   => "Dashboard",
            "DeskMap"      => "Desk Map",
            "CameraStudio" => "Camera Studio",
            "Calibration"  => "Calibration",
            "Actions"      => "Actions",
            "Hardware"     => "Hardware",
            "Profiles"     => "Profiles",
            "Evaluation"   => "Evaluation",
            "Settings"     => "Settings",
            _              => null
        };

        if (section is not null)
        {
            ContentFrame.Content = new WorkbenchPage(section);
        }
    }
}
