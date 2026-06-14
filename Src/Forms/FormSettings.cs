﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using UniConsul.Models;
using System.Text.Json;
using UniConsul.Services;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UniConsul.Forms
{
    public class FormSettings : Form
    {
        public class ComboItem
        {
            public string Text { get; set; }
            public string Value { get; set; }
            public override string ToString() { return Text; }
        }

        private AppSettings _settings;
        public AppSettings ResultSettings { get; private set; }

        private Dictionary<string, NumericUpDown> weightInputs = new Dictionary<string, NumericUpDown>();
        private Dictionary<string, int> defaultWeights = new Dictionary<string, int> { { "足切りライン", 30 }, { "ファイルパス合致", 40 }, { "学習辞書合致", 30 }, { "完全一致", 30 }, { "カテゴリ辞書合致", 25 }, { "単語包含(複数)", 20 }, { "専用ソフト一致", 20 }, { "直前タスクの継続", 20 }, { "単語包含(単一)", 10 }, { "期限超過", 10 }, { "時間帯のパターン", 5 }, { "汎用ソフト一致", 5 }, { "本日が期日", 5 }, { "優先度「高」", 5 } };
        private Dictionary<string, string> weightTooltips = new Dictionary<string, string> {
            { "足切りライン", "タスクとして確定させるために必要な、最低限の合計ポイントです" },
            { "ファイルパス合致", "ブラウザのURLや、開いているファイルのフォルダパス(場所)にタスク名が含まれているかをチェックします" },
            { "学習辞書合致", "設定した「タスクの紐付け(学習辞書)」のルールと一致した場合に加点します" },
            { "完全一致", "ウィンドウのタイトルの中に、タスク名がそっくりそのまま含まれている場合に加点します" },
            { "カテゴリ辞書合致", "設定した「カテゴリの紐付け(学習辞書)」のルールと一致した場合に加点します" },
            { "単語包含(複数)", "タスク名を単語に区切り、その単語がタイトルやファイルパスに2つ以上含まれている場合です" },
            { "専用ソフト一致", "タスクのカテゴリ名がアプリ名と同じ場合、またはIllustrator等の専用ソフト使用中の場合です" },
            { "直前タスクの継続", "過去15分以内に記録されていた直前のタスクと同じ場合、作業が続いているとみなします" },
            { "単語包含(単一)", "タスク名の単語がタイトルやファイルパスに1つだけ含まれている場合です" },
            { "期限超過", "すでに期日を過ぎてしまっているタスクを優先的に紐付けます" },
            { "時間帯のパターン", "過去14日間のデータから、今の時間帯によく実行されているタスクを優先します" },
            { "汎用ソフト一致", "ExcelやChromeなど、一般的なソフトを使用している場合に少しだけ加点します" },
            { "本日が期日", "今日が期日のタスクを優先的に紐付けます" },
            { "優先度「高」", "優先度が「高」に設定されているタスクを優先的に紐付けます" }
        };

        private TabControl tabControl;
        private ToolTip _mainToolTip;
        
        // General
        private CheckBox chkAutoTracking;
        private CheckBox chkRunAtStartup, chkMinimizeToTray, chkAlwaysOnTop, chkEnableSoundEffects;
        private TextBox txtPasscode;
        private TrackBar tbOpacity;
        private NumericUpDown numIdleTimeout, numLongTask;
        private CheckBox chkRememberWindowSize;
        private Button btnResetWindowSize;
        private NumericUpDown numWindowWidth, numWindowHeight, numMainSplitter, numCalendarSplitter, numCalendarLeftSplitter, numFilesSplitter;
        private Dictionary<string, NumericUpDown[]> subWindowSizeControls = new Dictionary<string, NumericUpDown[]>();

        // View
        private ComboBox cmbStartupView, cmbDateFormat, cmbWeekStart, cmbOverlap;
        private NumericUpDown numDayStartHour, numTimelineStart, numTimelineEnd, numEventNotify;
        private CheckBox chkColorWeekend, chkShowTooltips, chkEventNotify, chkDarkMode, chkColorVisionSupport;

        // Task
        private ComboBox cmbListDensity, cmbDefaultSort, cmbDblClick, cmbNotifyStyle, cmbDefaultPriority, cmbGlobalNotify;
        private CheckBox chkShowStrikethrough, chkShowKanbanDone, chkShowIcons;
        private NumericUpDown numAlertRed, numAlertYellow, numDueOffset, numNotifyDays;

        // Data
        private TextBox txtBackupPath;
        private Button btnBrowseBackup;
        private NumericUpDown numAutoArchive, numWarnPercent, numPomodoro, numRetention, numProjArchive;
        private CheckBox chkArchiveOnProjComp, chkArchiveOnTaskComp;
        private CheckedListBox clbExcludeStatuses;
        private CheckBox chkExcludePendingTime;

        private List<TaskItem> _allTasks;
        private List<ProjectItem> _projects;
        

        public FormSettings(AppSettings currentSettings, List<TaskItem> allTasks = null, List<ProjectItem> projects = null)
        {
            // ディープコピーしてキャンセル時に元の設定を汚さないようにする
            string json = JsonSerializer.Serialize(currentSettings);
            _settings = JsonSerializer.Deserialize<AppSettings>(json);
            _allTasks = allTasks ?? new List<TaskItem>();
            _projects = projects ?? new List<ProjectItem>();
            InitializeComponent();
            LoadData();

            UniConsul.Utils.IconHelper.SetAppIcon(this);

            // 💡 設定画面自身のサイズ復元
            if (_settings != null && _settings.WindowSizes != null && _settings.WindowSizes.ContainsKey(this.Name)) {
                var parts = _settings.WindowSizes[this.Name].Split(',');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) {
                    this.Width = Math.Max(this.MinimumSize.Width, w);
                    this.Height = Math.Max(this.MinimumSize.Height, h);
                }
            }

            // 💡 FormSettings自身のリサイズ処理が_settings.WindowWidth(メイン画面の幅)を誤って上書きするのを防ぐため、
            // ここでの EnableDynamicResizing の呼び出しは無効化します。
            // ThemeManager.EnableDynamicResizing(this, _settings);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            bool isDark = _settings != null && _settings.IsDarkMode;
            
            // 重複していたAPI呼び出しと再帰的テーマ適用をThemeManagerに委譲
            ThemeManager.ApplyDarkModeToWindow(this.Handle, isDark);
            ThemeManager.ApplyTheme(this, isDark);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // ウィンドウサイズが変更されたら、配置タブの入力欄の数値も自動で同期させる
            if (subWindowSizeControls != null && subWindowSizeControls.ContainsKey("FormSettings"))
            {
                try
                {
                    subWindowSizeControls["FormSettings"][0].Value = Math.Max(subWindowSizeControls["FormSettings"][0].Minimum, this.Width);
                    subWindowSizeControls["FormSettings"][1].Value = Math.Max(subWindowSizeControls["FormSettings"][1].Minimum, this.Height);
                }
                catch { }
            }
        }

        private void InitializeComponent()
        {
            this.Name = "FormSettings";
            this.Text = "全体設定";
            this.Size = new Size(860, 850);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(860, 850);
            this.AutoScaleMode = AutoScaleMode.Dpi;

            // --- Top Panel ---
            var topPanel = new Panel     { Dock = DockStyle.Top, Height = 45 };
            var flowTop = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5, 8, 15, 5) };
            var btnCancelTop = new Button { Text = "キャンセル", Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
            var btnSaveTop = new Button { Text = "設定を反映して閉じる", Size = new Size(130, 30) };
            btnSaveTop.Click += ButtonSave_Click;
            flowTop.Controls.AddRange(new Control[] { btnCancelTop, btnSaveTop });
            topPanel.Controls.Add(flowTop);
            this.Controls.Add(topPanel);

            // --- Bottom Panel ---
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            var btnCancel = new Button { Text = "キャンセル", Location = new Point(730, 10), Size = new Size(90, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            var btnSave = new Button { Text = "設定を反映して閉じる", Location = new Point(570, 10), Size = new Size(150, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnSave.Click += ButtonSave_Click;
            bottomPanel.Controls.AddRange(new Control[] { btnSave, btnCancel });
            this.Controls.Add(bottomPanel);
            
            // --- Tab Control ---
            tabControl = new TabControl { 
                Dock = DockStyle.Fill, 
                DrawMode = TabDrawMode.OwnerDrawFixed 
            };
            tabControl.DrawItem += TabControl_DrawItem;
            this.Controls.Add(tabControl);
            
            // Dockの挙動を正しくするため、TabControlを最前面(Zオーダー0)にして残りの領域を埋める
            tabControl.BringToFront();
            
            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;

            // --- 1. General Tab ---
            var tabGeneral = new TabPage("一般・動作");
            tabControl.TabPages.Add(tabGeneral);
            int y = 15;
            chkAutoTracking = new CheckBox { Text = "自動記録を有効にする", AutoSize = true };
            AddField(tabGeneral, ref y, null, chkAutoTracking);

            chkRunAtStartup = new CheckBox { Text = "Windows起動時に自動実行する", AutoSize = true };
            AddField(tabGeneral, ref y, null, chkRunAtStartup);
            chkMinimizeToTray = new CheckBox { Text = "閉じるボタンで最小化 (タスクトレイへ)", AutoSize = true };
            AddField(tabGeneral, ref y, null, chkMinimizeToTray);
            chkAlwaysOnTop = new CheckBox { Text = "ウィンドウを常に手前に表示", AutoSize = true };
            AddField(tabGeneral, ref y, null, chkAlwaysOnTop);
            chkEnableSoundEffects = new CheckBox { Text = "完了時に効果音を鳴らす", AutoSize = true };
            AddField(tabGeneral, ref y, null, chkEnableSoundEffects);

            txtPasscode = new TextBox { Width = 150 };
            AddField(tabGeneral, ref y, "起動パスコード (空欄で無効):", txtPasscode);

            numIdleTimeout = new NumericUpDown { Minimum = 1, Maximum = 120, Width = 60 };
            AddField(tabGeneral, ref y, "非アクティブ判定 (分):", numIdleTimeout);

            numLongTask = new NumericUpDown { Minimum = 0, Maximum = 1440, Width = 60 };
            AddField(tabGeneral, ref y, "長時間作業の警告 (分):", numLongTask);

            // --- 2. View & Calendar Tab ---
            var tabView = new TabPage("表示・カレンダー");
            tabControl.TabPages.Add(tabView);
            y = 15;
            chkDarkMode = new CheckBox { Text = "ダークモードを有効にする", AutoSize = true };
            chkColorVisionSupport = new CheckBox { Text = "色覚サポートモードを有効にする (見分けやすい配色)", AutoSize = true };
            AddField(tabView, ref y, null, chkDarkMode, null, chkColorVisionSupport);

            cmbStartupView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, DisplayMember = "Text", ValueMember = "Value" };
            cmbStartupView.DataSource = new[] { new ComboItem { Text = "リスト", Value = "List" }, new ComboItem { Text = "カンバン", Value = "Kanban" }, new ComboItem { Text = "カレンダー", Value = "Calendar" } };
            cmbDateFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cmbDateFormat.Items.AddRange(new[] { "yyyy/MM/dd", "MM/dd", "yyyy-MM-dd" });
            AddField(tabView, ref y, "起動時の画面:", cmbStartupView, "日付形式:", cmbDateFormat);

            numDayStartHour = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 60 };
            cmbWeekStart = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
            cmbWeekStart.Items.AddRange(new[] { "日曜", "月曜" });
            AddField(tabView, ref y, "日付の境界線 (時):", numDayStartHour, "週の始まり:", cmbWeekStart);

            chkColorWeekend = new CheckBox { Text = "カレンダーの土日を色分けする", AutoSize = true };
            chkShowTooltips = new CheckBox { Text = "ツールチップを表示する", AutoSize = true };
            AddField(tabView, ref y, null, chkColorWeekend, null, chkShowTooltips);

            numTimelineStart = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 50 };
            numTimelineEnd = new NumericUpDown { Minimum = 1, Maximum = 24, Width = 50 };
            AddField(tabView, ref y, "タイムライン表示範囲 (時):", numTimelineStart, "～", numTimelineEnd);

            cmbOverlap = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DisplayMember = "Text", ValueMember = "Value" };
            cmbOverlap.DataSource = new[] { new ComboItem { Text = "エラーを表示 (中断)", Value = "Error" }, new ComboItem { Text = "上書き (修正)", Value = "Overwrite" } };
            AddField(tabView, ref y, "時間記録の重複:", cmbOverlap);

            chkEventNotify = new CheckBox { Text = "イベント通知有効", AutoSize = true };
            numEventNotify = new NumericUpDown { Minimum = 0, Maximum = 1440, Width = 60 };
            AddField(tabView, ref y, null, chkEventNotify, "通知 (分前):", numEventNotify);

            // --- 3. Task & List Tab ---
            var tabTask = new TabPage("タスク操作・リスト");
            tabControl.TabPages.Add(tabTask);
            y = 15;
            cmbListDensity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, DisplayMember = "Text", ValueMember = "Value" };
            cmbListDensity.DataSource = new[] { new ComboItem { Text = "狭い", Value = "Compact" }, new ComboItem { Text = "標準", Value = "Standard" }, new ComboItem { Text = "広い", Value = "Relaxed" } };
            chkShowStrikethrough = new CheckBox { Text = "完了タスク打ち消し線", AutoSize = true };
            AddField(tabTask, ref y, "リスト行間:", cmbListDensity, null, chkShowStrikethrough);

            chkShowKanbanDone = new CheckBox { Text = "カンバンの完了列表示", AutoSize = true };
            chkShowIcons = new CheckBox { Text = "アイコン(⚠️等)を表示", AutoSize = true };
            cmbDefaultSort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, DisplayMember = "Text", ValueMember = "Value" };
            cmbDefaultSort.DataSource = new[] { new ComboItem { Text = "期日", Value = "DueDate" }, new ComboItem { Text = "優先度", Value = "Priority" }, new ComboItem { Text = "作成日", Value = "CreatedDate" } };
            AddField(tabTask, ref y, null, chkShowKanbanDone, null, chkShowIcons);

            AddField(tabTask, ref y, "デフォルト並び順:", cmbDefaultSort);

            cmbDblClick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, DisplayMember = "Text", ValueMember = "Value" };
            cmbDblClick.DataSource = new[] { new ComboItem { Text = "編集", Value = "Edit" }, new ComboItem { Text = "状態切替", Value = "ToggleStatus" } };
            cmbNotifyStyle = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, DisplayMember = "Text", ValueMember = "Value" };
            cmbNotifyStyle.DataSource = new[] { new ComboItem { Text = "ダイアログ", Value = "Dialog" }, new ComboItem { Text = "バルーン", Value = "Balloon" } };
            AddField(tabTask, ref y, "ダブルクリック動作:", cmbDblClick, "通知スタイル:", cmbNotifyStyle);

            numAlertRed = new NumericUpDown { Width = 50 };
            numAlertYellow = new NumericUpDown { Width = 50 };
            AddField(tabTask, ref y, "期限アラート (赤):", numAlertRed, "(黄):", numAlertYellow);

            cmbDefaultPriority = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
            cmbDefaultPriority.Items.AddRange(new[] { "高", "中", "低" });
            numDueOffset = new NumericUpDown { Width = 50 };
            AddField(tabTask, ref y, "新規タスク 優先度:", cmbDefaultPriority, "期限+日:", numDueOffset);

            cmbGlobalNotify = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cmbGlobalNotify.Items.AddRange(new[] { "通知しない", "当日", "1日前", "前の営業日", "3日前", "1週間前" });
            numNotifyDays = new NumericUpDown { Minimum = 1, Maximum = 30, Width = 50 };
            AddField(tabTask, ref y, "デフォルト通知:", cmbGlobalNotify, "ボタン表示期間:", numNotifyDays);

            // --- 4. Data & Analysis Tab ---
            var tabData = new TabPage("データ・分析");
            tabControl.TabPages.Add(tabData);
            y = 15;
            txtBackupPath = new TextBox { Width = 200 };
            btnBrowseBackup = new Button { Text = "...", Width = 30 };
            btnBrowseBackup.Click += (s, e) => {
                using (var fbd = new FolderBrowserDialog()) {
                    if (fbd.ShowDialog() == DialogResult.OK) txtBackupPath.Text = fbd.SelectedPath;
                }
            };
            tabData.Controls.Add(new Label { Text = "バックアップ保存先:", Location = new Point(15, y + 4), AutoSize = true });
            txtBackupPath.Location = new Point(160, y);
            tabData.Controls.Add(txtBackupPath);
            btnBrowseBackup.Location = new Point(txtBackupPath.Right + 5, y - 1);
            tabData.Controls.Add(btnBrowseBackup);
            y += 35;

            numAutoArchive = new NumericUpDown { Width = 60, Maximum = 3650 };
            numWarnPercent = new NumericUpDown { Width = 60 };
            AddField(tabData, ref y, "自動アーカイブ日数:", numAutoArchive, "警告基準 (%):", numWarnPercent);

            numPomodoro = new NumericUpDown { Width = 60 };
            numRetention = new NumericUpDown { Width = 60, Minimum = 1, Maximum = 3650 };
            AddField(tabData, ref y, "ポモドーロ (分):", numPomodoro, "バックアップ保持 (日):", numRetention);

            numProjArchive = new NumericUpDown { Width = 60, Maximum = 3650 };
            AddField(tabData, ref y, "PJ自動アーカイブ (日):", numProjArchive);

            chkArchiveOnProjComp = new CheckBox { Text = "PJ完了時にタスクを即時アーカイブ", AutoSize = true };
            AddField(tabData, ref y, null, chkArchiveOnProjComp);

            chkArchiveOnTaskComp = new CheckBox { Text = "タスク完了時に即時アーカイブ", AutoSize = true };
            AddField(tabData, ref y, null, chkArchiveOnTaskComp);

            chkExcludePendingTime = new CheckBox { Text = "進捗スピードの計算から保留・承認待ち時間を除外する", AutoSize = true };
            AddField(tabData, ref y, null, chkExcludePendingTime);

            clbExcludeStatuses = new CheckedListBox { Width = 350, Height = 80, CheckOnClick = true };
            var statuses = new[] { "未実施", "保留", "実施中", "確認待ち", "完了済み" };
            clbExcludeStatuses.Items.AddRange(statuses);
            tabData.Controls.Add(new Label { Text = "リードタイム計算から除外するステータス:", Location = new Point(15, y), AutoSize = true });
            y += 20;
            clbExcludeStatuses.Location = new Point(15, y);
            tabData.Controls.Add(clbExcludeStatuses);

            // --- 5. Window & Layout Tab ---
            var tabLayout = new TabPage("ウィンドウ・配置");
            tabControl.TabPages.Add(tabLayout);
            y = 15;

            tbOpacity = new TrackBar { Minimum = 5, Maximum = 10, Width = 150, TickFrequency = 1, LargeChange = 1 };
            AddField(tabLayout, ref y, "ウィンドウ透明度:", tbOpacity);

            y += 20; // 「ウィンドウ透明度」と「記憶する」の設定群の間に余白を追加してオフセット

            chkRememberWindowSize = new CheckBox { Text = "終了時のウィンドウサイズと分割位置を記憶する", AutoSize = true };
            btnResetWindowSize = new Button { Text = "初期値に戻す", Width = 100, Height = 25 };
            btnResetWindowSize.Click += (s, e) => {
                numWindowWidth.Value = FormMain.DefaultWindowWidth;
                numWindowHeight.Value = FormMain.DefaultWindowHeight;
                numMainSplitter.Value = FormMain.DefaultMainSplitter;
                numFilesSplitter.Value = FormMain.DefaultFilesSplitter;
                numCalendarSplitter.Value = FormMain.DefaultCalendarSplitter;
                numCalendarLeftSplitter.Value = FormMain.DefaultCalendarLeftSplitter;
                
                foreach (var kvp in FormMain.DefaultSubWindowSizes) {
                    if (subWindowSizeControls.ContainsKey(kvp.Key)) {
                        subWindowSizeControls[kvp.Key][0].Value = kvp.Value.Width;
                        subWindowSizeControls[kvp.Key][1].Value = kvp.Value.Height;
                    }
                }
            };
            AddField(tabLayout, ref y, null, chkRememberWindowSize, null, btnResetWindowSize);

            y += 15; // 下の要素との間に余白（オフセット）を作る

            numWindowWidth = new NumericUpDown { Minimum = 800, Maximum = 4000, Width = 60 };
            numWindowHeight = new NumericUpDown { Minimum = 600, Maximum = 3000, Width = 60 };
            AddField(tabLayout, ref y, "ウィンドウ幅:", numWindowWidth, "高さ:", numWindowHeight);

            numMainSplitter = new NumericUpDown { Minimum = 100, Maximum = 3000, Width = 60 };
            numFilesSplitter = new NumericUpDown { Minimum = 100, Maximum = 3000, Width = 60 };
            AddField(tabLayout, ref y, "メイン画面 (上下):", numMainSplitter, "ファイル領域 (左右):", numFilesSplitter);

            numCalendarSplitter = new NumericUpDown { Minimum = 100, Maximum = 3000, Width = 60 };
            numCalendarLeftSplitter = new NumericUpDown { Minimum = 100, Maximum = 3000, Width = 60 };
            AddField(tabLayout, ref y, "カレンダー (左右):", numCalendarSplitter, "カレンダー詳細 (上下):", numCalendarLeftSplitter);

            y += 10;
            tabLayout.Controls.Add(new Label { Text = "■ 各サブ画面のサイズ:", Location = new Point(15, y), AutoSize = true, Font = new Font("Meiryo UI", 9, FontStyle.Bold) });
            y += 25;

            string[] subForms = { 
                "FormTaskInput|タスク編集", "FormProjectInput|PJ編集", "FormCategoryEditor|カテゴリ編集", 
                "FormTemplateEditor|テンプレート", "FormTemplateTaskInput|テンプレタスク", "FormArchiveView|アーカイブ", 
                "FormReport|レポート", "FormSettings|設定画面", "FormRecurringRuleEditor|定期ルール", 
                "FormDailyReport|日報出力", "FormLearningDictionary|学習辞書", "FormIcsExchange|ICS連携" 
            };
            foreach (var sf in subForms) {
                var parts = sf.Split('|');
                var key = parts[0];
                
                var numW = new NumericUpDown { Minimum = 300, Maximum = 4000, Width = 60 };
                var numH = new NumericUpDown { Minimum = 200, Maximum = 3000, Width = 60 };
                subWindowSizeControls[key] = new NumericUpDown[] { numW, numH };
                AddField(tabLayout, ref y, parts[1] + " (幅):", numW, "(高さ):", numH);
            }

            // --- 6. AutoTracker Tab ---
            var tabAutoTracker = new TabPage("自動紐付け");
            tabControl.TabPages.Add(tabAutoTracker);
            
            var flowWeights = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15, 15, 15, 60), FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            tabAutoTracker.Controls.Add(flowWeights);
            
            var pnlWeightsHeader = new FlowLayoutPanel { Width = 800, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 15), WrapContents = false };
            var lblWeightsDesc = new Label { Text = "作業中の「アプリ名やウィンドウのタイトル」と「登録済みのタスク」を比較する条件を設定します。\n条件を満たしてポイントが「基準スコア」を超えたタスクに、自動記録が紐付けられます。", AutoSize = true, Font = new Font("Meiryo UI", 9, FontStyle.Bold) };
            var btnHelp = new Button { Text = "？", Size = new Size(30, 30), Font = new Font("Meiryo UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(10, 0, 0, 0) };
            btnHelp.Click += (s, ev) => {
                string helpText = "【自動紐付けの仕組み】\n\n" +
                                  "作業中の「アプリ名」や「ウィンドウのタイトル」と、登録済みの「タスク名」などを比較し、設定された条件を満たすとポイントが加算されます。\n\n" +
                                  "最も合計ポイントが高く、かつ「基準スコア」を超えたタスクが、現在の作業として紐付けられます。\n" +
                                  "基準スコアに満たない場合は、どのタスクにも紐付かず「未分類」として記録されます。\n\n" +
                                  "・同点になった場合は、「直近1時間のタスク」「期日が近いタスク」「最近更新されたタスク」の順で優先されます。\n\n" +
                                  "※ 各項目の数値を調整することで、紐付けの優先度をカスタマイズできます。";
                MessageBox.Show(this, helpText, "自動紐付けの仕組みについて", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            pnlWeightsHeader.Controls.AddRange(new Control[] { lblWeightsDesc, btnHelp });
            flowWeights.Controls.Add(pnlWeightsHeader);

            foreach (var kvp in defaultWeights)
            {
                string displayLabel = kvp.Key;
                switch (kvp.Key) { case "足切りライン": displayLabel = "👉 紐付けの基準スコア (この点数未満は未分類になります)"; break; case "ファイルパス合致": displayLabel = "ファイルパスや URL に「タスク名」が含まれている"; break; case "学習辞書合致": displayLabel = "「タスクの紐付け(学習辞書)」のルールと一致する"; break; case "完全一致": displayLabel = "ウィンドウのタイトルに「タスク名」が完全に含まれる"; break; case "カテゴリ辞書合致": displayLabel = "「カテゴリの紐付け(学習辞書)」のルールと一致する"; break; case "単語包含(複数)": displayLabel = "タイトル等に「タスク名」の単語が 2つ以上 含まれる"; break; case "専用ソフト一致": displayLabel = "タスクのカテゴリ名とアプリ名が一致 (または専用ソフト使用中)"; break; case "直前タスクの継続": displayLabel = "直前 (過去15分以内) に実行していたタスクと同じである"; break; case "単語包含(単一)": displayLabel = "タイトル等に「タスク名」の単語が 1つだけ 含まれる"; break; case "期限超過": displayLabel = "すでに期日を過ぎているタスクである"; break; case "時間帯のパターン": displayLabel = "過去のデータから、この時間帯によく実行するタスクである"; break; case "汎用ソフト一致": displayLabel = "一般的なソフト (Excel, Chrome等) を使用中である"; break; case "本日が期日": displayLabel = "今日が期日のタスクである"; break; case "優先度「高」": displayLabel = "優先度が「高」に設定されているタスクである"; break; }

                var pnl = new Panel { Width = 375, Height = 48, Margin = new Padding(5) };
                var lblTag = new Label { 
                    Text = displayLabel, Location = new Point(5, 5), AutoSize = true, MaximumSize = new Size(295, 0), Padding = new Padding(5, 3, 5, 3), Font = new Font("Meiryo UI", 9, kvp.Key == "足切りライン" ? FontStyle.Bold : FontStyle.Regular)
                };
                if (kvp.Key == "足切りライン") lblTag.ForeColor = Color.DodgerBlue;
                
                var num = new NumericUpDown { Location = new Point(305, 13), Width = 60, Minimum = 0, Maximum = 200, Font = new Font("Meiryo UI", 9) };
                
                weightInputs[kvp.Key] = num;
                pnl.Controls.Add(lblTag); pnl.Controls.Add(num); flowWeights.Controls.Add(pnl);
            }
            
            var pnlReset = new Panel { AutoSize = true, Height = 40, Margin = new Padding(0) };
            var btnResetWeights = new Button { Text = "デフォルトに戻す", Width = 150, Height = 28, Location = new Point(5, 10) };
            btnResetWeights.Click += (s, ev) => { foreach (var kvp in defaultWeights) if(weightInputs.ContainsKey(kvp.Key)) weightInputs[kvp.Key].Value = kvp.Value; };
            pnlReset.Controls.Add(btnResetWeights);
            flowWeights.Controls.Add(pnlReset);

            // --- 7. Maintenance Tab ---
            var tabMaint = new TabPage("メンテナンス");
            tabControl.TabPages.Add(tabMaint);
            y = 15;
            
            var btnOpenData = new Button { Text = "📂 データフォルダを開く", Location = new Point(15, y), Size = new Size(220, 30) };
            btnOpenData.Click += (s, e) => { 
                string appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UniConsul");
                var dataSvc = new DataService(appRoot);
                System.Diagnostics.Process.Start("explorer.exe", dataSvc.AppRoot); 
            };
            tabMaint.Controls.Add(btnOpenData);
            y += 40;

            var btnResetPos = new Button { Text = "🔄 ウィンドウ位置リセット", Location = new Point(15, y), Size = new Size(220, 30) };
            btnResetPos.Click += (s, e) => {
                if (this.Owner != null) {
                    this.Owner.StartPosition = FormStartPosition.Manual;
                    this.Owner.Location = new Point(100, 100);
                    MessageBox.Show("ウィンドウ位置をリセットしました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            tabMaint.Controls.Add(btnResetPos);
            y += 40;

            var btnBackupSave = new Button { Text = "💾 現在の設定をファイルに保存...", Location = new Point(15, y), Size = new Size(220, 30) };
            btnBackupSave.Click += BtnBackupSave_Click;
            tabMaint.Controls.Add(btnBackupSave);
            y += 40;

            var btnResetConfig = new Button { Text = "⚠️ 設定を初期化", Location = new Point(15, y), Size = new Size(220, 30), ForeColor = Color.Red };
            btnResetConfig.Click += (s, e) => {
                if (MessageBox.Show("すべての設定を初期化します。よろしいですか？\n(データは削除されません)", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                    string appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UniConsul");
                    var dataSvc = new DataService(appRoot);
                    if (System.IO.File.Exists(dataSvc.SettingsFile)) System.IO.File.Delete(dataSvc.SettingsFile);
                    MessageBox.Show("設定を初期化しました。アプリを再起動してください。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Exit();
                }
            };
            tabMaint.Controls.Add(btnResetConfig);

            SetupToolTips();
        }

        private void SetupToolTips()
        {
            _mainToolTip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 500, ReshowDelay = 100 };
            
            _mainToolTip.SetToolTip(chkRunAtStartup, "PCを起動した際、自動的にこのアプリを立ち上げます");
            _mainToolTip.SetToolTip(chkMinimizeToTray, "ウィンドウ右上の「×」ボタンを押したとき、アプリを終了せずにタスクトレイに常駐させます");
            _mainToolTip.SetToolTip(chkAlwaysOnTop, "このアプリのウィンドウを常に他のウィンドウより手前に表示し続けます");
            _mainToolTip.SetToolTip(txtPasscode, "アプリを開く際にパスワードを要求します。空欄にすると無効になります");
            _mainToolTip.SetToolTip(numIdleTimeout, "PCの操作がない状態が指定した時間(分)続くと、作業記録のタイマーを自動で一時停止します");
            _mainToolTip.SetToolTip(numLongTask, "1つのタスクを休憩なしで連続して記録していると、指定した時間(分)で警告を出します");

            _mainToolTip.SetToolTip(chkDarkMode, "アプリ全体の配色を暗いテーマに変更します");
            _mainToolTip.SetToolTip(chkColorVisionSupport, "完了タスクに斜線を引くなど、色覚特性に配慮した視認性の高いデザインを使用します");
            _mainToolTip.SetToolTip(cmbStartupView, "アプリを起動した際に最初に表示されるタブを選択します");
            _mainToolTip.SetToolTip(cmbOverlap, "タイムライン上で時間記録が重なってしまった場合の処理方法です。\nエラー: 保存させない\n上書き: 前の記録を自動で短縮する");

            _mainToolTip.SetToolTip(txtBackupPath, "自動・手動バックアップファイルの保存先フォルダです");
            _mainToolTip.SetToolTip(numAutoArchive, "タスクが「完了済み」になってから指定した日数が経過すると、自動的にアーカイブ(過去データ)へ移動し、動作を軽くします");
            _mainToolTip.SetToolTip(numProjArchive, "プロジェクト内の全タスクが完了してから指定した日数が経過すると、自動的にアーカイブへ移動します");
            _mainToolTip.SetToolTip(chkArchiveOnProjComp, "チェックを入れると、プロジェクト全体が完了した瞬間に中のタスクをすべて即座にアーカイブします");

            _mainToolTip.SetToolTip(chkShowIcons, "リストやカンバンで、優先度などの文字と一緒にアイコン（絵文字）を表示します");
            _mainToolTip.SetToolTip(cmbNotifyStyle, "通知の表示方法です。\nダイアログ: 画面中央にポップアップ\nバルーン: タスクトレイから通知");

            _mainToolTip.SetToolTip(chkRememberWindowSize, "アプリ終了時のウィンドウサイズと各分割線の位置を記憶し、次回起動時に復元します");
            
            chkShowTooltips.CheckedChanged += (s, e) => {
                _mainToolTip.Active = chkShowTooltips.Checked;
            };
            
            foreach (var kvp in weightInputs) {
                if (weightTooltips.ContainsKey(kvp.Key)) {
                    _mainToolTip.SetToolTip(kvp.Value, weightTooltips[kvp.Key]);
                    _mainToolTip.SetToolTip(kvp.Value.Parent, weightTooltips[kvp.Key]);
                    foreach (Control c in kvp.Value.Parent.Controls) {
                        _mainToolTip.SetToolTip(c, weightTooltips[kvp.Key]);
                    }
                }
            }
        }

        private void TabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            bool isDark = _settings != null && _settings.IsDarkMode;
            if (e.Index < 0 || e.Index >= tabControl.TabPages.Count) return;
            
            var tabPage = tabControl.TabPages[e.Index];
            var tabRect = tabControl.GetTabRect(e.Index);
            var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            
            Color bgColor = isDark 
                ? (isSelected ? Color.FromArgb(80, 80, 80) : Color.FromArgb(45, 45, 48)) 
                : (isSelected ? Color.White : Color.FromArgb(240, 240, 240));
                
            Color textColor = isDark ? Color.White : Color.Black;
            
            using (var bgBrush = new SolidBrush(bgColor)) e.Graphics.FillRectangle(bgBrush, tabRect);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(e.Graphics, tabPage.Text, tabControl.Font, tabRect, textColor, flags);
        }

        private void AddField(Control parent, ref int y, string label, Control ctrl, string label2 = null, Control ctrl2 = null)
        {
            if (label != null) {
                parent.Controls.Add(new Label { Text = label, Location = new Point(15, y + 4), AutoSize = true }); // テキストボックスと高さを揃える
                ctrl.Location = new Point(160, y);
            } else {
                ctrl.Location = new Point(15, y);
            }
            parent.Controls.Add(ctrl);

            if (ctrl2 != null) {
                if (label2 != null) {
                    parent.Controls.Add(new Label { Text = label2, Location = new Point(300, y + 4), AutoSize = true });
                    ctrl2.Location = new Point(450, y);
                } else {
                    ctrl2.Location = new Point(300, y);
                }
                parent.Controls.Add(ctrl2);
            }
            y += 35;
        }

        private void LoadData()
        {
            try { chkAutoTracking.Checked = _settings.AutoTracker != null ? _settings.AutoTracker.EnableAutoTracking : false; } catch {}
            try { chkRunAtStartup.Checked = _settings.RunAtStartup; } catch {}
            try { chkMinimizeToTray.Checked = _settings.MinimizeToTray; } catch {}
            try { chkAlwaysOnTop.Checked = _settings.AlwaysOnTop; } catch {}
            try { chkEnableSoundEffects.Checked = _settings.EnableSoundEffects; } catch {}
            try { 
                if (!string.IsNullOrEmpty(_settings.Passcode)) txtPasscode.Text = "********";
                else txtPasscode.Text = "";
            } catch {}
            try { tbOpacity.Value = Math.Max(5, Math.Min(10, (int)(_settings.WindowOpacity * 10))); } catch { tbOpacity.Value = 10; }
            try { numIdleTimeout.Value = Math.Max(1, _settings.IdleTimeoutMinutes); } catch { numIdleTimeout.Value = 5; }
            try { numLongTask.Value = _settings.LongTaskNotificationMinutes; } catch { numLongTask.Value = 180; }

            try { chkRememberWindowSize.Checked = _settings.RememberWindowSize; } catch { chkRememberWindowSize.Checked = true; }
            try { numWindowWidth.Value = Math.Max(800, _settings.WindowWidth); } catch { numWindowWidth.Value = FormMain.DefaultWindowWidth; }
            try { numWindowHeight.Value = Math.Max(600, _settings.WindowHeight); } catch { numWindowHeight.Value = FormMain.DefaultWindowHeight; }
            try { numMainSplitter.Value = Math.Max(100, _settings.MainSplitterDistance); } catch { numMainSplitter.Value = FormMain.DefaultMainSplitter; }
            try { numFilesSplitter.Value = Math.Max(100, _settings.FilesSplitterDistance); } catch { numFilesSplitter.Value = FormMain.DefaultFilesSplitter; }
            try { numCalendarSplitter.Value = Math.Max(100, _settings.CalendarSplitterDistance); } catch { numCalendarSplitter.Value = FormMain.DefaultCalendarSplitter; }
            try { numCalendarLeftSplitter.Value = Math.Max(100, _settings.CalendarLeftSplitterDistance); } catch { numCalendarLeftSplitter.Value = FormMain.DefaultCalendarLeftSplitter; }

            if (_settings.WindowSizes != null) {
                foreach (var key in subWindowSizeControls.Keys) {
                    if (_settings.WindowSizes.ContainsKey(key)) {
                        var parts = _settings.WindowSizes[key].Split(',');
                        int w; int h; if (parts.Length >= 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h)) {
                            try { subWindowSizeControls[key][0].Value = Math.Max(300, w); } catch {}
                            try { subWindowSizeControls[key][1].Value = Math.Max(200, h); } catch {}
                        }
                    } else {
                        if (FormMain.DefaultSubWindowSizes.ContainsKey(key)) {
                            subWindowSizeControls[key][0].Value = FormMain.DefaultSubWindowSizes[key].Width;
                            subWindowSizeControls[key][1].Value = FormMain.DefaultSubWindowSizes[key].Height;
                        }
                    }
                }
            } else {
                foreach (var key in subWindowSizeControls.Keys) {
                    if (FormMain.DefaultSubWindowSizes.ContainsKey(key)) {
                        subWindowSizeControls[key][0].Value = FormMain.DefaultSubWindowSizes[key].Width;
                        subWindowSizeControls[key][1].Value = FormMain.DefaultSubWindowSizes[key].Height;
                    }
                }
            }

            if (_settings.AutoTracker != null && _settings.AutoTracker.InferenceWeights != null) {
                foreach (var kvp in defaultWeights) {
                    if (weightInputs.ContainsKey(kvp.Key)) {
                        weightInputs[kvp.Key].Value = _settings.AutoTracker.InferenceWeights.ContainsKey(kvp.Key) ? _settings.AutoTracker.InferenceWeights[kvp.Key] : kvp.Value;
                    }
                }
            } else {
                foreach (var kvp in defaultWeights) {
                    if (weightInputs.ContainsKey(kvp.Key)) weightInputs[kvp.Key].Value = kvp.Value;
                }
            }

            try { chkDarkMode.Checked = _settings.IsDarkMode; } catch {}
            try { chkColorVisionSupport.Checked = _settings.EnableColorVisionSupport; } catch {}
            try { cmbStartupView.SelectedValue = _settings.StartupView ?? "List"; } catch { cmbStartupView.SelectedIndex = 0; }
            try { cmbDateFormat.SelectedItem = _settings.DateFormat ?? "yyyy/MM/dd"; } catch { cmbDateFormat.SelectedIndex = 0; }
            try { numDayStartHour.Value = _settings.DayStartHour; } catch { numDayStartHour.Value = 0; }
            try { cmbWeekStart.SelectedIndex = _settings.CalendarWeekStart; } catch { cmbWeekStart.SelectedIndex = 0; }
            try { chkColorWeekend.Checked = _settings.ColorWeekend; } catch { chkColorWeekend.Checked = true; }
            try { chkShowTooltips.Checked = _settings.ShowTooltips; } catch { chkShowTooltips.Checked = true; }
            try { numTimelineStart.Value = _settings.TimelineStartHour; } catch { numTimelineStart.Value = 8; }
            try { numTimelineEnd.Value = _settings.TimelineEndHour; } catch { numTimelineEnd.Value = 24; }
            try { cmbOverlap.SelectedValue = _settings.TimeLogOverlapBehavior ?? "Error"; } catch { cmbOverlap.SelectedIndex = 0; }
            try { chkEventNotify.Checked = _settings.EventNotificationEnabled; } catch { chkEventNotify.Checked = true; }
            try { numEventNotify.Value = _settings.EventNotificationMinutes; } catch { numEventNotify.Value = 15; }

            try { cmbListDensity.SelectedValue = _settings.ListDensity ?? "Standard"; } catch { cmbListDensity.SelectedIndex = 1; }
            try { chkShowStrikethrough.Checked = _settings.ShowStrikethrough; } catch { chkShowStrikethrough.Checked = true; }
            try { chkShowKanbanDone.Checked = _settings.ShowKanbanDone; } catch { chkShowKanbanDone.Checked = true; }
            try { chkShowIcons.Checked = _settings.ShowIcons; } catch { chkShowIcons.Checked = true; }
            try { cmbDefaultSort.SelectedValue = _settings.DefaultSort ?? "DueDate"; } catch { cmbDefaultSort.SelectedIndex = 0; }
            try { cmbDblClick.SelectedValue = _settings.DoubleClickAction ?? "Edit"; } catch { cmbDblClick.SelectedIndex = 0; }
            try { cmbNotifyStyle.SelectedValue = _settings.NotificationStyle ?? "Dialog"; } catch { cmbNotifyStyle.SelectedIndex = 0; }
            try { numAlertRed.Value = _settings.AlertDaysRed; } catch {}
            try { numAlertYellow.Value = _settings.AlertDaysYellow; } catch {}
            try { cmbDefaultPriority.SelectedItem = _settings.DefaultPriority ?? "中"; } catch { cmbDefaultPriority.SelectedIndex = 1; }
            try { numDueOffset.Value = _settings.DefaultDueOffset; } catch {}
            try { cmbGlobalNotify.SelectedItem = _settings.GlobalNotification ?? "当日"; } catch { cmbGlobalNotify.SelectedIndex = 1; }
            try { numNotifyDays.Value = Math.Max(1, _settings.NotificationButtonDays); } catch { numNotifyDays.Value = 7; }

            try { txtBackupPath.Text = _settings.BackupPath; } catch {}
            try { numAutoArchive.Value = _settings.AutoArchiveDays; } catch { numAutoArchive.Value = 30; }
            try { numWarnPercent.Value = _settings.AnalysisWarnPercent; } catch { numWarnPercent.Value = 40; }
            try { numPomodoro.Value = _settings.PomodoroWorkMinutes; } catch { numPomodoro.Value = 25; }
            try { numRetention.Value = Math.Max(1, _settings.BackupRetentionDays); } catch { numRetention.Value = 30; }
            try { numProjArchive.Value = _settings.AutoArchiveProjectsDays; } catch { numProjArchive.Value = 60; }
            try { chkArchiveOnProjComp.Checked = _settings.ArchiveTasksOnProjectCompletion; } catch {}
            try { chkArchiveOnTaskComp.Checked = _settings.ArchiveTasksOnCompletion; } catch {}
            try { chkExcludePendingTime.Checked = _settings.ExcludePendingTime; } catch {}
            
            try { 
                if (_settings.LeadTimeExcludeStatuses != null) {
                    for (int i = 0; i < clbExcludeStatuses.Items.Count; i++) {
                        if (_settings.LeadTimeExcludeStatuses.Contains(clbExcludeStatuses.Items[i].ToString())) {
                            clbExcludeStatuses.SetItemChecked(i, true);
                        }
                    }
                }
            } catch {}

            if (_mainToolTip != null) _mainToolTip.Active = chkShowTooltips.Checked;
        }

        private static string ComputeHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        private void UpdateSettingsFromUI()
        {
            try {
                if (_settings.AutoTracker == null) _settings.AutoTracker = new AutoTrackerSettings();
                _settings.AutoTracker.EnableAutoTracking = chkAutoTracking.Checked;
            } catch {}
            try { _settings.RunAtStartup = chkRunAtStartup.Checked; } catch {}
            try { _settings.MinimizeToTray = chkMinimizeToTray.Checked; } catch {}
            try { _settings.AlwaysOnTop = chkAlwaysOnTop.Checked; } catch {}
            try { _settings.EnableSoundEffects = chkEnableSoundEffects.Checked; } catch {}
            try { 
                if (txtPasscode.Text != "********") {
                    if (string.IsNullOrEmpty(txtPasscode.Text)) _settings.Passcode = "";
                    else _settings.Passcode = ComputeHash(txtPasscode.Text);
                }
            } catch {}
            try { _settings.WindowOpacity = tbOpacity.Value / 10.0; } catch {}
            try { _settings.IdleTimeoutMinutes = (int)numIdleTimeout.Value; } catch {}
            try { _settings.LongTaskNotificationMinutes = (int)numLongTask.Value; } catch {}

            try { _settings.RememberWindowSize = chkRememberWindowSize.Checked; } catch {}
            try { _settings.WindowWidth = (int)numWindowWidth.Value; } catch {}
            try { _settings.WindowHeight = (int)numWindowHeight.Value; } catch {}
            try { _settings.MainSplitterDistance = (int)numMainSplitter.Value; } catch {}
            try { _settings.FilesSplitterDistance = (int)numFilesSplitter.Value; } catch {}
            try { _settings.CalendarSplitterDistance = (int)numCalendarSplitter.Value; } catch {}
            try { _settings.CalendarLeftSplitterDistance = (int)numCalendarLeftSplitter.Value; } catch {}

            try {
                if (_settings.WindowSizes == null) _settings.WindowSizes = new Dictionary<string, string>();
                foreach (var key in subWindowSizeControls.Keys) {
                    int w = (int)subWindowSizeControls[key][0].Value;
                    int h = (int)subWindowSizeControls[key][1].Value;
                    string existing = _settings.WindowSizes.ContainsKey(key) ? _settings.WindowSizes[key] : "";
                    var parts = existing.Split(',');
                    string newVal = string.Format("{0},{1}", w, h);
                    if (parts.Length > 2) newVal += "," + string.Join(",", parts.Skip(2)); // スプリッターの値を保護
                    _settings.WindowSizes[key] = newVal;
                }
            } catch {}

            try { _settings.IsDarkMode = chkDarkMode.Checked; } catch {}
            try { _settings.EnableColorVisionSupport = chkColorVisionSupport.Checked; } catch {}
            try { _settings.StartupView = cmbStartupView.SelectedValue != null ? cmbStartupView.SelectedValue.ToString() : "List"; } catch {}
            try { _settings.DateFormat = cmbDateFormat.SelectedItem != null ? cmbDateFormat.SelectedItem.ToString() : "yyyy/MM/dd"; } catch {}
            try { _settings.DayStartHour = (int)numDayStartHour.Value; } catch {}
            try { _settings.CalendarWeekStart = cmbWeekStart.SelectedIndex; } catch {}
            try { _settings.ColorWeekend = chkColorWeekend.Checked; } catch {}
            try { _settings.ShowTooltips = chkShowTooltips.Checked; } catch {}
            try { _settings.TimelineStartHour = (int)numTimelineStart.Value; } catch {}
            try { _settings.TimelineEndHour = (int)numTimelineEnd.Value; } catch {}
            try { _settings.TimeLogOverlapBehavior = cmbOverlap.SelectedValue != null ? cmbOverlap.SelectedValue.ToString() : "Error"; } catch {}
            try { _settings.EventNotificationEnabled = chkEventNotify.Checked; } catch {}
            try { _settings.EventNotificationMinutes = (int)numEventNotify.Value; } catch {}

            try { _settings.ListDensity = cmbListDensity.SelectedValue != null ? cmbListDensity.SelectedValue.ToString() : "Standard"; } catch {}
            try { _settings.ShowStrikethrough = chkShowStrikethrough.Checked; } catch {}
            try { _settings.ShowKanbanDone = chkShowKanbanDone.Checked; } catch {}
            try { _settings.ShowIcons = chkShowIcons.Checked; } catch {}
            try { _settings.DefaultSort = cmbDefaultSort.SelectedValue != null ? cmbDefaultSort.SelectedValue.ToString() : "DueDate"; } catch {}
            try { _settings.DoubleClickAction = cmbDblClick.SelectedValue != null ? cmbDblClick.SelectedValue.ToString() : "Edit"; } catch {}
            try { _settings.NotificationStyle = cmbNotifyStyle.SelectedValue != null ? cmbNotifyStyle.SelectedValue.ToString() : "Dialog"; } catch {}
            try { _settings.AlertDaysRed = (int)numAlertRed.Value; } catch {}
            try { _settings.AlertDaysYellow = (int)numAlertYellow.Value; } catch {}
            try { _settings.DefaultPriority = cmbDefaultPriority.SelectedItem != null ? cmbDefaultPriority.SelectedItem.ToString() : "中"; } catch {}
            try { _settings.DefaultDueOffset = (int)numDueOffset.Value; } catch {}
            try { _settings.GlobalNotification = cmbGlobalNotify.SelectedItem != null ? cmbGlobalNotify.SelectedItem.ToString() : "当日"; } catch {}
            try { _settings.NotificationButtonDays = (int)numNotifyDays.Value; } catch {}

            try { _settings.BackupPath = txtBackupPath.Text; } catch {}
            try { _settings.AutoArchiveDays = (int)numAutoArchive.Value; } catch {}
            try { _settings.AnalysisWarnPercent = (int)numWarnPercent.Value; } catch {}
            try { _settings.PomodoroWorkMinutes = (int)numPomodoro.Value; } catch {}
            try { _settings.BackupRetentionDays = (int)numRetention.Value; } catch {}
            try { _settings.AutoArchiveProjectsDays = (int)numProjArchive.Value; } catch {}
            try { _settings.ArchiveTasksOnProjectCompletion = chkArchiveOnProjComp.Checked; } catch {}
            try { _settings.ArchiveTasksOnCompletion = chkArchiveOnTaskComp.Checked; } catch {}
            try { _settings.ExcludePendingTime = chkExcludePendingTime.Checked; } catch {}
            
            try {
                var excludes = new List<string>();
                foreach (var item in clbExcludeStatuses.CheckedItems) excludes.Add(item.ToString());
                _settings.LeadTimeExcludeStatuses = excludes;
            } catch {}

            try {
                if (_settings.AutoTracker == null) _settings.AutoTracker = new AutoTrackerSettings();
                if (_settings.AutoTracker.InferenceWeights == null) _settings.AutoTracker.InferenceWeights = new Dictionary<string, int>();
                foreach (var kvp in weightInputs) {
                    _settings.AutoTracker.InferenceWeights[kvp.Key] = (int)kvp.Value.Value;
                }
            } catch {}
        }

        private void ButtonSave_Click(object sender, EventArgs e)
        {
            if (numTimelineStart.Value >= numTimelineEnd.Value)
            {
                MessageBox.Show("タイムラインの終了時刻は開始時刻より後に設定してください。", "設定エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            
            UpdateSettingsFromUI();

            ResultSettings = _settings;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void BtnBackupSave_Click(object sender, EventArgs e)
        {
            UpdateSettingsFromUI();
            using (var sfd = new SaveFileDialog { Filter = "JSONファイル|*.json", FileName = "settings_backup.json", Title = "設定の保存" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                        System.IO.File.WriteAllText(sfd.FileName, json, System.Text.Encoding.UTF8);
                        MessageBox.Show("設定を保存しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("保存に失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
