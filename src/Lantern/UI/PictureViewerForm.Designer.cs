#nullable disable

namespace Lantern.UI;

partial class PictureViewerForm
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
        toolStrip = new ToolStrip();
        btnZoomOut = new ToolStripButton();
        btnZoomIn = new ToolStripButton();
        btnFit = new ToolStripButton();
        btnActual = new ToolStripButton();
        lblZoom = new ToolStripLabel();
        btnSave = new ToolStripButton();
        pnlImage = new Panel();
        picImage = new PictureBox();
        statusStrip = new StatusStrip();
        lblStatus = new ToolStripStatusLabel();
        toolStrip.SuspendLayout();
        pnlImage.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)picImage).BeginInit();
        statusStrip.SuspendLayout();
        SuspendLayout();
        //
        // toolStrip
        //
        // The scales are buttons rather than a slider: a slider's value is where the mouse left it, and the
        // question anybody asks about a photo is "at what size am I looking at this", which is a number.
        toolStrip.Dock = DockStyle.Top;
        toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        toolStrip.ImageScalingSize = new Size(20, 20);
        toolStrip.Items.AddRange(new ToolStripItem[] { btnZoomOut, btnZoomIn, btnFit, btnActual, lblZoom, btnSave });
        toolStrip.Location = new Point(0, 0);
        toolStrip.Name = "toolStrip";
        toolStrip.RenderMode = ToolStripRenderMode.System;
        toolStrip.Size = new Size(904, 25);
        toolStrip.TabIndex = 0;
        //
        // btnZoomOut
        //
        btnZoomOut.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnZoomOut.Name = "btnZoomOut";
        btnZoomOut.Size = new Size(36, 22);
        btnZoomOut.Text = "−";
        // ToolTipText, not a ToolTip: a toolstrip item is not a Control, so there is no control to hang one on.
        btnZoomOut.ToolTipText = "Smaller (minus, or wheel down)";
        btnZoomOut.Click += ZoomOutButton_Click;
        //
        // btnZoomIn
        //
        btnZoomIn.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnZoomIn.Name = "btnZoomIn";
        btnZoomIn.Size = new Size(36, 22);
        btnZoomIn.Text = "+";
        btnZoomIn.ToolTipText = "Bigger (plus, or wheel up)";
        btnZoomIn.Click += ZoomInButton_Click;
        //
        // btnFit
        //
        btnFit.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnFit.Name = "btnFit";
        btnFit.Size = new Size(52, 22);
        btnFit.Text = "Fit";
        btnFit.ToolTipText = "As big as the window allows, without enlarging the photo (F)";
        btnFit.Click += FitButton_Click;
        //
        // btnActual
        //
        btnActual.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnActual.Name = "btnActual";
        btnActual.Size = new Size(60, 22);
        btnActual.Text = "100%";
        btnActual.ToolTipText = "The photo's own pixels (0, or double-click the picture)";
        btnActual.Click += ActualButton_Click;
        //
        // lblZoom
        //
        lblZoom.Name = "lblZoom";
        lblZoom.Size = new Size(44, 22);
        lblZoom.Text = "100%";
        lblZoom.TextAlign = ContentAlignment.MiddleRight;
        //
        // btnSave
        //
        btnSave.Alignment = ToolStripItemAlignment.Right;
        btnSave.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnSave.Name = "btnSave";
        btnSave.Size = new Size(72, 22);
        btnSave.Text = "Save to…";
        // Looking at a picture does not put a file on this machine; this button is the only way one appears.
        btnSave.ToolTipText = "Write these bytes somewhere you choose (Ctrl+S)";
        btnSave.Click += SaveButton_Click;
        //
        // pnlImage
        //
        // AutoScroll, because a photo bigger than the window is the normal state once somebody starts zooming,
        // and it must stay one photo rather than becoming a cropped one.
        pnlImage.AutoScroll = true;
        pnlImage.BackColor = Color.FromArgb(45, 45, 48);
        pnlImage.Controls.Add(picImage);
        pnlImage.Dock = DockStyle.Fill;
        pnlImage.Location = new Point(0, 25);
        pnlImage.Name = "pnlImage";
        pnlImage.Size = new Size(904, 610);
        pnlImage.TabIndex = 1;
        pnlImage.MouseWheel += Viewer_MouseWheel;
        pnlImage.Resize += ImagePanel_Resized;
        //
        // picImage
        //
        // StretchImage, and the control is resized instead of the bitmap: a 4000 × 3000 photo at 800% would
        // otherwise be a gigabyte of memory spent on one eyelash, and the wheel would stop being usable.
        picImage.Location = new Point(0, 0);
        picImage.Name = "picImage";
        picImage.Size = new Size(200, 150);
        picImage.SizeMode = PictureBoxSizeMode.StretchImage;
        picImage.TabIndex = 0;
        picImage.TabStop = false;
        picImage.MouseDoubleClick += Picture_MouseDoubleClick;
        picImage.MouseWheel += Viewer_MouseWheel;
        //
        // statusStrip
        //
        statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus });
        statusStrip.Location = new Point(0, 635);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(904, 24);
        statusStrip.TabIndex = 2;
        //
        // lblStatus
        //
        // Who sent it and when, and how big it is: the questions asked the moment a photo is opened, answered
        // without going back to the transcript and looking up the line it came from.
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(44, 19);
        lblStatus.Text = "picture";
        //
        // PictureViewerForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(904, 659);
        Controls.Add(pnlImage);
        Controls.Add(toolStrip);
        Controls.Add(statusStrip);
        KeyPreview = true;
        MinimumSize = new Size(360, 260);
        Name = "PictureViewerForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Lantern";
        KeyDown += Viewer_KeyDown;
        MouseWheel += Viewer_MouseWheel;
        toolStrip.ResumeLayout(false);
        toolStrip.PerformLayout();
        pnlImage.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)picImage).EndInit();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private ToolStrip toolStrip;
    private ToolStripButton btnZoomOut;
    private ToolStripButton btnZoomIn;
    private ToolStripButton btnFit;
    private ToolStripButton btnActual;
    private ToolStripLabel lblZoom;
    private ToolStripButton btnSave;
    private Panel pnlImage;
    private PictureBox picImage;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel lblStatus;
}
