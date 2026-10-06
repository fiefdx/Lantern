#nullable disable

namespace Lantern.UI;

partial class ClientForm
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
        components = new System.ComponentModel.Container();
        btnLeave = new Button();
        toolTip = new ToolTip(components);
        pnlRoomActions.SuspendLayout();
        SuspendLayout();
        //
        // btnLeave
        //
        btnLeave.AutoSize = true;
        btnLeave.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnLeave.Location = new Point(3, 3);
        btnLeave.MinimumSize = new Size(34, 0);
        btnLeave.Name = "btnLeave";
        btnLeave.Size = new Size(52, 26);
        btnLeave.TabIndex = 0;
        btnLeave.Text = "Leave";
        toolTip.SetToolTip(btnLeave, "Leave the current room");
        btnLeave.UseVisualStyleBackColor = true;
        btnLeave.Click += OnLeaveClick;
        //
        // ClientForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(984, 569);
        MinimumSize = new Size(760, 460);
        Name = "ClientForm";
        Text = "Lantern - client";
        pnlRoomActions.ResumeLayout(false);
        pnlRoomActions.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Button btnLeave;
    private ToolTip toolTip;
}
