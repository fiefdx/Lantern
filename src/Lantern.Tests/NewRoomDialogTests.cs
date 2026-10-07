using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Tests for the naming dialog of Figure 5 — it must not accept an unusable name.
/// </summary>
public class NewRoomDialogTests
{
    [Fact]
    public void OkDisabledForEmptyName()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog();
            Assert.False(Ok(dialog).Enabled);

            dialog.RoomNameText = "  ";
            Assert.False(Ok(dialog).Enabled);

            dialog.RoomNameText = "Study group";
            Assert.True(Ok(dialog).Enabled);
            Assert.Equal(string.Empty, Error(dialog).Text);
        });
    }

    [Fact]
    public void OkDisabledForTooLongName()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog();
            dialog.RoomNameText = new string('x', ValidationRules.RoomNameMaxLength + 1);
            Assert.False(Ok(dialog).Enabled);
            Assert.Contains("characters", Error(dialog).Text);
        });
    }

    [Fact]
    public void ExtraValidationReportsDuplicates()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog(
                initial: "x",
                extraValidation: name =>
                    name.Equals("Study group", StringComparison.OrdinalIgnoreCase)
                        ? "A room with that name already exists."
                        : null);

            dialog.RoomNameText = "study GROUP";
            Assert.False(Ok(dialog).Enabled);
            Assert.Equal("A room with that name already exists.", Error(dialog).Text);

            dialog.RoomNameText = "Lab";
            Assert.True(Ok(dialog).Enabled);
        });
    }

    [Fact]
    public void InitialValueIsPreFilledForRename()
    {
        Sta.Run(() =>
        {
            using var dialog = new NewRoomDialog("Rename Room", "Study group");
            Assert.Equal("Rename Room", dialog.Text);
            Assert.Equal("Study group", dialog.RoomNameText);
            Assert.True(Ok(dialog).Enabled);
        });
    }

    private static Button Ok(Form form) => (Button)form.Controls.Find("btnOk", searchAllChildren: true)[0];

    private static Label Error(Form form) => (Label)form.Controls.Find("lblError", searchAllChildren: true)[0];
}
