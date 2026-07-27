using Snapzy.Core.Recording;

public class FfmpegArgsTests
{
    private static RecordingOptions Opts(bool gdigrab = false, string mic = "") => new()
    { OffsetX = 100, OffsetY = 200, Width = 1280, Height = 720, Fps = 30, Cursor = true, MicDevice = mic, UseGdigrab = gdigrab };

    [Fact]
    public void Record_Ddagrab_BuildsLavfiGraph()
    {
        var a = FfmpegArgs.BuildRecordArgs(Opts(), "out.mp4");
        var joined = string.Join(" ", a);
        Assert.Contains("-f lavfi", joined);
        Assert.Contains("ddagrab=framerate=30:draw_mouse=1:offset_x=100:offset_y=200:video_size=1280x720", joined);
        Assert.Contains("hwdownload", joined);
        Assert.Contains("-c:v libx264", joined);
        Assert.Contains("-pix_fmt yuv420p", joined);
        Assert.Equal("out.mp4", a[^1]);
        Assert.DoesNotContain("-f dshow", joined);
    }

    [Fact]
    public void Record_WithMic_AddsDshowAudio()
    {
        var a = FfmpegArgs.BuildRecordArgs(Opts(mic: "Microphone (USB)"), "out.mp4");
        var joined = string.Join(" ", a);
        Assert.Contains("-f dshow", joined);
        Assert.Contains("audio=Microphone (USB)", joined);
        Assert.Contains("-c:a aac", joined);
    }

    [Fact]
    public void Record_Gdigrab_UsesDesktopInput()
    {
        var a = FfmpegArgs.BuildRecordArgs(Opts(gdigrab: true), "out.mp4");
        var joined = string.Join(" ", a);
        Assert.Contains("-f gdigrab", joined);
        Assert.Contains("-offset_x 100", joined);
        Assert.Contains("-video_size 1280x720", joined);
        Assert.Contains("desktop", joined);
    }

    [Fact]
    public void Gif_UsesTwoPassPalette()
    {
        var joined = string.Join(" ", FfmpegArgs.BuildGifArgs("in.mp4", "out.gif"));
        Assert.Contains("palettegen", joined);
        Assert.Contains("paletteuse", joined);
        Assert.Contains("fps=15", joined);
    }

    [Fact]
    public void Concat_UsesCopyCodec()
    {
        var joined = string.Join(" ", FfmpegArgs.BuildConcatArgs("list.txt", "out.mp4"));
        Assert.Contains("-f concat", joined);
        Assert.Contains("-safe 0", joined);
        Assert.Contains("-c copy", joined);
    }
}
