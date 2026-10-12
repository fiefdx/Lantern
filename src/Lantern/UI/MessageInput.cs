namespace Lantern.UI;

/// <summary>
/// The box a message is written in (v1.7).
/// <para>
/// A <see cref="RichTextBox"/> rather than a <see cref="TextBox"/> because the message box is an editor now: it
/// holds bold and colour, and it is two lines tall so a person can see the sentence they are composing. Everything
/// about <em>what a message is</em> - the length rule, what gets sent, what the styling means - stays in the window,
/// because that is what the window knows and the box does not. What is here is the two things only the control can
/// do, and one of them is a workaround for something the control cannot do at all.
/// </para>
/// </summary>
public class MessageInput : RichTextBox
{
    private const int WmPaste = 0x007D;

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
    /// hint is drawn here (see <see cref="OnPaint"/>), and this is the part a test can ask about without a screen.
    /// </para>
    /// </summary>
    public bool ShowsHint => Text.Length == 0;

    /// <summary>
    /// Draws the hint, because nothing else will.
    /// </summary>
    /// <remarks>
    /// The word is painted rather than typed: an empty box has to stay empty, since its emptiness is what the Send
    /// button reads. Grey, at the box's own font, at the same place the first character would go - close enough
    /// that the sentence appears to start where the hint was, rather than jumping.
    /// </remarks>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!ShowsHint)
        {
            return;
        }

        TextRenderer.DrawText(
            e.Graphics,
            Hint,
            Font,
            new Point(2, 1),
            ComposerStyle.HintColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
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
