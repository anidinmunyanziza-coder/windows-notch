using System.Globalization;
using System.Windows.Controls;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>Brief "charging" live activity shown on the closed pill when the
/// charger is plugged in.</summary>
public partial class ClosedChargingActivity : UserControl
{
    public ClosedChargingActivity(int percent)
    {
        InitializeComponent();
        PercentText.Text = percent + "%";
    }
}
