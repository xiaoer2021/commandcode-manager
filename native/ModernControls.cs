using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace CommandCodeManager {
public static class PaintStyle {
    public static readonly Color Ink = Color.FromArgb(24, 34, 51), Muted = Color.FromArgb(68, 82, 103), Blue = Color.FromArgb(33, 88, 205), Line = Color.FromArgb(173, 187, 207);
    public static GraphicsPath Round(Rectangle rectangle, int radius) {
        GraphicsPath path = new GraphicsPath(); int diameter = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90); path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90); path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }
    public static void Box(Graphics graphics, Rectangle rectangle, Color fill, Color border, int radius) {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = Round(rectangle, radius)) { using (SolidBrush brush = new SolidBrush(fill)) graphics.FillPath(brush, path); if (border != Color.Transparent) using (Pen pen = new Pen(border)) graphics.DrawPath(pen, path); }
    }
    public static void Text(Graphics graphics, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags) { TextRenderer.DrawText(graphics, text, font, bounds, color, flags | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis); }
}
public class ModernButton : Button {
    public bool Primary;
    private bool hover;
    public ModernButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
        Color fill = Primary ? hover ? Color.FromArgb(34, 86, 199) : PaintStyle.Blue : hover ? Color.FromArgb(237, 243, 254) : Color.White;
        PaintStyle.Box(e.Graphics, new Rectangle(1, 1, Width - 3, Height - 3), fill, Primary ? fill : PaintStyle.Line, 7);
        PaintStyle.Text(e.Graphics, Text, Font, Enabled ? Primary ? Color.White : PaintStyle.Ink : Color.FromArgb(170, 180, 195), new Rectangle(8, 0, Width - 16, Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 10, Height - 10));
    }
}
public class SummaryStrip : Control {
    private string[] values = { "0", "—", "0", "Windows 加密" };
    public SummaryStrip() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true); }
    public void UpdateValues(int count, string balance, int attention, int known) { values = new string[] { count.ToString(), balance, attention.ToString(), known + " / " + count + " 已同步" }; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent.BackColor); string[] labels = { "管理账号", "可用余额", "需要关注", "数据同步" }; int gap = 12, width = (Width - gap * 3) / 4;
        using (Font label = new Font(Font.FontFamily, 8.5f)) using (Font number = new Font(Font.FontFamily, 19, FontStyle.Bold)) using (Font info = new Font(Font.FontFamily, 12, FontStyle.Bold)) {
            for (int i = 0; i < 4; i++) { Rectangle box = new Rectangle(i * (width + gap), 1, width - 1, Height - 8); PaintStyle.Box(e.Graphics, box, Color.White, PaintStyle.Line, 10); PaintStyle.Text(e.Graphics, labels[i], label, PaintStyle.Muted, new Rectangle(box.X + 15, box.Y + 10, width - 30, 17), TextFormatFlags.SingleLine); PaintStyle.Text(e.Graphics, values[i], i == 3 ? info : number, i == 2 && values[i] != "0" ? Color.FromArgb(188, 118, 20) : PaintStyle.Ink, new Rectangle(box.X + 15, box.Y + 29, width - 30, 32), TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter); }
        }
    }
}
public class SmoothGrid : DataGridView {
    public SmoothGrid() { DoubleBuffered = true; }
}
}
