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
/// faces the font has but GDI will not render (<see cref="FaceOverlay"/>), and say what an empty box is for - a
/// <see cref="RichTextBox"/> has no placeholder of any kind, and the only place anything can be painted on it is
/// the paint message, since <c>OnPaint</c> is never called (see <see cref="PaintOverlay"/>).
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
    /// The word an empty box shows, so that an empty editor does not read as a broken one.
    /// <para>
    /// The word is drawn rather than typed: the box's emptiness is what the Send button reads, and a message that
    /// said "Message" because nobody overwrote the hint would be a very strange thing to send to a room.
    /// </para>
    /// </summary>
    public const string Hint = "Message";

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

        Invalidate();
    }

    /// <summary>The caret moving changes where the faces go, and which of them are on a selection.</summary>
    protected override void OnSelectionChanged(EventArgs e)
    {
        base.OnSelectionChanged(e);

        Invalidate();
    }

    /// <summary>Reflowing the text moves the faces, including the ones that were not on a line ending before.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        Invalidate();
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
