using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
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
}
