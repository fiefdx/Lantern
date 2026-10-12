using Lantern.Core;
using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Three facts about the room list that only become dangerous together: it is drawn from the service, drawing
/// it moves the selection, and a selection is how a person says “put me in that room”. A room list arriving
/// from the service was therefore read as a request to join, and joining a room is how a room list gets sent.
/// </summary>
/// <remarks>
/// <para>
/// With a window whose service answers on the window's own thread - which is not a contrivance, it is how the
/// host's window works, since its chat engine is in the same process and replies before the call returns - each
/// turn stacked on the last on the same stack, and the window died with a stack overflow on the line that puts
/// the selection back. Where it needed a room to refuse it, it asked for that room for ever; where it did not,
/// it still knocked again on every list that came in.
/// </para>
/// <para>
/// So these tests are not about appearance. They count how many times a window asks for the same room, because
/// a window that asks twice has usually been asked by itself.
/// </para>
/// </remarks>
public sealed class RoomListRefreshTests
{
    [Fact]
    public async Task ARoomListFromTheServiceIsNotSomebodyClickingTheRoomList()
    {
        await Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new PlainForm(service);

            form.Show();
            var rooms = Rooms(form);

            rooms.SelectedIndex = 0;
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 1", "the window to enter Room 1");

            // The host makes a room while nobody touches the list. The window redraws - and drawing is not
            // asking: it must not go and join the room it merely drew, nor leave the one it is in.
            var asked = service.JoinRequests.Count;
            await service.CreateRoomAsync("Study");
            Application.DoEvents();

            Assert.Equal(asked, service.JoinRequests.Count);
            Assert.Equal("Room 1", form.RoomNameNow);
            Assert.True(Items(Rooms(form)).Contains("Study"), $"the list shows {string.Join(", ", Items(Rooms(form)))}");
        });
    }

    [Fact]
    public async Task ARoomThatWillNotHaveUsIsNotKnockedOnAgain()
    {
        await Sta.RunAsync(async () =>
        {
            // Room 2 refuses entry, and the service sends a room list with the refusal - exactly what the real
            // server does when it turns a join down (T6.1) - on this thread, mid-call, as the host's own
            // window experiences it.
            var service = new FakeChatService("Amara", isHost: false)
            {
                RefuseRoomNamed = "Room 2",
                AnnounceRoomsOnJoin = true,
            };

            using var form = new PlainForm(service);
            form.Show();
            var rooms = Rooms(form);

            rooms.SelectedIndex = 1;
            await Watch.UntilAsync(() => service.JoinRequests.Count >= 1, "the window to ask for Room 2");
            await Watch.UntilAsync(() => form.StatusText.Contains("will not have you", StringComparison.Ordinal),
                "the refusal to reach the status bar");

            // One knock. Before this was fixed the window was inside the room list when it asked again, and
            // again: each ask redrew the list, each redraw asked, and there is no bottom to that.
            Assert.Equal(1, service.JoinRequests.Count(n => n == "Room 2"));
            Assert.Single(service.JoinRequests);

            // And the bar tells the person what happened rather than leaving them standing in a room they are
            // not in.
            Assert.Null(form.RoomNameNow);
            Assert.Contains("Room 2", form.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task TheHighlightComesOffARoomTheWindowDidNotGetInto()
    {
        await Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Amara", isHost: false)
            {
                RefuseRoomNamed = "Room 2",
                AnnounceRoomsOnJoin = true,
            };

            using var form = new PlainForm(service);
            form.Show();
            var rooms = Rooms(form);

            rooms.SelectedIndex = 1;
            await Watch.UntilAsync(() => form.StatusText.Contains("will not have you", StringComparison.Ordinal),
                "the refusal to be reported");

            // A highlight is a promise that this window is in that room, or will be. Keeping it on a room that
            // said no is what made every later room list try the door again - and it tells the person they are
            // somewhere they are not.
            Assert.Null(rooms.SelectedItem);
            Assert.Contains("Select a room to enter", Pane(form), StringComparison.Ordinal);
            Assert.False(((Button)Find(form, "btnSend")).Enabled);
        });
    }

    [Fact]
    public async Task AWindowStillWorksAfterARoomHasRefusedIt()
    {
        await Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Amara", isHost: false)
            {
                RefuseRoomNamed = "Room 2",
                AnnounceRoomsOnJoin = true,
            };

            using var form = new PlainForm(service);
            form.Show();
            var rooms = Rooms(form);

            rooms.SelectedIndex = 1;
            await Watch.UntilAsync(() => form.StatusText.Contains("will not have you", StringComparison.Ordinal),
                "the refusal to be reported");

            // The guard that stops the loop must be let go of, or the list is a painting from here on: the next
            // room the person clicks would do nothing at all.
            service.RefuseRoomNamed = null;
            rooms.SelectedIndex = 0;
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 1", "Room 1 to be entered after the refusal");

            var box = (RichTextBox)Find(form, "txtMessage");
            var send = (Button)Find(form, "btnSend");
            Assert.True(send.Enabled);

            box.Text = "still here";
            send.PerformClick();
            await Watch.UntilAsync(
                () => service.Transcript.Any(m => m is TextMessage t && t.Body == "still here"),
                "the message to be sent");
        });
    }

    [Fact]
    public async Task ARoomVanishingSomewhereElseDoesNotPullTheWindowOutOfItsRoom()
    {
        await Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Naledi", isHost: false)
            {
                RefuseRoomNamed = "Room 2",
                AnnounceRoomsOnJoin = true,
            };

            using var form = new PlainForm(service);
            form.Show();
            var rooms = Rooms(form);

            rooms.SelectedIndex = 0;
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 1", "the window to be in Room 1");

            rooms.SelectedIndex = 1;
            await Watch.UntilAsync(() => form.StatusText.Contains("will not have you", StringComparison.Ordinal),
                "Room 2 to refuse the window");

            // Now a room this window was never in is deleted. That is somebody else's news: the window stays in
            // Room 1. It used to be dragged out, because the list was still holding a highlight for Room 2 and
            // the redraw read that highlight as the thing worth preserving - so the missing highlight looked
            // like a missing room, and the window left the one it was really standing in.
            service.SimulateRoomClosed("Room 2");

            Assert.Equal("Room 1", form.RoomNameNow);
            Assert.Equal(2, service.JoinRequests.Count);
        });
    }

    // ------------------------------------------------------------------ plumbing

    private static Control Find(Control owner, string name)
        => owner.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
           ?? throw new InvalidOperationException($"the window has no control named '{name}'");

    private static ListBox Rooms(Control owner) => (ListBox)Find(owner, "lstRooms");

    private static IEnumerable<string> Items(ListBox list) => list.Items.Cast<object>().Select(i => i.ToString() ?? string.Empty);

    private static string Pane(Control owner) => ((RichTextBox)Find(owner, "rtbChat")).Text;

    /// <summary>A chat window with nothing overridden: the point here is what the window does by itself.</summary>
    private sealed class PlainForm : Lantern.UI.ChatForm
    {
        public PlainForm(IChatService service) : base(service)
        {
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? RoomNameNow => CurrentRoom?.Name;
    }
}
