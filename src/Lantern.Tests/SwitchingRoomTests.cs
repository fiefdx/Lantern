using Lantern.Core;
using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Switching rooms by clicking the list. The hard part is not the click, it that switching a room is the one
/// thing this window does where a room list arrives <em>while it is busy</em>: the service answers a join with
/// a room list, and in the host's own window that answer arrives inside the call, before the window has
/// finished moving itself. A list drawn in that moment does not yet know where the window is going.
/// </summary>
public sealed class SwitchingRoomTests
{
    [Fact]
    public async Task ClickingAnotherRoomTakesTheWindowAndTheHighlightTogether()
    {
        await Sta.RunAsync(async () =>
        {
            // The host's own window: its engine is in process, so the room list for the join arrives on this
            // thread, inside JoinRoomAsync, while the window is still between rooms.
            var service = new FakeChatService("Haitao")
            {
                AnnounceRoomsOnJoin = true,
            };

            using var form = new PlainForm(service);
            form.Show();
            var rooms = Rooms(form);

            rooms.SelectedIndex = 0;
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 1", "the window to be in Room 1");

            // Now switch. Half a second of daylight between the two assertions, and both must be true: the
            // window is in Room 2, and the list says Room 2.
            rooms.SelectedIndex = 1;
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 2", "the window to move to Room 2");

            Assert.Equal("Room 2", SelectedName(rooms));
            Assert.Contains("in room \"Room 2\"", form.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ClickingBackAgainFindsTheRoomWhereTheLastOneLeftTheHighlight()
    {
        await Sta.RunAsync(async () =>
        {
            var service = new FakeChatService("Naledi")
            {
                AnnounceRoomsOnJoin = true,
            };

            using var form = new PlainForm(service);
            form.Show();
            var rooms = Rooms(form);

            rooms.SelectedIndex = 0;
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 1", "Room 1");
            rooms.SelectedIndex = 2;
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 3", "Room 3");

            // Three rooms, two switches, and the list still agrees with the window. A person switching back
            // and forth is exactly who notices when it stops agreeing, because the second click then looks
            // like it did nothing.
            Assert.Equal("Room 3", SelectedName(rooms));

            rooms.SelectedIndex = 1;
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 2", "Room 2");
            Assert.Equal("Room 2", SelectedName(rooms));

            Assert.Equal(3, service.JoinRequests.Count(n => n is not null));
        });
    }

    [Fact]
    public async Task AWindowDoesNotEndUpWhereTheClickItMadeTwoClicksAgo()
    {
        await Sta.RunAsync(async () =>
        {
            var service = new SlowToAnswer("Thabo")
            {
                AnnounceRoomsOnJoin = true,
            };

            using var form = new PlainForm(service);
            form.Show();
            var rooms = Rooms(form);

            // Room 2, then, before the first answer is in, Room 3. The window may take Room 2's answer in any
            // order it likes; what it must not do is finish in the room that the older click asked for.
            rooms.SelectedIndex = 1;
            rooms.SelectedIndex = 2;

            await Watch.UntilAsync(() => service.Asks >= 2, "both joins to be asked for");
            service.Answer();
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 3", "the newer room to win");

            Assert.Equal("Room 3", SelectedName(rooms));
        });
    }

    /// <summary>
    /// A rename is a name changing and not a room changing, and the window is where that has to be true. Its
    /// row stays where it is, still highlighted; what it had been reading stays on the screen; and the room it
    /// believes itself in becomes the room the service now holds under that identity - because the object it
    /// was holding is a transcript that nothing else in the program is writing to any more.
    /// <para>
    /// Rooms used to be found by name, so this test used to fail in the middle: the window went on showing the
    /// room it had been in, and every line said after the rename went somewhere else.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ARoomRenamedUnderTheWindowIsStillTheRoomItWasIn()
    {
        await Sta.RunAsync(async () =>
        {
            // A host window, because a host is the one that renames rooms. The fake raises the same room list
            // the real service does, which is what gives the window the new name.
            var service = new FakeChatService("Haitao");
            using var form = new PlainForm(service);
            form.Show();
            var rooms = Rooms(form);

            rooms.SelectByName("Room 2");
            await Watch.UntilAsync(() => form.RoomNameNow == "Room 2", "the window to be in Room 2");

            service.SimulateIncoming(new TextMessage("Victory", "Room 2", "before the rename"));
            await Watch.UntilAsync(() => ChatPane(form).Contains("before the rename", StringComparison.Ordinal), "the line to be on the screen");

            await service.RenameByNameAsync("Room 2", "Quiet Corner");
            await Watch.UntilAsync(() => form.RoomNameNow == "Quiet Corner", "the window to follow its room's new name");

            // The row is the same row, saying something new, and the transcript on the screen is the one the
            // window was already reading.
            Assert.Equal("Quiet Corner", SelectedName(rooms));
            Assert.Contains("before the rename", ChatPane(form), StringComparison.Ordinal);

            // And the window is attached to the room that is live now: a line said after the rename arrives in
            // this transcript rather than into a room that only this window still believes in.
            service.SimulateIncoming(new TextMessage("Victory", "Quiet Corner", "after the rename"));
            await Watch.UntilAsync(() => ChatPane(form).Contains("after the rename", StringComparison.Ordinal), "the next line to arrive here");
        });
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>
    /// A fake that holds a join open for a moment, the way a Bluetooth link does: the answer comes back some
    /// indeterminate time later, so two clicks can be in the air together.
    /// </summary>
    private sealed class SlowToAnswer : FakeChatService
    {
        // One permit, and it starts taken: the first join waits, and stays waiting until the test says the
        // link has answered. Every join after that queues behind it in the order they were asked for.
        private readonly SemaphoreSlim gate = new(0);

        /// <summary>
        /// How many joins have been asked for, whether or not the link has answered any of them. A test that
        /// waits for the second click to have gone out cannot wait for the first answer as well - that would
        /// be waiting for the thing it is arranging to be slow.
        /// </summary>
        public int Asks { get; private set; }

        public SlowToAnswer(string nickname) : base(nickname)
        {
        }

        /// <summary>The link answers: the join that has been waiting longest goes through first.</summary>
        public void Answer() => gate.Release();

        public override async Task JoinRoomAsync(Guid? roomId, CancellationToken cancel = default)
        {
            Asks++;

            // No ConfigureAwait(false) anywhere in here: the window that started this is on an STA thread and
            // the continuation has to get back to it.
            await gate.WaitAsync(cancel);
            try
            {
                await base.JoinRoomAsync(roomId, cancel);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private static Control Find(Control owner, string name)
        => owner.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
           ?? throw new InvalidOperationException($"the window has no control named '{name}'");

    private static ListBox Rooms(Control owner) => (ListBox)Find(owner, "lstRooms");

    /// <summary>What the highlighted row says. A row is a room, and this is the only fair way to read one.</summary>
    private static string? SelectedName(ListBox rooms) => rooms.SelectedItem?.ToString();

    private static string ChatPane(Control owner) => ((RichTextBox)Find(owner, "rtbChat")).Text;

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
