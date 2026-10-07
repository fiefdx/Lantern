#nullable disable

namespace Lantern.UI;

partial class NewRoomDialog
{
    /// <summary>Required designer variable.</summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>Clean up any resources being used.</summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        lblPrompt = new Label();
        txtRoomName = new TextBox();
        lblError = new Label();
        btnOk = new Button();
        btnCancel = new Button();
        SuspendLayout();
        //
        // lblPrompt
        //
        lblPrompt.AutoSize = true;
        lblPrompt.Location = new Point(14, 16);
        lblPrompt.Name = "lblPrompt";
        lblPrompt.Size = new Size(74, 15);
        lblPrompt.TabIndex = 0;
        lblPrompt.Text = "Room name:";
        //
        // txtRoomName
        //
        txtRoomName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtRoomName.Location = new Point(102, 13);
        txtRoomName.MaxLength = 24;
        txtRoomName.Name = "txtRoomName";
        txtRoomName.Size = new Size(274, 23);
        txtRoomName.TabIndex = 1;
        txtRoomName.KeyDown += TxtRoomName_KeyDown;
        //
        // lblError
        //
        lblError.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblError.ForeColor = Color.Firebrick;
        lblError.Location = new Point(102, 40);
        lblError.Name = "lblError";
        lblError.Size = new Size(274, 18);
        lblError.TabIndex = 2;
        lblError.Text = string.Empty;
        //
        // btnOk
        //
        btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnOk.DialogResult = DialogResult.OK;
        btnOk.Location = new Point(220, 66);
        btnOk.Name = "btnOk";
        btnOk.Size = new Size(75, 26);
        btnOk.TabIndex = 3;
        btnOk.Text = "OK";
        btnOk.UseVisualStyleBackColor = true;
        btnOk.Click += BtnOk_Click;
        //
        // btnCancel
        //
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(301, 66);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(75, 26);
        btnCancel.TabIndex = 4;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;
        //
        // NewRoomDialog
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(388, 104);
        Controls.Add(lblPrompt);
        Controls.Add(txtRoomName);
        Controls.Add(lblError);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "NewRoomDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "New Room";
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblPrompt;
    private TextBox txtRoomName;
    private Label lblError;
    private Button btnOk;
    private Button btnCancel;
}
