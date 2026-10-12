using System.Globalization;
using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// Reading and writing formatting in the message box (v1.7).
/// <para>
/// A <see cref="RichTextBox"/> stores formatting itself, in its own way, and the only way to talk to it is to
/// highlight something and say what that something should look like. This class is that conversation, kept in one
/// place: how a character's ink is read back as a <see cref="StyledRun"/>, how a run is written onto characters,
/// and how the whole box is read out as the list of runs that goes on the wire.
/// </para>
/// <para>
/// <b>Everything is done one character at a time</b>, which looks fussy until you have met the trap it avoids.
/// Setting <c>SelectionFont</c> replaces the whole style mask rather than the one flag you were thinking about, so
/// "make it italic" written over "make it bold" comes out italic-but-not-bold - and for a selection of mixed
/// formatting the control will not even tell you what it holds, reporting plain text where half the range is bold.
/// Reading each character and writing it back with one flag changed gets it right every time. Two hundred
/// characters is two hundred pairs of messages to the control, which is around ten milliseconds, and nobody has
/// ever noticed a toolbar button taking ten milliseconds.
/// </para>
/// </summary>
public static class InputFormatting
{
    /// <summary>
    /// What the box says about wherever the caret happens to be: for a caret, the formatting the next character
    /// will get; for a highlight, the formatting of the run it starts in.
    /// <para>
    /// This one reads without moving anything, which matters more than it sounds: a selection change is what tells
    /// the toolbar to ask this question, so an implementation that moved the caret to answer it would be starting
    /// the thing it was asked about, over and over.
    /// </para>
    /// </summary>
    public static StyledRun FlagsHere(RichTextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);

        return Read(box);
    }

    /// <summary>
    /// Writes one run's worth of ink on the highlighted characters, one character at a time.
    /// <para>
    /// <paramref name="change"/> is handed what each character already is and says what it should become, which is
    /// what lets a toggle flip bold without disturbing the italic next to it. With nothing highlighted there is
    /// nothing to write on, and the caret's pending formatting is <see cref="SetPending"/> instead.
    /// </para>
    /// </summary>
    public static void ApplyToSelection(RichTextBox box, Func<StyledRun, StyledRun> change)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(change);

        var from = box.SelectionStart;
        var count = box.SelectionLength;

        Within(box, () =>
        {
            for (var at = from; at < from + count; at++)
            {
                box.Select(at, 1);

                Write(box, change(Read(box)));
            }
        });
    }

    /// <summary>
    /// Says what the next character typed here will look like.
    /// <para>
    /// The tools are the truth about what the person asked for, so the caret is put in their state rather than left
    /// in whatever the character behind it happened to be. Without this, clicking Bold and typing would produce
    /// plain text, and the button would be lying.
    /// </para>
    /// </summary>
    public static void SetPending(RichTextBox box, StyledRun brush)
    {
        ArgumentNullException.ThrowIfNull(box);

        Within(box, () => Write(box, brush));
    }

    /// <summary>
    /// The styling of a stretch of the box as the wire wants it: runs end to end over the whole of the stretch, in
    /// characters as <see cref="ValidationRules"/> counts them, neighbours of one style merged into one run.
    /// <para>
    /// Counted in characters and not in UTF-16 units for the same reason the length limit is: a run boundary in the
    /// middle of a family emoji would describe half a face, and the receiver would throw the entire annotation away
    /// because the lengths would not add up (T12.1).
    /// </para>
    /// </summary>
    /// <param name="from">Where the stretch starts, as an index into the box's text.</param>
    /// <param name="length">How many characters of the box's text the stretch is.</param>
    public static IReadOnlyList<StyledRun> RunsOf(RichTextBox box, int from, int length)
    {
        ArgumentNullException.ThrowIfNull(box);

        var text = box.Text;
        var to = Math.Min(from + length, text.Length);

        if (from < 0 || to <= from)
        {
            return [];
        }

        var runs = new List<StyledRun>();
        StyledRun? current = null;
        var counted = 0;

        Within(box, () =>
        {
            // One character at a time in the control's indices, counted one character at a time in the ruler's.
            for (var e = StringInfo.GetTextElementEnumerator(text); e.MoveNext();)
            {
                var at = e.ElementIndex;

                if (at < from)
                {
                    continue;
                }

                if (at >= to)
                {
                    break;
                }

                box.Select(at, 1);

                var flags = Read(box);

                if (current is not null && flags == current)
                {
                    counted++;
                }
                else
                {
                    if (current is not null)
                    {
                        runs.Add(current with { Length = counted });
                    }

                    current = flags;
                    counted = 1;
                }
            }
        });

        if (current is not null)
        {
            runs.Add(current with { Length = counted });
        }

        return runs;
    }

    /// <summary>
    /// Reads the highlighted character as a run.
    /// <para>
    /// The colour is only a claim when it is not the colour the box writes in anyway, which is what keeps an
    /// ordinary sentence off the wire - and it is compared as four bytes rather than as two <see cref="Color"/>s,
    /// because a colour the control hands back and the colour the box was told to use can describe the same black
    /// and still not be <see cref="Color.op_Equality">equal</see>: one is a known colour and one is a number.
    /// </para>
    /// </summary>
    private static StyledRun Read(RichTextBox box)
    {
        var font = box.SelectionFont ?? box.Font;
        var colour = box.SelectionColor;

        return new StyledRun(
            0,
            font.Style.HasFlag(FontStyle.Bold),
            font.Style.HasFlag(FontStyle.Italic),
            font.Style.HasFlag(FontStyle.Underline),
            SizeLevel(box, font.Size),
            colour.ToArgb() == box.ForeColor.ToArgb() ? null : ComposerStyle.Hex(colour));
    }

    /// <summary>Writes a run onto the highlighted character, taking the control's own font when nothing is claimed.</summary>
    private static void Write(RichTextBox box, StyledRun run)
    {
        var font = TranscriptStyle.RunFont(box.Font, run);

        box.SelectionFont = font ?? box.Font;

        if (font is not null)
        {
            font.Dispose();
        }

        box.SelectionColor = TranscriptStyle.RunColour(run) ?? box.ForeColor;
    }

    /// <summary>
    /// Which of the three sizes the wire carries a point size is, measured against this box's own size.
    /// <para>
    /// A step rather than a lookup because the wire carries three sizes and no point counts: what the box drew at
    /// some size has to be said back as one of the three, and the one it is nearest is the one it was.
    /// </para>
    /// <para>
    /// The steps are half of what the writing side moves by, taken from <see cref="TranscriptStyle"/> rather than
    /// restated here. Two definitions of "large" is how a message comes out smaller than it was typed, and it is
    /// the same reason the sizes themselves live over there.
    /// </para>
    /// </summary>
    private static int SizeLevel(RichTextBox box, float points)
    {
        var delta = points - box.Font.Size;

        if (delta <= TranscriptStyle.SmallDelta / 2f)
        {
            return StyledRun.Small;
        }

        return delta >= TranscriptStyle.LargeDelta / 2f ? StyledRun.Large : StyledRun.Normal;
    }

    /// <summary>
    /// Runs something that moves the caret, and puts the caret back.
    /// <para>
    /// Every read here is a selection change, and a selection change is what tells the toolbar to update itself -
    /// so without the window's own guard (see <see cref="ChatForm"/>) reading the box to find out what it looks
    /// like would cause the very thing it is trying to find out about, repeatedly.
    /// </para>
    /// </summary>
    private static T Within<T>(RichTextBox box, Func<T> read)
    {
        var start = box.SelectionStart;
        var length = box.SelectionLength;

        try
        {
            return read();
        }
        finally
        {
            box.Select(start, length);
        }
    }

    private static void Within(RichTextBox box, Action read) => Within(box, () =>
    {
        read();

        return true;
    });
}
