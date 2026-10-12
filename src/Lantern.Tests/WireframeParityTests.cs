using Lantern.Core;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T6.2: the wireframes are what the class was asked to build to, so the words on the window are pinned here.
/// A polish pass that is not written down is a polish pass that gets undone in the next task.
/// </summary>
public sealed class WireframeParityTests
{
    [Fact]
    public void TheWindowsAreTitledAsTheFiguresTitleThem()
    {
        Sta.Run(() =>
        {
            using var host = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            using var client = new Lantern.UI.ClientForm(new FakeChatService("Victory", isHost: false));

            // "Lantern - host" and "Lantern - client": the two figures, word for word. It matters more than it
            // looks - in a field test four laptops are open at once and the title bar is how you know which
            // one is the host before you click anything.
            Assert.Equal("Lantern - host", host.Text);
            Assert.Equal("Lantern - client", client.Text);
        });
    }

    [Fact]
    public void TheMenusAreTheFourInTheFigure()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            var menu = Assert.IsType<MenuStrip>(Find(form, "menuStrip"));

            // File, Edit, View, Help - in that order, as drawn.
            Assert.Equal(
                new[] { "&File", "&Edit", "&View", "&Help" },
                menu.Items.Cast<ToolStripMenuItem>().Select(i => i.Text ?? string.Empty));

            // And the items the scenarios need are where the figure puts them.
            Assert.Contains("&Quit", Items(menu, "fileMenu"));
            Assert.Contains("&Rename room", Items(menu, "editMenu"));
            Assert.Contains("&Clear chat pane", Items(menu, "viewMenu"));
            Assert.Contains("&About", Items(menu, "helpMenu"));
        });
    }

    [Fact]
    public void TheWindowIsThreeColumnsWithTheChattingAreaInTheMiddle()
    {
        // Rooms on the left, members on the right, and the chatting area down the middle - which is also what the
        // proposal's figure says, and what a person reads off the window: two lists with the conversation between
        // them. The columns are columns of the same window and nothing more: no pane belongs to two of them.
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            form.Show();
            Pump();

            var table = (TableLayoutPanel)Find(form, "tableLayout");
            var rooms = Find(form, "pnlRooms");
            var chat = Find(form, "pnlChat");
            var members = Find(form, "pnlMembers");

            Assert.Equal(0, table.GetColumn(rooms));
            Assert.Equal(1, table.GetColumn(chat));
            Assert.Equal(2, table.GetColumn(members));

            // Said as geometry as well, because a column index is what the code believes and the screen is what
            // the person sees, and it is the screen that is wrong when the two disagree.
            Assert.True(rooms.Right <= chat.Left + 1, "the rooms are not to the left of the chatting area");
            Assert.True(chat.Right <= members.Left + 1, "the chatting area is not to the left of the members");

            // Each list lives in its own column under its own header, and nothing from one column reaches into
            // another: a strip of buttons running under the rooms list is buttons that look disabled.
            Assert.Same(rooms, Find(form, "pnlRoomHeader").Parent);
            Assert.Same(rooms, Find(form, "lstRooms").Parent);
            Assert.True(Find(form, "pnlRoomHeader").Bottom <= Find(form, "lstRooms").Top + 1);

            Assert.Same(members, Find(form, "pnlMembersHeader").Parent);
            Assert.Same(members, Find(form, "lstMembers").Parent);
            Assert.True(Find(form, "pnlMembersHeader").Bottom <= Find(form, "lstMembers").Top + 1);

            Assert.True(rooms.Height >= Find(form, "lstRooms").Height);
            Assert.True(members.Height >= Find(form, "lstMembers").Height);
        });
    }

    [Fact]
    public void TheChattingAreaIsTheTranscriptOverTheInputArea()
    {
        // Two rows in the middle column: what was said, and the means of saying more. The transcript takes what is
        // left, which is the only arrangement that makes sense of a window that is dragged taller.
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            form.Show();
            Pump();

            var chat = (TableLayoutPanel)Find(form, "pnlChat");
            var transcript = Find(form, "rtbChat");
            var input = Find(form, "pnlInput");

            Assert.Equal(2, chat.RowCount);
            Assert.Equal(0, chat.GetRow(transcript));
            Assert.Equal(1, chat.GetRow(input));
            Assert.True(transcript.Bottom <= input.Top + 1, "the transcript is not above the input area");

            // The transcript is the row that gives, so making the window taller has to make the transcript taller
            // rather than padding out the two rows of the input area.
            var taller = new Size(form.Width, form.Height + 120);

            var before = transcript.Height;
            form.Size = taller;
            Pump();

            Assert.True(transcript.Height > before, "a taller window did not give the extra room to the transcript");
        });
    }

    [Fact]
    public void TheInputAreaIsItsToolbarOverItsBoxAndSend()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            form.Show();
            Pump();

            var input = (TableLayoutPanel)Find(form, "pnlInput");
            var strip = Find(form, "pnlFormat");
            var box = Find(form, "txtMessage");
            var send = Find(form, "btnSend");

            // Two rows: the tools across the top, and underneath them the box with Send beside it. Every function
            // that puts something into a message is on the strip, and Send is not, because Send is the thing that
            // ends the message rather than one of the ways of making it up.
            Assert.Equal(2, input.RowCount);
            Assert.Equal(2, input.ColumnCount);

            Assert.Equal(0, input.GetRow(strip));
            Assert.Equal(1, input.GetRow(box));
            Assert.Equal(1, input.GetRow(send));
            Assert.Equal(0, input.GetColumn(box));
            Assert.Equal(1, input.GetColumn(send));

            Assert.True(strip.Bottom <= box.Top + 1, "the tools are not above the box");
            Assert.True(box.Right <= send.Left + 1, "the send button is not beside the box");
            Assert.True(box.Top <= send.Bottom && send.Top <= box.Bottom, "the send button is not on the box's row");

            Assert.Equal(strip, Find(form, "btnBold").Parent);
            Assert.Equal(strip, Find(form, "btnItalic").Parent);
            Assert.Equal(strip, Find(form, "btnUnderline").Parent);
            Assert.Equal(strip, Find(form, "btnSize").Parent);
            Assert.Equal(strip, Find(form, "btnColor").Parent);
            Assert.Equal(strip, Find(form, "btnEmoji").Parent);
            Assert.Equal(strip, Find(form, "btnShare").Parent);
            Assert.NotEqual(strip, send.Parent);

            // And the whole of it belongs to the middle column. This is the flaw the layout was asked to fix: the
            // tool strip used to be laid across the window, so the half of it over the rooms list was a row of
            // tools with nothing under them.
            var chat = Find(form, "pnlChat");

            // Measured on the screen rather than in each panel's own coordinates, because these are not siblings -
            // and it is the screen that shows whether a strip of tools has wandered over the rooms list.
            var onChat = chat.RectangleToScreen(chat.ClientRectangle);
            var onInput = input.RectangleToScreen(input.ClientRectangle);
            var onStrip = strip.RectangleToScreen(strip.ClientRectangle);

            Assert.True(onInput.Left >= onChat.Left - 1 && onInput.Right <= onChat.Right + 1, "the input area is not inside the chatting area");
            Assert.True(onStrip.Right <= onChat.Right + 1, "the tools reach past the chatting area");
        });
    }

    [Fact]
    public void TheToolsDoNotFallOnOneAnotherWhenTheWindowIsAtItsNarrowest()
    {
        // A strip of tools is a row of fixed-width buttons in a space that a person can drag smaller, which is
        // the shape of window where one button ends up on top of another, or off the end. Overlapping tools are
        // worse than a clipped one: the person clicks Bold and Emoji answers.
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            form.Show();
            Pump();

            form.Size = new Size(form.MinimumSize.Width, form.MinimumSize.Height);
            Pump();

            var strip = Find(form, "pnlFormat");
            var onStrip = strip.RectangleToScreen(strip.ClientRectangle);

            var tools = new[] { "btnBold", "btnItalic", "btnUnderline", "btnSize", "btnColor", "btnEmoji", "btnShare" }
                .Select(name => (name, onScreen: Find(form, name).RectangleToScreen(Find(form, name).ClientRectangle)))
                .ToList();

            foreach (var (name, onScreen) in tools)
            {
                Assert.True(onScreen.Left >= onStrip.Left - 1, $"{name} has fallen off the left end of the tool strip");
                Assert.True(onScreen.Right <= onStrip.Right + 1, $"{name} has fallen off the right end of the tool strip");
            }

            for (var one = 0; one < tools.Count; one++)
            {
                for (var other = one + 1; other < tools.Count; other++)
                {
                    Assert.False(
                        tools[one].onScreen.IntersectsWith(tools[other].onScreen),
                        $"{tools[one].name} and {tools[other].name} are on top of one another");
                }
            }
        });
    }

    [Fact]
    public void TheRoomButtonsStayInTheColumnTheyBelongTo()
    {
        // The host's New and Delete live in the rooms header. When the header was a cell of a flat grid they could
        // not escape it; now they are in a panel of their own with an anchored edge, which is exactly the arrangement
        // that puts a room button over the transcript.
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            form.Show();
            Pump();

            var rooms = Find(form, "pnlRooms");
            var onRooms = rooms.RectangleToScreen(rooms.ClientRectangle);

            var actions = Find(form, "pnlRoomActions");
            var onActions = actions.RectangleToScreen(actions.ClientRectangle);

            Assert.True(onActions.Width > 0, "the room management buttons are not in the header at all");
            Assert.True(onActions.Left >= onRooms.Left - 1 && onActions.Right <= onRooms.Right + 1, "the room buttons are outside the rooms column");
        });
    }

    [Fact]
    public void TheInputAreaSaysWhatItIsForAndSendSaysWhatItDoes()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            Assert.Equal("Share", ((Button)Find(form, "btnShare")).Text);
            Assert.Equal("Send", ((Button)Find(form, "btnSend")).Text);
            Assert.Equal("Emoji", ((Button)Find(form, "btnEmoji")).Text);

            // The drawn box says "message ...", which is what a placeholder is for: the box is empty and a person
            // still knows what goes in it. A RichTextBox has no PlaceholderText, so the box draws the word itself.
            var box = (Lantern.UI.MessageInput)Find(form, "txtMessage");

            Assert.Equal("Message", Lantern.UI.MessageInput.Hint, ignoreCase: true);
            Assert.True(box.ShowsHint);
        });
    }

    [Fact]
    public void ThePanesAreNamedWhatTheProposalCallsThem()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);

            // The hand-drawn figure labels these "Groups" and "Users". The app says "Rooms" and "Members",
            // because that is the vocabulary of the proposal and of the feature list (room management, room
            // member list) and of the file on disk, rooms.json - and a marker reading the paper and using the
            // program should be reading the same words. The figure is a drawing of a layout, not a second
            // naming scheme, and this is the one place where the two differ.
            Assert.Equal("Rooms", ((Label)Find(form, "lblRoomsHeader")).Text);
            Assert.Equal("Members", ((Label)Find(form, "lblMembersHeader")).Text);

            // Once a room is in view the count joins the member header, as drawn: "Users (3)".
            ((ListBox)Find(form, "lstRooms")).SelectedIndex = 0;
            Application.DoEvents();

            Assert.Matches(@"^Members \(\d\)$", ((Label)Find(form, "lblMembersHeader")).Text);
        });
    }

    [Fact]
    public void TheJoinNoticeReadsAsTheFigureWritesIt()
    {
        // "User 2 - join this chat", from the transcript in the figure. The wording lives in the domain so the
        // server's frames and the transcript cannot drift apart.
        Assert.Equal("User 2 - join this chat", Lantern.Domain.SystemMessage.JoinBody("User 2"));
        Assert.Equal("User 2 - left this chat", Lantern.Domain.SystemMessage.LeaveBody("User 2"));
    }

    private static IEnumerable<string> Items(MenuStrip menu, string menuName)
        => ((ToolStripMenuItem)menu.Items[menuName]!).DropDownItems.Cast<ToolStripItem>().Select(i => i.Text ?? string.Empty);

    /// <summary>Lets the layout actually happen, which is the only way a geometry assertion means anything.</summary>
    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static Control Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
            ?? throw new InvalidOperationException($"no control named {name}");
}
