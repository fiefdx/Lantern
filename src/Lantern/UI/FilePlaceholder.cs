using System.Drawing.Drawing2D;

namespace Lantern.UI;

/// <summary>
/// The small page a shared file gets in the transcript (F8).
/// </summary>
/// <remarks>
/// A photo already looks like a photo, and a file looked like a sentence. That made a file the one entry in the
/// window with nothing to point at: right-clicking it meant aiming at a line of text, and a person who has just
/// seen a thumbnail beside every picture reasonably expects something beside every file too.
/// <para>
/// The picture is <em>drawn</em>, not shipped. There is no icon file in the repository to lose, to fail to copy
/// next to <c>Lantern.exe</c> (which is how the sounds nearly arrived), or to look wrong on somebody's display
/// scale; and because it is drawn from the file's own name, <c>deck.pdf</c> and <c>deck.zip</c> are visibly two
/// different things, which a single generic icon cannot manage. The cost of drawing it instead of shipping it is
/// that its colours are a decision in code, so they are named here and measured in a test like every other colour
/// the transcript is made of.
/// </para>
/// </remarks>
public static class FilePlaceholder
{
    /// <summary>Tall and narrow, the way a page is, and small enough not to shout over the message beside it.</summary>
    public const int Width = 40;

    public const int Height = 46;

    /// <summary>The ink of the outline and the extension: the transcript's own headline blue, on the transcript's own white.</summary>
    public static readonly Color InkColor = Color.FromArgb(0x1F, 0x4E, 0x79);

    /// <summary>The suggested lines of writing on the page - quiet, but above the ratio that reading costs.</summary>
    public static readonly Color RuleColor = Color.FromArgb(0x6E, 0x6E, 0x6E);

    /// <summary>
    /// The page: outline, a folded corner, two lines of suggested text, and the extension the file actually has.
    /// </summary>
    /// <remarks>
    /// Caller owns the returned bitmap; <see cref="InlineFor"/> disposes the one it makes.
    /// </remarks>
    public static Bitmap Bitmap(string fileName)
    {
        var bitmap = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.White);

        // One pixel in from the edge: a pen draws on the line it is given, so half of a line along the border
        // would be cropped and the page would arrive with three edges.
        var fold = 11;
        var page = new[]
        {
            new Point(1, 1),
            new Point(Width - fold - 2, 1),
            new Point(Width - 2, fold + 1),
            new Point(Width - 2, Height - 2),
            new Point(1, Height - 2),
        };

        using var ink = new Pen(InkColor, 1f);
        using var rule = new Pen(RuleColor, 1f);

        g.FillPolygon(Brushes.White, page);
        g.DrawPolygon(ink, page);

        // The folded corner, which is what makes a rectangle read as a document at thumbnail size.
        g.DrawLines(ink, new[]
        {
            new Point(Width - fold - 2, 1),
            new Point(Width - fold - 2, fold + 1),
            new Point(Width - 2, fold + 1),
        });

        g.DrawLine(rule, 7, 19, Width - 8, 19);
        g.DrawLine(rule, 7, 24, Width - 14, 24);

        var label = Label(fileName);
        using var font = new Font("Segoe UI", 6.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var text = new SolidBrush(InkColor);

        var measured = g.MeasureString(label, font);
        g.DrawString(
            label,
            font,
            text,
            new PointF(Math.Max(1f, (Width - measured.Width) / 2f), Height - measured.Height - 3f));

        return bitmap;
    }

    /// <summary>
    /// RTF for the transcript, or null when GDI+ could not encode the page. Null is not a failure: the entry's
    /// text line is already written, and a window that threw over an icon would lose the file and everything
    /// said after it.
    /// </summary>
    public static string? InlineFor(string fileName)
    {
        using var page = Bitmap(fileName);
        return RtfPicture.Inline(page);
    }

    /// <summary>
    /// What is written on the page: the extension in capitals, or FILE when there is no extension worth
    /// reading. Long ones go to FILE as well - six letters at this size is mud, and mud with a number of
    /// characters in it is not information.
    /// </summary>
    public static string Label(string fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).TrimStart('.');

        if (extension.Length == 0 || extension.Length > 5 || !extension.All(char.IsLetterOrDigit))
        {
            return "FILE";
        }

        return extension.ToUpperInvariant();
    }
}
