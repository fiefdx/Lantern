using System.Reflection;
using Lantern.Core;
using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Smoke tests for the shared chat window and its two role flavours (Figures 3 and 4), now driven
/// by <see cref="FakeChatService"/> rather than hard-coded demo data: what the window shows must
/// follow what the service holds.
/// </summary>
public class ChatFormTests
{
    [Fact]
    public void HostWindowIsTitledHostAndCanRename()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            Assert.Equal("Lantern - host", form.Text);
            Assert.NotNull(Find(form, "btnNew"));
            Assert.NotNull(Find(form, "btnDelete"));
            Assert.Null(Find(form, "btnLeave"));
            Assert.True(Menu(form, "renameMenuItem")!.Enabled);
        });
    }

    [Fact]
    public void ClientWindowIsTitledClientAndCannotRename()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.ClientForm(new FakeChatService("Victory", isHost: false));
            Assert.Equal("Lantern - client", form.Text);
            Assert.NotNull(Find(form, "btnLeave"));
            Assert.Null(Find(form, "btnNew"));
            Assert.Null(Find(form, "btnDelete"));
            Assert.False(Menu(form, "renameMenuItem")!.Enabled);
        });
    }

    [Fact]
    public void RoomListComesFromTheService()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var rooms = (ListBox)Find(form, "lstRooms")!;

            Assert.Equal(service.Rooms.Select(r => r.Name), rooms.Items.Names());
        });
    }

    [Fact]
    public void ARoomAddedToTheServiceAppearsInTheList()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var rooms = (ListBox)Find(form, "lstRooms")!;

            service.Rooms.Add(new Room("Announced later"));

            Assert.Contains("Announced later", rooms.Items.Names());
        });
    }

    [Fact]
    public void DeletingTheCurrentRoomDropsOutOfIt()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var rooms = (ListBox)Find(form, "lstRooms")!;
            var send = (Button)Find(form, "btnSend")!;

            rooms.SelectedIndex = 0;
            Assert.True(send.Enabled);

            service.Rooms.RemoveAt(0);

            Assert.False(send.Enabled);
        });
    }

    [Fact]
    public void SelectingARoomFillsTheMemberListAndEnablesSend()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Victory", isHost: false);
            using var form = new Lantern.UI.ClientForm(service);
            var rooms = (ListBox)Find(form, "lstRooms")!;
            var members = (ListBox)Find(form, "lstMembers")!;
            var send = (Button)Find(form, "btnSend")!;
            var message = (RichTextBox)Find(form, "txtMessage")!;

            Assert.False(send.Enabled);
            Assert.False(message.Enabled);

            rooms.SelectedIndex = 0;

            Assert.True(send.Enabled);
            Assert.True(message.Enabled);
            Assert.Equal(service.Members.Select(u => u.Nickname), members.Items.Cast<string>().Select(n => n.Replace(" (you)", string.Empty)));
            Assert.Contains("Victory (you)", members.Items.Cast<string>());
        });
    }

    [Fact]
    public void SendingAppendsToTheTranscriptAndClearsTheBox()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;
            var box = (RichTextBox)Find(form, "txtMessage")!;
            var send = (Button)Find(form, "btnSend")!;

            // A control that has never been shown has no window handle, so PerformClick would do
            // nothing; the click handler is called the way the button's own event would.
            var onClick = typeof(Lantern.UI.ChatForm).GetMethod("BtnSend_Click",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            rooms.SelectedIndex = 0;
            box.Text = "hello from the test";
            onClick.Invoke(form, [null, EventArgs.Empty]);

            Assert.Contains("hello from the test", chat.Text);
            Assert.Equal(string.Empty, box.Text);
            Assert.Contains("hello from the test", service.Transcript.OfType<TextMessage>().Select(m => m.Body));
        });
    }

    [Fact]
    public void IncomingTrafficFromTheServiceLandsInTheTranscript()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            // A window that has not entered a room has nowhere to put traffic, so enter one first.
            rooms.SelectedIndex = 0;

            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "arrived from the wire"));

            // F13: who sent it is in the headline and what they sent is on the line below, in another colour.
            // Asserting the old one-line form would have been asserting the bug.
            Assert.Contains("arrived from the wire", chat.Text);
            Assert.DoesNotContain("User 2: arrived from the wire", chat.Text);
            Assert.Contains(chat.Lines, line => line.EndsWith("User 2", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TrafficFromAnotherRoomIsNotDrawnOnTheOneShowing()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;

            // The service keeps a transcript per room, so traffic for somewhere else must not be
            // drawn as though it belonged to the room on screen.
            service.SimulateIncoming(new TextMessage("User 2", "Room 2", "somewhere else entirely"));

            Assert.DoesNotContain("somewhere else entirely", chat.Text);
        });
    }

    [Fact]
    public void ReEnteringARoomReplaysItsHistory()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;

            Assert.Contains("hi", chat.Text);
        });
    }

    [Fact]
    public void BusyFlagDisablesSendAndShare()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            var send = (Button)Find(form, "btnSend")!;
            var share = (Button)Find(form, "btnShare")!;
            var rooms = (ListBox)Find(form, "lstRooms")!;

            rooms.SelectedIndex = 0;
            Assert.True(share.Enabled);

            var setBusy = typeof(Lantern.UI.ChatForm).GetMethod("SetBusy",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            setBusy.Invoke(form, [true]);
            Assert.False(send.Enabled);
            Assert.False(share.Enabled);

            setBusy.Invoke(form, [false]);
            Assert.True(send.Enabled);
            Assert.True(share.Enabled);
        });
    }

    [Fact]
    public void LabelsSayRoomNotGroup()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm(new FakeChatService("Haitao"));
            Assert.Equal("Rooms", ((Label)Find(form, "lblRoomsHeader")!).Text);

            var rooms = (ListBox)Find(form, "lstRooms")!;
            Assert.All(rooms.Items.Cast<object>(), item => Assert.StartsWith("Room", item.ToString()));
        });
    }

    private static ToolStripMenuItem? Menu(Control parent, string name)
        => parent.Controls.OfType<MenuStrip>()
            .SelectMany(m => Walk(m.Items.OfType<ToolStripMenuItem>()))
            .FirstOrDefault(i => i.Name == name);

    private static IEnumerable<ToolStripMenuItem> Walk(IEnumerable<ToolStripMenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Walk(item.DropDownItems.OfType<ToolStripMenuItem>()))
            {
                yield return child;
            }
        }
    }

    private static Control? Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault();
}
