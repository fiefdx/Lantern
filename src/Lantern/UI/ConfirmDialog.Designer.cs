#nullable disable

namespace Lantern.UI;

partial class ConfirmDialog
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
        lblMessage = new Label();
        btnOk = new Button();
        btnCancel = new Button();
        SuspendLayout();
        //
        // lblMessage
        //
        lblMessage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lblMessage.Location = new Point(14, 14);
        lblMessage.Name = "lblMessage";
        lblMessage.Size = new Size(360, 46);
        lblMessage.TabIndex = 0;
        lblMessage.TextAlign = ContentAlignment.MiddleLeft;
        //
        // btnOk
        //
        btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnOk.DialogResult = DialogResult.OK;
        btnOk.Location = new Point(220, 66);
        btnOk.Name = "btnOk";
        btnOk.Size = new Size(75, 26);
        btnOk.TabIndex = 1;
        btnOk.Text = "OK";
        btnOk.UseVisualStyleBackColor = true;
        //
        // btnCancel
        //
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new Point(301, 66);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(75, 26);
        btnCancel.TabIndex = 2;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;
        //
        // ConfirmDialog
        //
        AcceptButton = btnOk;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnCancel;
        ClientSize = new Size(388, 104);
        Controls.Add(lblMessage);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(320, 120);
        Name = "ConfirmDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Confirm";
        ResumeLayout(false);
    }

    #endregion

    private Label lblMessage;
    private Button btnOk;
    private Button btnCancel;
}
