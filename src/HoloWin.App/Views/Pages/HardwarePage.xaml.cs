using Microsoft.UI.Xaml.Controls;
namespace HoloWin.App.Views.Pages;
public sealed partial class HardwarePage : Page
{
    public HardwarePage()
    {
        InitializeComponent();
        Content = new WorkbenchPage("Hardware");
    }
}
