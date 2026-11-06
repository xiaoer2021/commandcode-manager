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
    private struct NativeRect { public int Left, Top, Right, Bottom; }
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
    private static readonly Color Ink = Color.FromArgb(30, 45, 69), Muted = Color.FromArgb(52, 73, 102), Accent = Color.FromArgb(8, 116, 235), Track = Color.FromArgb(216, 228, 242);
    public Action OpenMain = delegate { };
    public Action<Point> Moved = delegate { };
    public Action<bool> LockChanged = delegate { };
    public Action CloseRequested = delegate { };
    public Action<string> ModeChanged = delegate { };
    public Action<int, int> WidthChanged = delegate { };
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly ToolStripMenuItem barsItem = new ToolStripMenuItem("用量条");
    private readonly ToolStripMenuItem liveItem = new ToolStripMenuItem("实时消耗");
    private readonly ToolStripMenuItem openItem = new ToolStripMenuItem("打开主界面");
    private readonly ToolStripMenuItem lockItem = new ToolStripMenuItem("固定位置（锁定）");
    private readonly ToolStripMenuItem closeItem = new ToolStripMenuItem("关闭悬浮窗");
    private bool acrylic, dragging, locked;
    private Point dragStart, windowStart;
    private string mode = "bars", accountName = "当前账号";
    private int desiredWidth;
    private double? monthPercent, weekPercent, fivePercent;
    private string balance = "—", forecast = "—", detail = "暂无账号";
    private readonly StringFormat leftFormat = Align(false), rightFormat = Align(true), vLeft = VAlign(false), vRight = VAlign(true);
    private float DpiScale { get { return DeviceDpi / 96f; } }
    private int Radius { get { return (int)Math.Round(12 * DpiScale); } }
    public bool Blurred { get { return acrylic; } }
    public string Mode { get { return mode; } set { mode = string.IsNullOrEmpty(value) ? "bars" : value; UpdateModeItems(); UpdateSize(); Render(); } }
    public bool Locked { get { return locked; } set { locked = value; lockItem.Text = value ? "解除固定（可移动）" : "固定位置（锁定）"; } }
    public string Snapshot { get { return balance + "|" + forecast + "|" + detail + "|" + (monthPercent.HasValue ? monthPercent.Value.ToString("0") : "-"); } }
    public void SetWidth(int width) { float scale = DpiScale; int minimum = (int)Math.Round(280 * scale), maximum = (int)Math.Round(1200 * scale); desiredWidth = Math.Max(minimum, Math.Min(maximum, width)); if (IsHandleCreated) Width = desiredWidth; }
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
        barsItem.Padding = new Padding(8, 6, 14, 6);
        liveItem.Padding = new Padding(8, 6, 14, 6);
        openItem.Padding = new Padding(8, 6, 14, 6);
        lockItem.Padding = new Padding(8, 6, 14, 6);
        closeItem.Padding = new Padding(8, 6, 14, 6);
        barsItem.Click += (s, e) => ChangeMode("bars");
        liveItem.Click += (s, e) => ChangeMode("live");
        openItem.Click += (s, e) => OpenMain();
        lockItem.Click += (s, e) => { Locked = !Locked; LockChanged(locked); Render(); };
        closeItem.Click += (s, e) => CloseRequested();
        menu.Items.Add(barsItem);
        menu.Items.Add(liveItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(lockItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(closeItem);
        UpdateModeItems();
        DpiChanged += (s, e) => { Size = ScaledSize; Render(); };
        MouseDown += DragStart;
        MouseMove += DragMove;
        MouseUp += DragEnd;
        DoubleClick += (s, e) => OpenMain();
        MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) menu.Show(this, PointToClient(Cursor.Position)); };
        LocationChanged += (s, e) => Render();
    }
    private Size ScaledSize { get { float scale = DpiScale; int width = desiredWidth > 0 ? desiredWidth : (int)Math.Round(320 * scale); return new Size(width, (int)Math.Round((mode == "bars" ? 44 : 80) * scale)); } }
    private void UpdateSize() { if (IsHandleCreated) Size = ScaledSize; }
    private void UpdateModeItems() { barsItem.Text = (mode == "bars" ? "● " : "   ") + "用量条"; liveItem.Text = (mode == "live" ? "● " : "   ") + "实时消耗"; }
    private void ChangeMode(string value) { if (mode == value) return; mode = value; UpdateModeItems(); UpdateSize(); Render(); ModeChanged(value); }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x80000 | 0x80; return parameters; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyAcrylic(); Size = ScaledSize; Render(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Render(); }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void WndProc(ref Message message) {
        const int WM_NCHITTEST = 0x84, WM_SIZING = 0x214, WM_EXITSIZEMOVE = 0x232;
        if (message.Msg == WM_NCHITTEST) {
            base.WndProc(ref message);
            if ((int)message.Result == 1) {
                long position = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(position & 65535), (short)((position >> 16) & 65535)));
                int border = (int)Math.Round(7 * DpiScale);
                if (point.X <= border) message.Result = (IntPtr)10;
                else if (point.X >= Width - border) message.Result = (IntPtr)11;
            }
            return;
        }
        if (message.Msg == WM_SIZING) {
            NativeRect rect = (NativeRect)Marshal.PtrToStructure(message.LParam, typeof(NativeRect));
            rect.Top = Top; rect.Bottom = Top + Height;
            int handle = message.WParam.ToInt32();
            int minimum = (int)Math.Round(280 * DpiScale), maximum = (int)Math.Round(1200 * DpiScale);
            int width = rect.Right - rect.Left;
            bool fromLeft = handle == 1 || handle == 4 || handle == 7;
            if (width < minimum) { if (fromLeft) rect.Left = rect.Right - minimum; else rect.Right = rect.Left + minimum; }
            else if (width > maximum) { if (fromLeft) rect.Left = rect.Right - maximum; else rect.Right = rect.Left + maximum; }
            Marshal.StructureToPtr(rect, message.LParam, false);
            message.Result = (IntPtr)1;
            return;
        }
        if (message.Msg == WM_EXITSIZEMOVE) {
            desiredWidth = Width;
            WidthChanged(Left, Width);
        }
        base.WndProc(ref message);
    }
    private void ApplyAcrylic() {
        if (!IsHandleCreated) return;
        try { int corner = 2; DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int)); } catch { }
        try {
            AccentPolicy accent = new AccentPolicy { AccentState = 4, AccentFlags = 2, GradientColor = unchecked((int)0x60F2F6FB), AnimationId = 0 };
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
    public void UpdateBars(string account, double? month, double? week, double? five) {
        if (accountName == account && monthPercent == month && weekPercent == week && fivePercent == five) return;
        accountName = string.IsNullOrEmpty(account) ? "当前账号" : account; monthPercent = month; weekPercent = week; fivePercent = five;
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
            using (GraphicsPath path = PaintStyle.Round(new Rectangle(0, 0, width, height), Radius)) {
                using (SolidBrush fill = new SolidBrush(acrylic ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(150, 247, 250, 253))) g.FillPath(fill, path);
                using (Pen border = new Pen(Color.FromArgb(acrylic ? 60 : 36, 30, 70, 120), Math.Max(1f, DpiScale))) g.DrawPath(border, path);
            }
            if (mode == "bars") RenderBars(g, width); else RenderLive(g, width);
            PushBitmap(bitmap);
        }
    }
    private void RenderLive(Graphics g, int width) {
        float scale = DpiScale;
        int pad = (int)Math.Round(16 * scale), half = width / 2;
        using (Font valueFont = new Font("Microsoft YaHei UI", 18f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
        using (Font labelFont = new Font("Microsoft YaHei UI", 13f * scale, GraphicsUnit.Pixel))
        using (Font detailFont = new Font("Microsoft YaHei UI", 13f * scale, GraphicsUnit.Pixel))
        using (SolidBrush ink = new SolidBrush(Ink)) using (SolidBrush muted = new SolidBrush(Muted)) using (SolidBrush accent = new SolidBrush(Accent)) {
            g.DrawString(balance, valueFont, ink, new RectangleF(pad, 8 * scale, half - pad - 4, 28 * scale), leftFormat);
            g.DrawString(forecast, valueFont, accent, new RectangleF(half + 2, 8 * scale, width - half - pad - 2, 28 * scale), rightFormat);
            g.DrawString("可用余额", labelFont, muted, new RectangleF(pad, 40 * scale, half - pad - 4, 18 * scale), leftFormat);
            g.DrawString("预计可用 Token", labelFont, muted, new RectangleF(half + 2, 40 * scale, width - half - pad - 2, 18 * scale), rightFormat);
            using (Pen line = new Pen(Color.FromArgb(48, 30, 70, 120), Math.Max(1f, scale))) g.DrawLine(line, pad, 62 * scale, width - pad, 62 * scale);
            g.DrawString(detail, detailFont, muted, new RectangleF(pad, 64 * scale, width - pad * 2, 16 * scale), leftFormat);
        }
    }
    private void RenderBars(Graphics g, int width) {
        float scale = DpiScale;
        int pad = (int)Math.Round(11 * scale), top = (int)Math.Round(17 * scale), gap = (int)Math.Round(14 * scale);
        int columnWidth = Math.Max(54, (width - pad * 2 - gap * 2) / 3);
        int labelWidth = (int)Math.Round(42 * scale), valueWidth = (int)Math.Round(44 * scale);
        using (Font nameFont = new Font("Microsoft YaHei UI", 11.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
        using (Font labelFont = new Font("Microsoft YaHei UI", 11f * scale, GraphicsUnit.Pixel))
        using (Font valueFont = new Font("Microsoft YaHei UI", 11.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
        using (SolidBrush ink = new SolidBrush(Ink)) using (SolidBrush muted = new SolidBrush(Muted)) {
            g.DrawString(accountName, nameFont, ink, new RectangleF(pad, 2 * scale, width - pad * 2, 15 * scale), leftFormat);
            string[] labels = { "月度", "每周", "5 小时" };
            double?[] values = { monthPercent, weekPercent, fivePercent };
            for (int index = 0; index < 3; index++) {
                int left = pad + index * (columnWidth + gap);
                if (index > 0) using (Pen divider = new Pen(Color.FromArgb(30, 30, 70, 120), Math.Max(1f, scale))) g.DrawLine(divider, left - gap / 2, top + 2 * scale, left - gap / 2, top + 19 * scale);
                g.DrawString(labels[index], labelFont, muted, new RectangleF(left, top, labelWidth, 14 * scale), vLeft);
                if (values[index].HasValue) {
                    double percent = Math.Max(0, Math.Min(100, values[index].Value));
                    Color tone = percent >= 95 ? Color.FromArgb(227, 96, 90) : percent >= 80 ? Color.FromArgb(211, 144, 35) : Accent;
                    using (SolidBrush toneBrush = new SolidBrush(tone)) g.DrawString(percent.ToString("0") + "%", valueFont, toneBrush, new RectangleF(left + labelWidth, top, valueWidth, 14 * scale), vLeft);
                    Rectangle track = new Rectangle(left, top + (int)Math.Round(15 * scale), columnWidth, (int)Math.Round(5 * scale));
                    using (GraphicsPath path = PaintStyle.Round(track, (int)Math.Round(3 * scale))) using (SolidBrush brush = new SolidBrush(Track)) g.FillPath(brush, path);
                    if (percent > 0) {
                        int fillWidth = Math.Max((int)Math.Round(4 * scale), (int)Math.Round(columnWidth * percent / 100d));
                        Rectangle fill = new Rectangle(left, track.Y, Math.Min(columnWidth, fillWidth), track.Height);
                        using (GraphicsPath path = PaintStyle.Round(fill, (int)Math.Round(3 * scale))) using (SolidBrush brush = new SolidBrush(tone)) g.FillPath(brush, path);
                    }
                } else {
                    g.DrawString("—", valueFont, muted, new RectangleF(left + labelWidth, top, valueWidth, 14 * scale), vLeft);
                }
            }
        }
    }
    private static StringFormat Align(bool right) {
        StringFormat format = new StringFormat(StringFormatFlags.NoWrap);
        format.Trimming = StringTrimming.EllipsisCharacter;
        format.Alignment = right ? StringAlignment.Far : StringAlignment.Near;
        format.LineAlignment = StringAlignment.Near;
        return format;
    }
    private static StringFormat VAlign(bool right) {
        StringFormat format = new StringFormat(StringFormatFlags.NoWrap);
        format.Trimming = StringTrimming.EllipsisCharacter;
        format.Alignment = right ? StringAlignment.Far : StringAlignment.Near;
        format.LineAlignment = StringAlignment.Center;
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
