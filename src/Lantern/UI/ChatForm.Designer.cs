#nullable disable

namespace Lantern.UI;

partial class ChatForm
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
        menuStrip = new MenuStrip();
        fileMenu = new ToolStripMenuItem();
        quitMenuItem = new ToolStripMenuItem();
        editMenu = new ToolStripMenuItem();
        renameMenuItem = new ToolStripMenuItem();
        viewMenu = new ToolStripMenuItem();
        clearChatMenuItem = new ToolStripMenuItem();
        helpMenu = new ToolStripMenuItem();
        aboutMenuItem = new ToolStripMenuItem();
        tableLayout = new TableLayoutPanel();
        pnlRoomHeader = new Panel();
        lblRoomsHeader = new Label();
        pnlRoomActions = new FlowLayoutPanel();
        lstRooms = new ListBox();
        rtbChat = new RichTextBox();
        pnlMembersHeader = new Panel();
        lblMembersHeader = new Label();
        lstMembers = new ListBox();
        btnShare = new Button();
        txtMessage = new TextBox();
        btnSend = new Button();
        statusStrip = new StatusStrip();
        lblStatus = new ToolStripStatusLabel();
        toolTip = new ToolTip(components);
        menuStrip.SuspendLayout();
        tableLayout.SuspendLayout();
        pnlRoomHeader.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();
        //
        // menuStrip
        //
        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, viewMenu, helpMenu });
        menuStrip.Location = new Point(0, 0);
        menuStrip.Name = "menuStrip";
        menuStrip.Size = new Size(984, 24);
        menuStrip.TabIndex = 0;
        //
        // fileMenu
        //
        fileMenu.DropDownItems.AddRange(new ToolStripItem[] { quitMenuItem });
        fileMenu.Name = "fileMenu";
        fileMenu.Text = "&File";
        //
        // quitMenuItem
        //
        quitMenuItem.Name = "quitMenuItem";
        quitMenuItem.Text = "&Quit";
        quitMenuItem.Click += QuitMenuItem_Click;
        //
        // editMenu
        //
        editMenu.DropDownItems.AddRange(new ToolStripItem[] { renameMenuItem });
        editMenu.Name = "editMenu";
        editMenu.Text = "&Edit";
        //
        // renameMenuItem
        //
        renameMenuItem.Name = "renameMenuItem";
        renameMenuItem.Text = "&Rename room";
        renameMenuItem.Click += RenameMenuItem_Click;
        //
        // viewMenu
        //
        viewMenu.DropDownItems.AddRange(new ToolStripItem[] { clearChatMenuItem });
        viewMenu.Name = "viewMenu";
        viewMenu.Text = "&View";
        //
        // clearChatMenuItem
        //
        clearChatMenuItem.Name = "clearChatMenuItem";
        clearChatMenuItem.Text = "&Clear chat pane";
        clearChatMenuItem.Click += ClearChatMenuItem_Click;
        //
        // helpMenu
        //
        helpMenu.DropDownItems.AddRange(new ToolStripItem[] { aboutMenuItem });
        helpMenu.Name = "helpMenu";
        helpMenu.Text = "&Help";
        //
        // aboutMenuItem
        //
        aboutMenuItem.Name = "aboutMenuItem";
        aboutMenuItem.Text = "&About";
        aboutMenuItem.Click += AboutMenuItem_Click;
        //
        // tableLayout
        //
        tableLayout.ColumnCount = 3;
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        tableLayout.Controls.Add(pnlRoomHeader, 0, 0);
        tableLayout.Controls.Add(lstRooms, 0, 1);
        tableLayout.Controls.Add(btnShare, 0, 2);
        tableLayout.Controls.Add(rtbChat, 1, 1);
        tableLayout.Controls.Add(txtMessage, 1, 2);
        tableLayout.Controls.Add(pnlMembersHeader, 2, 0);
        tableLayout.Controls.Add(lstMembers, 2, 1);
        tableLayout.Controls.Add(btnSend, 2, 2);
        tableLayout.Dock = DockStyle.Fill;
        tableLayout.Location = new Point(0, 24);
        tableLayout.Name = "tableLayout";
        tableLayout.Padding = new Padding(6, 4, 6, 4);
        tableLayout.RowCount = 3;
        tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        tableLayout.Size = new Size(984, 521);
        tableLayout.TabIndex = 1;
        //
        // pnlRoomHeader
        //
        pnlRoomHeader.Controls.Add(lblRoomsHeader);
        pnlRoomHeader.Controls.Add(pnlRoomActions);
        pnlRoomHeader.Dock = DockStyle.Fill;
        pnlRoomHeader.Location = new Point(6, 4);
        pnlRoomHeader.Name = "pnlRoomHeader";
        pnlRoomHeader.Size = new Size(178, 28);
        pnlRoomHeader.TabIndex = 0;
        //
        // lblRoomsHeader
        //
        lblRoomsHeader.Dock = DockStyle.Fill;
        lblRoomsHeader.Location = new Point(0, 0);
        lblRoomsHeader.Name = "lblRoomsHeader";
        lblRoomsHeader.Size = new Size(178, 28);
        lblRoomsHeader.TabIndex = 0;
        lblRoomsHeader.Text = "Rooms";
        lblRoomsHeader.TextAlign = ContentAlignment.MiddleLeft;
        //
        // pnlRoomActions
        //
        pnlRoomActions.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        pnlRoomActions.AutoSize = true;
        pnlRoomActions.FlowDirection = FlowDirection.LeftToRight;
        pnlRoomActions.Location = new Point(178, 0);
        pnlRoomActions.Name = "pnlRoomActions";
        pnlRoomActions.Size = new Size(0, 0);
        pnlRoomActions.TabIndex = 1;
        pnlRoomActions.WrapContents = false;
        //
        // lstRooms
        //
        lstRooms.Dock = DockStyle.Fill;
        lstRooms.FormattingEnabled = true;
        lstRooms.IntegralHeight = false;
        lstRooms.ItemHeight = 15;
        lstRooms.Location = new Point(6, 38);
        lstRooms.Name = "lstRooms";
        lstRooms.Size = new Size(178, 413);
        lstRooms.TabIndex = 1;
        lstRooms.SelectedIndexChanged += LstRooms_SelectedIndexChanged;
        //
        // rtbChat
        //
        rtbChat.BorderStyle = BorderStyle.None;
        rtbChat.Dock = DockStyle.Fill;
        rtbChat.Font = new Font("Segoe UI", 9.75F);
        rtbChat.Location = new Point(190, 38);
        rtbChat.Name = "rtbChat";
        rtbChat.ReadOnly = true;
        rtbChat.Size = new Size(618, 413);
        rtbChat.TabIndex = 2;
        rtbChat.TabStop = false;
        rtbChat.Text = "";
        //
        // pnlMembersHeader
        //
        pnlMembersHeader.Controls.Add(lblMembersHeader);
        pnlMembersHeader.Dock = DockStyle.Fill;
        pnlMembersHeader.Location = new Point(814, 4);
        pnlMembersHeader.Name = "pnlMembersHeader";
        pnlMembersHeader.Size = new Size(164, 28);
        pnlMembersHeader.TabIndex = 3;
        //
        // lblMembersHeader
        //
        lblMembersHeader.Dock = DockStyle.Fill;
        lblMembersHeader.Location = new Point(0, 0);
        lblMembersHeader.Name = "lblMembersHeader";
        lblMembersHeader.Size = new Size(164, 28);
        lblMembersHeader.TabIndex = 0;
        lblMembersHeader.Text = "Members";
        lblMembersHeader.TextAlign = ContentAlignment.MiddleLeft;
        //
        // lstMembers
        //
        lstMembers.Dock = DockStyle.Fill;
        lstMembers.FormattingEnabled = true;
        lstMembers.IntegralHeight = false;
        lstMembers.ItemHeight = 15;
        lstMembers.Location = new Point(814, 38);
        lstMembers.Name = "lstMembers";
        lstMembers.Size = new Size(164, 413);
        lstMembers.TabIndex = 4;
        //
        // btnShare
        //
        btnShare.Dock = DockStyle.Fill;
        btnShare.Location = new Point(6, 457);
        btnShare.Name = "btnShare";
        btnShare.Size = new Size(178, 28);
        btnShare.TabIndex = 5;
        btnShare.Text = "Share";
        toolTip.SetToolTip(btnShare, "Attach a picture or send a file to the room");
        btnShare.UseVisualStyleBackColor = true;
        btnShare.Click += BtnShare_Click;
        //
        // txtMessage
        //
        txtMessage.Dock = DockStyle.Fill;
        txtMessage.Location = new Point(190, 457);
        txtMessage.Multiline = true;
        txtMessage.Name = "txtMessage";
        txtMessage.PlaceholderText = "Message";
        txtMessage.Size = new Size(618, 28);
        txtMessage.TabIndex = 6;
        txtMessage.KeyDown += TxtMessage_KeyDown;
        //
        // btnSend
        //
        btnSend.Dock = DockStyle.Fill;
        btnSend.Location = new Point(814, 457);
        btnSend.Name = "btnSend";
        btnSend.Size = new Size(164, 28);
        btnSend.TabIndex = 7;
        btnSend.Text = "Send";
        toolTip.SetToolTip(btnSend, "Send the message to the current room (Enter)");
        btnSend.UseVisualStyleBackColor = true;
        btnSend.Click += BtnSend_Click;
        //
        // statusStrip
        //
        statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus });
        statusStrip.Location = new Point(0, 545);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(984, 24);
        statusStrip.TabIndex = 2;
        //
        // lblStatus
        //
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(72, 19);
        lblStatus.Text = "Status: idle";
        //
        // ChatForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(984, 569);
        Controls.Add(tableLayout);
        Controls.Add(statusStrip);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;
        MinimumSize = new Size(760, 460);
        Name = "ChatForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Lantern";
        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        tableLayout.ResumeLayout(false);
        pnlRoomHeader.ResumeLayout(false);
        pnlRoomHeader.PerformLayout();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private MenuStrip menuStrip;
    private ToolStripMenuItem fileMenu;
    private ToolStripMenuItem quitMenuItem;
    private ToolStripMenuItem editMenu;
    private ToolStripMenuItem renameMenuItem;
    private ToolStripMenuItem viewMenu;
    private ToolStripMenuItem clearChatMenuItem;
    private ToolStripMenuItem helpMenu;
    private ToolStripMenuItem aboutMenuItem;
    private TableLayoutPanel tableLayout;
    protected Panel pnlRoomHeader;
    private Label lblRoomsHeader;
    protected FlowLayoutPanel pnlRoomActions;
    protected ListBox lstRooms;
    protected RichTextBox rtbChat;
    private Panel pnlMembersHeader;
    protected Label lblMembersHeader;
    protected ListBox lstMembers;
    protected Button btnShare;
    protected TextBox txtMessage;
    protected Button btnSend;
    private StatusStrip statusStrip;
    protected ToolStripStatusLabel lblStatus;
    private ToolTip toolTip;
}
