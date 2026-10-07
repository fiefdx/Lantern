using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T3.4 and T3.5: the room buttons on the two windows — scenario 3 in clicks rather than in prose.
/// <para>
/// The dialogs are answered by the test rather than by a person: <see cref="HostForm"/> and
/// <see cref="ClientForm"/> keep the asking in one overridable method each. What is under test is the
/// button, the service call behind it and the list that has to refresh; a modal in the middle of that
/// would make it untestable, and the dialogs themselves have their own suite.
/// </para>
/// </summary>
public class RoomManagementUiTests
{
    [Fact]
    public void NewAddsTheRoomTheUserNamed()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new AnswerableHost(service) { Answer = "Garden" };

            form.Show();   // a button only answers to PerformClick once its window exists

            Click(form, "btnNew");

            Assert.Contains("Garden", service.Rooms.Select(r => r.Name));
            Assert.Contains("Garden", RoomNames(form));       // the list the host sees is the same list
        });
    }

    [Fact]
    public void CancellingTheNameAddsNothing()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new AnswerableHost(service) { Answer = null };

            form.Show();   // a button only answers to PerformClick once its window exists
            var before = service.Rooms.Select(r => r.Name).ToArray();

            Click(form, "btnNew");

            Assert.Equal(before, service.Rooms.Select(r => r.Name));
        });
    }

    [Fact]
    public void DeleteAsksFirstAndThenRemovesTheRoom()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new AnswerableHost(service) { Confirms = false };

            form.Show();   // a button only answers to PerformClick once its window exists
            SelectRoom(form, "Room 2");

            Click(form, "btnDelete");

            // "Are you sure" answered no means nothing happened at all.
            Assert.Contains("Room 2", service.Rooms.Select(r => r.Name));

            form.Confirms = true;
            Click(form, "btnDelete");

            Assert.DoesNotContain("Room 2", service.Rooms.Select(r => r.Name));
            Assert.DoesNotContain("Room 2", RoomNames(form));
        });
    }

    [Fact]
    public void DeleteWithoutASelectionSaysSoInsteadOfGuessing()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new AnswerableHost(service) { Confirms = true };

            form.Show();   // a button only answers to PerformClick once its window exists

            Click(form, "btnDelete");

            // Guessing would mean deleting whichever room happened to be first in the list.
            Assert.Equal(3, service.Rooms.Count);
            Assert.Contains("select a room", Status(form), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RenameChangesTheNameInTheListAndOnTheService()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new AnswerableHost(service) { Answer = "Kitchen Table" };

            form.Show();   // a button only answers to PerformClick once its window exists
            SelectRoom(form, "Room 1");

            ClickMenu(form, "renameMenuItem");

            Assert.Contains("Kitchen Table", service.Rooms.Select(r => r.Name));
            Assert.DoesNotContain("Room 1", service.Rooms.Select(r => r.Name));
            Assert.Contains("Kitchen Table", RoomNames(form));
        });
    }

    [Fact]
    public void RenamingToANameAlreadyTakenIsRefused()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new AnswerableHost(service) { Answer = "Room 2" };

            form.Show();   // a button only answers to PerformClick once its window exists
            SelectRoom(form, "Room 1");

            ClickMenu(form, "renameMenuItem");

            // Two rooms answering to the same name would make the room list ambiguous, and the join
            // rule matches names case-insensitively anyway.
            Assert.Equal("Room 1", service.Rooms.First().Name);
        });
    }

    [Fact]
    public void TheClientJoinsTheRoomItClicks()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Victory", isHost: false);
            using var form = new ClientForm(service);

            form.Show();   // a button only answers to PerformClick once its window exists

            SelectRoom(form, "Room 2");

            Assert.True(StandingIn(service, "Room 2"));
            Assert.Single(service.Members);                       // in there, on my own
            Assert.Contains("in room \"Room 2\"", Status(form), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheClientSwitchesRoomsOneAtATime()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Victory", isHost: false);
            using var form = new ClientForm(service);

            form.Show();   // a button only answers to PerformClick once its window exists

            SelectRoom(form, "Room 1");
            SelectRoom(form, "Room 3");

            // F6: one room at a time, so the first room must have let go.
            Assert.True(StandingIn(service, "Room 3"));
            Assert.False(StandingIn(service, "Room 1"));
        });
    }

    [Fact]
    public void LeavingIsAskedForAndThenMeansNowhere()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Victory", isHost: false);
            using var form = new AnswerableClient(service) { Confirms = false };

            form.Show();   // a button only answers to PerformClick once its window exists
            SelectRoom(form, "Room 1");

            Click(form, "btnLeave");

            Assert.True(StandingIn(service, "Room 1"));            // asked, refused, nothing changed

            form.Confirms = true;
            SelectRoom(form, "Room 2");
            Click(form, "btnLeave");

            Assert.DoesNotContain(service.Rooms, r => r.Members.Any(u => u.Nickname == "Victory"));
            Assert.Empty(service.Members);
            Assert.Contains("left the room", Status(form), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LeaveWithoutARoomSaysSoInsteadOfThrowing()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Victory", isHost: false);
            using var form = new AnswerableClient(service) { Confirms = true };

            form.Show();   // a button only answers to PerformClick once its window exists

            Click(form, "btnLeave");

            Assert.Contains("not in a room", Status(form), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void WhenTheHostClosesTheRoomTheWindowEndsUpStandingNowhere()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Victory", isHost: false);
            using var form = new ClientForm(service);

            form.Show();   // a button only answers to PerformClick once its window exists
            SelectRoom(form, "Room 2");

            service.SimulateRoomClosed("Room 2");                 // what the host's delete looks like from here

            Assert.DoesNotContain("Room 2", RoomNames(form));
            Assert.Contains("Select a room to enter", ChatPane(form), StringComparison.Ordinal);
        });
    }

    // ------------------------------------------------------------------ plumbing

    private static Control Find(Control owner, string name)
        => owner.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
           ?? throw new InvalidOperationException($"the window has no control named '{name}'");

    private static void Click(Form form, string button)
        => Find<Button>(form, button).PerformClick();

    /// <summary>Menu items are not controls, so they are looked up on the menu strip itself.</summary>
    private static void ClickMenu(Form form, string item)
    {
        var menu = Find<MenuStrip>(form, "menuStrip");
        var menuItem = (ToolStripMenuItem)menu.Items.Find(item, searchAllChildren: true)[0];
        menuItem.PerformClick();
    }

    private static void SelectRoom(Form form, string name)
        => Find<ListBox>(form, "lstRooms").SelectedItem = name;

    private static IEnumerable<string> RoomNames(Form form)
        => Find<ListBox>(form, "lstRooms").Items.Cast<string>();

    /// <summary>The status strip's label is a tool strip item, not a control, so it is not in Controls.</summary>
    private static string Status(Control form)
    {
        var strip = Find<StatusStrip>(form, "statusStrip");
        var label = strip.Items.Cast<ToolStripItem>().Single(item => item.Name == "lblStatus");
        return label.Text ?? string.Empty;
    }

    private static T Find<T>(Control owner, string name)
        where T : Control
        => (T)Find(owner, name);

    private static string ChatPane(Control form)
        => Find<RichTextBox>(form, "rtbChat").Text;

    /// <summary>Whether this window's own user is standing in a room - what "current" means on the fake.</summary>
    private static bool StandingIn(FakeChatService service, string roomName)
        => service.Rooms.Single(r => r.Name == roomName).Members.Any(u => u.Nickname == service.Nickname);

    /// <summary>The host window with a person made of fields.</summary>
    private sealed class AnswerableHost : HostForm
    {
        public AnswerableHost(IChatService service)
            : base(service)
        {
        }

        // Fields rather than properties: the WinForms analyzer wants every public property on a form to
        // take part in designer serialisation, and these have nothing to do with the designer.

        /// <summary>The name to give the dialog, or null for Cancel.</summary>
        public string? Answer;

        public bool Confirms = true;

        protected override string? AskForRoomName(string title, string current, Func<string, string?> validate)
            => Answer is null || validate(Answer) is not null ? null : Answer;

        protected override bool ConfirmDeleteRoom(string roomName) => Confirms;

        protected override bool ConfirmQuit() => true;
    }

    /// <summary>The client window with a person made of fields.</summary>
    private sealed class AnswerableClient : ClientForm
    {
        public AnswerableClient(IChatService service)
            : base(service)
        {
        }

        public bool Confirms = true;

        protected override bool ConfirmLeaveRoom(string roomName) => Confirms;
    }
}
