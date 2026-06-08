using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UniConsul.Models;
using UniConsul.Services;
using UniConsul.Utils;
using System.IO;

namespace UniConsul.Forms
{
    public class FormTaskInput : Form
    {
        private TaskItem _existingTask;
        private List<ProjectItem> _projects;
        private Dictionary<string, List<string>> _categories;

        public TaskItem ResultTask { get; private set; }

        private const int DTM_GETMONTHCAL = 0x1008;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        private DataService _dataService;

        private CheckBox chkRecurring;
        private GroupBox grpRecurring;
        private ComboBox cmbFreq;
        private Panel pnlFreqOptions;
        private CheckBox[] chkDays;
        private NumericUpDown numDay;
        private Label lblGeneratedInfo;

        private ComboBox comboProject;
        private DateTimePicker datePicker;
        private ComboBox comboPriority;
        private ComboBox comboStatus;
        private ComboBox comboNotify;
        private ComboBox comboCategory;
        private ComboBox comboSubCategory;
        private NumericUpDown numTargetHours;
        private CheckBox chkEnableTarget;
        private Label lblTargetHint;
        private Button btnApplyHint;
        private List<TaskItem> _allTasks;
        private TextBox textTask;
        private Button buttonSave;
        private Button buttonCancel;

        public FormTaskInput(TaskItem existingTask, string projectIDForNew, List<ProjectItem> projects, Dictionary<string, List<string>> categories)
        {
            _existingTask = existingTask;
            _projects = projects ?? new List<ProjectItem>();
            _categories = categories ?? new Dictionary<string, List<string>>();

            _dataService = new DataService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskManager"));
            _allTasks = _dataService.LoadTasksFromCsv(_dataService.TasksFile) ?? new List<TaskItem>();

            InitializeComponent();
            LoadData(projectIDForNew);

            // 💡 ウィンドウサイズの記憶と復元を有効化
            var settings = _dataService.LoadSettings();
            ThemeManager.EnableDynamicResizing(this, settings, () => _dataService.SaveToJson(_dataService.SettingsFile, settings));

            DataService.DataUpdated += DataService_DataUpdated;
        }

        private void DataService_DataUpdated(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => DataService_DataUpdated(sender, e)));
                return;
            }
            
            _projects = _dataService.LoadFromJson<List<ProjectItem>>(_dataService.ProjectsFile, new List<ProjectItem>());
            _categories = _dataService.LoadFromJson<Dictionary<string, List<string>>>(_dataService.CategoriesFile, new Dictionary<string, List<string>>());
            _allTasks = _dataService.LoadTasksFromCsv(_dataService.TasksFile) ?? new List<TaskItem>();

            object selProj = comboProject.SelectedItem;
            comboProject.Items.Clear();
            foreach (var proj in _projects.OrderBy(p => p.ProjectName)) comboProject.Items.Add(proj);
            if (selProj != null) {
                var p = selProj as ProjectItem;
                var match = _projects.FirstOrDefault(x => x.ProjectID == p.ProjectID);
                if (match != null) comboProject.SelectedItem = match;
            }

            object selCat = comboCategory.SelectedItem;
            comboCategory.Items.Clear();
            comboCategory.Items.AddRange(_categories.Keys.ToArray());
            if (selCat != null && _categories.ContainsKey(selCat.ToString())) comboCategory.SelectedItem = selCat;
        }

        private void InitializeComponent()
        {
            this.Text = _existingTask != null ? "タスクの編集" : "プロジェクト／タスクの新規追加";
            this.ClientSize = new Size(410, 590);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            // --- プロジェクト ---
            var labelProject = new Label { Text = "プロジェクト：", Location = new Point(15, 15), AutoSize = true };
            this.Controls.Add(labelProject);

            comboProject = new ComboBox
            {
                Location = new Point(15, 35),
                Size = new Size(380, 25),
                DropDownStyle = ComboBoxStyle.DropDown,
                DisplayMember = "ProjectName",
                ValueMember = "ProjectID"
            };
            comboProject.SelectedIndexChanged += (s, e) => SuggestTargetTime();
            this.Controls.Add(comboProject);

            // --- 期日 ---
            var labelDue = new Label { Text = "期日：", Location = new Point(15, 75), AutoSize = true };
            this.Controls.Add(labelDue);

            datePicker = new DateTimePicker
            {
                ShowCheckBox = true,
                Location = new Point(15, 95),
                Width = 180,
                Format = DateTimePickerFormat.Short
            };
            this.Controls.Add(datePicker);

            // --- 優先度 ---
            var labelPriority = new Label { Text = "優先度：", Location = new Point(215, 75), AutoSize = true };
            this.Controls.Add(labelPriority);

            comboPriority = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(215, 95),
                Width = 180
            };
            comboPriority.Items.AddRange(new[] { "高", "中", "低" });
            this.Controls.Add(comboPriority);

            // --- 進捗度 ---
            var labelStatus = new Label { Text = "進捗度：", Location = new Point(15, 135), AutoSize = true };
            this.Controls.Add(labelStatus);

            comboStatus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(15, 155),
                Width = 180
            };
            comboStatus.Items.AddRange(new[] { "未実施", "保留", "実施中", "確認待ち", "完了済み" });
            this.Controls.Add(comboStatus);

            // --- 通知設定 ---
            var labelNotify = new Label { Text = "通知設定：", Location = new Point(215, 135), AutoSize = true };
            this.Controls.Add(labelNotify);

            comboNotify = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(215, 155),
                Width = 180
            };
            comboNotify.Items.AddRange(new[] { "全体設定に従う", "通知しない", "当日", "1日前", "前の営業日", "3日前", "1週間前" });
            this.Controls.Add(comboNotify);

            // --- カテゴリ ---
            var labelCategory = new Label { Text = "カテゴリ：", Location = new Point(15, 195), AutoSize = true };
            this.Controls.Add(labelCategory);

            comboCategory = new ComboBox
            {
                Location = new Point(15, 215),
                Size = new Size(180, 25),
                DropDownStyle = ComboBoxStyle.DropDown
            };
            comboCategory.SelectedIndexChanged += ComboCategory_SelectedIndexChanged;
            this.Controls.Add(comboCategory);

            // --- サブカテゴリ ---
            var labelSubCategory = new Label { Text = "サブカテゴリ：", Location = new Point(215, 195), AutoSize = true };
            this.Controls.Add(labelSubCategory);

            comboSubCategory = new ComboBox
            {
                Location = new Point(215, 215),
                Size = new Size(180, 25),
                DropDownStyle = ComboBoxStyle.DropDown
            };
            this.Controls.Add(comboSubCategory);

            // --- 目標時間 ---
            chkEnableTarget = new CheckBox { Text = "目標時間 (h) を設定する", Location = new Point(15, 253), AutoSize = true };
            chkEnableTarget.CheckedChanged += (s, e) => { numTargetHours.Enabled = chkEnableTarget.Checked; };
            this.Controls.Add(chkEnableTarget);

            numTargetHours = new NumericUpDown
            {
                Location = new Point(15, 275),
                Width = 80,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Maximum = 9999m,
                Minimum = 0m,
                Enabled = false
            };
            this.Controls.Add(numTargetHours);

            lblTargetHint = new Label { Text = "", Location = new Point(105, 278), AutoSize = true, ForeColor = Color.Gray };
            this.Controls.Add(lblTargetHint);

            btnApplyHint = new Button { Text = "適用", Location = new Point(200, 274), Size = new Size(50, 24), Visible = false };
            btnApplyHint.Click += btnApplyHint_Click;
            this.Controls.Add(btnApplyHint);

            // --- タスク内容 ---
            var labelTask = new Label { Text = "タスク内容：", Location = new Point(15, 315), AutoSize = true };
            this.Controls.Add(labelTask);

            textTask = new TextBox
            {
                Multiline = true,
                Location = new Point(15, 335),
                Size = new Size(380, 80),
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true
            };
            this.Controls.Add(textTask);

            // --- 定期設定 ---
            lblGeneratedInfo = new Label { Text = "ℹ️ このタスクは定期実行ルールから生成されました", Location = new Point(15, 425), AutoSize = true, ForeColor = Color.Gray, Visible = false };
            this.Controls.Add(lblGeneratedInfo);

            chkRecurring = new CheckBox { Text = "このタスクを定期実行（ルーチン）にする", Location = new Point(15, 425), AutoSize = true };
            this.Controls.Add(chkRecurring);

            grpRecurring = new GroupBox { Text = "定期設定", Location = new Point(15, 450), Size = new Size(380, 85) };
            this.Controls.Add(grpRecurring);

            chkRecurring.CheckedChanged += (s, e) => { 
                cmbFreq.Enabled = chkRecurring.Checked;
                pnlFreqOptions.Enabled = chkRecurring.Checked;
            };

            grpRecurring.Controls.Add(new Label { Text = "頻度:", Location = new Point(10, 25), AutoSize = true });
            cmbFreq = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(50, 22), Width = 100, Enabled = false };
            cmbFreq.Items.AddRange(new[] { "毎日", "毎週", "毎月" });
            cmbFreq.SelectedIndex = 0;
            grpRecurring.Controls.Add(cmbFreq);

            pnlFreqOptions = new Panel { Location = new Point(10, 55), Size = new Size(360, 25), Enabled = false };
            grpRecurring.Controls.Add(pnlFreqOptions);

            chkDays = new CheckBox[7];
            string[] days = { "月", "火", "水", "木", "金", "土", "日" };
            for (int i = 0; i < 7; i++) {
                chkDays[i] = new CheckBox { Text = days[i], AutoSize = true, Location = new Point(i * 45, 2), Visible = false };
                pnlFreqOptions.Controls.Add(chkDays[i]);
            }

            var lblDay = new Label { Text = "毎月:", Location = new Point(0, 5), AutoSize = true, Visible = false };
            numDay = new NumericUpDown { Location = new Point(40, 2), Width = 50, Minimum = 1, Maximum = 31, Visible = false };
            var lblDaySuffix = new Label { Text = "日", Location = new Point(95, 5), AutoSize = true, Visible = false };
            pnlFreqOptions.Controls.AddRange(new Control[] { lblDay, numDay, lblDaySuffix });

            cmbFreq.SelectedIndexChanged += (s, e) => {
                string freq = cmbFreq.SelectedItem.ToString();
                foreach (var c in chkDays) c.Visible = (freq == "毎週");
                lblDay.Visible = numDay.Visible = lblDaySuffix.Visible = (freq == "毎月");
            };

            // --- ボタン ---
            buttonSave = new Button { Text = "保存", Location = new Point(225, 545), Size = new Size(80, 30) };
            buttonSave.Click += ButtonSave_Click;
            this.Controls.Add(buttonSave);

            buttonCancel = new Button { Text = "キャンセル", Location = new Point(315, 545), Size = new Size(80, 30) };
            buttonCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(buttonCancel);

            this.AcceptButton = buttonSave;
            this.CancelButton = buttonCancel;
            this.ActiveControl = textTask;
        }
        
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            DataService.DataUpdated -= DataService_DataUpdated;
            base.OnFormClosed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var settings = _dataService.LoadSettings();
            bool isDark = settings != null && settings.IsDarkMode; // 💡 設定ファイルから正確にダークモード状態を取得

            ThemeManager.ApplyDarkModeToWindow(this.Handle, isDark);
            ThemeManager.ApplyTheme(this, isDark);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var settings = _dataService.LoadSettings();
            bool isDark = settings != null && settings.IsDarkMode;
            ThemeManager.ApplyDarkModeToWindow(this.Handle, isDark);
        }

        private void LoadData(string projectIDForNew)
        {
            // カテゴリの初期化
            comboCategory.Items.AddRange(_categories.Keys.ToArray());

            // プロジェクトの初期化
            foreach (var proj in _projects.OrderBy(p => p.ProjectName))
            {
                comboProject.Items.Add(proj);
            }

            if (_existingTask != null)
            {
                var selectedProj = _projects.FirstOrDefault(p => p.ProjectID == _existingTask.ProjectID);
                if (selectedProj != null) comboProject.SelectedItem = selectedProj;

                DateTime dueDate;
                if (DateTime.TryParse(_existingTask.期日, out dueDate))
                {
                    datePicker.Value = dueDate;
                    datePicker.Checked = true;
                }
                else
                {
                    datePicker.Checked = false;
                }

                comboPriority.SelectedItem = _existingTask.優先度;
                comboStatus.SelectedItem = _existingTask.進捗度;
                textTask.Text = _existingTask.タスク;
                comboNotify.SelectedItem = _existingTask.通知設定;

                if (!string.IsNullOrEmpty(_existingTask.カテゴリ) && comboCategory.Items.Contains(_existingTask.カテゴリ))
                {
                    comboCategory.SelectedItem = _existingTask.カテゴリ;
                    if (!string.IsNullOrEmpty(_existingTask.サブカテゴリ) && comboSubCategory.Items.Contains(_existingTask.サブカテゴリ))
                    {
                        comboSubCategory.SelectedItem = _existingTask.サブカテゴリ;
                    }
                }

                if (_existingTask.TargetHours.HasValue)
                {
                    chkEnableTarget.Checked = true;
                    numTargetHours.Value = (decimal)_existingTask.TargetHours.Value;
                }
                else
                {
                    chkEnableTarget.Checked = false;
                    numTargetHours.Value = 0m;
                }

                if (!string.IsNullOrEmpty(_existingTask.ParentRuleID)) {
                    chkRecurring.Visible = false;
                    grpRecurring.Visible = false;
                    lblGeneratedInfo.Visible = true;
                } else {
                    var rules = _dataService.LoadFromJson<List<RecurringRule>>(_dataService.RecurringRulesFile, new List<RecurringRule>());
                    var matchedRule = rules.FirstOrDefault(r => r.TaskName == _existingTask.タスク);
                    if (matchedRule != null) {
                        chkRecurring.Checked = true;
                        cmbFreq.Enabled = true;
                        pnlFreqOptions.Enabled = true;
                        if (cmbFreq.Items.Contains(matchedRule.Frequency)) cmbFreq.SelectedItem = matchedRule.Frequency;
                        if (matchedRule.Frequency == "毎週" && matchedRule.Params != null && matchedRule.Params.ContainsKey("Days")) {
                            var dList = matchedRule.Params["Days"] as System.Collections.ArrayList;
                            if (dList != null) {
                                string[] days = { "月", "火", "水", "木", "金", "土", "日" };
                                for (int i=0; i<7; i++) if (dList.Contains(days[i])) chkDays[i].Checked = true;
                            }
                        } else if (matchedRule.Frequency == "毎月" && matchedRule.Params != null && matchedRule.Params.ContainsKey("Day")) {
                            int d;
                            if (int.TryParse(matchedRule.Params["Day"].ToString(), out d)) numDay.Value = d;
                        }
                    }
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(projectIDForNew))
                {
                    var selectedProj = _projects.FirstOrDefault(p => p.ProjectID == projectIDForNew);
                    if (selectedProj != null) comboProject.SelectedItem = selectedProj;
                }
                datePicker.Value = DateTime.Today.AddDays(1);
                datePicker.Checked = true;
                comboPriority.SelectedIndex = 1; // 中
                comboStatus.SelectedIndex = 0;   // 未実施
                comboNotify.SelectedIndex = 0;   // 全体設定に従う
            }
        }

        private void ComboCategory_SelectedIndexChanged(object sender, EventArgs e)
        {
            comboSubCategory.Items.Clear();
            string selectedCat = comboCategory.SelectedItem != null ? comboCategory.SelectedItem.ToString() : null;
            if (!string.IsNullOrEmpty(selectedCat) && _categories.ContainsKey(selectedCat))
            {
                comboSubCategory.Items.AddRange(_categories[selectedCat].ToArray());
            }
            SuggestTargetTime();
        }

        private void SuggestTargetTime()
        {
            if (comboCategory.SelectedItem == null || string.IsNullOrWhiteSpace(comboCategory.SelectedItem.ToString()))
            {
                lblTargetHint.Text = "";
                lblTargetHint.Tag = null;
                btnApplyHint.Visible = false;
                return;
            }

            string selectedCategory = comboCategory.SelectedItem.ToString();
            string currentProjectId = "";

            var selectedProj = comboProject.SelectedItem as ProjectItem;
            if (selectedProj != null)
            {
                currentProjectId = selectedProj.ProjectID;
            }

            var baseValidTasks = _allTasks.Where(t => t.進捗度 == "完了済み" && t.TrackedTimeSeconds > 0);

            var firstPriorityTasks = baseValidTasks.Where(t => t.ProjectID == currentProjectId && t.カテゴリ == selectedCategory).ToList();

            double averageSeconds = 0;

            if (firstPriorityTasks.Any())
            {
                averageSeconds = firstPriorityTasks.Average(t => t.TrackedTimeSeconds);
            }
            else
            {
                var secondPriorityTasks = baseValidTasks.Where(t => t.カテゴリ == selectedCategory).ToList();
                if (secondPriorityTasks.Any())
                {
                    averageSeconds = secondPriorityTasks.Average(t => t.TrackedTimeSeconds);
                }
            }

            if (averageSeconds > 0)
            {
                double averageHours = averageSeconds / 3600.0;
                decimal targetHours = Math.Round((decimal)averageHours, 1);

                lblTargetHint.Text = string.Format("💡 平均 {0:F1}h", targetHours);
                lblTargetHint.Tag = targetHours;
                btnApplyHint.Visible = true;

                bool isEditMode = _existingTask != null;

                if (!isEditMode && !chkEnableTarget.Checked)
                {
                    chkEnableTarget.Checked = true;
                    
                    if (targetHours < numTargetHours.Minimum) targetHours = numTargetHours.Minimum;
                    if (targetHours > numTargetHours.Maximum) targetHours = numTargetHours.Maximum;

                    numTargetHours.Value = targetHours;
                }
            }
            else
            {
                lblTargetHint.Text = "";
                lblTargetHint.Tag = null;
                btnApplyHint.Visible = false;
            }
        }

        private void btnApplyHint_Click(object sender, EventArgs e)
        {
            decimal targetHours;
            if (lblTargetHint.Tag != null && decimal.TryParse(lblTargetHint.Tag.ToString(), out targetHours))
            {
                chkEnableTarget.Checked = true;
                
                if (targetHours < numTargetHours.Minimum) targetHours = numTargetHours.Minimum;
                if (targetHours > numTargetHours.Maximum) targetHours = numTargetHours.Maximum;

                numTargetHours.Value = targetHours;
            }
        }

        private async void ButtonSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(comboProject.Text))
            {
                MessageBox.Show("プロジェクトは必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(textTask.Text))
            {
                MessageBox.Show("タスク内容は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(comboCategory.Text))
            {
                MessageBox.Show("カテゴリは必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 編集モードなら既存のオブジェクトを更新、新規なら新しいインスタンスを作成
            ResultTask = _existingTask ?? new TaskItem { ID = Guid.NewGuid().ToString(), 保存日付 = DateTime.Today.ToString("yyyy-MM-dd") };
            
            ResultTask.タスク = textTask.Text.Trim();
            ResultTask.期日 = datePicker.Checked ? datePicker.Value.ToString("yyyy-MM-dd") : "";
            ResultTask.優先度 = comboPriority.SelectedItem != null ? comboPriority.SelectedItem.ToString() : null;
            ResultTask.進捗度 = comboStatus.SelectedItem != null ? comboStatus.SelectedItem.ToString() : null;
            ResultTask.通知設定 = comboNotify.SelectedItem != null ? comboNotify.SelectedItem.ToString() : null;
            ResultTask.カテゴリ = !string.IsNullOrWhiteSpace(comboCategory.Text) ? comboCategory.Text.Trim() : null;
            ResultTask.サブカテゴリ = !string.IsNullOrWhiteSpace(comboSubCategory.Text) ? comboSubCategory.Text.Trim() : "";
            ResultTask.TargetHours = chkEnableTarget.Checked && numTargetHours.Value > 0 ? (double?)numTargetHours.Value : null;

            // プロジェクトIDの解決ロジック（自由入力による新規プロジェクト作成）
            var selectedProj = comboProject.SelectedItem as ProjectItem;
            if (selectedProj != null)
            {
                ResultTask.ProjectID = selectedProj.ProjectID;
            }
            else
            {
                // 新規プロジェクトが入力された場合、親フォーム側で Projects リストへの追加処理が必要です。
                // 一時的に ProjectName プロパティに文字列として格納し、親フォームで解決するフラグとします。
                ResultTask.ProjectName = comboProject.Text.Trim(); 
                ResultTask.ProjectID = ""; // 親側で Guid 採番する
            }

            // カテゴリとサブカテゴリの新規追加処理
            if (!string.IsNullOrEmpty(ResultTask.カテゴリ))
            {
                bool categoryUpdated = false;
                if (!_categories.ContainsKey(ResultTask.カテゴリ))
                {
                    _categories[ResultTask.カテゴリ] = new List<string>();
                    categoryUpdated = true;
                }
                if (!string.IsNullOrEmpty(ResultTask.サブカテゴリ) && !_categories[ResultTask.カテゴリ].Contains(ResultTask.サブカテゴリ))
                {
                    _categories[ResultTask.カテゴリ].Add(ResultTask.サブカテゴリ);
                    categoryUpdated = true;
                }
                
                if (categoryUpdated)
                {
                    _dataService.SaveToJson(_dataService.CategoriesFile, _categories);
                }
            }

            if (chkRecurring.Visible) {
                var rules = _dataService.LoadFromJson<List<RecurringRule>>(_dataService.RecurringRulesFile, new List<RecurringRule>());
                string originalTaskName = (_existingTask != null && _existingTask.タスク != null) ? _existingTask.タスク : "";
                string newTaskName = ResultTask.タスク;
                bool ruleUpdated = false;

                if (chkRecurring.Checked) {
                    var ruleParams = new Dictionary<string, object>();
                    if (cmbFreq.SelectedItem.ToString() == "毎週") {
                        var selectedDays = new List<string>();
                        string[] days = { "月", "火", "水", "木", "金", "土", "日" };
                        for (int i=0; i<7; i++) if (chkDays[i].Checked) selectedDays.Add(days[i]);
                        ruleParams["Days"] = selectedDays;
                    } else if (cmbFreq.SelectedItem.ToString() == "毎月") {
                        ruleParams["Day"] = (int)numDay.Value;
                    }

                    var existingRule = !string.IsNullOrEmpty(originalTaskName) ? rules.FirstOrDefault(r => r.TaskName == originalTaskName) : null;
                    if (existingRule != null) {
                        existingRule.TaskName = newTaskName;
                        existingRule.Frequency = cmbFreq.SelectedItem.ToString();
                        existingRule.Params = ruleParams;
                        existingRule.BaseTask = ResultTask;
                    } else {
                        var newRule = new RecurringRule {
                            RuleID = Guid.NewGuid().ToString(), Type = "Task", TaskName = newTaskName, Frequency = cmbFreq.SelectedItem.ToString(),
                            Params = ruleParams, BaseTask = ResultTask, IsActive = true, NextRunDate = DateTime.Today.ToString("yyyy-MM-dd"), TheoreticalDate = DateTime.Today.ToString("yyyy-MM-dd")
                        };
                        rules.Add(newRule);
                    }
                    ruleUpdated = true;
                } else {
                    if (!string.IsNullOrEmpty(originalTaskName)) {
                        var ruleToDelete = rules.FirstOrDefault(r => r.TaskName == originalTaskName);
                        if (ruleToDelete != null) { rules.Remove(ruleToDelete); ruleUpdated = true; }
                    }
                }
                if (ruleUpdated) _dataService.SaveToJson(_dataService.RecurringRulesFile, rules);
            }

            await UIUtility.ShowSaveFeedbackAndClose(this, "タスクを保存しました");
        }
    }
}
