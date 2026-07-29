using System.Drawing;
using System.Drawing.Imaging;
using Snapzy.Core.Capture;

// Named "SessionTests" so the desktop-locked test filter (!~CaptureTests, which
// excludes the live-screen CaptureTests class) cannot swallow this class.
public class ManualScrollSessionTests
{
    // Same deterministic scrolled-view model as ImageStitcherTests: each content
    // row encodes its document row in the pixel color.
    private static Bitmap View(int docStartRow, int width = 40, int height = 200, int furnitureRows = 0)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            Color c;
            if (y >= height - furnitureRows)
            {
                c = Color.FromArgb(255, 50, 50, 50);
            }
            else
            {
                var doc = docStartRow + y;
                c = Color.FromArgb(255, doc % 256, (doc / 256) % 256, (doc / 65536) % 256);
            }
            for (var x = 0; x < width; x++) bmp.SetPixel(x, y, c);
        }
        return bmp;
    }

    [Fact]
    public void Tick_ScrolledFrame_AppendsNewRows()
    {
        using var session = new ManualScrollCapture(View(0));
        session.Tick(View(120)); // 80 rows overlap, 120 new
        Assert.Equal(ManualScrollCapture.TrackState.Tracking, session.State);
        Assert.Equal(1, session.StepsAppended);
        Assert.Equal(320, session.Height); // 200 + 120
    }

    [Fact]
    public void Tick_IdenticalFrame_ReportsNoMovement()
    {
        using var session = new ManualScrollCapture(View(0));
        session.Tick(View(0));
        Assert.Equal(ManualScrollCapture.TrackState.NoMovement, session.State);
        Assert.Equal(0, session.StepsAppended);
        Assert.Equal(200, session.Height);
    }

    [Fact]
    public void Tick_OverJump_ReportsLost_ThenRecovers()
    {
        using var session = new ManualScrollCapture(View(0));
        session.Tick(View(5000)); // user jumped way past the viewport
        Assert.Equal(ManualScrollCapture.TrackState.Lost, session.State);
        Assert.Equal(200, session.Height); // nothing appended

        session.Tick(View(120)); // user scrolled back to an overlapping position
        Assert.Equal(ManualScrollCapture.TrackState.Tracking, session.State);
        Assert.Equal(320, session.Height);
    }

    [Fact]
    public void Tick_ScrollUpThenDown_KeepsStitchConsistent()
    {
        using var session = new ManualScrollCapture(View(0));
        session.Tick(View(120));   // down: 200 + 120 = 320
        session.Tick(View(0));     // back up: last-appended's tail (doc 319) not visible -> Lost
        Assert.Equal(ManualScrollCapture.TrackState.Lost, session.State);
        session.Tick(View(240));   // down past the previous position: overlap 80, 120 new
        Assert.Equal(ManualScrollCapture.TrackState.Tracking, session.State);
        Assert.Equal(440, session.Height);
        Assert.Equal(2, session.StepsAppended);
    }

    [Fact]
    public void Tick_FurnitureBand_TrimmedOnceFromFirstFrame()
    {
        using var session = new ManualScrollCapture(View(0, furnitureRows: 36));
        session.Tick(View(120, furnitureRows: 36));
        // initial content 164 rows (200-36) + 120 new rows appended
        Assert.Equal(284, session.Height);
    }

    [Fact]
    public void Tick_CountsDiagnosticsPerOutcome()
    {
        using var session = new ManualScrollCapture(View(0));
        session.Tick(View(120));   // append
        session.Tick(View(120));   // no movement
        session.Tick(View(5000));  // lost
        session.Tick(View(240));   // append (recovered)
        Assert.Equal(2, session.StepsAppended);
        Assert.Equal(1, session.NoMoveTicks);
        Assert.Equal(1, session.LostTicks);
    }

    [Fact]
    public void Detach_ReturnsStitchWithSequentialContent()
    {
        var session = new ManualScrollCapture(View(0));
        session.Tick(View(120));
        session.Tick(View(240));
        using var img = session.Detach();
        Assert.Equal(440, img.Height);
        // Spot-check that every 40th row encodes its own document row.
        for (var y = 0; y < img.Height; y += 40)
        {
            var p = img.GetPixel(0, y);
            var doc = p.R + p.G * 256 + p.B * 65536;
            Assert.Equal(y, doc);
        }
    }
}
