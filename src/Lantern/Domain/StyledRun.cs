using System.Globalization;

namespace Lantern.Domain;

/// <summary>
/// One run of formatting laid over part of a message (v1.7).
/// <para>
/// A run is a length and what that stretch of the sentence looks like. Runs are laid end to end and are
/// counted in <em>characters somebody counts</em> — the ruler <see cref="ValidationRules.Length"/> uses —
/// rather than in offsets into UTF-16, because a boundary drawn in the middle of <c>👨‍👩‍👧</c> would put half a
/// family in bold and the other half not. Run lengths and no offsets also means a receiver cannot be given
/// overlapping spans, gaps, or a list whose order matters.
/// </para>
/// <para>
/// Runs never restate the sentence. <see cref="TextMessage.Body"/> says what the message says, and a
/// receiver that cannot make sense of this list — an older build, a list that does not add up, a size
/// nobody has heard of — throws the whole list away and reads the words. That is the one failure mode
/// <see cref="Fits"/> exists to detect, and <see cref="Accepted"/> to carry out.
/// </para>
/// </summary>
/// <param name="Length">How many characters this run covers. Never zero, never negative.</param>
/// <param name="Bold">Set when the stretch is bold. Independent of the other two flags, not a font style.</param>
/// <param name="Italic">Set when the stretch is italic.</param>
/// <param name="Underline">Set when the stretch is underlined.</param>
/// <param name="Size">One of <see cref="Small"/>, <see cref="Normal"/> or <see cref="Large"/>.</param>
/// <param name="Color"><c>#RRGGBB</c>, or null for "the colour this window already uses".</param>
public sealed record StyledRun(
    int Length,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    int Size = StyledRun.Normal,
    string? Color = null)
{
    /// <summary>The smallest of the three sizes a message may ask for.</summary>
    public const int Small = 0;

    /// <summary>
    /// The size the window would have used anyway. A run at this size says nothing about size, which is
    /// why it is the default and why it never reaches the wire.
    /// </summary>
    public const int Normal = 1;

    /// <summary>The largest of the three sizes a message may ask for.</summary>
    public const int Large = 2;

    /// <summary>
    /// How many runs one message may claim. Past this somebody is either confused or trying to make a GDI
    /// control spend its evening laying out runs — and a list this long is already longer than the
    /// message it annotates could usefully have.
    /// </summary>
    public const int MaxRuns = 64;

    /// <summary>
    /// The whole of a sentence of <paramref name="length"/> characters with nothing said about it. What a
    /// sender uses when it is formatting one part of a line and not another.
    /// </summary>
    public static IReadOnlyList<StyledRun> Plain(int length) => [new StyledRun(length)];

    /// <summary>
    /// Whether a run list describes the sentence it was sent with: the lengths add up to exactly the whole
    /// of it, every run covers something, there are at most <see cref="MaxRuns"/> of them, and every size
    /// and colour is one a window could actually draw.
    /// </summary>
    /// <remarks>
    /// A list that disagrees with its own text is a bug in the sender, and the recipient has no way to
    /// guess which half to believe — so it believes the sentence. This is the only thing that can go wrong
    /// with formatting on the wire, and it is decided here rather than in <see cref="TextMessage"/> so
    /// that a message with an impossible style list still arrives as a message.
    /// <para>
    /// Sizes and colours are checked here and nowhere else, because they arrive from another machine: a
    /// peer could claim size nine or the colour "green", and handing that to a font or a colour parser is
    /// not a thing to find out on somebody's desktop.
    /// </para>
    /// </remarks>
    public static bool Fits(IReadOnlyList<StyledRun>? runs, string? body)
    {
        if (runs is null or { Count: 0 } || runs.Count > MaxRuns)
        {
            return false;
        }

        var wanted = ValidationRules.Length(body);
        if (wanted == 0)
        {
            return false;
        }

        var covered = 0;

        foreach (var run in runs)
        {
            if (run.Length <= 0 || covered > wanted - run.Length)
            {
                // Also the overflow guard: covered + run.Length is never computed, so no sum of a
                // handful of int.MaxValue runs can wrap its way back up to the length of a sentence.
                return false;
            }

            if (run.Size is not (Small or Normal or Large) || !IsColour(run.Color))
            {
                return false;
            }

            covered += run.Length;
        }

        return covered == wanted;
    }

    /// <summary>
    /// The list a receiver should apply: the runs as sent when they describe the sentence, and none at all
    /// when they do not. Never part of a list — half an annotation applied to a whole sentence is how one
    /// bold word ends up on three.
    /// </summary>
    public static IReadOnlyList<StyledRun> Accepted(IReadOnlyList<StyledRun>? runs, string? body)
        => Fits(runs, body) ? runs! : [];

    /// <summary>null means "no colour claimed"; otherwise exactly a hash and six hexadecimal digits.</summary>
    private static bool IsColour(string? color)
    {
        if (color is null)
        {
            return true;
        }

        if (color.Length != 7 || color[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < color.Length; i++)
        {
            if (!Uri.IsHexDigit(color[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Cuts the sentence into the stretches the styling talks about, in the order they occur.
    /// <para>
    /// Runs are counts of <em>characters as anybody counting them would count them</em>, so this walks
    /// <see cref="System.Globalization.StringInfo"/> elements and never a UTF-16 unit. A cut that landed inside a
    /// family emoji would hand the drawing code half a surrogate pair, which is not a character: half a face in one
    /// style and the rest of it in another.
    /// </para>
    /// <remarks>
    /// Nothing here assumes the runs are well formed, because a message can be handed over in process as easily as
    /// parsed off a socket, and a malformed annotation must still leave the sentence intact: runs describing more
    /// characters than exist stop at the end, runs describing none are skipped, and whatever is left over is plain.
    /// Cutting a sentence up must never be able to lose part of it.
    /// </remarks>
    /// </summary>
    public static IReadOnlyList<StyledPiece> Split(string body, IReadOnlyList<StyledRun>? runs)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.Length == 0)
        {
            return [];
        }

        // Where each character begins, so that a count of characters can be cut at a character boundary.
        var starts = new List<int>();

        for (var e = StringInfo.GetTextElementEnumerator(body); e.MoveNext();)
        {
            starts.Add(e.ElementIndex);
        }

        // One past the last character, so that a run reaching the end of the sentence has a boundary to be cut at
        // just like any other.
        starts.Add(body.Length);

        var pieces = new List<StyledPiece>();
        var total = starts.Count - 1;
        var element = 0;

        foreach (var run in runs ?? [])
        {
            if (element >= total)
            {
                break;
            }

            var take = Math.Min(Math.Max(run.Length, 0), total - element);

            if (take == 0)
            {
                // A run describing no characters would make an empty piece, which the drawing code would
                // dutifully apply and re-apply for nothing.
                continue;
            }

            pieces.Add(new StyledPiece(body[starts[element]..starts[element + take]], run));
            element += take;
        }

        if (element < total)
        {
            // Whatever the list did not talk about is plain - and its length is the truth about that stretch, so a
            // caller that re-reads the pieces gets a list that adds up even when the one it was given did not.
            pieces.Add(new StyledPiece(body[starts[element]..], new StyledRun(total - element)));
        }

        return pieces;
    }
}

/// <summary>One stretch of the sentence, and what the wire said about it.</summary>
public readonly record struct StyledPiece(string Text, StyledRun Run);
