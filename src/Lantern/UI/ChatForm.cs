using Lantern.Core;
using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// The window both roles share (Figures 3 and 4): rooms on the left, transcript in the middle,
/// members on the right, message box along the bottom.
/// <para>
/// Nothing here knows where the data comes from: every room, member and message is read from
/// <see cref="Service"/> and every change is pushed back through it. That is what lets the same
/// window run on the in-memory <see cref="FakeChatService"/> today and on the real chat engine
/// later — the UI never talks to the transport.
/// </para>
/// Roles differ only in which buttons the subclass drops into <see cref="pnlRoomActions"/>.
/// </summary>
/// <remarks>
/// Concrete rather than abstract, and only because the Windows Forms designer has to construct a class to
/// draw it: the shared layout - the transcript, the room list, the status bar, most of both windows - lives
/// in this file, and an abstract base is a file design view can never open. The constructor that takes a
/// chat is still <c>protected</c>, so this is one word of openness and no more: nobody outside a subclass can
/// build a chat window around a chat, and <see cref="DesignTime"/> keeps the remaining door for the designer.
/// </remarks>
public partial class ChatForm : Form
{
    /// <summary>
    /// True while the room list is being rebuilt, so the rebuild does not read as a click. This is load
    /// bearing: the moment the selection is put back, the list says “this room was chosen”, and choosing a
    /// room is a request to the service, which answers with another room list. See <see cref="ShowRooms"/>.
    /// </summary>
    private bool syncing;

    /// <summary>A room list arrived while the list was already being rebuilt, so one more pass is owed.</summary>
    private bool roomsRefreshPending;

    /// <summary>
    /// The room a join is on its way to, while it is on its way. The list is highlighted for this rather than
    /// for <see cref="CurrentRoom"/> during a switch, because the person has already chosen and a room list
    /// that arrives in the middle of the join must not put the highlight back where they came from.
    /// </summary>
    private Guid? enteringRoomId;

    /// <summary>Which join <see cref="enteringRoomId"/> belongs to, so an older answer that arrives late loses.</summary>
    private Guid enteringToken;

    /// <summary>
    /// How many times one room list may cause the list to be redrawn. Not a magic number: it is “several more
    /// times than anybody will change the rooms while we are drawing”. Reaching it means something is handing
    /// this window room lists faster than it can draw them, which is the loop this guard exists to stop.
    /// </summary>
    private const int MaxRoomRepaints = 8;

    private bool busy;

    /// <summary>
    /// Which picture, if any, is written where in the transcript (F7), as character positions.
    /// <para>
    /// Positions rather than line text because an entry is no longer one line (F13): it is a headline, the
    /// message under it and, for a photo, the thumbnail under that - three lines, only one of which names the
    /// file, and the headline is the same text for every message that minute from that sender. The double click
    /// knows the character under the caret and nothing else, so that is what an entry is keyed by. Positions
    /// also survive word wrap, which line numbers do not: the same text gets more lines when the window gets
    /// narrower, and a transcript that opened the wrong photo after somebody resized the window would be a
    /// transcript that opened the wrong photo.
    /// </para>
    /// Rebuilt whenever the transcript is redrawn.
    /// </summary>
    private readonly List<PictureSpan> pictures = new();

    /// <summary>Where each file's entry was written, for the same reason pictures are tracked: a right click has to know what it landed on.</summary>
    private readonly List<FileSpan> files = new();

    private sealed record FileSpan(int Start, int End, FileMessage File);

    /// <summary>
    /// Every transfer this window has seen, either way, by id.
    /// <para>
    /// The progress bar belongs to whichever file is furthest ahead and forgets the others; a transcript entry
    /// does not forget, and outlives the bar. So the entry keeps its own handle on the job - which is what makes
    /// "save that file to my folder" work ten minutes after it arrived, or while a second file is using the bar.
    /// </para>
    /// </summary>
    private readonly Dictionary<Guid, FileTransferJob> transfers = [];

    /// <summary>
    /// Where each file this window received has already been written, by transfer id.
    /// <para>
    /// Without it, saving the same entry twice writes the file twice and says “saved” twice, and asking for a
    /// file that was declined writes nothing and says nothing. Both of those are the window being quiet about a
    /// thing the person can only discover by looking in the folder.
    /// </para>
    /// </summary>
    private readonly Dictionary<Guid, string> savedTo = [];

    /// <summary>One picture message, and the span of transcript it occupies: from its first character to just past its last.</summary>
    private sealed record PictureSpan(int Start, int End, PictureMessage Picture);

    /// <summary>
    /// The picture windows this window has open (F14). One per picture, so looking at the same photo twice
    /// brings back the window that is already there instead of stacking another copy of it.
    /// </summary>
    private readonly List<PictureViewerForm> viewers = new();

    /// <summary>
    /// The picture windows this window has open. They belong to this window: closing it closes them, and a
    /// person who closes the room a photo was sent in should not be left holding the photo.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<PictureViewerForm> OpenPictures => viewers;

    /// <summary>How many characters of transcript this window has written - where the next entry will start.</summary>
    private int TranscriptEnd => rtbChat.TextLength;

    /// <summary>
    /// The window as the design surface builds it: over a chat that talks to nothing, because the designer has
    /// no chat to give it and still has to draw a room list, a member list and a transcript to put controls
    /// among. Anywhere else this throws - see <see cref="DesignTime"/>.
    /// </summary>
    /// <remarks>
    /// It calls <c>InitializeComponent</c> itself rather than chaining to the other constructor, and so does
    /// every window in this project: the designer looks for that call in the constructor it uses, and a design
    /// surface assembled by a different route is a surface that lies about the window. Both constructors then
    /// hand over to <see cref="SetUp"/>, so the window being drawn and the window being used are the same
    /// window down to the last status-bar word.
    /// </remarks>
    public ChatForm()
    {
        Service = DesignTime.ChatService(isHost: true, nameof(ChatForm));

        InitializeComponent();

        SetUp();
    }

    protected ChatForm(IChatService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Service = service;

        InitializeComponent();

        SetUp();
    }

    /// <summary>
    /// What the window does once it has controls and a chat: the state it opens in said out loud, the service's
    /// news relayed on to this thread, and the three lists filled. Every constructor comes here.
    /// </summary>
    private void SetUp()
    {
        txtMessage.MaxLength = ValidationRules.MessageMaxLength;

        // The state this window opens in, said out loud rather than left to be inferred from a room list that
        // happens to be filled in. Scenario 1 asks for these words in the host's bar; the startup scenario
        // asks the bar to report the connection state. Said here, after the service has been started and its
        // news relayed, so that it is what a person reads when they look up.
        SetStatus(Service.IsHost ? ServerOnlineState : ConnectedState);

        BindService();
        ShowRooms();
        ShowMembers();
        ShowTranscript(); // no room entered yet: Send, Share and the box all start disabled
    }

    /// <summary>
    /// Everything this window knows about rooms, members and traffic. Both constructors fill it before the
    /// controls are built, so it is never missing and never changed afterwards.
    /// </summary>
    protected IChatService Service { get; private set; }

    /// <summary>The room this window is in, or <see langword="null"/> when none is selected.</summary>
    protected Room? CurrentRoom { get; private set; }

    /// <summary>The highlighted room in the list, whether or not it has been entered.</summary>
    protected Room? SelectedRoom => (lstRooms.SelectedItem as RoomRow)?.Room;

    // ------------------------------------------------------------- plumbing

    private void BindService()
    {
        Service.MessageReceived += (_, message) => OnUi(() => ShowIncoming(message));
        Service.RoomsChanged += (_, _) => OnUi(ShowRooms);
        Service.MembersChanged += (_, _) => OnUi(ShowMembers);
        Service.StatusChanged += (_, status) => OnUi(() => SetStatus(status));
        Service.IncomingFile += (_, job) => OnUi(() => OfferFile(job));
        Service.ConnectionLost += (_, error) => OnUi(() => OnConnectionLost(error));
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread, dropping it if the window is closing.</summary>
    private void OnUi(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        try
        {
            // Before the window has a handle there is nothing to marshal on to: the form was built
            // on this very thread, so run it here.
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // The window went away while the event was in flight.
        }
    }

    /// <summary>
    /// F10: gives this window something to make a noise with.
    /// <para>
    /// Nothing is attached by default, and the window itself never builds one: the place that composes the
    /// program is the place that knows whether this is a person's desktop or a test runner with four hundred
    /// windows in it, and a noise is not something to decide on somebody else's behalf.
    /// </para>
    /// </summary>
    public void AttachSounds(SoundNotifier sounds)
    {
        ArgumentNullException.ThrowIfNull(sounds);

        if (noises is not null)
        {
            throw new InvalidOperationException("This window already makes a noise.");
        }

        noises = sounds;
        Service.MessageReceived += (_, message) => OnUi(() => noises.MessageArrived(message));

        // The one time a window is allowed to complain about its own noise. Every failure inside the notifier is
        // swallowed on purpose - a chat message must not fail because of a sound card - which is correct for the
        // message and hopeless for the person sitting in front of "it does not make a sound". So the reason
        // surfaces here, once, in the bar this window already uses for answering.
        sounds.CouldNotPlay += reason => OnUi(() => SetStatus($"no sound: {reason}"));
    }

    /// <summary>A service call refuses something the user asked for (duplicate name, over-long message).</summary>
    protected void SetStatus(string message)
        => lblStatus.Text = message.Length == 0 ? "Status: idle" : $"Status: {message}";

    /// <summary>
    /// The states a window can be in, in the words the scenario list and the figure use for them: scenario 1
    /// asks for “server online” in the host's bar, and the startup scenario asks the bar to report the
    /// connection state. There is deliberately no “reconnecting”: Lantern does not dial anybody back on its
    /// own (M4, and <c>ConnectionLifecycleTests</c>), so the bar says the meeting is over instead of
    /// promising an attempt that is not going to happen.
    /// </summary>
    protected const string ServerOnlineState = "server online";
    protected const string ConnectedState = "connected";
    protected const string ConnectionLostState = "connection lost";

    /// <summary>
    /// What the status bar says. Readable from outside because it is the only place this program explains
    /// itself — and because a window that has stepped aside on its own no longer has a control tree to
    /// look through.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string StatusText => lblStatus.Text ?? string.Empty;

    /// <summary>
    /// Runs a service call with Send and Share greyed out, and reports a refusal in the status bar
    /// instead of letting it reach the message loop.
    /// </summary>
    protected async Task RunAsync(Func<Task> call)
    {
        SetBusy(true);
        try
        {
            await call();
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus($"failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Grey out Send and Share while an operation is in flight.</summary>
    protected bool IsBusy => busy;

    private void SetBusy(bool value)
    {
        busy = value;
        btnSend.Enabled = !value && CurrentRoom is not null;
        btnShare.Enabled = !value && CurrentRoom is not null;
        UseWaitCursor = value;
    }

    // ----------------------------------------------------------- room list

    /// <summary>
    /// Rebuilds the room list from the service, keeping the window pointed at the room it is actually in.
    /// </summary>
    /// <remarks>
    /// All of it happens inside the <see cref="syncing"/> guard, and that is not tidiness. Putting the
    /// selection back is a change to the list like any other, so it raises the same event a click does - and
    /// that event means “the person asked for this room”. A room list from the service is therefore not a
    /// request to redraw but a request to join, and joining a room makes the service send another room list.
    /// With the highlight sitting on a room this window had not got into, every list re-knocked on that door:
    /// join, list, join, list. When the news arrived on the window's own thread - which is how the host's own
    /// window gets it, in process, before the join has even returned - each turn stacked on the last until the
    /// process died of a stack overflow on the line that restores the selection.
    /// <para>
    /// Two things keep that impossible now: nothing outside the guard raises the selection, and the list is
    /// anchored on <see cref="CurrentRoom"/> rather than on whatever was highlighted, so it cannot hold a
    /// highlight the window has no intention of honouring.
    /// </para>
    /// </remarks>
    private void ShowRooms()
    {
        if (syncing)
        {
            // A room list while a redraw is in hand. Take a note of it and get out; nesting one redraw inside
            // another is exactly the shape that used to grow the stack.
            roomsRefreshPending = true;
            return;
        }

        syncing = true;
        var passes = 0;
        try
        {
            do
            {
                roomsRefreshPending = false;
                passes++;
                RepaintRooms();
            }
            while (roomsRefreshPending && passes < MaxRoomRepaints);
        }
        finally
        {
            syncing = false;
            roomsRefreshPending = false;
        }
    }

    /// <summary>
    /// Draws the current rooms and puts the highlight on the room this window is in - or on the one it is on
    /// its way to, while a join is in the air.
    /// </summary>
    /// <remarks>
    /// Every decision in here is made on <see cref="Room.Id"/>. The name would have done the same for a
    /// window on its own, and nothing else: a rename hands out a new <see cref="Room"/> for the same room,
    /// and a window that had been remembering names was left holding a room nobody could find again - which
    /// is what a rename used to do to everybody standing in one, transcript included.
    /// </remarks>
    private void RepaintRooms()
    {
        var listed = Service.Rooms.ToArray();

        // Follow the room across its own rename. Holding the old object would mean a transcript that nothing
        // else in the program writes to any more, and a window that stops changing after it has been renamed.
        if (CurrentRoom is { } standing && listed.FirstOrDefault(r => r.Id == standing.Id) is { } stillThere && !ReferenceEquals(stillThere, standing))
        {
            CurrentRoom = stillThere;
        }

        var wanted = enteringRoomId ?? CurrentRoom?.Id;

        lstRooms.BeginUpdate();
        lstRooms.Items.Clear();

        foreach (var room in listed)
        {
            lstRooms.Items.Add(new RoomRow(room));
        }

        lstRooms.EndUpdate();

        var index = wanted is null ? -1 : RowOf(wanted.Value);

        if (index >= 0)
        {
            lstRooms.SelectedIndex = index;
        }
        else if (CurrentRoom is not null && !listed.Any(r => r.Id == CurrentRoom.Id))
        {
            // The room we were in disappeared (the host deleted it). Only that counts: a room this window
            // was never in going away is somebody else's news.
            LeaveRoom(announce: false);
        }
    }

    /// <summary>
    /// Which row shows a room, by identity. The name is not consulted: while a rename is arriving two rows can
    /// carry the same name, and only one of them is the room.
    /// </summary>
    private int RowOf(Guid roomId)
    {
        for (var i = 0; i < lstRooms.Items.Count; i++)
        {
            if (lstRooms.Items[i] is RoomRow row && row.Room.Id == roomId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// One row of the room list. It holds the room rather than its name because everything done to a room -
    /// entered, renamed, deleted - has to be done to <em>that</em> room, and a name is the one thing about a
    /// room that is not stable. The name is still what the row says out loud.
    /// </summary>
    protected sealed record RoomRow(Room Room)
    {
        public override string ToString() => Room.Name;
    }

    private void LstRooms_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!syncing && lstRooms.SelectedItem is RoomRow row && row.Room.Id != CurrentRoom?.Id)
        {
            _ = EnterRoomAsync(row.Room);
        }
    }

    /// <summary>
    /// Points the list at a room by identity - at the one this window is in, or at nothing when it is in none.
    /// </summary>
    /// <remarks>
    /// Used when an attempt to enter a room did not end with this window in it. Left as it was, the highlight
    /// would go on naming a room the window is not in, and the next room list would treat that highlight as an
    /// order and try the same door again - quietly, forever, in the company of nothing else changing.
    /// </remarks>
    private void Highlight(Guid? roomId)
    {
        var wasSyncing = syncing;
        syncing = true;
        try
        {
            lstRooms.SelectedIndex = roomId is null ? -1 : RowOf(roomId.Value);
        }
        finally
        {
            syncing = wasSyncing;
        }
    }

    /// <summary>This window is no longer on its way anywhere, and the list shows where it actually is.</summary>
    private void StopEntering()
    {
        enteringRoomId = null;
        Highlight(CurrentRoom?.Id);
    }

    /// <summary>
    /// Goes into a room by name - by the same door as a click in the list, because there is one way to enter a
    /// room in this window and a second way would be a second set of rules about who may enter what.
    /// </summary>
    protected async Task EnterRoomByNameAsync(string name)
    {
        var room = Service.Rooms.FirstOrDefault(r => r.Name == name);

        if (room is null || room.Id == CurrentRoom?.Id)
        {
            return;
        }

        await EnterRoomAsync(room);
    }

    private async Task EnterRoomAsync(Room room)
    {
        // Two clicks, two answers, and the link does not promise which comes back first. Whichever click is
        // newer is the one this window obeys; the older answer is dropped rather than allowed to move the
        // window into a room somebody stopped asking for.
        var token = Guid.NewGuid();
        enteringToken = token;
        enteringRoomId = room.Id;

        try
        {
            await RunAsync(async () =>
            {
                try
                {
                    await Service.JoinRoomAsync(room.Id);
                }
                catch (Exception)
                {
                    // The room said no. Say it back in the bar below - but first take the highlight off the
                    // door, because a highlight is a promise this window keeps trying to keep.
                    if (enteringToken == token)
                    {
                        StopEntering();
                    }

                    throw;
                }

                if (enteringToken != token)
                {
                    return;
                }

                CurrentRoom = Service.Rooms.FirstOrDefault(r => r.Id == room.Id);

                if (CurrentRoom is null)
                {
                    StopEntering();
                    SetStatus($"failed: no room \"{room.Name}\"");
                    return;
                }

                ShowMembers();
                ShowTranscript();

                // The list follows the window, not the other way about: a room list that arrived while this
                // join was in the air was drawn from a state that did not yet know where the window was going.
                Highlight(CurrentRoom.Id);
                SetStatus($"in room \"{room.Name}\"");
            });
        }
        finally
        {
            if (enteringToken == token)
            {
                StopEntering();
            }
        }
    }

    /// <summary>Redraws the transcript from the current room's own history (F6).</summary>
    private void ShowTranscript()
    {
        ClearTranscript();
        var editable = CurrentRoom is not null;
        txtMessage.Enabled = editable;
        if (!busy)
        {
            btnSend.Enabled = editable;
            btnShare.Enabled = editable;
        }

        if (CurrentRoom is null)
        {
            lblMembersHeader.Text = "Members";
            Append(new SystemMessage(string.Empty, $"Welcome, {Service.Nickname}! Select a room to enter."));
            return;
        }

        lblMembersHeader.Text = $"Members ({CurrentRoom.Members.Count})";
        foreach (var message in CurrentRoom.Transcript)
        {
            Append(message);
        }
    }

    /// <summary>Drops out of the current room; <paramref name="announce"/> leaves a notice behind.</summary>
    protected void LeaveRoom(bool announce)
    {
        if (announce && CurrentRoom is { } room)
        {
            room.Append(SystemMessage.Leave(room.Name, Service.Nickname));
        }

        CurrentRoom = null;
        ShowTranscript();
        ShowMembers();
    }

    // ---------------------------------------------------------- member list

    private void ShowMembers()
    {
        lstMembers.BeginUpdate();
        lstMembers.Items.Clear();
        foreach (var user in Service.Members)
        {
            lstMembers.Items.Add(user.Nickname == Service.Nickname ? $"{user.Nickname} (you)" : user.Nickname);
        }

        lstMembers.EndUpdate();
        if (CurrentRoom is not null)
        {
            lblMembersHeader.Text = $"Members ({Service.Members.Count})";
        }
    }

    // --------------------------------------------------------------- sending

    private void BtnSend_Click(object? sender, EventArgs e) => _ = SendMessageAsync();

    private void TxtMessage_KeyDown(object? sender, KeyEventArgs e)
    {
        // Enter sends, Shift+Enter adds a line break (scenario 4).
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        var body = txtMessage.Text.Trim();
        if (CurrentRoom is not { } room || body.Length == 0)
        {
            return;
        }

        if (ValidationRules.MessageError(body) is { } error)
        {
            SetStatus(error);
            return;
        }

        var message = new TextMessage(Service.Nickname, room.Name, body);
        await RunAsync(async () =>
        {
            await Service.SendAsync(message);
            txtMessage.Clear();
            Append(message);
        });
    }

    private void BtnShare_Click(object? sender, EventArgs e) => _ = ShareAsync();

    /// <summary>
    /// F7 and F8: one Share button for pictures and files alike, because the person clicking has a thing
    /// they want to send and should not be asked a second question about what kind of thing it is. The
    /// name decides (see <see cref="Attachments"/>), and a name nobody recognises becomes a file, which is
    /// the safe answer: a file arrives complete and they open it themselves.
    /// </summary>
    private async Task ShareAsync()
    {
        if (CurrentRoom is not { } room)
        {
            SetStatus("select a room first");
            return;
        }

        var path = AskForAttachment();

        if (path is null)
        {
            // Changed their mind, which is not a failure and needs nothing said about it.
            return;
        }

        var file = new FileInfo(path);

        if (!file.Exists)
        {
            SetStatus("that file could not be read");
            return;
        }

        if (Attachments.IsPicture(path))
        {
            await SharePictureAsync(room, file);
        }
        else
        {
            ShareFile(room, file);
        }
    }

    /// <summary>
    /// F7: a picture's bytes are the message, so they are read here and now - a photo is at most a few
    /// megabytes, and it is already sitting in memory on the way to the transcript.
    /// </summary>
    private async Task SharePictureAsync(Room room, FileInfo file)
    {
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Somebody else has it open, or the drive went away between the dialog and this line. The
            // window is still a window.
            SetStatus($"could not read {file.Name}: {ex.Message}");
            return;
        }

        // Said here, while the person is still looking at the folder they picked from. The host would
        // refuse it too - and does, for the peer that does not check - but a 40 MB photo should not have
        // to travel to the host and back to be turned down.
        if (ValidationRules.PictureError(file.Name, bytes.LongLength) is { } error)
        {
            SetStatus(error);
            return;
        }

        var picture = new PictureMessage(Service.Nickname, room.Name, file.Name, bytes);

        // The bar goes up before the await, because the wait is the thing worth showing: over a radio these
        // bytes are a minute of otherwise motionless window, and a window that sits still for a minute is
        // asked about. It comes down whatever the send did, so a refusal cannot leave a bar running.
        BeginPictureUpload(file.Name, bytes.LongLength);

        try
        {
            await RunAsync(async () =>
            {
                await Service.SendAsync(picture);
                Append(picture);
                SetStatus($"sent {file.Name}");
            });
        }
        finally
        {
            EndPictureUpload();
        }
    }

    /// <summary>
    /// F8: the bytes are not read here. A file is announced and then carried in numbered chunks, which is
    /// what makes it work for a file bigger than the memory of the machine sending it - and it is why this
    /// returns at once instead of waiting: the job that comes back is the thing to watch, not this call.
    /// </summary>
    private void ShareFile(Room room, FileInfo file)
    {
        FileTransferJob job;

        try
        {
            job = Service.SendFile(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The file went away, or is too big, or is being held by something else. All of them are
            // worth one line in the status bar and a window that is still a window.
            SetStatus(ex.Message);
            return;
        }

        // Built from the job rather than from what this window knows, so the id in this transcript is the
        // id every other window's transcript has for the same file (F8: one event, seen four times).
        Append(new FileMessage(job.SenderNickname, job.RoomName, job.FileName, job.SizeBytes, job.Id, id: job.MessageId));

        WatchSend(job);
        SetStatus($"sending {file.Name}");
    }

    /// <summary>
    /// Puts a sending file's progress where a person can see it, and stops the transfer if they ask.
    /// </summary>
    private void WatchSend(FileTransferJob job)
    {
        Track(job);
        ShowFileProgress();
    }

    /// <summary>
    /// Asks for something to send; null means nothing was chosen. One method to override, so the decision
    /// made from the choice can be tested without a dialog standing in front of the test runner.
    /// </summary>
    protected virtual string? AskForAttachment()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Share",
            Filter = $@"{Attachments.PictureFilter}|All files (*.*)|*.*",
        };

        return picker.ShowDialog(this) == DialogResult.OK ? picker.FileName : null;
    }

    // ------------------------------------------------------------- receiving
    //
    // F8, the other half: a file arrives whether or not anybody answers, because the bytes are already on
    // their way. So the window says so in a strip above the input rather than in a dialog - the person can
    // keep typing, and a second offer has somewhere to wait - and the file goes to disk only when they say
    // where. Turning one down throws the bytes away and tells the sender, which is the transfer layer's job
    // rather than this window's.

    /// <summary>Files offered to this window that have not been answered yet, oldest first.</summary>
    private readonly List<FileTransferJob> unanswered = [];

    /// <summary>Files this window has agreed to keep and is waiting to write out.</summary>
    private readonly List<FileTransferJob> accepted = [];

    /// <summary>Every file in flight either way, which between them own the progress bar.</summary>
    private readonly List<FileTransferJob> inFlight = [];

    /// <summary>The offer the notice strip is about, or null when the strip is down.</summary>
    private FileTransferJob? offered;

    /// <summary>The file the bar, its label and the Stop button belong to.</summary>
    private FileTransferJob? shown;

    /// <summary>Files this window stopped itself - their ending needs no comment on top of the one given.</summary>
    private readonly HashSet<FileTransferJob> stoppedHere = [];

    /// <summary>What this window plays when something arrives, if anybody has asked it to (F10).</summary>
    private SoundNotifier? noises;

    /// <summary>Scenario 6: name, size and sender, then Save to… or Decline.</summary>
    private void OfferFile(FileTransferJob job)
    {
        // Said here, when the file is announced, rather than when it arrives: the sender's window wrote this
        // line the moment it sent, and a room whose transcript reads differently in different windows is a
        // room where two people cannot agree about what happened (F8: one event, seen in each window).
        Append(new FileMessage(job.SenderNickname, job.RoomName, job.FileName, job.SizeBytes, job.Id, id: job.MessageId));

        Track(job);
        unanswered.Add(job);
        ShowOffer();
    }

    /// <summary>
    /// Starts watching a transfer in either direction: it owns the progress bar from now until it is over,
    /// however it ends.
    /// </summary>
    private void Track(FileTransferJob job)
    {
        inFlight.Add(job);
        transfers[job.Id] = job;

        _ = job.Finished.ContinueWith(
            _ => OnUi(() => FileFinished(job)),
            TaskScheduler.Default);

        // Polled rather than driven by the job's progress callback. A chunk is 8 KiB, so a hundred megabyte
        // file is twelve thousand reports and a window that repaints four times a second is the same window
        // as this one either way - and reading BytesDone cannot miss one, cannot arrive on the wrong thread,
        // and cannot be the reason a transfer waits.
        timerProgress.Start();
        ShowFileProgress();
    }

    private void TimerProgress_Tick(object? sender, EventArgs e) => ShowFileProgress();

    /// <summary>Redraws the progress part of the status bar from whichever file is furthest ahead.</summary>
    private void ShowFileProgress()
    {
        shown = accepted.LastOrDefault(Moving)
            ?? inFlight.LastOrDefault(Moving);

        if (shown is null)
        {
            inFlight.RemoveAll(j => !Moving(j));
            accepted.RemoveAll(j => !Moving(j));

            // A picture that is still going out owns the bar when no file does - a moving bar and a count of
            // seconds, which is all a single frame can be. A file's real percentage takes the bar when both.
            if (uploading is not null)
            {
                ShowUploadLabel();
                return;
            }

            progressFile.Visible = false;
            lblFileProgress.Visible = false;
            btnFileStop.Visible = false;
            timerProgress.Stop();

            return;
        }

        var others = inFlight.Count(j => Moving(j) && j != shown);

        progressFile.Style = ProgressBarStyle.Continuous;
        progressFile.Value = (int)Math.Round(shown.Fraction * progressFile.Maximum);
        progressFile.Visible = true;

        // Which way it is going, how far it has got and how big it is - in that order, because the first
        // question asked of a progress bar is "am I sending this or receiving it", and a bare percentage
        // answers none of them. Over a radio the name alone can sit there for a minute.
        lblFileProgress.Text = $"{Direction(shown)} {shown.FileName} {shown.Fraction:P0}"
            + $" - {FileSize.Describe(shown.BytesDone)} of {FileSize.Describe(shown.SizeBytes)}"
            + (others > 0 ? $" (+{others})" : string.Empty);
        lblFileProgress.Visible = true;

        // Stopping is offered in both directions. Half of a file is worth stopping whether it was coming or
        // going, and the person who notices is the one holding the mouse.
        btnFileStop.Visible = true;
    }

    /// <summary>Which way a file is moving, from this window's point of view, in the two words a person wants.</summary>
    private string Direction(FileTransferJob job) => accepted.Contains(job)
        ? "saving"
        : string.Equals(job.SenderNickname, Service.Nickname, StringComparison.Ordinal) ? "sending" : "receiving";

    // ----------------------------------------------------------- a picture in flight
    //
    // A picture is not chunked: its bytes are the message, and they travel in one frame. So there is no fraction
    // to report - not laziness, but a fact about the wire, and the honest thing to show is that something is
    // happening and how long it has been happening. A 5 MB photo over Bluetooth is a minute of otherwise silent
    // window, which reads as a crash to anyone who has not sent one before.

    /// <summary>The picture this window is pushing out, or null when it is not pushing one.</summary>
    private (string Name, long Bytes)? uploading;

    private DateTime uploadStarted;

    /// <summary>Starts the bar for a picture being sent. Nothing else knows it is happening; see <see cref="EndPictureUpload"/>.</summary>
    private void BeginPictureUpload(string name, long bytes)
    {
        uploading = (name, bytes);
        uploadStarted = DateTime.Now;
        timerProgress.Start();
        ShowUploadLabel();
    }

    /// <summary>Takes the picture's bar down - unless a file has taken the bar over in the meantime, which is its own news.</summary>
    private void EndPictureUpload()
    {
        uploading = null;

        if (!inFlight.Any(Moving))
        {
            progressFile.Visible = false;
            lblFileProgress.Visible = false;
            timerProgress.Stop();
        }
    }

    /// <summary>
    /// The line beside the moving bar while a picture is going out: its name, its size and the seconds so far.
    /// The seconds are the useful part - they are the difference between a working machine and a stopped one.
    /// </summary>
    private void ShowUploadLabel()
    {
        if (uploading is not { } shot)
        {
            return;
        }

        lblFileProgress.Text = $"sending {shot.Name} - {FileSize.Describe(shot.Bytes)}"
            + $" - {(int)Math.Round((DateTime.Now - uploadStarted).TotalSeconds)} s";
        lblFileProgress.Visible = true;

        progressFile.Style = ProgressBarStyle.Marquee;
        progressFile.Visible = true;

        // Stopping one frame halfway is not a thing the wire can do, so no Stop button is offered: a button
        // that cannot stop anything is worse than no button.
        btnFileStop.Visible = inFlight.Any(Moving);
    }

    private void FileFinished(FileTransferJob job)
    {
        // Read before it is removed: whether this window had agreed to keep the file decides what is worth
        // saying about it, and the two lines cannot be said in the same breath.
        var wasAccepted = accepted.Contains(job);

        accepted.Remove(job);
        stoppedHere.Remove(job);

        // A file that has finished arriving but was never answered is still an offer worth making: the bytes
        // are in Lantern's own area and will sit there until somebody says where they go. Only a cancelled
        // transfer comes off the list by itself - there is nothing left to hand over.
        if (job.Cancel.IsCancellationRequested)
        {
            unanswered.Remove(job);
        }

        // The noise is asked for here rather than inside the transfer, because only the window knows whether
        // this file was its own. A host relaying somebody else's file has no business announcing it twice.
        noises?.FileDone(job);

        if (job.Cancel.IsCancellationRequested)
        {
            // Somebody stopped it - this window, the person at the other end, or a machine that could not
            // drain fast enough - and one of them has already said so in a line of its own. What this adds is
            // the bar going away: a number stuck at 34% reads as "still going" to anybody who glances at it.
            if (!stoppedHere.Contains(job) && shown == job)
            {
                SetStatus($"{job.FileName} did not finish");
            }

            // The offer goes down with it: a strip offering a file that no longer exists is a Decline button
            // pointed at nothing.
            ShowOffer();
        }
        else if (shown == job && !wasAccepted && !job.IsComplete)
        {
            // Ended short without anybody stopping it - the link went away in the middle, most likely. The
            // transfer layer has said why in a line of its own; this is the part that says the number on the
            // bar is not going to move again.
            SetStatus($"{job.FileName} did not finish");
        }
        else if (shown == job && !wasAccepted)
        {
            // "arrived" rather than "saved": what has arrived is not yet anywhere permanent, and a window
            // that says saved before it has written would be believed. A file this window had already agreed
            // to keep is not said anything here - the line about it is written when it has been written, and
            // saying "arrived" after that would be the story told backwards.
            SetStatus($"{job.FileName} {(job.SenderNickname == Service.Nickname ? "sent" : "arrived")}");
        }

        ShowFileProgress();
    }

    /// <summary>Still going: not over, and not stopped. A stopped transfer counts as over everywhere.</summary>
    private static bool Moving(FileTransferJob job) => !job.Finished.IsCompleted && !job.Cancel.IsCancellationRequested;

    /// <summary>Puts the oldest unanswered offer in the strip, or takes the strip down.</summary>
    private void ShowOffer()
    {
        // Cancelled ones go; a file that arrived whole and was never answered is still there to be had.
        unanswered.RemoveAll(j => j.Cancel.IsCancellationRequested);

        if (unanswered.Count == 0)
        {
            offered = null;
            pnlFileNotice.Visible = false;
            lblFileNotice.Text = string.Empty;

            return;
        }

        var job = unanswered[0];
        var waiting = unanswered.Count - 1;

        offered = job;
        lblFileNotice.Text = $"{job.SenderNickname} is sending {job.FileName} ({FileSize.Describe(job.SizeBytes)})"
            + (waiting > 0 ? $" - {waiting} more waiting" : string.Empty);
        pnlFileNotice.Visible = true;
    }

    private void BtnFileSave_Click(object? sender, EventArgs e)
    {
        if (offered is not { } job)
        {
            return;
        }

        var folder = AskForFolder(job.FileName);

        if (folder is null)
        {
            // No folder chosen is not a refusal - the strip stays up, the bytes keep arriving, and the person
            // can decide properly instead of having lost the file.
            return;
        }

        unanswered.Remove(job);
        accepted.Add(job);
        ShowOffer();
        ShowFileProgress();

        _ = SaveReceivedAsync(job, Path.Combine(folder, job.FileName));
    }

    /// <summary>
    /// Moves a completed transfer from the spool to where the person said. Returns when the file is
    /// written, which for a file still arriving means whenever the last of it turns up.
    /// </summary>
    private async Task SaveReceivedAsync(FileTransferJob job, string path)
    {
        try
        {
            await Service.SaveReceivedFileAsync(job, path).ConfigureAwait(false);

            // Written on the window's thread with the line about it, not out here on the task's thread: this
            // dictionary is the transcript's own memory, and two writers to it is a bug nobody can see.
            OnUi(() =>
            {
                savedTo[job.Id] = path;
                SetStatus($"saved {job.FileName} to {Path.GetDirectoryName(path)}");
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            OnUi(() =>
            {
                accepted.Remove(job);
                SetStatus(ex.Message);

                // A folder that would not have it is not a lost file: the bytes are still in Lantern's own
                // area, waiting. So the offer comes back and the person is asked where - which is the whole
                // recovery, and the difference between "read-only folder" meaning "try the Documents folder
                // instead" and meaning "that file is gone".
                //
                // Not when the transfer itself was cancelled: that file never arrived whole, there is nothing
                // in the spool worth a second folder, and offering it again would be offering a button to
                // nothing.
                if (!job.Cancel.IsCancellationRequested && !unanswered.Contains(job))
                {
                    unanswered.Add(job);
                    ShowOffer();
                }
            });
        }
        catch (OperationCanceledException)
        {
            // The transfer was stopped, which is said elsewhere and louder.
        }
    }

    private void BtnFileDecline_Click(object? sender, EventArgs e)
    {
        if (offered is not { } job)
        {
            return;
        }

        unanswered.Remove(job);
        ShowOffer();

        // Cancelling the job *is* the decline: the spool is thrown away and the sender is told, both of which
        // belong to the transfer rather than to this window.
        stoppedHere.Add(job);
        job.CancelTransfer();
        SetStatus($"turned down {job.FileName}");

        // Straight away rather than on the next tick: a bar left at 3% of a file that was just thrown away is
        // a bar telling a lie about a file nobody is receiving any more.
        ShowFileProgress();
    }

    private void BtnFileStop_Click(object? sender, EventArgs e) => StopCurrentFile();

    /// <summary>
    /// Stops the file the bar is about. Public for the same reason <see cref="OpenTranscriptLineAtCaret" />
    /// is: it is what a control does, written down where it can be called without a mouse.
    /// </summary>
    public void StopCurrentFile()
    {
        if (shown is { } job && Moving(job))
        {
            stoppedHere.Add(job);
            job.CancelTransfer();
            accepted.Remove(job);
            SetStatus($"stopped {job.FileName}");
            ShowFileProgress();
            ShowOffer();
        }
    }

    /// <summary>
    /// Asks for a folder to put an incoming file in; null means none was chosen. The file keeps its own
    /// name inside it - a receiver choosing a whole folder is saying where, not what, which is also the only
    /// thing that works when three files arrive in the same minute.
    /// </summary>
    protected virtual string? AskForFolder(string fileName)
    {
        using var picker = new FolderBrowserDialog
        {
            Description = $"Where should {fileName} go?",
            UseDescriptionForTitle = true,
        };

        return picker.ShowDialog(this) == DialogResult.OK ? picker.SelectedPath : null;
    }

    /// <summary>What the offer strip says, or empty when nothing is being offered.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string FileOfferText => pnlFileNotice.Visible ? lblFileNotice.Text ?? string.Empty : string.Empty;

    /// <summary>The percentage on the bar, or -1 when no file is in flight.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int FileProgressPercent => progressFile.Visible ? (int)Math.Round(progressFile.Value * 100.0 / progressFile.Maximum) : -1;

    /// <summary>What the bar is labelled with - which file, how far, and how many others are behind it.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string FileProgressText => lblFileProgress.Visible ? lblFileProgress.Text ?? string.Empty : string.Empty;

    /// <summary>
    /// True while the bar is running without a number. A picture's bytes travel in one frame, so there is no
    /// fraction to report - but there is something happening, and that is worth showing on its own.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsProgressRunningBlind => progressFile.Visible && progressFile.Style == ProgressBarStyle.Marquee;

    /// <summary>True while the Stop button is offering to stop something.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool CanStopFile => btnFileStop.Visible;

    /// <summary>How many files this window has moving, in either direction.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int FilesInFlight => inFlight.Count(Moving);

    // ------------------------------------------------------------- F7: opening a picture

    /// <summary>
    /// F7: a thumbnail is 160 pixels; the photo is not. Double-clicking anywhere on a photo's entry - its
    /// headline, its caption or the thumbnail itself - opens the picture itself (F14), because "share a photo"
    /// that nobody can actually look at is only half a feature.
    /// </summary>
    private void RtbChat_MouseDoubleClick(object? sender, MouseEventArgs e)
        => TryOpenPictureAt(rtbChat.GetCharIndexFromPosition(e.Location));

    // ------------------------------------------------------ saving a shared thing
    //
    // F7/F8 asked how a picture or a file gets INTO a room; this is the other direction. A file being received
    // already asks where it should go, but only once, in the moment it arrives - and a person who wanted it in
    // their own folder, noticed some days later, or who wanted the photo they had already looked at, had no way
    // to say so. The transcript is where the thing lives, so that is where the asking happens: right click an
    // entry, choose Save, name the folder. The bytes are handed over by the same code the offer strip uses, so
    // there is not a second set of rules about where received files may be written.

    /// <summary>The entry the Save menu is pointed at, worked out as the menu opens.</summary>
    private PictureSpan? pointedPicture;

    private FileSpan? pointedFile;

    /// <summary>
    /// Which shared entry a position in the transcript falls in, if any.
    /// </summary>
    /// <remarks>
    /// The entry's own characters, and also a caret sitting on a line that begins inside the entry: somebody
    /// aiming at the end of a short caption, or just under a thumbnail, lands on the line break that ends the
    /// entry, and a menu that refuses across a two pixel gap is read as “this program cannot do it”. The double
    /// click keeps the stricter rule (<see cref="TryOpenPictureAt"/>) on purpose - opening a photo nobody clicked
    /// on is worse than doing nothing, whereas a menu that appears where the entry's line still is, is.
    /// </remarks>
    private (PictureSpan? Picture, FileSpan? File) SharedAt(int caret)
    {
        var lineStart = caret >= 0 && caret < rtbChat.TextLength
            ? rtbChat.GetFirstCharIndexFromLine(rtbChat.GetLineFromCharIndex(caret))
            : -1;

        bool Belongs(int start, int end) =>
            caret >= start && caret < end
            || (lineStart >= start && lineStart < end && caret < rtbChat.TextLength);

        return (pictures.LastOrDefault(s => Belongs(s.Start, s.End)),
            files.LastOrDefault(s => Belongs(s.Start, s.End)));
    }

    /// <summary>
    /// True when this window could put the bytes behind an entry somewhere else - which needs the bytes, which
    /// means they came IN. A file this window sent is already on this machine, and offering to save it back onto
    /// itself would be a button that does nothing.
    /// </summary>
    private bool CameIn(FileSpan? span) =>
        span is not null
        && transfers.TryGetValue(span.File.TransferId, out var job)
        && !string.Equals(job.SenderNickname, Service.Nickname, StringComparison.Ordinal);

    private void CtxTranscript_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Where the mouse is, unless the menu was raised some other way (the keyboard's menu key, a touch and
        // hold), in which case the caret is the only thing that knows what was pointed at.
        var point = rtbChat.PointToClient(Cursor.Position);
        var caret = rtbChat.ClientRectangle.Contains(point)
            ? rtbChat.GetCharIndexFromPosition(point)
            : rtbChat.SelectionStart;

        (pointedPicture, pointedFile) = SharedAt(caret);

        savePayloadMenuItem.Text = pointedPicture is not null ? "Save picture to…" : "Save file to…";
        savePayloadMenuItem.Enabled = pointedPicture is not null || CameIn(pointedFile);

        // Nothing saveable here: no menu at all. A one-item menu with the item greyed out is the window
        // claiming it does not know what was right-clicked, when it knows exactly - it is a sentence.
        e.Cancel = !savePayloadMenuItem.Enabled;
    }

    private void SavePayloadMenuItem_Click(object? sender, EventArgs e) => _ = SaveSharedAtAsync(
        pointedPicture?.Start ?? pointedFile?.Start ?? rtbChat.SelectionStart);

    /// <summary>True when right-clicking this position in the transcript has something to offer.</summary>
    public bool CanSaveSharedAt(int caret)
    {
        var (picture, file) = SharedAt(caret);
        return picture is not null || CameIn(file);
    }

    /// <summary>
    /// Puts the shared picture or file at this position into a folder the person chooses.
    /// </summary>
    /// <returns>The path written, or null when nothing there can be saved or they changed their mind about the
    /// folder. Which of those it was is said in the status bar.</returns>
    public async Task<string?> SaveSharedAtAsync(int caret)
    {
        var (picture, file) = SharedAt(caret);

        if (picture is not null)
        {
            // The bytes are already in memory - that is what makes the thumbnail - so this needs no asking of
            // the service, and it writes what was sent rather than what the thumbnail was shrunk to.
            var to = AskForFolder(picture.Picture.FileName) is { } folder ? FreePath(folder, picture.Picture.FileName) : null;

            if (to is null)
            {
                return null;
            }

            try
            {
                File.WriteAllBytes(to, picture.Picture.Data);
                SetStatus($"saved {Path.GetFileName(to)}");
                return to;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The folder is read-only, the disk is full, another program holds the file open. All of them
                // worth one line, and none of them a reason to lose the window.
                SetStatus($"{Path.GetFileName(to)} could not be saved: {ex.Message}");
                return null;
            }
        }

        if (CameIn(file) && transfers.TryGetValue(file!.File.TransferId, out var job))
        {
            var to = AskForFolder(job.FileName) is { } chosen ? Path.Combine(chosen, job.FileName) : null;

            if (to is null)
            {
                return null;
            }

            if (savedTo.TryGetValue(job.Id, out var already)
                && string.Equals(Path.GetDirectoryName(already), Path.GetDirectoryName(to), StringComparison.OrdinalIgnoreCase))
            {
                // Not a refusal - a fact about where the bytes went. Saying it in the bar is cheaper than
                // writing a second copy somewhere the person will have to compare later.
                SetStatus($"{job.FileName} is already saved at {already}");
                return already;
            }

            // The offer strip's own way of writing a received file out, reasons included: a file that is still
            // in Lantern's area is written, one that was thrown away says so rather than writing nothing and
            // keeping quiet about it.
            await SaveReceivedAsync(job, to);

            if (!File.Exists(to))
            {
                return null;
            }

            savedTo[job.Id] = to;
            return to;
        }

        return null;
    }

    /// <summary>
    /// A place in this folder for a thing being saved: its own name, and if that name is taken then the name
    /// with a number on the end. Saving a photo over something already in the folder would destroy a file the
    /// person never mentioned, and a chat window has no right to do that quietly.
    /// </summary>
    private static string FreePath(string folder, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var candidate = Path.Combine(folder, name);

        for (var n = 2; File.Exists(candidate) && n < 1000; n++)
        {
            candidate = Path.Combine(folder, $"{stem} ({n}){extension}");
        }

        return candidate;
    }

    /// <summary>Opens the picture the caret is standing on, if the caret is standing on one.</summary>
    public void OpenPictureAtCaret() => TryOpenPictureAt(rtbChat.SelectionStart);

    /// <summary>
    /// Opens the picture written at <paramref name="caret"/>, and says whether there was one.
    /// </summary>
    /// <remarks>
    /// Separate from the mouse so the rule - transcript furniture that is not a picture does nothing - is
    /// answerable without a caret, a click and a window. Not by text: two photos from the same person in the
    /// same minute have the same headline, and the file name is in the caption but not in the thumbnail, so any
    /// text match gets one of them wrong. The span the entry was written into is the only thing that knows.
    /// </remarks>
    public bool TryOpenPictureAt(int caret)
    {
        foreach (var span in pictures)
        {
            if (caret >= span.Start && caret < span.End)
            {
                OpenPicture(span.Picture);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Shows the picture in its own window (F14): bigger than the transcript can be, and scalable, because the
    /// thing a person double-clicks a thumbnail for is to see the photo.
    /// </summary>
    /// <remarks>
    /// Nothing is written to disk to do it. The bytes are already here, in the message - until now looking at a
    /// photo in a transcript also put a copy of it in a cache folder and asked the system's photo viewer, which
    /// is two surprises (a file nobody asked for, and a window that is not this program's) in the path of a
    /// double click. Getting the file out is still possible and is now a thing the person asks for, on the
    /// viewer's own Save button.
    /// <para>
    /// One window per picture, and a second double click on the same photo raises the window that is already
    /// open. A person comparing two photos wants two windows; a person who double-clicked the same one three
    /// times wants one.
    /// </para>
    /// </remarks>
    protected virtual void OpenPicture(PictureMessage picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        var already = viewers.FirstOrDefault(v => v.Picture.Id == picture.Id);

        if (already is not null)
        {
            if (already.WindowState == FormWindowState.Minimized)
            {
                already.WindowState = FormWindowState.Normal;
            }

            already.Activate();
            return;
        }

        try
        {
            var viewer = NewPictureViewer(picture);

            viewers.Add(viewer);
            viewer.FormClosed += (_, _) => viewers.Remove(viewer);

            // Owned, not modal: the room keeps talking while the photo is up, and a person reading a photo
            // while three people argue about it is the normal case, not the exception.
            viewer.Show(this);

            SetStatus($"showing {picture.FileName}");
        }
        catch (Exception ex) when (ex is IOException or OutOfMemoryException or ArgumentException or NotSupportedException)
        {
            // GDI+ reports an undecodable payload through most of those. The line stays in the transcript
            // either way, and the bar says what happened instead of the window dying on a click.
            SetStatus($"could not open {picture.FileName}: {ex.Message}");
        }
    }

    /// <summary>The window a picture is shown in. Overridden only so a test can build a subclass of it.</summary>
    protected virtual PictureViewerForm NewPictureViewer(PictureMessage picture) => new(picture);

    // ------------------------------------------------------------- transcript

    /// <summary>
    /// Shows an arriving message if it belongs to the room on screen. The service keeps transcripts
    /// for every room, so a notice about a room this window is not in - the one the host just closed,
    /// for instance - would otherwise be drawn as though it belonged here.
    /// </summary>
    private void ShowIncoming(Message message)
    {
        if (CurrentRoom is not { } room
            || !string.Equals(message.RoomName, room.Name, StringComparison.Ordinal))
        {
            return;
        }

        Append(message);
    }

    /// <summary>
    /// Appends one line to the transcript. Incoming traffic arrives here through
    /// <see cref="IChatService.MessageReceived"/>; something this window sent is appended directly,
    /// because there is no echo back from the service.
    /// </summary>
    protected void Append(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var start = TranscriptEnd;
        var content = TranscriptStyle.ContentColorFor(message);

        // F13: the headline on its line, the message on the next, in another colour. The line break belongs to
        // the content's colour rather than the headline's, so that selecting an entry and copying it out of the
        // transcript carries the message in one colour rather than a stray blue newline.
        WriteRun(TranscriptStyle.Headline(message), TranscriptStyle.HeadlineColorFor(message));
        WriteRun(Environment.NewLine, content);
        WriteRun(TranscriptStyle.Content(message), content);

        if (message is PictureMessage picture)
        {
            // The entry's lines come first and the thumbnail under them, so a picture that turns out not
            // to be a picture at all still leaves its entry in the transcript (see RtfPicture).
            WriteRun(Environment.NewLine, content);

            if (RtfPicture.InlineFor(picture) is { } rtf)
            {
                AppendRtf(rtf);
            }

            pictures.Add(new PictureSpan(start, TranscriptEnd, picture));
        }
        else if (message is FileMessage file)
        {
            // A file's entry is a thing a person may want to act on later - "put that in my folder" - and the
            // transcript is where they saw it, so the entry is recorded the same way a picture's is.
            files.Add(new FileSpan(start, TranscriptEnd, file));
        }

        WriteRun(Environment.NewLine, content);
        rtbChat.ScrollToCaret();

        if (message.Kind == MessageKind.JOIN_LEAVE_NOTICE)
        {
            ShowMembers();
        }
    }

    /// <summary>
    /// Appends text at the end of the transcript in one colour. Every byte the transcript is made of comes
    /// through here, which is what makes “every message has a headline in one colour and its content in
    /// another” a property of the window rather than of the messages that happened to be sent.
    /// </summary>
    private void WriteRun(string text, Color color)
    {
        rtbChat.SelectionStart = rtbChat.TextLength;
        rtbChat.SelectionLength = 0;
        rtbChat.SelectionColor = color;
        rtbChat.AppendText(text);
    }

    /// <summary>
    /// Empties the transcript and forgets where the pictures in it were. Both halves are needed: an entry that
    /// is no longer on screen but is still in <see cref="pictures"/> would be opened by the click that lands on
    /// whatever replaced it at that position.
    /// </summary>
    private void ClearTranscript()
    {
        rtbChat.Clear();
        pictures.Clear();
        files.Clear();
        transfers.Clear();
        savedTo.Clear();
    }

    /// <summary>
    /// Writes RTF into the transcript at the end.
    /// <para>
    /// The transcript is read-only, and a read-only RichTextBox refuses an insertion made through
    /// <see cref="RichTextBox.SelectedRtf"/> without complaining - which is how a picture can be sent,
    /// broadcast, received, decoded and then simply not appear. <see cref="RichTextBox.ReadOnly"/> is a
    /// promise about the keyboard rather than about this code, so it is lifted for the insertion and put
    /// back whether or not the insertion worked.
    /// </para>
    /// </summary>
    private void AppendRtf(string rtf)
    {
        var wasReadOnly = rtbChat.ReadOnly;
        rtbChat.ReadOnly = false;

        try
        {
            rtbChat.SelectionStart = rtbChat.TextLength;
            rtbChat.SelectionLength = 0;
            rtbChat.SelectedRtf = rtf;
        }
        finally
        {
            rtbChat.ReadOnly = wasReadOnly;
        }
    }

    // ------------------------------------------------------------------ menu

    /// <summary>
    /// Room management is host-only; clients see the menu items but cannot use them. The buttons in the header
    /// are greyed the same way (<see cref="HostForm"/>), so there is no way to ask for a room that ends in
    /// nothing happening, and no way that is not visible.
    /// </summary>
    protected void SetRoomEditingEnabled(bool enabled)
    {
        newRoomMenuItem.Enabled = enabled;
        renameMenuItem.Enabled = enabled;
        deleteRoomMenuItem.Enabled = enabled;
    }

    /// <summary>
    /// Edit ▸ New room. The base form says who can: a client is looking at somebody else's rooms, and a
    /// request it is not allowed to make is refused in the bar rather than swallowed.
    /// </summary>
    protected virtual void NewRoomMenuItem_Click(object? sender, EventArgs e)
        => SetStatus("only the host can create rooms");

    protected virtual void RenameMenuItem_Click(object? sender, EventArgs e)
        => SetStatus("only the host can rename rooms");

    /// <summary>Edit ▸ Delete room. Same refusal, same reason.</summary>
    protected virtual void DeleteRoomMenuItem_Click(object? sender, EventArgs e)
        => SetStatus("only the host can delete rooms");

    private void ClearChatMenuItem_Click(object? sender, EventArgs e) => ClearTranscript();

    private void AboutMenuItem_Click(object? sender, EventArgs e)
        => MessageBox.Show(
            this,
            // Say what this build actually does. Until M4 the radio is never touched, and a demo where
            // the About box claims Bluetooth while the windows talk to 127.0.0.1 is a demo that ends in
            // an argument about whether anything was proven. M4: name the transport in use here.
            "Lantern\nGroup chat between Windows machines.\n\nDevelopment build: messages travel over a "
                + "local loopback transport (127.0.0.1), not Bluetooth yet.",
            "About Lantern",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

    private void QuitMenuItem_Click(object? sender, EventArgs e) => OnQuitRequested();

    /// <summary>File ▸ Quit. The asking happens in <see cref="OnFormClosing"/>, the stopping in <see cref="OnFormClosed"/>.</summary>
    protected virtual void OnQuitRequested() => Close();

    /// <summary>
    /// How long this window will stand still for a chat that is packing up. Half a second, because that is
    /// about how long a window that has already been told to close may be seen thinking before it looks
    /// broken — and a Bluetooth channel routinely wants more than that to say goodbye.
    /// </summary>
    private static readonly TimeSpan ClosingGrace = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Stopping the chat, which this window started on the way out and which may still be going when the
    /// window has gone. Whoever owns this window (the start screen, or the process on its way out) is the
    /// one that decides how long to wait for it; the window itself does not.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Task StoppingChat { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Why this window closed, in the words the start screen repeats back: <c>null</c> when the person
    /// asked for it, and the reason when something else did — losing a host is not the same news as
    /// leaving, and a start screen that comes back silently looks like a start screen that was never left.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? ClosingExplanation { get; protected set; }

    /// <summary>
    /// Whether the user really means it. The host window asks, because everybody else is connected to
    /// it.
    /// </summary>
    protected virtual bool ConfirmClosing() => true;

    /// <summary>
    /// The question is asked here rather than on the menu item, because the ✕ on the title bar and
    /// Alt+F4 come through here as well - and a host closing the window disconnects everybody, which is
    /// not something a keystroke should be able to skip past.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmClosing())
        {
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// The close box is as much an exit as File ▸ Quit, and a chat still running behind a closed window
    /// is a chat the user cannot stop: clients would keep their connection to a host with no face.
    /// </summary>
    /// <remarks>
    /// The chat is stopped here, on the way out, because a chat still running behind a closed window is a
    /// chat nobody can stop — and it is waited on only as long as <see cref="ClosingGrace"/>. That is the
    /// whole of the difference between a close that feels instant and a close that feels broken: the work
    /// is not skipped, it is handed over (see <see cref="StoppingChat"/>), and whoever owns this window
    /// decides whether the process may go before it is finished. The start screen waits before it lets a
    /// new chat start, and waits again before the program exits, so the host's room list still lands in
    /// the file (F9).
    /// </remarks>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        // Off the UI thread on purpose: awaiting on this thread would need the message loop that is
        // already on its way out, and blocking it directly would deadlock against the same loop.
        var service = Service;
        StoppingChat = Task.Run(async () =>
        {
            try
            {
                await service.ShutdownAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The window is going away and there is nobody left to tell. The service reports what it
                // can while it is still up; a shutdown that cannot finish is not worth throwing over.
            }
        });

        try
        {
            StoppingChat.Wait(ClosingGrace);
        }
        catch (Exception)
        {
            // Already swallowed inside the task; a faulted shutdown must not be thrown at a window that is
            // on its way out of the screen.
        }
    }

    /// <summary>Scenario 6: the host has gone, so this window has nothing left to do.</summary>
    /// <remarks>
    /// The telling is split out for the same reason as the dialogs on the other windows: a modal sits in
    /// the middle of this method and stops a test reaching the rest of it. What is under test is that the
    /// status bar says why, and that the window steps aside rather than sitting there offering a Send
    /// button that can only fail.
    /// </remarks>
    protected virtual void OnConnectionLost(Exception error)
    {
        // The state first and the reason after it: the reason is for this moment and the state is for whoever
        // looks at the bar afterwards, when the box has been acknowledged and this window has stepped aside.
        SetStatus($"{ConnectionLostState} — {error.Message}");
        ClosingExplanation = error.Message;
        ShowConnectionLost(error);
        Close();
    }

    /// <summary>Telling the person, in a box they have to acknowledge.</summary>
    protected virtual void ShowConnectionLost(Exception error) => ConfirmDialog.ConnectionLost(this);
}
