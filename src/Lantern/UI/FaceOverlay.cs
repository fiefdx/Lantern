using System.Drawing.Drawing2D;
using System.Globalization;

namespace Lantern.UI;

/// <summary>
/// Draws the colour faces in an editable box, over the outline glyphs the control draws there itself.
/// <para>
/// The rule the whole design rests on: <b>the text is the truth and the face is paint</b>. An emoji stays one
/// element of <c>Text</c>, counted like any other character, sent as itself; what is added is a picture put on top
/// of the place it occupies. Nothing is inserted, so nothing can be deleted as a picture, renumbered as two
/// characters, or arrive on the wire as a space - which is why embedding a real picture in the document was rejected
/// (the measured record is in <c>docs/design/formatting-across-the-link.md</c>).
/// </para>
/// <para>
/// It is called from <see cref="MessageInput"/>'s own paint, and by nothing else: a RichEdit window paints its text
/// over the whole client every time it paints at all, so anything painted on top of it has to be repainted by
/// whoever paints after it, and nothing else paints after it.
/// </para>
/// </summary>
public static class FaceOverlay
{
    /// <summary>
    /// How much bigger than the square to be covered the face is rasterised, which is the air the renderer leaves
    /// round the artwork (<see cref="ColorEmoji.Face"/>).
    /// </summary>
    private const int InkAir = 3;
    /// <summary>
    /// The square the control actually draws a given face in, ink and antialiasing included.
    /// <para>
    /// <b>The advance, not the cell's height and not an em.</b> Measured off the screen with a lone face on a line and
    /// nothing of ours painted on it: in Microsoft YaHei UI the ink the control drew occupied x +0..+12 and y +2..+14
    /// from the cell at 9pt, and x +0..+17 and y +3..+20 at 13pt - twelve by twelve on an advance of twelve, seventeen
    /// by seventeen on an advance of seventeen. The glyph is the advance, square, starting at the cell's own left edge
    /// and sitting a little way down from its top.
    /// </para>
    /// <para>
    /// An earlier version of this method made the square a third wider than the advance, on the strength of a
    /// measurement that had counted a full stop typed after the face as part of it. The face came out four to six
    /// pixels wider than the character it was hiding, and two or three pixels above it, which looks exactly like what
    /// it is: the picture sitting where the character is not.
    /// </para>
    /// <para>
    /// The advance is measured from the control rather than assumed, because it belongs to the run: a face typed at
    /// Large sits in a wider cell than one typed at Small, and each needs its own square. One earlier attempt at this
    /// used the box's own <c>Font.Height</c>, which is the same for both and puts large faces too high in the line.
    /// </para>
    /// </summary>
    public static Rectangle CellOf(RichTextBox box, int charIndex) =>
        CellOf(box, charIndex, ElementLengthAt(box.Text, charIndex));

    /// <summary>
    /// The same, for a caller that already knows how many units the element occupies - which is <see cref="Paint"/>,
    /// and worth a separate door because finding that out for itself means walking the whole sentence again for every
    /// face in it.
    /// </summary>
    private static Rectangle CellOf(RichTextBox box, int charIndex, int elementLength)
    {
        var start = box.GetPositionFromCharIndex(charIndex);
        var after = box.GetPositionFromCharIndex(charIndex + elementLength);
        var advance = Math.Max(8, after.X - start.X);

        // How far below the top of the cell the ink starts, measured at four sizes: two pixels on advances of nine
        // and twelve, three on fifteen and seventeen. Four sixths of an advance rounded down reproduces all four, and
        // being derived from the advance it follows the run rather than the box - which matters, because a face typed
        // at Large sits in a taller line than the box's own font and has to be moved down with it.
        return new Rectangle(start.X, start.Y + ((advance + 4) / 6), advance, advance);
    }

    /// <summary>
    /// Where to draw a face's bitmap so that the <em>ink</em> of it - not the square of bitmap it comes in - lands on
    /// <paramref name="glyph"/>.
    /// <para>
    /// A face rendered into a bitmap of N pixels inks about N - 3 of them: there is air round the artwork - three
    /// pixels at nearly every size measured, 9 through 25, so 12 of a 15, 15 of an 18, 18 of a 22 - and it is not
    /// quite the same at each. Drawing such a bitmap into the square the glyph occupied therefore puts the picture a
    /// couple of pixels inside it and low, which on a two-line box is visible. So the bitmap is scaled by the ratio
    /// the two ink boxes give and hung from the middle of its own ink rather than from its corner.
    /// </para>
    /// <para>
    /// The scale is very nearly one, because <see cref="Paint"/> asks for a bitmap three pixels bigger than the square
    /// it is going into: the face is rasterised at the size it is shown, and a picture that is not rescaled is a
    /// picture that stays sharp.
    /// </para>
    /// </summary>
    public static RectangleF DrawOf(Rectangle glyph, Rectangle ink, int side)
    {
        if (ink.Width < 1 || ink.Height < 1 || side < 1)
        {
            return glyph;
        }

        var scale = Math.Min(glyph.Width / (float)ink.Width, glyph.Height / (float)ink.Height);

        // Hang the drawing from the middle of its ink: whatever air the renderer left, the ink ends up centred on the
        // square it was asked to cover.
        return new RectangleF(
            glyph.X + glyph.Width / 2f - ((ink.X + ink.Width / 2f) * scale),
            glyph.Y + glyph.Height / 2f - ((ink.Y + ink.Height / 2f) * scale),
            side * scale,
            side * scale);
    }

    /// <summary>
    /// Paints every face in <paramref name="box"/>, and says how many it painted.
    /// </summary>
    /// <remarks>
    /// For each face: put the colour that was behind the glyph back over the glyph, then draw the artwork where the
    /// glyph was. In that order, always - artwork first is artwork underneath the outline it is meant to replace.
    /// <para>
    /// The face is found by <see cref="System.Globalization.StringInfo"/> over the text, so this agrees with the
    /// counter, the truncation and the wire about what one character is, and a surrogate pair is one square and not
    /// two painted on top of each other.
    /// </para>
    /// </remarks>
    public static int Paint(RichTextBox box, Graphics graphics)
    {
        var text = box.Text;

        if (text.Length == 0)
        {
            return 0;
        }

        var previousInterpolation = graphics.InterpolationMode;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var painted = 0;

        foreach (var (start, element) in Elements(text))
        {
            if (!ColorEmoji.IsEmoji(element))
            {
                continue;
            }

            // Asked for three pixels wider than the square it has to cover, because the artwork does not fill its own
            // cell - see DrawOf, which puts the ink back where the glyph's ink was.
            var glyph = CellOf(box, start, element.Length);
            var face = ColorEmoji.CachedGlyph(element, glyph.Width + InkAir);

            // A face the machine has no artwork for keeps its outline, and that is the whole of the fallback: no
            // patch over it, so the control's own drawing stays exactly as it is.
            if (face is null)
            {
                continue;
            }

            // What was behind the glyph. A selected face is on the selection colour, and painting the box's
            // background over it leaves a block of the wrong colour with a face in the middle of it.
            //
            // The colour is measured, not guessed: the highlight a RichEdit window draws is
            // SystemColors.Highlight exactly (FF0078D7 here, sampled off the screen), and when the box is not
            // focused it draws no selection at all, so the background is the whole story again.
            var behind = WasBehind(box, start, element.Length);

            if (behind is not null)
            {
                // The glyph's own footprint, and not the square of it: a face is round, so an ellipse inscribed in
                // the square the glyph was drawn in - a pixel larger, for the antialiasing that feathers out past the
                // ink - is the shape that has to be erased. A rectangle would cover the corners, which on a square
                // this tight are the tops and bottoms of the letters either side, and they are not ours to rub out.
                using var brush = new SolidBrush(behind.Value);
                graphics.FillEllipse(brush, Rectangle.Inflate(glyph, 1, 1));
            }

            graphics.DrawImage(face.Image, DrawOf(glyph, face.Ink, face.Image.Width));
            painted++;
        }

        graphics.InterpolationMode = previousInterpolation;

        return painted;
    }

    private static Color? WasBehind(RichTextBox box, int start, int length)
    {
        var selected = box.SelectionLength > 0
            && box.SelectionStart <= start
            && start + length <= box.SelectionStart + box.SelectionLength;

        if (selected && box.Focused)
        {
            return SystemColors.Highlight;
        }

        return box.BackColor;
    }

    /// <summary>
    /// The text elements of a string with where each one sits, which is how a face is found without ever cutting a
    /// surrogate pair in half.
    /// </summary>
    private static IEnumerable<(int Start, string Element)> Elements(string text)
    {
        var iterator = StringInfo.GetTextElementEnumerator(text);

        while (iterator.MoveNext())
        {
            yield return (iterator.ElementIndex, iterator.GetTextElement());
        }
    }

    /// <summary>How many UTF-16 units the element at this index occupies - two for a face, more for a joined family.</summary>
    private static int ElementLengthAt(string text, int index)
    {
        foreach (var (start, element) in Elements(text))
        {
            if (start == index)
            {
                return element.Length;
            }
        }

        return 1;
    }
}
