namespace LecturePlayer.UI;

/// <summary>شباك صغير بيظهر فوق شريط التقديم: صورة من الفيديو + الوقت. مش بياخد الفوكس.</summary>
internal sealed class PreviewPopup : Form
{
    private readonly PictureBox _picture = new();
    private readonly Label _caption = new();

    public PreviewPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.Border;
        Padding = new Padding(2);

        _picture.Dock = DockStyle.Fill;
        _picture.SizeMode = PictureBoxSizeMode.Zoom;
        _picture.BackColor = Color.Black;

        _caption.Dock = DockStyle.Bottom;
        _caption.Height = 22;
        _caption.TextAlign = ContentAlignment.MiddleCenter;
        _caption.ForeColor = Theme.Text;
        _caption.BackColor = Theme.Panel;
        _caption.Font = new Font("Segoe UI", 9f);

        Controls.Add(_picture);
        Controls.Add(_caption);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    public bool HasImage => _picture.Image != null;

    /// <param name="anchor">نقطة على الشاشة (منتصف تحت الشباك)</param>
    public void ShowAt(Image? image, string caption, Point anchor, Size imageSize)
    {
        var old = _picture.Image;
        _picture.Image = image;
        if (old != null && !ReferenceEquals(old, image)) old.Dispose();
        _picture.Visible = image != null;
        _caption.Text = caption;

        int w = imageSize.Width + 4;
        int h = (image != null ? imageSize.Height : 0) + _caption.Height + 4;
        var screen = Screen.FromPoint(anchor).WorkingArea;
        int x = Math.Clamp(anchor.X - w / 2, screen.Left, screen.Right - w);
        int y = Math.Max(screen.Top, anchor.Y - h - 8);
        Bounds = new Rectangle(x, y, w, h);
        if (!Visible) Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _picture.Image?.Dispose();
        base.Dispose(disposing);
    }
}
