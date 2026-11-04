using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CommandCodeManager;
namespace ModernHost {
public sealed class MiniWindow : Form {
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
    private struct CompositionData { public int Attribute; public IntPtr Data; public int SizeOfData; }
    private struct NativePoint { public int X; public int Y; }
    private struct NativeSize { public int Width; public int Height; }
    private struct BlendFunction { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr window, ref CompositionData data);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref NativePoint destination, ref NativeSize size, IntPtr sourceDc, ref NativePoint source, int colorKey, ref BlendFunction blend, int flags);
    private static readonly Color Ink = Color.FromArgb(30, 45, 69), Muted = Color.FromArgb(74, 100, 128), Accent = Color.FromArgb(8, 116, 235);
    public Action OpenMain = delegate { };
    public Action<Point> Moved = delegate { };
    public Action<bool> LockChanged = delegate { };
    public Action CloseRequested = delegate { };
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly ToolStripMenuItem openItem = new ToolStripMenuItem("打开主界面");
    private readonly ToolStripMenuItem lockItem = new ToolStripMenuItem("锁定位置");
    private readonly ToolStripMenuItem closeItem = new ToolStripMenuItem("关闭悬浮窗");
    private bool acrylic, dragging, locked;
    private Point dragStart, windowStart;
    private string balance = "—", forecast = "—", detail = "暂无账号";
    private readonly StringFormat leftFormat = Align(false), rightFormat = Align(true);
    private float DpiScale { get { return DeviceDpi / 96f; } }
    private int Radius { get { return (int)Math.Round(12 * DpiScale); } }
    public bool Blurred { get { return acrylic; } }
    public string Snapshot { get { return balance + "|" + forecast + "|" + detail; } }
    public bool Locked { get { return locked; } set { locked = value; lockItem.Text = value ? "解锁位置（可移动）" : "锁定位置（不可移动）"; } }
    public MiniWindow() {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Size = ScaledSize;
        menu.Font = new Font("Microsoft YaHei UI", 9);
        menu.ShowImageMargin = false;
        menu.Renderer = new TrayMenuRenderer();
        menu.Padding = new Padding(0, 4, 0, 4);
        openItem.Padding = new Padding(8, 6, 14, 6);
        lockItem.Padding = new Padding(8, 6, 14, 6);
        closeItem.Padding = new Padding(8, 6, 14, 6);
        openItem.Click += (s, e) => OpenMain();
        lockItem.Click += (s, e) => { Locked = !Locked; LockChanged(locked); Render(); };
        closeItem.Click += (s, e) => CloseRequested();
        menu.Items.Add(openItem);
        menu.Items.Add(lockItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(closeItem);
        DpiChanged += (s, e) => { Size = ScaledSize; Render(); };
        MouseDown += DragStart;
        MouseMove += DragMove;
        MouseUp += DragEnd;
        DoubleClick += (s, e) => OpenMain();
        MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) menu.Show(this, PointToClient(Cursor.Position)); };
        LocationChanged += (s, e) => Render();
    }
    private Size ScaledSize { get { float scale = DpiScale; return new Size((int)Math.Round(244 * scale), (int)Math.Round(66 * scale)); } }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x80000 | 0x80; return parameters; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyAcrylic(); Size = ScaledSize; Render(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Render(); }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    private void ApplyAcrylic() {
        if (!IsHandleCreated) return;
        try { int corner = 2; DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int)); } catch { }
        try {
            AccentPolicy accent = new AccentPolicy { AccentState = 4, AccentFlags = 2, GradientColor = unchecked((int)0x99F2F6FB), AnimationId = 0 };
            int size = Marshal.SizeOf(typeof(AccentPolicy));
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try {
                Marshal.StructureToPtr(accent, pointer, false);
                CompositionData data = new CompositionData { Attribute = 19, Data = pointer, SizeOfData = size };
                acrylic = SetWindowCompositionAttribute(Handle, ref data) != 0;
            } finally { Marshal.FreeHGlobal(pointer); }
        } catch { acrylic = false; }
    }
    public void UpdateData(string balanceText, string forecastText, string detailText) {
        if (balance == balanceText && forecast == forecastText && detail == detailText) return;
        balance = balanceText; forecast = forecastText; detail = detailText;
        if (Visible) Render();
    }
    private void DragStart(object sender, MouseEventArgs e) {
        if (e.Button != MouseButtons.Left || locked) return;
        dragging = true; dragStart = Cursor.Position; windowStart = Location;
    }
    private void DragMove(object sender, MouseEventArgs e) {
        if (!dragging) return;
        Point current = Cursor.Position;
        Location = new Point(windowStart.X + current.X - dragStart.X, windowStart.Y + current.Y - dragStart.Y);
    }
    private void DragEnd(object sender, MouseEventArgs e) {
        if (!dragging) return;
        dragging = false; Moved(Location);
    }
    private void Render() {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
        int width = Width, height = Height;
        using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bitmap)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int radius = Radius;
            using (GraphicsPath path = PaintStyle.Round(new Rectangle(0, 0, width, height), radius)) {
                using (SolidBrush fill = new SolidBrush(acrylic ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(236, 247, 250, 253))) g.FillPath(fill, path);
                using (Pen border = new Pen(Color.FromArgb(acrylic ? 70 : 42, 30, 70, 120), Math.Max(1f, DpiScale))) g.DrawPath(border, path);
            }
            float scale = DpiScale;
            int pad = (int)Math.Round(14 * scale), half = width / 2;
            using (Font valueFont = new Font("Microsoft YaHei UI", 15f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font labelFont = new Font("Microsoft YaHei UI", 10.5f * scale, GraphicsUnit.Pixel))
            using (Font detailFont = new Font("Microsoft YaHei UI", 10.5f * scale, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(Ink)) using (SolidBrush muted = new SolidBrush(Muted)) using (SolidBrush accent = new SolidBrush(Accent)) {
                RectangleF left = new RectangleF(pad, 7 * scale, half - pad - 4, 24 * scale);
                RectangleF right = new RectangleF(half + 2, 7 * scale, width - half - pad - 2, 24 * scale);
                g.DrawString(balance, valueFont, ink, left, leftFormat);
                g.DrawString(forecast, valueFont, accent, right, rightFormat);
                g.DrawString("可用余额", labelFont, muted, new RectangleF(pad, 31 * scale, half - pad - 4, 15 * scale), leftFormat);
                g.DrawString("预计可用 Token", labelFont, muted, new RectangleF(half + 2, 31 * scale, width - half - pad - 2, 15 * scale), rightFormat);
                using (Pen line = new Pen(Color.FromArgb(48, 30, 70, 120), Math.Max(1f, scale))) g.DrawLine(line, pad, 49 * scale, width - pad, 49 * scale);
                g.DrawString(detail, detailFont, muted, new RectangleF(pad, 51 * scale, width - pad * 2, 15 * scale), leftFormat);
            }
            PushBitmap(bitmap);
        }
    }
    private static StringFormat Align(bool right) {
        StringFormat format = new StringFormat(StringFormatFlags.NoWrap);
        format.Trimming = StringTrimming.EllipsisCharacter;
        format.Alignment = right ? StringAlignment.Far : StringAlignment.Near;
        format.LineAlignment = StringAlignment.Near;
        return format;
    }
    private void PushBitmap(Bitmap bitmap) {
        IntPtr screenDc = GetDC(IntPtr.Zero), memoryDc = CreateCompatibleDC(screenDc), handle = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr previous = SelectObject(memoryDc, handle);
        try {
            NativeSize size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
            NativePoint source = new NativePoint { X = 0, Y = 0 }, destination = new NativePoint { X = Left, Y = Top };
            BlendFunction blend = new BlendFunction { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, 2);
        } finally {
            SelectObject(memoryDc, previous);
            DeleteObject(handle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
}
