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
    /// The square the control actually draws a given face in.
    /// <para>
    /// <b>It is not the cell.</b> <c>GetPositionFromCharIndex</c> reports where the character's <em>advance</em>
    /// begins, and the emoji the font supplies is drawn wider than that: measured off the screen for a lone face in
    /// Microsoft YaHei UI, its dark ink spanned x 2..16 and y 1..14 in a cell from x 1 twelve wide at 9pt, and x
    /// 2..22 and y 2..22 in a cell from x 1 seventeen wide at 13pt. The ink is fifteen and twenty-one across, the
    /// advances twelve and seventeen - the glyph is drawn at an <em>em</em>, and the em of these fonts is a third
    /// wider than the advance again. Paint the cell and the right shoulder of the black outline is left showing,
    /// with the colour face sitting to the left of where the glyph actually was, which is exactly the mess this class
    /// exists to remove.
    /// </para>
    /// <para>
    /// The advance is measured from the control rather than assumed, because it belongs to the run: a face typed at
    /// Large sits in a wider cell than one typed at Small, and both need their own square.
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

        // The advance plus a third: 12 -> 16 and 17 -> 22 where the ink measured 15 and 21 across, so the square is
        // the glyph with a pixel to spare rather than the glyph exactly. Being a pixel wide of the mark is the right
        // way to be wrong here; being a pixel short leaves black outline on screen, which is the thing that started
        // this task.
        var advance = Math.Max(8, after.X - start.X);
        var em = advance + advance / 3;

        // A pixel in from the cell's left at both sizes measured. Not centred on the cell: the glyph is not centred
        // on it either, it runs out past the advance on the right and stops just inside the left.
        return new Rectangle(start.X + 1, start.Y + 1, em, em);
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

            var glyph = CellOf(box, start, element.Length);
            var face = ColorEmoji.CachedGlyph(element, glyph.Width);

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
                // the square it is drawn in is the shape that has to be erased. A rectangle would do what the
                // measurement above makes unavoidable - the glyph overflows its advance, so the erase does too - and
                // then wipe the first strokes of whatever letter follows. Anything this covers, the control's own
                // outline glyph was already drawing over.
                using var brush = new SolidBrush(behind.Value);
                graphics.FillEllipse(brush, glyph);
            }

            graphics.DrawImage(face, glyph);
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
