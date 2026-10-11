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
        newRoomMenuItem = new ToolStripMenuItem();
        renameMenuItem = new ToolStripMenuItem();
        deleteRoomMenuItem = new ToolStripMenuItem();
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
        btnEmoji = new Button();
        txtMessage = new TextBox();
        btnSend = new Button();
        statusStrip = new StatusStrip();
        lblStatus = new ToolStripStatusLabel();
        lblFileProgress = new ToolStripStatusLabel();
        progressFile = new ToolStripProgressBar();
        btnFileStop = new ToolStripButton();
        ctxTranscript = new ContextMenuStrip();
        savePayloadMenuItem = new ToolStripMenuItem();
        pnlFileNotice = new Panel();
        lblFileNotice = new Label();
        pnlFileActions = new FlowLayoutPanel();
        btnFileSave = new Button();
        btnFileDecline = new Button();
        timerProgress = new System.Windows.Forms.Timer(components);
        toolTip = new ToolTip(components);
        menuStrip.SuspendLayout();
        tableLayout.SuspendLayout();
        pnlRoomHeader.SuspendLayout();
        statusStrip.SuspendLayout();
        ctxTranscript.SuspendLayout();
        pnlFileNotice.SuspendLayout();
        pnlFileActions.SuspendLayout();
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
        // The room a person is standing in is the room they want to make and unmake, so room management is on
        // the window that shows the rooms and not only on two small buttons in their header (F5). The same three
        // things - make one, name one, destroy one - in the order a person does them.
        editMenu.DropDownItems.AddRange(new ToolStripItem[] { newRoomMenuItem, renameMenuItem, deleteRoomMenuItem });
        editMenu.Name = "editMenu";
        editMenu.Text = "&Edit";
        //
        // newRoomMenuItem
        //
        // Ctrl+N, and deliberately no Del: a shortcut on a menu item takes the key away from whatever the person
        // is typing into, and the message box is directly next to this menu.
        newRoomMenuItem.Name = "newRoomMenuItem";
        newRoomMenuItem.ShortcutKeys = Keys.Control | Keys.N;
        newRoomMenuItem.Text = "&New room";
        newRoomMenuItem.Click += NewRoomMenuItem_Click;
        //
        // renameMenuItem
        //
        renameMenuItem.Name = "renameMenuItem";
        renameMenuItem.Text = "&Rename room";
        renameMenuItem.Click += RenameMenuItem_Click;
        //
        // deleteRoomMenuItem
        //
        deleteRoomMenuItem.Name = "deleteRoomMenuItem";
        deleteRoomMenuItem.ShortcutKeys = Keys.Control | Keys.D;
        deleteRoomMenuItem.Text = "&Delete room";
        deleteRoomMenuItem.Click += DeleteRoomMenuItem_Click;
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
        tableLayout.ColumnCount = 4;
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        tableLayout.Controls.Add(pnlRoomHeader, 0, 0);
        tableLayout.Controls.Add(lstRooms, 0, 1);
        tableLayout.Controls.Add(btnShare, 0, 2);
        tableLayout.Controls.Add(rtbChat, 1, 1);
        tableLayout.Controls.Add(txtMessage, 1, 2);
        tableLayout.Controls.Add(pnlMembersHeader, 3, 0);
        tableLayout.Controls.Add(lstMembers, 3, 1);
        tableLayout.Controls.Add(btnEmoji, 2, 2);
        tableLayout.Controls.Add(btnSend, 3, 2);
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
        rtbChat.BackColor = Color.White;
        rtbChat.BorderStyle = BorderStyle.None;
        rtbChat.Dock = DockStyle.Fill;
        rtbChat.Font = new Font("Segoe UI", 9.75F);
        rtbChat.ForeColor = Color.Black;
        rtbChat.Location = new Point(190, 38);
        rtbChat.Name = "rtbChat";
        rtbChat.ReadOnly = true;
        rtbChat.Size = new Size(618, 413);
        rtbChat.TabIndex = 2;
        rtbChat.TabStop = false;
        rtbChat.MouseDoubleClick += RtbChat_MouseDoubleClick;
        rtbChat.ContextMenuStrip = ctxTranscript;
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
        // btnEmoji
        //
        btnEmoji.Dock = DockStyle.Fill;
        btnEmoji.Location = new Point(814, 457);
        btnEmoji.Name = "btnEmoji";
        btnEmoji.Size = new Size(82, 28);
        btnEmoji.TabIndex = 7;
        btnEmoji.Text = "Emoji";
        btnEmoji.TextImageRelation = TextImageRelation.ImageBeforeText;
        toolTip.SetToolTip(btnEmoji, "Choose an emoji from a table and put it in the message");
        btnEmoji.UseVisualStyleBackColor = true;
        btnEmoji.Click += BtnEmoji_Click;
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
        btnSend.TabIndex = 8;
        btnSend.Text = "Send";
        toolTip.SetToolTip(btnSend, "Send the message to the current room (Enter)");
        btnSend.UseVisualStyleBackColor = true;
        btnSend.Click += BtnSend_Click;
        //
        // statusStrip
        //
        statusStrip.Items.AddRange(new ToolStripItem[] { lblStatus, lblFileProgress, progressFile, btnFileStop });
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
        // lblFileProgress
        //
        // The bar and its label belong to whichever file is moving, so they start invisible: a status bar
        // with an empty progress bar in it looks like something is happening when nothing is.
        lblFileProgress.Name = "lblFileProgress";
        lblFileProgress.Size = new Size(0, 19);
        lblFileProgress.Text = "";
        lblFileProgress.Visible = false;
        //
        // progressFile
        //
        progressFile.Name = "progressFile";
        progressFile.Size = new Size(160, 16);
        progressFile.Visible = false;
        //
        // btnFileStop
        //
        btnFileStop.DisplayStyle = ToolStripItemDisplayStyle.Text;
        btnFileStop.Name = "btnFileStop";
        btnFileStop.Size = new Size(44, 22);
        btnFileStop.Text = "Stop";
        btnFileStop.Visible = false;
        btnFileStop.ToolTipText = "Stop this file";
        btnFileStop.Click += BtnFileStop_Click;
        //
        //
        // ctxTranscript
        //
        // The one thing worth doing to a shared picture or file in the transcript is putting it in a folder of
        // the person's own, so that is the whole menu. Right-clicking anywhere else in the transcript is
        // refused in code, because a menu of one greyed item that does not apply here is noise.
        //
        ctxTranscript.Name = "ctxTranscript";
        ctxTranscript.Size = new Size(190, 26);
        ctxTranscript.Items.AddRange(new ToolStripItem[] { savePayloadMenuItem });
        ctxTranscript.Opening += CtxTranscript_Opening;
        //
        // savePayloadMenuItem
        //
        savePayloadMenuItem.Name = "savePayloadMenuItem";
        savePayloadMenuItem.Size = new Size(189, 22);
        savePayloadMenuItem.Text = "Save to…";
        savePayloadMenuItem.Click += SavePayloadMenuItem_Click;
        //
        // pnlFileNotice
        //
        // The offer lives in the window rather than in a dialog. A modal dialog would stop the person
        // typing to the room while they decide, and with two files arriving at once only one dialog can be
        // up - the second offer would have nowhere to be written down.
        pnlFileNotice.Controls.Add(lblFileNotice);
        pnlFileNotice.Controls.Add(pnlFileActions);
        pnlFileNotice.Dock = DockStyle.Top;
        pnlFileNotice.Location = new Point(0, 24);
        pnlFileNotice.Name = "pnlFileNotice";
        pnlFileNotice.Padding = new Padding(8, 3, 8, 3);
        pnlFileNotice.Size = new Size(984, 40);
        pnlFileNotice.TabIndex = 3;
        pnlFileNotice.Visible = false;
        //
        // lblFileNotice
        //
        lblFileNotice.Dock = DockStyle.Fill;
        lblFileNotice.Location = new Point(8, 3);
        lblFileNotice.Name = "lblFileNotice";
        lblFileNotice.Size = new Size(768, 34);
        lblFileNotice.TabIndex = 0;
        lblFileNotice.Text = "";
        lblFileNotice.TextAlign = ContentAlignment.MiddleLeft;
        //
        // pnlFileActions
        //
        pnlFileActions.Controls.Add(btnFileSave);
        pnlFileActions.Controls.Add(btnFileDecline);
        pnlFileActions.Dock = DockStyle.Right;
        pnlFileActions.FlowDirection = FlowDirection.RightToLeft;
        pnlFileActions.Location = new Point(776, 3);
        pnlFileActions.Name = "pnlFileActions";
        pnlFileActions.Padding = new Padding(4, 0, 0, 0);
        pnlFileActions.Size = new Size(200, 34);
        pnlFileActions.TabIndex = 1;
        pnlFileActions.WrapContents = false;
        //
        // btnFileSave
        //
        btnFileSave.Anchor = AnchorStyles.Top;
        btnFileSave.Location = new Point(4, 4);
        btnFileSave.Name = "btnFileSave";
        btnFileSave.Size = new Size(92, 26);
        btnFileSave.TabIndex = 0;
        btnFileSave.Text = "Save to…";
        toolTip.SetToolTip(btnFileSave, "Choose where to put this file; it keeps arriving either way");
        btnFileSave.UseVisualStyleBackColor = true;
        btnFileSave.Click += BtnFileSave_Click;
        //
        // btnFileDecline
        //
        btnFileDecline.Anchor = AnchorStyles.Top;
        btnFileDecline.Location = new Point(102, 4);
        btnFileDecline.Name = "btnFileDecline";
        btnFileDecline.Size = new Size(92, 26);
        btnFileDecline.TabIndex = 1;
        btnFileDecline.Text = "Decline";
        toolTip.SetToolTip(btnFileDecline, "Tell them no; the bytes already here are thrown away");
        btnFileDecline.UseVisualStyleBackColor = true;
        btnFileDecline.Click += BtnFileDecline_Click;
        //
        // timerProgress
        //
        timerProgress.Interval = 250;
        timerProgress.Tick += TimerProgress_Tick;
        //
        // ChatForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(984, 569);
        Controls.Add(tableLayout);
        Controls.Add(statusStrip);
        Controls.Add(pnlFileNotice);
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
        ctxTranscript.ResumeLayout(false);
        pnlFileNotice.ResumeLayout(false);
        pnlFileActions.ResumeLayout(false);
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
    private ToolStripMenuItem newRoomMenuItem;
    private ToolStripMenuItem renameMenuItem;
    private ToolStripMenuItem deleteRoomMenuItem;
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
    protected Button btnEmoji;
    protected TextBox txtMessage;
    protected Button btnSend;
    private StatusStrip statusStrip;
    protected ToolStripStatusLabel lblStatus;
    private ContextMenuStrip ctxTranscript;
    private ToolStripMenuItem savePayloadMenuItem;
    private ToolStripStatusLabel lblFileProgress;
    private ToolStripProgressBar progressFile;
    private ToolStripButton btnFileStop;
    private Panel pnlFileNotice;
    protected Label lblFileNotice;
    private FlowLayoutPanel pnlFileActions;
    protected Button btnFileSave;
    protected Button btnFileDecline;
    private System.Windows.Forms.Timer timerProgress;
    private ToolTip toolTip;
}
