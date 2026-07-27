using Snapzy.Core.Localization;

public class LocalizationTests
{
    [Fact]
    public void Get_KnownKey_ReturnsEnglishByDefault()
    {
        Strings.SetLanguage("en");
        Assert.Equal("Settings", Strings.Get("Tray_Settings"));
    }

    [Fact]
    public void Get_SwitchesToChineseAndBack()
    {
        Strings.SetLanguage("zh-CN");
        Assert.NotEqual(Strings.Get("Tray_Settings"), "Settings"); // zh value differs
        Strings.SetLanguage("en");
        Assert.Equal("Settings", Strings.Get("Tray_Settings"));
    }

    [Fact]
    public void Get_UnknownKey_ReturnsKeyItself() =>
        Assert.Equal("No_Such_Key", Strings.Get("No_Such_Key"));
}
