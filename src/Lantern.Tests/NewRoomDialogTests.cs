using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Tests for the naming dialog of Figure 5 — it must not accept an unusable name.
/// </summary>
public class NewRoomDialogTests
{
    [Fact]
    public void OkDisabledForEmptyName()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog();
            Assert.False(Ok(dialog).Enabled);

            dialog.RoomNameText = "  ";
            Assert.False(Ok(dialog).Enabled);

            dialog.RoomNameText = "Study group";
            Assert.True(Ok(dialog).Enabled);
            Assert.Equal(string.Empty, Error(dialog).Text);
        });
    }

    [Fact]
    public void OkDisabledForTooLongName()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog();
            dialog.RoomNameText = new string('x', ValidationRules.RoomNameMaxLength + 1);
            Assert.False(Ok(dialog).Enabled);
            Assert.Contains("characters", Error(dialog).Text);
        });
    }

    [Fact]
    public void ExtraValidationReportsDuplicates()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog(
                initial: "x",
                extraValidation: name =>
                    name.Equals("Study group", StringComparison.OrdinalIgnoreCase)
                        ? "A room with that name already exists."
                        : null);

            dialog.RoomNameText = "study GROUP";
            Assert.False(Ok(dialog).Enabled);
            Assert.Equal("A room with that name already exists.", Error(dialog).Text);

            dialog.RoomNameText = "Lab";
            Assert.True(Ok(dialog).Enabled);
        });
    }

    [Fact]
    public void InitialValueIsPreFilledForRename()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog("Rename Room", "Study group");
            Assert.Equal("Rename Room", dialog.Text);
            Assert.Equal("Study group", dialog.RoomNameText);
            Assert.True(Ok(dialog).Enabled);
        });
    }

    /// <summary>
    /// The tests below type. Everything above sets <see cref="NewRoomDialog.RoomNameText"/>, which validates on
    /// its own on the way in - so those tests could never tell whether typing reaches the rules at all, and the
    /// dialog spent its whole life so far opening on "Room name is required.", being typed into, and staying
    /// greyed. The host-side tests answer the question themselves (<c>AskForRoomName</c> is a seam), so a real
    /// dialog was never even opened. A claim about what happens "as you type" needs a test that types.
    /// </summary>
    [Fact]
    public void TypingANameMakesTheOkButtonClickable()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog();
            Assert.False(Ok(dialog).Enabled);
            Assert.Equal("Room name is required.", Error(dialog).Text);

            TypeInto(dialog, "Study group");

            Assert.True(Ok(dialog).Enabled);
            Assert.Equal(string.Empty, Error(dialog).Text);

            // What the report was about: a name typed into the dialog is a name the dialog is ready to take.
            // (Clicking it cannot be tested from here - see TheOkButtonCarriesNoAnswerOfItsOwn for why the click
            // route is out of reach of this suite, and what is pinned down instead.)
            Assert.Equal("Study group", dialog.RoomNameText);
        });
    }

    [Fact]
    public void TypingTheNameBackOutGreysTheOkButtonAgain()
    {
        // The button promises "this name is usable", so it cannot go on promising it after the name has gone.
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog("Rename Room", "Study group");
            Assert.True(Ok(dialog).Enabled);

            TypeInto(dialog, "");

            Assert.False(Ok(dialog).Enabled);
            Assert.Equal("Room name is required.", Error(dialog).Text);
        });
    }

    [Fact]
    public void ADuplicateIsRefusedWhileTheDuplicateIsStillInTheBox()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog(
                extraValidation: name =>
                    name.Equals("Study group", StringComparison.OrdinalIgnoreCase)
                        ? "A room with that name already exists."
                        : null);

            TypeInto(dialog, "study GROUP");
            Assert.False(Ok(dialog).Enabled);
            Assert.Equal("A room with that name already exists.", Error(dialog).Text);

            // The last keystroke is the one that matters: a name made unusable by a duplicate becomes usable
            // again when it stops being one, without reopening anything.
            TypeInto(dialog, "Lab");
            Assert.True(Ok(dialog).Enabled);
            Assert.Equal(string.Empty, Error(dialog).Text);
        });
    }

    [Fact]
    public void TheBoxWillNotLetANameBeTypedTooLong()
    {
        // The dialog refuses a too-long name, and a person cannot type one: the box is given the rule's own
        // ceiling, so the keystroke that would go past it never arrives. What is asserted is that the two
        // ceilings are the same number - widen the box without touching the rule and the "characters or fewer"
        // message below starts appearing to somebody who cannot see what they did wrong.
        //
        // (Not asserted: that the framework refuses the keystroke. MaxLength guards WM_CHAR, a real keystroke;
        // this harness gets text in through the selection, which is not what a keyboard does.)
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog();

            Assert.Equal(ValidationRules.RoomNameMaxLength, Name(dialog).MaxLength);
        });
    }

    [Fact]
    public void TheOkButtonCarriesNoAnswerOfItsOwn()
    {
        // A button with a DialogResult of its own hands the form that answer once the click handler has run,
        // happy or not - so the dialog's promise that it refuses to close while the name is unusable would be
        // the framework's promise to keep, and the framework has never heard of room names. Reachable by hand:
        // type a name, and have it stop being usable between the last keystroke and the click (another window
        // taking that room), and the name the guard refused goes out to the caller anyway.
        //
        // The click itself is out of reach of this suite: Button.PerformClick does nothing until the window can
        // be selected, and these windows are never drawn. So what is pinned down is the setting the guard rests
        // on - Accept() is the only thing in here allowed to answer, and it answers only after asking.
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog();

            Assert.Equal(DialogResult.None, Ok(dialog).DialogResult);
            Assert.Equal(DialogResult.Cancel, Cancel(dialog).DialogResult);   // taking it back needs no checking
        });
    }

    /// <summary>
    /// Types into the box the way a keyboard does: one character at a time, through the selection, which is the
    /// input path (and so the one that honours MaxLength). Assigning Text would be a paste, not typing - and it
    /// would sail past the limit, which is half of what the last test is checking.
    /// </summary>
    private static void TypeInto(Form form, string text)
    {
        var box = Name(form);
        box.SelectAll();
        box.SelectedText = string.Empty;   // clearing with a keyboard is typing over what is there

        foreach (var character in text)
        {
            box.SelectionStart = box.TextLength;
            box.SelectedText = character.ToString();
        }
    }

    private static TextBox Name(Form form) => (TextBox)form.Controls.Find("txtRoomName", searchAllChildren: true)[0];

    private static Button Ok(Form form) => (Button)form.Controls.Find("btnOk", searchAllChildren: true)[0];

    private static Button Cancel(Form form) => (Button)form.Controls.Find("btnCancel", searchAllChildren: true)[0];

    private static Label Error(Form form) => (Label)form.Controls.Find("lblError", searchAllChildren: true)[0];
}
