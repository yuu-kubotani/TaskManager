using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TaskManager.Models;

namespace TaskManager
{
    public class FormTimeLogEntry : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public TimeLog ResultLog { get; private set; }

        private DateTimePicker _timePickerStart;
        private DateTimePicker _timePickerEnd;
        private RadioButton _radioTask;
        private RadioButton _radioMemo;
        private ComboBox _comboProject;
        private ComboBox _comboTask;
        private TextBox _textMemo;
        private Button _btnOK;
        private Button _btnCancel;

        private List<ProjectItem> _projects;
        private List<TaskItem> _tasks;
        private TimeLog _existingLog;
        private DateTime _initialStart;
        private DateTime _initialEnd;

        public FormTimeLogEntry(DateTime initialStart, DateTime initialEnd, List<ProjectItem> projects, List<TaskItem> tasks, TimeLog existingLog = null)
        {
            _initialStart = initialStart;
            _initialEnd = initialEnd;
            _projects = projects ?? new List<ProjectItem>();
            _tasks = tasks ?? new List<TaskItem>();
            _existingLog = existingLog;

            InitializeComponent();
            LoadData();
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
            FixThemeRecursively(this, isDark);
        }

        private void FixThemeRecursively(Control parent, bool isDark)
        {
            Color formBg = isDark ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
            Color surfaceBg = isDark ? Color.FromArgb(30, 30, 30) : SystemColors.Window;
            Color fg = isDark ? Color.White : SystemColors.ControlText;
            this.BackColor = formBg;
            foreach (Control c in parent.Controls) {
                if (c is Panel || c is GroupBox) c.BackColor = formBg;
                else if (c is TextBox || c is ComboBox) {
                    c.BackColor = surfaceBg; c.ForeColor = fg;
                    var cmb = c as ComboBox;
                    if (cmb != null) cmb.FlatStyle = FlatStyle.Flat;
                    var txt = c as TextBox;
                    if (txt != null) txt.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is Button) {
                    var btn = (Button)c;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = isDark ? Color.FromArgb(80, 80, 80) : Color.DarkGray;
                    btn.BackColor = isDark ? Color.FromArgb(60, 60, 65) : SystemColors.Control;
                    btn.ForeColor = fg;
                }
                if (c is Label || c is CheckBox || c is RadioButton) c.ForeColor = fg;
                if (c.HasChildren) FixThemeRecursively(c, isDark);
            }
        }

        private void InitializeComponent()
        {
            this.Text = _existingLog == null ? "時間記録の追加" : "時間記録の編集";
            this.Size = new Size(400, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;

            var labelStart = new Label { Text = "開始時刻:", Location = new Point(15, 15), AutoSize = true };
            _timePickerStart = new DateTimePicker { Location = new Point(15, 35), Width = 150, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd HH:mm" };
            
            var btnStartNow = new Button { Text = "今すぐ開始", Location = new Point(75, 10), Size = new Size(90, 22) };
            btnStartNow.Click += (s, e) => {
                TimeSpan duration = _timePickerEnd.Value - _timePickerStart.Value;
                _timePickerStart.Value = DateTime.Now;
                _timePickerEnd.Value = DateTime.Now.Add(duration);
            };

            var labelEnd = new Label { Text = "終了時刻:", Location = new Point(200, 15), AutoSize = true };
            _timePickerEnd = new DateTimePicker { Location = new Point(200, 35), Width = 150, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd HH:mm" };

            _radioTask = new RadioButton { Text = "タスクに紐付ける", Location = new Point(15, 70), AutoSize = true, Checked = true };
            _radioMemo = new RadioButton { Text = "メモとして記録", Location = new Point(15, 160), AutoSize = true };

            var panelTask = new Panel { Location = new Point(30, 95), Size = new Size(340, 55) };
            var labelProject = new Label { Text = "プロジェクト:", Location = new Point(0, 0), AutoSize = true };
            _comboProject = new ComboBox { Location = new Point(0, 20), Size = new Size(150, 25), DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "ProjectName", ValueMember = "ProjectID" };
            var labelTask = new Label { Text = "タスク:", Location = new Point(170, 0), AutoSize = true };
            _comboTask = new ComboBox { Location = new Point(170, 20), Size = new Size(170, 25), DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "タスク", ValueMember = "ID" };
            panelTask.Controls.AddRange(new Control[] { labelProject, _comboProject, labelTask, _comboTask });

            _textMemo = new TextBox { Location = new Point(30, 185), Size = new Size(340, 25), Enabled = false };

            _radioTask.CheckedChanged += (s, e) => { panelTask.Enabled = _radioTask.Checked; _textMemo.Enabled = !_radioTask.Checked; };
            _comboProject.SelectedIndexChanged += (s, e) => {
                _comboTask.Items.Clear();
                var proj = _comboProject.SelectedItem as ProjectItem;
                if (proj != null) {
                    _comboTask.Items.AddRange(_tasks.Where(t => t.ProjectID == proj.ProjectID).OrderBy(t => t.タスク).ToArray());
                    if (_comboTask.Items.Count > 0) _comboTask.SelectedIndex = 0;
                }
            };

            _btnOK = new Button { Text = "OK", Location = new Point(100, 230), Size = new Size(80, 25) };
            _btnCancel = new Button { Text = "キャンセル", Location = new Point(200, 230), Size = new Size(80, 25) };
            _btnOK.Click += BtnOK_Click; _btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            this.AcceptButton = _btnOK; this.CancelButton = _btnCancel;
            
            this.Controls.AddRange(new Control[] { 
                labelStart, _timePickerStart, btnStartNow, 
                labelEnd, _timePickerEnd, 
                _radioTask, _radioMemo, panelTask, _textMemo, 
                _btnOK, _btnCancel 
            });
        }

        private void LoadData()
        {
            _comboProject.Items.AddRange(_projects.OrderBy(p => p.ProjectName).ToArray());
            if (_existingLog != null) {
                DateTime st;
                if (DateTime.TryParse(_existingLog.StartTime, out st)) _timePickerStart.Value = st;
                DateTime et;
                if (DateTime.TryParse(_existingLog.EndTime, out et)) _timePickerEnd.Value = et;
                if (!string.IsNullOrEmpty(_existingLog.TaskID)) {
                    _radioTask.Checked = true; var task = _tasks.FirstOrDefault(t => t.ID == _existingLog.TaskID);
                    if (task != null) { var proj = _comboProject.Items.Cast<ProjectItem>().FirstOrDefault(p => p.ProjectID == task.ProjectID); if (proj != null) _comboProject.SelectedItem = proj; var cTask = _comboTask.Items.Cast<TaskItem>().FirstOrDefault(t => t.ID == task.ID); if (cTask != null) _comboTask.SelectedItem = cTask; }
                } else { _radioMemo.Checked = true; _textMemo.Text = _existingLog.Memo; }
            } else {
                _timePickerStart.Value = _initialStart; _timePickerEnd.Value = _initialEnd;
                if (_comboProject.Items.Count > 0) _comboProject.SelectedIndex = 0;
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (_timePickerEnd.Value <= _timePickerStart.Value) { MessageBox.Show("終了時刻は開始時刻より後に設定してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            ResultLog = _existingLog ?? new TimeLog(); ResultLog.StartTime = _timePickerStart.Value.ToString("o"); ResultLog.EndTime = _timePickerEnd.Value.ToString("o");
            if (_radioTask.Checked) {
                var selTask = _comboTask.SelectedItem as TaskItem;
                if (selTask != null) { ResultLog.TaskID = selTask.ID; ResultLog.Memo = null; }
                else { MessageBox.Show("タスクを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            } else {
                if (string.IsNullOrWhiteSpace(_textMemo.Text)) { MessageBox.Show("メモを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                ResultLog.TaskID = null; ResultLog.Memo = _textMemo.Text.Trim();
            }
            this.DialogResult = DialogResult.OK; this.Close();
        }
    }
}
