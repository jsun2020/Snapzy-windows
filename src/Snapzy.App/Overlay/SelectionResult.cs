using System.Drawing;

namespace Snapzy.App.Overlay;

/// <summary>What the user chose on the post-selection floating toolbar.</summary>
public enum OverlayAction
{
    Confirm,   // normal screenshot (Enter / double-click / check button)
    Annotate,  // open the annotation editor
    Ocr,       // copy recognized text, no file saved
    Record,    // start recording the selected region
    Scroll,    // long screenshot: scroll-capture the window under the selection
}

public class SelectionResult
{
    public Rectangle Rect { get; set; }          // physical virtual-screen pixels
    public IntPtr Hwnd { get; set; } = IntPtr.Zero; // non-zero when a window was snapped
    public OverlayAction Action { get; set; } = OverlayAction.Confirm;
}
