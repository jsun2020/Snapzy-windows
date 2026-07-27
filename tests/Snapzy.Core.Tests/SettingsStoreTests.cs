using Snapzy.Core.Settings;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("snapzy-test").FullName;
    private string File => Path.Combine(_dir, "portable.json");
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = SettingsStore.Load(File);
        Assert.Equal("en", s.Language);
        Assert.Equal("png", s.ImageFormat);
        Assert.Equal(30, s.RecordingFps);
        Assert.Equal("Ctrl+Shift+3", s.Hotkeys["CaptureFullscreen"].Gesture);
        Assert.True(s.Hotkeys["CaptureFullscreen"].Enabled);
        Assert.Equal(6, s.Hotkeys.Count);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var s = AppSettings.CreateDefault();
        s.Language = "zh-CN";
        s.RetentionDays = 30;
        s.Hotkeys["OpenHistory"].Gesture = "Ctrl+Alt+H";
        SettingsStore.Save(s, File);
        var loaded = SettingsStore.Load(File);
        Assert.Equal("zh-CN", loaded.Language);
        Assert.Equal(30, loaded.RetentionDays);
        Assert.Equal("Ctrl+Alt+H", loaded.Hotkeys["OpenHistory"].Gesture);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsAndBacksUp()
    {
        System.IO.File.WriteAllText(File, "{not json!!");
        var s = SettingsStore.Load(File);
        Assert.Equal("en", s.Language);
        Assert.Single(Directory.GetFiles(_dir, "portable.json.bad-*"));
    }
}
