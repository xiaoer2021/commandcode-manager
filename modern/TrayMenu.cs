using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CommandCodeManager;
namespace ModernHost {
public sealed class TrayMenuRenderer : ToolStripProfessionalRenderer {
    private static readonly Color Ink = Color.FromArgb(30, 45, 69), Muted = Color.FromArgb(78, 104, 132), Accent = Color.FromArgb(6, 74, 160), Surface = Color.FromArgb(252, 253, 255), Border = Color.FromArgb(176, 194, 216), Divider = Color.FromArgb(214, 226, 239);
    public TrayMenuRenderer() : base(new TrayColorTable()) { RoundedEdges = false; }
    private static GraphicsPath Round(Rectangle rectangle, int radius) {
        GraphicsPath path = new GraphicsPath(); int diameter = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90); path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using (SolidBrush brush = new SolidBrush(Surface)) e.Graphics.FillRectangle(brush, e.AffectedBounds); }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (Pen pen = new Pen(Border)) using (GraphicsPath path = Round(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 8)) g.DrawPath(pen, path);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
        TrayUsageBarItem bars = e.Item as TrayUsageBarItem;
        if (bars != null) { TrayUsagePaint.Draw(e.Graphics, new Rectangle(Point.Empty, e.Item.Size), bars.Month, bars.Week, bars.Five, bars.Unit, bars.Limited); return; }
        if (!e.Item.Selected || !e.Item.Enabled || e.Item is ToolStripSeparator) return;
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = Round(new Rectangle(5, 1, e.Item.Width - 10, e.Item.Height - 2), 6)) using (SolidBrush brush = new SolidBrush(Color.FromArgb(232, 242, 255))) g.FillPath(brush, path);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
        e.TextColor = !e.Item.Enabled ? Muted : e.Item.Selected ? Accent : Ink;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
        using (Pen pen = new Pen(Divider)) e.Graphics.DrawLine(pen, 13, e.Item.Height / 2, e.Item.Width - 13, e.Item.Height / 2);
    }
}
public sealed class TrayColorTable : ProfessionalColorTable {
    public override Color MenuItemSelected { get { return Color.FromArgb(232, 242, 255); } }
    public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(232, 242, 255); } }
    public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(232, 242, 255); } }
    public override Color ToolStripDropDownBackground { get { return Color.FromArgb(252, 253, 255); } }
    public override Color ImageMarginGradientBegin { get { return Color.FromArgb(252, 253, 255); } }
    public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(252, 253, 255); } }
    public override Color ImageMarginGradientEnd { get { return Color.FromArgb(252, 253, 255); } }
    public override Color MenuBorder { get { return Color.FromArgb(176, 194, 216); } }
    public override Color SeparatorDark { get { return Color.FromArgb(214, 226, 239); } }
    public override Color SeparatorLight { get { return Color.FromArgb(214, 226, 239); } }
}
public sealed class TrayUsageBarItem : ToolStripMenuItem {
    public WindowUsage Month, Week, Five;
    public string Unit;
    public bool? Limited;
    public TrayUsageBarItem() { AutoSize = false; Size = new Size(252, 86); Text = ""; Enabled = false; Padding = Padding.Empty; }
}
public static class TrayUsagePaint {
    public static void Draw(Graphics g, Rectangle bounds, WindowUsage month, WindowUsage week, WindowUsage five, string unit, bool? limited) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        WindowUsage[] windows = { month, week, five };
        string[] labels = { "月度", "每周", "5 小时" };
        using (Font labelFont = new Font("Microsoft YaHei UI", 9f)) using (Font valueFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)) using (Font smallFont = new Font("Microsoft YaHei UI", 9f)) {
            for (int index = 0; index < 3; index++) {
                int y = bounds.Y + 6 + index * 26;
                TextRenderer.DrawText(g, labels[index], labelFont, new Rectangle(bounds.X + 12, y, 48, 16), Color.FromArgb(78, 104, 132), TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.SingleLine);
                WindowUsage window = windows[index];
                if (limited == false) {
                    TextRenderer.DrawText(g, "不限窗口", smallFont, new Rectangle(bounds.X + 62, y, bounds.Width - 74, 16), Color.FromArgb(120, 140, 160), TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.SingleLine);
                    continue;
                }
                if (window == null || !window.Used.HasValue || !window.Cap.HasValue || window.Cap.Value <= 0) {
                    TextRenderer.DrawText(g, "暂无数据", smallFont, new Rectangle(bounds.X + 62, y, bounds.Width - 74, 16), Color.FromArgb(120, 140, 160), TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.SingleLine);
                    continue;
                }
                double percent = Math.Max(0, Math.Min(100, window.Used.Value / window.Cap.Value * 100));
                bool ratio = unit == "%" || unit == "percent";
                
                int left = bounds.X + 62, right = bounds.X + bounds.Width - 62, trackWidth = Math.Max(20, right - left);
                Rectangle track = new Rectangle(left, y + 16, trackWidth, 5);
                using (GraphicsPath path = Round(track, 3)) using (SolidBrush brush = new SolidBrush(Color.FromArgb(214, 226, 241))) g.FillPath(brush, path);
                int fillWidth = Math.Max(2, (int)Math.Round(trackWidth * percent / 100d));
                Rectangle fill = new Rectangle(left, y + 16, Math.Min(trackWidth, fillWidth), 5);
                Color tone = percent >= 95 ? Color.FromArgb(227, 96, 90) : percent >= 80 ? Color.FromArgb(211, 144, 35) : Color.FromArgb(17, 128, 238);
                using (GraphicsPath path = Round(fill, 3)) using (SolidBrush brush = new SolidBrush(tone)) g.FillPath(brush, path);
                TextRenderer.DrawText(g, percent.ToString("0") + "%", valueFont, new Rectangle(bounds.X + bounds.Width - 56, y, 44, 16), tone, TextFormatFlags.NoPadding | TextFormatFlags.Right | TextFormatFlags.SingleLine);
            }
        }
    }
    private static GraphicsPath Round(Rectangle rectangle, int radius) {
        GraphicsPath path = new GraphicsPath(); int diameter = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90); path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
}
}