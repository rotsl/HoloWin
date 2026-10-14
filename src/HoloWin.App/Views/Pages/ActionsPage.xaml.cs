using Microsoft.UI.Xaml.Controls;
namespace HoloWin.App.Views.Pages;
public sealed partial class ActionsPage : Page
{
    public ActionsPage()
    {
        InitializeComponent();
        Content = new WorkbenchPage("Actions");
    }
}
