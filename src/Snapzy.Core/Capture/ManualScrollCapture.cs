using System.Drawing;

namespace Snapzy.Core.Capture;

/// <summary>
/// Incremental stitcher for user-driven long screenshots: the USER scrolls the
/// window (wheel, scrollbar drag, keys - anything), we periodically capture the
/// client area and append whatever new content appeared at the bottom. This
/// avoids every failure mode of injected scrolling (apps that ignore synthetic
/// input, pages already at the bottom, animations outrunning the stitcher) -
/// the user sees what happens and clicks Save when done.
///
/// Feed frames via Tick (takes ownership of the bitmap). Over-jumps do not
/// abort: the session reports Lost and recovers as soon as an overlapping
/// frame appears again (scroll back a little). Not thread-safe; drive from a
/// single capture loop.
/// </summary>
public sealed class ManualScrollCapture : IDisposable
{
    public enum TrackState
    {
        /// <summary>New content was appended by the latest frame.</summary>
        Tracking,
        /// <summary>Latest frame showed nothing new (page idle or scrolled up).</summary>
        NoMovement,
        /// <summary>Latest frame no longer overlaps the captured content - the
        /// user jumped too far; scrolling back re-establishes tracking.</summary>
        Lost,
    }

    public const int MaxHeight = 20000;

    private Bitmap _accumulated;
    private Bitmap _lastAppended;
    private Bitmap? _lastLost;
    private bool _furnitureTrimmed;
    private bool _disposed;

    public TrackState State { get; private set; } = TrackState.NoMovement;
    public int StepsAppended { get; private set; }
    /// <summary>Ticks that showed no new content (diagnostics).</summary>
    public int NoMoveTicks { get; private set; }
    /// <summary>Ticks whose frame did not overlap the stitch (diagnostics: a
    /// high count with zero appends means the user out-scrolled the sampler).</summary>
    public int LostTicks { get; private set; }
    public int Height => _accumulated.Height;
    public bool IsFull => _accumulated.Height >= MaxHeight;

    /// <summary>Takes ownership of the initial frame (current viewport).</summary>
    public ManualScrollCapture(Bitmap initialFrame)
    {
        _accumulated = initialFrame;
        _lastAppended = (Bitmap)initialFrame.Clone();
    }

    /// <summary>Processes the next captured frame (takes ownership).</summary>
    public void Tick(Bitmap current)
    {
        if (_disposed || IsFull) { current.Dispose(); return; }

        var match = ImageStitcher.FindOverlap(_lastAppended, current);
        if (match is null)
        {
            State = TrackState.Lost;
            LostTicks++;
            _lastLost?.Dispose();
            _lastLost = current; // retained for diagnostics (TakeLastLostFrame)
            return;
        }
        var (offset, furniture) = match.Value;
        if (offset >= current.Height - furniture)
        {
            // Fully overlapping = no new content. Scrolling UP also lands here
            // or in Lost; either way nothing is appended and nothing is broken.
            State = TrackState.NoMovement;
            NoMoveTicks++;
            current.Dispose();
            return;
        }
        if (!_furnitureTrimmed && furniture > 0)
        {
            // Drop the static bottom band (scrollbar/padding) from the first frame.
            var cropped = ImageStitcher.CropBottom(_accumulated, furniture);
            _accumulated.Dispose();
            _accumulated = cropped;
        }
        _furnitureTrimmed = true;
        var grown = ImageStitcher.AppendNewRows(_accumulated, current, offset, furniture);
        _accumulated.Dispose();
        _accumulated = grown;
        _lastAppended.Dispose();
        _lastAppended = current;
        StepsAppended++;
        State = TrackState.Tracking;
    }

    /// <summary>The most recent frame that failed to overlap the stitch, for
    /// diagnostics. Caller takes ownership; call BEFORE Detach/Dispose.</summary>
    public Bitmap? TakeLastLostFrame()
    {
        var f = _lastLost;
        _lastLost = null;
        return f;
    }

    /// <summary>Returns the stitched image; the caller takes ownership and the
    /// session must not be used afterwards (Dispose is then a no-op for it).</summary>
    public Bitmap Detach()
    {
        var img = _accumulated;
        _accumulated = null!;
        _lastAppended.Dispose();
        _lastLost?.Dispose();
        _lastLost = null;
        _disposed = true;
        return img;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accumulated.Dispose();
        _lastAppended.Dispose();
        _lastLost?.Dispose();
    }
}
