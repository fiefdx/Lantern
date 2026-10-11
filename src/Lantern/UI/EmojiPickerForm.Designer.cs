#nullable disable
namespace Lantern.UI;

partial class EmojiPickerForm
{
    private System.ComponentModel.IContainer components;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        grid = new TableLayoutPanel();
        btnClose = new Button();
        toolTip = new System.Windows.Forms.ToolTip(components);
        SuspendLayout();
        //
        // grid
        //
        grid.ColumnCount = 8;
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39F));
        grid.Location = new Point(6, 6);
        grid.Name = "grid";
        grid.RowCount = 6;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        grid.Size = new Size(312, 216);
        grid.TabIndex = 0;
        //
        // btnClose
        //
        btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        btnClose.DialogResult = DialogResult.Cancel;
        btnClose.Location = new Point(6, 228);
        btnClose.Name = "btnClose";
        btnClose.Size = new Size(312, 26);
        btnClose.TabIndex = 1;
        btnClose.Text = "Close";
        toolTip.SetToolTip(btnClose, "Close the table without changing the message (Escape)");
        btnClose.UseVisualStyleBackColor = true;
        //
        // EmojiPickerForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = btnClose;
        ClientSize = new Size(324, 260);
        Controls.Add(grid);
        Controls.Add(btnClose);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        KeyPreview = true;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "EmojiPickerForm";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Choose an emoji";
        ResumeLayout(false);
    }

    protected TableLayoutPanel grid;
    protected Button btnClose;
    private System.Windows.Forms.ToolTip toolTip;
}
