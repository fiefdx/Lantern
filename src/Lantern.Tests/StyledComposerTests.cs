using System.Reflection;
using System.Text;
using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using SkiaSharp;
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

            // Send is beside the box on the box's own row rather than up on the strip with the tools: it is what
            // ends a message, not one of the ways of making one up.
            Assert.Same(box.Parent, SendButton(form).Parent);
            Assert.True(box.Right <= SendButton(form).Left + 1, "the send button is not beside the box");
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
            Pick(form, "btnColor", "Green");
            Send(form);

            var styles = Styles(service, "red letters");

            // Named by the name the menu shows, and checked as the hex the wire carries: the menu says Green, the
            // room hears #008000, and both have to be true of the same click.
            Assert.Equal(Palette()["Green"], styles[0].Color);
            Assert.Equal("#008000", styles[0].Color);
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
            Pick(form, "btnSize", "Large");
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

            Pick(form, "btnSize", "Large");

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
            Pick(form, "btnColor", "Purple");

            Assert.Equal(Palette()["Purple"], Hex(Find(form, "btnColor")!.ForeColor));
        });
    }

    [Fact]
    public void TheSizeToolOpensAMenuOfTheThreeSizes()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var menu = Menu(form, "btnSize");

            Assert.Equal(["Small", "Normal", "Large"], menu.Items.Cast<ToolStripMenuItem>().Select(i => i.Text));

            // The menu means what it says by being what it says: the Small row is set in the small font. Three words
            // of one size ask the person reading them to already know the answer.
            var points = menu.Items.Cast<ToolStripMenuItem>().Select(i => i.Font.SizeInPoints).ToList();

            Assert.True(points[0] < points[1] && points[1] < points[2],
                $"the size menu was set {string.Join(", ", points)} points, which does not get bigger going down");

            // And the menu is found from the button it belongs to, which is also how a right-click finds it.
            Assert.Same(menu, ((Button)Find(form, "btnSize")!).ContextMenuStrip);
            Assert.NotSame(menu, Menu(form, "btnColor"));
        });
    }

    [Fact]
    public void TheToolsStillFitWhatTheySayAtTheSizeTheyShow()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var size = (Button)Find(form, "btnSize")!;
            var colour = (Button)Find(form, "btnColor")!;
            var box = Box(form);

            // A room first, and words to put the caret in: with nowhere to send the box is shut, the menu picks
            // nothing, and this test would measure the same 9-point button three times and think it had proved
            // something.
            Enter(form);
            box.Text = "measure me";
            box.Select(0, 6);

            // The size tool is drawn at the size it is about to give, which means its own word gets bigger every
            // time it is used - and a button whose word is cut off says "A" where it meant "Aa". Measured, not
            // eyeballed, because the widths depend on the machine's font as much as on these numbers.
            foreach (var name in new[] { "Small", "Normal", "Large" })
            {
                Pick(form, "btnSize", name);

                Assert.True(Fits(size),
                    $"at {size.Font.SizeInPoints} points the size tool's '{size.Text}' needs " +
                    $"{Needed(size)}px of the {size.ClientSize.Width}px it has");
            }

            Assert.True(Fits(colour),
                $"the colour tool's '{colour.Text}' needs {Needed(colour)}px of the {colour.ClientSize.Width}px it has");
        });
    }

    [Fact]
    public void TheArrowSayingAToolOpensAMenuIsAGlyphTheFontHas()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var family = ((Button)Find(form, "btnSize")!).Font.FontFamily.Name;

            Assert.Contains((char)DropdownArrow, ((Button)Find(form, "btnSize")!).Text);
            Assert.Contains((char)DropdownArrow, ((Button)Find(form, "btnColor")!).Text);

            // Measured rather than assumed, because a code point the font has no glyph for comes back as .notdef - a
            // replacement box on a toolbar, meaning nothing at all, and about the width of the arrow it replaced. The
            // font's own table is the only honest answer to "will this draw".
            using var typeface = SKFontManager.Default.MatchFamily(family);

            Assert.NotNull(typeface);
            Assert.True(typeface!.GetGlyph(DropdownArrow) != 0,
                $"'{family}' has no glyph for U+{DropdownArrow:X4}, so both menu tools would wear a box");
        });
    }

    [Fact]
    public void TheColorToolOpensAMenuOfTheWholePalette()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var items = Menu(form, "btnColor").Items.Cast<ToolStripMenuItem>().ToList();

            // Every colour the composer can put on the wire is offered, each in the colour it stands for, Automatic
            // last. A menu offering three of six leaves the rest to be found by cycling, which is what this replaced.
            Assert.Equal(["Red", "Orange", "Green", "Blue", "Purple", "Pink", "Automatic"],
                items.Select(i => i.Text));

            foreach (var item in items.Where(i => i.Text != "Automatic"))
            {
                Assert.Equal(item.Text, Palette().Single(p => p.Value == Hex(item.ForeColor)).Key);
            }
        });
    }

    [Fact]
    public void AutomaticColorIsTheOneThatSaysNothing()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var box = Box(form);

            Enter(form);
            box.Text = "back to the window's own";
            box.Select(0, 4);
            Pick(form, "btnColor", "Blue");
            box.Select(0, 4);
            Pick(form, "btnColor", "Automatic");
            Send(form);

            // Automatic is not a colour, it is the absence of one. On the wire an absence is an empty field, not a
            // hex code that matches one person's theme and misarrives as another's.
            Assert.All(Styles(service, "back to the window's own"), style => Assert.Null(style.Color));
        });
    }

    [Fact]
    public void TheMenuSaysWhichChoiceIsAlreadyMade()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var box = Box(form);

            Enter(form);
            box.Text = "big and small";
            box.Select(0, 3);
            Pick(form, "btnSize", "Large");

            Assert.True(SizeItem(form, "Large").Checked, "the size was chosen and the menu still says otherwise");
            Assert.False(SizeItem(form, "Small").Checked);

            // The caret is what the menu reports, because the caret is what the next character gets.
            box.Select(10, 3);
            Assert.True(SizeItem(form, "Normal").Checked);
            box.Select(0, 3);
            Assert.True(SizeItem(form, "Large").Checked);
        });
    }

    [Fact]
    public void TheEmojiToolIsAFaceWithNothingBesideIt()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var emoji = (Button)Find(form, "btnEmoji")!;

            // The face is the label. "Emoji" beside a face says it twice, and the second time in a language the face
            // does not need. The word still exists for the two people asking in other ways: one who cannot see the
            // screen, and one hovering over a button they have not clicked yet.
            Assert.Equal(string.Empty, emoji.Text);
            Assert.NotNull(emoji.Image);
            Assert.Equal("Emoji", emoji.AccessibleName);
            Assert.Contains("emoji", ToolTipOf(form, emoji), StringComparison.OrdinalIgnoreCase);
            AssertWhole(emoji);
        });
    }

    [Fact]
    public void TheShareToolIsAPageNotAWord()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var share = (Button)Find(form, "btnShare")!;

            Assert.Equal(string.Empty, share.Text);
            Assert.NotNull(share.Image);
            Assert.Contains("share", share.AccessibleName, StringComparison.OrdinalIgnoreCase);
            AssertWhole(share);

            // A page says "a file", not "a picture or a file", so the hover has to finish what the icon starts.
            var tip = ToolTipOf(form, share);
            Assert.Contains("picture", tip, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("file", tip, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void AFaceIsDrawnWholeWhereverItIsDrawn()
    {
        // Measured before this was fixed: in a cell of N pixels drawn at an em of N, the ink of a face reached the
        // last column and row at every size tried, and a flag ran off both sides besides. A COLR emoji's ink box is
        // not its em box, so no choice of baseline makes it fit. Whatever is clipped here is clipped in the toolbar,
        // in the picker and in the transcript at once, because one method draws all three.
        Assert.True(Lantern.UI.ColorEmoji.Available, "this machine has no emoji font to measure");

        foreach (var face in new[] { "🙂", "😀", "👍", "🎉", "🇻🇳", "🐧" })
        {
            foreach (var pixels in new[] { 16, 18, 22, 30 })
            {
                var glyph = Lantern.UI.ColorEmoji.Glyph(face, pixels);

                Assert.NotNull(glyph);
                var ink = InkBox(glyph, $"{face} at {pixels} was drawn with nothing inked in it");

                Assert.True(ink.Left >= 1 && ink.Top >= 1 && ink.Right <= pixels - 2 && ink.Bottom <= pixels - 2,
                    $"{face} at {pixels} was drawn from ({ink.Left},{ink.Top}) to ({ink.Right},{ink.Bottom}) in a " +
                    $"{pixels}x{pixels} cell, so part of it is outside the cell");

                // Square, because that is what an emoji is and what the line it sits on is sized by.
                Assert.Equal(pixels, glyph.Width);
                Assert.Equal(pixels, glyph.Height);
                glyph.Dispose();
            }
        }
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

    /// <summary>The arrow on the two tools that open a menu (U+25BC, black down-pointing triangle).</summary>
    private const int DropdownArrow = 0x25BC;

    /// <summary>The word a tool draws, in the font it is drawn in, does it fit inside the tool?</summary>
    private static bool Fits(Button tool) => Needed(tool) <= tool.ClientSize.Width;

    private static int Needed(Button tool) =>
        TextRenderer.MeasureText(tool.Text, tool.Font, Size.Empty, TextFormatFlags.NoPadding).Width;

    /// <summary>Chooses one row of the menu a tool carries, the way a person does.</summary>
    private static void Pick(Control form, string tool, string item) => Item(form, tool, item).PerformClick();

    private static ToolStripMenuItem Item(Control form, string tool, string item) =>
        Menu(form, tool).Items.Cast<ToolStripMenuItem>().Single(i => i.Text == item);

    private static ToolStripMenuItem SizeItem(Control form, string item) => Item(form, "btnSize", item);

    /// <summary>The menu a tool button carries - found from the button, because that is where it belongs.</summary>
    private static ContextMenuStrip Menu(Control form, string tool)
    {
        var menu = ((Button)Find(form, tool)!).ContextMenuStrip;

        Assert.NotNull(menu);

        return menu!;
    }

    /// <summary>The palette as name to hex: what the menu shows, and what the wire carries.</summary>
    private static Dictionary<string, string?> Palette() =>
        ComposerStyle.Palette.ToDictionary(ink => ink.Name, ink => ink.Color is { } colour ? Hex(colour) : null);

    private static string Hex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    private static string ToolTipOf(Control form, Control target)
    {
        var field = typeof(Lantern.UI.ChatForm).GetField("toolTip", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tip = (System.Windows.Forms.ToolTip)field.GetValue((Lantern.UI.ChatForm)form)!;

        return tip.GetToolTip(target) ?? string.Empty;
    }

    /// <summary>A button whose picture is no bigger than the room the button gives it, and is not blank.</summary>
    private static void AssertWhole(Button button)
    {
        Assert.NotNull(button.Image);
        Assert.True(button.Image!.Width <= button.ClientSize.Width && button.Image.Height <= button.ClientSize.Height,
            $"{button.Name} holds a {button.Image.Width}x{button.Image.Height} picture in a " +
            $"{button.ClientSize.Width}x{button.ClientSize.Height} space, and WinForms cuts the difference");

        // A picture with nothing inked in it looks exactly like a button whose icon failed to load.
        InkBox((Bitmap)button.Image!, $"{button.Name}'s picture is blank");
    }

    /// <summary>The rectangle some picture's ink covers, counting the last inked row and column as inside.</summary>
    private record struct InkBounds(int Left, int Top, int Right, int Bottom);

    /// <summary>
    /// The rectangle the non-transparent pixels of a picture cover, last inked row and column counted as inside.
    /// A picture with nothing inked in it is the failure the caller would otherwise have to think of, so it fails here.
    /// </summary>
    private static InkBounds InkBox(Bitmap bitmap, string because)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A > 0)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        Assert.True(right >= 0, because);

        return new InkBounds(left, top, right, bottom);
    }

    /// <summary>The styling the room was told about for one line - and a complaint if the line never got there.</summary>
    private static IReadOnlyList<StyledRun> Styles(FakeChatService service, string body)
    {
        var message = service.Transcript.OfType<TextMessage>().LastOrDefault(m => m.Body == body);

        Assert.NotNull(message);

        return message!.Styles;
    }
}
