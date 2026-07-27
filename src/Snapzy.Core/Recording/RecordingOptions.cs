namespace Snapzy.Core.Recording;

public class RecordingOptions
{
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; } = 30;
    public bool Cursor { get; set; } = true;
    public string MicDevice { get; set; } = "";
    public bool UseGdigrab { get; set; }
}
