using System.Drawing.Imaging;
using Lantern.Domain;

namespace Lantern.UI;

/// <summary>
/// The window a picture is shown in (F14): the photo at a size the transcript could never offer, and at a
/// size the person chooses.
/// <para>
/// A thumbnail in a transcript answers "was a photo sent?" and nothing else. The thing a person double-clicks
/// it for is the photo - can I read the sign in it, whose face is that, what is written on the slide. So this
/// window opens large, scales the picture in steps and continuously, and gets out of the way: the chat keeps
/// running behind it, because a photo arriving in a group is usually followed by three messages about it.
/// </para>
/// <para>
/// Nothing here writes a file. The bytes are already in the message, so looking at a picture needs no
/// temporary copy of it and no other program; the Save button is where a copy onto this machine is asked for,
/// and it is a button rather than a side effect.
/// </para>
/// </summary>
/// <remarks>
/// Not sealed, for one reason: <see cref="AskForSavePath"/>. A window that asks a person for a file name with
/// a modal in the middle of it cannot be shown to save anything by a test, and the saving is the part worth
/// testing. The asking is therefore one method, and a test answers it.
/// </remarks>
public partial class PictureViewerForm : Form
{
    /// <summary>The smallest a picture is drawn. Below this it is a smudge and the pixels are a lie.</summary>
    public const double MinimumZoom = 0.10;

    /// <summary>The largest. Eight times the photo, which on a 4000 pixel original is a screenful of one eyelash.</summary>
    public const double MaximumZoom = 8.00;

    /// <summary>How much one step in or out is. Comfortably visible, small enough that walking it does not overshoot.</summary>
    public const double ZoomStep = 1.25;

    private readonly PictureMessage picture;

    /// <summary>The decoded photo, or null when the bytes are not a picture. Owned by this window.</summary>
    private Image? image;

    private double zoom = 1.0;
    private bool fitted = true;

    /// <summary>
    /// For the design surface, which builds the window in order to draw it and has no photo to hand over -
    /// so it is given one, drawn here, rather than an empty grey box that tells nobody whether the layout
    /// works. Anywhere else this throws; a window for a picture nobody sent is a window with nothing in it.
    /// See <see cref="DesignTime"/>.
    /// </summary>
    /// <remarks>
    /// It calls <c>InitializeComponent</c> itself rather than chaining to the other constructor, like every
    /// other window in this project: the designer looks for that call in the constructor it uses, and both
    /// constructors then meet in <see cref="SetUp"/>.
    /// </remarks>
    public PictureViewerForm()
    {
        picture = DesignTime.Picture(nameof(PictureViewerForm));

        InitializeComponent();

        SetUp();
    }

    public PictureViewerForm(PictureMessage picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        this.picture = picture;

        InitializeComponent();

        SetUp();
    }

    /// <summary>The message this window shows the picture of.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public PictureMessage Picture => picture;

    /// <summary>Whether there is a photo to draw at all - false when the bytes turned out not to be a picture.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool HasPicture => image is not null;

    /// <summary>How big the picture is being drawn, 1.0 being its own pixels.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Zoom => zoom;

    /// <summary>Whether the size is being chosen to suit the window rather than by the person looking.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsFitted => fitted;

    /// <summary>What the window is showing, or what went wrong, in the bar along the bottom.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string StatusText => lblStatus.Text ?? string.Empty;

    /// <summary>The label that reads the scale out loud, as a percentage.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string ZoomText => lblZoom.Text ?? string.Empty;

    // ------------------------------------------------------------------ scaling

    /// <summary>
    /// Keeps a scale inside the range this window will draw at. Public and static because "what happens when
    /// somebody holds the wheel down at the bottom of the range" is a rule about numbers, not about a window.
    /// </summary>
    public static double ClampZoom(double value)
        => double.IsFinite(value) ? Math.Clamp(value, MinimumZoom, MaximumZoom) : 1.0;

    /// <summary>
    /// The scale that shows a <c>width × height</c> picture inside an <c>areaWidth × areaHeight</c> space,
    /// aspect kept.
    /// </summary>
    /// <remarks>
    /// It never enlarges above 1.0. A photo smaller than the window drawn to fill the window is the same photo
    /// with the same detail, made of four times as many made-up pixels; the person who wants that is one click
    /// on the zoom button away from it, and the default should be the honest one.
    /// </remarks>
    public static double FitScale(int width, int height, int areaWidth, int areaHeight)
    {
        if (width <= 0 || height <= 0 || areaWidth <= 0 || areaHeight <= 0)
        {
            return 1.0;
        }

        return ClampZoom(Math.Min(1.0, Math.Min((double)areaWidth / width, (double)areaHeight / height)));
    }

    /// <summary>One step in, wherever that lands - and the fit stops being the window's choice, because it is now somebody's.</summary>
    public void ZoomIn() => ApplyZoom(zoom * ZoomStep);

    /// <summary>One step out.</summary>
    public void ZoomOut() => ApplyZoom(zoom / ZoomStep);

    /// <summary>The picture's own pixels, one to one.</summary>
    public void ActualSize()
    {
        fitted = false;
        ApplyZoom(1.0);
    }

    /// <summary>The picture at whatever size suits the window - which is how it opens, and where pressing F goes back.</summary>
    public void FitToWindow()
    {
        fitted = true;
        Apply();
    }

    /// <summary>Fit, or one-to-one, whichever the window is not doing. Double-clicking the picture does this.</summary>
    public void ToggleFit()
    {
        if (fitted)
        {
            ActualSize();
        }
        else
        {
            FitToWindow();
        }
    }

    private void ApplyZoom(double value)
    {
        fitted = false;
        zoom = ClampZoom(value);
        Apply();
    }

    /// <summary>
    /// Draws the picture at the current scale: the picture box becomes the scaled size inside a panel that
    /// scrolls, so zooming past the window gives scrollbars rather than a cropped photo, and a picture smaller
    /// than the window sits in the middle of it rather than in the top-left corner.
    /// </summary>
    private void Apply()
    {
        if (image is null)
        {
            lblZoom.Text = "—";
            return;
        }

        var area = pnlImage.ClientSize;

        if (fitted)
        {
            zoom = FitScale(image.Width, image.Height, area.Width, area.Height);
        }

        var width = Math.Max(1, (int)Math.Round(image.Width * zoom));
        var height = Math.Max(1, (int)Math.Round(image.Height * zoom));

        // The control is stretched rather than the bitmap resized: a 4000 × 3000 photo at 800% is a
        // 32000 × 24000 bitmap, which is a gigabyte of memory spent on one eyelash. Stretching a bitmap the
        // window already holds costs nothing per step, so the wheel can be held down.
        picImage.Size = new Size(width, height);
        picImage.Location = new Point(
            Math.Max(0, (area.Width - width) / 2),
            Math.Max(0, (area.Height - height) / 2));

        lblZoom.Text = $"{(int)Math.Round(zoom * 100)}%";
    }

    // --------------------------------------------------------------- the rest

    private void SetUp()
    {
        AppIcon.Apply(this);

        Text = picture.FileName.Length == 0 ? "Picture" : picture.FileName;

        image = picture.ToImage();

        // And here is the picture. Everything else in this window - the fit, the zoom, the percentage, the
        // dimensions in the bar - reads from `image` and would have gone on working perfectly with nothing on
        // the screen, which is exactly what it did: the control that paints was never given the picture, so the
        // window scaled, sized itself and reported a zoom over an empty grey panel. A test that looks at the
        // pixels, below, is the reason that cannot happen again quietly.
        picImage.Image = image;

        var zoomable = image is not null;
        btnZoomIn.Enabled = zoomable;
        btnZoomOut.Enabled = zoomable;
        btnFit.Enabled = zoomable;
        btnActual.Enabled = zoomable;

        lblStatus.Text = image is null
            ? $"{picture.FileName} did not arrive as a picture"
            : $"{picture.SenderNickname} · {picture.SentAt:yyyy-MM-dd HH:mm} · {image.Width} × {image.Height}";

        StartLarge();
        FitToWindow();
    }

    /// <summary>
    /// Opens most of the screen. A window whose whole job is showing a photo bigger than a thumbnail should
    /// not open at the size of a thumbnail; anybody who wants it smaller has a title bar to drag.
    /// </summary>
    private void StartLarge()
    {
        var area = (Screen.PrimaryScreen?.WorkingArea) ?? new Rectangle(0, 0, 1280, 1024);

        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        Width = Math.Max(MinimumSize.Width, (int)Math.Round(area.Width * 0.8));
        Height = Math.Max(MinimumSize.Height, (int)Math.Round(area.Height * 0.8));
    }

    private void ZoomInButton_Click(object? sender, EventArgs e) => ZoomIn();

    private void ZoomOutButton_Click(object? sender, EventArgs e) => ZoomOut();

    private void FitButton_Click(object? sender, EventArgs e) => FitToWindow();

    private void ActualButton_Click(object? sender, EventArgs e) => ActualSize();

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (picture.Data.Length == 0)
        {
            SetStatus("there is nothing here to save");
            return;
        }

        var path = AskForSavePath();

        if (path is null)
        {
            SetStatus("kept it in the chat only");
            return;
        }

        try
        {
            File.WriteAllBytes(path, picture.Data);
            SetStatus($"saved to {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus($"could not save: {ex.Message}");
        }
    }

    /// <summary>
    /// Where to put the file, or null for "nowhere". A method rather than a dialog inlined in the handler, so
    /// that a test can answer instead of a person - the same reason the room dialogs are methods
    /// (<see cref="HostForm.AskForRoomName"/>), and the saving itself is what is worth testing.
    /// </summary>
    protected virtual string? AskForSavePath()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save this picture",
            FileName = picture.FileName.Length == 0 ? "picture.png" : picture.FileName,
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void SetStatus(string message) => lblStatus.Text = message;

    private void Viewer_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Add or Keys.Oemplus when !e.Control:
                ZoomIn();
                e.Handled = true;
                break;
            case Keys.Subtract or Keys.OemMinus when !e.Control:
                ZoomOut();
                e.Handled = true;
                break;
            case Keys.D0 or Keys.NumPad0 when !e.Control:
                ActualSize();
                e.Handled = true;
                break;
            case Keys.F when !e.Control:
                FitToWindow();
                e.Handled = true;
                break;
            case Keys.Enter when !e.Control:
                ToggleFit();
                e.Handled = true;
                break;
            case Keys.Escape:
                Close();
                e.Handled = true;
                break;
            case Keys.S when e.Control:
                SaveButton_Click(sender, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void Viewer_MouseWheel(object? sender, MouseEventArgs e)
    {
        // The wheel over the picture zooms it, which is what a person holding Ctrl and rolling is expecting
        // even if they forgot the Ctrl - there is nothing else in this window to scroll sideways, and the
        // panel still scrolls by its own scrollbars when the picture is larger than the window.
        if (image is null)
        {
            return;
        }

        if (e.Delta > 0)
        {
            ZoomIn();
        }
        else if (e.Delta < 0)
        {
            ZoomOut();
        }
    }

    private void Picture_MouseDoubleClick(object? sender, MouseEventArgs e) => ToggleFit();

    /// <summary>Re-fits while the window is being dragged wider, as long as nobody has chosen a scale themselves.</summary>
    private void ImagePanel_Resized(object? sender, EventArgs e)
    {
        if (fitted)
        {
            Apply();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // The photo is this window's to look after: it was decoded here, and there is no other copy of the
        // pixels anywhere - only the bytes, which the message owns.
        // The control is told first: a PictureBox holding a disposed image is an exception on the next paint,
        // and a window that is closing could easily paint once more on the way out.
        picImage.Image = null;
        image?.Dispose();
        image = null;

        base.OnFormClosed(e);
    }
}
