using System.Reflection;
using System.Text;
using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T12.3: the message box becomes an editor. A strip of tools above it, room for two lines in it, and what was
/// formatted inside it arrives in the room formatted - while the sentence itself stays exactly what the box holds,
/// so the length rule, the caret and the wire all go on telling the same story.
/// </summary>
public class StyledComposerTests
{
    // ---------------------------------------------------------------------------- the strip and the box

    [Fact]
    public void TheToolsSitAboveTheBoxTheyFormat()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            var strip = Find(form, "pnlFormat")!;
            var box = Box(form);

            foreach (var name in new[] { "btnBold", "btnItalic", "btnUnderline", "btnSize", "btnColor" })
            {
                Assert.Same(strip, Find(form, name)!.Parent);
            }

            // Emoji and Share are tools of the input area too, so they belong on the strip rather than beside it.
            Assert.Same(strip, Find(form, "btnEmoji")!.Parent);
            Assert.Same(strip, Find(form, "btnShare")!.Parent);

            Assert.True(strip.Bottom <= box.Top + 1, "the strip is not above the box");
            Assert.True(strip.Bottom <= SendButton(form).Top + 1, "the send button is not below the strip");
            Assert.NotSame(strip, SendButton(form).Parent);
        });
    }

    [Fact]
    public void TheBoxIsTwoLinesTall()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);

            Assert.True(box.Multiline, "a message that wraps has nowhere to go");
            Assert.True(box.Height >= 2 * box.Font.Height,
                $"the box is {box.Height}px tall, which is under two lines of {box.Font.Height}px");
        });
    }

    [Fact]
    public void AnEmptyBoxSaysWhatGoesInIt()
    {
        // A RichTextBox has no PlaceholderText, so the word is drawn by the box itself. A box with nothing in it
        // and nothing saying what it is for reads as a broken window rather than an empty one.
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);

            Assert.True(box.ShowsHint, "an empty box says nothing about itself");

            box.Text = "something typed";

            Assert.False(box.ShowsHint);
        });
    }

    // ------------------------------------------------------------------------- typing it in and sending it

    [Fact]
    public void AWordMadeBoldArrivesBold()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "shout now";
            box.Select(0, 5);
            Click(form, "btnBold");
            Send(form);

            var styles = Styles(service, "shout now");

            Assert.Equal(2, styles.Count);
            Assert.Equal(5, styles[0].Length);
            Assert.True(styles[0].Bold);
            Assert.Equal(4, styles[1].Length);
            Assert.False(styles[1].Bold);
        });
    }

    [Fact]
    public void APlainMessageSaysNothingAboutStyling()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "nothing special";
            Send(form);

            // Not "one plain run": an unformatted line must put no style field on the wire at all, so an old peer
            // and a new one read exactly the same frame (T12.1).
            Assert.Empty(Styles(service, "nothing special"));
        });
    }

    [Fact]
    public void MakingALineBoldAndThenItalicKeepsBoth()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "emphasis";
            box.Select(0, 4);
            Click(form, "btnBold");
            Click(form, "btnItalic");
            Click(form, "btnUnderline");
            Send(form);

            var styles = Styles(service, "emphasis");

            // The trap this is here for: handing the control a new Font for the second flag replaced the whole
            // style mask, and the bold quietly disappeared under the italic.
            Assert.True(styles[0].Bold, "the bold was lost when italic went over it");
            Assert.True(styles[0].Italic);
            Assert.True(styles[0].Underline);
            Assert.False(styles[^1].Bold);
            Assert.False(styles[^1].Italic);
        });
    }

    [Fact]
    public void BoldOnAndBoldOffLeavesNothingToSay()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "indecisive";
            box.Select(0, 6);
            Click(form, "btnBold");
            Click(form, "btnBold");
            Send(form);

            Assert.Empty(Styles(service, "indecisive"));
        });
    }

    [Fact]
    public void AWordGivenAColorArrivesWithThatColor()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "red letters";
            box.Select(0, 3);
            Click(form, "btnColor");
            Send(form);

            var styles = Styles(service, "red letters");

            Assert.Equal("#C00000", styles[0].Color);
            Assert.Null(styles[^1].Color);
        });
    }

    [Fact]
    public void AWordMadeLargeArrivesLarge()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "say it bigger";
            box.Select(0, 4);
            Click(form, "btnSize");
            Send(form);

            var styles = Styles(service, "say it bigger");

            Assert.Equal(StyledRun.Large, styles[0].Size);
            Assert.Equal(StyledRun.Normal, styles[^1].Size);
        });
    }

    [Fact]
    public void EmphasisDoesNotOutliveTheMessageItWasTypedFor()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "first";
            box.Select(0, 5);
            Click(form, "btnBold");
            Send(form);

            box.Text = "second";
            Send(form);

            // Style state that survives a send is how a chat window turns into a ransom note: every word from
            // there on in bold, with no idea how it started.
            Assert.All(Styles(service, "second"), style => Assert.False(style.Bold));
        });
    }

    [Fact]
    public void SpacesAroundAMessageDoNotKnockTheRunsOutOfStep()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "   shout   ";
            box.Select(3, 5);
            Click(form, "btnBold");
            Send(form);

            // The body is trimmed before it is sent. Runs counted against the untrimmed text would describe a
            // longer sentence than the one travelling with them, and the whole annotation is then refused
            // (T12.1) - the words would arrive and the emphasis would not.
            var styles = Styles(service, "shout");

            Assert.Single(styles);
            Assert.Equal(5, styles[0].Length);
            Assert.True(styles[0].Bold);
        });
    }

    [Fact]
    public void MoreStyleChangesThanTheWireCarriesAreFoldedNotDropped()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);

            // Two hundred characters alternating plain and bold: a hundred runs, which is more than the wire will
            // carry. The message has to arrive with its formatting rather than without any.
            var body = new StringBuilder();

            for (var i = 0; i < 100; i++)
            {
                body.Append(i % 2 == 0 ? "ab" : "CD");
            }

            box.Text = body.ToString();
            box.Select(0, body.Length);
            Click(form, "btnBold");

            for (var at = 0; at < body.Length; at += 4)
            {
                box.Select(at, 2);
                Click(form, "btnBold");
            }

            Send(form);

            var styles = Styles(service, body.ToString());

            Assert.NotEmpty(styles);
            Assert.True(styles.Count <= StyledRun.MaxRuns,
                $"the wire was handed {styles.Count} runs, which is more than it takes");
            Assert.Equal(body.Length, styles.Sum(s => s.Length));
        });
    }

    // ---------------------------------------------------------------------------- what the box lets you do

    [Fact]
    public void EnterSendsAndShiftEnterIsLeftToTheBox()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "two lines";
            box.Select(box.TextLength, 0);

            var sent = Key(form, Keys.Enter, shift: false);

            Assert.True(sent.SuppressKeyPress, "Enter reached the box and put a line break where a send belonged");
            Assert.Equal(string.Empty, box.Text);
            Assert.Contains("two lines", service.Transcript.OfType<TextMessage>().Select(m => m.Body));

            box.Text = "one line";
            box.Select(box.TextLength, 0);

            var breakLine = Key(form, Keys.Enter, shift: true);

            // The line break is the control's own work; this window only has to keep out of its way.
            Assert.False(breakLine.SuppressKeyPress);
            Assert.False(breakLine.Handled);
            Assert.DoesNotContain("one line", service.Transcript.OfType<TextMessage>().Select(m => m.Body));
        });
    }

    [Fact]
    public void TheLimitCountsCharactersSomebodyCounts()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);

            // A face is one character and two UTF-16 units. A control counting units would refuse the five
            // hundredth face at the wrong moment, silently, and say nothing about it.
            box.Text = string.Concat(Enumerable.Repeat("\U0001F600", ValidationRules.MessageMaxLength));

            Assert.Equal(ValidationRules.MessageMaxLength, ValidationRules.Length(box.Text));

            box.AppendText(" one more");

            Assert.Equal(ValidationRules.MessageMaxLength, ValidationRules.Length(box.Text));
            Assert.Contains(ValidationRules.MessageMaxLength.ToString(), form.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void WhatIsCutOffIsSaidOutOfLoud()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);

            box.Text = new string('x', ValidationRules.MessageMaxLength - 3);
            var said = form.StatusText;

            box.AppendText("abcde");

            Assert.Equal(ValidationRules.MessageMaxLength, ValidationRules.Length(box.Text));
            Assert.NotEqual(said, form.StatusText);
            Assert.False(string.IsNullOrWhiteSpace(form.StatusText), "the tail vanished without a word about it");
        });
    }

    [Fact]
    public void APasteArrivesAsTextAndNotAsSomebodyElsesFormatting()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);

            Enter(form);
            box.Text = "here comes: ";
            box.Select(box.TextLength, 0);

            Assert.True(Paste(form, () => "pasted words"));
            Assert.Equal("here comes: pasted words", box.Text);

            // Pasted rich text bringing its own bold would put formatting on screen that the person never asked
            // for - and the wire carries none of what Word thinks that sentence looks like.
            Assert.Equal(FontStyle.Regular, StyleAt(box, box.Text.IndexOf("pasted", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void APasteWithNothingToSaySaysSo()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);

            Enter(form);
            box.Text = "untouched";
            var said = form.StatusText;

            Assert.False(Paste(form, () => string.Empty));

            Assert.Equal("untouched", box.Text);
            Assert.NotEqual(said, form.StatusText);
        });
    }

    // --------------------------------------------------------------------------- the tools say where they are

    [Fact]
    public void AToolShowsThatItIsOn()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);

            Enter(form);
            box.Text = "hello";
            box.Select(0, 2);
            Click(form, "btnBold");

            Assert.Equal(ComposerStyle.ToolOnColor, Find(form, "btnBold")!.BackColor);

            Click(form, "btnBold");

            Assert.Equal(ComposerStyle.ToolOffColor, Find(form, "btnBold")!.BackColor);
        });
    }

    [Fact]
    public void MovingTheCaretIntoPlainTextShowsTheToolIsOff()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);

            Enter(form);
            box.Text = "bold plain";
            box.Select(0, 4);
            Click(form, "btnBold");

            Assert.Equal(ComposerStyle.ToolOnColor, Find(form, "btnBold")!.BackColor);

            box.Select(6, 0);

            Assert.Equal(ComposerStyle.ToolOffColor, Find(form, "btnBold")!.BackColor);
        });
    }

    [Fact]
    public void TheSizeToolIsDrawnAtTheSizeItIsAboutToGive()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);
            var size = Find(form, "btnSize")!;

            Enter(form);

            Assert.Equal(box.Font.Size, size.Font.Size, 1);

            Click(form, "btnSize");

            Assert.True(size.Font.Size > box.Font.Size, "the tool does not show the size it is offering");
        });
    }

    [Fact]
    public void TheColorToolWearsTheColorItIsAboutToGive()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            Enter(form);
            Click(form, "btnColor");

            Assert.Equal(Color.FromArgb(0xC0, 0, 0), Find(form, "btnColor")!.ForeColor);
        });
    }

    [Fact]
    public void TheToolsAreGreyedOutWithTheBoxWhenThereIsNowhereToSend()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            // Wherever the box cannot be typed in, a tool that still worked would be formatting a message that
            // cannot be sent.
            Assert.False(box.Enabled, "this is not the state the test is about");

            foreach (var name in new[] { "btnBold", "btnItalic", "btnUnderline", "btnSize", "btnColor" })
            {
                Assert.False(Find(form, name)!.Enabled, $"{name} works while the message box does not");
            }
        });
    }

    [Fact]
    public void CtrlVIsWhatPutsWordsInTheBox()
    {
        Sta.Run(() =>
        {
            using var form = new HostForm(new FakeChatService("Haitao"));

            form.Show();
            Enter(form);

            WithClipboard("copied from elsewhere", () =>
            {
                KeyDown(form, Keys.V | Keys.Control);

                Assert.Equal("copied from elsewhere", Box(form).Text);
            });
        });
    }

    [Fact]
    public void AWindowOpeningPastesNothing()
    {
        // The RichEdit window behind the message box is sent messages with the paste message number while it is
        // being created - before the form has finished showing, with nobody at the keyboard. A box that believed
        // those pasted whatever was sitting on the clipboard into a message the person had not started typing, and
        // said nothing about it. So opening a window is not a paste, whatever the message says it is.
        const string clipboard = "something from earlier";

        Sta.Run(() =>
        {
            using var form = new HostForm(new FakeChatService("Haitao"));

            WithClipboard(clipboard, () =>
            {
                form.Show();
                Pump();
                Enter(form);
                Pump();

                Assert.Equal(string.Empty, Box(form).Text);
            });
        });
    }

    [Fact]
    public void APasteOnlyHappensWhenSomebodyHoldsAKey()
    {
        Assert.False(Lantern.UI.MessageInput.IsPasteGesture(Keys.None));
        Assert.True(Lantern.UI.MessageInput.IsPasteGesture(Keys.Control));
        Assert.True(Lantern.UI.MessageInput.IsPasteGesture(Keys.Shift));
    }

    // ------------------------------------------------------------------------------------------ the domain

    [Fact]
    public void TooManyRunsAreFoldedWithoutLosingTheSentence()
    {
        var runs = new List<StyledRun>();

        for (var i = 0; i < 300; i++)
        {
            runs.Add(i % 2 == 0 ? new StyledRun(1) : new StyledRun(1, Bold: true));
        }

        var folded = StyledRun.Fold(runs, StyledRun.MaxRuns);

        Assert.True(folded.Count <= StyledRun.MaxRuns);
        Assert.Equal(300, folded.Sum(r => r.Length));
        // Nothing invented: every run that comes out is a style that went in, whatever it ended up covering.
        Assert.All(folded, run => Assert.Contains(run with { Length = 0 }, runs.Select(r => r with { Length = 0 })));
    }

    [Fact]
    public void FoldingLeavesAloneWhatAlreadyFits()
    {
        var runs = new List<StyledRun> { new(3, Bold: true), new(4, Color: "#0000C0") };

        Assert.Equal(runs, StyledRun.Fold(runs, StyledRun.MaxRuns));
    }

    [Fact]
    public void AdjacentRunsWithTheSameStyleAreOneRun()
    {
        var folded = StyledRun.Fold(
            [new StyledRun(2, Bold: true), new StyledRun(3, Bold: true), new StyledRun(1)],
            StyledRun.MaxRuns);

        Assert.Equal(2, folded.Count);
        Assert.Equal(5, folded[0].Length);
    }

    // ------------------------------------------------------------------------------------------ helpers

    private static MessageInput Box(Control form) => (MessageInput)Find(form, "txtMessage")!;

    private static Button SendButton(Control form) => (Button)Find(form, "btnSend")!;

    private static Control? Find(Control root, string name)
    {
        if (root.Name == name)
        {
            return root;
        }

        foreach (Control child in root.Controls)
        {
            if (Find(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void Enter(Control form) => ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;

    /// <summary>Clicks the tool the way the mouse would: the handler its Click event is wired to.</summary>
    private static void Click(Control form, string tool) => Invoke(form, $"Btn{tool[3..]}_Click", Find(form, tool)!);

    private static void Send(Control form) => Invoke(form, "BtnSend_Click", SendButton(form));

    private static void Invoke(Control form, string handler, object argument)
        => typeof(Lantern.UI.ChatForm)
            .GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [argument, EventArgs.Empty]);

    /// <summary>Runs a body with this on the clipboard, then puts back whatever was there before.</summary>
    private static void WithClipboard(string words, Action body)
    {
        var was = Clipboard.ContainsText() ? Clipboard.GetText() : null;

        Clipboard.Clear();
        Clipboard.SetDataObject(words, copy: true);

        try
        {
            body();
        }
        finally
        {
            Clipboard.Clear();

            if (was is not null)
            {
                Clipboard.SetDataObject(was, copy: true);
            }
        }
    }

    /// <summary>Presses a key down in the message box, which is how the window's own key handling is reached.</summary>
    private static void KeyDown(Control form, Keys key)
        => typeof(Lantern.UI.ChatForm)
            .GetMethod("TxtMessage_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [Box(form), new KeyEventArgs(key)]);

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static bool Paste(Control form, Func<string> clipboardText)
        => (bool)typeof(Lantern.UI.ChatForm)
            .GetMethod("PastePlain", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [clipboardText])!;

    private static KeyEventArgs Key(Control form, Keys key, bool shift)
    {
        var e = new KeyEventArgs(shift ? key | Keys.Shift : key);

        typeof(Lantern.UI.ChatForm)
            .GetMethod("TxtMessage_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [Box(form), e]);

        return e;
    }

    private static FontStyle StyleAt(RichTextBox box, int index)
    {
        box.Select(index, 1);

        return box.SelectionFont?.Style ?? FontStyle.Regular;
    }

    /// <summary>The styling the room was told about for one line - and a complaint if the line never got there.</summary>
    private static IReadOnlyList<StyledRun> Styles(FakeChatService service, string body)
    {
        var message = service.Transcript.OfType<TextMessage>().LastOrDefault(m => m.Body == body);

        Assert.NotNull(message);

        return message!.Styles;
    }
}
