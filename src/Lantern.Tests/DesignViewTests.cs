using System.Reflection;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// The Windows Forms designer builds a window in order to draw it, and every window in this project is built
/// with the thing it shows - a chat, a question, a radio. Both halves of that are load-bearing and they pull
/// against each other: without a constructor the designer can use, a control cannot be moved without typing
/// numbers into a file; with a constructor anybody can use, the program gains windows that open with nothing
/// behind them and every button inert. These tests hold the door open and shut at the same time.
/// </summary>
public class DesignViewTests
{
    /// <summary>Every window a person might open in design view, which is every window there is.</summary>
    public static TheoryData<Type> EveryWindow() => new()
    {
        { typeof(StartupForm) },
        { typeof(ChatForm) },
        { typeof(HostForm) },
        { typeof(ClientForm) },
        { typeof(NewRoomDialog) },
        { typeof(ConfirmDialog) },
    };

    [Theory]
    [MemberData(nameof(EveryWindow))]
    public void EveryWindowHasTheConstructorTheDesignerUses(Type window)
        // The design host asks for a constructor with no arguments and has no way to invent the rest. If this
        // fails, the window opens as text and somebody has to guess what 8 is for.
        => Assert.NotNull(window.GetConstructor(Type.EmptyTypes));

    [Theory]
    [MemberData(nameof(EveryWindow))]
    public void NoWindowIsAbstractInTheWayDesignViewCannotStand(Type window)
        // An abstract class is a file design view can never open, which is why the shared chat window - the
        // transcript, the room list, the status bar, most of both windows - is concrete. See the other two
        // tests for what keeps it honest instead.
        => Assert.False(window.IsAbstract);

    [Fact]
    public void TheSharedChatWindowIsOpenToTheDesignerAndToNobodyElse()
    {
        // ChatForm was abstract, and that is the usual way to keep a chat window from being built around
        // nothing. It is concrete now because design view cannot open an abstract class, so the same keeping
        // is done by the shape of the constructors: exactly one public door, and it is the designer's.
        var door = Assert.Single(typeof(ChatForm).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        Assert.Empty(door.GetParameters());
    }

    [Fact]
    public void OutsideTheDesignerAChatWindowStillCannotBeBuiltWithoutAChat()
        // The door the designer walks through is not a door the program can walk through: each of these throws
        // before a single control exists, which is the rule the constructors have always had, still true after
        // they were opened up.
        => Sta.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => new ChatForm());
            Assert.Throws<InvalidOperationException>(() => new HostForm());
            Assert.Throws<InvalidOperationException>(() => new ClientForm());
        });

    [Fact]
    public void OutsideTheDesignerAConfirmationStillCannotBeBuiltWithoutItsQuestion()
        // "Are you sure?" is not a question this program is allowed to ask on its own behalf; the questions
        // live in ConfirmQuit, ConfirmDeleteRoom and ConfirmLeaveRoom, which is where the wording is decided.
        => Sta.Run(() => Assert.Throws<InvalidOperationException>(() => new ConfirmDialog()));

    [Fact]
    public void ABlankRoomDialogIsAWholeRoomDialog()
        // The exception, and a deliberate one: an empty room waiting to be named is a sensible thing to build
        // at run time as well as a necessary thing for the designer, so this window's door is open both ways.
        => Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog();

            Assert.Equal("New Room", dialog.Text);
            Assert.Equal(string.Empty, dialog.RoomName);
        });
}
