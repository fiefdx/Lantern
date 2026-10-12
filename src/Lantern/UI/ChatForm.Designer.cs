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
        pnlRooms = new Panel();
        pnlChat = new TableLayoutPanel();
        pnlInput = new TableLayoutPanel();
        pnlMembers = new Panel();
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
        txtMessage = new MessageInput();
        btnSend = new Button();
        pnlFormat = new Panel();
        btnBold = new Button();
        btnItalic = new Button();
        btnUnderline = new Button();
        btnSize = new Button();
        btnColor = new Button();
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
        pnlRooms.SuspendLayout();
        pnlChat.SuspendLayout();
        pnlInput.SuspendLayout();
        pnlMembers.SuspendLayout();
        pnlRoomHeader.SuspendLayout();
        statusStrip.SuspendLayout();
        ctxTranscript.SuspendLayout();
        pnlFileNotice.SuspendLayout();
        pnlFileActions.SuspendLayout();
        pnlFormat.SuspendLayout();
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
        // tableLayout - three columns, and each of them owns everything inside it.
        //
        // The rooms on the left, the members on the right, and the chatting area down the middle with the transcript
        // over the input area. This used to be one flat grid of four columns and four rows, which laid the tool strip
        // across the whole window - so its left half sat over the rooms list and its right half over the members
        // list, and a row of tools with nothing under them is a row of tools that looks like it does not work. It
        // also put an empty column between the message box and Send for no reason anybody could give. A pane belongs
        // to exactly one column now, and the splits go the way a person describes the window out loud.
        //
        tableLayout.ColumnCount = 3;
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        tableLayout.Controls.Add(pnlRooms, 0, 0);
        tableLayout.Controls.Add(pnlChat, 1, 0);
        tableLayout.Controls.Add(pnlMembers, 2, 0);
        tableLayout.Dock = DockStyle.Fill;
        tableLayout.Location = new Point(0, 24);
        tableLayout.Name = "tableLayout";
        tableLayout.Padding = new Padding(6, 4, 6, 4);
        tableLayout.RowCount = 1;
        tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tableLayout.Size = new Size(984, 521);
        tableLayout.TabIndex = 1;
        //
        // pnlRooms - the left column: the header with the room management buttons in it, over the list of rooms.
        //
        pnlRooms.Controls.Add(lstRooms);
        pnlRooms.Controls.Add(pnlRoomHeader);
        pnlRooms.Dock = DockStyle.Fill;
        pnlRooms.Location = new Point(9, 7);
        pnlRooms.Name = "pnlRooms";
        pnlRooms.Size = new Size(184, 507);
        pnlRooms.TabIndex = 0;
        //
        // pnlChat - the middle column: what was said, over the means of saying more. The transcript takes whatever
        // room is left over, because it is the only part of this window that grows when the window grows.
        //
        pnlChat.ColumnCount = 1;
        pnlChat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pnlChat.Controls.Add(rtbChat, 0, 0);
        pnlChat.Controls.Add(pnlInput, 0, 1);
        pnlChat.Dock = DockStyle.Fill;
        pnlChat.Location = new Point(196, 7);
        pnlChat.Name = "pnlChat";
        pnlChat.RowCount = 2;
        pnlChat.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        pnlChat.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pnlChat.Size = new Size(606, 507);
        pnlChat.TabIndex = 1;
        //
        // pnlInput - the bottom row of the middle column: the tools, and under them the box with Send beside it. It
        // is a panel of its own rather than more rows of the outer grid precisely so that all of it stays inside the
        // chatting area. It takes the height it needs instead of being told one: both rows in it are fixed, and a
        // fixed total would clip them the moment the display scaling disagreed with the number chosen here.
        //
        pnlInput.AutoSize = true;
        pnlInput.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        pnlInput.ColumnCount = 2;
        pnlInput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pnlInput.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
        pnlInput.Controls.Add(pnlFormat, 0, 0);
        pnlInput.SetColumnSpan(pnlFormat, 2);
        pnlInput.Controls.Add(txtMessage, 0, 1);
        pnlInput.Controls.Add(btnSend, 1, 1);
        pnlInput.Dock = DockStyle.Fill;
        pnlInput.Location = new Point(3, 453);
        pnlInput.Name = "pnlInput";
        pnlInput.RowCount = 2;
        pnlInput.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        pnlInput.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        pnlInput.Size = new Size(600, 90);
        pnlInput.TabIndex = 1;
        //
        // pnlMembers - the right column: the header that counts them, over the list of them.
        //
        pnlMembers.Controls.Add(lstMembers);
        pnlMembers.Controls.Add(pnlMembersHeader);
        pnlMembers.Dock = DockStyle.Fill;
        pnlMembers.Location = new Point(811, 7);
        pnlMembers.Name = "pnlMembers";
        pnlMembers.Size = new Size(164, 507);
        pnlMembers.TabIndex = 2;
        //
        // pnlRoomHeader
        //
        pnlRoomHeader.Controls.Add(lblRoomsHeader);
        pnlRoomHeader.Controls.Add(pnlRoomActions);
        pnlRoomHeader.Dock = DockStyle.Top;
        pnlRoomHeader.Margin = new Padding(0, 0, 0, 3);
        pnlRoomHeader.Location = new Point(6, 4);
        pnlRoomHeader.Name = "pnlRoomHeader";
        pnlRoomHeader.Size = new Size(178, 30);
        pnlRoomHeader.TabIndex = 0;
        //
        // lblRoomsHeader
        //
        lblRoomsHeader.Dock = DockStyle.Fill;
        lblRoomsHeader.Location = new Point(0, 0);
        lblRoomsHeader.Name = "lblRoomsHeader";
        lblRoomsHeader.Size = new Size(178, 30);
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
        pnlMembersHeader.Dock = DockStyle.Top;
        pnlMembersHeader.Margin = new Padding(0, 0, 0, 3);
        pnlMembersHeader.Location = new Point(814, 4);
        pnlMembersHeader.Name = "pnlMembersHeader";
        pnlMembersHeader.Size = new Size(164, 30);
        pnlMembersHeader.TabIndex = 3;
        //
        // lblMembersHeader
        //
        lblMembersHeader.Dock = DockStyle.Fill;
        lblMembersHeader.Location = new Point(0, 0);
        lblMembersHeader.Name = "lblMembersHeader";
        lblMembersHeader.Size = new Size(164, 30);
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
        //
        // pnlFormat - the tools of the message box: the five that change what the words look like, and the two
        // that put something into them. The style tools are at the left, in the order somebody works through them
        // (shape, then size, then colour); the two that insert something are at the right, over the box they insert
        // into. The widths are not decoration: the strip is a row of fixed-width buttons in a space a person can
        // drag smaller, and the two groups used to run into one another at the window's minimum width - which is
        // how a click on Underline opened the emoji table. The parity test measures it at that width.
        //
        pnlFormat.Controls.Add(btnBold);
        pnlFormat.Controls.Add(btnItalic);
        pnlFormat.Controls.Add(btnUnderline);
        pnlFormat.Controls.Add(btnSize);
        pnlFormat.Controls.Add(btnColor);
        pnlFormat.Controls.Add(btnEmoji);
        pnlFormat.Controls.Add(btnShare);
        pnlFormat.Dock = DockStyle.Fill;
        pnlFormat.Location = new Point(3, 3);
        pnlFormat.Name = "pnlFormat";
        pnlFormat.Size = new Size(594, 30);
        pnlFormat.TabIndex = 0;
        //
        // btnBold
        //
        btnBold.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
        btnBold.Location = new Point(0, 2);
        btnBold.Name = "btnBold";
        btnBold.Size = new Size(30, 24);
        btnBold.TabIndex = 0;
        btnBold.Text = "B";
        toolTip.SetToolTip(btnBold, "Bold the chosen words, or type bold from here");
        btnBold.UseVisualStyleBackColor = false;
        btnBold.Click += BtnBold_Click;
        //
        // btnItalic
        //
        btnItalic.Font = new Font("Segoe UI", 9.75F, FontStyle.Italic, GraphicsUnit.Point, 0);
        btnItalic.Location = new Point(32, 2);
        btnItalic.Name = "btnItalic";
        btnItalic.Size = new Size(30, 24);
        btnItalic.TabIndex = 1;
        btnItalic.Text = "I";
        toolTip.SetToolTip(btnItalic, "Italicise the chosen words, or type italics from here");
        btnItalic.UseVisualStyleBackColor = false;
        btnItalic.Click += BtnItalic_Click;
        //
        // btnUnderline
        //
        btnUnderline.Font = new Font("Segoe UI", 9.75F, FontStyle.Underline, GraphicsUnit.Point, 0);
        btnUnderline.Location = new Point(64, 2);
        btnUnderline.Name = "btnUnderline";
        btnUnderline.Size = new Size(30, 24);
        btnUnderline.TabIndex = 2;
        btnUnderline.Text = "U";
        toolTip.SetToolTip(btnUnderline, "Underline the chosen words, or type underlined from here");
        btnUnderline.UseVisualStyleBackColor = false;
        btnUnderline.Click += BtnUnderline_Click;
        //
        // btnSize
        //
        btnSize.Location = new Point(98, 2);
        btnSize.Name = "btnSize";
        btnSize.Size = new Size(40, 24);
        btnSize.TabIndex = 3;
        btnSize.Text = "Aa";
        toolTip.SetToolTip(btnSize, "The size of the next words: normal, large or small");
        btnSize.UseVisualStyleBackColor = false;
        btnSize.Click += BtnSize_Click;
        //
        // btnColor
        //
        btnColor.Location = new Point(142, 2);
        btnColor.Name = "btnColor";
        btnColor.Size = new Size(40, 24);
        btnColor.TabIndex = 4;
        btnColor.Text = "A";
        toolTip.SetToolTip(btnColor, "The colour of the next words; press again for the next colour, and once more to put it back");
        btnColor.UseVisualStyleBackColor = false;
        btnColor.Click += BtnColor_Click;
        //
        // btnEmoji
        //
        btnEmoji.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnEmoji.Location = new Point(432, 2);
        btnEmoji.Name = "btnEmoji";
        btnEmoji.Size = new Size(88, 24);
        btnEmoji.TabIndex = 5;
        btnEmoji.Text = "Emoji";
        btnEmoji.TextImageRelation = TextImageRelation.ImageBeforeText;
        toolTip.SetToolTip(btnEmoji, "Choose an emoji from a table and put it in the message");
        btnEmoji.UseVisualStyleBackColor = true;
        btnEmoji.Click += BtnEmoji_Click;
        //
        // btnShare
        //
        btnShare.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnShare.Location = new Point(524, 2);
        btnShare.Name = "btnShare";
        btnShare.Size = new Size(70, 24);
        btnShare.TabIndex = 6;
        btnShare.Text = "Share";
        toolTip.SetToolTip(btnShare, "Attach a picture or send a file to the room");
        btnShare.UseVisualStyleBackColor = true;
        btnShare.Click += BtnShare_Click;
        //
        // txtMessage
        //
        txtMessage.Dock = DockStyle.Fill;
        txtMessage.HideSelection = false;
        txtMessage.Location = new Point(3, 39);
        txtMessage.Multiline = true;
        txtMessage.Name = "txtMessage";
        txtMessage.ForeColor = Color.Black;
        txtMessage.ScrollBars = RichTextBoxScrollBars.Vertical;
        txtMessage.Size = new Size(503, 54);
        txtMessage.TabIndex = 1;
        txtMessage.KeyDown += TxtMessage_KeyDown;
        //
        // btnSend
        //
        btnSend.Dock = DockStyle.Fill;
        btnSend.Location = new Point(512, 39);
        btnSend.Name = "btnSend";
        btnSend.Size = new Size(85, 54);
        btnSend.TabIndex = 2;
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
        pnlRoomHeader.ResumeLayout(false);
        pnlRoomHeader.PerformLayout();
        ctxTranscript.ResumeLayout(false);
        pnlFileNotice.ResumeLayout(false);
        pnlFileActions.ResumeLayout(false);
        pnlFormat.ResumeLayout(false);
        pnlInput.ResumeLayout(false);
        pnlChat.ResumeLayout(false);
        pnlMembers.ResumeLayout(false);
        pnlRooms.ResumeLayout(false);
        tableLayout.ResumeLayout(false);
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

    /// <summary>The left column: the rooms, under the header that manages them.</summary>
    protected Panel pnlRooms;

    /// <summary>The middle column: the transcript over the input area.</summary>
    protected TableLayoutPanel pnlChat;

    /// <summary>The bottom row of the middle column: the tools over the message box and Send.</summary>
    protected TableLayoutPanel pnlInput;

    /// <summary>The right column: who is in the room.</summary>
    protected Panel pnlMembers;
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
    protected MessageInput txtMessage;
    protected Button btnSend;
    protected Panel pnlFormat;
    protected Button btnBold;
    protected Button btnItalic;
    protected Button btnUnderline;
    protected Button btnSize;
    protected Button btnColor;
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
