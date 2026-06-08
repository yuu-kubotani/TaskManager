using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UniConsul.Models;
using UniConsul.Services;

namespace UniConsul.Forms
{
    public class FormRecurringRuleEditor : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private DataService _dataService;
        private List<ProjectItem> _projects;
        private List<RecurringRule> _rules;
        private Dictionary<string, List<string>> _categories;
        private object _initialItem;

        private DataGridView _dgv;
        private FlowLayoutPanel _mainFlow;
        private CheckBox _chkIsActive, _chkTriggerPreGen, _chkEndOfMonth, _chkEventAllDay;
        private CheckBox[] _chkDays = new CheckBox[7];
        private TextBox _txtTitle, _txtTaskContent;
        private RadioButton _radioProject, _radioTask, _radioEvent, _radioGenScheduled, _radioGenCompletion;
        private ComboBox _cmbIntervalPreset, _cmbGenWeekendShift, _cmbGenHolidayShift, _cmbDueOffsetUnit, _cmbDueWeekendShift, _cmbDueHolidayShift;
        private ComboBox _cmbTaskProj, _cmbTaskCat, _cmbTaskSubCat, _cmbTaskPrio, _cmbTaskStatus, _cmbTaskNotify, _cmbSourceProj;
        private NumericUpDown _numInterval, _numPreGen, _numDueOffset;
        private DateTimePicker _dtpEventStart, _dtpEventEnd, _dtpNextRun;
        private Label _lblPreview, _lblIntervalSuffix, _lblProjWarning;
        private Panel _pnlFreq, _pnlPreGen, _pnlGenTiming, _pnlGenAvoidance, _pnlTaskRow1, _pnlTaskRow2, _pnlTaskRow3, _pnlTaskRow4, _pnlProjDetails, _pnlTime;
        private GroupBox _grpDueSettings, _grpDetails;
        private Button _btnSave, _btnDelete, _btnClose;

        private class ComboItem { public string Text { get; set; } public string Value { get; set; } public override string ToString() { return Text; } }

        public FormRecurringRuleEditor(DataService dataService, List<ProjectItem> projects, object initialItem = null)
        {
            _dataService = dataService;
            _projects = projects;
            _categories = _dataService.LoadFromJson<Dictionary<string, List<string>>>(_dataService.CategoriesFile, new Dictionary<string, List<string>>());
            
            if (initialItem != null)
            {
                _initialItem = initialItem;
            }

            this.Text = "定期実行ルール統合管理 (C#版)";
            this.Size = new Size(900, 850);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            _dtpNextRun = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120 };

            var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 150 };
            this.Controls.Add(splitContainer);

            // 上部：ルール一覧
            _dgv = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, RowHeadersVisible = false, MultiSelect = false };
            _dgv.Columns.Add(new DataGridViewCheckBoxColumn { Name = "IsActive", HeaderText = "有効", DataPropertyName = "IsActive", Width = 50 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "RuleID", Visible = false, DataPropertyName = "RuleID" });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "種別", DataPropertyName = "Type", Width = 80, ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "タイトル", DataPropertyName = "TaskName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Interval", HeaderText = "頻度", DataPropertyName = "Frequency", Width = 100, ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "NextGenDate", HeaderText = "次回生成", DataPropertyName = "NextRunDate", Width = 100, ReadOnly = true });
            splitContainer.Panel1.Controls.Add(_dgv);

            // 下部：詳細編集コントロール
            _mainFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10) };
            splitContainer.Panel2.Controls.Add(_mainFlow);

            // 1. 基本情報
            var grpBasic = new GroupBox { Text = "基本情報", Size = new Size(800, 80) };
            _chkIsActive = new CheckBox { Text = "このルールを有効にする", Location = new Point(15, 20), AutoSize = true, Checked = true, Font = new Font("Meiryo UI", 10, FontStyle.Bold) };
            _txtTitle = new TextBox { Location = new Point(80, 47), Width = 450 };
            grpBasic.Controls.AddRange(new Control[] { _chkIsActive, new Label { Text = "タイトル:", Location = new Point(15, 50), AutoSize = true }, _txtTitle });
            _mainFlow.Controls.Add(grpBasic);

            // 2. アイテム種別
            var grpType = new GroupBox { Text = "アイテム種別", Size = new Size(800, 50) };
            _radioProject = new RadioButton { Text = "プロジェクト", Location = new Point(20, 20), AutoSize = true };
            _radioTask = new RadioButton { Text = "タスク", Location = new Point(120, 20), AutoSize = true, Checked = true };
            _radioEvent = new RadioButton { Text = "予定 (イベント)", Location = new Point(200, 20), AutoSize = true };
            grpType.Controls.AddRange(new Control[] { _radioProject, _radioTask, _radioEvent });
            _mainFlow.Controls.Add(grpType);

            // 3. 生成設定
            var grpGenSettings = new GroupBox { Text = "生成設定 (スケジュール)", AutoSize = true, MinimumSize = new Size(800, 0), Padding = new Padding(5) };
            var genFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            grpGenSettings.Controls.Add(genFlow);

            _pnlGenTiming = new FlowLayoutPanel { AutoSize = true };
            _radioGenScheduled = new RadioButton { Text = "予定・期限起点", AutoSize = true, Checked = true };
            _radioGenCompletion = new RadioButton { Text = "完了起点", AutoSize = true };
            _pnlGenTiming.Controls.AddRange(new Control[] { new Label { Text = "生成タイミング:", AutoSize = true, Margin = new Padding(0, 4, 10, 0) }, _radioGenScheduled, _radioGenCompletion });
            genFlow.Controls.Add(_pnlGenTiming);

            _pnlFreq = new FlowLayoutPanel { AutoSize = true, MinimumSize = new Size(780, 0), Padding = new Padding(10, 5, 0, 0) };
            _cmbIntervalPreset = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbIntervalPreset.Items.AddRange(new[] { "毎日", "毎週", "毎月 (同日)", "その他" });
            _numInterval = new NumericUpDown { Width = 50, Minimum = 1, Maximum = 365, Value = 1 };
            _lblIntervalSuffix = new Label { Text = "日ごと", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            _pnlFreq.Controls.AddRange(new Control[] { new Label { Text = "頻度:", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _cmbIntervalPreset, _numInterval, _lblIntervalSuffix });
            genFlow.Controls.Add(_pnlFreq);

            var pnlDays = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(10, 0, 0, 0) };
            string[] days = { "月", "火", "水", "木", "金", "土", "日" };
            for (int i = 0; i < 7; i++) { _chkDays[i] = new CheckBox { Text = days[i], AutoSize = true, Visible = false }; pnlDays.Controls.Add(_chkDays[i]); }
            _chkEndOfMonth = new CheckBox { Text = "月末(最終日)を指定", AutoSize = true, Visible = false };
            pnlDays.Controls.Add(_chkEndOfMonth);
            genFlow.Controls.Add(pnlDays);

            _pnlGenAvoidance = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(10, 0, 0, 0) };
            _cmbGenWeekendShift = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Text", ValueMember = "Value" }; 
            _cmbGenWeekendShift.Items.AddRange(new[] { new ComboItem { Text = "しない", Value = "None" }, new ComboItem { Text = "金曜日に前倒し", Value = "Friday" }, new ComboItem { Text = "月曜日に先送り", Value = "Monday" } }); _cmbGenWeekendShift.SelectedIndex = 0;
            _cmbGenHolidayShift = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Text", ValueMember = "Value" }; 
            _cmbGenHolidayShift.Items.AddRange(new[] { new ComboItem { Text = "しない", Value = "None" }, new ComboItem { Text = "前営業日に前倒し", Value = "Before" }, new ComboItem { Text = "翌営業日に先送り", Value = "After" } }); _cmbGenHolidayShift.SelectedIndex = 0;
            _pnlGenAvoidance.Controls.AddRange(new Control[] { new Label { Text = "土日回避:", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _cmbGenWeekendShift, new Label { Text = "祝日回避:", AutoSize = true, Margin = new Padding(5, 4, 0, 0) }, _cmbGenHolidayShift });
            genFlow.Controls.Add(_pnlGenAvoidance);

            _pnlPreGen = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(10, 5, 0, 0) };
            _chkTriggerPreGen = new CheckBox { Text = "事前生成を行う", AutoSize = true };
            _numPreGen = new NumericUpDown { Width = 50, Minimum = 0, Maximum = 365, Enabled = false };
            _pnlPreGen.Controls.AddRange(new Control[] { _chkTriggerPreGen, _numPreGen, new Label { Text = "日前", AutoSize = true, Margin = new Padding(0, 4, 0, 0) } });
            genFlow.Controls.Add(_pnlPreGen);
            _mainFlow.Controls.Add(grpGenSettings);

            // 4. 期日設定
            _grpDueSettings = new GroupBox { Text = "期日設定", AutoSize = true, MinimumSize = new Size(800, 0), Padding = new Padding(5) };
            var dueFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            _grpDueSettings.Controls.Add(dueFlow);

            var pnlDueOffset = new FlowLayoutPanel { AutoSize = true };
            _numDueOffset = new NumericUpDown { Width = 60, Minimum = 0 };
            _cmbDueOffsetUnit = new ComboBox { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList }; _cmbDueOffsetUnit.Items.AddRange(new[] { "日後", "週間後", "ヶ月後" }); _cmbDueOffsetUnit.SelectedIndex = 0;
            pnlDueOffset.Controls.AddRange(new Control[] { new Label { Text = "基準: 生成日", AutoSize = true, Margin = new Padding(0, 4, 10, 0) }, new Label { Text = "間隔:", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _numDueOffset, _cmbDueOffsetUnit });
            dueFlow.Controls.Add(pnlDueOffset);

            var pnlDueAvoidance = new FlowLayoutPanel { AutoSize = true };
            _cmbDueWeekendShift = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Text", ValueMember = "Value" }; 
            _cmbDueWeekendShift.Items.AddRange(new[] { new ComboItem { Text = "しない", Value = "None" }, new ComboItem { Text = "金曜日に前倒し", Value = "Friday" }, new ComboItem { Text = "月曜日に先送り", Value = "Monday" } }); _cmbDueWeekendShift.SelectedIndex = 0;
            _cmbDueHolidayShift = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Text", ValueMember = "Value" }; 
            _cmbDueHolidayShift.Items.AddRange(new[] { new ComboItem { Text = "しない", Value = "None" }, new ComboItem { Text = "前営業日に前倒し", Value = "Before" }, new ComboItem { Text = "翌営業日に先送り", Value = "After" } }); _cmbDueHolidayShift.SelectedIndex = 0;
            pnlDueAvoidance.Controls.AddRange(new Control[] { new Label { Text = "期日の土日回避:", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _cmbDueWeekendShift, new Label { Text = "祝日回避:", AutoSize = true, Margin = new Padding(5, 4, 0, 0) }, _cmbDueHolidayShift });
            dueFlow.Controls.Add(pnlDueAvoidance);
            _mainFlow.Controls.Add(_grpDueSettings);

            // 5. アイテム詳細設定
            _grpDetails = new GroupBox { Text = "アイテム詳細設定", AutoSize = true, MinimumSize = new Size(800, 0), Padding = new Padding(5) };
            var detailsFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            _grpDetails.Controls.Add(detailsFlow);

            _pnlTaskRow1 = new FlowLayoutPanel { AutoSize = true };
            _cmbTaskProj = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "ProjectName", ValueMember = "ProjectID" };
            foreach (var p in _projects.OrderBy(x => x.ProjectName)) _cmbTaskProj.Items.Add(p);
            _pnlTaskRow1.Controls.AddRange(new Control[] { new Label { Text = "プロジェクト:", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _cmbTaskProj });
            detailsFlow.Controls.Add(_pnlTaskRow1);

            _pnlTaskRow2 = new FlowLayoutPanel { AutoSize = true };
            _txtTaskContent = new TextBox { Width = 450 };
            _pnlTaskRow2.Controls.AddRange(new Control[] { new Label { Text = "タスク名:", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _txtTaskContent });
            detailsFlow.Controls.Add(_pnlTaskRow2);

            _pnlTaskRow3 = new FlowLayoutPanel { AutoSize = true };
            _cmbTaskCat = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList }; _cmbTaskCat.Items.AddRange(_categories.Keys.ToArray());
            _cmbTaskSubCat = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbTaskCat.SelectedIndexChanged += (s, e) => { _cmbTaskSubCat.Items.Clear(); if (_cmbTaskCat.SelectedItem != null && _categories.ContainsKey(_cmbTaskCat.SelectedItem.ToString())) _cmbTaskSubCat.Items.AddRange(_categories[_cmbTaskCat.SelectedItem.ToString()].ToArray()); };
            _pnlTaskRow3.Controls.AddRange(new Control[] { new Label { Text = "カテゴリ:", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _cmbTaskCat, new Label { Text = "サブカテゴリ:", AutoSize = true, Margin = new Padding(10, 4, 0, 0) }, _cmbTaskSubCat });
            detailsFlow.Controls.Add(_pnlTaskRow3);

            _pnlTaskRow4 = new FlowLayoutPanel { AutoSize = true };
            _cmbTaskPrio = new ComboBox { Width = 60, DropDownStyle = ComboBoxStyle.DropDownList }; _cmbTaskPrio.Items.AddRange(new[] { "高", "中", "低" }); _cmbTaskPrio.SelectedIndex = 1;
            _cmbTaskStatus = new ComboBox { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList }; _cmbTaskStatus.Items.AddRange(new[] { "未実施", "保留", "実施中", "確認待ち", "完了済み" }); _cmbTaskStatus.SelectedIndex = 0;
            _cmbTaskNotify = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList }; _cmbTaskNotify.Items.AddRange(new[] { "全体設定に従う", "通知しない", "当日", "1日前", "前の営業日", "3日前", "1週間前" }); _cmbTaskNotify.SelectedIndex = 0;
            _pnlTaskRow4.Controls.AddRange(new Control[] { new Label { Text = "優先度:", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _cmbTaskPrio, new Label { Text = "進捗:", AutoSize = true, Margin = new Padding(10, 4, 0, 0) }, _cmbTaskStatus, new Label { Text = "通知:", AutoSize = true, Margin = new Padding(10, 4, 0, 0) }, _cmbTaskNotify });
            detailsFlow.Controls.Add(_pnlTaskRow4);

            _pnlProjDetails = new FlowLayoutPanel { AutoSize = true, Visible = false };
            _cmbSourceProj = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "ProjectName", ValueMember = "ProjectID" };
            _cmbSourceProj.Items.Add("(なし)"); foreach (var p in _projects.OrderBy(x => x.ProjectName)) _cmbSourceProj.Items.Add(p); _cmbSourceProj.SelectedIndex = 0;
            _pnlProjDetails.Controls.AddRange(new Control[] { new Label { Text = "コピー元プロジェクト(任意):", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _cmbSourceProj });
            detailsFlow.Controls.Add(_pnlProjDetails);

            _pnlTime = new FlowLayoutPanel { AutoSize = true, Visible = false };
            _chkEventAllDay = new CheckBox { Text = "終日", AutoSize = true, Margin = new Padding(0, 4, 10, 0) };
            _dtpEventStart = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 70 };
            _dtpEventEnd = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 70 };
            _chkEventAllDay.CheckedChanged += (s, e) => { _dtpEventStart.Enabled = !_chkEventAllDay.Checked; _dtpEventEnd.Enabled = !_chkEventAllDay.Checked; CalculatePreview(); };
            _pnlTime.Controls.AddRange(new Control[] { _chkEventAllDay, _dtpEventStart, new Label { Text = "～", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, _dtpEventEnd });
            detailsFlow.Controls.Add(_pnlTime);

            _lblProjWarning = new Label { Text = "⚠️ テンプレート複製モード: 完了時、プロジェクト内の全タスクが複製されます", AutoSize = true, ForeColor = Color.DarkOrange, Visible = false };
            detailsFlow.Controls.Add(_lblProjWarning);
            _mainFlow.Controls.Add(_grpDetails);

            // 6. プレビュー
            var grpPreview = new GroupBox { Text = "次回予定プレビュー", Size = new Size(800, 60) };
            _lblPreview = new Label { Text = "計算中...", AutoSize = true, Location = new Point(15, 25), Font = new Font("Meiryo UI", 9, FontStyle.Bold) };
            grpPreview.Controls.Add(_lblPreview);
            _mainFlow.Controls.Add(grpPreview);

            // 7. アクションボタン
            var pnlActions = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(0, 10, 0, 0), Margin = new Padding(0, 15, 0, 15) };
            _btnSave = new Button { Text = "保存", Size = new Size(100, 35) };
            _btnDelete = new Button { Text = "削除", Size = new Size(100, 35), ForeColor = Color.Red };
            _btnClose = new Button { Text = "閉じる", Size = new Size(100, 35), DialogResult = DialogResult.Cancel };
            pnlActions.Controls.AddRange(new Control[] { _btnSave, _btnDelete, _btnClose });
            _mainFlow.Controls.Add(pnlActions);

            // --- イベントバインド ---
            EventHandler uiUpdater = (s, e) => { UpdateUIState(); CalculatePreview(); };
            
            _radioTask.CheckedChanged += uiUpdater; _radioProject.CheckedChanged += uiUpdater; _radioEvent.CheckedChanged += uiUpdater;
            _radioGenScheduled.CheckedChanged += uiUpdater; _radioGenCompletion.CheckedChanged += uiUpdater; _chkTriggerPreGen.CheckedChanged += uiUpdater;
            _cmbIntervalPreset.SelectedIndexChanged += uiUpdater; _numInterval.ValueChanged += uiUpdater;
            _dtpNextRun.ValueChanged += uiUpdater; _numDueOffset.ValueChanged += uiUpdater; _cmbDueOffsetUnit.SelectedIndexChanged += uiUpdater;
            _cmbGenWeekendShift.SelectedIndexChanged += uiUpdater; _cmbGenHolidayShift.SelectedIndexChanged += uiUpdater;
            _cmbDueWeekendShift.SelectedIndexChanged += uiUpdater; _cmbDueHolidayShift.SelectedIndexChanged += uiUpdater;
            foreach (var c in _chkDays) c.CheckedChanged += uiUpdater;
            _chkEndOfMonth.CheckedChanged += uiUpdater;
            _dtpEventStart.ValueChanged += uiUpdater; _dtpEventEnd.ValueChanged += uiUpdater;

            _dgv.SelectionChanged += Dgv_SelectionChanged;
            _btnSave.Click += BtnSave_Click;
            _btnDelete.Click += BtnDelete_Click;

            _cmbIntervalPreset.SelectedIndex = 0;
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
                if (c is Panel || c is GroupBox || c is SplitContainer || c is SplitterPanel) c.BackColor = formBg;
                else if (c is TextBox || c is ComboBox || c is NumericUpDown) {
                    c.BackColor = surfaceBg; c.ForeColor = fg;
                    var cmb = c as ComboBox;
                    if (cmb != null) cmb.FlatStyle = FlatStyle.Flat;
                    var txt = c as TextBox;
                    if (txt != null) txt.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is DataGridView) {
                    var dgv = (DataGridView)c;
                    dgv.BackgroundColor = surfaceBg;
                    dgv.DefaultCellStyle.BackColor = surfaceBg;
                    dgv.DefaultCellStyle.ForeColor = fg;
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = formBg;
                    dgv.ColumnHeadersDefaultCellStyle.ForeColor = fg;
                    dgv.EnableHeadersVisualStyles = false;
                }
                else if (c is Button) {
                    var btn = (Button)c;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = isDark ? Color.FromArgb(80, 80, 80) : Color.DarkGray;
                    btn.BackColor = isDark ? Color.FromArgb(60, 60, 65) : SystemColors.Control;
                    if (btn.ForeColor != Color.Red) btn.ForeColor = fg;
                    else if (isDark) btn.ForeColor = Color.LightCoral;
                }
                if (c is Label || c is CheckBox || c is RadioButton) c.ForeColor = fg;
                if (c.HasChildren) FixThemeRecursively(c, isDark);
            }
        }

        private void LoadData()
        {
            _rules = _dataService.LoadFromJson<List<RecurringRule>>(_dataService.RecurringRulesFile, new List<RecurringRule>());
            _dgv.DataSource = new BindingSource { DataSource = _rules };

            if (_initialItem != null)
            {
                _dgv.ClearSelection();
                var t = _initialItem as TaskItem;
                var prj = _initialItem as ProjectItem;
                var ev = _initialItem as EventItem;
                if (t != null) {
                    _radioTask.Checked = true; _txtTitle.Text = t.タスク;
                    var p = _cmbTaskProj.Items.Cast<ProjectItem>().FirstOrDefault(x => x.ProjectID == t.ProjectID);
                    if (p != null) _cmbTaskProj.SelectedItem = p;
                    _cmbTaskCat.SelectedItem = t.カテゴリ; _cmbTaskPrio.SelectedItem = t.優先度;
                    DateTime due; if (DateTime.TryParse(t.期日, out due)) _dtpNextRun.Value = due.AddDays(1);
                } else if (prj != null) {
                    _radioProject.Checked = true; _txtTitle.Text = prj.ProjectName;
                    DateTime pdue; if (DateTime.TryParse(prj.ProjectDueDate, out pdue)) _dtpNextRun.Value = pdue.AddDays(1);
                } else if (ev != null) {
                    _radioEvent.Checked = true; _txtTitle.Text = ev.Title;
                    _chkEventAllDay.Checked = ev.IsAllDay;
                    if (!ev.IsAllDay) {
                            DateTime st;
                            if (DateTime.TryParse(ev.StartTime, out st)) { _dtpEventStart.Value = st; _dtpNextRun.Value = st.AddDays(1); }
                            DateTime et;
                            if (DateTime.TryParse(ev.EndTime, out et)) _dtpEventEnd.Value = et;
                        } else {
                            DateTime std;
                            if (DateTime.TryParse(ev.StartTime, out std)) _dtpNextRun.Value = std.AddDays(1);
                        }
                }
                UpdateUIState();
            }
        }

        private void Dgv_SelectionChanged(object sender, EventArgs e)
        {
            if (_dgv.SelectedRows.Count > 0 && _dgv.SelectedRows[0].DataBoundItem as RecurringRule != null)
            {
                var rule = _dgv.SelectedRows[0].DataBoundItem as RecurringRule;
                _txtTitle.Text = rule.TaskName;
                _chkIsActive.Checked = rule.IsActive;
                
                if (rule.Type == "Project") _radioProject.Checked = true;
                else if (rule.Type == "Event") _radioEvent.Checked = true;
                else _radioTask.Checked = true;

                bool isComp = rule.TriggerModes != null && rule.TriggerModes.Contains("OnCompletion");
                if (isComp) _radioGenCompletion.Checked = true; else _radioGenScheduled.Checked = true;
                
                _chkTriggerPreGen.Checked = rule.TriggerModes != null && rule.TriggerModes.Contains("PreGeneration");
                _numPreGen.Value = rule.PreGenDays;

                if (_cmbIntervalPreset.Items.Contains(rule.Frequency)) _cmbIntervalPreset.SelectedItem = rule.Frequency;
                _numInterval.Value = rule.IntervalDays > 0 ? rule.IntervalDays : 1;
                var gw = _cmbGenWeekendShift.Items.Cast<ComboItem>().FirstOrDefault(i => i.Value == rule.WeekendShift); if (gw != null) _cmbGenWeekendShift.SelectedItem = gw;
                var gh = _cmbGenHolidayShift.Items.Cast<ComboItem>().FirstOrDefault(i => i.Value == rule.HolidayShift); if (gh != null) _cmbGenHolidayShift.SelectedItem = gh;
                DateTime nr;
                if (DateTime.TryParse(rule.NextRunDate, out nr)) _dtpNextRun.Value = nr;

                if (rule.Params != null)
                {
                    var dList = rule.Params.ContainsKey("Days") ? rule.Params["Days"] as System.Collections.ArrayList : null;
                    if (dList != null) {
                        string[] days = { "月", "火", "水", "木", "金", "土", "日" };
                        for (int i = 0; i < 7; i++) _chkDays[i].Checked = dList.Contains(days[i]);
                    }
                    if (rule.Params.ContainsKey("IsEndOfMonth")) _chkEndOfMonth.Checked = Convert.ToBoolean(rule.Params["IsEndOfMonth"]);
                    
                    if (rule.Type == "Task" && rule.BaseTask != null) {
                        var pt = _cmbTaskProj.Items.Cast<ProjectItem>().FirstOrDefault(p => p.ProjectID == rule.BaseTask.ProjectID);
                        if (pt != null) _cmbTaskProj.SelectedItem = pt;
                        _cmbTaskCat.SelectedItem = rule.BaseTask.カテゴリ; _cmbTaskPrio.SelectedItem = rule.BaseTask.優先度;
                        _cmbTaskStatus.SelectedItem = !string.IsNullOrEmpty(rule.BaseTask.進捗度) ? rule.BaseTask.進捗度 : "未実施";
                        _cmbTaskNotify.SelectedItem = !string.IsNullOrEmpty(rule.BaseTask.通知設定) ? rule.BaseTask.通知設定 : "全体設定に従う";
                        _cmbTaskSubCat.SelectedItem = rule.BaseTask.サブカテゴリ; _txtTaskContent.Text = rule.BaseTask.タスク;
                    } else if (rule.Type == "Project" && rule.Params.ContainsKey("SourceProjectID")) {
                        var src = _cmbSourceProj.Items.Cast<object>().FirstOrDefault(p => { var pi = p as ProjectItem; return pi != null && pi.ProjectID == rule.Params["SourceProjectID"].ToString(); });
                        if (src != null) _cmbSourceProj.SelectedItem = src;
                    } else if (rule.Type == "Event") {
                        _chkEventAllDay.Checked = rule.Params.ContainsKey("IsAllDay") && Convert.ToBoolean(rule.Params["IsAllDay"]);
                        DateTime st;
                        if (rule.Params.ContainsKey("StartTime") && DateTime.TryParse(rule.Params["StartTime"].ToString(), out st)) _dtpEventStart.Value = st;
                        DateTime et;
                        if (rule.Params.ContainsKey("EndTime") && DateTime.TryParse(rule.Params["EndTime"].ToString(), out et)) _dtpEventEnd.Value = et;
                    }
                    
                    if (rule.Params.ContainsKey("DueOffset")) _numDueOffset.Value = Convert.ToInt32(rule.Params["DueOffset"]);
                    if (rule.Params.ContainsKey("DueOffsetUnit") && _cmbDueOffsetUnit.Items.Contains(rule.Params["DueOffsetUnit"].ToString())) _cmbDueOffsetUnit.SelectedItem = rule.Params["DueOffsetUnit"].ToString();
                    if (rule.Params.ContainsKey("DueWeekendShift")) { var dw = _cmbDueWeekendShift.Items.Cast<ComboItem>().FirstOrDefault(i => i.Value == rule.Params["DueWeekendShift"].ToString()); if (dw != null) _cmbDueWeekendShift.SelectedItem = dw; }
                    if (rule.Params.ContainsKey("DueHolidayShift")) { var dh = _cmbDueHolidayShift.Items.Cast<ComboItem>().FirstOrDefault(i => i.Value == rule.Params["DueHolidayShift"].ToString()); if (dh != null) _cmbDueHolidayShift.SelectedItem = dh; }
                }
                UpdateUIState();
            }
        }

        private void UpdateUIState()
        {
            if (_radioEvent == null) return;
            bool isTask = _radioTask.Checked; bool isProject = _radioProject.Checked; bool isEvent = _radioEvent.Checked;

            if (_radioGenScheduled.Checked) { _pnlPreGen.Visible = true; _numPreGen.Enabled = _chkTriggerPreGen.Checked; } else _pnlPreGen.Visible = false;
            if (isEvent) { _radioGenScheduled.Checked = true; _pnlGenTiming.Enabled = false; _pnlGenTiming.Visible = false; _grpDueSettings.Visible = false; }
            else { _pnlGenTiming.Enabled = true; _pnlGenTiming.Visible = true; _grpDueSettings.Visible = true; }
            
            _pnlGenAvoidance.Visible = true;
            string freq = _cmbIntervalPreset.SelectedItem != null ? _cmbIntervalPreset.SelectedItem.ToString() : null;
            bool isCustom = (freq == "その他");
            _numInterval.Visible = isCustom; _lblIntervalSuffix.Visible = isCustom;
            foreach (var c in _chkDays) c.Visible = (freq == "毎週");
            _chkEndOfMonth.Visible = (freq == "毎月 (同日)");
            _numInterval.Enabled = isCustom;
            if (freq == "毎日") _numInterval.Value = 1; if (freq == "毎週") _numInterval.Value = 7; if (freq == "毎月 (同日)") _numInterval.Value = 1;

            _grpDetails.Visible = true;
            _pnlTaskRow1.Visible = isTask; _pnlTaskRow2.Visible = isTask; _pnlTaskRow3.Visible = isTask; _pnlTaskRow4.Visible = isTask;
            _pnlProjDetails.Visible = isProject; _pnlTime.Visible = isEvent; _lblProjWarning.Visible = isProject;
        }

        private void CalculatePreview()
        {
            if (_lblPreview == null || _cmbIntervalPreset.SelectedItem == null) return;
            DateTime baseDate = _dtpNextRun.Value.Date;
            string freq = _cmbIntervalPreset.SelectedItem != null ? _cmbIntervalPreset.SelectedItem.ToString() : "毎日";
            int interval = (int)_numInterval.Value;
            var gwItem = _cmbGenWeekendShift.SelectedItem as ComboItem; var ghItem = _cmbGenHolidayShift.SelectedItem as ComboItem;
            var rule = new RecurringRule { Frequency = freq, IntervalDays = interval, IntervalUnit = (freq == "毎月 (同日)") ? "Month" : "Day", WeekendShift = gwItem != null ? gwItem.Value : "None", HolidayShift = ghItem != null ? ghItem.Value : "None", Params = new Dictionary<string, object>() };
            if (freq == "毎週") { var sD = new List<string>(); string[] ds = { "月", "火", "水", "木", "金", "土", "日" }; for (int i = 0; i < 7; i++) if (_chkDays[i].Checked) sD.Add(ds[i]); rule.Params["Days"] = sD; }
            else if (freq == "毎月 (同日)") { rule.Params["IsEndOfMonth"] = _chkEndOfMonth.Checked; rule.Params["Day"] = baseDate.Day; }

            var nextOcc = GetNextRecurringDate(baseDate, rule);
            DateTime actualDate = nextOcc.ActualDate;
            string previewText = string.Format("次回生成: {0:yyyy/MM/dd (ddd)}", actualDate);
            Color foreColor = Color.Black;

            if (_radioTask.Checked || _radioProject.Checked) {
                int dueOffset = (int)_numDueOffset.Value; string dueUnit = _cmbDueOffsetUnit.SelectedItem != null ? _cmbDueOffsetUnit.SelectedItem.ToString() : null;
                var dwItem = _cmbDueWeekendShift.SelectedItem as ComboItem; string dueWShift = dwItem != null ? dwItem.Value : "None";
                var dhItem = _cmbDueHolidayShift.SelectedItem as ComboItem; string dueHShift = dhItem != null ? dhItem.Value : "None";
                DateTime tempDue = actualDate;
                if (dueUnit == "日後") tempDue = actualDate.AddDays(dueOffset); else if (dueUnit == "週間後") tempDue = actualDate.AddDays(dueOffset * 7); else if (dueUnit == "ヶ月後") tempDue = actualDate.AddMonths(dueOffset);
                var holidays = _dataService.GetHolidays();
                for (int i = 0; i < 30; i++) {
                    string dStr = tempDue.ToString("yyyy-MM-dd"); bool isHol = holidays.ContainsKey(dStr); bool isWe = tempDue.DayOfWeek == DayOfWeek.Saturday || tempDue.DayOfWeek == DayOfWeek.Sunday;
                    if (!isHol && !isWe) break;
                    bool shifted = false;
                    if (isHol) { if (dueHShift == "Before") { tempDue = tempDue.AddDays(-1); shifted = true; } else if (dueHShift == "After") { tempDue = tempDue.AddDays(1); shifted = true; } }
                    if (!shifted && isWe) {
                        if (dueWShift == "Friday") { tempDue = tempDue.AddDays(tempDue.DayOfWeek == DayOfWeek.Saturday ? -1 : -2); shifted = true; }
                        else if (dueWShift == "Monday") { tempDue = tempDue.AddDays(tempDue.DayOfWeek == DayOfWeek.Saturday ? 2 : 1); shifted = true; }
                    }
                    if (!shifted) break;
                }
                previewText += string.Format(" ｜ アイテム期日: {0:yyyy/MM/dd (ddd)}", tempDue);
            } else if (_radioEvent.Checked) { previewText += string.Format(" ｜ 時間: {0:HH:mm} - {1:HH:mm}", _dtpEventStart.Value, _dtpEventEnd.Value); }

            if (nextOcc.IsAdjusted) { previewText += " (月末補正あり)"; foreColor = Color.DarkBlue; }
            if (actualDate.Date < DateTime.Today) { previewText += " (過去日 - 次回起動時に再計算)"; foreColor = Color.DimGray; }
            _lblPreview.Text = previewText; _lblPreview.ForeColor = foreColor;
        }

        private class RecurringDateResult { public DateTime TheoreticalDate { get; set; } public DateTime ActualDate { get; set; } public bool IsAdjusted { get; set; } }
        private RecurringDateResult GetNextRecurringDate(DateTime baseDate, RecurringRule rule)
        {
            DateTime nextTheo = baseDate; int interval = rule.IntervalDays > 0 ? rule.IntervalDays : 1; bool isAdj = false;
            if (!string.IsNullOrEmpty(rule.Frequency)) {
                if (rule.Frequency == "毎日") nextTheo = baseDate.AddDays(interval);
                else if (rule.Frequency == "毎週") nextTheo = baseDate.AddDays(7 * interval);
                else if (rule.Frequency == "毎月 (同日)") {
                    bool isEnd = rule.Params != null && rule.Params.ContainsKey("IsEndOfMonth") && Convert.ToBoolean(rule.Params["IsEndOfMonth"]);
                    DateTime nextMonth = baseDate.AddMonths(interval); int daysInM = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
                    if (isEnd) { nextTheo = new DateTime(nextMonth.Year, nextMonth.Month, daysInM); isAdj = true; }
                    else {
                        int targetDay = rule.Params != null && rule.Params.ContainsKey("Day") ? Convert.ToInt32(rule.Params["Day"]) : baseDate.Day;
                        int actualDay = Math.Min(targetDay, daysInM); nextTheo = new DateTime(nextMonth.Year, nextMonth.Month, actualDay); if (targetDay > daysInM) isAdj = true;
                    }
                } else nextTheo = baseDate.AddDays(interval);
            }
            DateTime actualDate = nextTheo; var holidays = _dataService.GetHolidays();
            for (int i = 0; i < 30; i++) {
                string dateStr = actualDate.ToString("yyyy-MM-dd"); bool isHol = holidays.ContainsKey(dateStr); bool isWe = actualDate.DayOfWeek == DayOfWeek.Saturday || actualDate.DayOfWeek == DayOfWeek.Sunday;
                if (!isHol && !isWe) break;
                bool shifted = false;
                if (isHol) { if (rule.HolidayShift == "Before") { actualDate = actualDate.AddDays(-1); shifted = true; } else if (rule.HolidayShift == "After") { actualDate = actualDate.AddDays(1); shifted = true; } }
                if (!shifted && isWe) {
                    if (rule.WeekendShift == "Friday") { actualDate = actualDate.AddDays(actualDate.DayOfWeek == DayOfWeek.Saturday ? -1 : -2); shifted = true; }
                    else if (rule.WeekendShift == "Monday") { actualDate = actualDate.AddDays(actualDate.DayOfWeek == DayOfWeek.Saturday ? 2 : 1); shifted = true; }
                }
                if (!shifted) break;
            }
            return new RecurringDateResult { TheoreticalDate = nextTheo, ActualDate = actualDate, IsAdjusted = isAdj };
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtTitle.Text)) { MessageBox.Show("タイトルを入力してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            string type = _radioTask.Checked ? "Task" : (_radioProject.Checked ? "Project" : "Event");
            var triggers = new List<string>();
            if (_radioGenCompletion.Checked) triggers.Add("OnCompletion");
            if (_radioGenScheduled.Checked) triggers.Add("OnExpiration");
            if (_radioGenScheduled.Checked && _chkTriggerPreGen.Checked) triggers.Add("PreGeneration");
            string freq = _cmbIntervalPreset.SelectedItem != null ? _cmbIntervalPreset.SelectedItem.ToString() : "毎日";

            var ruleParams = new Dictionary<string, object>();
            if (freq == "毎週") {
                var selDays = new List<string>(); string[] days = { "月", "火", "水", "木", "金", "土", "日" };
                for (int i = 0; i < 7; i++) if (_chkDays[i].Checked) selDays.Add(days[i]);
                ruleParams["Days"] = selDays;
            } else if (freq == "毎月 (同日)") { ruleParams["IsEndOfMonth"] = _chkEndOfMonth.Checked; ruleParams["Day"] = _dtpNextRun.Value.Day; }

            ruleParams["DueOffset"] = (int)_numDueOffset.Value;
            ruleParams["DueOffsetUnit"] = _cmbDueOffsetUnit.SelectedItem != null ? _cmbDueOffsetUnit.SelectedItem.ToString() : "日後";
            var dws = _cmbDueWeekendShift.SelectedItem as ComboItem;
            ruleParams["DueWeekendShift"] = dws != null ? dws.Value : "None";
            var dhs = _cmbDueHolidayShift.SelectedItem as ComboItem;
            ruleParams["DueHolidayShift"] = dhs != null ? dhs.Value : "None";

            TaskItem baseTask = null;
            if (type == "Task") {
                if (_cmbTaskProj.SelectedItem == null) { MessageBox.Show("プロジェクトを選択してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                var projItem = _cmbTaskProj.SelectedItem as ProjectItem;
                baseTask = new TaskItem {
                    ProjectID = projItem != null ? projItem.ProjectID : null, カテゴリ = _cmbTaskCat.SelectedItem != null ? _cmbTaskCat.SelectedItem.ToString() : "", 優先度 = _cmbTaskPrio.SelectedItem != null ? _cmbTaskPrio.SelectedItem.ToString() : "中",
                    進捗度 = _cmbTaskStatus.SelectedItem != null ? _cmbTaskStatus.SelectedItem.ToString() : "未実施", 通知設定 = _cmbTaskNotify.SelectedItem != null ? _cmbTaskNotify.SelectedItem.ToString() : "全体設定に従う", サブカテゴリ = _cmbTaskSubCat.SelectedItem != null ? _cmbTaskSubCat.SelectedItem.ToString() : "",
                    タスク = string.IsNullOrWhiteSpace(_txtTaskContent.Text) ? _txtTitle.Text.Trim() : _txtTaskContent.Text
                };
            } else if (type == "Project") {
                var srcProj = _cmbSourceProj.SelectedItem as ProjectItem;
                if (srcProj != null) ruleParams["SourceProjectID"] = srcProj.ProjectID;
            } else if (type == "Event") {
                ruleParams["IsAllDay"] = _chkEventAllDay.Checked;
                if (!_chkEventAllDay.Checked) { ruleParams["StartTime"] = _dtpEventStart.Value.ToString("HH:mm"); ruleParams["EndTime"] = _dtpEventEnd.Value.ToString("HH:mm"); }
            }

            string id = _dgv.SelectedRows.Count > 0 ? _dgv.SelectedRows[0].Cells["RuleID"].Value.ToString() : Guid.NewGuid().ToString();
            var existing = _rules.FirstOrDefault(r => r.RuleID == id);
            string nextRun = _dtpNextRun.Value.ToString("yyyy-MM-dd");

            if (existing != null) {
                existing.Type = type; existing.TaskName = _txtTitle.Text.Trim(); existing.TriggerModes = triggers; existing.CalculationBase = _radioGenCompletion.Checked ? "Completion" : "Generation";
                existing.IntervalDays = (int)_numInterval.Value; existing.IntervalUnit = (freq == "毎月 (同日)") ? "Month" : "Day"; existing.Frequency = freq; existing.PreGenDays = (int)_numPreGen.Value;
                var gw = _cmbGenWeekendShift.SelectedItem as ComboItem; var gh = _cmbGenHolidayShift.SelectedItem as ComboItem;
                existing.WeekendShift = gw != null ? gw.Value : "None"; existing.HolidayShift = gh != null ? gh.Value : "None"; existing.Params = ruleParams;
                existing.BaseTask = baseTask; existing.IsActive = _chkIsActive.Checked;
            } else {
                var gw = _cmbGenWeekendShift.SelectedItem as ComboItem; var gh = _cmbGenHolidayShift.SelectedItem as ComboItem;
                var newRule = new RecurringRule {
                    RuleID = id, Type = type, TaskName = _txtTitle.Text.Trim(), TriggerModes = triggers, CalculationBase = _radioGenCompletion.Checked ? "Completion" : "Generation",
                    IntervalDays = (int)_numInterval.Value, IntervalUnit = (freq == "毎月 (同日)") ? "Month" : "Day", Frequency = freq, PreGenDays = (int)_numPreGen.Value,
                    WeekendShift = gw != null ? gw.Value : "None", HolidayShift = gh != null ? gh.Value : "None", Params = ruleParams,
                    NextRunDate = nextRun, TheoreticalDate = nextRun, BaseTask = baseTask, IsActive = _chkIsActive.Checked
                };
                if (_initialItem != null) {
                    var ti = _initialItem as TaskItem;
                    var pi = _initialItem as ProjectItem;
                    var evi = _initialItem as EventItem;
                    if (ti != null) newRule.CurrentInstanceID = ti.ID;
                    else if (pi != null) newRule.CurrentInstanceID = pi.ProjectID;
                    else if (evi != null) newRule.CurrentInstanceID = evi.ID;
                }
                _rules.Add(newRule);
            }

            _dataService.SaveToJson(_dataService.RecurringRulesFile, _rules);
            MessageBox.Show("定期実行ルールを保存しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.Close();
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (_dgv.SelectedRows.Count > 0 && _dgv.SelectedRows[0].DataBoundItem as RecurringRule != null)
            {
                var rule = _dgv.SelectedRows[0].DataBoundItem as RecurringRule;
                if (MessageBox.Show(string.Format("定期ルール '{0}' を削除しますか？", rule.TaskName), "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    _rules.Remove(rule);
                    _dataService.SaveToJson(_dataService.RecurringRulesFile, _rules);
                    LoadData();
                }
            }
        }
    }
}
