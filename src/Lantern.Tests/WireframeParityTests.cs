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
    public void TheBottomRowIsShareTheMessageTheSend()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));

            Assert.Equal("Share", ((Button)Find(form, "btnShare")).Text);
            Assert.Equal("Send", ((Button)Find(form, "btnSend")).Text);

            // The drawn box says "message ...", which is what a placeholder is for: the box is empty and a
            // person still knows what goes in it.
            Assert.Equal("Message", ((TextBox)Find(form, "txtMessage")).PlaceholderText);
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

    private static Control Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
            ?? throw new InvalidOperationException($"no control named {name}");
}
