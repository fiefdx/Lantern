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
    private static readonly Dictionary<string, byte[]? > cache = [];
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
    /// The RTF that puts these emoji into a RichTextBox at <paramref name="pixels"/> tall, or null when this
    /// machine cannot produce a colour face - in which case the caller writes the characters instead.
    /// </summary>
    /// <remarks>
    /// One picture per text element, even when several arrive together. A cell is square because that is what an
    /// emoji is, and two faces in a row are two cells: drawing both into one cell would either squash them or put
    /// the second one outside the bitmap, where the person typing them would wait some time for it to appear.
    /// </remarks>
    public static string? InlineRtf(string text, int pixels)
    {
        StringBuilder? rtf = null;

        foreach (var element in Elements(text))
        {
            if (Png(element, pixels) is not { } png)
            {
                // All or nothing. Half a sentence in colour and half in outline, with no way to tell why, reads as
                // a rendering fault rather than as a machine that cannot do colour.
                return null;
            }

            using var stream = new MemoryStream(png);
            using var bitmap = Image.FromStream(stream);
            var inline = RtfPicture.Inline(bitmap);

            if (inline is null)
            {
                return null;
            }

            rtf ??= new StringBuilder();
            rtf.Append(inline);
        }

        return rtf?.ToString();
    }

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

    private static byte[]? Render(string element, int pixels)
    {
        if (typeface is null)
        {
            return null;
        }

        try
        {
            using var bitmap = new SKBitmap(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            using var font = new SKFont(typeface, pixels);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

            // Baseline a little above the bottom: the font's descenders would otherwise be cut off by the cell.
            canvas.DrawText(element, 0, pixels * 0.88f, font, paint);
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
}
