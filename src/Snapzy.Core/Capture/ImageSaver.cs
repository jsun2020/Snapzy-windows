using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace Snapzy.Core.Capture;

public static class ImageSaver
{
    public static void Save(Bitmap bmp, string path, string format, string ffmpegExe)
    {
        switch (format)
        {
            case "jpg":
                var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (var p = new EncoderParameters(1))
                {
                    p.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
                    bmp.Save(path, enc, p);
                }
                break;
            case "webp":
                var tmp = path + ".tmp.png";
                bmp.Save(tmp, ImageFormat.Png);
                try
                {
                    var psi = new ProcessStartInfo(ffmpegExe) { CreateNoWindow = true, UseShellExecute = false };
                    foreach (var a in Recording.FfmpegArgs.BuildWebpArgs(tmp, path)) psi.ArgumentList.Add(a);
                    using var proc = Process.Start(psi)
                        ?? throw new InvalidOperationException("ffmpeg failed to start");
                    proc.WaitForExit(30_000);
                    if (proc.ExitCode != 0 || !File.Exists(path))
                        throw new InvalidOperationException($"ffmpeg webp encode failed (exit {proc.ExitCode})");
                }
                finally { File.Delete(tmp); }
                break;
            default:
                bmp.Save(path, ImageFormat.Png);
                break;
        }
    }
}
