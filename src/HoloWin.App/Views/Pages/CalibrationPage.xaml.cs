using Microsoft.UI.Xaml.Controls;
namespace HoloWin.App.Views.Pages;
public sealed partial class CalibrationPage : Page
{
    public CalibrationPage()
    {
        InitializeComponent();
        Content = new WorkbenchPage("Calibration");
    }
}
