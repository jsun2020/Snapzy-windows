namespace Snapzy.Core.Recording;

public static class FfmpegArgs
{
    public static List<string> BuildRecordArgs(RecordingOptions o, string outMp4)
    {
        var args = new List<string> { "-y", "-hide_banner" };
        var mouse = o.Cursor ? 1 : 0;
        if (o.UseGdigrab)
        {
            args.AddRange(new[]
            {
                "-f", "gdigrab", "-framerate", o.Fps.ToString(), "-draw_mouse", mouse.ToString(),
                "-offset_x", o.OffsetX.ToString(), "-offset_y", o.OffsetY.ToString(),
                "-video_size", $"{o.Width}x{o.Height}", "-i", "desktop",
            });
        }
        else
        {
            var graph = $"ddagrab=framerate={o.Fps}:draw_mouse={mouse}" +
                        $":offset_x={o.OffsetX}:offset_y={o.OffsetY}:video_size={o.Width}x{o.Height}" +
                        ",hwdownload,format=bgra";
            args.AddRange(new[] { "-f", "lavfi", "-i", graph });
        }
        if (!string.IsNullOrEmpty(o.MicDevice))
            args.AddRange(new[] { "-f", "dshow", "-i", "audio=" + o.MicDevice, "-c:a", "aac", "-b:a", "128k" });
        args.AddRange(new[]
        {
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
            "-pix_fmt", "yuv420p", "-movflags", "+faststart", outMp4,
        });
        return args;
    }

    public static List<string> BuildConcatArgs(string listFile, string outMp4) => new()
    { "-y", "-hide_banner", "-f", "concat", "-safe", "0", "-i", listFile, "-c", "copy", outMp4 };

    public static List<string> BuildGifArgs(string inMp4, string outGif) => new()
    {
        "-y", "-hide_banner", "-i", inMp4, "-filter_complex",
        "[0:v]fps=15,scale=iw:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle",
        outGif,
    };

    public static List<string> BuildWebpArgs(string inPng, string outWebp) => new()
    { "-y", "-hide_banner", "-i", inPng, "-c:v", "libwebp", "-quality", "90", outWebp };

    public static List<string> BuildAnimatedWebpArgs(string inMp4, string outWebp) => new()
    {
        "-y", "-hide_banner", "-i", inMp4,
        "-vf", "fps=15", "-c:v", "libwebp", "-lossless", "0", "-q:v", "75", "-loop", "0",
        outWebp,
    };

    public static List<string> BuildMuxArgs(string videoMp4, string audioWav, string outMp4, bool mixWithExistingAudio)
    {
        if (mixWithExistingAudio)
        {
            return new()
            {
                "-y", "-hide_banner", "-i", videoMp4, "-i", audioWav,
                "-filter_complex", "[0:a][1:a]amix=inputs=2:duration=first[a]",
                "-map", "0:v", "-map", "[a]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
                outMp4,
            };
        }
        return new()
        {
            "-y", "-hide_banner", "-i", videoMp4, "-i", audioWav,
            "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest",
            outMp4,
        };
    }
}
