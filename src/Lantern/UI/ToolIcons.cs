using System.Drawing.Drawing2D;

namespace Lantern.UI;

/// <summary>
/// The pictures the strip above the message box draws for itself.
/// <para>
/// A tool whose face is a word has to be read before it is understood, and a strip of seven words is a strip nobody
/// reads. Two of the tools say what they are with a shape instead - a face for the emoji table, a sheet of paper for
/// sending a file - and both shapes are drawn here from nothing but lines.
/// </para>
/// <para>
/// Drawn rather than shipped as an image resource for three reasons: it takes the window's own text colour, so it is
/// still visible on a theme nobody has seen a picture of; it comes out sharp at whatever size the button is, on
/// whatever scale the monitor is; and eight lines of geometry can be read in a review, which a <c>.png</c> cannot.
/// </para>
/// </summary>
public static class ToolIcons
{
    /// <summary>
    /// A sheet of paper with its top-right corner folded over, which is what every file listing in the world has
    /// meant by "a file" since there were file listings.
    /// </summary>
    /// <remarks>
    /// The fold is drawn as its own two lines rather than left out: a rectangle with a corner missing is a rectangle
    /// with a corner missing, and it is the fold that says paper.
    /// </remarks>
    /// <param name="pixels">The square to draw in, as <see cref="ComposerStyle.PicturePixels"/> measures it.</param>
    public static Bitmap Page(int pixels)
    {
        if (pixels < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(pixels), pixels, "a page smaller than eight pixels is a dot");
        }

        var bitmap = new Bitmap(pixels, pixels);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        // The window's own text colour, so the icon is as readable as the sentence beside it on any theme.
        using var pen = new Pen(SystemColors.ControlText, Math.Max(1f, pixels / 14f))
        {
            LineJoin = LineJoin.Miter,
        };

        // Inset by half the pen, so the outside of the stroke is the edge of the picture rather than outside it.
        var edge = (pen.Width / 2f) + 0.5f;
        var left = edge;
        var top = edge;
        var right = pixels - 1 - edge;
        var bottom = pixels - 1 - edge;
        var fold = Math.Max(3f, (right - left) * 0.32f);

        graphics.DrawLines(pen,
        [
            new PointF(left, top),
            new PointF(right - fold, top),
            new PointF(right, top + fold),
            new PointF(right, bottom),
            new PointF(left, bottom),
            new PointF(left, top),
        ]);

        graphics.DrawLines(pen,
        [
            new PointF(right - fold, top),
            new PointF(right - fold, top + fold),
            new PointF(right, top + fold),
        ]);

        // Two lines of writing, and only at a size where two lines are two lines: at eighteen pixels they merge into
        // a smudge across the page, which reads as dirt rather than as words.
        if (pixels >= 22)
        {
            var first = top + fold + (bottom - top - fold) * 0.32f;
            var apart = (bottom - first) * 0.5f;

            graphics.DrawLine(pen, left + (fold * 0.4f), first, right - (fold * 0.4f), first);
            graphics.DrawLine(pen, left + (fold * 0.4f), first + apart, right - (fold * 0.4f), first + apart);
        }

        return bitmap;
    }
}
