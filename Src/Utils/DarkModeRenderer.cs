﻿﻿using System.Drawing;
using System.Windows.Forms;

namespace UniConsul.Forms
{
    public class DarkModeColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Color.FromArgb(80, 80, 80); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(80, 80, 80); } }
        public override Color MenuBorder { get { return Color.FromArgb(50, 50, 50); } }
        public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(60, 60, 60); } }
        public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(60, 60, 60); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(37, 37, 38); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(37, 37, 38); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(37, 37, 38); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(37, 37, 38); } }
        public override Color ButtonSelectedHighlight { get { return Color.FromArgb(80, 80, 80); } }
        public override Color ButtonPressedHighlight { get { return Color.FromArgb(100, 100, 100); } }
        public override Color ButtonCheckedHighlight { get { return Color.FromArgb(100, 100, 100); } }
        public override Color ButtonSelectedBorder { get { return Color.FromArgb(100, 100, 100); } }
        public override Color SeparatorDark { get { return Color.FromArgb(80, 80, 80); } }
        public override Color SeparatorLight { get { return Color.FromArgb(80, 80, 80); } }
    }

    public class DarkModeRenderer : ToolStripProfessionalRenderer
    {
        public DarkModeRenderer() : base(new DarkModeColorTable()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Color.White; base.OnRenderItemText(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Color.White; base.OnRenderArrow(e); }
        
        // ツールバー全体の背景を強制的にダークカラーで塗りつぶす
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Color.FromArgb(30, 30, 30))) 
            {
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
        }

        // メニューの背景を塗りつぶす
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Color.FromArgb(37, 37, 38))) 
            {
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
        }

        // ボタン（左上のアイコン等）の背景やホバー時のハイライトを強制上書き
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                using (var b = new SolidBrush(Color.FromArgb(80, 80, 80))) 
                {
                    e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
                }
            }
        }

        // プルダウン付きボタンの背景を強制上書き
        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                using (var b = new SolidBrush(Color.FromArgb(80, 80, 80))) 
                {
                    e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
                }
            }
        }
    }
}
