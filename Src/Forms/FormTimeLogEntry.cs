﻿﻿﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UniConsul.Models;
using System.IO;

namespace UniConsul.Forms
{
    public class FormTimeLogEntry : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

        public TimeLog ResultLog { get; private set; }
        public bool SaveToDictionary { get; private set; }
        public string DictionaryKeyword { get; private set; }
        public string MatchType { get; private set; }
        public string OriginalRule { get; private set; }

        private DateTimePicker _timePickerStart;
        private DateTimePicker _timePickerEnd;
        private RadioButton _radioTask;
        private RadioButton _radioMemo;
        private ComboBox _comboProject;
        private ComboBox _comboTask;
        private TextBox _textMemo;
        private Button _btnOK;
        private Button _btnCancel;
        
        private Label _lblAutoLogTitle;
        private Label _lblLearn;
        private ComboBox _cmbLearnType;
        private Label _lblKeyword;
        private TextBox _txtKeyword;
        private Button _btnSelectProcess;

        private List<ProjectItem> _projects;
        private List<TaskItem> _tasks;
        private TimeLog _existingLog;
        private AutoLogEntry _autoLog;
        private DateTime _initialStart;
        private DateTime _initialEnd;

        public FormTimeLogEntry(DateTime initialStart, DateTime initialEnd, List<ProjectItem> projects, List<TaskItem> tasks, TimeLog existingLog = null, AutoLogEntry autoLog = null)
        {
            _initialStart = initialStart;
            _initialEnd = initialEnd;
            _projects = projects ?? new List<ProjectItem>();
            _tasks = tasks ?? new List<TaskItem>();
            _existingLog = existingLog;
            _autoLog = autoLog;

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
            foreach (Control c in parent.Controls)
            {
                if (c is Panel || c is GroupBox)
                {
                    c.BackColor = formBg;
                }
                else if (c is TextBox || c is ComboBox)
                {
                    c.BackColor = surfaceBg;
                    c.ForeColor = fg;
                    if (c is ComboBox) ((ComboBox)c).FlatStyle = FlatStyle.Flat;
                    if (c is TextBox) ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c.GetType() == typeof(DateTimePicker))
                {
                    // WinFormsの仕様上、DateTimePickerの入力欄はOSのシステムカラーで固定されるため設定を除外
                }
                else if (c is Button)
                {
                    var btn = (Button)c;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = isDark ? Color.FromArgb(80, 80, 80) : Color.DarkGray;
                    btn.BackColor = isDark ? Color.FromArgb(60, 60, 65) : SystemColors.Control;
                    btn.ForeColor = fg;
                }
                
                if (c is Label || c is CheckBox || c is RadioButton)
                {
                    c.ForeColor = fg;
                }
                
                if (c.HasChildren)
                {
                    FixThemeRecursively(c, isDark);
                }
            }
        }

        private void InitializeComponent()
        {
            this.Text = _existingLog == null ? (_autoLog == null ? "時間記録の追加" : "自動記録の確定・修正") : "時間記録の編集";
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            var labelStart = new Label { Text = "開始時刻:", Location = new Point(15, 15), AutoSize = true };
            _timePickerStart = new DateTimePicker { Location = new Point(15, 35), Width = 150, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd HH:mm" };
            
            var btnStartNow = new Button { Text = "今すぐ開始", Location = new Point(75, 10), Size = new Size(90, 22) };
            btnStartNow.Click += (s, e) => 
            {
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

            _radioTask.CheckedChanged += (s, e) => 
            { 
                panelTask.Enabled = _radioTask.Checked; 
                _textMemo.Enabled = !_radioTask.Checked; 
            };
            
            _comboProject.SelectedIndexChanged += (s, e) => 
            {
                _comboTask.Items.Clear();
                var proj = _comboProject.SelectedItem as ProjectItem;
                if (proj != null)
                {
                    _comboTask.Items.AddRange(_tasks.Where(t => t.ProjectID == proj.ProjectID).OrderBy(t => t.タスク).ToArray());
                    if (_comboTask.Items.Count > 0)
                    {
                        _comboTask.SelectedIndex = 0;
                    }
                }
            };

            _lblAutoLogTitle = new Label { Location = new Point(15, 225), AutoSize = true, MaximumSize = new Size(360, 40), ForeColor = Color.DimGray, Visible = false };
            _lblLearn = new Label { Text = "学習辞書に登録 (自動紐付け):", Location = new Point(15, 255), AutoSize = true, Visible = false };
            _cmbLearnType = new ComboBox { Location = new Point(15, 275), Size = new Size(355, 25), DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
            _cmbLearnType.Items.Add(new KeyValuePair<string, string>("", "登録しない"));
            _cmbLearnType.Items.Add(new KeyValuePair<string, string>("Title", "タイトル/パスに含まれる (部分一致)"));
            _cmbLearnType.Items.Add(new KeyValuePair<string, string>("Process", "起動中ソフト名に一致 (完全一致)"));
            _cmbLearnType.Items.Add(new KeyValuePair<string, string>("Regex", "複雑な条件で一致させる (正規表現)"));
            _cmbLearnType.DisplayMember = "Value"; _cmbLearnType.ValueMember = "Key";
            
            _lblKeyword = new Label { Text = "キーワード:", Location = new Point(15, 310), AutoSize = true, Visible = false };
            _txtKeyword = new TextBox { Location = new Point(90, 308), Size = new Size(150, 25), Enabled = false, Visible = false };
            
            _btnSelectProcess = new Button { Text = "起動中アプリから...", Location = new Point(245, 307), Size = new Size(125, 27), Visible = false };
            _btnSelectProcess.Click += (s, e) => {
                using (var form = new FormProcessSelector(this.BackColor.R < 100)) {
                    if (form.ShowDialog(this) == DialogResult.OK) {
                        _txtKeyword.Text = form.SelectedProcessName;
                        _cmbLearnType.SelectedIndex = 2; // "Process"
                    }
                }
            };

            _cmbLearnType.SelectedIndexChanged += (s, e) => {
                bool isTargetSelected = _cmbLearnType.SelectedIndex > 0;
                _txtKeyword.Enabled = isTargetSelected;
                _btnSelectProcess.Enabled = isTargetSelected;
                var selectedItem = (KeyValuePair<string, string>)_cmbLearnType.SelectedItem;
                if (selectedItem.Key == "Process" || string.IsNullOrEmpty(_txtKeyword.Text)) {
                    string pName = _autoLog != null ? _autoLog.ProcessName : "";
                    _txtKeyword.Text = !string.IsNullOrEmpty(pName) ? pName.Replace(".exe", "") : "";
                }
            };

            _lblLearn.Visible = true; 
            _cmbLearnType.Visible = true; 
            _lblKeyword.Visible = true;
            _txtKeyword.Visible = true;
            _btnSelectProcess.Visible = true;

            int yOffset = 350;
            this.Size = new Size(400, 460);
            _cmbLearnType.SelectedIndex = 0;

            if (_autoLog != null)
            {
                _lblAutoLogTitle.Visible = true;
                
                string titleText = "対象ログ: " + (_autoLog.WindowTitle ?? _autoLog.ProcessName);
                _lblAutoLogTitle.Text = titleText;
            }

            _btnOK = new Button { Text = "OK", Location = new Point(100, yOffset), Size = new Size(80, 25) };
            _btnCancel = new Button { Text = "キャンセル", Location = new Point(200, yOffset), Size = new Size(80, 25) };
            _btnOK.Click += BtnOK_Click; 
            _btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            this.AcceptButton = _btnOK; this.CancelButton = _btnCancel;
            
            this.Controls.AddRange(new Control[] { 
                labelStart, _timePickerStart, btnStartNow, 
                labelEnd, _timePickerEnd, 
                _radioTask, _radioMemo, panelTask, _textMemo, 
                _lblAutoLogTitle, _lblLearn, _cmbLearnType, _lblKeyword, _txtKeyword, _btnSelectProcess,
                _btnOK, _btnCancel 
            });
        }

        private void LoadData()
        {
            _comboProject.Items.AddRange(_projects.OrderBy(p => p.ProjectName).ToArray());
            
            if (_existingLog != null)
            {
                DateTime st;
                if (DateTime.TryParse(_existingLog.StartTime, out st))
                    _timePickerStart.Value = st;
                    
                DateTime et;
                if (DateTime.TryParse(_existingLog.EndTime, out et))
                    _timePickerEnd.Value = et;
                    
                if (!string.IsNullOrEmpty(_existingLog.TaskID))
                {
                    _radioTask.Checked = true;
                    var task = _tasks.FirstOrDefault(t => t.ID == _existingLog.TaskID);
                    if (task != null)
                    {
                        var proj = _comboProject.Items.Cast<ProjectItem>().FirstOrDefault(p => p.ProjectID == task.ProjectID);
                        if (proj != null) _comboProject.SelectedItem = proj;
                        
                        var cTask = _comboTask.Items.Cast<TaskItem>().FirstOrDefault(t => t.ID == task.ID);
                        if (cTask != null) _comboTask.SelectedItem = cTask;
                    }
                }
                else
                {
                    _radioMemo.Checked = true;
                    _textMemo.Text = _existingLog.Memo;
                }
            }
            else if (_autoLog != null)
            {
                _timePickerStart.Value = _initialStart;
                _timePickerEnd.Value = _initialEnd;
                _radioTask.Checked = true;
                if (_autoLog.Inference != null && !string.IsNullOrEmpty(_autoLog.Inference.TaskID))
                {
                    var task = _tasks.FirstOrDefault(t => t.ID == _autoLog.Inference.TaskID);
                    if (task != null)
                    {
                        var proj = _comboProject.Items.Cast<ProjectItem>().FirstOrDefault(p => p.ProjectID == task.ProjectID);
                        if (proj != null) _comboProject.SelectedItem = proj;
                        
                        var cTask = _comboTask.Items.Cast<TaskItem>().FirstOrDefault(t => t.ID == task.ID);
                        if (cTask != null) _comboTask.SelectedItem = cTask;
                    }
                }
            }
            else
            {
                _timePickerStart.Value = _initialStart;
                _timePickerEnd.Value = _initialEnd;
                if (_comboProject.Items.Count > 0)
                {
                    _comboProject.SelectedIndex = 0;
                }
            }

            // 学習ルールの検索とセット
            string appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskManager");
            var dataService = new UniConsul.Services.DataService(appRoot);
            var settings = dataService.LoadSettings();
            if (settings != null && settings.AutoTracker != null && settings.AutoTracker.LearningDictionary != null)
            {
                string searchTaskId = _existingLog != null ? _existingLog.TaskID : (_autoLog != null && _autoLog.Inference != null ? _autoLog.Inference.TaskID : "");
                string aProcName = _autoLog != null ? _autoLog.ProcessName : "";
                string searchKeyword = !string.IsNullOrEmpty(aProcName) ? aProcName.Replace(".exe", "") : "";

                foreach (var rule in settings.AutoTracker.LearningDictionary)
                {
                    if (rule.StartsWith("CAT:")) continue;
                    
                    var parts = rule.Split(new[] { "::" }, 3, StringSplitOptions.None);
                    if (parts.Length == 3)
                    {
                        string rTask = parts[0];
                        string rMatch = parts[1];
                        string rKwd = parts[2];

                        if ((!string.IsNullOrEmpty(searchTaskId) && rTask == searchTaskId) || 
                            (!string.IsNullOrEmpty(searchKeyword) && rKwd.Contains(searchKeyword) && rMatch == "Process"))
                        {
                            OriginalRule = rule;
                            _txtKeyword.Text = rKwd;
                            for (int i = 0; i < _cmbLearnType.Items.Count; i++)
                            {
                                if (((KeyValuePair<string, string>)_cmbLearnType.Items[i]).Key == rMatch)
                                {
                                    _cmbLearnType.SelectedIndex = i;
                                    break;
                                }
                            }
                            if (rMatch == "Title") _cmbLearnType.SelectedIndex = 1;
                            else if (rMatch == "Process") _cmbLearnType.SelectedIndex = 2;
                            else if (rMatch == "Regex") _cmbLearnType.SelectedIndex = 3;
                            break; 
                        }
                    }
                    else if (parts.Length == 2)
                    {
                        string rTask = parts[0];
                        string rKwd = parts[1];

                        if ((!string.IsNullOrEmpty(searchTaskId) && rTask == searchTaskId) || 
                            (!string.IsNullOrEmpty(searchKeyword) && rKwd.Contains(searchKeyword)))
                        {
                            OriginalRule = rule;
                            _txtKeyword.Text = rKwd;
                            _cmbLearnType.SelectedIndex = 1; // "Title" 相当
                            break; 
                        }
                    }
                }
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (_timePickerEnd.Value <= _timePickerStart.Value)
            {
                MessageBox.Show("終了時刻は開始時刻より後に設定してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            ResultLog = _existingLog ?? new TimeLog();
            ResultLog.StartTime = _timePickerStart.Value.ToString("o");
            ResultLog.EndTime = _timePickerEnd.Value.ToString("o");
            
            if (_radioTask.Checked)
            {
                var selTask = _comboTask.SelectedItem as TaskItem;
                if (selTask != null)
                {
                    ResultLog.TaskID = selTask.ID;
                    ResultLog.Memo = null;
                }
                else
                {
                    MessageBox.Show("タスクを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_textMemo.Text))
                {
                    MessageBox.Show("メモを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ResultLog.TaskID = null;
                ResultLog.Memo = _textMemo.Text.Trim();
            }
            
            SaveToDictionary = _cmbLearnType != null && _cmbLearnType.SelectedIndex > 0;
            MatchType = "Title"; // デフォルト値
            if (SaveToDictionary)
            {
                var selectedItem = (KeyValuePair<string, string>)_cmbLearnType.SelectedItem;
                MatchType = selectedItem.Key;
            }
            DictionaryKeyword = _txtKeyword != null ? _txtKeyword.Text.Trim() : "";
            
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
