using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// The single modal used to name a room (Figure 5, “New Room”). Also reused in
/// pre-filled form for renaming. OK / Cancel only; refuses to close while the name is
/// unusable, so callers always receive a name that passed <see cref="ValidationRules"/>.
/// </summary>
public partial class NewRoomDialog : Form
{
    private readonly Func<string, string?>? extraValidation;

    public NewRoomDialog(string title = "New Room", string initial = "", Func<string, string?>? extraValidation = null)
    {
        this.extraValidation = extraValidation;
        InitializeComponent();

        Text = title;
        btnOk.Text = string.IsNullOrEmpty(initial) ? "OK" : "Save";
        txtRoomName.Text = initial;
        txtRoomName.SelectionStart = txtRoomName.TextLength;
        ValidateInput();
    }

    /// <summary>The trimmed room name the user accepted.</summary>
    public string RoomName { get; private set; } = string.Empty;

    /// <summary>What is typed in the box right now (re-runs validation when set).</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string RoomNameText
    {
        get => txtRoomName.Text;
        set
        {
            txtRoomName.Text = value;
            ValidateInput();
        }
    }

    private void BtnOk_Click(object? sender, EventArgs e) => Accept();

    private void TxtRoomName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            Accept();
        }
    }

    private void Accept()
    {
        if (!ValidateInput())
        {
            return;
        }

        RoomName = ValidationRules.Normalize(txtRoomName.Text);
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool ValidateInput()
    {
        var raw = txtRoomName.Text;
        var error = ValidationRules.RoomNameError(raw) ?? extraValidation?.Invoke(ValidationRules.Normalize(raw));

        lblError.Text = error ?? string.Empty;
        btnOk.Enabled = error is null;
        return error is null;
    }
}
