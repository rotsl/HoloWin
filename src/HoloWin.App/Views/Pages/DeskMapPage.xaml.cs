using Microsoft.UI.Xaml.Controls;
namespace HoloWin.App.Views.Pages;
public sealed partial class DeskMapPage : Page
{
    public DeskMapPage()
    {
        InitializeComponent();
        Content = new WorkbenchPage("Desk Map");
    }
}
