namespace Lantern.UI;

/// <summary>
/// The box a message is written in (v1.7).
/// <para>
/// A <see cref="RichTextBox"/> rather than a <see cref="TextBox"/> because the message box is an editor now: it
/// holds bold and colour, and it is two lines tall so a person can see the sentence they are composing. Everything
/// about <em>what a message is</em> - the length rule, what gets sent, what the styling means - stays in the window,
/// because that is what the window knows and the box does not.
/// </para>
/// <para>
/// What is here is what only the control can do, and all three of those are workarounds for something a RichEdit
/// window cannot be asked to do politely: take a paste as plain words rather than as rich text, show the colour
/// faces the font has but GDI will not render - which takes two things, uninking the outline glyph the control
/// insists on drawing (<see cref="RenewInk"/>) and painting Windows' own face over the gap left behind
/// (<see cref="FaceOverlay"/>) - and saying what an empty box is for, since a <see cref="RichTextBox"/> has no
/// placeholder of any kind and the only place anything can be painted on it is the paint message, because
/// <c>OnPaint</c> is never called (see <see cref="PaintOverlay"/>).
/// </para>
/// </summary>
public class MessageInput : RichTextBox
{
    private const int WmPaint = 0x000F;
    private const int WmPaste = 0x007D;
    private const int WmHScroll = 0x0114;
    private const int WmVScroll = 0x0115;
    private const int WmMouseWheel = 0x020A;

    /// <summary>
    /// RichEdit: draw the selection, or do not, without changing what is selected. <c>WM_USER + 63</c>.
    /// <para>
    /// The first window message this project has ever sent, and it is here for one reason: a RichTextBox will not
    /// say what a stretch of its own text is wearing unless that stretch is highlighted, and highlighting the middle
    /// of somebody's sentence makes that window draw itself again. See <see cref="RenewInk"/>.
    /// </para>
    /// </summary>
    private const int WmHideSelection = 0x043F;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr want, IntPtr detail);

    /// <summary>
    /// The word an empty box shows, so that an empty editor does not read as a broken one.
    /// <para>
    /// The word is drawn rather than typed: the box's emptiness is what the Send button reads, and a message that
    /// said "Message" because nobody overwrote the hint would be a very strange thing to send to a room.
    /// </para>
    /// </summary>
    public const string Hint = "Message";

    /// <summary>
    /// How tall the tallest ink on each of the box's lines is, in pixels, as long as nobody has changed the text
    /// or the ink since it was last worked out.
    /// </summary>
    private readonly Dictionary<int, int> tallest = [];

    /// <summary>
    /// Whether the box is reading its own ink right now - which it does by moving the selection, and nobody should
    /// take that for a person moving the caret.
    /// </summary>
    private bool measuring;

    /// <summary>
    /// The piece of the box whose ink may need putting right, because it arrived since it was last looked after or
    /// somebody wrote formatting over it.
    /// <para>
    /// Worth carrying: a face costs about a millisecond to look after, and doing it to every face in the box on
    /// every keystroke measured <b>8 ms a key</b> for a sentence with six of them in it, which is a box that lags
    /// while it is typed in. Only new characters and newly formatted ones can need it, so the box diffs its own
    /// text - a walk over a string, costing nothing next to a message to the control - and asks about the piece that
    /// changed. <c>dirtyAll</c> is how a box that has not yet looked at any of itself, or has been asked before it
    /// had a window to ask about, says so.
    /// </para>
    /// </summary>
    private bool dirtyAll = true;

    private int dirtyFrom;

    private int dirtyTo;

    /// <summary>The text as it was the last time its ink was accounted for, which is what the diff is against.</summary>
    private string seen = string.Empty;

    /// <summary>
    /// Whether the box is in the middle of reading what its own lines are wearing.
    /// <para>
    /// A <see cref="RichTextBox"/> has one font of its own and stores the rest itself, and the only way to ask what
    /// a stretch of it looks like is to highlight that stretch. So working out how tall the ink on a line is means
    /// selecting it, and a selection change is what tells the window's toolbar to ask what the caret is sitting in -
    /// which would put the Bold button in the state of whatever character the reading happened to be on. The window
    /// asks this before believing a selection change.
    /// </para>
    /// </summary>
    public bool Measuring => measuring;

    /// <summary>
    /// How tall the tallest ink on the line a character sits on is, in pixels of em, or null when the box has not
    /// worked that out.
    /// <para>
    /// This is what tells <see cref="FaceOverlay"/> where the line's baseline is: the face has to be painted on the
    /// baseline the line is using, not on the one its own size would give it, or a small face beside large letters
    /// floats above them with its outline showing underneath.
    /// </para>
    /// <para>
    /// <b>Read out of what <see cref="RenewInk"/> found, and never measured here.</b> This is called from the
    /// paint, and measuring is not something that can be done from a paint - see there.
    /// </para>
    /// </summary>
    public int? TallestEmOnLine(int charIndex)
    {
        if (charIndex < 0 || charIndex > TextLength)
        {
            return null;
        }

        var line = GetLineFromCharIndex(charIndex);

        if (line < 0 || !tallest.TryGetValue(line, out var em))
        {
            return null;
        }

        return em;
    }

    /// <summary>
    /// Looks at the box's own ink now: takes the ink off the outline glyphs under its faces, and remembers how tall
    /// the tallest thing on each line is.
    /// <para>
    /// <b>Now, and not when the window next paints - which is the whole point of this method existing.</b> Asked
    /// what a stretch of text is wearing, a RichTextBox answers only about what is highlighted, so measuring means
    /// highlighting, and highlighting makes the window draw itself again. This window paints a face over its own
    /// outline glyph on every paint message it is given, so measuring from the paint measured from the paint: the
    /// reading selected, the selection painted, the paint read, the reading selected. Counted on a line of twelve
    /// characters: <b>346 selections and 31 paints</b> for one answer, all of them inside the one question, because
    /// the answer had not been reached by the time the first of those paints arrived and each paint asked again.
    /// A box doing that on every keystroke is a box that "refreshes many times in a flash", which is the report
    /// this answers to.
    /// </para>
    /// <para>
    /// The selection is hidden for the duration as well, which is worth one message either way: with the
    /// highlighting invisible, the window has nothing to redraw, and the paints a reading used to cause are not
    /// merely fewer but none - measured, zero, against two per highlight when it is shown.
    /// </para>
    /// <para>
    /// <b>Taking the ink off the outline glyphs happens here too, in the same window.</b> It is the same kind of
    /// work - the box putting its own faces right by talking to the control about them, which means selecting, which
    /// means the selection has to be hidden and put back - and doing it in a second window would cost a second
    /// repaint of the box on every keystroke to change nothing. See <see cref="BlankOutlineGlyphs"/> for what is
    /// written and why that is the way it is written.
    /// </para>
    /// </summary>
    /// <summary>Passes no stretch, which is how the callers that know nothing about formatting ask for a box to
    /// look after its own ink.</summary>
    public const int Nowhere = -1;

    public void RenewInk() => RenewInk(Nowhere, Nowhere);

    /// <summary>
    /// As <see cref="RenewInk()"/>, and additionally puts the blank back on any face in the stretch whose
    /// formatting was just written over - which is what keeps <see cref="InputFormatting"/> from putting the
    /// control's outline face back under a face it passed on the way to making a word bold.
    /// </summary>
    public void RenewInk(int writtenFrom, int writtenTo)
    {
        if (writtenFrom >= 0)
        {
            // A stretch to look at. When the box has not been through yet at all, dirtyAll says so, and one stretch
            // is no reason to stop believing it.
            dirtyFrom = Math.Min(dirtyFrom, Math.Max(0, writtenFrom));
            dirtyTo = Math.Max(dirtyTo, writtenTo);
        }

        if (measuring || !IsHandleCreated)
        {
            return;
        }

        tallest.Clear();

        var at = SelectionStart;
        var length = SelectionLength;

        measuring = true;
        SendMessage(Handle, WmHideSelection, 1, 0);

        try
        {
            var from = dirtyAll ? 0 : dirtyFrom;
            var to = dirtyAll ? TextLength : dirtyTo;

            BlankOutlineGlyphs(from, to);

            dirtyAll = false;
            dirtyFrom = int.MaxValue;
            dirtyTo = 0;

            // Every display line of what is there, which is not every line a person may have typed: a wrapped
            // paragraph is several display lines, and each has its own tallest ink.
            for (var line = 0; line <= GetLineFromCharIndex(TextLength); line++)
            {
                tallest[line] = TallestEm(line);
            }
        }
        finally
        {
            // The caret goes back while the selection is still hidden, and the highlighting is put back on last of
            // all: it ends up exactly where it started, so the one repaint this costs is the box putting its own
            // selection back and nothing else. Measured: that is the whole of it - one paint per reading, where
            // showing the highlighting while reading cost two apiece and doing it from the paint cost thirty-two.
            Select(at, length);

            SendMessage(Handle, WmHideSelection, 0, 0);

            measuring = false;
        }
    }

    /// <summary>
    /// Makes every face in the box a character with nothing drawn on it, so that the only face the box shows is the
    /// one <see cref="FaceOverlay"/> paints.
    /// <para>
    /// "should only render the colorful icon, not the black white icon ... only put a empty placeholder then render
    /// the colorful icon over the empty placeholder." What the box did was let RichEdit draw the monochrome outline -
    /// measured off the screen, a black face 12 pixels square for a 9 point face, drawn by the control itself in the
    /// text colour, with eyes and a mouth in it - and then paint a colour face over it. Two drawings of the same
    /// face, in two sizes and two shapes, lined up by arithmetic about an advance. It looked right and was one pixel
    /// from looking wrong, and the outline is square while the patch put over it is round, so what survived at the
    /// corners was precisely the black somebody was asking to have taken away.
    /// </para>
    /// <para>
    /// <b>The character is not touched.</b> It stays one text element, counted by <see cref="Domain.ValidationRules"/>,
    /// sent as itself, deleteable with one backspace, and it is still where the caret goes. Only its ink goes away.
    /// </para>
    /// <para>
    /// <b>Why the text colour, when the control has a "hide character" effect.</b> It does, since RichEdit 3.0
    /// (<c>CFE_HIDDEN</c>, <c>0x100</c>), and it was tried, and what it hides is the character's <em>place</em> as
    /// well as its ink: measured, a line reading <c>a</c> <c>face</c> <c>b</c> had its characters at x 1, 8 and 28,
    /// and after hiding the face every one of them was at x 1. A hidden character takes no room, which is the right
    /// behaviour for hidden text and the wrong one for a face that is very much there - the advance is what the face
    /// is painted into, what the caret walks over, and what the box measures a line's ink with. Colour is the other
    /// way: positions measured before and after are identical, the format sticks to the character through edits and
    /// word wrap, and the caret sitting after a blank face still reads back black, so what gets typed next is
    /// visible.
    /// </para>
    /// <para>
    /// The colour is the box's own background, which is not a claim about the text and is therefore not read back as
    /// one - see <see cref="InputFormatting"/>, whose reader drops the colour of any character that is a face. The
    /// patch <see cref="FaceOverlay"/> still puts down first is kept as well: it is what covers a face that arrived
    /// with ink of its own, from a paste or an undo, in the moment before this method has been round it again.
    /// </para>
    /// <para>
    /// <b>And the blank is put back on the faces only.</b> A character's formatting is what the next character typed
    /// after it inherits, which is how a box knows to carry on bold; and it means that the character typed after a
    /// face would inherit the blank and arrive invisible - measured, <c>WhatIsTypedAfterAFaceIsInkedNormally</c> was
    /// written first and failed first, which is the only reason it is not in the next release. So every stretch of
    /// words that begins just after a face is asked what it is wearing, and given back the box's own ink if it turns
    /// out to have picked up the blank instead. That is one read per face rather than one per character, which is
    /// what makes it affordable on a keystroke; and since the repair writes the ordinary colour onto the first
    /// character of the stretch, whatever is typed after it inherits that and the sentence heals itself from there.
    /// Nothing else in the box is offered the background colour as ink, and the colour tools do not offer it either -
    /// their six inks are red, orange, green, blue, purple and pink (see <see cref="ComposerStyle"/>).
    /// </para>
    /// </summary>
    private void BlankOutlineGlyphs(int from, int to)
    {
        var blank = BackColor;
        var wasFace = false;

        // Every element is walked, because knowing whether the character at a position follows a face is knowing what
        // it is - which costs a string, not a message to the control. Only the ones inside the piece that changed
        // are ever touched.
        foreach (var (at, element) in Elements(0, TextLength))
        {
            var face = ColorEmoji.IsEmoji(element);

            // Words nowhere near a face are none of this method's business, and not looking at them at all is what
            // keeps it affordable: reading a colour costs a message to the control, writing one costs a redraw of
            // the run, and this is asked on every keystroke. So the only questions are asked of faces and of the one
            // character that starts just after one.
            if (at < to && at + element.Length > from && (face || wasFace))
            {
                Select(at, element.Length);

                var wears = SelectionColor.ToArgb();

                if (face)
                {
                    // A face already wearing the blank is left exactly where it is, which is what makes a box of
                    // nothing but faces cost one message a face rather than two.
                    if (wears != blank.ToArgb())
                    {
                        SelectionColor = blank;
                    }
                }
                else if (wears == blank.ToArgb())
                {
                    // Words that have picked up a face's blank, which is words nobody can see.
                    SelectionColor = ForeColor;
                }
            }

            wasFace = face;
        }
    }

    /// <summary>
    /// Reads one line's ink.
    /// <para>
    /// <b>Faces are measured, not asked about.</b> Asking the control what a face is wearing is both the expensive
    /// question - a highlight that crosses one costs an order of magnitude more than one that does not, because the
    /// control lays the fallback font out again - and the untrustworthy one: the answer comes back naming
    /// <c>Segoe UI Symbol</c> or <c>Calibri</c> rather than the font the box was given, because those are the faces
    /// RichEdit borrows to draw it with. And the size is already knowable for nothing, since a face's ink is its
    /// advance square and the advance is where the next character starts. So the faces are stepped over, and the
    /// questions go to the words between them.
    /// </para>
    /// <para>
    /// <b>The words are asked in stretches, not one at a time.</b> A read of a highlighted stretch costs about a
    /// thirtieth of a millisecond, which sounds like nothing until the arithmetic is done: a line of forty characters
    /// walked a character at a time measured fifteen milliseconds, and this question is asked on every keystroke.
    /// A whole stretch that wears one font answers for all of itself in one read - which is nearly every stretch -
    /// and only one that says "mixed" is cut in half and asked again.
    /// </para>
    /// <para>
    /// Every cut is on a text-element boundary, for the reason everything else here is: a family is two UTF-16 units
    /// of some and one character, and half a face is nothing anybody typed.
    /// </para>
    /// </summary>
    private int TallestEm(int line)
    {
        var from = GetFirstCharIndexFromLine(line);

        if (from < 0)
        {
            return EmOf(Font.Size);
        }

        // The line ends where the next one starts, or at the end of the text. Both are display lines: a wrapped
        // paragraph is several of these, and it is the display line whose baseline the control is laying out.
        var to = GetFirstCharIndexFromLine(line + 1);
        to = to < 0 ? TextLength : to;

        var points = Font.Size;

        // Nothing the box can be told to write is larger than this, so a line that has already produced one has
        // produced the answer and the rest can go unread.
        var largest = Font.Size + TranscriptStyle.LargeDelta;

        foreach (var words in Words(from, to))
        {
            points = TallestIn(points, words.Start, words.End, largest);
        }

        foreach (var face in Faces(from, to))
        {
            points = Math.Max(points, PointsOfFace(face));
        }

        return EmOf(points);
    }

    /// <summary>
    /// How tall the ink of one face is, in points: its own advance is its em, so two position questions answer it
    /// and the control is never asked what it thinks the face's font is.
    /// </summary>
    private float PointsOfFace(int start)
    {
        var begin = GetPositionFromCharIndex(start);
        var after = GetPositionFromCharIndex(Next(start));

        // Back to points from pixels, because everything else here is counted in points and one unit in a method
        // that has both is how a size drifts.
        return Math.Max(1f, (after.X - begin.X) * 72f / DeviceDpi);
    }

    /// <summary>Where the character after this one starts, which is where its advance ends.</summary>
    private int Next(int start) => start + System.Globalization.StringInfo.GetNextTextElement(Text, start).Length;

    /// <summary>
    /// The stretches of a line that are words rather than faces, and would therefore like to be asked about ink.
    /// </summary>
    private IEnumerable<(int Start, int End)> Words(int from, int to)
    {
        var start = -1;

        foreach (var (at, element) in Elements(from, to))
        {
            if (ColorEmoji.IsEmoji(element))
            {
                if (start >= 0)
                {
                    yield return (start, at);

                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = at;
            }
        }

        if (start >= 0)
        {
            yield return (start, to);
        }
    }

    /// <summary>Where every face on a line starts.</summary>
    private IEnumerable<int> Faces(int from, int to) =>
        Elements(from, to).Where(e => ColorEmoji.IsEmoji(e.Element)).Select(e => e.Start);

    private float TallestIn(float best, int from, int to, float largest)
    {
        var edges = Edges(from, to);
        var points = best;

        // The selection is hidden, and the caret put back, by the caller: see RenewInk for why both are needed.
        Look(0, edges.Count - 1);

        return points;

        void Look(int low, int high)
        {
            if (high <= low || points >= largest)
            {
                return;
            }

            Select(edges[low], edges[high] - edges[low]);

            // A font back means the whole stretch wears that one, which is the answer for all of it. Null means they
            // differ somewhere, and there is nothing to do but ask smaller.
            if (SelectionFont is { } worn)
            {
                points = Math.Max(points, worn.Size);

                return;
            }

            if (high - low == 1)
            {
                // One character that will not say what it is wearing. Leave it at the size the line is taken to be.
                return;
            }

            var middle = (low + high) / 2;

            Look(low, middle);
            Look(middle, high);
        }
    }

    /// <summary>
    /// Where each character of a stretch begins, and where the stretch ends - the cuts a halving is allowed to make.
    /// </summary>
    private List<int> Edges(int from, int to)
    {
        var edges = new List<int> { from };

        for (var e = System.Globalization.StringInfo.GetTextElementEnumerator(Text); e.MoveNext();)
        {
            var at = e.ElementIndex;

            if (at >= to)
            {
                break;
            }

            if (at > from)
            {
                edges.Add(at);
            }
        }

        edges.Add(to);

        return edges;
    }

    /// <summary>The characters of a stretch of the box's text, each with where it starts.</summary>
    private IEnumerable<(int Start, string Element)> Elements(int from, int to)
    {
        for (var e = System.Globalization.StringInfo.GetTextElementEnumerator(Text); e.MoveNext();)
        {
            var at = e.ElementIndex;

            if (at >= to)
            {
                yield break;
            }

            if (at >= from)
            {
                yield return (at, e.GetTextElement());
            }
        }
    }

    /// <summary>
    /// A point size in device pixels, which is what the square of a face is measured in: twelve at nine points on
    /// this machine, seventeen at thirteen, and the same as the advance the control reports for a face at that size.
    /// </summary>
    private int EmOf(float points) =>
        Math.Max(1, (int)Math.Round((points * DeviceDpi / 72f), MidpointRounding.AwayFromZero));

    /// <summary>
    /// Raised instead of the control's own paste, which is <see cref="RichTextBox"/> pasting whatever the
    /// clipboard happens to be holding - rich text and all.
    /// <para>
    /// The window answers it by taking the words out of the clipboard and putting them in unadorned. Rich text
    /// from elsewhere would put formatting on the screen that nobody here asked for, formatting the wire has no way
    /// to describe, in colours the room never chose; and formatting pasted in from another chat program is a good
    /// way to find out that its idea of a styled run and this one are not the same thing. So a paste brings words,
    /// which is what was asked for.
    /// </para>
    /// </summary>
    public event EventHandler? PasteRequested;

    /// <summary>
    /// Whether the box should be showing the word that says what it is for.
    /// <para>
    /// A <see cref="RichTextBox"/> has no <c>PlaceholderText</c> - that is a <see cref="TextBox"/> idea - so the
    /// hint is drawn by this control (see <see cref="PaintOverlay"/>), and this is the part a test can ask about
    /// without a screen.
    /// </para>
    /// </summary>
    public bool ShowsHint => Text.Length == 0;

    /// <summary>
    /// How many colour faces the last paint drew, and whether the word that says what an empty box is for went on.
    /// <para>
    /// These exist because a RichEdit window cannot be read back. Asking it for a picture of itself comes back with
    /// almost none of what it drew - 31 dark pixels for a line of text, measured - so neither the faces nor the hint
    /// can be proved by photographing the box, and the count the control saw during its own paint is the one thing
    /// that can be asked. The painting itself (<see cref="FaceOverlay"/>) is a method the tests call directly, for
    /// the same reason.
    /// </para>
    /// </summary>
    public int PaintedFaces { get; private set; }

    /// <summary>Whether the last paint drew the word that says what an empty box is for.</summary>
    public bool PaintedHint { get; private set; }

    /// <summary>
    /// How many times this window has been asked to paint itself.
    /// <para>
    /// For an ordinary control that number is a curiosity. For this one every paint message erases a face and draws
    /// it again - the control paints its own outline glyph, and <see cref="PaintOverlay"/> covers it afterwards - so
    /// a hundred extra paints is not wasted work but a visible strobe, and it is what a box that "refreshes many
    /// times in a flash" is. It is counted here because nothing else can see it: a RichEdit window raises no event
    /// for its own paint messages, and <c>OnPaint</c> does not happen.
    /// </para>
    /// </summary>
    public int Paints { get; private set; }

    /// <summary>
    /// Puts the faces and the hint on the window, after the control has drawn its own text over the whole client.
    /// </summary>
    /// <remarks>
    /// <b>Called from the paint message and not from <c>OnPaint</c>, because <c>OnPaint</c> does not happen.</b>
    /// Measured on this machine: the paint message arrives at this control's window procedure, and WinForms invokes
    /// neither <c>OnPaint</c> nor the <c>Paint</c> event for it - the window's own procedure paints, and that is the
    /// end of it. An earlier version of this class drew the hint in an <c>OnPaint</c> override, which meant the word
    /// "Message" was never on the screen, and the test for it went on passing because it asked the
    /// <see cref="ShowsHint"/> property rather than the window. Whatever paints on this box has to do it here.
    /// <para>
    /// Faces before hint is not an ordering anyone has to think about: an empty box is the only box with a hint, and
    /// an empty box has no faces in it.
    /// </para>
    /// </remarks>
    private void PaintOverlay()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        using var graphics = Graphics.FromHwnd(Handle);

        PaintedFaces = FaceOverlay.Paint(this, graphics);
        PaintedHint = false;

        if (!ShowsHint)
        {
            return;
        }

        // Painted rather than typed: an empty box has to stay empty, since its emptiness is what the Send button
        // reads. Grey, at the box's own font, at the place the first character would go - close enough that the
        // sentence appears to start where the word was rather than jumping.
        TextRenderer.DrawText(
            graphics,
            Hint,
            Font,
            new Point(2, 1),
            ComposerStyle.HintColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        PaintedHint = true;
    }

    /// <summary>
    /// Asks for the whole client again whenever the text, the caret or the scroll moved.
    /// <para>
    /// A face is paint at a position, and a position is only true until something moves. The control repaints the
    /// text it owns; nothing repaints what was painted on top of it, so a scrolled box would keep its faces where
    /// they were drawn rather than where their characters went.
    /// </para>
    /// </summary>
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);

        // New words, other words' ink coming in, or words going away: either way a line may be wearing something it
        // was not before, and a face painted to the wrong baseline is a face floating in the line. Measured here
        // rather than in the paint, which is what the paint would otherwise spend its time doing - RenewInk. And a
        // face that has just arrived has an outline glyph on it, which is the other half of what RenewInk does.
        // What needs looking at is the piece that changed, which is what the diff below says. A keystroke in the
        // middle of a sentence is two characters of it, and the forty faces elsewhere in the box are left alone.
        NoteWhatChanged();
        RenewInk();
        Invalidate();
    }

    /// <summary>
    /// Adds the difference between the text and the text this box last accounted for to the piece that needs its ink
    /// put right.
    /// </summary>
    /// <remarks>
    /// Common prefix, common suffix, and everything between them, which is exactly what an insertion, a deletion,
    /// a paste or an undo did - and it is done on strings rather than by asking the control, because that is the
    /// difference between a microsecond and a millisecond per character. Anything that cannot be accounted for
    /// leaves <see cref="dirtyAll"/> set, which is the honest answer rather than a wrong one.
    /// </remarks>
    private void NoteWhatChanged()
    {
        var now = Text;

        if (!IsHandleCreated)
        {
            // Nothing has been put right yet, and there is no window to put it right in.
            seen = now;
            dirtyAll = true;

            return;
        }

        var start = 0;

        while (start < seen.Length && start < now.Length && seen[start] == now[start])
        {
            start++;
        }

        var then = seen.Length;
        var ends = now.Length;

        while (then > start && ends > start && seen[then - 1] == now[ends - 1])
        {
            then--;
            ends--;
        }

        dirtyFrom = Math.Min(dirtyFrom, start);
        dirtyTo = Math.Max(dirtyTo, ends);

        seen = now;
    }

    /// <summary>The caret moving changes where the faces go, and which of them are on a selection.</summary>
    protected override void OnSelectionChanged(EventArgs e)
    {
        base.OnSelectionChanged(e);

        // Reading the box's own ink is done by selecting pieces of it, and putting the caret back afterwards. That
        // is not the person moving the caret, and asking for a whole repaint every time would be asking for one
        // while painting, for a picture that has not changed.
        if (!measuring)
        {
            Invalidate();
        }
    }

    /// <summary>Reflowing the text moves the faces, including the ones that were not on a line ending before.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // Reflowing moves the lines as well as the faces on them: what was one display line becomes two, and each
        // of those has its own tallest ink.
        RenewInk();
        Invalidate();
    }

    /// <summary>The room's font is what an unstyled line is worn in, so every line's height goes with it.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        RenewInk();
    }

    /// <summary>
    /// The first moment the box can be asked anything about how it lays its text out, so it asks at once: the
    /// first paint of a room has a face in it as often as not, and an answer that arrives a paint late is a face
    /// painted one place and corrected to another.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        RenewInk();
    }

    /// <summary>
    /// Losing or gaining the caret changes the colour behind a selected face, so the faces are asked for again.
    /// <para>
    /// A selected face is hidden under a patch of the selection colour, which is right while the box has the caret
    /// and wrong the moment it does not: an unfocused RichEdit window draws no selection at all, measured, so the
    /// blue patch would be a block of colour with a face on it, sitting where no selection is.
    /// </para>
    /// </summary>
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);

        Invalidate();
    }

    /// <summary>As above: the selection the control was drawing is gone, and so is the colour the patch matched.</summary>
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);

        Invalidate();
    }

    // The parameter type is spelled out because this project calls its chat message `Message` everywhere, and
    // the windowing kind of the same name would otherwise be the one being referred to here.
    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        var asked = PasteRequested;

        if (m.Msg == WmPaste && asked is not null && Focused && IsPasteGesture(ModifierKeys))
        {
            asked(this, EventArgs.Empty);

            return;
        }

        base.WndProc(ref m);

        // The control has drawn its text over the whole client, so the faces and the hint go on top of that - after,
        // not before, because paint underneath the control's own glyphs is paint nobody sees.
        if (m.Msg == WmPaint)
        {
            Paints++;
            PaintOverlay();
        }

        // Scrolling is the movement nobody tells this control about: the RichEdit window scrolls its own client and
        // then repaints only the strip it has just exposed, so every face already on screen would stay where it was
        // drawn. The wheel and the two bars are the whole of it, and the box is two lines tall - a whole-client
        // repaint costs nothing next to being wrong about where a person's own face is.
        if (m.Msg is WmMouseWheel or WmHScroll or WmVScroll)
        {
            Invalidate();
        }
    }

    /// <summary>
    /// Whether the keys held down say "paste" - Ctrl+V, or Shift+Insert - as opposed to nothing at all.
    /// <para>
    /// This is not a flourish, it is the fix for a bug that was measured rather than imagined. The RichEdit window
    /// behind this control is sent messages with the paste message number while it is being created - three of
    /// them, before a form has finished showing, with nothing focused and nobody at the keyboard. Honouring those
    /// meant that entering a room pasted whatever happened to be sitting on the clipboard into the message box,
    /// which is a thing a person notices only after the room has seen it. A paste is a paste when somebody asks
    /// for one with a key, and a message wearing the number of a paste is not somebody asking.
    /// </para>
    /// </summary>
    public static bool IsPasteGesture(Keys modifiers) => (modifiers & (Keys.Control | Keys.Shift)) != 0;
}
