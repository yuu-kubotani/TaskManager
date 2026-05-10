using System;
using System.Drawing;
using System.Windows.Forms;
using TaskManager.Services;

namespace TaskManager.Forms
{
    public class FormMemoInput : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private TextBox txtMemo;
        public string ResultMemo { get; private set; }

        public FormMemoInput(string existingMemo, bool isDarkMode)
        {
            InitializeComponent(existingMemo);
            ThemeManager.ApplyTheme(this, isDarkMode);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try {
                int useImmersiveDarkMode = this.BackColor.R < 100 ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            } catch { }
        }

        private void InitializeComponent(string existingMemo)
        {
            this.Text = "メモ";
            this.Size = new Size(400, 300);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            txtMemo = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Top,
                Height = 210,
                Text = existingMemo
            };
            this.Controls.Add(txtMemo);

            var btnOk = new Button { Text = "保存", Location = new Point(110, 225), Size = new Size(80, 25), DialogResult = DialogResult.OK };
            btnOk.Click += (s, e) => { ResultMemo = txtMemo.Text; this.Close(); };
            this.Controls.Add(btnOk);

            var btnCancel = new Button { Text = "キャンセル", Location = new Point(200, 225), Size = new Size(80, 25), DialogResult = DialogResult.Cancel };
            btnCancel.Click += (s, e) => { this.Close(); };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
            this.ActiveControl = txtMemo;
        }
    }
}
