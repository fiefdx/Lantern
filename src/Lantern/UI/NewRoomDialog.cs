using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// The single modal used to name a room (Figure 5, “New Room”). Also reused in
/// pre-filled form for renaming. OK / Cancel only; refuses to close while the name is
/// unusable, so callers always receive a name that passed <see cref="ValidationRules"/>.
/// </summary>
/// <remarks>
/// Two things this dialog has to hold on to itself, both of them learned the hard way:
/// <list type="bullet">
/// <item>what the name in the box is worth is decided <b>as it is typed</b> (<see cref="TxtRoomName_TextChanged"/>),
/// because a greyed OK button and a line of red are both claims about the text there <em>now</em>;</item>
/// <item>and whether the dialog closes is decided here, by <see cref="Accept"/>. The OK button carries no
/// <see cref="Button.DialogResult"/> of its own, because a button that does has the framework hand the form that
/// answer once the click handler has run, happy or not - which would let an unusable name out through a guard
/// that had just refused it. Cancel is allowed to carry its own answer: there is nothing about "I take it back"
/// that needs checking first.</item>
/// </list>
/// </remarks>
public partial class NewRoomDialog : Form
{
    private readonly Func<string, string?>? extraValidation;

    /// <summary>
    /// An empty room to name. The designer needs a constructor with no arguments to build the dialog, and this
    /// one is also the plain thing to ask for, so unlike the others it refuses nothing - see
    /// <see cref="DesignTime"/> for why every window here calls <c>InitializeComponent</c> in its own
    /// constructor rather than chaining to a neighbour.
    /// </summary>
    public NewRoomDialog()
    {
        InitializeComponent();

        Present("New Room", string.Empty);
    }

    public NewRoomDialog(string title = "New Room", string initial = "", Func<string, string?>? extraValidation = null)
    {
        this.extraValidation = extraValidation;

        InitializeComponent();

        Present(title, initial);
    }

    private void Present(string title, string initial)
    {
        AppIcon.Apply(this);

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

    /// <summary>
    /// Every change to the name asks the rules again, because the OK button and the error line are both claims
    /// about the text that is in the box <em>now</em>.
    /// </summary>
    /// <remarks>
    /// Without this the dialog decided "Room name is required." once, before any of a name existed, and said it
    /// back at somebody who was typing into it: the button stayed greyed under a perfectly good name and the
    /// reason under it never changed. Nothing else re-runs these rules while a name is being made — Enter goes
    /// through <see cref="Accept"/>, which does validate, so the keyboard could get the room made and the mouse
    /// could not, which is how it stayed hidden for as long as it did.
    /// </remarks>
    private void TxtRoomName_TextChanged(object? sender, EventArgs e) => ValidateInput();

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
