namespace VlessMonitor.UI;

/// <summary>Dark-themed renderer for the tray context menu.</summary>
public class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Fg : Theme.FgDim;
        base.OnRenderItemText(e);
    }

    private class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Theme.Bg3;
        public override Color MenuItemSelectedGradientBegin => Theme.Bg3;
        public override Color MenuItemSelectedGradientEnd => Theme.Bg3;
        public override Color MenuItemBorder => Theme.Bg3;
        public override Color MenuBorder => Theme.Bg3;
        public override Color ToolStripDropDownBackground => Theme.Bg;
        public override Color ImageMarginGradientBegin => Theme.Bg;
        public override Color ImageMarginGradientMiddle => Theme.Bg;
        public override Color ImageMarginGradientEnd => Theme.Bg;
        public override Color SeparatorDark => Theme.Bg3;
        public override Color SeparatorLight => Theme.Bg3;
    }
}
