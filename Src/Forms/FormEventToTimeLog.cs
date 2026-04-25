using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TaskManager.Models;
using TaskManager.Services;

namespace TaskManager.Forms
{
    public class FormEventToTimeLog : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private EventItem _evt;
        private List<TimeLog> _allTimeLogs;

        private DateTimePicker dtpStart, dtpEnd;
        private TextBox txtMemo;
        private RadioButton radOverwrite, radFill;

        public FormEventToTimeLog(EventItem evt, DateTime selectedDate, List<TimeLog> allTimeLogs, bool isDarkMode)
        {
            _evt = evt;
            _allTimeLogs = allTimeLogs;

            InitializeComponent();
            ThemeManager.ApplyTheme(this, isDarkMode);
            
            DateTime s;
            if (DateTime.TryParse(_evt.StartTime, out s)) dtpStart.Value = s; else dtpStart.Value = selectedDate.AddHours(9);
            DateTime e;
            if (DateTime.TryParse(_evt.EndTime, out e)) dtpEnd.Value = e; else dtpEnd.Value = selectedDate.AddHours(10);
            txtMemo.Text = _evt.Title;
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

        private void InitializeComponent()
        {
            this.Text = "予定を実績へコピー";
            this.Size = new Size(450, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false; this.MinimizeBox = false;

            this.Controls.Add(new Label { Text = "開始:", Location = new Point(20, 20), AutoSize = true });
            dtpStart = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd HH:mm", Location = new Point(80, 18), Width = 250 };
            this.Controls.Add(dtpStart);

            this.Controls.Add(new Label { Text = "終了:", Location = new Point(20, 50), AutoSize = true });
            dtpEnd = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd HH:mm", Location = new Point(80, 48), Width = 250 };
            this.Controls.Add(dtpEnd);

            this.Controls.Add(new Label { Text = "内容:", Location = new Point(20, 80), AutoSize = true });
            txtMemo = new TextBox { Location = new Point(80, 78), Width = 330 };
            this.Controls.Add(txtMemo);

            var grp = new GroupBox { Text = "重複時の処理", Location = new Point(20, 120), Size = new Size(390, 60) };
            radOverwrite = new RadioButton { Text = "上書きする", Location = new Point(20, 25), AutoSize = true, Checked = true };
            radFill = new RadioButton { Text = "空き時間のみ埋める", Location = new Point(150, 25), AutoSize = true };
            grp.Controls.AddRange(new Control[] { radOverwrite, radFill });
            this.Controls.Add(grp);

            var btnOk = new Button { Text = "登録", Location = new Point(100, 230) };
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            var btnCancel = new Button { Text = "キャンセル", Location = new Point(200, 230) };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk; this.CancelButton = btnCancel;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            var newLog = new TimeLog { ID = Guid.NewGuid().ToString(), Memo = txtMemo.Text, StartTime = dtpStart.Value.ToString("o"), EndTime = dtpEnd.Value.ToString("o") };
            
            if (radOverwrite.Checked) {
                var logsToRemove = new List<TimeLog>(); var logsToAdd = new List<TimeLog>();
                foreach (var log in _allTimeLogs) {
                    if (string.IsNullOrEmpty(log.StartTime) || string.IsNullOrEmpty(log.EndTime)) continue;
                    DateTime os = DateTime.Parse(log.StartTime); DateTime oe = DateTime.Parse(log.EndTime);
                    if (dtpStart.Value <= os && dtpEnd.Value >= oe) logsToRemove.Add(log);
                    else if (dtpStart.Value > os && dtpEnd.Value < oe) { logsToAdd.Add(new TimeLog { ID = Guid.NewGuid().ToString(), TaskID = log.TaskID, Memo = log.Memo, StartTime = dtpEnd.Value.ToString("o"), EndTime = oe.ToString("o") }); log.EndTime = dtpStart.Value.ToString("o"); }
                    else if (dtpStart.Value > os && dtpStart.Value < oe) log.EndTime = dtpStart.Value.ToString("o");
                    else if (dtpEnd.Value > os && dtpEnd.Value < oe) log.StartTime = dtpEnd.Value.ToString("o");
                }
                foreach (var l in logsToRemove) _allTimeLogs.Remove(l); _allTimeLogs.AddRange(logsToAdd); _allTimeLogs.Add(newLog);
            } else {
                DateTime start = dtpStart.Value;
                DateTime end = dtpEnd.Value;

                var existingLogs = _allTimeLogs.Where(l => 
                    !string.IsNullOrEmpty(l.StartTime) && 
                    !string.IsNullOrEmpty(l.EndTime) && 
                    DateTime.Parse(l.StartTime).Date == start.Date)
                    .OrderBy(l => DateTime.Parse(l.StartTime)).ToList();

                DateTime current = start;
                foreach (var log in existingLogs) {
                    DateTime lStart = DateTime.Parse(log.StartTime);
                    DateTime lEnd = DateTime.Parse(log.EndTime);

                    if (lStart > current) {
                        DateTime gapEnd = lStart < end ? lStart : end;
                        if (gapEnd > current) {
                            _allTimeLogs.Add(new TimeLog { ID = Guid.NewGuid().ToString(), TaskID = newLog.TaskID, Memo = newLog.Memo, StartTime = current.ToString("o"), EndTime = gapEnd.ToString("o") });
                        }
                    }
                    if (lEnd > current) current = lEnd;
                    if (current >= end) break;
                }

                if (current < end) {
                    _allTimeLogs.Add(new TimeLog { ID = Guid.NewGuid().ToString(), TaskID = newLog.TaskID, Memo = newLog.Memo, StartTime = current.ToString("o"), EndTime = end.ToString("o") });
                }
            }
            this.DialogResult = DialogResult.OK; this.Close();
        }
    }
}
