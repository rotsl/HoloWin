using Microsoft.UI.Xaml.Controls;
namespace HoloWin.App.Views.Pages;
public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        Content = new WorkbenchPage("Dashboard");
    }
}
