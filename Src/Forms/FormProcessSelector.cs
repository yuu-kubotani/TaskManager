using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Linq;

namespace UniConsul.Forms
{
    public class FormProcessSelector : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public string SelectedProcessName { get; private set; }
        private ListView _listView;

        public FormProcessSelector(bool isDarkMode)
        {
            this.Text = "プロセスを選択 (起動中のアプリ)";
            this.Size = new Size(500, 400);
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            _listView.Columns.Add("プロセス名", 150);
            _listView.Columns.Add("ウィンドウ タイトル", 300);
            _listView.DoubleClick += (s, e) => ConfirmSelection();

            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            var btnOk = new Button { Text = "選択", Location = new Point(310, 10), Size = new Size(80, 30) };
            btnOk.Click += (s, e) => ConfirmSelection();
            var btnCancel = new Button { Text = "キャンセル", Location = new Point(400, 10), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
            pnlBottom.Controls.AddRange(new Control[] { btnOk, btnCancel });
            
            this.Controls.Add(_listView);
            this.Controls.Add(pnlBottom);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            LoadProcesses();
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

        private void LoadProcesses()
        {
            var allProcesses = Process.GetProcesses();
            var validProcesses = new System.Collections.Generic.List<Process>();

            foreach (var p in allProcesses)
            {
                try
                {
                    if (!string.IsNullOrEmpty(p.MainWindowTitle))
                    {
                        validProcesses.Add(p);
                    }
                }
                catch { /* 権限がないプロセスや終了済みプロセスの例外は無視してスキップする */ }
            }

            foreach (var p in validProcesses.OrderBy(p => p.ProcessName))
            {
                var lvi = new ListViewItem(p.ProcessName);
                try { lvi.SubItems.Add(p.MainWindowTitle); } catch { lvi.SubItems.Add(""); }
                _listView.Items.Add(lvi);
            }
        }

        private void ConfirmSelection()
        {
            if (_listView.SelectedItems.Count > 0) {
                SelectedProcessName = _listView.SelectedItems[0].Text;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }
}