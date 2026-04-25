using System;
using System.Drawing;
using System.Windows.Forms;
using TaskManager.Models;

namespace TaskManager.Forms
{
    public class FormProjectInput : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        private ProjectItem _project;
        
        private TextBox _txtName;
        private DateTimePicker _dtpDue;
        private ComboBox _cmbNotify;
        private Panel _pnlColor;
        private CheckBox _chkAutoArchive;

        public FormProjectInput(ProjectItem project)
        {
            _project = project;

            this.Text = "プロジェクトのプロパティ";
            this.Size = new Size(450, 360);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            var lblName = new Label { Text = "プロジェクト名:", Location = new Point(15, 15), AutoSize = true };
            _txtName = new TextBox { Location = new Point(15, 35), Size = new Size(400, 25), Text = _project.ProjectName };

            var lblDue = new Label { Text = "プロジェクトの期日:", Location = new Point(15, 70), AutoSize = true };
            _dtpDue = new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Location = new Point(15, 90) };
            
            _dtpDue.DropDown += (s, ev) => {
                if (this.BackColor.R >= 100) return;
                IntPtr hMonthCal = SendMessage(_dtpDue.Handle, 0x1008, IntPtr.Zero, IntPtr.Zero);
                if (hMonthCal != IntPtr.Zero) {
                    SetWindowTheme(hMonthCal, "", "");
                    int bg = ColorTranslator.ToWin32(Color.FromArgb(30, 30, 30));
                    int fg = ColorTranslator.ToWin32(Color.White);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)0, (IntPtr)bg);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)1, (IntPtr)fg);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)2, (IntPtr)ColorTranslator.ToWin32(Color.FromArgb(45, 45, 48)));
                    SendMessage(hMonthCal, 0x1006, (IntPtr)3, (IntPtr)fg);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)4, (IntPtr)bg);
                    SendMessage(hMonthCal, 0x1006, (IntPtr)5, (IntPtr)ColorTranslator.ToWin32(Color.Gray));
                }
            };

            DateTime due;
            if (!string.IsNullOrEmpty(_project.ProjectDueDate) && DateTime.TryParse(_project.ProjectDueDate, out due)) {
                _dtpDue.Checked = true; _dtpDue.Value = due;
            } else { _dtpDue.Checked = false; }

            var lblNotify = new Label { Text = "期日の通知設定:", Location = new Point(220, 70), AutoSize = true };
            _cmbNotify = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(220, 90), Width = 150 };
            _cmbNotify.Items.AddRange(new[] { "全体設定に従う", "通知しない", "当日", "1日前", "前の営業日", "3日前", "1週間前" });
            _cmbNotify.SelectedItem = _project.Notification ?? "全体設定に従う";

            var lblColor = new Label { Text = "タイムラインの色:", Location = new Point(15, 130), AutoSize = true };
            _pnlColor = new Panel { Location = new Point(15, 150), Size = new Size(100, 25), BorderStyle = BorderStyle.FixedSingle };
            try { _pnlColor.BackColor = ColorTranslator.FromHtml(_project.ProjectColor); } catch { _pnlColor.BackColor = Color.LightGray; }
            var btnColor = new Button { Text = "色を選択...", Location = new Point(120, 149), Size = new Size(80, 27) };
            btnColor.Click += (s, e) => {
                using (var cd = new ColorDialog { Color = _pnlColor.BackColor }) {
                    if (cd.ShowDialog() == DialogResult.OK) _pnlColor.BackColor = cd.Color;
                }
            };

            _chkAutoArchive = new CheckBox { Text = "このプロジェクトの完了済みタスクを自動アーカイブする", Location = new Point(15, 190), AutoSize = true, Checked = _project.AutoArchiveTasks };

            var btnSave = new Button { Text = "保存", Location = new Point(120, 260), Size = new Size(80, 25) };
            var btnCancel = new Button { Text = "キャンセル", Location = new Point(230, 260), Size = new Size(80, 25) };

            btnSave.Click += BtnSave_Click;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            this.Controls.AddRange(new Control[] { lblName, _txtName, lblDue, _dtpDue, lblNotify, _cmbNotify, lblColor, _pnlColor, btnColor, _chkAutoArchive, btnSave, btnCancel });
            this.AcceptButton = btnSave; this.CancelButton = btnCancel;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            bool isDark = this.BackColor.R < 100;

            try {
                int useImmersiveDarkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }

            this.BackColor = isDark ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
            FixThemeRecursively(this, isDark);
        }

        private void FixThemeRecursively(Control parent, bool isDark)
        {
            Color formBg = isDark ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
            Color surfaceBg = isDark ? Color.FromArgb(30, 30, 30) : SystemColors.Window;
            Color fg = isDark ? Color.White : SystemColors.ControlText;

            foreach (Control c in parent.Controls)
            {
                if (c is Panel && c != _pnlColor || c is GroupBox) c.BackColor = formBg;
                else if (c is TextBox || c is ComboBox) {
                    c.BackColor = surfaceBg; c.ForeColor = fg;
                    var cmb = c as ComboBox;
                    if (cmb != null) cmb.FlatStyle = FlatStyle.Flat;
                    var txt = c as TextBox;
                    if (txt != null) txt.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is DateTimePicker) {
                    var dtp = (DateTimePicker)c;
                    dtp.BackColor = surfaceBg; dtp.ForeColor = fg;
                }
                else if (c is Button && c.Text != "色を選択...") {
                    var btn = (Button)c;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = isDark ? Color.FromArgb(80, 80, 80) : Color.DarkGray;
                    btn.BackColor = isDark ? Color.FromArgb(60, 60, 65) : SystemColors.Control;
                    btn.ForeColor = fg;
                }
                if (c is Label || c is CheckBox) c.ForeColor = fg;

                if (c.HasChildren) FixThemeRecursively(c, isDark);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bool isDark = this.BackColor.R < 100;
            try {
                int useImmersiveDarkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }
        }

        private async void BtnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text)) { MessageBox.Show("プロジェクト名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            _project.ProjectName = _txtName.Text.Trim();
            _project.ProjectDueDate = _dtpDue.Checked ? _dtpDue.Value.ToString("yyyy-MM-dd") : null;
            _project.Notification = _cmbNotify.SelectedItem.ToString();
            _project.ProjectColor = ColorTranslator.ToHtml(_pnlColor.BackColor);
            _project.AutoArchiveTasks = _chkAutoArchive.Checked;

            await ShowSaveFeedbackAndClose("プロジェクトを保存しました");
        }

        private async System.Threading.Tasks.Task ShowSaveFeedbackAndClose(string message)
        {
            foreach (Control c in this.Controls) { var b = c as Button; if (b != null) b.Enabled = false; }
            var lbl = new Label { Text = message, Font = new Font("Meiryo UI", 9, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(180, 0, 0, 0), AutoSize = true, Padding = new Padding(10) };
            this.Controls.Add(lbl);
            lbl.Location = new Point((this.ClientSize.Width - lbl.Width) / 2, (this.ClientSize.Height - lbl.Height) / 2);
            lbl.BringToFront();
            await System.Threading.Tasks.Task.Delay(1200);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
