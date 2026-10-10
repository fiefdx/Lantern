#nullable disable

namespace Lantern.UI;

partial class HostForm
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
        btnNew = new Button();
        btnDelete = new Button();
        toolTip = new ToolTip(components);
        pnlRoomActions.SuspendLayout();
        SuspendLayout();
        //
        // btnNew
        //
        btnNew.AutoSize = true;
        btnNew.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnNew.Location = new Point(3, 3);
        btnNew.MinimumSize = new Size(34, 0);
        btnNew.Name = "btnNew";
        btnNew.Size = new Size(34, 26);
        btnNew.TabIndex = 0;
        btnNew.Text = "New";
        toolTip.SetToolTip(btnNew, "Create a room (Ctrl+N)");
        btnNew.UseVisualStyleBackColor = true;
        btnNew.Click += OnNewRoomClick;
        //
        // btnDelete
        //
        btnDelete.AutoSize = true;
        btnDelete.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnDelete.Location = new Point(43, 3);
        btnDelete.MinimumSize = new Size(34, 0);
        btnDelete.Name = "btnDelete";
        btnDelete.Size = new Size(52, 26);
        btnDelete.TabIndex = 1;
        btnDelete.Text = "Delete";
        toolTip.SetToolTip(btnDelete, "Delete the highlighted room (Ctrl+D)");
        btnDelete.UseVisualStyleBackColor = true;
        btnDelete.Click += OnDeleteRoomClick;
        //
        // HostForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(984, 569);
        MinimumSize = new Size(760, 460);
        Name = "HostForm";
        Text = "Lantern - host";
        pnlRoomActions.ResumeLayout(false);
        pnlRoomActions.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Button btnNew;
    private Button btnDelete;
    private ToolTip toolTip;
}
