using System.Drawing;

namespace Snapzy.App.Overlay;

public class SelectionResult
{
    public Rectangle Rect { get; set; }          // physical virtual-screen pixels
    public IntPtr Hwnd { get; set; } = IntPtr.Zero; // non-zero when a window was snapped
}
