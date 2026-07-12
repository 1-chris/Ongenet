using Avalonia.Controls;

namespace Ongenet.App.Views.Panels;

public partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
        Ongenet.App.Accessibility.A11y.Landmark(this,
            Ongenet.App.Localization.Loc.Get("A11y_SessionView_Name"),
            Ongenet.App.Localization.Loc.Get("A11y_SessionView_Help"));
    }
}
