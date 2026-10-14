using Microsoft.UI.Xaml.Controls;
namespace HoloWin.App.Views.Pages;
public sealed partial class CameraStudioPage : Page
{
    public CameraStudioPage()
    {
        InitializeComponent();
        Content = new WorkbenchPage("Camera Studio");
    }
}
