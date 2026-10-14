using Microsoft.UI.Xaml.Controls;
namespace HoloWin.App.Views.Pages;
public sealed partial class EvaluationPage : Page
{
    public EvaluationPage()
    {
        InitializeComponent();
        Content = new WorkbenchPage("Evaluation");
    }
}
