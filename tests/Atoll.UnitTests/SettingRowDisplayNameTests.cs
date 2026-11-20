using Atoll.UI.Settings;
using Xunit;

namespace Atoll.UnitTests;

public class SettingRowDisplayNameTests
{
    [Theory]
    [InlineData("updateChannel", "Update Channel")]
    [InlineData("menubarIcon", "Menubar Icon")]
    [InlineData("showOnAllDisplays", "Show On All Displays")]
    [InlineData("hideDynamicIslandFromScreenCapture", "Hide Dynamic Island From Screen Capture")]
    [InlineData("cachedMusicLiveActivityPreference", "Cached Music Live Activity Preference")]
    [InlineData("enableHUD", "Enable HUD")]
    [InlineData("releaseName", "Release Name")]
    public void DeriveDisplayName_HumanisesSchemaKeys(string key, string expected)
    {
        Assert.Equal(expected, SettingRow.DeriveDisplayName(key));
    }

    [Fact]
    public void DeriveDisplayName_HandlesEmpty()
    {
        Assert.Equal(string.Empty, SettingRow.DeriveDisplayName(string.Empty));
    }
}
