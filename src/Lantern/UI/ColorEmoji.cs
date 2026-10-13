using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Lantern.UI;

/// <summary>
/// The colourful version of an emoji, as Windows draws it.
/// </summary>
/// <remarks>
/// <para>
/// WinForms draws text with GDI, and GDI has no colour-glyph path at all: asked for the bitmap of <c>U+1F600</c>
/// it returns nothing whatsoever. Windows' emoji live in <c>seguiemj.ttf</c> as a COLR/CPAL font - coloured layers
/// plus a palette - and only a DirectWrite-class rasteriser resolves those layers into colours. Microsoft says as
/// much about its own GDI-compatible text interfaces: they do not do colour fonts.
/// </para>
/// <para>
/// So the colourful face is not drawn as text in the transcript. It is rasterised here, into a PNG at the height of
/// the line it will sit on, and inserted as one of the inline pictures the transcript already knows how to carry
/// (see <see cref="RtfPicture"/>). The artwork is Windows' own - the same font the <b>Win + .</b> picker reads - so
/// a face that was chosen from that popup is the face that appears, only in colour.
/// </para>
/// <para>
/// Everything here is best-effort by design. If the font is not on the machine, or Skia cannot load it, or one
/// particular element has no glyph, the caller is told with a null and writes the emoji as ordinary text: an outline
/// face is a worse-looking message, and losing the message would be a worse bug.
/// </para>
/// </remarks>
public static class ColorEmoji
{
    /// <summary>The font the Windows emoji picker uses. Measured on this machine: COLR, colour, 2 MB.</summary>
    public static string FontPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "seguiemj.ttf");

    private static readonly SKTypeface? typeface = Load();
    private static readonly Dictionary<string, byte[]?> cache = [];
    private static readonly Dictionary<string, Face?> faces = [];
    private static readonly object gate = new();

    /// <summary>True when this machine has Windows' emoji font and Skia could open it.</summary>
    public static bool Available => typeface is not null;

    /// <summary>
    /// Whether a text element should be put on screen as a picture rather than as text.
    /// </summary>
    /// <remarks>
    /// The ranges are the emoji blocks, not "everything above ASCII": <c>ê</c>, <c>ế</c> and every other letter a
    /// person types are in the supplementary planes too, and turning those into pictures would break the sentence
    /// for the sake of a face. A joined sequence (family, flag, skin tone) is one element by the same ruler the
    /// message length uses - <see cref="Domain.ValidationRules.Length"/> - so it becomes one picture.
    /// </remarks>
    public static bool IsEmoji(string element)
    {
        if (element.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < element.Length;)
        {
            var codePoint = char.ConvertToUtf32(element, i);
            i += char.IsHighSurrogate(element[i]) ? 2 : 1;

            var isEmoji = codePoint >= 0x1F000
                || codePoint is >= 0x2600 and <= 0x27BF       // ☀ ☔ ✂ ❤ and friends
                || codePoint is >= 0x2B00 and <= 0x2BFF       // ⬛ ⬜ and arrows
                || codePoint == 0xFE0F                        // the "give me the emoji, not the text" mark
                || codePoint == 0x200D;                       // the joiner that makes one picture of several

            if (!isEmoji)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A piece of a message at a time, each one flagged as emoji or not, with neighbours of the same kind joined.
    /// </summary>
    /// <remarks>
    /// Split by text element rather than by char, because a face is two chars: cutting between the halves of a
    /// surrogate pair is how a face becomes two nonsense characters on the way past.
    /// </remarks>
    public static IEnumerable<(string Piece, bool Emoji)> Split(string text)
    {
        var run = new StringBuilder();
        var runIsEmoji = false;

        foreach (var element in Elements(text))
        {
            var emoji = IsEmoji(element);

            if (run.Length > 0 && emoji != runIsEmoji)
            {
                yield return (run.ToString(), runIsEmoji);
                run.Clear();
            }

            runIsEmoji = emoji;
            run.Append(element);
        }

        if (run.Length > 0)
        {
            yield return (run.ToString(), runIsEmoji);
        }
    }

    /// <summary>
    /// The RTF that puts these emoji into a RichTextBox, each one <paramref name="pixels"/> across and standing on
    /// the bottom of a box <paramref name="tall"/> tall, or null when this machine cannot produce a colour face - in
    /// which case the caller writes the characters instead.
    /// </summary>
    /// <remarks>
    /// One picture per text element, even when several arrive together. Two faces in a row are two cells: drawing
    /// both into one cell would either squash them or put the second one outside the bitmap, where the person typing
    /// them would wait some time for it to appear. Several of those cells go into <b>one</b> RTF document rather than
    /// being concatenated as several, which is the difference between three faces arriving and one arriving - see
    /// <see cref="RtfPicture.Document"/>.
    /// <para>
    /// <b>Why the box is asked for separately from the face.</b> A picture goes where its control puts it, and the
    /// one thing RichEdit will not do is set a picture down on the line's baseline: measured on six lines, the top of
    /// every picture landed on the top of the line it was on, whether that line was 14, 18 or 25 pixels tall. A face
    /// 18 tall therefore always ended 15 pixels down, while the letters of the line it was standing among always ended
    /// one pixel above that line's own height - 13, 17, 24. Beside Large words that is a face eight pixels in the
    /// air; a small face beside ordinary words is six in the air the other way, and nothing about the message tells a
    /// person which they are looking at.
    /// </para>
    /// <para>
    /// So rather than work out where the baseline is - which GDI's own metrics do not say, measured - the picture is
    /// made as tall as the tallest letters on the line and carries its ink near the bottom of that. Two pixels above
    /// the bottom of a line's height is where letters stop: 16 down on a line 18 tall and 23 down on a line 25 tall,
    /// measured, which is the room's own size and Large both. At Small the same arithmetic leaves a face one pixel
    /// high, which is as near as arithmetic about a font gets at 14 pixels. The face is not stretched to fill the box:
    /// it is the same picture of a face at the same size, standing lower.
    /// </para>
    /// </remarks>
    public static string? InlineRtf(string text, int pixels, int tall)
    {
        var groups = new List<string>();

        foreach (var element in Elements(text))
        {
            if (CachedGlyph(element, pixels) is not { } face)
            {
                // All or nothing. Half a sentence in colour and half in outline, with no way to tell why, reads as
                // a rendering fault rather than as a machine that cannot do colour.
                return null;
            }

            using var standing = Stand(face, tall);

            if (RtfPicture.Group(standing) is not { } group)
            {
                return null;
            }

            groups.Add(group);
        }

        return groups.Count == 0 ? null : RtfPicture.Document(groups);
    }

    /// <summary>
    /// The same face, in a picture <c>max(its own height, <paramref name="tall"/>)</c> tall, with the last row of its
    /// ink two pixels above the bottom of <paramref name="tall"/> - which is where the letters of the line it stands
    /// among stop.
    /// </summary>
    /// <remarks>
    /// A face <i>bigger</i> than the letters around it gets the box it asked for rather than the line's: such a face is
    /// the tallest thing on its line, the line is built around it, and shrinking a Large face to fit an ordinary line
    /// would be taking the Large back off it. It still stands on the same rule, which on its own line is where it
    /// always stood.
    /// </remarks>
    private static Bitmap Stand(Face face, int tall)
    {
        var across = face.Image.Width;

        // Never shorter than the face itself, or the face would be cut, and never shorter than one pixel because a
        // zero-high canvas is not a bitmap.
        var box = Math.Max(across, Math.Max(1, tall));

        var canvas = new Bitmap(across, box);

        using var g = Graphics.FromImage(canvas);

        // Where to put the artwork so that the last row of its ink lands two pixels above the bottom of the line's
        // height. Not two above the bottom of the artwork, which is air: a face comes out of the font with air under
        // it, and air stands on nothing.
        var stand = tall - 2 - (face.Ink.Bottom - 1);

        // A face taller than the line it is on cannot stand on the line and keep all of itself below the top of it,
        // so it stands as near as it can without any ink being cut: the artwork's own air is what gets clipped, and
        // a face's air is nowhere anybody is looking.
        g.DrawImageUnscaled(face.Image, 0, Math.Max(-face.Ink.Top, stand));

        return canvas;
    }

    /// <summary>
    /// Faces on a line of their own size: the box they stand in is the face, which is what every message written in
    /// one size used to be before standing on a line of other people's letters was a thing that had to be asked about.
    /// </summary>
    public static string? InlineRtf(string text, int pixels) => InlineRtf(text, pixels, pixels);

    /// <summary>
    /// A face as an image a control can hold - a button's icon, a tile in the picker - or null when this machine
    /// cannot draw one. Detached from its stream on purpose: an <see cref="Image"/> made straight from a
    /// <see cref="MemoryStream"/> is only on loan from it, and the stream has to outlive the control that shows it.
    /// </summary>
    public static Bitmap? Glyph(string element, int pixels)
    {
        if (Png(element, pixels) is not { } png)
        {
            return null;
        }

        using var stream = new MemoryStream(png);
        using var loaded = Image.FromStream(stream);

        return new Bitmap(loaded);
    }

    /// <summary>
    /// A cached face, and the part of its square that is actually painted.
    /// <para>
    /// The second half is the reason this is a pair rather than a bitmap. A face drawn into an N by N cell does not
    /// fill it - there is air round the artwork, three pixels of it at nearly every size from 9 to 25 measured, and
    /// not quite the same number at each -
    /// so anybody putting the picture somewhere exact has to know where the ink is or it lands a couple of pixels off
    /// the mark. <see cref="FaceOverlay"/> is the anybody who cares most: it is covering a glyph the control drew
    /// itself, and being two pixels wrong there is the complaint this whole feature was opened with.
    /// </para>
    /// </summary>
    public sealed record Face(Bitmap Image, Rectangle Ink);

    /// <summary>
    /// A face a control can draw again and again: the same bitmap for the same face at the same size, because the
    /// message box asks for its handful of faces on every repaint and decoding one each time is work nobody can see.
    /// </summary>
    /// <remarks>
    /// Like <see cref="Png"/>, these are never disposed: what can be held is the faces a person can pick times the few
    /// sizes a line comes in, which is a couple of hundred small bitmaps for the life of the window. Where a face is
    /// asked for once - a button's icon - <see cref="Glyph"/> still hands out a fresh one for the control to own.
    /// </remarks>
    public static Face? CachedGlyph(string element, int pixels)
    {
        if (pixels < 1)
        {
            return null;
        }

        lock (gate)
        {
            var key = $"{pixels}|{element}";

            if (!faces.TryGetValue(key, out var face))
            {
                // Where the ink is, measured once per face per size. It is a scan of every pixel in a small bitmap,
                // which is worth doing exactly once and caching alongside the picture it describes rather than on
                // every repaint of a box that may hold a dozen faces.
                face = Glyph(element, pixels) is not { } image
                    ? null
                    : new Face(image, InkOf(image));

                faces[key] = face;
            }

            return face;
        }
    }

    /// <summary>
    /// The bounding box of everything in a bitmap that is not transparent, which is what <see cref="Render"/> fits
    /// to the cell and therefore what the cell's edges do not tell. Anything with even a touch of alpha counts, so
    /// this is the artwork plus its antialiasing - a pixel wider, on each side, than the ink a person would point at.
    /// </summary>
    private static Rectangle InkOf(Bitmap image)
    {
        var left = image.Width;
        var top = image.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).A > 0)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    /// <summary>A PNG of one emoji, cached: a room full of the same face is one raster, not two hundred.</summary>
    public static byte[]? Png(string element, int pixels)
    {
        lock (gate)
        {
            var key = $"{pixels}|{element}";

            if (!cache.TryGetValue(key, out var png))
            {
                cache[key] = png = Render(element, pixels);
            }

            return png;
        }
    }

    private static IEnumerable<string> Elements(string text)
    {
        for (var it = StringInfo.GetTextElementEnumerator(text); it.MoveNext();)
        {
            yield return (string)it.Current;
        }
    }

    private static SKTypeface? Load()
    {
        try
        {
            return File.Exists(FontPath) ? SKTypeface.FromFile(FontPath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            // OutOfMemoryException is what SkiaSharp throws for a font it cannot parse.
            return null;
        }
    }

    /// <summary>
    /// The face, scaled and centred so that the whole of it lands inside a <c>pixels</c> by <c>pixels</c> cell.
    /// <para>
    /// An emoji's ink box is not its em box. Drawing at an em of N into a cell of N used to put the ink of a face
    /// against the last row and column at every size measured (16, 18, 20, 22, 24, 28) and a flag outside both sides,
    /// which is how "the emoji is cut in half in the toolbar" happened - in the toolbar, in the picker and in the
    /// transcript alike, since all three ask for this method. There is no baseline offset that fixes it, because the
    /// ink is taller than the em it was drawn at: the only thing that fits is measuring the ink and drawing to fit.
    /// </para>
    /// <para>
    /// The cell stays exactly <c>pixels</c> square, because the line a face sits in is sized by that number, and a
    /// picture that came out a different size than it was asked for moves the line under the sentence.
    /// </para>
    /// </remarks>
    private static byte[]? Render(string element, int pixels)
    {
        if (typeface is null)
        {
            return null;
        }

        try
        {
            if (MeasureInk(element, pixels) is not { } measured)
            {
                // No ink means no glyph for this element on this machine, which the caller already handles by
                // writing the characters instead.
                return null;
            }

            // One pixel of overhang on every side. The measurement counts a pixel as inked when it is only partly
            // covered, and the shape carries on into the pixel beyond it - which is the difference between a face
            // with a pixel of air around it and a face with its antialiasing sawn off along one edge.
            var ink = new SKRectI(measured.Left - 1, measured.Top - 1, measured.Right + 1, measured.Bottom + 1);

            using var bitmap = new SKBitmap(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            using var font = new SKFont(typeface, pixels);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

            // A pixel of air at the edge of the cell. Ink that stops exactly on the last row reads as a line cut
            // across the face, and it is the antialiasing that reaches the row nobody measured.
            var margin = pixels >= 8 ? 1f : 0f;
            var room = pixels - (2 * margin);

            // Never bigger than drawn: a narrow glyph is not a mistake in the font, and inflating it to fill the cell
            // would make a flag fatter than the same flag in the message beside it.
            var scale = Math.Min(1f, Math.Min(room / ink.Width, room / ink.Height));
            var (anchorX, anchorY) = ProbeAnchor(pixels);

            canvas.Translate(margin + ((room - (ink.Width * scale)) / 2f) - (ink.Left * scale),
                margin + ((room - (ink.Height * scale)) / 2f) - (ink.Top * scale));
            canvas.Scale(scale);
            canvas.DrawText(element, anchorX, anchorY, font, paint);
            canvas.Flush();

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            return data is null ? null : data.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>How much bigger the measuring canvas is than the em drawn on it - enough that nothing can be cut by it.</summary>
    private const int ProbeScale = 4;

    /// <summary>Where the baseline of the measuring canvas starts its text: the middle, with room to hang below.</summary>
    private static (float X, float Y) ProbeAnchor(int em)
    {
        var side = em * ProbeScale;

        return (side / 2f, side / 2f);
    }

    /// <summary>
    /// The rectangle this element's ink actually covers when drawn at <paramref name="em"/> on a canvas too big to
    /// clip it, in that canvas's pixels, or null when the font has nothing to draw.
    /// </summary>
    /// <remarks>
    /// Measured by looking, not by asking the font: these are COLR/CPAL glyphs, whose reported metrics describe the
    /// typeface as a whole rather than the coloured picture of one emoji, which is exactly the difference that caused
    /// the clipping. One extra raster and one scan per face per size, then the cache in <see cref="Png"/> means it
    /// happens once.
    /// </remarks>
    private static SKRectI? MeasureInk(string element, int em)
    {
        var side = em * ProbeScale;
        var (x, y) = ProbeAnchor(em);

        using var bitmap = new SKBitmap(side, side, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var font = new SKFont(typeface, em);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        canvas.DrawText(element, x, y, font, paint);
        canvas.Flush();

        var left = side;
        var top = side;
        var right = -1;
        var bottom = -1;

        for (var row = 0; row < side; row++)
        {
            for (var column = 0; column < side; column++)
            {
                if (bitmap.GetPixel(column, row).Alpha > 0)
                {
                    left = Math.Min(left, column);
                    top = Math.Min(top, row);
                    right = Math.Max(right, column);
                    bottom = Math.Max(bottom, row);
                }
            }
        }

        return right < 0 ? null : new SKRectI(left, top, right + 1, bottom + 1);
    }
}
