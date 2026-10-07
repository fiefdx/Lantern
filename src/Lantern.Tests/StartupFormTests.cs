using Lantern.Core;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Designer smoke tests for the startup window (Figure 2). These only prove the
/// generated layout constructs; behaviour is covered by <c>ValidationRulesTests</c>.
/// </summary>
public class StartupFormTests
{
    [Fact]
    public void FormConstructsAndShowsPlaceholders()
    {
        Sta.Run(() =>
        {
            using var form = new StartupForm();
            Assert.Equal("Lantern", form.Text);
            Assert.NotEmpty(form.Nickname);
            Assert.True(form.Controls.Count > 0);
        });
    }

    [Fact]
    public void JoinIsDisabledUntilAServerIsSelected()
    {
        Sta.Run(() =>
        {
            using var form = new StartupForm();
            var join = Assert.IsType<Button>(Find(form, "btnJoin"));
            var list = Assert.IsType<ListBox>(Find(form, "lstServers"));

            // Nothing is invented for the list: it fills from a scan (T2.6), so it starts empty and
            // there is nothing to join.
            Assert.Empty(list.Items);
            Assert.False(join.Enabled);

            list.Items.Add(new ServerEndpoint("Kitchen Table", "127.0.0.1:43797", Guid.Empty));
            list.SelectedIndex = 0;
            Assert.True(join.Enabled);
        });
    }

    private static Control? Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault();
}
