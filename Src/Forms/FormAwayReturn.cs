using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UniConsul.Models;
using UniConsul.Services;

namespace UniConsul.Forms
{
    public class FormAwayReturn : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public string Action { get; private set; } // "Continue", "OtherTask", "Break"
        public string SelectedTaskID { get; private set; }

        private ComboBox _cmbTasks;

        public FormAwayReturn(DateTime startAway, DateTime endAway, List<TaskItem> tasks, List<ProjectItem> projects, string suspendedTaskID, bool isDarkMode)
        {
            this.Text = "離席からの復帰";
            this.Size = new Size(500, 320);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.TopMost = true;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            int awayMinutes = (int)(endAway - startAway).TotalMinutes;
            
            var lblTitle = new Label { Text = "お帰りなさい！", Font = new Font("Meiryo UI", 12, FontStyle.Bold), Location = new Point(20, 20), AutoSize = true };
            var lblTime = new Label { Text = string.Format("{0:HH:mm} から {1:HH:mm} まで（約{2}分間）PCの操作がありませんでした。\nこの間の記録をどうしますか？", startAway, endAway, awayMinutes), Location = new Point(20, 50), AutoSize = true };

            var btnContinue = new Button { Location = new Point(20, 100), Size = new Size(440, 35), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
            
            string suspendedTaskName = "不明";
            if (!string.IsNullOrEmpty(suspendedTaskID))
            {
                var t = tasks.FirstOrDefault(x => x.ID == suspendedTaskID);
                if (t != null) suspendedTaskName = t.タスク;
            }
            btnContinue.Text = "▶ 直前のタスクを継続していた (" + suspendedTaskName + ")";
            btnContinue.Enabled = !string.IsNullOrEmpty(suspendedTaskID);
            btnContinue.Click += (s, e) => { Action = "Continue"; SelectedTaskID = suspendedTaskID; this.DialogResult = DialogResult.OK; this.Close(); };

            var btnOther = new Button { Text = "▶ 別のタスクを作業していた", Location = new Point(20, 145), Size = new Size(200, 35), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
            _cmbTasks = new ComboBox { Location = new Point(230, 150), Size = new Size(230, 25), DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "タスク", ValueMember = "ID" };
            
            var activeTasks = tasks.Where(t => t.進捗度 != "完了済み").OrderBy(t => t.タスク).ToList();
            foreach (var t in activeTasks) _cmbTasks.Items.Add(t);
            if (_cmbTasks.Items.Count > 0) _cmbTasks.SelectedIndex = 0;

            btnOther.Click += (s, e) => { 
                var sel = _cmbTasks.SelectedItem as TaskItem;
                if (sel != null) {
                    Action = "OtherTask"; 
                    SelectedTaskID = sel.ID; 
                    this.DialogResult = DialogResult.OK; 
                    this.Close(); 
                }
            };

            var btnBreak = new Button { Text = "☕ 休憩・離席していた (記録しない)", Location = new Point(20, 190), Size = new Size(440, 35), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
            btnBreak.Click += (s, e) => { Action = "Break"; this.DialogResult = DialogResult.OK; this.Close(); };

            var btnCancel = new Button { Text = "あとで決める (キャンセル)", Location = new Point(300, 240), Size = new Size(160, 30) };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            this.Controls.AddRange(new Control[] { lblTitle, lblTime, btnContinue, btnOther, _cmbTasks, btnBreak, btnCancel });
            this.CancelButton = btnCancel;

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
    }
}