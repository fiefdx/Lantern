using System.Reflection;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Smoke tests for the shared chat window and its two role flavours (Figures 3 and 4).
/// </summary>
public class ChatFormTests
{
    [Fact]
    public void HostWindowIsTitledHostAndCanRename()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm("Haitao");
            Assert.Equal("Lantern - host", form.Text);
            Assert.Equal("Haitao", form.Nickname);
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
            using var form = new Lantern.UI.ClientForm("Victory");
            Assert.Equal("Lantern - client", form.Text);
            Assert.NotNull(Find(form, "btnLeave"));
            Assert.Null(Find(form, "btnNew"));
            Assert.Null(Find(form, "btnDelete"));
            Assert.False(Menu(form, "renameMenuItem")!.Enabled);
        });
    }

    [Fact]
    public void SendRequiresARoomAndText()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm("Haitao");
            var send = (Button)Find(form, "btnSend")!;
            var list = (ListBox)Find(form, "lstRooms")!;
            var box = (TextBox)Find(form, "txtMessage")!;

            list.SelectedIndex = 0;
            Assert.False(send.Enabled);

            box.Text = "hello";
            Assert.True(send.Enabled);

            list.SelectedIndex = -1;
            Assert.False(send.Enabled);
        });
    }

    [Fact]
    public void BusyFlagDisablesSendAndShare()
    {
        Sta.Run(() =>
        {
            using var form = new Lantern.UI.HostForm("Haitao");
            var send = (Button)Find(form, "btnSend")!;
            var share = (Button)Find(form, "btnShare")!;
            var list = (ListBox)Find(form, "lstRooms")!;
            var box = (TextBox)Find(form, "txtMessage")!;

            list.SelectedIndex = 0;
            box.Text = "hello";
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
            using var form = new Lantern.UI.HostForm("Haitao");
            Assert.Equal("Rooms", ((Label)Find(form, "lblRoomsHeader")!).Text);

            var rooms = (ListBox)Find(form, "lstRooms")!;
            Assert.All(rooms.Items.Cast<object>(), item =>
                Assert.StartsWith("Room", item.ToString()));
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
