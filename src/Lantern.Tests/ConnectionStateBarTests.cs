using Lantern.Core;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T6.2: the status bar is the only place this program explains itself, so the state of the connection has to
/// be readable there - not only in a box that appears once and is dismissed. The words are the ones the
/// scenario list and the figure use: "server online" in the host's bar (scenario 1), a connection state in the
/// client's (the startup scenario).
/// </summary>
public sealed class ConnectionStateBarTests
{
    [Fact]
    public void AHostWindowOpensSayingTheServerIsOnline()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);

            form.Show();

            // Scenario 1 ends with these words: "Room names saved from earlier sessions are loaded, and the
            // status bar shows 'server online'." A person who has just clicked Start a server should not have
            // to infer it from a room list that happens to be filled in.
            Assert.Equal("Status: server online", form.StatusText);

            form.Dispose();
        });
    }

    [Fact]
    public void AClientWindowOpensSayingItIsConnected()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Victory", isHost: false);
            using var form = new Lantern.UI.ClientForm(service);

            form.Show();

            // The figure's own word for this bar is "connected", which is also the shortest true thing that
            // can be said about a window that has just been opened by a join that worked.
            Assert.Equal("Status: connected", form.StatusText);

            form.Dispose();
        });
    }

    [Fact]
    public void TheWindowHasTheLastWordAboutItsOwnCondition()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");

            // What a real service does on the way in: it starts, and it says things while starting. Those
            // happen before the window exists, and they are not what a person should be reading when they look
            // up for the first time - the state of the connection is.
            service.SimulateStatus("3 rooms loaded");

            using var form = new Lantern.UI.HostForm(service);
            form.Show();

            Assert.Equal("Status: server online", form.StatusText);

            // Afterwards the bar goes back to being a bar: an answer to what the person just did takes it,
            // because that is what they are looking at then. The state is there to be returned to, not a sign
            // that cannot be talked over.
            ((ListBox)Find(form, "lstRooms")).SelectedIndex = 0;
            Application.DoEvents();
            Assert.Contains("room", form.StatusText, StringComparison.OrdinalIgnoreCase);

            form.Dispose();
        });
    }

    private static Control Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
            ?? throw new InvalidOperationException($"no control named {name}");
}
