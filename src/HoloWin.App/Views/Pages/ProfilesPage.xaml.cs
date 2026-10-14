using Microsoft.UI.Xaml.Controls;
namespace HoloWin.App.Views.Pages;
public sealed partial class ProfilesPage : Page
{
    public ProfilesPage()
    {
        InitializeComponent();
        Content = new WorkbenchPage("Profiles");
    }
}
