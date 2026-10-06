#nullable disable

namespace Lantern.UI;

partial class StartupForm
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
        lblNickname = new Label();
        txtNickname = new TextBox();
        lblHostHint = new Label();
        btnStartServer = new Button();
        lblServerList = new Label();
        lstServers = new ListBox();
        btnScan = new Button();
        btnJoin = new Button();
        statusStrip = new StatusStrip();
        lblStatus = new ToolStripStatusLabel();
        toolTip = new ToolTip(components);
        statusStrip.SuspendLayout();
        SuspendLayout();
        //
        // lblNickname
        //
        lblNickname.AutoSize = true;
        lblNickname.Location = new Point(12, 18);
        lblNickname.Name = "lblNickname";
        lblNickname.Size = new Size(63, 15);
        lblNickname.TabIndex = 0;
        lblNickname.Text = "Nickname:";
        //
        // txtNickname
        //
        txtNickname.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtNickname.Location = new Point(104, 15);
        txtNickname.MaxLength = 16;
        txtNickname.Name = "txtNickname";
        txtNickname.Size = new Size(356, 23);
        txtNickname.TabIndex = 1;
        //
        // lblHostHint
        //
        lblHostHint.AutoSize = true;
        lblHostHint.ForeColor = SystemColors.GrayText;
        lblHostHint.Location = new Point(104, 44);
        lblHostHint.Name = "lblHostHint";
        lblHostHint.Size = new Size(220, 15);
        lblHostHint.TabIndex = 2;
        lblHostHint.Text = "Host a chat: this machine becomes the server";
        //
        // btnStartServer
        //
        btnStartServer.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnStartServer.Location = new Point(476, 39);
        btnStartServer.Name = "btnStartServer";
        btnStartServer.Size = new Size(132, 26);
        btnStartServer.TabIndex = 3;
        btnStartServer.Text = "Start a Server";
        toolTip.SetToolTip(btnStartServer, "Start a Bluetooth chat server on this machine");
        btnStartServer.UseVisualStyleBackColor = true;
        btnStartServer.Click += BtnStartServer_Click;
        //
        // lblServerList
        //
        lblServerList.AutoSize = true;
        lblServerList.Location = new Point(12, 82);
        lblServerList.Name = "lblServerList";
        lblServerList.Size = new Size(70, 15);
        lblServerList.TabIndex = 4;
        lblServerList.Text = "Server list:";
        //
        // lstServers
        //
        lstServers.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lstServers.FormattingEnabled = true;
        lstServers.ItemHeight = 15;
        lstServers.Location = new Point(12, 102);
        lstServers.Name = "lstServers";
        lstServers.Size = new Size(448, 274);
        lstServers.TabIndex = 5;
        lstServers.SelectedIndexChanged += LstServers_SelectedIndexChanged;
        //
        // btnScan
        //
        btnScan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnScan.Location = new Point(476, 102);
        btnScan.Name = "btnScan";
        btnScan.Size = new Size(132, 26);
        btnScan.TabIndex = 6;
        btnScan.Text = "Scan";
        toolTip.SetToolTip(btnScan, "Scan for nearby chat servers");
        btnScan.UseVisualStyleBackColor = true;
        btnScan.Click += BtnScan_Click;
        //
        // btnJoin
        //
        btnJoin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnJoin.Enabled = false;
        btnJoin.Location = new Point(476, 134);
        btnJoin.Name = "btnJoin";
        btnJoin.Size = new Size(132, 26);
        btnJoin.TabIndex = 7;
        btnJoin.Text = "Join";
        toolTip.SetToolTip(btnJoin, "Join the selected server");
        btnJoin.UseVisualStyleBackColor = true;
        btnJoin.Click += BtnJoin_Click;
        //
        // statusStrip
        //
        statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus });
        statusStrip.Location = new Point(0, 419);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(620, 22);
        statusStrip.TabIndex = 8;
        //
        // lblStatus
        //
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(72, 17);
        lblStatus.Text = "Status: idle";
        //
        // StartupForm
        //
        AcceptButton = btnStartServer;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(620, 441);
        Controls.Add(lblNickname);
        Controls.Add(txtNickname);
        Controls.Add(lblHostHint);
        Controls.Add(btnStartServer);
        Controls.Add(lblServerList);
        Controls.Add(lstServers);
        Controls.Add(btnScan);
        Controls.Add(btnJoin);
        Controls.Add(statusStrip);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimumSize = new Size(520, 380);
        Name = "StartupForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Lantern";
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblNickname;
    private TextBox txtNickname;
    private Label lblHostHint;
    private Button btnStartServer;
    private Label lblServerList;
    private ListBox lstServers;
    private Button btnScan;
    private Button btnJoin;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel lblStatus;
    private ToolTip toolTip;
}
