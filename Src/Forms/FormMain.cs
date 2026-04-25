using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using Microsoft.Win32;
using TaskManager.Services;
using TaskManager.Native;
using TaskManager.Models;

namespace TaskManager.Forms
{
    public partial class FormMain : Form
    {
        // PowerShellの $script: 変数群に相当するグローバル状態
        private readonly string AppVersion = "13.0";

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private AppSettings Settings;
        private DataService dataService;
        private readonly string[] TaskStatuses = new[] { "未実施", "保留", "実施中", "確認待ち", "完了済み" };
        private List<TaskItem> AllTasks = new List<TaskItem>();
        private List<ProjectItem> Projects = new List<ProjectItem>();
        private Dictionary<string, List<string>> Categories = new Dictionary<string, List<string>>();
        private Dictionary<string, List<EventItem>> AllEvents = new Dictionary<string, List<EventItem>>();
        private List<TimeLog> AllTimeLogs = new List<TimeLog>();
        private bool isDarkMode = false;
        private bool isColorVisionSupport = false;
        private bool groupByProject = true;
        private Dictionary<string, bool> projectExpansionStates = new Dictionary<string, bool>();
        private Stack<IUndoCommand> undoStack = new Stack<IUndoCommand>(); // Undo履歴管理
        
        private string currentCategoryFilter = "(すべて)";
        private ToolStripComboBox categoryFilterComboBox;

        private ToolStripMenuItem darkModeMenuItem;

        // UIコントロール
        private MenuStrip mainMenu;
        private ToolStrip toolStrip;
        private StatusStrip statusBar;
        private SplitContainer mainContainer;
        private TabControl tabControl;
        private TabPage listTabPage;
        private TabPage kanbanTabPage;
        private TabPage calendarTabPage;
        private DataGridView taskDataGridView;
        private ToolStripStatusLabel statusLabel;
        
        // クイック設定用コントロール
        private Panel quickSettingsPanel;
        private TrackBar qTbOpacity;
        private NumericUpDown qNumTimeStart, qNumTimeEnd, qNumPomodoro;
        private ComboBox qCmbDensity;
        private CheckBox qChkKanbanDone;

        // DataGridView コンテキストメニュー
        private ContextMenuStrip dgvContextMenu;
        private ToolStripMenuItem dgvEditMenuItem;
        private ToolStripMenuItem dgvDeleteMenuItem;
        private ToolStripMenuItem dgvChangeStatusMenuItem;
        private ToolStripMenuItem dgvProjectPropertiesMenuItem;
        private ToolStripMenuItem dgvProjectDeleteMenuItem;
        private ToolStripMenuItem dgvArchiveMenuItem;
        private ToolStripMenuItem dgvBulkCompleteMenuItem;
        private ToolStripMenuItem dgvBulkDeleteMenuItem;

        // カンバン用コントロール
        private TableLayoutPanel kanbanLayout;
        private Dictionary<string, ListBox> kanbanLists = new Dictionary<string, ListBox>();
        private TaskItem kanbanDragTask = null;
        private Point kanbanDragStartPoint = Point.Empty;
        private ContextMenuStrip kanbanContextMenu;
        private ToolStripMenuItem kanbanEditMenuItem;
        private ToolStripMenuItem kanbanDeleteMenuItem;
        
        // カレンダー用コントロール
        private SplitContainer calendarSplitContainer;
        private SplitContainer calendarLeftSplitContainer;
        private TableLayoutPanel calendarGrid;
        private TableLayoutPanel dayInfoTableLayoutPanel;
        private Button btnPrevYear, btnPrevMonth, btnNextMonth, btnNextYear;
        private Label lblMonthYear;
        private FlowLayoutPanel dayInfoEventsPanel;
        private FlowLayoutPanel dayInfoTasksPanel;
        
        // カレンダー・タイムライン用状態変数
        private DateTime currentCalendarDate = DateTime.Today;
        private DateTime selectedCalendarDate = DateTime.Today;
        private string dragMode = "none"; // none, move, resizeTop, resizeBottom, createLog, createEvent
        private string dragItemType = null; // Event, TimeLog
        private object draggedItem = null;
        private Point dragStartPoint = Point.Empty;
        private RectangleF ghostRect = RectangleF.Empty;
        private float snapLineY = -1;
        private DateTime dragItemOriginalStartTime;
        private DateTime dragItemOriginalEndTime;
        private TimeLog selectedTimeLog = null;
        private EventItem selectedEvent = null;
        private Point dayInfoDragStartPoint = Point.Empty;

        private Panel timelinePanel;
        private Panel notificationPanel;
        private Label lblNotification;
        
        // タイマー
        private Timer trackingTimer;
        private Timer idleCheckTimer;
        private Timer notificationTimer;
        private HashSet<string> notifiedEventIds = new HashSet<string>();
        private string currentlyTrackingTaskID = null;
        private string suspendedTaskID = null;
        private DateTime? currentTaskStartTime = null;
        private int longTaskCheckSeconds = 0;
        private bool longTaskNotificationShown = false;
        private bool idleMessageShown = false;
        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayMenu;
        private bool forceExit = false;

        // 関連ファイル・プレビュー用コントロール
        private SplitContainer associatedFilesSplitContainer;
        private GroupBox associatedFilesGroup;
        private ListView fileListView;
        private ImageList globalImageList;
        private GroupBox previewGroup;
        private Panel previewPanel;

        // 関連ファイル 右クリックメニュー
        private ContextMenuStrip fileListContextMenu;
        private ToolStripMenuItem renameFileMenuItem;
        private ToolStripMenuItem openLocationMenuItem;
        private ToolStripMenuItem copyPathMenuItem;
        private ToolStripMenuItem addUrlMenuItem;
        private ToolStripMenuItem addMemoMenuItem;
        private ToolStripMenuItem deleteFileMenuItem;

        public FormMain()
        {
            // 実行ファイルのディレクトリをルートとする
            string appRoot = AppDomain.CurrentDomain.BaseDirectory;
            dataService = new DataService(appRoot);

            InitializeComponent();
            LoadData();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try {
                int useImmersiveDarkMode = isDarkMode ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.Text = string.Format("タスク管理マネージャー v{0}", AppVersion);
            this.Width = 1280;
            this.Height = 1024;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true; // フォーム全体でキーイベントを取得可能にする
            this.KeyDown += FormMain_KeyDown;

            // --- メインメニューの構築 ---
            mainMenu = new MenuStrip();

            var fileMenuItem = new ToolStripMenuItem("ファイル(&F)");
            var addNewTaskMenuItem = new ToolStripMenuItem("プロジェクト／タスクの新規追加(&N)") { ShortcutKeys = Keys.Control | Keys.N };
            addNewTaskMenuItem.Click += AddNewTaskAction;
            var addNewEventMenuItem = new ToolStripMenuItem("イベントの追加(&A)");
            addNewEventMenuItem.Click += (s, e) => {
                var form = new FormEventInput(null, selectedCalendarDate);
                ThemeManager.ApplyTheme(form, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK) {
                    string dateKey = selectedCalendarDate.ToString("yyyy-MM-dd");
                    if (!AllEvents.ContainsKey(dateKey)) AllEvents[dateKey] = new List<EventItem>();
                    AllEvents[dateKey].Add(form.ResultEvent);
                    dataService.SaveToJson(dataService.EventsFile, AllEvents);
                    UpdateAllViews();
                }
            };
            var globalSettingsMenuItem = new ToolStripMenuItem("全体設定(&O)");
            globalSettingsMenuItem.Click += (s, e) => {
                SyncWindowSizesFromDisk();
                var form = new FormSettings(Settings);
                ThemeManager.ApplyTheme(form, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    Settings = form.ResultSettings;
                    dataService.SaveToJson(dataService.SettingsFile, Settings);
                    isDarkMode = Settings.IsDarkMode;
                    isColorVisionSupport = Settings.EnableColorVisionSupport;
                    darkModeMenuItem.Checked = isDarkMode;
                    UpdateStartupShortcut();
                    UpdateTheme();
                    UpdateAllViews();
                    ApplyWindowSettings();
                }
            };
            var backupRestoreMenuItem = new ToolStripMenuItem("バックアップと復元(&B)");
            backupRestoreMenuItem.Click += (s, e) => {
                var form = new FormBackupRestore(dataService, Settings, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    ReloadDataAfterRestore();
                }
            };
            var reportMenuItem = new ToolStripMenuItem("レポート(&R)");
            reportMenuItem.Click += (s, e) => { 
                new FormReport(dataService, AllTimeLogs, AllTasks, Projects, Settings, isDarkMode).ShowDialog(this); 
            };
            var dailyReportMenuItem = new ToolStripMenuItem("日報の出力(&D)");
            dailyReportMenuItem.Click += (s, e) => {
                new FormDailyReport(AllTimeLogs, AllTasks, Projects, isDarkMode).ShowDialog(this);
            };
            var icsExchangeMenuItem = new ToolStripMenuItem("カレンダー連携 (ICS)...");
            icsExchangeMenuItem.Click += (s, e) => {
                var form = new FormIcsExchange(dataService, AllTasks, AllEvents, Projects, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    UpdateAllViews();
                }
            };
            var exitMenuItem = new ToolStripMenuItem("終了(&X)");
            exitMenuItem.Click += (s, e) => { forceExit = true; this.Close(); };
            fileMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
                addNewTaskMenuItem, addNewEventMenuItem, new ToolStripSeparator(),
                globalSettingsMenuItem, backupRestoreMenuItem, reportMenuItem, dailyReportMenuItem, icsExchangeMenuItem,
                new ToolStripSeparator(), exitMenuItem
            });

            var editMenuItem = new ToolStripMenuItem("編集(&E)");
            var editCategoriesMenuItem = new ToolStripMenuItem("カテゴリの編集(&C)");
            var editTemplatesMenuItem = new ToolStripMenuItem("テンプレートの編集(&M)");
            var manageRecurringMenuItem = new ToolStripMenuItem("定期タスクの設定(&R)");
            manageRecurringMenuItem.Click += (s, e) => {
                new FormRecurringRuleEditor(dataService, Projects).ShowDialog(this);
                InvokeRecurringTasks(); // 編集後に再評価
            };
            editMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
                editCategoriesMenuItem, editTemplatesMenuItem, manageRecurringMenuItem
            });
            
            editCategoriesMenuItem.Click += (s, e) => {
                SyncWindowSizesFromDisk();
                var form = new FormCategoryEditor(dataService, Categories, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK) {
                    Categories = dataService.LoadFromJson<Dictionary<string, List<string>>>(dataService.CategoriesFile, new Dictionary<string, List<string>>());
                    UpdateCategoryFilterComboBox();
                    UpdateAllViews();
                }
            };
            editTemplatesMenuItem.Click += (s, e) => {
                SyncWindowSizesFromDisk();
                var form = new FormTemplateEditor(dataService, isDarkMode);
                form.ShowDialog(this);
            };

            var viewMenuItem = new ToolStripMenuItem("表示(&V)");
            var toggleFilesPanelMenuItem = new ToolStripMenuItem("関連ファイルパネルの表示/非表示");
            toggleFilesPanelMenuItem.Click += (s, e) => { mainContainer.Panel2Collapsed = !mainContainer.Panel2Collapsed; };
            
            var groupingMenuItem = new ToolStripMenuItem("表示方法の切替 (プロジェクト/カテゴリ)") { CheckOnClick = true, Checked = true };
            groupingMenuItem.Click += (s, e) => { groupByProject = groupingMenuItem.Checked; UpdateAllViews(); };
            
            var hideCompletedMenuItem = new ToolStripMenuItem("完了したタスクを隠す") { CheckOnClick = true };
            hideCompletedMenuItem.Click += (s, e) => { 
                if (Settings != null) { Settings.HideCompletedTasks = hideCompletedMenuItem.Checked; dataService.SaveToJson(dataService.SettingsFile, Settings); }
                UpdateAllViews(); 
            };
            
            var showKanbanDoneMenuItem = new ToolStripMenuItem("カンバンの完了列を表示") { CheckOnClick = true, Checked = true };
            showKanbanDoneMenuItem.Click += (s, e) => { 
                if (Settings != null) { Settings.ShowKanbanDone = showKanbanDoneMenuItem.Checked; dataService.SaveToJson(dataService.SettingsFile, Settings); }
                UpdateAllViews(); 
            };
            
            darkModeMenuItem = new ToolStripMenuItem("ダークモード") { CheckOnClick = true };
            var viewArchiveMenuItem = new ToolStripMenuItem("アーカイブビューを開く...");
            viewArchiveMenuItem.Click += (s, e) => {
                var form = new FormArchiveView(dataService, AllTasks, Projects, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    AllTasks = dataService.LoadTasksFromCsv(dataService.TasksFile);
                    Projects = dataService.LoadFromJson<List<ProjectItem>>(dataService.ProjectsFile, new List<ProjectItem>());
                    UpdateAllViews();
                }
            };
            darkModeMenuItem.Click += DarkModeMenuItem_Click;
            viewMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
                toggleFilesPanelMenuItem, groupingMenuItem, new ToolStripSeparator(),
                hideCompletedMenuItem, showKanbanDoneMenuItem, darkModeMenuItem, new ToolStripSeparator(),
                viewArchiveMenuItem
            });

            mainMenu.Items.AddRange(new ToolStripItem[] { fileMenuItem, editMenuItem, viewMenuItem });
            this.MainMenuStrip = mainMenu;

            // --- ツールバーの構築 ---
            toolStrip = new ToolStrip { ImageScalingSize = new Size(24, 24), AutoSize = false, Height = 48 };

            var btnAdd = new ToolStripDropDownButton("新規追加") { 
                Image = SystemIcons.Application.ToBitmap(), 
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, 
                TextImageRelation = TextImageRelation.ImageAboveText,
                AutoSize = false, Size = new Size(70, 45)
            };
            var addTaskMenu = new ToolStripMenuItem("プロジェクト／タスクの新規追加");
            addTaskMenu.Click += AddNewTaskAction;
            var addEventMenu = new ToolStripMenuItem("イベントの追加");
            addEventMenu.Click += (s, e) => {
                var form = new FormEventInput(null, selectedCalendarDate);
                ThemeManager.ApplyTheme(form, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK) {
                    string dateKey = selectedCalendarDate.ToString("yyyy-MM-dd");
                    if (!AllEvents.ContainsKey(dateKey)) AllEvents[dateKey] = new List<EventItem>();
                    AllEvents[dateKey].Add(form.ResultEvent);
                    dataService.SaveToJson(dataService.EventsFile, AllEvents);
                    UpdateAllViews();
                }
            };
            btnAdd.DropDownItems.AddRange(new ToolStripItem[] { addTaskMenu, addEventMenu });
            var btnAddFromTemplate = new ToolStripButton("テンプレートから追加") { 
                Image = SystemIcons.Application.ToBitmap(), 
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, 
                TextImageRelation = TextImageRelation.ImageAboveText,
                AutoSize = false, Size = new Size(130, 45)
            };
            btnAddFromTemplate.Click += (s, e) => {
                var form = new FormTemplate(dataService, Projects, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK) {
                    if (form.NewProject != null) Projects.Add(form.NewProject);
                    if (form.NewTasks != null) AllTasks.AddRange(form.NewTasks);
                    dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                    UpdateAllViews();
                }
            };
            var btnNotifications = new ToolStripButton("🔔 通知") { 
                Image = SystemIcons.Information.ToBitmap(), 
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, 
                TextImageRelation = TextImageRelation.ImageAboveText,
                AutoSize = false, Size = new Size(70, 45)
            };
            btnNotifications.Click += BtnNotifications_Click;
            var btnLatestReport = new ToolStripButton("最新のレポート") { 
                Image = SystemIcons.Application.ToBitmap(), 
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, 
                TextImageRelation = TextImageRelation.ImageAboveText,
                AutoSize = false, Size = new Size(100, 45)
            };
            btnLatestReport.Click += (s, e) => { 
                new FormReport(dataService, AllTimeLogs, AllTasks, Projects, Settings, isDarkMode).ShowDialog(this); 
            };
            var btnQuickSettings = new ToolStripButton("⚙️ クイック設定") { 
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, 
                TextImageRelation = TextImageRelation.ImageAboveText,
                AutoSize = false, Size = new Size(90, 45),
                Alignment = ToolStripItemAlignment.Right
            };
            btnQuickSettings.Click += (s, e) => {
                quickSettingsPanel.Visible = !quickSettingsPanel.Visible;
                if (quickSettingsPanel.Visible) SyncQuickSettingsToUI();
            };

            categoryFilterComboBox = new ToolStripComboBox { Alignment = ToolStripItemAlignment.Right, Width = 150 };
            categoryFilterComboBox.SelectedIndexChanged += (s, e) => {
                currentCategoryFilter = categoryFilterComboBox.SelectedItem != null ? categoryFilterComboBox.SelectedItem.ToString() : "(すべて)";
                UpdateAllViews();
            };
            var lblCategoryFilter = new ToolStripLabel("カテゴリ絞り込み:") { Alignment = ToolStripItemAlignment.Right };

            toolStrip.Items.AddRange(new ToolStripItem[] {
                btnAdd, btnAddFromTemplate, new ToolStripSeparator(),
                btnNotifications, btnLatestReport,
                btnQuickSettings, categoryFilterComboBox, lblCategoryFilter
            });

            // --- ステータスバーの構築 ---
            statusBar = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("読み込み中...");
            statusBar.Items.Add(statusLabel);

            // --- コンテナの初期化 ---
            mainContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 550
            };
            
            // --- クイック設定パネルの構築 ---
            quickSettingsPanel = new Panel { Dock = DockStyle.Right, Width = 260, Visible = false, BorderStyle = BorderStyle.FixedSingle };
            var qFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(15) };
            quickSettingsPanel.Controls.Add(qFlow);

            var qTitle = new Label { Text = "⚙️ クイック設定", Font = new Font("Meiryo UI", 11, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 15) };
            qFlow.Controls.Add(qTitle);

            qFlow.Controls.Add(new Label { Text = "ウィンドウ透明度:", AutoSize = true });
            qTbOpacity = new TrackBar { Minimum = 5, Maximum = 10, Width = 200, TickFrequency = 1 };
            qTbOpacity.ValueChanged += (s, e) => { if (Settings != null) { Settings.WindowOpacity = qTbOpacity.Value / 10.0; this.Opacity = Settings.WindowOpacity; } };
            qFlow.Controls.Add(qTbOpacity);

            qFlow.Controls.Add(new Label { Text = "タイムライン開始時刻:", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
            qNumTimeStart = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 80 };
            qNumTimeStart.ValueChanged += (s, e) => { if (Settings != null) { Settings.TimelineStartHour = (int)qNumTimeStart.Value; if (selectedCalendarDate != null) UpdateTimelineView(selectedCalendarDate); } };
            qFlow.Controls.Add(qNumTimeStart);

            qFlow.Controls.Add(new Label { Text = "タイムライン終了時刻:", AutoSize = true });
            qNumTimeEnd = new NumericUpDown { Minimum = 1, Maximum = 24, Width = 80 };
            qNumTimeEnd.ValueChanged += (s, e) => { if (Settings != null) { Settings.TimelineEndHour = (int)qNumTimeEnd.Value; if (selectedCalendarDate != null) UpdateTimelineView(selectedCalendarDate); } };
            qFlow.Controls.Add(qNumTimeEnd);

            qFlow.Controls.Add(new Label { Text = "リスト行間:", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
            qCmbDensity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            qCmbDensity.Items.AddRange(new[] { "狭い (Compact)", "標準 (Standard)", "広い (Relaxed)" });
            qCmbDensity.SelectedIndexChanged += (s, e) => { 
                if (Settings != null && qCmbDensity.SelectedIndex >= 0) { 
                    string density = qCmbDensity.SelectedIndex == 0 ? "Compact" : (qCmbDensity.SelectedIndex == 2 ? "Relaxed" : "Standard");
                    Settings.ListDensity = density; UpdateDataGridView(); 
                } 
            };
            qFlow.Controls.Add(qCmbDensity);

            qChkKanbanDone = new CheckBox { Text = "カンバンの完了列を表示", AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            qChkKanbanDone.CheckedChanged += (s, e) => { if (Settings != null) { Settings.ShowKanbanDone = qChkKanbanDone.Checked; UpdateKanbanColumnsVisibility(); } };
            qFlow.Controls.Add(qChkKanbanDone);

            qFlow.Controls.Add(new Label { Text = "ポモドーロ (分):", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
            qNumPomodoro = new NumericUpDown { Minimum = 1, Maximum = 120, Width = 80 };
            qNumPomodoro.ValueChanged += (s, e) => { if (Settings != null) Settings.PomodoroWorkMinutes = (int)qNumPomodoro.Value; };
            qFlow.Controls.Add(qNumPomodoro);

            var btnSaveQuick = new Button { Text = "設定を保存", Width = 150, Height = 30, Margin = new Padding(0, 20, 0, 0) };
            btnSaveQuick.Click += (s, e) => {
                dataService.SaveToJson(dataService.SettingsFile, Settings);
                MessageBox.Show("クイック設定を保存しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            qFlow.Controls.Add(btnSaveQuick);

            this.Controls.Add(quickSettingsPanel);
            this.Controls.Add(statusBar);
            this.Controls.Add(mainContainer);
            this.Controls.Add(toolStrip);
            this.Controls.Add(mainMenu);

            notificationPanel = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.LightYellow, Visible = false };
            lblNotification = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Meiryo UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            lblNotification.Click += (s, e) => { InvokeRecurringTasks(); UpdateAllViews(); };
            notificationPanel.Controls.Add(lblNotification);
            this.Controls.Add(notificationPanel);
            notificationPanel.BringToFront(); // メインコンテナ等より手前に
            quickSettingsPanel.BringToFront(); // 右端に固定するため最前面に

            // --- タブコントロール ---
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed
            };
            tabControl.DrawItem += TabControl_DrawItem;
            mainContainer.Panel1.Controls.Add(tabControl);

            listTabPage = new TabPage("リスト表示");
            kanbanTabPage = new TabPage("カンバンボード");
            calendarTabPage = new TabPage("カレンダー表示");
            
            tabControl.TabPages.Add(listTabPage);
            tabControl.TabPages.Add(kanbanTabPage);
            tabControl.TabPages.Add(calendarTabPage);

            // --- DataGridView ---
            taskDataGridView = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32
            };
            taskDataGridView.RowTemplate.Height = 28; // 行の高さを少し広げて見やすく
            
            InitializeDataGridViewColumns();
            taskDataGridView.CellPainting += TaskDataGridView_CellPainting;
            taskDataGridView.CellClick += TaskDataGridView_CellClick;
            taskDataGridView.CellDoubleClick += TaskDataGridView_CellDoubleClick;
            taskDataGridView.CellMouseDown += TaskDataGridView_CellMouseDown;
            taskDataGridView.MouseMove += TaskDataGridView_MouseMove;
            taskDataGridView.KeyDown += TaskDataGridView_KeyDown;
            
            // --- リストのスクロール時のちらつき（Flickering）を防止 ---
            typeof(DataGridView).InvokeMember("DoubleBuffered", 
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, 
                null, taskDataGridView, new object[] { true });
            listTabPage.Controls.Add(taskDataGridView);

            // --- DataGridView 右クリックメニューの構築 ---
            dgvContextMenu = new ContextMenuStrip();
            dgvEditMenuItem = new ToolStripMenuItem("編集");
            dgvDeleteMenuItem = new ToolStripMenuItem("削除");
            dgvChangeStatusMenuItem = new ToolStripMenuItem("進捗度の変更");
            foreach (string status in TaskStatuses) {
                var subItem = new ToolStripMenuItem(status) { Tag = status };
                subItem.Click += DgvChangeStatusMenuItem_Click;
                dgvChangeStatusMenuItem.DropDownItems.Add(subItem);
            }
            dgvProjectPropertiesMenuItem = new ToolStripMenuItem("プロパティの編集");
            dgvProjectDeleteMenuItem = new ToolStripMenuItem("削除");
            dgvArchiveMenuItem = new ToolStripMenuItem("アーカイブ");
            var dgvMakeRecurringMenuItem = new ToolStripMenuItem("このアイテムを定期ルールに登録");
            dgvMakeRecurringMenuItem.Click += (s, e) => {
                if (taskDataGridView.SelectedRows.Count > 0) {
                    new FormRecurringRuleEditor(dataService, Projects, taskDataGridView.SelectedRows[0].Tag).ShowDialog(this);
                    InvokeRecurringTasks();
                }
            };
            dgvBulkCompleteMenuItem = new ToolStripMenuItem("選択したタスクを一括完了");
            dgvBulkDeleteMenuItem = new ToolStripMenuItem("選択したタスクを一括削除");

            dgvContextMenu.Items.AddRange(new ToolStripItem[] {
                dgvEditMenuItem, dgvDeleteMenuItem, dgvChangeStatusMenuItem,
                dgvBulkCompleteMenuItem, dgvBulkDeleteMenuItem,
                dgvMakeRecurringMenuItem,
                new ToolStripSeparator(),
                dgvProjectPropertiesMenuItem, dgvProjectDeleteMenuItem,
                new ToolStripSeparator(),
                dgvArchiveMenuItem
            });
            
            dgvContextMenu.Opening += DgvContextMenu_Opening;
            dgvEditMenuItem.Click += DgvEditMenuItem_Click;
            dgvDeleteMenuItem.Click += DgvDeleteMenuItem_Click;
            dgvProjectPropertiesMenuItem.Click += DgvProjectPropertiesMenuItem_Click;
            dgvProjectDeleteMenuItem.Click += DgvProjectDeleteMenuItem_Click;
            dgvArchiveMenuItem.Click += DgvArchiveMenuItem_Click;
            dgvBulkCompleteMenuItem.Click += DgvBulkCompleteMenuItem_Click;
            dgvBulkDeleteMenuItem.Click += DgvBulkDeleteMenuItem_Click;
            
            taskDataGridView.ContextMenuStrip = dgvContextMenu;
            taskDataGridView.SelectionChanged += TaskDataGridView_SelectionChanged;

            // --- 下部パネル (関連ファイルとプレビュー) ---
            mainContainer.Panel2.Padding = new Padding(0, 5, 0, 0);
            
            associatedFilesSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = (int)(this.Width * 0.4) // リスト領域を広げるため40%にする
            };
            mainContainer.Panel2.Controls.Add(associatedFilesSplitContainer);

            associatedFilesGroup = new GroupBox { Text = "関連ファイル", Dock = DockStyle.Fill };
            associatedFilesSplitContainer.Panel1.Controls.Add(associatedFilesGroup);

            globalImageList = new ImageList();
            globalImageList.Images.Add("__file__", SystemIcons.WinLogo);
            globalImageList.Images.Add("__folder__", SystemIcons.Warning);

            fileListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                AllowDrop = true,
                FullRowSelect = true,
                SmallImageList = globalImageList,
                MultiSelect = true,
                OwnerDraw = true
            };
            fileListView.Columns.Add("ファイル名", 400);
            fileListView.Columns.Add("種類", 100);
            fileListView.Columns.Add("追加日", 150);
            fileListView.DrawColumnHeader += FileListView_DrawColumnHeader;
            fileListView.DrawItem += (s, ev) => ev.DrawDefault = true;
            fileListView.DrawSubItem += (s, ev) => ev.DrawDefault = true;
            fileListView.DragEnter += FileListView_DragEnter;
            fileListView.DragDrop += FileListView_DragDrop;
            fileListView.DoubleClick += FileListView_DoubleClick;
            fileListView.SelectedIndexChanged += FileListView_SelectedIndexChanged;
            fileListView.KeyDown += FileListView_KeyDown;
            associatedFilesGroup.Controls.Add(fileListView);

            // --- 関連ファイルリストの右クリックメニュー ---
            fileListContextMenu = new ContextMenuStrip();
            renameFileMenuItem = new ToolStripMenuItem("名前の変更");
            openLocationMenuItem = new ToolStripMenuItem("ファイルの場所を開く");
            copyPathMenuItem = new ToolStripMenuItem("パスをコピー");
            addUrlMenuItem = new ToolStripMenuItem("URLを追加...");
            addMemoMenuItem = new ToolStripMenuItem("メモを追加...");
            deleteFileMenuItem = new ToolStripMenuItem("削除");
            fileListContextMenu.Items.AddRange(new ToolStripItem[] { renameFileMenuItem, openLocationMenuItem, copyPathMenuItem, addUrlMenuItem, addMemoMenuItem, new ToolStripSeparator(), deleteFileMenuItem });
            fileListContextMenu.Opening += FileListContextMenu_Opening;
            renameFileMenuItem.Click += RenameFileMenuItem_Click;
            openLocationMenuItem.Click += OpenLocationMenuItem_Click;
            copyPathMenuItem.Click += CopyPathMenuItem_Click;
            addUrlMenuItem.Click += AddUrlMenuItem_Click;
            addMemoMenuItem.Click += AddMemoMenuItem_Click;
            deleteFileMenuItem.Click += DeleteFileMenuItem_Click;
            fileListView.ContextMenuStrip = fileListContextMenu;

            previewGroup = new GroupBox { Text = "プレビュー", Dock = DockStyle.Fill };
            associatedFilesSplitContainer.Panel2.Controls.Add(previewGroup);
            previewPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            previewGroup.Controls.Add(previewPanel);

            // --- タイムライン（カスタム描画パネル） ---
            timelinePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle
            };
            // C#ではDoubleBufferedプロパティをリフレクション経由、または派生クラスで有効化します。
            typeof(Panel).InvokeMember("DoubleBuffered", 
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, 
                null, timelinePanel, new object[] { true });
                
            timelinePanel.Paint += TimelinePanel_Paint;
            
            // --- カンバンとカレンダーの構築 ---
            InitializeKanbanView();
            InitializeCalendarView();

            trackingTimer = new Timer { Interval = 1000 };
            trackingTimer.Tick += TrackingTimer_Tick;
            idleCheckTimer = new Timer { Interval = 30000 };
            idleCheckTimer.Tick += IdleCheckTimer_Tick;

            notificationTimer = new Timer { Interval = 60000 };
            notificationTimer.Tick += NotificationTimer_Tick;
            notificationTimer.Start();
            
            tabControl.SelectedIndexChanged += (s, e) => 
            {
                mainContainer.Panel2Collapsed = (tabControl.SelectedTab != listTabPage);
            };

            // --- タスクトレイアイコンの準備 ---
            notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Task Manager",
                Visible = true
            };
            notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            };

            trayMenu = new ContextMenuStrip();
            var trayExitItem = new ToolStripMenuItem("終了");
            trayExitItem.Click += (s, e) => { forceExit = true; this.Close(); };
            trayMenu.Items.Add(trayExitItem);
            notifyIcon.ContextMenuStrip = trayMenu;

            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            this.FormClosed += (s, e) => SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;

            // --- カスタムアイコンの読み込み ---
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uni consul（ユニコン）.png");
                if (File.Exists(iconPath))
                {
                    using (Bitmap bmp = new Bitmap(iconPath))
                    {
                        IntPtr hIcon = bmp.GetHicon();
                        Icon customIcon = Icon.FromHandle(hIcon);
                        this.Icon = customIcon;
                        if (notifyIcon != null) notifyIcon.Icon = customIcon;

                        this.FormClosed += (s, e) => {
                            customIcon.Dispose();
                            NativeMethods.DestroyIcon(hIcon);
                        };
                    }
                }
                else
                {
                    MessageBox.Show("アイコン画像が見つかりません。\n探した場所:\n" + iconPath, "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("アイコンの読み込み中にエラーが発生しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Dictionary<string, string> GetHolidays()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "holidays.json");
            if (File.Exists(path))
            {
                try
                {
                    var loaded = dataService.LoadFromJson<Dictionary<string, string>>(path, new Dictionary<string, string>());
                    if (loaded != null && loaded.Count > 0) return loaded;
                }
                catch { }
            }

            return new Dictionary<string, string>
            {
                { "2026-01-01", "元日" }, { "2026-01-12", "成人の日" }, { "2026-02-11", "建国記念の日" }, { "2026-02-23", "天皇誕生日" },
                { "2026-03-20", "春分の日" }, { "2026-04-29", "昭和の日" }, { "2026-05-03", "憲法記念日" }, { "2026-05-04", "みどりの日" },
                { "2026-05-05", "こどもの日" }, { "2026-05-06", "振替休日" }, { "2026-07-20", "海の日" }, { "2026-08-11", "山の日" },
                { "2026-09-21", "敬老の日" }, { "2026-09-22", "国民の休日" }, { "2026-09-23", "秋分の日" }, { "2026-10-12", "スポーツの日" },
                { "2026-11-03", "文化の日" }, { "2026-11-23", "勤労感謝の日" }
            };
        }

        private DateTime GetNotifyDate(DateTime dueDate, string notificationSetting)
        {
            if (string.IsNullOrEmpty(notificationSetting) || notificationSetting == "全体設定に従う")
            {
                notificationSetting = Settings != null && Settings.GlobalNotification != null ? Settings.GlobalNotification : "当日";
            }

            switch (notificationSetting)
            {
                case "1日前": return dueDate.AddDays(-1);
                case "3日前": return dueDate.AddDays(-3);
                case "1週間前": return dueDate.AddDays(-7);
                case "前の営業日":
                    DateTime prev = dueDate.AddDays(-1);
                    while (prev.DayOfWeek == DayOfWeek.Saturday || prev.DayOfWeek == DayOfWeek.Sunday)
                    {
                        prev = prev.AddDays(-1);
                    }
                    return prev;
                case "当日":
                default:
                    return dueDate;
            }
        }

        private void BtnNotifications_Click(object sender, EventArgs e)
        {
            var messages = new List<string>();
            DateTime today = DateTime.Today;

            // 期限超過、または事前通知日を迎えたタスクを抽出
            var alertTasks = AllTasks.Where(t => t.進捗度 != "完了済み" && !string.IsNullOrEmpty(t.期日)).ToList();
            foreach (var t in alertTasks)
            {
                DateTime due;
                if (DateTime.TryParse(t.期日, out due))
                {
                    string notifySetting = t.通知設定;
                    if (notifySetting == "全体設定に従う" && !string.IsNullOrEmpty(t.ProjectID))
                    {
                        var proj = Projects.FirstOrDefault(p => p.ProjectID == t.ProjectID);
                        if (proj != null && proj.Notification != "全体設定に従う")
                        {
                            notifySetting = proj.Notification;
                        }
                    }
                    
                    if (notifySetting == "通知しない") continue;
                    
                    DateTime notifyDate = GetNotifyDate(due, notifySetting);

                    if (notifyDate.Date <= today)
                    {
                        messages.Add(string.Format("[タスク] {0} (期日: {1:yyyy/MM/dd})", t.タスク, due));
                    }
                }
            }

            // プロジェクトの期日も通知の対象とする
            foreach (var p in Projects)
            {
                DateTime due;
                if (!string.IsNullOrEmpty(p.ProjectDueDate) && DateTime.TryParse(p.ProjectDueDate, out due))
                {
                    string notifySetting = p.Notification;
                    if (notifySetting == "通知しない") continue;
                    
                    DateTime notifyDate = GetNotifyDate(due, notifySetting);
                    if (notifyDate.Date <= today)
                    {
                        messages.Add(string.Format("[プロジェクト] {0} (期日: {1:yyyy/MM/dd})", p.ProjectName, due));
                    }
                }
            }

            // 本日のイベントを抽出
            string todayStr = today.ToString("yyyy-MM-dd");
            if (AllEvents.ContainsKey(todayStr))
            {
                foreach (var evt in AllEvents[todayStr])
                {
                    string timeStr = "終日";
                    if (!evt.IsAllDay)
                    {
                        DateTime st, et;
                        bool hasStart = DateTime.TryParse(evt.StartTime, out st);
                        bool hasEnd = DateTime.TryParse(evt.EndTime, out et);
                        if (hasStart && hasEnd)
                        {
                            timeStr = string.Format("{0:HH:mm} - {1:HH:mm}", st, et);
                        }
                        else if (hasStart)
                        {
                            timeStr = st.ToString("HH:mm");
                        }
                        else
                        {
                            timeStr = evt.StartTime;
                        }
                    }
                    messages.Add(string.Format("[イベント] {0} ({1})", evt.Title, timeStr));
                }
            }

            if (messages.Count == 0)
            {
                MessageBox.Show("現在、お知らせする通知はありません。", "通知", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                using (var form = new FormNotification(messages, isDarkMode))
                    form.ShowDialog(this);
            }
        }

        private void NotificationTimer_Tick(object sender, EventArgs e)
        {
            if (Settings != null && Settings.EventNotificationEnabled)
            {
                DateTime now = DateTime.Now;
                DateTime limit = now.AddMinutes(Settings.EventNotificationMinutes);
                string todayStr = now.ToString("yyyy-MM-dd");

                if (AllEvents.ContainsKey(todayStr))
                {
                    foreach (var evt in AllEvents[todayStr])
                    {
                        if (evt.IsAllDay || string.IsNullOrEmpty(evt.StartTime)) continue;
                        DateTime startTime;
                        if (DateTime.TryParse(evt.StartTime, out startTime))
                        {
                            // 開始時刻が「現在」～「設定したX分後」の間にあり、かつまだ通知していない場合
                            if (startTime > now && startTime <= limit && !notifiedEventIds.Contains(evt.ID))
                            {
                                ShowNotification("イベント通知", string.Format("まもなく予定の時間です: {0} ({1:HH:mm})", evt.Title, startTime));
                                notifiedEventIds.Add(evt.ID);
                            }
                        }
                    }
                }
            }
        }

        private void ShowNotification(string title, string message)
        {
            string style = Settings != null && Settings.NotificationStyle != null ? Settings.NotificationStyle : "Dialog";
            if (style == "Balloon" && notifyIcon != null && notifyIcon.Visible)
            {
                notifyIcon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
            }
            else
            {
                MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void InitializeKanbanView()
        {
            kanbanLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = TaskStatuses.Length,
                RowCount = 2
            };
            kanbanTabPage.Controls.Add(kanbanLayout);

            for (int i = 0; i < TaskStatuses.Length; i++)
            {
                kanbanLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / TaskStatuses.Length));
            }
            kanbanLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            kanbanLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // --- カンバン 右クリックメニューの構築 ---
            kanbanContextMenu = new ContextMenuStrip();
            kanbanEditMenuItem = new ToolStripMenuItem("編集");
            kanbanDeleteMenuItem = new ToolStripMenuItem("削除");
            var kanbanMakeRecurringMenuItem = new ToolStripMenuItem("このアイテムを定期ルールに登録");
            kanbanMakeRecurringMenuItem.Click += (s, e) => {
                var listBox = kanbanContextMenu.SourceControl as ListBox;
                if (listBox != null) {
                    var task = listBox.SelectedItem as TaskItem;
                    if (task != null) {
                        new FormRecurringRuleEditor(dataService, Projects, task).ShowDialog(this);
                        InvokeRecurringTasks();
                        UpdateAllViews();
                    }
                }
            };
            kanbanContextMenu.Items.AddRange(new ToolStripItem[] { kanbanEditMenuItem, kanbanDeleteMenuItem, new ToolStripSeparator(), kanbanMakeRecurringMenuItem });
            kanbanEditMenuItem.Click += KanbanEditMenuItem_Click;
            kanbanDeleteMenuItem.Click += KanbanDeleteMenuItem_Click;

            for (int i = 0; i < TaskStatuses.Length; i++)
            {
                string status = TaskStatuses[i];
                var header = new Label
                {
                    Text = status,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Meiryo UI", 10, FontStyle.Bold),
                    MinimumSize = new Size(1, 30)
                };
                kanbanLayout.Controls.Add(header, i, 0);

                var listBox = new ListBox
                {
                    Dock = DockStyle.Fill,
                    AllowDrop = true,
                    DisplayMember = "タスク", // TaskItemプロパティの名称に合わせる
                    SelectionMode = SelectionMode.One,
                    DrawMode = DrawMode.OwnerDrawFixed,
                    ItemHeight = 65, // カンバンカードの高さを広げる
                    Tag = status
                };
                listBox.ContextMenuStrip = kanbanContextMenu;
                listBox.DoubleClick += KanbanListBox_DoubleClick;
                listBox.DrawItem += KanbanListBox_DrawItem;
                listBox.MouseDown += KanbanListBox_MouseDown;
                listBox.MouseMove += KanbanListBox_MouseMove;
                listBox.DragEnter += KanbanListBox_DragEnter;
                listBox.DragDrop += KanbanListBox_DragDrop;
                listBox.KeyDown += KanbanListBox_KeyDown;
                kanbanLayout.Controls.Add(listBox, i, 1);
                kanbanLists[status] = listBox;
            }
        }

        private void InitializeCalendarView()
        {
            calendarSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = (int)(this.Width * 0.66)
            };
            calendarTabPage.Controls.Add(calendarSplitContainer);

            calendarLeftSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 400
            };
            calendarSplitContainer.Panel1.Controls.Add(calendarLeftSplitContainer);

            // --- 左上：カレンダーグリッド ---
            var calendarGridPanel = new Panel { Dock = DockStyle.Fill };
            calendarLeftSplitContainer.Panel1.Controls.Add(calendarGridPanel);

            var navPanel = new Panel { Dock = DockStyle.Top, Height = 40 };
            calendarGridPanel.Controls.Add(navPanel);

            var navFlowLayout = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0), AutoSize = true, WrapContents = false };
            navPanel.Controls.Add(navFlowLayout);
            
            navPanel.Resize += (s, e) => {
                navFlowLayout.Location = new Point((navPanel.Width - navFlowLayout.Width) / 2, 0);
            };
            
            btnPrevYear = new Button { Text = "<<", AutoSize = false, Size = new Size(40, 28), FlatStyle = FlatStyle.Flat };
            btnPrevMonth = new Button { Text = "<", AutoSize = false, Size = new Size(35, 28), FlatStyle = FlatStyle.Flat };
            lblMonthYear = new Label { Text = currentCalendarDate.ToString("yyyy年 MM月"), Font = new Font("Meiryo UI", 12, FontStyle.Bold), Margin = new Padding(20, 4, 20, 0), AutoSize = true };
            btnNextMonth = new Button { Text = ">", AutoSize = false, Size = new Size(35, 28), FlatStyle = FlatStyle.Flat };
            btnNextYear = new Button { Text = ">>", AutoSize = false, Size = new Size(40, 28), FlatStyle = FlatStyle.Flat };
            navFlowLayout.Controls.AddRange(new Control[] { btnPrevYear, btnPrevMonth, lblMonthYear, btnNextMonth, btnNextYear });

            btnPrevYear.Click += (s, e) => { currentCalendarDate = currentCalendarDate.AddYears(-1); UpdateCalendarGrid(currentCalendarDate); };
            btnPrevMonth.Click += (s, e) => { currentCalendarDate = currentCalendarDate.AddMonths(-1); UpdateCalendarGrid(currentCalendarDate); };
            btnNextMonth.Click += (s, e) => { currentCalendarDate = currentCalendarDate.AddMonths(1); UpdateCalendarGrid(currentCalendarDate); };
            btnNextYear.Click += (s, e) => { currentCalendarDate = currentCalendarDate.AddYears(1); UpdateCalendarGrid(currentCalendarDate); };

            calendarGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 7 };
            for (int i = 0; i < 7; i++) calendarGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.28f));
            calendarGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            for (int i = 0; i < 6; i++) calendarGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 16.66f));
            
            calendarGridPanel.Controls.Add(calendarGrid);
            calendarGrid.BringToFront();

            // --- 左下：選択日の詳細 ---
            var dayInfoGroupBox = new GroupBox { Text = "選択日の詳細", Dock = DockStyle.Fill };
            calendarLeftSplitContainer.Panel2.Controls.Add(dayInfoGroupBox);

            dayInfoTableLayoutPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            dayInfoTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            dayInfoTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            
            var eventsGroup = new GroupBox { Text = "イベント", Dock = DockStyle.Fill };
            dayInfoEventsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            eventsGroup.Controls.Add(dayInfoEventsPanel);
            dayInfoTableLayoutPanel.Controls.Add(eventsGroup, 0, 0);

            var tasksGroup = new GroupBox { Text = "期日 (プロジェクト/タスク)", Dock = DockStyle.Fill };
            dayInfoTasksPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            tasksGroup.Controls.Add(dayInfoTasksPanel);
            dayInfoTableLayoutPanel.Controls.Add(tasksGroup, 1, 0);

            dayInfoGroupBox.Controls.Add(dayInfoTableLayoutPanel);

            // --- 右：タイムラインパネル ---
            calendarSplitContainer.Panel2.Controls.Add(timelinePanel);
            
            timelinePanel.MouseDown += TimelinePanel_MouseDown;
            timelinePanel.MouseMove += TimelinePanel_MouseMove;
            timelinePanel.MouseUp += TimelinePanel_MouseUp;
            timelinePanel.MouseDoubleClick += TimelinePanel_MouseDoubleClick;
            timelinePanel.MouseClick += TimelinePanel_MouseClick;
            timelinePanel.KeyDown += TimelinePanel_KeyDown;
            timelinePanel.TabStop = true;
            
            timelinePanel.AllowDrop = true;
            timelinePanel.DragEnter += TimelinePanel_DragEnter;
            timelinePanel.DragDrop += TimelinePanel_DragDrop;
        }

        private void LoadData()
        {
            try
            {
                Settings = dataService.LoadSettings();
                AllTasks = dataService.LoadTasksFromCsv(dataService.TasksFile);
                Projects = dataService.LoadFromJson<List<ProjectItem>>(dataService.ProjectsFile, new List<ProjectItem>());
                Categories = dataService.LoadFromJson<Dictionary<string, List<string>>>(dataService.CategoriesFile, new Dictionary<string, List<string>>());
                AllEvents = dataService.LoadFromJson<Dictionary<string, List<EventItem>>>(dataService.EventsFile, new Dictionary<string, List<EventItem>>());
                AllTimeLogs = dataService.LoadFromJson<List<TimeLog>>(dataService.TimeLogsFile, new List<TimeLog>());

                isDarkMode = Settings.IsDarkMode;
                isColorVisionSupport = Settings.EnableColorVisionSupport;
                darkModeMenuItem.Checked = isDarkMode;

                UpdateCategoryFilterComboBox();

                dataService.StartAutomaticBackup(Settings);
                dataService.CompressOldArchives(Settings);

                InvokeRecurringTasks(); // アプリ起動時に定期タスク生成エンジンを実行
                UpdateStartupShortcut(); // スタートアップショートカットの同期

                UpdateTheme();
                UpdateAllViews();
                ApplyWindowSettings();
                if (quickSettingsPanel != null && quickSettingsPanel.Visible) SyncQuickSettingsToUI();

                if (Settings != null && !string.IsNullOrEmpty(Settings.Passcode))
                {
                    if (!ShowLoginDialog())
                    {
                        Environment.Exit(0); // 認証に失敗、またはキャンセルされた場合はアプリを終了
                    }
                }

                InvokeAutoArchiving();
                InvokeProjectAutoArchiving();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("ファイル名の変更中にエラーが発生しました:\n{0}", ex.Message), "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            idleCheckTimer.Start();
        }

        private void SyncWindowSizesFromDisk()
        {
            if (Settings == null) return;
            try {
                var latest = dataService.LoadSettings();
                if (latest != null && latest.WindowSizes != null) {
                    if (Settings.WindowSizes == null) Settings.WindowSizes = new Dictionary<string, string>();
                    foreach (var kvp in latest.WindowSizes) {
                        Settings.WindowSizes[kvp.Key] = kvp.Value;
                    }
                }
            } catch {}
        }

        private void SyncQuickSettingsToUI()
        {
            if (Settings == null) return;
            try { qTbOpacity.Value = Math.Max(5, Math.Min(10, (int)(Settings.WindowOpacity * 10))); } catch {}
            try { qNumTimeStart.Value = Settings.TimelineStartHour; } catch {}
            try { qNumTimeEnd.Value = Settings.TimelineEndHour; } catch {}
            try { 
                if (Settings.ListDensity == "Compact") qCmbDensity.SelectedIndex = 0;
                else if (Settings.ListDensity == "Relaxed") qCmbDensity.SelectedIndex = 2;
                else qCmbDensity.SelectedIndex = 1;
            } catch {}
            try { qChkKanbanDone.Checked = Settings.ShowKanbanDone; } catch {}
            try { qNumPomodoro.Value = Settings.PomodoroWorkMinutes; } catch {}
        }

        private void ReloadDataAfterRestore()
        {
            Settings = dataService.LoadSettings();
            AllTasks = dataService.LoadTasksFromCsv(dataService.TasksFile);
            Projects = dataService.LoadFromJson<List<ProjectItem>>(dataService.ProjectsFile, new List<ProjectItem>());
            Categories = dataService.LoadFromJson<Dictionary<string, List<string>>>(dataService.CategoriesFile, new Dictionary<string, List<string>>());
            AllEvents = dataService.LoadFromJson<Dictionary<string, List<EventItem>>>(dataService.EventsFile, new Dictionary<string, List<EventItem>>());
            AllTimeLogs = dataService.LoadFromJson<List<TimeLog>>(dataService.TimeLogsFile, new List<TimeLog>());
            isDarkMode = Settings != null && Settings.IsDarkMode;
            isColorVisionSupport = Settings != null && Settings.EnableColorVisionSupport;
            darkModeMenuItem.Checked = isDarkMode;
            UpdateCategoryFilterComboBox();
            UpdateTheme();
            UpdateAllViews();
            ApplyWindowSettings();
            if (quickSettingsPanel != null && quickSettingsPanel.Visible) SyncQuickSettingsToUI();
        }

        private void DarkModeMenuItem_Click(object sender, EventArgs e)
        {
            isDarkMode = darkModeMenuItem.Checked;
            if (Settings != null)
            {
                Settings.IsDarkMode = isDarkMode;
                dataService.SaveToJson(dataService.SettingsFile, Settings);
            }
            UpdateTheme();
        }

        private void UpdateTheme()
        {
            ThemeManager.ApplyTheme(this, isDarkMode);

            // ?? ウィンドウの「上の白枠」（タイトルバー）を完全にダークモードにする処理
            try {
                int useImmersiveDarkMode = isDarkMode ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }

            Color darkBg = Color.FromArgb(45, 45, 48);
            Color darkSurface = Color.FromArgb(30, 30, 30);
            Color darkText = Color.White;

            this.BackColor = isDarkMode ? darkBg : SystemColors.Control;

            if (isDarkMode)
            {
                mainMenu.Renderer = new DarkModeRenderer();
                toolStrip.Renderer = new DarkModeRenderer();
                statusBar.Renderer = new DarkModeRenderer();
                mainMenu.BackColor = darkBg; toolStrip.BackColor = darkBg; statusBar.BackColor = darkBg;
                mainMenu.ForeColor = darkText; toolStrip.ForeColor = darkText; statusBar.ForeColor = darkText;

                mainContainer.BackColor = darkBg;
                listTabPage.BackColor = darkSurface;
                kanbanTabPage.BackColor = darkSurface;
                calendarTabPage.BackColor = darkSurface;

                // カレンダーの各コンテナ背面を黒に統一して白浮きを完全に防ぐ
                if (calendarSplitContainer != null) calendarSplitContainer.BackColor = darkBg;
                if (calendarLeftSplitContainer != null) {
                    calendarLeftSplitContainer.Panel1.BackColor = darkSurface;
                    calendarLeftSplitContainer.Panel2.BackColor = darkSurface;
                }

                if (fileListView != null) {
                    fileListView.BackColor = darkSurface;
                    fileListView.ForeColor = darkText;
                }
                
                if (taskDataGridView != null) {
                    // 見出し（項目名）がクリックされても色が変わらないようにする
                    taskDataGridView.EnableHeadersVisualStyles = false;
                    taskDataGridView.ColumnHeadersDefaultCellStyle.BackColor = darkBg;
                    taskDataGridView.ColumnHeadersDefaultCellStyle.ForeColor = darkText;
                    taskDataGridView.ColumnHeadersDefaultCellStyle.SelectionBackColor = darkBg;
                    taskDataGridView.ColumnHeadersDefaultCellStyle.SelectionForeColor = darkText;
                }
            }
            else
            {
                mainMenu.Renderer = new ToolStripProfessionalRenderer();
                toolStrip.Renderer = new ToolStripProfessionalRenderer();
                statusBar.Renderer = new ToolStripProfessionalRenderer();
                mainMenu.BackColor = SystemColors.Control; toolStrip.BackColor = SystemColors.Control; statusBar.BackColor = SystemColors.Control;
                mainMenu.ForeColor = SystemColors.ControlText; toolStrip.ForeColor = SystemColors.ControlText; statusBar.ForeColor = SystemColors.ControlText;

                mainContainer.BackColor = SystemColors.Control;
                listTabPage.BackColor = SystemColors.Window;
                kanbanTabPage.BackColor = SystemColors.Window;
                calendarTabPage.BackColor = SystemColors.Window;

                if (calendarSplitContainer != null) calendarSplitContainer.BackColor = SystemColors.Control;
                if (calendarLeftSplitContainer != null) {
                    calendarLeftSplitContainer.Panel1.BackColor = SystemColors.Window;
                    calendarLeftSplitContainer.Panel2.BackColor = SystemColors.Window;
                }

                if (fileListView != null) {
                    fileListView.BackColor = SystemColors.Window;
                    fileListView.ForeColor = SystemColors.WindowText;
                }

                if (taskDataGridView != null) {
                    // 見出し（項目名）がクリックされても色が変わらないようにする
                    taskDataGridView.EnableHeadersVisualStyles = false;
                    taskDataGridView.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
                    taskDataGridView.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
                    taskDataGridView.ColumnHeadersDefaultCellStyle.SelectionBackColor = SystemColors.Control;
                    taskDataGridView.ColumnHeadersDefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
                }
            }

            if (calendarGrid != null) calendarGrid.BackColor = isDarkMode ? darkSurface : SystemColors.Window;
            if (btnPrevYear != null) btnPrevYear.ForeColor = isDarkMode ? darkText : SystemColors.ControlText;
            if (btnPrevMonth != null) btnPrevMonth.ForeColor = isDarkMode ? darkText : SystemColors.ControlText;
            if (btnNextYear != null) btnNextYear.ForeColor = isDarkMode ? darkText : SystemColors.ControlText;
            if (btnNextMonth != null) btnNextMonth.ForeColor = isDarkMode ? darkText : SystemColors.ControlText;
            if (lblMonthYear != null) lblMonthYear.ForeColor = isDarkMode ? darkText : SystemColors.ControlText;

            tabControl.Invalidate();
            if (fileListView != null) fileListView.Invalidate();
            UpdateAllViews();
        }

        private void UpdateStartupShortcut()
        {
            try
            {
                string startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolderPath, "TaskManager.lnk");

                if (Settings != null && Settings.RunAtStartup)
                {
                    Type t = Type.GetTypeFromProgID("WScript.Shell");
                    if (t != null)
                    {
                        dynamic shell = Activator.CreateInstance(t);
                        dynamic shortcut = shell.CreateShortcut(shortcutPath);
                        shortcut.TargetPath = Application.ExecutablePath;
                        shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                        shortcut.Description = "Task Manager Application";
                        shortcut.Save();
                    }
                }
                else
                {
                    if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("スタートアップ登録の同期に失敗しました:\n{0}", ex.Message), "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateCategoryFilterComboBox()
        {
            categoryFilterComboBox.Items.Clear();
            categoryFilterComboBox.Items.Add("(すべて)");
            if (Categories != null)
            {
                foreach (var cat in Categories.Keys.OrderBy(k => k))
                {
                    categoryFilterComboBox.Items.Add(cat);
                }
            }
            if (categoryFilterComboBox.Items.Contains(currentCategoryFilter))
            {
                categoryFilterComboBox.SelectedItem = currentCategoryFilter;
            }
            else
            {
                categoryFilterComboBox.SelectedIndex = 0;
                currentCategoryFilter = "(すべて)";
            }
        }

        private void UpdateAllViews()
        {
            UpdateKanbanColumnsVisibility();
            UpdateKanbanView();
            UpdateDataGridView();
            UpdateAssociatedFilesView();
            UpdateCalendarGrid(currentCalendarDate);
            UpdateDayInfoPanel(selectedCalendarDate);
            UpdateTimelineView(selectedCalendarDate);
            UpdateStatusBar();
            UpdateRecurringTaskNotification();
        }

        private void UpdateRecurringTaskNotification()
        {
            var rules = dataService.LoadFromJson<List<RecurringRule>>(dataService.RecurringRulesFile, new List<RecurringRule>());
            if (rules == null || notificationPanel == null) return;
            
            DateTime today = DateTime.Today;
            int pendingCount = rules.Count(r => {
                if (!r.IsActive) return false;
                DateTime d;
                if (!DateTime.TryParse(r.NextRunDate, out d)) return false;
                if (d.Date > today) return false;
                if (r.TriggerModes != null && r.TriggerModes.Contains("OnCompletion")) return false;
                return true;
            });

            if (pendingCount > 0) {
                notificationPanel.Visible = true;
                lblNotification.Text = string.Format("⚠️ 未処理の定期タスクが {0} 件あります（クリックして生成）", pendingCount);
                notificationPanel.BackColor = isDarkMode ? Color.FromArgb(100, 80, 0) : Color.LightYellow;
            } else {
                notificationPanel.Visible = false;
            }
        }

        private void UpdateStatusBar()
        {
            if (statusLabel != null)
            {
                int totalTasks = AllTasks.Count;
                int completedTasks = AllTasks.Count(t => t.進捗度 == "完了済み");
                int projectsCount = Projects.Count;
                statusLabel.Text = string.Format("プロジェクト: {0}件 | 総タスク: {1}件 (完了: {2}件)", projectsCount, totalTasks, completedTasks);
            }
        }

        private void InitializeDataGridViewColumns()
        {
            taskDataGridView.Columns.Clear();
            taskDataGridView.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

            taskDataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "プロジェクト／タスク", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 45, MinimumWidth = 250 });
            taskDataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "DueDate", HeaderText = "期日", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 100 });
            taskDataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Progress", HeaderText = "進捗", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 100 });
            taskDataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Priority", HeaderText = "優先度", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 60 });
            taskDataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "TrackedTime", HeaderText = "実績", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 80 });
            taskDataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "カテゴリ", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 15, MinimumWidth = 100 });
            taskDataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "SubCategory", HeaderText = "サブカテゴリ", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 15, MinimumWidth = 100 });
            taskDataGridView.Columns.Add(new DataGridViewTextBoxColumn { Name = "RecordAction", HeaderText = "記録操作", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 80 });

            taskDataGridView.Columns["DueDate"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            taskDataGridView.Columns["Progress"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            taskDataGridView.Columns["Priority"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            taskDataGridView.Columns["TrackedTime"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            taskDataGridView.Columns["Priority"].HeaderCell.Style.WrapMode = DataGridViewTriState.False;
        }

        private string FormatTrackedTime(double totalSeconds)
        {
            if (totalSeconds <= 0) return "";
            var ts = TimeSpan.FromSeconds(totalSeconds);
            return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
        }

        private class DataGridViewSelectionState
        {
            public string SelectedId { get; set; }
            public bool IsSelectedProject { get; set; }
            public int FirstDisplayedRowIndex { get; set; }
        }

        private DataGridViewSelectionState SaveDataGridViewSelection()
        {
            var state = new DataGridViewSelectionState
            {
                FirstDisplayedRowIndex = taskDataGridView.FirstDisplayedScrollingRowIndex
            };

            if (taskDataGridView.SelectedRows.Count > 0)
            {
                var selectedTag = taskDataGridView.SelectedRows[0].Tag;
                var t = selectedTag as TaskItem;
                var p = selectedTag as ProjectItem;
                if (t != null) { state.SelectedId = t.ID; state.IsSelectedProject = false; }
                else if (p != null) { state.SelectedId = p.ProjectID; state.IsSelectedProject = true; }
            }
            return state;
        }

        private void RestoreDataGridViewSelection(DataGridViewSelectionState state)
        {
            if (!string.IsNullOrEmpty(state.SelectedId))
            {
                taskDataGridView.ClearSelection();
                foreach (DataGridViewRow row in taskDataGridView.Rows)
                {
                    var tag = row.Tag;
                    var t = tag as TaskItem;
                    var p = tag as ProjectItem;
                    bool isMatch = (!state.IsSelectedProject && t != null && t.ID == state.SelectedId) ||
                                   (state.IsSelectedProject && p != null && p.ProjectID == state.SelectedId);
                    if (isMatch)
                    {
                        row.Selected = true;
                        try { taskDataGridView.CurrentCell = row.Cells[0]; } catch { }
                        break;
                    }
                }
            }

            if (state.FirstDisplayedRowIndex >= 0 && state.FirstDisplayedRowIndex < taskDataGridView.RowCount)
            {
                try { taskDataGridView.FirstDisplayedScrollingRowIndex = state.FirstDisplayedRowIndex; } catch { }
            }
        }

        private IEnumerable<TaskItem> GetFilteredTasksForGrid()
        {
            var tasksToDisplay = AllTasks.AsEnumerable();
            if (Settings != null && Settings.HideCompletedTasks)
            {
                tasksToDisplay = tasksToDisplay.Where(t => t.進捗度 != "完了済み");
            }
            if (currentCategoryFilter != "(すべて)")
            {
                tasksToDisplay = tasksToDisplay.Where(t => t.カテゴリ == currentCategoryFilter);
            }
            return tasksToDisplay;
        }

        private void PopulateDataGridViewGroupedByProject(IEnumerable<TaskItem> tasksToDisplay, int rowHeight)
        {
            var progressMapping = new Dictionary<string, int>
            {
                { "未実施", 0 }, { "保留", 0 }, { "実施中", 50 }, { "確認待ち", 75 }, { "完了済み", 100 }
            };
            var tasksGrouped = tasksToDisplay.GroupBy(t => t.ProjectID).ToList();

            foreach (var project in Projects.OrderBy(p => p.ProjectName))
            {
                var group = tasksGrouped.FirstOrDefault(g => g.Key == project.ProjectID);
                var projectTasks = group != null ? group.ToList() : new List<TaskItem>();

                AddProjectRowToGrid(project, projectTasks, progressMapping, rowHeight);

                if (projectExpansionStates.ContainsKey(project.ProjectID) && projectExpansionStates[project.ProjectID] && projectTasks.Count > 0)
                {
                    var sortedTasks = SortTasksForGrid(projectTasks);
                    foreach (var task in sortedTasks)
                    {
                        AddTaskRowToGrid(task, rowHeight);
                    }
                }
            }
        }

        private void AddProjectRowToGrid(ProjectItem project, List<TaskItem> projectTasks, Dictionary<string, int> progressMapping, int rowHeight)
        {
            int averageProgress = 0;
            double totalProjectSeconds = 0;
            if (projectTasks.Count > 0)
            {
                averageProgress = (int)projectTasks.Average(t => progressMapping.ContainsKey(t.進捗度 ?? "") ? progressMapping[t.進捗度] : 0);
                totalProjectSeconds = projectTasks.Sum(t => t.TrackedTimeSeconds);
                if (currentlyTrackingTaskID != null && projectTasks.Any(t => t.ID == currentlyTrackingTaskID) && currentTaskStartTime.HasValue)
                {
                    totalProjectSeconds += (DateTime.Now - currentTaskStartTime.Value).TotalSeconds;
                }
            }

            string pTrackedTimeStr = FormatTrackedTime(totalProjectSeconds);

            if (!projectExpansionStates.ContainsKey(project.ProjectID))
            {
                projectExpansionStates[project.ProjectID] = true;
            }

            bool isExpanded = projectExpansionStates[project.ProjectID];
            string prefix = isExpanded ? "[-] " : "[+] ";

            int rowIndex = taskDataGridView.Rows.Add(
                prefix + project.ProjectName,
                project.ProjectDueDate ?? "",
                averageProgress,
                "",
                pTrackedTimeStr,
                "",
                "",
                ""
            );

            var row = taskDataGridView.Rows[rowIndex];
            row.Height = rowHeight;
            row.Tag = project;
            row.DefaultCellStyle.Font = new Font("Meiryo UI", 9, FontStyle.Bold);
            row.DefaultCellStyle.BackColor = isDarkMode ? Color.FromArgb(58, 58, 62) : Color.LightGray;
        }

        private IEnumerable<TaskItem> SortTasksForGrid(List<TaskItem> projectTasks)
        {
            string defaultSort = Settings != null && Settings.DefaultSort != null ? Settings.DefaultSort : "DueDate";
            var sortedTasks = projectTasks.OrderBy(t => t.進捗度 == "完了済み");
            
            if (defaultSort == "Priority")
            {
                var priorityOrder = new Dictionary<string, int> { { "高", 1 }, { "中", 2 }, { "低", 3 } };
                sortedTasks = sortedTasks.ThenBy(t => priorityOrder.ContainsKey(t.優先度 ?? "") ? priorityOrder[t.優先度] : 4);
            }
            else if (defaultSort == "CreatedDate")
            {
                sortedTasks = sortedTasks.ThenBy(t => t.保存日付);
            }
            else
            {
                sortedTasks = sortedTasks.ThenBy(t => t.期日);
            }
            
            return sortedTasks;
        }

        private void AddTaskRowToGrid(TaskItem task, int rowHeight)
        {
            double totalTaskSec = task.TrackedTimeSeconds;
            if (task.ID == currentlyTrackingTaskID && currentTaskStartTime.HasValue)
            {
                totalTaskSec += (DateTime.Now - currentTaskStartTime.Value).TotalSeconds;
            }
            string tTrackedTimeStr = FormatTrackedTime(totalTaskSec);

            bool showIcons = Settings == null || Settings.ShowIcons;
            string priorityStr = task.優先度;
            if (showIcons) priorityStr = task.優先度 == "高" ? "🔺高" : (task.優先度 == "低" ? "⏬低" : task.優先度);

            int tIndex = taskDataGridView.Rows.Add(
                "    " + task.タスク,
                task.期日,
                task.進捗度,
                priorityStr,
                tTrackedTimeStr,
                task.カテゴリ,
                task.サブカテゴリ,
                "▶ 開始"
            );

            var tRow = taskDataGridView.Rows[tIndex];
            tRow.Height = rowHeight;
            tRow.Tag = task;

            if (task.進捗度 == "完了済み")
            {
                tRow.Cells["RecordAction"].Value = "";
                tRow.DefaultCellStyle.Font = new Font("Meiryo UI", 9, FontStyle.Strikeout);
                tRow.DefaultCellStyle.ForeColor = Color.Gray;
            }
            else
            {
                tRow.DefaultCellStyle.Font = new Font("Meiryo UI", 9, FontStyle.Regular);
                
                if (task.優先度 == "高")
                {
                    tRow.DefaultCellStyle.BackColor = isColorVisionSupport ? (isDarkMode ? Color.FromArgb(90, 45, 0) : Color.FromArgb(255, 230, 210)) : (isDarkMode ? Color.FromArgb(60, 60, 0) : Color.LightYellow);
                }

                DateTime dueDate = DateTime.MinValue;
                if (!string.IsNullOrWhiteSpace(task.期日) && DateTime.TryParse(task.期日, out dueDate))
                {
                    if (dueDate.Date < DateTime.Today)
                    {
                        tRow.DefaultCellStyle.ForeColor = ThemeManager.GetOverdueColor(isDarkMode, isColorVisionSupport);
                        if (showIcons) tRow.Cells[1].Value = "⚠️ " + task.期日;
                    }
                }
            }
            
            if (task.ID == currentlyTrackingTaskID)
            {
                tRow.Cells["RecordAction"].Value = "■ 停止";
                tRow.DefaultCellStyle.BackColor = isDarkMode ? Color.DarkGreen : Color.LightGreen;
            }
        }

        private void UpdateDataGridView()
        {
            if (taskDataGridView == null) return;

            var selectionState = SaveDataGridViewSelection();

            taskDataGridView.SuspendLayout();
            try
            {
                int rowHeight = 28;
                if (Settings != null)
                {
                    if (Settings.ListDensity == "Compact") rowHeight = 22;
                    else if (Settings.ListDensity == "Relaxed") rowHeight = 36;
                }
                taskDataGridView.RowTemplate.Height = rowHeight;

                taskDataGridView.Rows.Clear();

                var tasksToDisplay = GetFilteredTasksForGrid();

                if (groupByProject)
                {
                    PopulateDataGridViewGroupedByProject(tasksToDisplay, rowHeight);
                }
            }
            finally
            {
                taskDataGridView.ResumeLayout();
            }

            RestoreDataGridViewSelection(selectionState);
        }

        private void TaskDataGridView_SelectionChanged(object sender, EventArgs e)
        {
            UpdateAssociatedFilesView();
        }

        private void UpdateKanbanColumnsVisibility()
        {
            if (kanbanLayout == null || kanbanLayout.IsDisposed) return;

            bool showCompleted = Settings != null ? Settings.ShowKanbanDone : true;
            int completedIndex = Array.IndexOf(TaskStatuses, "完了済み");
            
            if (completedIndex < 0) return;

            kanbanLayout.SuspendLayout();
            try
            {
                int visibleCount = showCompleted ? TaskStatuses.Length : TaskStatuses.Length - 1;
                if (visibleCount < 1) visibleCount = 1;
                float percentWidth = 100f / visibleCount;

                for (int i = 0; i < kanbanLayout.ColumnStyles.Count; i++)
                {
                    if (i == completedIndex)
                    {
                        kanbanLayout.ColumnStyles[i].SizeType = showCompleted ? SizeType.Percent : SizeType.Absolute;
                        kanbanLayout.ColumnStyles[i].Width = showCompleted ? percentWidth : 0;
                    }
                    else
                    {
                        kanbanLayout.ColumnStyles[i].SizeType = SizeType.Percent;
                        kanbanLayout.ColumnStyles[i].Width = percentWidth;
                    }
                }
            }
            finally
            {
                kanbanLayout.ResumeLayout(true);
            }
        }

        private void UpdateKanbanView()
        {
            foreach (var listBox in kanbanLists.Values)
            {
                listBox.Items.Clear();
            }

            var tasksToDisplay = AllTasks.AsEnumerable();
            if (Settings != null && Settings.HideCompletedTasks)
            {
                tasksToDisplay = tasksToDisplay.Where(t => t.進捗度 != "完了済み");
            }
            if (currentCategoryFilter != "(すべて)")
            {
                tasksToDisplay = tasksToDisplay.Where(t => t.カテゴリ == currentCategoryFilter);
            }

            foreach (var task in tasksToDisplay)
            {
                if (!string.IsNullOrEmpty(task.進捗度) && kanbanLists.ContainsKey(task.進捗度))
                {
                    kanbanLists[task.進捗度].Items.Add(task);
                }
            }
        }

        // --- Undo (Ctrl+Z) キーボードショートカットの処理 ---
        private void FormMain_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z)
            {
                if (undoStack.Count > 0)
                {
                    var cmd = undoStack.Pop();
                    cmd.Undo();
                    
                    // フィードバック表示
                    notificationPanel.Visible = true;
                    lblNotification.Text = "操作を元に戻しました (Undo)";
                    notificationPanel.BackColor = isDarkMode ? Color.DarkGreen : Color.LightGreen;
                    
                    // 3秒後に元の通知パネルの状態に戻す
                    System.Threading.Tasks.Task.Delay(3000).ContinueWith(t => 
                    {
                        if (!this.IsDisposed) this.Invoke((MethodInvoker)(() => UpdateRecurringTaskNotification()));
                    });
                }
            }
        }

        // --- タスク削除の共通化 (Undo対応) ---
        private void DeleteTasksWithUndo(List<TaskItem> tasks)
        {
            if (tasks == null || tasks.Count == 0) return;

            // 元に戻す処理を登録
            undoStack.Push(new TaskBulkDeleteCommand(new List<TaskItem>(tasks), (restoredTasks) =>
            {
                AllTasks.AddRange(restoredTasks);
                dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                UpdateAllViews();
            }));

            foreach (var task in tasks) AllTasks.Remove(task);
            dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
            UpdateAllViews();
        }

        private void UpdateTaskStatus(TaskItem task, string newStatus)
        {
            string oldStatus = task.進捗度;
            string oldCompletionDate = task.完了日;
            if (oldStatus == newStatus) return;

            // --- ステータス変更のUndo履歴を登録 ---
            undoStack.Push(new TaskStatusChangeCommand(task, oldStatus, oldCompletionDate, (t, prevStatus, prevCompDate) =>
            {
                t.進捗度 = prevStatus;
                t.完了日 = prevCompDate;
                dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                UpdateAllViews();
            }));

            // 1. ステータス変更履歴の記録
            var statusLog = new StatusLog
            {
                TaskID = task.ID,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Timestamp = DateTime.Now.ToString("o")
            };
            var currentLogs = dataService.LoadFromJson<List<StatusLog>>(dataService.StatusLogsFile, new List<StatusLog>());
            currentLogs.Add(statusLog);
            dataService.SaveToJson(dataService.StatusLogsFile, currentLogs);

            // 2. 完了日の設定・リセット
            if (newStatus == "完了済み")
            {
                if (string.IsNullOrEmpty(task.完了日)) task.完了日 = DateTime.Now.ToString("yyyy-MM-dd");

                if (Settings != null && Settings.EnableSoundEffects)
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }
            }
            else
            {
                task.完了日 = "";
            }
            task.進捗度 = newStatus;
            
            // --- 完了トリガーの定期タスク生成 ---
            if (newStatus == "完了済み" && oldStatus != "完了済み")
            {
                var rules = dataService.LoadFromJson<List<RecurringRule>>(dataService.RecurringRulesFile, new List<RecurringRule>());
                var triggerRule = rules.FirstOrDefault(r => r.CurrentInstanceID == task.ID);
                if (triggerRule != null && triggerRule.IsActive && triggerRule.TriggerModes != null && triggerRule.TriggerModes.Contains("OnCompletion"))
                {
                    DateTime td = DateTime.MinValue;
                    var baseDate = triggerRule.CalculationBase == "Completion" ? DateTime.Today : (DateTime.TryParse(triggerRule.TheoreticalDate, out td) ? td : DateTime.Today);
                    var nextOcc = GetNextRecurringDate(baseDate, triggerRule);
                    if (triggerRule.CalculationBase != "Completion" && nextOcc.ActualDate < DateTime.Today) while (nextOcc.ActualDate < DateTime.Today) nextOcc = GetNextRecurringDate(nextOcc.TheoreticalDate, triggerRule);
                    
                    var genResult = InvokeItemGeneration(triggerRule, nextOcc.TheoreticalDate);
                    if (genResult != null) { triggerRule.NextRunDate = genResult.ActualDate.ToString("yyyy-MM-dd"); triggerRule.TheoreticalDate = genResult.TheoreticalDate.ToString("yyyy-MM-dd"); }
                    dataService.SaveToJson(dataService.RecurringRulesFile, rules);
                }
            }

            // 3. 実績時間の再計算
            double totalSeconds = 0;
            var taskLogs = AllTimeLogs.Where(l => l.TaskID == task.ID && !string.IsNullOrEmpty(l.EndTime));
            foreach (var log in taskLogs)
            {
                DateTime start = DateTime.MinValue, end = DateTime.MinValue;
                if (DateTime.TryParse(log.StartTime, out start) && DateTime.TryParse(log.EndTime, out end))
                {
                    totalSeconds += (end - start).TotalSeconds;
                }
            }
            task.TrackedTimeSeconds = totalSeconds;

            dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);

            // --- 即時アーカイブ機能 ---
            if (newStatus == "完了済み" && oldStatus != "完了済み" && Settings != null)
            {
                if (Settings.ArchiveTasksOnCompletion)
                {
                    MoveTaskToArchive(new List<TaskItem> { task });
                    return; // アーカイブされた場合はここで終了
                }

                if (Settings.ArchiveTasksOnProjectCompletion && !string.IsNullOrEmpty(task.ProjectID))
                {
                    var tasksInProject = AllTasks.Where(t => t.ProjectID == task.ProjectID).ToList();
                    bool allComplete = true;
                    foreach (var t in tasksInProject)
                    {
                        if (t.進捗度 != "完了済み") { allComplete = false; break; }
                    }
                    if (allComplete && tasksInProject.Count > 0)
                    {
                        MoveTaskToArchive(tasksInProject);
                    }
                }
            }

            InvokeProjectAutoArchiving();
        }

        // ==========================================================
        // リスト表示 (DataGridView) イベントハンドラ
        // ==========================================================
        
        private void TaskDataGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var tag = taskDataGridView.Rows[e.RowIndex].Tag;

            var project = tag as ProjectItem;
            var task = tag as TaskItem;
            if (project != null)
            {
                var form = new FormProjectInput(project);
                ThemeManager.ApplyTheme(form, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    UpdateAllViews();
                }
            }
            else if (task != null)
            {
                string action = Settings != null && Settings.DoubleClickAction != null ? Settings.DoubleClickAction : "Edit";
                if (action == "ToggleStatus")
                {
                    string newStatus = task.進捗度 == "完了済み" ? "未実施" : "完了済み";
                    UpdateTaskStatus(task, newStatus);
                    UpdateAllViews();
                }
                else
                {
                    var form = new FormTaskInput(task, null, Projects, Categories);
                    ThemeManager.ApplyTheme(form, isDarkMode);
                    if (form.ShowDialog(this) == DialogResult.OK)
                    {
                        dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                        UpdateAllViews();
                    }
                }
            }
        }

        private void TaskDataGridView_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            // 右クリック時に確実に行を選択させる
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                if (!taskDataGridView.Rows[e.RowIndex].Selected)
                {
                    taskDataGridView.ClearSelection();
                    taskDataGridView.Rows[e.RowIndex].Selected = true;
                    try { taskDataGridView.CurrentCell = taskDataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex]; } catch { }
                }
            }
        }

        private void TaskDataGridView_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && dragStartPoint != Point.Empty)
            {
                Size dragSize = SystemInformation.DragSize;
                if (Math.Abs(e.X - dragStartPoint.X) > dragSize.Width || Math.Abs(e.Y - dragStartPoint.Y) > dragSize.Height)
                {
                    if (taskDataGridView.SelectedRows.Count > 0)
                    {
                        var item = taskDataGridView.SelectedRows[0].Tag;
                        if (item != null) {
                            string type = item is ProjectItem ? "Project" : (item is TaskItem ? "Task" : "");
                            if (type != "") taskDataGridView.DoDragDrop(new object[] { type, item, DateTime.Today }, DragDropEffects.Move);
                        }
                    }
                    dragStartPoint = Point.Empty;
                }
            }
        }

        private void DgvContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            bool isMultiSelect = taskDataGridView.SelectedRows.Count > 1;
            var tag = taskDataGridView.SelectedRows[0].Tag;
            bool isProject = tag is ProjectItem;

            dgvEditMenuItem.Visible = !isProject && !isMultiSelect;
            dgvDeleteMenuItem.Visible = !isProject && !isMultiSelect;
            dgvChangeStatusMenuItem.Visible = !isProject && !isMultiSelect;
            
            dgvProjectPropertiesMenuItem.Visible = isProject && !isMultiSelect;
            dgvProjectDeleteMenuItem.Visible = isProject && !isMultiSelect;
            dgvArchiveMenuItem.Visible = !isMultiSelect; // アーカイブは現状単一選択のみ

            dgvBulkCompleteMenuItem.Visible = isMultiSelect;
            dgvBulkDeleteMenuItem.Visible = isMultiSelect;

            var task = tag as TaskItem;
            if (!isProject && !isMultiSelect && task != null)
            {
                foreach (ToolStripItem subItem in dgvChangeStatusMenuItem.DropDownItems)
                {
                    subItem.Visible = (subItem.Tag as string) != task.進捗度;
                }
            }
        }

        private void DgvEditMenuItem_Click(object sender, EventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0) return;
            var task = taskDataGridView.SelectedRows[0].Tag as TaskItem;
            if (task != null)
            {
                var form = new FormTaskInput(task, null, Projects, Categories);
                ThemeManager.ApplyTheme(form, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                    UpdateAllViews();
                }
            }
        }

        private void TaskDataGridView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && taskDataGridView.SelectedRows.Count > 0)
            {
                DgvDeleteMenuItem_Click(sender, EventArgs.Empty);
            }
        }

        private void DgvDeleteMenuItem_Click(object sender, EventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0) return;
            var task = taskDataGridView.SelectedRows[0].Tag as TaskItem;
            if (task != null)
            {
                if (MessageBox.Show(string.Format("タスク '{0}' を削除しますか？", task.タスク), "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    DeleteTasksWithUndo(new List<TaskItem> { task });
                }
            }
        }

        private void DgvChangeStatusMenuItem_Click(object sender, EventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0) return;
            var task = taskDataGridView.SelectedRows[0].Tag as TaskItem;
            var menuItem = sender as ToolStripMenuItem;
            if (task != null && menuItem != null)
            {
                UpdateTaskStatus(task, menuItem.Tag as string);
                UpdateAllViews();
            }
        }

        private void DgvProjectPropertiesMenuItem_Click(object sender, EventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0) return;
            var project = taskDataGridView.SelectedRows[0].Tag as ProjectItem;
            if (project != null)
            {
                var form = new FormProjectInput(project);
                ThemeManager.ApplyTheme(form, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    UpdateAllViews();
                }
            }
        }

        private void DgvProjectDeleteMenuItem_Click(object sender, EventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0) return;
            var project = taskDataGridView.SelectedRows[0].Tag as ProjectItem;
            if (project != null)
            {
                if (MessageBox.Show(string.Format("プロジェクト '{0}' を削除します。\n関連するすべてのタスクも削除されます。\n\nよろしいですか？", project.ProjectName), "プロジェクトの削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    Projects.Remove(project);
                    AllTasks.RemoveAll(t => t.ProjectID == project.ProjectID);
                    dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                    UpdateAllViews();
                }
            }
        }

        private void DgvArchiveMenuItem_Click(object sender, EventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0) return;
            var tag = taskDataGridView.SelectedRows[0].Tag;

            var project = tag as ProjectItem;
            var task = tag as TaskItem;
            if (project != null)
            {
                if (MessageBox.Show(string.Format("プロジェクト '{0}' をアーカイブしますか？\n関連するすべてのタスクも一緒にアーカイブされます。", project.ProjectName), "アーカイブの確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    MoveProjectToArchive(project);
                    UpdateAllViews();
                }
            }
            else if (task != null)
            {
                if (MessageBox.Show(string.Format("タスク '{0}' をアーカイブしますか？", task.タスク), "アーカイブの確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    MoveTaskToArchive(new List<TaskItem> { task });
                    UpdateAllViews();
                }
            }
        }

        private void DgvBulkCompleteMenuItem_Click(object sender, EventArgs e)
        {
            var selectedTasks = new List<TaskItem>();
            foreach (DataGridViewRow row in taskDataGridView.SelectedRows)
            {
                var task = row.Tag as TaskItem;
                if (task != null && task.進捗度 != "完了済み")
                {
                    selectedTasks.Add(task);
                }
            }

            if (selectedTasks.Count > 0)
            {
                foreach (var task in selectedTasks) UpdateTaskStatus(task, "完了済み");
                UpdateAllViews();
            }
        }

        private void DgvBulkDeleteMenuItem_Click(object sender, EventArgs e)
        {
            var selectedTasks = new List<TaskItem>();
            foreach (DataGridViewRow row in taskDataGridView.SelectedRows)
            {
                var task = row.Tag as TaskItem;
                if (task != null) selectedTasks.Add(task);
            }

            if (selectedTasks.Count > 0 && MessageBox.Show(string.Format("選択した {0} 件のタスクを削除しますか？", selectedTasks.Count), "一括削除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                DeleteTasksWithUndo(selectedTasks);
            }
        }

        private void AddNewTaskAction(object sender, EventArgs e)
        {
            string projectIDForNew = null;
            if (taskDataGridView.SelectedRows.Count > 0)
            {
                var selectedTag = taskDataGridView.SelectedRows[0].Tag;
                var p = selectedTag as ProjectItem;
                var t = selectedTag as TaskItem;
                if (p != null) projectIDForNew = p.ProjectID;
                else if (t != null) projectIDForNew = t.ProjectID;
            }

            var form = new FormTaskInput(null, projectIDForNew, Projects, Categories);
            ThemeManager.ApplyTheme(form, isDarkMode);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                // 新規プロジェクトが入力された場合、新しいプロジェクトとして登録
                if (string.IsNullOrEmpty(form.ResultTask.ProjectID) && !string.IsNullOrEmpty(form.ResultTask.ProjectName))
                {
                    var newProj = new ProjectItem { ProjectID = Guid.NewGuid().ToString(), ProjectName = form.ResultTask.ProjectName, ProjectColor = "#D3D3D3", AutoArchiveTasks = true };
                    Projects.Add(newProj);
                    dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    form.ResultTask.ProjectID = newProj.ProjectID;
                }
                AllTasks.Add(form.ResultTask);
                dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                UpdateAllViews();
            }
        }

        private void MoveTaskToArchive(List<TaskItem> tasksToArchive)
        {
            if (tasksToArchive.Count == 0) return;
            string archiveFile = dataService.TasksFile.Replace("tasks.csv", "archived_tasks.csv");
            var archivedTasks = dataService.LoadTasksFromCsv(archiveFile);
            string archiveDate = DateTime.Now.ToString("yyyy-MM-dd");
            
            foreach (var task in tasksToArchive)
            {
                var proj = Projects.FirstOrDefault(p => p.ProjectID == task.ProjectID);
                task.ProjectName = proj != null ? proj.ProjectName : "";
                task.ArchivedDate = archiveDate;
                archivedTasks.Add(task);
                AllTasks.Remove(task);
            }
            dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
            dataService.SaveTasksToCsv(archiveFile, archivedTasks);
        }

        private void MoveProjectToArchive(ProjectItem project)
        {
            var tasksInProject = AllTasks.Where(t => t.ProjectID == project.ProjectID).ToList();
            if (tasksInProject.Count > 0) MoveTaskToArchive(tasksInProject);
            
            string archiveFile = dataService.ProjectsFile.Replace("projects.json", "archived_projects.json");
            var archivedProjects = dataService.LoadFromJson<List<ProjectItem>>(archiveFile, new List<ProjectItem>());
            project.ArchivedDate = DateTime.Now.ToString("yyyy-MM-dd");
            archivedProjects.Add(project);
            Projects.Remove(project);
            
            dataService.SaveToJson(dataService.ProjectsFile, Projects);
            dataService.SaveToJson(archiveFile, archivedProjects);
        }

        private void InvokeAutoArchiving()
        {
            int autoArchiveDays = Settings != null ? Settings.AutoArchiveDays : 30;
            if (autoArchiveDays <= 0) return;

            DateTime today = DateTime.Today;
            var tasksToArchive = new List<TaskItem>();

            foreach (var task in AllTasks)
            {
                if (task.進捗度 == "完了済み" && !string.IsNullOrEmpty(task.完了日))
                {
                    bool canArchive = true;
                    if (!string.IsNullOrEmpty(task.ProjectID))
                    {
                        var proj = Projects.FirstOrDefault(p => p.ProjectID == task.ProjectID);
                        if (proj != null && !proj.AutoArchiveTasks) canArchive = false;
                    }

                    if (canArchive)
                    {
                        DateTime compDate = DateTime.MinValue;
                        if (DateTime.TryParse(task.完了日, out compDate) && (today - compDate).TotalDays >= autoArchiveDays)
                        {
                            tasksToArchive.Add(task);
                        }
                    }
                }
            }
            if (tasksToArchive.Count > 0) MoveTaskToArchive(tasksToArchive);
        }

        private void InvokeProjectAutoArchiving()
        {
            int autoArchiveDays = Settings != null ? Settings.AutoArchiveProjectsDays : 60;
            if (autoArchiveDays <= 0) return;

            DateTime today = DateTime.Today;
            var projectsToArchive = new List<ProjectItem>();

            foreach (var project in Projects.ToList())
            {
                var tasksInProject = AllTasks.Where(t => t.ProjectID == project.ProjectID).ToList();
                if (tasksInProject.Count == 0) continue;
                
                var incompleteTasks = tasksInProject.Where(t => t.進捗度 != "完了済み").ToList();
                if (incompleteTasks.Count == 0)
                {
                    DateTime latestComp = tasksInProject.Select(t => { DateTime d; return DateTime.TryParse(t.完了日, out d) ? d : DateTime.MinValue; }).Max();
                    if (latestComp != DateTime.MinValue && (today - latestComp).TotalDays >= autoArchiveDays) projectsToArchive.Add(project);
                }
            }
            foreach (var proj in projectsToArchive) MoveProjectToArchive(proj);
        }

        private void UpdateCalendarGrid(DateTime dateInMonth)
        {
            lblMonthYear.Text = currentCalendarDate.ToString("yyyy年 MM月");
            calendarGrid.SuspendLayout();
            foreach (Control control in calendarGrid.Controls) control.Dispose();
            calendarGrid.Controls.Clear();

            int weekStart = Settings != null ? Settings.CalendarWeekStart : 0;
            bool colorWeekend = Settings != null ? Settings.ColorWeekend : true;
            var holidays = GetHolidays();

            string[] daysOfWeek = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
            for (int i = 0; i < 7; i++)
            {
                int dayIndex = (weekStart + i) % 7;
                var lbl = new Label { Text = daysOfWeek[dayIndex], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Meiryo UI", 9) };
                
                if (dayIndex == 0) lbl.ForeColor = ThemeManager.GetSundayColor(isDarkMode, isColorVisionSupport);
                else if (dayIndex == 6) lbl.ForeColor = ThemeManager.GetSaturdayColor(isDarkMode, isColorVisionSupport);
                else lbl.ForeColor = isDarkMode ? Color.White : SystemColors.ControlText;

                calendarGrid.Controls.Add(lbl, i, 0);
            }

            DateTime firstDayOfMonth = new DateTime(dateInMonth.Year, dateInMonth.Month, 1);
            int startOffset = (int)firstDayOfMonth.DayOfWeek - weekStart;
            if (startOffset < 0) startOffset += 7;
            DateTime currentDate = firstDayOfMonth.AddDays(-startOffset);

            for (int row = 1; row <= 6; row++)
            {
                for (int col = 0; col < 7; col++)
                {
                    var itemsPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Tag = currentDate };
                    bool isHoliday = holidays.ContainsKey(currentDate.ToString("yyyy-MM-dd"));
                    Color backColor = isDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Window;
                    if (currentDate.Month != dateInMonth.Month) backColor = isDarkMode ? Color.FromArgb(30, 30, 30) : SystemColors.Control;
                    else if (currentDate.Date == DateTime.Today) backColor = isDarkMode ? Color.FromArgb(60, 60, 30) : Color.FromArgb(255, 255, 224);
                    else if (colorWeekend || isHoliday)
                    {
                        if (isHoliday || currentDate.DayOfWeek == DayOfWeek.Sunday) backColor = isDarkMode ? Color.FromArgb(65, 40, 40) : Color.FromArgb(255, 240, 240);
                        else if (currentDate.DayOfWeek == DayOfWeek.Saturday) backColor = isDarkMode ? Color.FromArgb(40, 50, 65) : Color.FromArgb(230, 245, 255);
                    }
                    itemsPanel.BackColor = backColor;
                    itemsPanel.MouseClick += (s, e) => {
                        if (e.Button == MouseButtons.Left) {
                            selectedCalendarDate = (DateTime)((Control)s).Tag;
                            UpdateDayInfoPanel(selectedCalendarDate);
                            UpdateTimelineView(selectedCalendarDate);
                            calendarGrid.Refresh();
                        }
                    };
                    
                    // --- ドラッグ＆ドロップ対応 ---
                    itemsPanel.AllowDrop = true;
                    itemsPanel.DragEnter += CalendarCell_DragEnter;
                    itemsPanel.DragDrop += CalendarCell_DragDrop;

                    itemsPanel.Paint += CalendarItemPanel_Paint;
                    calendarGrid.Controls.Add(itemsPanel, col, row);
                    currentDate = currentDate.AddDays(1);
                }
            }
            calendarGrid.ResumeLayout(true);
        }

        private void CalendarCell_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(object[]))) {
                var data = e.Data.GetData(typeof(object[])) as object[];
                if (data != null && data.Length >= 2) { e.Effect = DragDropEffects.Move; return; }
            }
            e.Effect = DragDropEffects.None;
        }

        private void CalendarCell_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(object[]))) {
                var data = e.Data.GetData(typeof(object[])) as object[];
                if (data != null && data.Length >= 2) {
                    var panel = sender as Panel;
                    if (panel == null || !(panel.Tag is DateTime)) return;
                    DateTime dropDate = (DateTime)panel.Tag;
                    string type = data[0] as string;
                    object item = data[1];

                    if (type == "Task" && item is TaskItem) {
                        var task = (TaskItem)item; DateTime originalTime = dropDate;
                        DateTime d;
                        if (!string.IsNullOrEmpty(task.期日) && DateTime.TryParse(task.期日, out d)) originalTime = dropDate.Date.Add(d.TimeOfDay);
                        task.期日 = originalTime.ToString("yyyy-MM-dd HH:mm"); dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                    } else if (type == "Project" && item is ProjectItem) {
                        var proj = (ProjectItem)item; DateTime originalTime = dropDate;
                        DateTime d;
                        if (!string.IsNullOrEmpty(proj.ProjectDueDate) && DateTime.TryParse(proj.ProjectDueDate, out d)) originalTime = dropDate.Date.Add(d.TimeOfDay);
                        proj.ProjectDueDate = originalTime.ToString("yyyy-MM-dd HH:mm"); dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    } else if (type == "Event" && item is EventItem) {
                        var evt = (EventItem)item;
                        DateTime oldStart;
                        if (DateTime.TryParse(evt.StartTime, out oldStart)) {
                            TimeSpan duration = TimeSpan.FromHours(1); 
                            DateTime oldEnd;
                            if (DateTime.TryParse(evt.EndTime, out oldEnd) && oldEnd > oldStart) duration = oldEnd - oldStart;
                            string oldDateStr = oldStart.ToString("yyyy-MM-dd"); DateTime newStart = dropDate.Date.Add(oldStart.TimeOfDay); DateTime newEnd = newStart.Add(duration); string newDateStr = newStart.ToString("yyyy-MM-dd");
                            if (oldDateStr != newDateStr) {
                                if (AllEvents.ContainsKey(oldDateStr)) AllEvents[oldDateStr].Remove(evt);
                                if (!AllEvents.ContainsKey(newDateStr)) AllEvents[newDateStr] = new List<EventItem>();
                                AllEvents[newDateStr].Add(evt);
                            }
                            evt.StartTime = newStart.ToString("o"); evt.EndTime = newEnd.ToString("o"); dataService.SaveToJson(dataService.EventsFile, AllEvents);
                        }
                    }
                    UpdateAllViews();
                }
            }
        }

        private void CalendarItemPanel_Paint(object sender, PaintEventArgs e)
        {
            var panel = sender as Panel;
            DateTime date = (DateTime)panel.Tag;
            string dateStr = date.ToString("yyyy-MM-dd");
            var holidays = GetHolidays();
            bool isHoliday = holidays.ContainsKey(dateStr);
            var g = e.Graphics;
            var rect = panel.ClientRectangle;

            bool isToday = date.Date == DateTime.Today;
            bool isSelected = date.Date == selectedCalendarDate.Date;

            if (isSelected) {
                using (var pen = new Pen(SystemColors.Highlight, 3)) g.DrawRectangle(pen, 1, 1, rect.Width - 3, rect.Height - 3);
            } else if (isToday) {
                Color todayColor = isColorVisionSupport ? Color.FromArgb(213, 94, 0) : (isDarkMode ? Color.Goldenrod : Color.OrangeRed);
                using (var pen = new Pen(todayColor, 2)) g.DrawRectangle(pen, 1, 1, rect.Width - 3, rect.Height - 3);
            } else {
                using (var pen = new Pen(isDarkMode ? Color.FromArgb(70, 70, 70) : Color.LightGray)) {
                    g.DrawLine(pen, 0, rect.Height - 1, rect.Width, rect.Height - 1);
                    g.DrawLine(pen, rect.Width - 1, 0, rect.Width - 1, rect.Height);
                }
            }

            int currentY = 20; int lineHeight = 14;
            using (var sfItem = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var itemFont = new Font("Meiryo UI", 8.25f))
            using (var eventBrush = new SolidBrush(ThemeManager.GetEventColor(isDarkMode, isColorVisionSupport)))
            using (var projBrush = new SolidBrush(ThemeManager.GetProjectColor(isDarkMode, isColorVisionSupport)))
            using (var taskBrush = new SolidBrush(ThemeManager.GetTaskColor(isDarkMode, isColorVisionSupport)))
            {
                if (AllEvents.ContainsKey(dateStr)) {
                    foreach (var evt in AllEvents[dateStr]) {
                        if (currentY + lineHeight > rect.Height) break;
                        string dispText = (!evt.IsAllDay && !string.IsNullOrEmpty(evt.StartTime)) ? string.Format("{0:HH:mm} {1}", DateTime.Parse(evt.StartTime), evt.Title) : evt.Title;
                        g.DrawString(dispText, itemFont, eventBrush, new RectangleF(5, currentY, rect.Width - 7, lineHeight), sfItem);
                        currentY += lineHeight;
                    }
                }

                var projsDue = Projects.Where(p => { DateTime dt = DateTime.MinValue; return !string.IsNullOrEmpty(p.ProjectDueDate) && DateTime.TryParse(p.ProjectDueDate, out dt) && dt.Date == date.Date; });
                foreach (var proj in projsDue) {
                    if (currentY + lineHeight > rect.Height) break;
                    g.DrawString(string.Format("[P] {0}", proj.ProjectName), itemFont, projBrush, new RectangleF(5, currentY, rect.Width - 7, lineHeight), sfItem);
                    currentY += lineHeight;
                }

                var tasksDue = AllTasks.Where(t => { DateTime dt = DateTime.MinValue; return (Settings == null || !Settings.HideCompletedTasks || t.進捗度 != "完了済み") && !string.IsNullOrEmpty(t.期日) && DateTime.TryParse(t.期日, out dt) && dt.Date == date.Date; });
                foreach (var task in tasksDue) {
                    if (currentY + lineHeight > rect.Height) break;
                    bool isOverdue = false;
                    DateTime tDue;
                    if (DateTime.TryParse(task.期日, out tDue) && tDue.Date < DateTime.Today) isOverdue = true;
                    bool showIcons = Settings == null || Settings.ShowIcons;
                    string prefix = (isOverdue && (isColorVisionSupport || showIcons)) ? "[⚠️T] " : "[T] ";
                    g.DrawString(string.Format("{0}{1}", prefix, task.タスク), itemFont, taskBrush, new RectangleF(5, currentY, rect.Width - 7, lineHeight), sfItem);
                    currentY += lineHeight;
                }
            }

            Color dayTextColor = date.Month == currentCalendarDate.Month ? (isDarkMode ? Color.White : Color.Black) : Color.DimGray;
            if (date.Month == currentCalendarDate.Month)
            {
                if (isHoliday || date.DayOfWeek == DayOfWeek.Sunday) dayTextColor = ThemeManager.GetSundayColor(isDarkMode, isColorVisionSupport);
                else if (date.DayOfWeek == DayOfWeek.Saturday) dayTextColor = ThemeManager.GetSaturdayColor(isDarkMode, isColorVisionSupport);
            }

            using (var dayFont = new Font("Meiryo UI", 9.5f, FontStyle.Bold))
            using (var dayBrush = new SolidBrush(dayTextColor))
            {
                string dayStr = date.Day.ToString();
                SizeF daySize = g.MeasureString(dayStr, dayFont);
                g.DrawString(dayStr, dayFont, dayBrush, rect.Width - daySize.Width - 4, 2);

                if (isHoliday && date.Month == currentCalendarDate.Month)
                {
                    using (var holFont = new Font("Meiryo UI", 7.5f))
                    {
                        SizeF holSize = g.MeasureString(holidays[dateStr], holFont);
                        g.DrawString(holidays[dateStr], holFont, dayBrush, rect.Width - daySize.Width - holSize.Width - 6, 4);
                    }
                }
            }
        }

        private Panel CreateDayInfoCard(string itemType, string title, string details, Color indicatorColor, object itemData, DateTime date)
        {
            var card = new Panel { Size = new Size(300, string.IsNullOrEmpty(details) ? 40 : 55), Margin = new Padding(5, 5, 5, 0), BackColor = isDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Window, BorderStyle = BorderStyle.FixedSingle };
            var colorBar = new Panel { Dock = DockStyle.Left, Width = 5, BackColor = indicatorColor };
            card.Controls.Add(colorBar);
            
            string displayType = itemType;
            if (itemType == "Event") displayType = "イベント";
            else if (itemType == "Project") displayType = "プロジェクト期日";
            else if (itemType == "Task") displayType = "タスク期日";

            var lblType = new Label { Text = string.Format("[{0}]", displayType), ForeColor = indicatorColor, Location = new Point(10, 5), AutoSize = true, Font = new Font("Meiryo UI", 8, FontStyle.Bold) };
            var lblTitle = new Label { Text = title, ForeColor = isDarkMode ? Color.White : SystemColors.ControlText, Location = new Point(15, 22), AutoSize = true, Font = new Font("Meiryo UI", 9) };
            card.Controls.Add(lblType); card.Controls.Add(lblTitle);
            
            if (!string.IsNullOrEmpty(details)) {
                var lblDetails = new Label { Text = details, ForeColor = isDarkMode ? Color.Silver : Color.DimGray, Location = new Point(20, 38), AutoSize = true, Font = new Font("Meiryo UI", 8, FontStyle.Italic) };
                card.Controls.Add(lblDetails);
            }
            
            card.Tag = new object[] { itemType, itemData, date };

            EventHandler doubleClickAction = (s, ev) => {
                var c = s as Control;
                while (c != null && c.Tag == null) c = c.Parent;
                if (c != null && c.Tag is object[]) {
                    var tagData = c.Tag as object[];
                    string type = tagData[0] as string;
                    object data = tagData[1];
                    if (type == "Event" && data is EventItem) {
                        var form = new FormEventInput((EventItem)data);
                        ThemeManager.ApplyTheme(form, isDarkMode);
                        if (form.ShowDialog(this) == DialogResult.OK) {
                            dataService.SaveToJson(dataService.EventsFile, AllEvents);
                            UpdateAllViews();
                        }
                    } else if (type == "Project" && data is ProjectItem) {
                        var form = new FormProjectInput((ProjectItem)data);
                        ThemeManager.ApplyTheme(form, isDarkMode);
                        if (form.ShowDialog(this) == DialogResult.OK) {
                            dataService.SaveToJson(dataService.ProjectsFile, Projects);
                            UpdateAllViews();
                        }
                    } else if (type == "Task" && data is TaskItem) {
                        var form = new FormTaskInput((TaskItem)data, null, Projects, Categories);
                        ThemeManager.ApplyTheme(form, isDarkMode);
                        if (form.ShowDialog(this) == DialogResult.OK) {
                            dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                            UpdateAllViews();
                        }
                    }
                }
            };

            card.DoubleClick += doubleClickAction;
            foreach (Control child in card.Controls) child.DoubleClick += doubleClickAction;

            MouseEventHandler mouseDownAction = (s, ev) => {
                if (ev.Button == MouseButtons.Left) dayInfoDragStartPoint = ev.Location;
            };
            MouseEventHandler mouseMoveAction = (s, ev) => {
                if (ev.Button != MouseButtons.Left || dayInfoDragStartPoint == Point.Empty) return;
                if (Math.Abs(ev.X - dayInfoDragStartPoint.X) < 5 && Math.Abs(ev.Y - dayInfoDragStartPoint.Y) < 5) return;

                var c = s as Control;
                while (c != null && c.Tag == null) c = c.Parent;
                if (c != null && c.Tag is object[]) {
                    c.DoDragDrop(c.Tag, DragDropEffects.Move);
                }
                dayInfoDragStartPoint = Point.Empty;
            };

            card.MouseDown += mouseDownAction;
            card.MouseMove += mouseMoveAction;
            foreach (Control child in card.Controls) {
                child.MouseDown += mouseDownAction;
                child.MouseMove += mouseMoveAction;
            }

            var ctx = new ContextMenuStrip();
            if (isDarkMode) ctx.Renderer = new DarkModeRenderer();
            
            var editMenu = new ToolStripMenuItem("編集");
            editMenu.Click += (s, ev) => doubleClickAction(card, EventArgs.Empty);
            ctx.Items.Add(editMenu);

            if (itemType == "Event") {
                var copyMenu = new ToolStripMenuItem("実績へコピー");
                copyMenu.Click += (s, ev) => {
                    if (itemData is EventItem) {
                        var form = new FormEventToTimeLog((EventItem)itemData, date, AllTimeLogs, isDarkMode);
                        if (form.ShowDialog(this) == DialogResult.OK) {
                            dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                            UpdateAllViews();
                        }
                    }
                };
                ctx.Items.Add(copyMenu);
            }

            var makeRecurringItem = new ToolStripMenuItem("このアイテムを定期ルールに登録");
            makeRecurringItem.Click += (s, ev) => {
                new FormRecurringRuleEditor(dataService, Projects, itemData).ShowDialog(this);
                InvokeRecurringTasks();
                UpdateAllViews();
            };
            ctx.Items.Add(makeRecurringItem);

            var deleteMenu = new ToolStripMenuItem("削除");
            deleteMenu.Click += (s, ev) => {
                if (itemType == "Event" && itemData is EventItem) {
                    var evt = (EventItem)itemData;
                    if (MessageBox.Show(string.Format("予定「{0}」を削除しますか？", evt.Title), "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                        string dStr = date.ToString("yyyy-MM-dd");
                        if (AllEvents.ContainsKey(dStr) && AllEvents[dStr].Contains(evt)) {
                            AllEvents[dStr].Remove(evt);
                            dataService.SaveToJson(dataService.EventsFile, AllEvents);
                            UpdateAllViews();
                        }
                    }
                } else if (itemType == "Project" && itemData is ProjectItem) {
                    var proj = (ProjectItem)itemData;
                    if (MessageBox.Show(string.Format("プロジェクト '{0}' を削除します。\n関連するすべてのタスクも削除されます。\n\nよろしいですか？", proj.ProjectName), "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                        Projects.Remove(proj);
                        AllTasks.RemoveAll(t => t.ProjectID == proj.ProjectID);
                        dataService.SaveToJson(dataService.ProjectsFile, Projects);
                        dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                        UpdateAllViews();
                    }
                } else if (itemType == "Task" && itemData is TaskItem) {
                    if (MessageBox.Show("選択したタスクを削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                        DeleteTasksWithUndo(new List<TaskItem> { (TaskItem)itemData });
                    }
                }
            };
            ctx.Items.Add(deleteMenu);
            card.ContextMenuStrip = ctx;

            return card;
        }

        private void UpdateDayInfoPanel(DateTime date)
        {
            if (dayInfoEventsPanel == null || dayInfoTasksPanel == null) return;
            dayInfoEventsPanel.Controls.Clear();
            dayInfoTasksPanel.Controls.Clear();
            ((GroupBox)dayInfoTableLayoutPanel.Parent).Text = "詳細: " + date.ToString("yyyy/MM/dd (ddd)");
            
            string dateStr = date.ToString("yyyy-MM-dd");
            if (AllEvents.ContainsKey(dateStr)) {
                foreach (var evt in AllEvents[dateStr]) {
                    string details = evt.IsAllDay ? "終日" : evt.StartTime;
                    DateTime st = DateTime.MinValue;
                    if (!evt.IsAllDay && DateTime.TryParse(evt.StartTime, out st)) {
                        details = st.ToString("HH:mm");
                        DateTime et = DateTime.MinValue;
                        if (DateTime.TryParse(evt.EndTime, out et)) details += " - " + et.ToString("HH:mm");
                    }
                    dayInfoEventsPanel.Controls.Add(CreateDayInfoCard("Event", evt.Title, details, ThemeManager.GetEventColor(isDarkMode, isColorVisionSupport), evt, date));
                }
            }
            if (dayInfoEventsPanel.Controls.Count == 0) dayInfoEventsPanel.Controls.Add(new Label { Text = "（なし）", AutoSize = true, Margin = new Padding(15, 10, 0, 5) });

            var projectsOnDay = Projects.Where(p => { DateTime dt = DateTime.MinValue; return !string.IsNullOrEmpty(p.ProjectDueDate) && DateTime.TryParse(p.ProjectDueDate, out dt) && dt.Date == date.Date; });
            foreach (var proj in projectsOnDay) {
                dayInfoTasksPanel.Controls.Add(CreateDayInfoCard("Project", proj.ProjectName, "", ThemeManager.GetProjectColor(isDarkMode, isColorVisionSupport), proj, date));
            }
            var tasksOnDay = AllTasks.Where(t => { DateTime dt = DateTime.MinValue; return (Settings == null || !Settings.HideCompletedTasks || t.進捗度 != "完了済み") && !string.IsNullOrEmpty(t.期日) && DateTime.TryParse(t.期日, out dt) && dt.Date == date.Date; });
            foreach (var task in tasksOnDay) {
                dayInfoTasksPanel.Controls.Add(CreateDayInfoCard("Task", task.タスク, string.Format("優先度: {0} | 進捗: {1}", task.優先度, task.進捗度), ThemeManager.GetTaskColor(isDarkMode, isColorVisionSupport), task, date));
            }
            if (dayInfoTasksPanel.Controls.Count == 0) dayInfoTasksPanel.Controls.Add(new Label { Text = "（なし）", AutoSize = true, Margin = new Padding(15, 10, 0, 5) });
        }

        private void UpdateTimelineView(DateTime date)
        {
            timelinePanel.Tag = date.Date;
            timelinePanel.Invalidate();
        }

        private void KanbanListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            ListBox listBox = sender as ListBox;
            if (listBox == null) return;

            TaskItem task = listBox.Items[e.Index] as TaskItem;
            if (task == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            // プロジェクト情報の取得
            var project = Projects.FirstOrDefault(p => p.ProjectID == task.ProjectID);
            string projectName = project != null ? project.ProjectName : "(プロジェクト未設定)";
            string projectColorString = project != null && !string.IsNullOrEmpty(project.ProjectColor) ? project.ProjectColor : "#D3D3D3";
            Color projectColor = Color.LightGray;
            try { projectColor = ColorTranslator.FromHtml(projectColorString); } catch { }

            // 色設定
            Color listBackColor = isSelected ? SystemColors.Highlight : listBox.BackColor;
            Color textColor = isSelected ? SystemColors.HighlightText : listBox.ForeColor;
            Color subTextColor = isSelected ? SystemColors.HighlightText : (isDarkMode ? Color.Silver : Color.DimGray);

            // 描画領域の計算
            Rectangle cardBounds = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);

            using (var backBrush = new SolidBrush(listBackColor)) g.FillRectangle(backBrush, e.Bounds);

            if (!isSelected)
            {
                Color cardBackColor = isDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Window;
                using (var cardBackBrush = new SolidBrush(cardBackColor)) g.FillRectangle(cardBackBrush, cardBounds);
            }

            using (var borderPen = new Pen(Color.FromArgb(220, 220, 220))) g.DrawRectangle(borderPen, cardBounds);
            using (var projectColorBrush = new SolidBrush(projectColor)) g.FillRectangle(projectColorBrush, cardBounds.X, cardBounds.Y, 4, cardBounds.Height);

            int leftMargin = cardBounds.X + 10;
            int topMargin = cardBounds.Y + 5;
            int contentWidth = Math.Max(1, cardBounds.Width - 15);

            using (var textBrush = new SolidBrush(textColor))
            using (var subTextBrush = new SolidBrush(subTextColor))
            using (var taskFont = new Font("Meiryo UI", 9, FontStyle.Bold))
            using (var projectFont = new Font("Meiryo UI", 8))
            using (var priorityFont = new Font("Meiryo UI", 8, FontStyle.Bold))
            {
                g.DrawString(task.タスク ?? "", taskFont, textBrush, new RectangleF(leftMargin, topMargin, contentWidth, 18));
                g.DrawString(projectName, projectFont, subTextBrush, new RectangleF(leftMargin, topMargin + 18, contentWidth, 15)); // 行間を広げる

                bool showIcons = Settings == null || Settings.ShowIcons;
                string priorityText = string.Format("優先度: {0}", showIcons ? (task.優先度 == "高" ? "🔺高" : (task.優先度 == "低" ? "⏬低" : task.優先度)) : task.優先度);
                Color priorityColor = ThemeManager.GetPriorityColor(task.優先度, isDarkMode, isColorVisionSupport);
                SizeF prioritySize = g.MeasureString(priorityText, priorityFont);
                float priorityY = cardBounds.Bottom - prioritySize.Height - 3;

                bool isOverdue = false;
                DateTime d = DateTime.MinValue;
                if (task.進捗度 != "完了済み" && !string.IsNullOrWhiteSpace(task.期日) && DateTime.TryParse(task.期日, out d) && d.Date < DateTime.Today) isOverdue = true;

                using (var priorityBrush = isSelected ? new SolidBrush(textColor) : new SolidBrush(priorityColor))
                {
                    g.DrawString(priorityText, priorityFont, priorityBrush, leftMargin, priorityY);
                }

                if (!string.IsNullOrWhiteSpace(task.期日))
                {
                    string dueDateText = isOverdue ? string.Format("{0}超過: {1}", showIcons ? "⚠️ " : "", task.期日) : string.Format("期日: {0}", task.期日);
                    SizeF dueDateSize = g.MeasureString(dueDateText, projectFont);
                    
                    using (var dueBrush = new SolidBrush(isOverdue ? ThemeManager.GetOverdueColor(isDarkMode, isColorVisionSupport) : subTextColor))
                    {
                        g.DrawString(dueDateText, projectFont, isSelected ? subTextBrush : dueBrush, cardBounds.Right - dueDateSize.Width - 5, priorityY);
                    }

                    if (isOverdue && isColorVisionSupport && !isSelected)
                    {
                        // 色覚サポート：期日超過のカードは破線の枠線で強調
                        using (var alertPen = new Pen(ThemeManager.GetOverdueColor(isDarkMode, isColorVisionSupport), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                        {
                            g.DrawRectangle(alertPen, cardBounds.X + 4, cardBounds.Y, cardBounds.Width - 4, cardBounds.Height);
                        }
                    }
                }
            }
        }

        // ==========================================================
        // 関連ファイルパネル イベントハンドラと処理
        // ==========================================================

        private void UpdateAssociatedFilesView()
        {
            if (fileListView == null) return;
            fileListView.Items.Clear();
            previewPanel.Controls.Clear();

            if (taskDataGridView.SelectedRows.Count == 0) return;
            var selectedObject = taskDataGridView.SelectedRows[0].Tag;
            if (selectedObject == null) return;

            List<WorkFile> filesToShow = new List<WorkFile>();

            var project = selectedObject as ProjectItem;
            var task = selectedObject as TaskItem;
            if (project != null)
            {
                if (project.WorkFiles != null) filesToShow.AddRange(project.WorkFiles);
            }
            else if (task != null)
            {
                if (task.WorkFiles != null) filesToShow.AddRange(task.WorkFiles);
                if (!string.IsNullOrEmpty(task.ProjectID))
                {
                    var parentProj = Projects.FirstOrDefault(p => p.ProjectID == task.ProjectID);
                    if (parentProj != null && parentProj.WorkFiles != null)
                    {
                        filesToShow.AddRange(parentProj.WorkFiles);
                    }
                }
            }

            var uniqueFiles = filesToShow.GroupBy(f => f.Content).Select(g => g.First()).OrderBy(f => f.DisplayName);

            foreach (var fileObject in uniqueFiles)
            {
                bool showTooltips = Settings == null || Settings.ShowTooltips;
                string tooltipStr = showTooltips ? string.Format("【{0}】\nファイル名: {1}\nパス/URL: {2}\n追加日時: {3}", 
                    fileObject.Type == "URL" ? "URLリンク" : (fileObject.Type == "Memo" ? "メモ" : "ファイル"),
                    fileObject.DisplayName, fileObject.Content, fileObject.DateAdded) : fileObject.Content;

                var lvi = new ListViewItem(fileObject.DisplayName) { Tag = fileObject, Name = fileObject.Content, ToolTipText = tooltipStr };
                lvi.SubItems.Add(fileObject.Type);
                lvi.SubItems.Add(fileObject.DateAdded);
                SetFileIcon(lvi, fileObject.Type);
                fileListView.Items.Add(lvi);
            }
        }

        private void SetFileIcon(ListViewItem item, string fileType)
        {
            string imageKey = "__file__";
            if (fileType == "File" || fileType == "Image")
            {
                string filePath = item.Name;
                if (File.Exists(filePath))
                {
                    string ext = Path.GetExtension(filePath).ToLower();
                    if (!string.IsNullOrEmpty(ext))
                    {
                        string extKey = "EXT_" + ext;
                        if (!globalImageList.Images.ContainsKey(extKey))
                        {
                            NativeMethods.SHFILEINFO sfi = new NativeMethods.SHFILEINFO();
                            uint flags = 0x100 | 0x400 | 0x80; // SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES
                            NativeMethods.SHGetFileInfo(filePath, 0x80, ref sfi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(sfi), flags);
                            if (sfi.hIcon != IntPtr.Zero)
                            {
                                using (Icon icon = Icon.FromHandle(sfi.hIcon))
                                {
                                    globalImageList.Images.Add(extKey, (Icon)icon.Clone());
                                }
                                NativeMethods.DestroyIcon(sfi.hIcon);
                            }
                        }
                        if (globalImageList.Images.ContainsKey(extKey)) imageKey = extKey;
                    }
                }
                else if (Directory.Exists(filePath))
                {
                    imageKey = "__folder__";
                }
            }
            item.ImageKey = imageKey;
        }

        private void FileListView_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            Color bgColor = isDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
            Color fgColor = isDarkMode ? Color.White : SystemColors.ControlText;

            using (var brush = new SolidBrush(bgColor)) {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            ControlPaint.DrawBorder3D(e.Graphics, e.Bounds, Border3DStyle.RaisedInner);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.LeftAndRightPadding;
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, e.Bounds, fgColor, flags);
        }

        private void FileListView_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void FileListView_DragDrop(object sender, DragEventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0)
            {
                MessageBox.Show("ファイルを追加する先のタスクまたはプロジェクトを選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedTag = taskDataGridView.SelectedRows[0].Tag;
            List<WorkFile> workFiles = null;
            bool isProject = false;

            var project = selectedTag as ProjectItem;
            var task = selectedTag as TaskItem;
            if (project != null)
            {
                workFiles = project.WorkFiles;
                isProject = true;
            }
            else if (task != null)
            {
                workFiles = task.WorkFiles;
            }

            if (workFiles == null) return;

            string[] droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
            bool updated = false;

            foreach (string file in droppedFiles)
            {
                if (!workFiles.Any(w => w.Content == file))
                {
                    string ext = Path.GetExtension(file).ToLower();
                    string type = "File";
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif") type = "Image";

                    workFiles.Add(new WorkFile
                    {
                        DisplayName = Path.GetFileName(file),
                        Type = type,
                        Content = file,
                        DateAdded = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                    });
                    updated = true;
                }
            }

            if (updated)
            {
                if (isProject) dataService.SaveToJson(dataService.ProjectsFile, Projects);
                else dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                UpdateAllViews();
            }
        }

        private void FileListContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (isDarkMode) fileListContextMenu.Renderer = new DarkModeRenderer();
            
            if (fileListView.SelectedItems.Count == 0)
            {
                renameFileMenuItem.Visible = false;
                openLocationMenuItem.Visible = false;
                copyPathMenuItem.Visible = false;
                deleteFileMenuItem.Visible = false;
            }
            else
            {
                var fileObject = fileListView.SelectedItems[0].Tag as WorkFile;
                bool isLocalFile = fileObject != null && (fileObject.Type == "File" || fileObject.Type == "Image");

                renameFileMenuItem.Visible = isLocalFile;
                openLocationMenuItem.Visible = isLocalFile;
                copyPathMenuItem.Visible = true;
                deleteFileMenuItem.Visible = true;
            }
        }

        private void RenameFileMenuItem_Click(object sender, EventArgs e)
        {
            if (fileListView.SelectedItems.Count == 0 || taskDataGridView.SelectedRows.Count == 0) return;
            var fileObject = fileListView.SelectedItems[0].Tag as WorkFile;
            var selectedObject = taskDataGridView.SelectedRows[0].Tag;
            if (fileObject == null || selectedObject == null) return;

            string oldPath = fileObject.Content;
            string oldName = Path.GetFileName(oldPath);
            string directory = Path.GetDirectoryName(oldPath);

            string newName = Prompt.ShowDialog("新しいファイル名を入力してください:", "名前の変更", oldName);
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
            
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
            {
                MessageBox.Show("ファイル名に使用できない文字が含まれています。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string newPath = Path.Combine(directory, newName);
            if (File.Exists(newPath))
            {
                MessageBox.Show("同じ名前のファイルが既に存在します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                File.Move(oldPath, newPath);
                fileObject.Content = newPath;
                fileObject.DisplayName = newName;

                if (selectedObject is ProjectItem) dataService.SaveToJson(dataService.ProjectsFile, Projects);
                else dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                UpdateAllViews();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("ファイル名の変更中にエラーが発生しました:\n{0}", ex.Message), "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenLocationMenuItem_Click(object sender, EventArgs e)
        {
            if (fileListView.SelectedItems.Count == 0) return;
            var fileObject = fileListView.SelectedItems[0].Tag as WorkFile;
            if (fileObject != null && (fileObject.Type == "File" || fileObject.Type == "Image") && File.Exists(fileObject.Content))
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + fileObject.Content + "\"");
            }
        }

        private void CopyPathMenuItem_Click(object sender, EventArgs e)
        {
            if (fileListView.SelectedItems.Count == 0) return;
            var fileObject = fileListView.SelectedItems[0].Tag as WorkFile;
            if (fileObject != null && !string.IsNullOrEmpty(fileObject.Content))
                Clipboard.SetText(fileObject.Content);
        }

        private void AddUrlMenuItem_Click(object sender, EventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0) return;
            string url = Prompt.ShowDialog("追加するURLを入力してください:", "URLの追加");
            if (!string.IsNullOrWhiteSpace(url))
            {
                var selectedObject = taskDataGridView.SelectedRows[0].Tag;
                if (selectedObject == null) return;

                List<WorkFile> workFiles = null;
                if (selectedObject is ProjectItem) workFiles = ((ProjectItem)selectedObject).WorkFiles;
                else if (selectedObject is TaskItem) workFiles = ((TaskItem)selectedObject).WorkFiles;

                if (workFiles != null)
                {
                    workFiles.Add(new WorkFile { DisplayName = url, Type = "URL", Content = url, DateAdded = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
                    if (selectedObject is ProjectItem) dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    else dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                    UpdateAllViews();
                }
            }
        }

        private void AddMemoMenuItem_Click(object sender, EventArgs e)
        {
            if (taskDataGridView.SelectedRows.Count == 0) return;
            string memo = ShowMemoInputForm("");
            if (memo != null)
            {
                var selectedObject = taskDataGridView.SelectedRows[0].Tag;
                if (selectedObject == null) return;

                List<WorkFile> workFiles = null;
                if (selectedObject is ProjectItem) workFiles = ((ProjectItem)selectedObject).WorkFiles;
                else if (selectedObject is TaskItem) workFiles = ((TaskItem)selectedObject).WorkFiles;

                if (workFiles != null)
                {
                    string firstLine = memo.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).FirstOrDefault();
                    workFiles.Add(new WorkFile { DisplayName = "[メモ] " + firstLine + "...", Type = "Memo", Content = memo, DateAdded = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
                    if (selectedObject is ProjectItem) dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    else dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                    UpdateAllViews();
                }
            }
        }

        private void FileListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && fileListView.SelectedItems.Count > 0)
            {
                DeleteFileMenuItem_Click(sender, EventArgs.Empty);
            }
        }

        private void DeleteFileMenuItem_Click(object sender, EventArgs e)
        {
            if (fileListView.SelectedItems.Count == 0 || taskDataGridView.SelectedRows.Count == 0) return;
            var selectedObject = taskDataGridView.SelectedRows[0].Tag;
            if (selectedObject == null) return;
            List<WorkFile> workFiles = null;
            if (selectedObject is ProjectItem) workFiles = ((ProjectItem)selectedObject).WorkFiles;
            else if (selectedObject is TaskItem) workFiles = ((TaskItem)selectedObject).WorkFiles;
            if (workFiles != null) {
                var filesToRemove = fileListView.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag as WorkFile).ToList();
                
                if (MessageBox.Show(string.Format("選択した {0} 件のファイルを一覧から削除しますか？", filesToRemove.Count), "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    foreach (var f in filesToRemove) workFiles.Remove(f);
                    if (selectedObject is ProjectItem) dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    else dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                    UpdateAllViews();
                }
            }
        }

        private void FileListView_DoubleClick(object sender, EventArgs e)
        {
            if (fileListView.SelectedItems.Count == 0) return;
            var workFile = fileListView.SelectedItems[0].Tag as WorkFile;
            if (workFile != null)
            {
                try
                {
                    if (workFile.Type == "File" || workFile.Type == "Image" || workFile.Type == "URL")
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(workFile.Content) { UseShellExecute = true });
                    }
                    else if (workFile.Type == "Memo")
                    {
                        string newMemo = ShowMemoInputForm(workFile.Content);
                        if (newMemo != null)
                        {
                            workFile.Content = newMemo;
                            string firstLine = newMemo.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).FirstOrDefault();
                            workFile.DisplayName = "[メモ] " + firstLine + "...";

                            var selectedObject = taskDataGridView.SelectedRows[0].Tag;
                            if (selectedObject is ProjectItem) dataService.SaveToJson(dataService.ProjectsFile, Projects);
                            else dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                            UpdateAllViews();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format("項目を開けませんでした: {0}", ex.Message), "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private string ShowMemoInputForm(string existingText)
        {
            using (var form = new Form { Text = "メモ", Width = 400, Height = 300, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                var textMemo = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Top, Height = 210, Text = existingText };
                form.Controls.Add(textMemo);
                var btnOK = new Button { Text = "OK", Location = new Point(110, 225), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "キャンセル", Location = new Point(200, 225), DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { btnOK, btnCancel });
                form.AcceptButton = btnOK; form.CancelButton = btnCancel; form.ActiveControl = textMemo;
                ThemeManager.ApplyTheme(form, isDarkMode);
                if (form.ShowDialog(this) == DialogResult.OK) return textMemo.Text;
                return null;
            }
        }

        private void FileListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            previewPanel.Controls.Clear();
            if (fileListView.SelectedItems.Count == 0) return;
            var workFile = fileListView.SelectedItems[0].Tag as WorkFile;
            if (workFile != null)
            {
                try
                {
                    if (workFile.Type == "File" || workFile.Type == "Image")
                    {
                        if (!File.Exists(workFile.Content)) { previewPanel.Controls.Add(new Label { Text = string.Format("ファイルが見つかりません:\n{0}", workFile.Content), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }); return; }
                        string ext = Path.GetExtension(workFile.Content).ToLower();
                        
                        var textExtensions = new[] { ".txt", ".log", ".csv", ".json", ".ps1", ".cs", ".md", ".xml", ".ini", ".bat", ".cmd", ".sh", ".yml", ".yaml", ".html", ".htm", ".css", ".js", ".py", ".rb", ".java", ".cpp", ".c", ".h", ".sql", ".php" };
                        var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff" };

                        if (textExtensions.Contains(ext))
                            previewPanel.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Text = File.ReadAllText(workFile.Content, System.Text.Encoding.UTF8) });
                        else if (imageExtensions.Contains(ext))
                            previewPanel.Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = Image.FromFile(workFile.Content) });
                        else if (ext == ".pdf")
                            previewPanel.Controls.Add(new Label { Text = "PDFファイルのプレビューは無効化されています。\n\nダブルクリックして外部アプリで開いてください。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
                        else
                            previewPanel.Controls.Add(new Label { Text = string.Format("プレビュー非対応のファイル形式です ({0})\n\nダブルクリックして外部アプリで開いてください。", ext), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
                    }
            else if (workFile.Type == "URL")
            {
                var lbl = new Label { Text = string.Format("ページのタイトルを取得中...\n\nURL:\n{0}", workFile.Content), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
                previewPanel.Controls.Add(lbl);
                string url = workFile.Content;
                System.Threading.Tasks.Task.Run(() => {
                    try {
                        using (var client = new System.Net.WebClient()) {
                            client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                            client.Encoding = System.Text.Encoding.UTF8;
                            string html = client.DownloadString(url);
                            var match = System.Text.RegularExpressions.Regex.Match(html, @"<title.*?>(.*?)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                            string title = match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : "ページのタイトルが見つかりませんでした。";
                            this.Invoke((MethodInvoker)delegate { lbl.Text = string.Format("URL:\n{0}\n\nタイトル:\n{1}", url, title); });
                        }
                    } catch (Exception ex) {
                        this.Invoke((MethodInvoker)delegate { lbl.Text = string.Format("URL:\n{0}\n\n取得に失敗しました:\n{1}", url, ex.Message); });
                    }
                });
            }
                    else if (workFile.Type == "Memo") previewPanel.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = workFile.Content });
                }
                catch (Exception ex) { previewPanel.Controls.Add(new Label { Text = string.Format("プレビュー中にエラーが発生しました:\n{0}", ex.Message), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Red }); }
            }
        }

        // ==========================================================
        // カンバン (ListBox) イベントハンドラ
        // ==========================================================

        private void KanbanListBox_DoubleClick(object sender, EventArgs e)
        {
            ListBox listBox = sender as ListBox;
            if (listBox != null)
            {
                TaskItem task = listBox.SelectedItem as TaskItem;
                if (task != null)
                {
                    var form = new FormTaskInput(task, null, Projects, Categories);
                    ThemeManager.ApplyTheme(form, isDarkMode);
                    if (form.ShowDialog(this) == DialogResult.OK)
                    {
                        dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                        UpdateAllViews();
                    }
                }
            }
        }

        private void KanbanEditMenuItem_Click(object sender, EventArgs e)
        {
            var listBox = kanbanContextMenu.SourceControl as ListBox;
            if (listBox != null && listBox.SelectedItem != null)
            {
                KanbanListBox_DoubleClick(listBox, EventArgs.Empty);
            }
        }

        private void KanbanDeleteMenuItem_Click(object sender, EventArgs e)
        {
            var listBox = kanbanContextMenu.SourceControl as ListBox;
            if (listBox != null)
            {
                var task = listBox.SelectedItem as TaskItem;
                if (task != null && MessageBox.Show("選択したタスクを削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    DeleteTasksWithUndo(new List<TaskItem> { task });
                }
            }
        }

        private void KanbanListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                var listBox = sender as ListBox;
                if (listBox != null)
                {
                    var task = listBox.SelectedItem as TaskItem;
                    if (task != null && MessageBox.Show(string.Format("タスク '{0}' を削除しますか？", task.タスク), "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        DeleteTasksWithUndo(new List<TaskItem> { task });
                    }
                }
            }
        }

        private void KanbanListBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ListBox lb = sender as ListBox;
                int index = lb.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches)
                {
                    kanbanDragTask = lb.Items[index] as TaskItem;
                    kanbanDragStartPoint = e.Location;
                }
                else
                {
                    kanbanDragTask = null;
                }
            }
        }

        private void KanbanListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && kanbanDragTask != null)
            {
                Size dragSize = SystemInformation.DragSize;
                if (Math.Abs(e.X - kanbanDragStartPoint.X) > dragSize.Width ||
                    Math.Abs(e.Y - kanbanDragStartPoint.Y) > dragSize.Height)
                {
                    ListBox lb = sender as ListBox;
                    lb.DoDragDrop(kanbanDragTask, DragDropEffects.Move);
                    kanbanDragTask = null; // ドラッグ開始後にリセット
                }
            }
        }

        private void KanbanListBox_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(TaskItem)) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void KanbanListBox_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TaskItem)))
            {
                TaskItem task = e.Data.GetData(typeof(TaskItem)) as TaskItem;
                ListBox targetListBox = sender as ListBox;
                string newStatus = targetListBox.Tag as string;

                if (task != null && task.進捗度 != newStatus)
                {
                    UpdateTaskStatus(task, newStatus);
                    UpdateAllViews();
                }
            }
        }

        private void TaskDataGridView_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            var dgv = sender as DataGridView;
            var row = dgv.Rows[e.RowIndex];
            var tag = row.Tag;
            var task = tag as TaskItem;

            if (dgv.Columns[e.ColumnIndex].Name == "Progress" && tag is ProjectItem)
            {
                e.PaintBackground(e.ClipBounds, true);
                int percentage = 0;
                if (e.Value is int) percentage = (int)e.Value;
                else {
                    int pVal;
                    if (e.Value != null && int.TryParse(e.Value.ToString(), out pVal)) percentage = pVal;
                }

                if (percentage > 0)
                {
                    int barWidth = (int)((e.CellBounds.Width - 4) * (percentage / 100.0f));
                    Rectangle barBounds = new Rectangle(e.CellBounds.X + 2, e.CellBounds.Y + 2, barWidth, e.CellBounds.Height - 5);
                    Color barColor = percentage == 100 ? ThemeManager.GetProgressCompleteColor(isColorVisionSupport) : ThemeManager.GetProgressIncompleteColor(isColorVisionSupport);

                    if (isColorVisionSupport && percentage < 100)
                    {
                        // 色覚サポート：進行中の場合は斜線パターンで塗りつぶし、視覚的な違いを強調する
                        using (var brush = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.LightUpwardDiagonal, Color.FromArgb(150, 255, 255, 255), barColor))
                        {
                            e.Graphics.FillRectangle(brush, barBounds);
                        }
                    }
                    else
                    {
                        using (var brush = new SolidBrush(barColor))
                        {
                            e.Graphics.FillRectangle(brush, barBounds);
                        }
                    }
                }

                string text = string.Format("{0} %", percentage);
                Font font = e.CellStyle.Font ?? dgv.Font;
                SizeF textSize = e.Graphics.MeasureString(text, font);
                float textX = e.CellBounds.Left + (e.CellBounds.Width - textSize.Width) / 2;
                float textY = e.CellBounds.Top + (e.CellBounds.Height - textSize.Height) / 2;

                using (var whiteBrush = new SolidBrush(Color.White))
                using (var blackBrush = new SolidBrush(Color.Black))
                {
                    e.Graphics.DrawString(text, font, whiteBrush, textX + 1, textY + 1);
                    e.Graphics.DrawString(text, font, blackBrush, textX, textY);
                }
                e.Handled = true;
            }
            else if (dgv.Columns[e.ColumnIndex].Name == "RecordAction" && task != null && task.進捗度 != "完了済み")
            {
                e.PaintBackground(e.ClipBounds, true);
                string cellValue = e.Value != null ? e.Value.ToString() : "";
                if (string.IsNullOrEmpty(cellValue))
                {
                    e.Handled = true;
                    return;
                }
                
                ControlPaint.DrawBorder3D(e.Graphics, e.CellBounds, Border3DStyle.Raised);
                Color textColor = e.CellStyle.ForeColor;
                
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
                TextRenderer.DrawText(e.Graphics, cellValue, e.CellStyle.Font ?? dgv.Font, e.CellBounds, textColor, flags);
                e.Handled = true;
            }
            else if (dgv.Columns[e.ColumnIndex].Name == "Name" && tag != null)
            {
                e.PaintBackground(e.ClipBounds, true);

                bool isSelected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;
                Color backColor = isSelected ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
                using (var backBrush = new SolidBrush(backColor))
                {
                    e.Graphics.FillRectangle(backBrush, e.CellBounds);
                }
                e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
                
                string text = e.FormattedValue != null ? e.FormattedValue.ToString() : "";
                bool hasFiles = false;
                bool isProject = tag is ProjectItem;

                if (isProject) hasFiles = ((ProjectItem)tag).WorkFiles != null && ((ProjectItem)tag).WorkFiles.Count > 0;
                else if (task != null) hasFiles = task.WorkFiles != null && task.WorkFiles.Count > 0;

                // 文字色は選択時でも元の色を維持する（期限超過の赤色など）
                Color textColor = e.CellStyle.ForeColor;
                
                Font cellFont = e.CellStyle.Font ?? dgv.Font;
                
                int clipIconWidth = 20;
                int expanderWidth = 20;
                int textMargin = 4;

                Rectangle currentBounds = e.CellBounds;
                string namePart = text.TrimStart();

                // 1. クリップアイコン描画 (ファイルがある場合)
                if (hasFiles)
                {
                    using (Font emojiFont = new Font("Segoe UI Emoji", cellFont.Size))
                    {
                        int iconY = currentBounds.Y + (currentBounds.Height - emojiFont.Height) / 2;
                        Rectangle iconRect = new Rectangle(currentBounds.X, iconY, clipIconWidth, currentBounds.Height);
                        TextRenderer.DrawText(e.Graphics, "📎", emojiFont, iconRect, textColor);
                    }
                }
                currentBounds.X += clipIconWidth;
                currentBounds.Width -= clipIconWidth;

                // 2. 開閉記号 / インデント描画
                if (isProject && (text.TrimStart().StartsWith("[-] ") || text.TrimStart().StartsWith("[+] ")))
                {
                    string expanderSymbol = text.TrimStart().Substring(0, 3);
                    namePart = text.TrimStart().Substring(4);
                    Rectangle expanderRect = new Rectangle(currentBounds.X, currentBounds.Y, expanderWidth, currentBounds.Height);
                    TextRenderer.DrawText(e.Graphics, expanderSymbol, cellFont, expanderRect, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                currentBounds.X += expanderWidth;
                currentBounds.Width -= expanderWidth;

                // 3. テキスト本文描画
                currentBounds.X += textMargin;
                currentBounds.Width -= textMargin;
                if (currentBounds.Width > 0)
                {
                    TextRenderer.DrawText(e.Graphics, namePart, cellFont, currentBounds, textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                }
                e.Handled = true;
            }
        }

        private void TaskDataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var dgv = sender as DataGridView;
            var tag = dgv.Rows[e.RowIndex].Tag;
            var task = tag as TaskItem;
            var project = tag as ProjectItem;

            if (dgv.Columns[e.ColumnIndex].Name == "RecordAction" && task != null && task.進捗度 != "完了済み")
            {
                string cellValue = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].Value != null ? dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString() : "";
                if (cellValue == "▶ 開始")
                {
                    if (task.進捗度 == "未実施" || task.進捗度 == "保留") UpdateTaskStatus(task, "実施中");
                    
                    StopTracking();
                    currentlyTrackingTaskID = task.ID;
                    currentTaskStartTime = DateTime.Now;
                    
                    AllTimeLogs.Add(new TimeLog { ID = Guid.NewGuid().ToString(), TaskID = task.ID, StartTime = currentTaskStartTime.Value.ToString("o"), EndTime = null });
                    
                    trackingTimer.Start();
                    longTaskCheckSeconds = 0;
                    longTaskNotificationShown = false;
                }
                else if (cellValue == "■ 停止")
                {
                    StopTracking();
                }
                UpdateDataGridView();
                return;
            }

            if (e.ColumnIndex == dgv.Columns["Name"].Index && project != null)
            {
                if (projectExpansionStates.ContainsKey(project.ProjectID))
                    projectExpansionStates[project.ProjectID] = !projectExpansionStates[project.ProjectID];
                else
                    projectExpansionStates[project.ProjectID] = true;
                
                UpdateDataGridView();
            }
        }

        private void TabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= tabControl.TabPages.Count) return;
            
            var tabPage = tabControl.TabPages[e.Index];
            var tabRect = tabControl.GetTabRect(e.Index);
            var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            
            Color bgColor, textColor;
            if (isDarkMode)
            {
                bgColor = isSelected ? Color.FromArgb(80, 80, 80) : Color.FromArgb(45, 45, 48);
                textColor = Color.White;
            }
            else
            {
                bgColor = isSelected ? Color.White : Color.FromArgb(240, 240, 240);
                textColor = Color.Black;
            }
            
            using (var bgBrush = new SolidBrush(bgColor)) e.Graphics.FillRectangle(bgBrush, tabRect);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(e.Graphics, tabPage.Text, tabControl.Font, tabRect, textColor, flags);
        }

        private void TimelinePanel_Paint(object sender, PaintEventArgs e)
        {
            var panel = sender as Panel;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int topMargin = 35, bottomMargin = 10, leftMargin = 50;
            int startHour = Settings != null ? Settings.TimelineStartHour : 8;
            int endHour = Settings != null ? Settings.TimelineEndHour : 24;
            int totalHours = endHour - startHour;

            int viewHeight = panel.Height - topMargin - bottomMargin;
            int viewWidth = panel.Width;
            int centerX = viewWidth / 2;

            g.Clear(isDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Window);
            if (viewHeight <= 0) return;

            float pixelsPerMinute = (float)viewHeight / (totalHours * 60);

            using (var hourFont = new Font("Meiryo UI", 8))
            using (var headerFont = new Font("Meiryo UI", 10, FontStyle.Bold))
            using (var itemFont = new Font("Meiryo UI", 8))
            using (var itemTextBrush = new SolidBrush(Color.White))
            using (var linePen = new Pen(isDarkMode ? Color.FromArgb(70, 70, 70) : Color.LightGray))
            using (var separatorPen = new Pen(isDarkMode ? Color.Gray : Color.DarkGray, 2))
            using (var textBrush = new SolidBrush(isDarkMode ? Color.Silver : Color.DimGray))
            {
                // 1. 時間グリッドとラベル
                for (int hour = startHour; hour <= endHour; hour++)
                {
                    float y = topMargin + ((hour - startHour) * 60 * pixelsPerMinute);
                    g.DrawLine(linePen, leftMargin - 5, y, viewWidth, y);
                    string timeString = string.Format("{0:D2}:00", hour);
                    g.DrawString(timeString, hourFont, textBrush, 5, y - 7);
                    if (hour < endHour)
                    {
                        float halfHourY = y + (30 * pixelsPerMinute);
                        g.DrawLine(linePen, leftMargin - 2, halfHourY, viewWidth, halfHourY);
                    }
                }

                // 2. 垂直区切り線とヘッダー
                g.DrawLine(separatorPen, centerX, topMargin - 10, centerX, panel.Height - bottomMargin);
                g.DrawString("予定", headerFont, textBrush, leftMargin + (centerX - leftMargin) / 2 - 20, 10);
                g.DrawString("実績", headerFont, textBrush, centerX + (viewWidth - centerX) / 2 - 20, 10);

                DateTime targetDate = panel.Tag is DateTime ? ((DateTime)panel.Tag).Date : DateTime.Today;
                string dateString = targetDate.ToString("yyyy-MM-dd");

                // 現在時刻線
                if (targetDate == DateTime.Today)
                {
                    var now = DateTime.Now;
                    if (now.Hour >= startHour && now.Hour < endHour)
                    {
                        float nowMinutes = (now.Hour - startHour) * 60 + now.Minute;
                        float nowY = topMargin + (nowMinutes * pixelsPerMinute);
                        Color nowColor = isColorVisionSupport ? Color.FromArgb(213, 94, 0) : Color.Red;
                        using (var nowLinePen = new Pen(nowColor, isColorVisionSupport ? 3 : 2)) {
                            if (isColorVisionSupport) nowLinePen.DashStyle = System.Drawing.Drawing2D.DashStyle.DashDot;
                            g.DrawLine(nowLinePen, leftMargin, nowY, viewWidth, nowY);
                        }
                    }
                }

                // 3. 左側: 予定 (Events)
                if (AllEvents.ContainsKey(dateString))
                {
                    Color evtBaseColor = ThemeManager.GetEventColor(isDarkMode, isColorVisionSupport);
                    using (var eventBrush = new SolidBrush(Color.FromArgb(180, evtBaseColor.R, evtBaseColor.G, evtBaseColor.B)))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        foreach (var evt in AllEvents[dateString])
                        {
                            if (evt.IsAllDay || string.IsNullOrEmpty(evt.StartTime) || string.IsNullOrEmpty(evt.EndTime)) continue;
                            DateTime startTime = DateTime.Parse(evt.StartTime);
                            DateTime endTime = DateTime.Parse(evt.EndTime);
                            if (startTime.Date > targetDate || endTime.Date < targetDate) continue;

                            float startMin = startTime.Date < targetDate ? 0 : (startTime.Hour - startHour) * 60 + startTime.Minute;
                            float endMin = endTime.Date > targetDate ? totalHours * 60 : (endTime.Hour - startHour) * 60 + endTime.Minute;

                            float itemY = topMargin + (startMin * pixelsPerMinute);
                            float itemHeight = (endMin - startMin) * pixelsPerMinute;
                            if (itemHeight < 1) continue;

                            var itemRect = new RectangleF(leftMargin + 2, itemY, centerX - leftMargin - 4, itemHeight);
                            g.FillRectangle(eventBrush, itemRect);
                            if (itemHeight > 15) g.DrawString(evt.Title, itemFont, itemTextBrush, itemRect, sf);
                        }
                    }
                }

                // 4. 右側: 実績 (TimeLogs)
                var logsForDay = AllTimeLogs.Where(l => !string.IsNullOrEmpty(l.StartTime) && !string.IsNullOrEmpty(l.EndTime) && DateTime.Parse(l.StartTime).Date == targetDate).ToList();
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var selectionPen = new Pen(Color.Black, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                {
                    foreach (var log in logsForDay)
                    {
                        DateTime startTime = DateTime.Parse(log.StartTime);
                        DateTime endTime = DateTime.Parse(log.EndTime);

                        float logStartMin = (startTime.Hour - startHour) * 60 + startTime.Minute;
                        float logEndMin = (endTime.Hour - startHour) * 60 + endTime.Minute;
                        float logY = topMargin + (logStartMin * pixelsPerMinute);
                        float logHeight = (logEndMin - logStartMin) * pixelsPerMinute;
                        if (logHeight < 1) logHeight = 1;

                        var task = AllTasks.FirstOrDefault(t => t.ID == log.TaskID);
                        string logText = "";
                        Color projectColor = Color.Gray;

                        if (task != null)
                        {
                            var project = Projects.FirstOrDefault(p => p.ProjectID == task.ProjectID);
                            logText = project != null ? string.Format("{0} - {1}", project.ProjectName, task.タスク) : task.タスク;
                            if (project != null && !string.IsNullOrEmpty(project.ProjectColor))
                                try { projectColor = ColorTranslator.FromHtml(project.ProjectColor); } catch { }
                        }
                        else if (!string.IsNullOrEmpty(log.Memo))
                        {
                            logText = "[実績] " + log.Memo;
                            projectColor = Color.SlateGray;
                        }

                        var logRect = new RectangleF(centerX + 2, logY, viewWidth - centerX - 4, logHeight);
                        using (var logBrush = new SolidBrush(projectColor)) g.FillRectangle(logBrush, logRect);

                        if (selectedTimeLog == log) g.DrawRectangle(selectionPen, logRect.X, logRect.Y, logRect.Width, logRect.Height);

                        if (logRect.Height > 15)
                        {
                            var textRect = new RectangleF(logRect.X + 4, logRect.Y + 2, Math.Max(1, logRect.Width - 8), logRect.Height - 4);
                            g.DrawString(logText, itemFont, itemTextBrush, textRect, sf);
                        }
                    }
                }

                // 5. ドラッグ中のゴーストとスナップ線
                if (panel.Capture && !ghostRect.IsEmpty)
                {
                    using (var ghostBrush = new SolidBrush(Color.FromArgb(100, 0, 120, 215))) g.FillRectangle(ghostBrush, ghostRect);
                }
                if (snapLineY > -1)
                {
                    Color snapColor = isColorVisionSupport ? Color.FromArgb(213, 94, 0) : Color.Red;
                    using (var snapPen = new Pen(snapColor, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawLine(snapPen, centerX, snapLineY, panel.Width, snapLineY);
                }
            }
        }

        // --- タイムライン用マウスイベントとヘルパーメソッド ---
        
        private DateTime GetTimeFromY(float y, bool snapToGrid = false)
        {
            int topMargin = 35;
            int startHour = Settings != null ? Settings.TimelineStartHour : 8;
            int endHour = Settings != null ? Settings.TimelineEndHour : 24;
            int totalHours = endHour - startHour;
            int viewHeight = timelinePanel.Height - topMargin - 10;
            if (viewHeight <= 0) return DateTime.Today;

            float pixelsPerMinute = (float)viewHeight / (totalHours * 60);
            float minutes = (y - topMargin) / pixelsPerMinute;
            if (minutes < 0) minutes = 0;
            if (minutes > totalHours * 60) minutes = totalHours * 60;
            
            DateTime targetDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;
            
            if (snapToGrid) {
                int snapMinutes = (int)Math.Round(minutes / 15.0) * 15;
                return targetDate.AddHours(startHour).AddMinutes(snapMinutes);
            }
            
            return targetDate.AddHours(startHour).AddMinutes(minutes);
        }

        private DateTime GetSnappedTime(DateTime time, DateTime date, object excludeLog, int snapThresholdMinutes = 5)
        {
            DateTime snappedTime = time;
            double minDiff = snapThresholdMinutes;

            // 1. 15分単位にスナップ
            int roundedMinute = (int)Math.Round(time.Minute / 15.0) * 15;
            DateTime gridTime = time.Date.AddHours(time.Hour).AddMinutes(roundedMinute);
            double diff = Math.Abs((time - gridTime).TotalMinutes);
            if (diff < minDiff)
            {
                minDiff = diff;
                snappedTime = gridTime;
            }

            // 2. 他のアイテムの端にスナップ
            var snapTargets = new List<DateTime>();
            foreach (var log in AllTimeLogs)
            {
                if (log != excludeLog && !string.IsNullOrEmpty(log.StartTime) && !string.IsNullOrEmpty(log.EndTime))
                {
                    DateTime st, et;
                    if (DateTime.TryParse(log.StartTime, out st) && st.Date == date.Date) snapTargets.Add(st);
                    if (DateTime.TryParse(log.EndTime, out et) && et.Date == date.Date) snapTargets.Add(et);
                }
            }
            string dateStr = date.ToString("yyyy-MM-dd");
            if (AllEvents.ContainsKey(dateStr))
            {
                foreach (var evt in AllEvents[dateStr])
                {
                    if (evt != excludeLog && !evt.IsAllDay && !string.IsNullOrEmpty(evt.StartTime) && !string.IsNullOrEmpty(evt.EndTime))
                    {
                        DateTime st, et;
                        if (DateTime.TryParse(evt.StartTime, out st)) snapTargets.Add(st);
                        if (DateTime.TryParse(evt.EndTime, out et)) snapTargets.Add(et);
                    }
                }
            }

            foreach (var target in snapTargets.Distinct())
            {
                diff = Math.Abs((time - target).TotalMinutes);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    snappedTime = target;
                }
            }
            return snappedTime;
        }

        private bool TestEventOverlap(DateTime start, DateTime end, string excludeId = null)
        {
            if (Settings != null && !Settings.EnableEventOverlapWarning) return false;
            string dateStr = start.ToString("yyyy-MM-dd");
            if (AllEvents.ContainsKey(dateStr))
            {
                foreach (var evt in AllEvents[dateStr])
                {
                    if (excludeId != null && evt.ID == excludeId) continue;
                    if (evt.IsAllDay || string.IsNullOrEmpty(evt.StartTime) || string.IsNullOrEmpty(evt.EndTime)) continue;
                    DateTime eStart, eEnd;
                    if (DateTime.TryParse(evt.StartTime, out eStart) && DateTime.TryParse(evt.EndTime, out eEnd))
                    {
                        if (start < eEnd && end > eStart) return true;
                    }
                }
            }
            return false;
        }

        private void HitTestTimeline(Point pt, out string hitType, out object hitItem, out string resizeMode)
        {
            hitType = null;
            hitItem = null;
            resizeMode = "none";

            int topMargin = 35, bottomMargin = 10, leftMargin = 50;
            int startHour = Settings != null ? Settings.TimelineStartHour : 8;
            int endHour = Settings != null ? Settings.TimelineEndHour : 24;
            int totalHours = endHour - startHour;
            int viewHeight = timelinePanel.Height - topMargin - bottomMargin;
            int centerX = timelinePanel.Width / 2;

            if (viewHeight <= 0) return;
            float pixelsPerMinute = (float)viewHeight / (totalHours * 60);
            DateTime targetDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;
            string dateString = targetDate.ToString("yyyy-MM-dd");

            // 右側: TimeLog
            if (pt.X > centerX)
            {
                var logsForDay = AllTimeLogs.Where(l => !string.IsNullOrEmpty(l.StartTime) && !string.IsNullOrEmpty(l.EndTime) && DateTime.Parse(l.StartTime).Date == targetDate).ToList();
                foreach (var log in logsForDay)
                {
                    DateTime st = DateTime.Parse(log.StartTime);
                    DateTime et = DateTime.Parse(log.EndTime);
                    float startMin = (st.Hour - startHour) * 60 + st.Minute;
                    float endMin = (et.Hour - startHour) * 60 + et.Minute;
                    float y1 = topMargin + startMin * pixelsPerMinute;
                    float y2 = topMargin + endMin * pixelsPerMinute;

                    if (pt.Y >= y1 - 5 && pt.Y <= y2 + 5)
                    {
                        hitType = "TimeLog"; hitItem = log;
                        if (Math.Abs(pt.Y - y1) <= 5) resizeMode = "resizeTop";
                        else if (Math.Abs(pt.Y - y2) <= 5) resizeMode = "resizeBottom";
                        else resizeMode = "move";
                        return;
                    }
                }
            }
            // 左側: Event
            else if (pt.X > leftMargin)
            {
                if (AllEvents.ContainsKey(dateString))
                {
                    foreach (var evt in AllEvents[dateString])
                    {
                        if (evt.IsAllDay || string.IsNullOrEmpty(evt.StartTime) || string.IsNullOrEmpty(evt.EndTime)) continue;
                        DateTime st = DateTime.Parse(evt.StartTime);
                        DateTime et = DateTime.Parse(evt.EndTime);
                        float startMin = (st.Hour - startHour) * 60 + st.Minute;
                        float endMin = (et.Hour - startHour) * 60 + et.Minute;
                        float y1 = topMargin + startMin * pixelsPerMinute;
                        float y2 = topMargin + endMin * pixelsPerMinute;

                        if (pt.Y >= y1 - 5 && pt.Y <= y2 + 5)
                        {
                            hitType = "Event"; hitItem = evt;
                            if (Math.Abs(pt.Y - y1) <= 5) resizeMode = "resizeTop";
                            else if (Math.Abs(pt.Y - y2) <= 5) resizeMode = "resizeBottom";
                            else resizeMode = "move";
                            return;
                        }
                    }
                }
            }
        }

        private void TimelinePanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            timelinePanel.Focus();
            
            string hitType; object hitItem; string rMode;
            HitTestTimeline(e.Location, out hitType, out hitItem, out rMode);

            if (hitItem != null)
            {
                dragMode = rMode;
                dragItemType = hitType;
                draggedItem = hitItem;
                dragStartPoint = e.Location;
                
                if (hitType == "Event") {
                    var evt = (EventItem)hitItem;
                    dragItemOriginalStartTime = DateTime.Parse(evt.StartTime);
                    dragItemOriginalEndTime = DateTime.Parse(evt.EndTime);
                    selectedEvent = evt;
                    selectedTimeLog = null;
                } else if (hitType == "TimeLog") {
                    var log = (TimeLog)hitItem;
                    dragItemOriginalStartTime = DateTime.Parse(log.StartTime);
                    dragItemOriginalEndTime = DateTime.Parse(log.EndTime);
                    selectedTimeLog = log;
                    selectedEvent = null;
                }
                timelinePanel.Capture = true;
            }
            else
            {
                int topMargin = 35;
                if (e.Y < topMargin) return;

                int centerX = timelinePanel.Width / 2;
                dragMode = e.X < centerX ? "createEvent" : "createLog";
                dragStartPoint = e.Location;
                DateTime targetDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;
                dragItemOriginalStartTime = GetSnappedTime(GetTimeFromY(e.Y, false), targetDate, null);
                bool bypassSnap = Control.ModifierKeys.HasFlag(Keys.Alt);
                dragItemOriginalStartTime = bypassSnap ? GetTimeFromY(e.Y, false) : GetSnappedTime(GetTimeFromY(e.Y, false), targetDate, null, 5);
                timelinePanel.Capture = true;
            }
            
            ghostRect = RectangleF.Empty;
            snapLineY = -1;
            timelinePanel.Invalidate();
        }

        private void CalculateDraggedTimes(float y, float startY, float pixelsPerMinute, DateTime targetDate, int startHour, out DateTime newStart, out DateTime newEnd)
        {
            float diffY = y - startY;
            float diffMinutes = diffY / pixelsPerMinute;
            int snapDiffMinutes = (int)Math.Round(diffMinutes / 15.0) * 15;

            newStart = dragItemOriginalStartTime;
            newEnd = dragItemOriginalEndTime;
            
            bool bypassSnap = Control.ModifierKeys.HasFlag(Keys.Alt);
            int threshold = bypassSnap ? 0 : 5;

            if (dragMode == "move") {
                newStart = newStart.AddMinutes(snapDiffMinutes);
                newEnd = newEnd.AddMinutes(snapDiffMinutes);
                DateTime newStartCandidate = dragItemOriginalStartTime.AddMinutes(diffMinutes);
                newStart = GetSnappedTime(newStartCandidate, targetDate, draggedItem);
                newStart = bypassSnap ? newStartCandidate : GetSnappedTime(newStartCandidate, targetDate, draggedItem, threshold);
                TimeSpan duration = dragItemOriginalEndTime - dragItemOriginalStartTime;
                newEnd = newStart.Add(duration);
                if (newStart.Hour < startHour) {
                    newEnd = newEnd.AddMinutes((startHour - newStart.Hour) * 60 - newStart.Minute);
                    newStart = targetDate.AddHours(startHour);
                    newEnd = newStart.Add(duration);
                }
            } else if (dragMode == "resizeTop") {
                newStart = newStart.AddMinutes(snapDiffMinutes);
                DateTime newStartCandidate = dragItemOriginalStartTime.AddMinutes(diffMinutes);
                newStart = GetSnappedTime(newStartCandidate, targetDate, draggedItem);
                newStart = bypassSnap ? newStartCandidate : GetSnappedTime(newStartCandidate, targetDate, draggedItem, threshold);
                if (newStart >= newEnd) newStart = newEnd.AddMinutes(-15);
            } else if (dragMode == "resizeBottom") {
                newEnd = newEnd.AddMinutes(snapDiffMinutes);
                DateTime newEndCandidate = dragItemOriginalEndTime.AddMinutes(diffMinutes);
                newEnd = GetSnappedTime(newEndCandidate, targetDate, draggedItem);
                newEnd = bypassSnap ? newEndCandidate : GetSnappedTime(newEndCandidate, targetDate, draggedItem, threshold);
                if (newEnd <= newStart) newEnd = newStart.AddMinutes(15);
            }
        }

        private void TimelinePanel_MouseMove(object sender, MouseEventArgs e)
        {
            int topMargin = 35, bottomMargin = 10, leftMargin = 50;
            int startHour = Settings != null ? Settings.TimelineStartHour : 8;
            int endHour = Settings != null ? Settings.TimelineEndHour : 24;
            int viewHeight = timelinePanel.Height - topMargin - bottomMargin;
            float pixelsPerMinute = (float)viewHeight / ((endHour - startHour) * 60);

            if (!timelinePanel.Capture)
            {
                string hitType; object hitItem; string rMode;
                HitTestTimeline(e.Location, out hitType, out hitItem, out rMode);
                if (rMode == "resizeTop" || rMode == "resizeBottom") timelinePanel.Cursor = Cursors.SizeNS;
                else if (rMode == "move") timelinePanel.Cursor = Cursors.SizeAll;
                else timelinePanel.Cursor = Cursors.Default;
                return;
            }

            DateTime targetDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;
            
            if (dragMode == "move" || dragMode == "resizeTop" || dragMode == "resizeBottom")
            {
                DateTime newStart; DateTime newEnd;
                CalculateDraggedTimes(e.Y, dragStartPoint.Y, pixelsPerMinute, targetDate, startHour, out newStart, out newEnd);

                float startMin = (newStart.Hour - startHour) * 60 + newStart.Minute;
                float endMin = (newEnd.Hour - startHour) * 60 + newEnd.Minute;
                float y1 = topMargin + startMin * pixelsPerMinute;
                float h = (endMin - startMin) * pixelsPerMinute;
                
                int centerX = timelinePanel.Width / 2;
                ghostRect = dragItemType == "Event" ? new RectangleF(leftMargin + 2, y1, centerX - leftMargin - 4, h) : new RectangleF(centerX + 2, y1, timelinePanel.Width - centerX - 4, h);
                snapLineY = dragMode == "resizeBottom" ? y1 + h : y1;
            }
            else if (dragMode == "createLog" || dragMode == "createEvent")
            {
                bool bypassSnap = Control.ModifierKeys.HasFlag(Keys.Alt);
                DateTime currentHoverTime = GetTimeFromY(e.Y, false);
                currentHoverTime = GetSnappedTime(currentHoverTime, targetDate, null);
                currentHoverTime = bypassSnap ? currentHoverTime : GetSnappedTime(currentHoverTime, targetDate, null, 5);

                DateTime startT = dragItemOriginalStartTime < currentHoverTime ? dragItemOriginalStartTime : currentHoverTime;
                DateTime endT = dragItemOriginalStartTime > currentHoverTime ? dragItemOriginalStartTime : currentHoverTime;
                if (startT == endT) endT = startT.AddMinutes(15);

                float startMin = (startT.Hour - startHour) * 60 + startT.Minute;
                float endMin = (endT.Hour - startHour) * 60 + endT.Minute;
                float y1 = topMargin + startMin * pixelsPerMinute;
                float h = (endMin - startMin) * pixelsPerMinute;

                int centerX = timelinePanel.Width / 2;
                ghostRect = dragMode == "createEvent" ? new RectangleF(leftMargin + 2, y1, centerX - leftMargin - 4, h) : new RectangleF(centerX + 2, y1, timelinePanel.Width - centerX - 4, h);
                snapLineY = e.Y;
            }

            timelinePanel.Invalidate();
        }

        private void TimelinePanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (!timelinePanel.Capture) return;
            timelinePanel.Capture = false;
            timelinePanel.Cursor = Cursors.Default;
            
            DateTime targetDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;

            if ((dragMode == "move" || dragMode == "resizeTop" || dragMode == "resizeBottom") && draggedItem != null)
            {
                int topMargin = 35, bottomMargin = 10;
                int startHour = Settings != null ? Settings.TimelineStartHour : 8;
                int endHour = Settings != null ? Settings.TimelineEndHour : 24;
                float pixelsPerMinute = (float)(timelinePanel.Height - topMargin - bottomMargin) / ((endHour - startHour) * 60);

                DateTime newStart; DateTime newEnd;
                CalculateDraggedTimes(e.Y, dragStartPoint.Y, pixelsPerMinute, targetDate, startHour, out newStart, out newEnd);

                if (dragItemType == "Event") {
                    var evt = (EventItem)draggedItem;
                    if (TestEventOverlap(newStart, newEnd, evt.ID)) {
                        if (MessageBox.Show("指定された時間帯は他の予定と重複しています。保存しますか？", "重複の警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) {
                            dragMode = "none"; dragItemType = null; draggedItem = null; ghostRect = RectangleF.Empty; snapLineY = -1; UpdateAllViews();
                            return;
                        }
                    }
                    evt.StartTime = newStart.ToString("o");
                    evt.EndTime = newEnd.ToString("o");
                    dataService.SaveToJson(dataService.EventsFile, AllEvents);
                } else if (dragItemType == "TimeLog") {
                    var log = (TimeLog)draggedItem;
                    string origStart = log.StartTime;
                    string origEnd = log.EndTime;
                    log.StartTime = newStart.ToString("o");
                    log.EndTime = newEnd.ToString("o");
                    if (ResolveTimeLogOverlap(log)) {
                        dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                        RecalculateTaskTrackedTime(log.TaskID);
                    } else {
                        log.StartTime = origStart;
                        log.EndTime = origEnd;
                    }
                }
            }
            else if (dragMode == "createLog" || dragMode == "createEvent")
            {
                bool bypassSnap = Control.ModifierKeys.HasFlag(Keys.Alt);
                DateTime endT = GetTimeFromY(e.Y, false);
                endT = GetSnappedTime(endT, targetDate, null);
                endT = bypassSnap ? endT : GetSnappedTime(endT, targetDate, null, 5);

                DateTime startT = dragItemOriginalStartTime;
                if (startT > endT) { var temp = startT; startT = endT; endT = temp; }
                if (startT == endT) endT = startT.AddMinutes(30);

                if (dragMode == "createEvent") {
                    if (TestEventOverlap(startT, endT)) {
                        if (MessageBox.Show("指定された時間帯は他の予定と重複しています。保存しますか？", "重複の警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) {
                            dragMode = "none"; dragItemType = null; draggedItem = null; ghostRect = RectangleF.Empty; snapLineY = -1; UpdateAllViews();
                            return;
                        }
                    }
                    var newEvt = new EventItem {
                        ID = Guid.NewGuid().ToString(),
                        Title = "新しい予定",
                        StartTime = startT.ToString("o"),
                        EndTime = endT.ToString("o"),
                        IsAllDay = false
                    };
                    string dateKey = targetDate.ToString("yyyy-MM-dd");
                    if (!AllEvents.ContainsKey(dateKey)) AllEvents[dateKey] = new List<EventItem>();
                    AllEvents[dateKey].Add(newEvt);
                    dataService.SaveToJson(dataService.EventsFile, AllEvents);
                } else {
                    var newLog = new TimeLog {
                        ID = Guid.NewGuid().ToString(),
                        TaskID = "",
                        Memo = "新しい実績",
                        StartTime = startT.ToString("o"),
                        EndTime = endT.ToString("o")
                    };
                    if (ResolveTimeLogOverlap(newLog)) {
                        AllTimeLogs.Add(newLog);
                        dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                    }
                }
            }
            
            dragMode = "none";
            dragItemType = null;
            draggedItem = null;
            ghostRect = RectangleF.Empty;
            snapLineY = -1;
            UpdateAllViews();
        }

        private void RecalculateTaskTrackedTime(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return;
            var task = AllTasks.FirstOrDefault(t => t.ID == taskId);
            if (task == null) return;

            double totalSeconds = 0;
            var taskLogs = AllTimeLogs.Where(l => l.TaskID == taskId && !string.IsNullOrEmpty(l.EndTime));
            foreach (var log in taskLogs)
            {
                DateTime start = DateTime.MinValue, end = DateTime.MinValue;
                if (DateTime.TryParse(log.StartTime, out start) && DateTime.TryParse(log.EndTime, out end))
                    totalSeconds += (end - start).TotalSeconds;
            }

            task.TrackedTimeSeconds = totalSeconds;
            dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
        }

        private void TimelinePanel_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            string hitType, rMode; object hitItem;
            HitTestTimeline(e.Location, out hitType, out hitItem, out rMode);
            if (hitItem != null)
            {
                if (hitType == "Event") {
                    var evt = (EventItem)hitItem;
                    var form = new FormEventInput(evt);
                    ThemeManager.ApplyTheme(form, isDarkMode);
                    if (form.ShowDialog(this) == DialogResult.OK) {
                        dataService.SaveToJson(dataService.EventsFile, AllEvents);
                        UpdateAllViews();
                    }
                } else if (hitType == "TimeLog") {
                    var log = (TimeLog)hitItem;
                    string originalStart = log.StartTime;
                    string originalEnd = log.EndTime;
                    var form = new FormTimeLogEntry(DateTime.MinValue, DateTime.MinValue, Projects, AllTasks, log);
                    ThemeManager.ApplyTheme(form, isDarkMode);
                    if (form.ShowDialog(this) == DialogResult.OK) {
                        if (ResolveTimeLogOverlap(log)) {
                            dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                            RecalculateTaskTrackedTime(log.TaskID);
                            UpdateAllViews(); 
                        } else {
                            log.StartTime = originalStart;
                            log.EndTime = originalEnd;
                        }
                    }
                }
            }
        }

        private void TimelinePanel_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                if (selectedTimeLog != null)
                {
                    if (MessageBox.Show("選択した時間記録を削除しますか？", "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        AllTimeLogs.Remove(selectedTimeLog);
                        selectedTimeLog = null;
                        dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                        DateTime selectedDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;
                        UpdateTimelineView(selectedDate);
                        UpdateDataGridView();
                    }
                }
                else if (selectedEvent != null)
                {
                    DateTime selectedDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;
                    string dateString = selectedDate.ToString("yyyy-MM-dd");
                    if (MessageBox.Show(string.Format("予定「{0}」を削除しますか？", selectedEvent.Title), "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        if (AllEvents.ContainsKey(dateString) && AllEvents[dateString].Contains(selectedEvent))
                        {
                            AllEvents[dateString].Remove(selectedEvent);
                            dataService.SaveToJson(dataService.EventsFile, AllEvents);
                            selectedEvent = null;
                            UpdateAllViews();
                        }
                    }
                }
            }
        }

        private void TimelinePanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            
            string hitType; object hitItem; string rMode;
            HitTestTimeline(e.Location, out hitType, out hitItem, out rMode);
            if (hitItem != null && hitType == "Event")
            {
                ContextMenuStrip ctx = new ContextMenuStrip();
                if (isDarkMode) ctx.Renderer = new DarkModeRenderer();

                
                var evt = (EventItem)hitItem;
                DateTime selectedDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;

                var editMenu = new ToolStripMenuItem("イベントを編集");
                editMenu.Click += (s, ev) => { var form = new FormEventInput(evt); if (form.ShowDialog(this) == DialogResult.OK) { dataService.SaveToJson(dataService.EventsFile, AllEvents); UpdateAllViews(); } };
                ctx.Items.Add(editMenu);

                var copyMenu = new ToolStripMenuItem("実績へコピー");
                copyMenu.Click += (s, ev) => {
                    var form = new FormEventToTimeLog(evt, selectedDate, AllTimeLogs, isDarkMode);
                    if (form.ShowDialog(this) == DialogResult.OK) { dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs); UpdateAllViews(); }
                };
                ctx.Items.Add(copyMenu);
                
                var deleteMenu = new ToolStripMenuItem("イベントを削除");
                deleteMenu.Click += (s, ev) => {
                    if (MessageBox.Show(string.Format("予定「{0}」を削除しますか？", evt.Title), "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                        foreach (var kvp in AllEvents) {
                            if (kvp.Value.Contains(evt)) { kvp.Value.Remove(evt); break; }
                        }
                        dataService.SaveToJson(dataService.EventsFile, AllEvents);
                        UpdateAllViews();
                    }
                };
                ctx.Items.Add(deleteMenu);

                ctx.Show(timelinePanel, e.Location);
            }
            else if (hitItem != null && hitType == "TimeLog")
            {
                ContextMenuStrip ctx = new ContextMenuStrip();
                if (isDarkMode) ctx.Renderer = new DarkModeRenderer();

                var log = (TimeLog)hitItem;
                DateTime selectedDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;

                var adjustMenu = new ToolStripMenuItem("時間の詳細調整...");
                adjustMenu.Click += (s, ev) => {
                    string origStart = log.StartTime;
                    string origEnd = log.EndTime;
                    var form = new FormTimeLogEntry(DateTime.MinValue, DateTime.MinValue, Projects, AllTasks, log);
                    ThemeManager.ApplyTheme(form, isDarkMode);
                    if (form.ShowDialog(this) == DialogResult.OK) {
                        if (ResolveTimeLogOverlap(log)) {
                            dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                            RecalculateTaskTrackedTime(log.TaskID);
                            UpdateAllViews(); 
                        } else {
                            log.StartTime = origStart;
                            log.EndTime = origEnd;
                        }
                    }
                };
                ctx.Items.Add(adjustMenu);

                var extendMenu = new ToolStripMenuItem("次の記録まで延長");
                extendMenu.Click += (s, ev) => {
                    DateTime currentEnd = DateTime.Parse(log.EndTime);
                    var nextLog = AllTimeLogs
                        .Where(l => l != log && !string.IsNullOrEmpty(l.StartTime) && DateTime.Parse(l.StartTime).Date == selectedDate.Date && DateTime.Parse(l.StartTime) >= currentEnd)
                        .OrderBy(l => DateTime.Parse(l.StartTime))
                        .FirstOrDefault();

                    if (nextLog != null) {
                        string origEnd = log.EndTime;
                        log.EndTime = nextLog.StartTime;
                        if (ResolveTimeLogOverlap(log)) {
                            dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                            RecalculateTaskTrackedTime(log.TaskID);
                            UpdateAllViews();
                        } else {
                            log.EndTime = origEnd;
                        }
                    } else {
                        MessageBox.Show("この後に延長できる記録はありません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                };
                ctx.Items.Add(extendMenu);

                var adjustStartMenu = new ToolStripMenuItem("開始時間の調整");
                var startEarlierItem = new ToolStripMenuItem("5分延長 (早める)");
                startEarlierItem.Click += (s, ev) => {
                    string origStart = log.StartTime;
                    log.StartTime = DateTime.Parse(log.StartTime).AddMinutes(-5).ToString("o");
                    if (ResolveTimeLogOverlap(log)) {
                        dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs); RecalculateTaskTrackedTime(log.TaskID); UpdateAllViews();
                    } else {
                        log.StartTime = origStart;
                    }
                };
                var startLaterItem = new ToolStripMenuItem("5分短縮 (遅らせる)");
                startLaterItem.Click += (s, ev) => {
                    string origStart = log.StartTime;
                    log.StartTime = DateTime.Parse(log.StartTime).AddMinutes(5).ToString("o");
                    if (ResolveTimeLogOverlap(log)) {
                        dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs); RecalculateTaskTrackedTime(log.TaskID); UpdateAllViews();
                    } else {
                        log.StartTime = origStart;
                    }
                };
                adjustStartMenu.DropDownItems.Add(startEarlierItem);
                adjustStartMenu.DropDownItems.Add(startLaterItem);
                ctx.Items.Add(adjustStartMenu);

                var adjustEndMenu = new ToolStripMenuItem("終了時間の調整");
                var endLaterItem = new ToolStripMenuItem("5分延長 (遅らせる)");
                endLaterItem.Click += (s, ev) => {
                    string origEnd = log.EndTime;
                    log.EndTime = DateTime.Parse(log.EndTime).AddMinutes(5).ToString("o");
                    if (ResolveTimeLogOverlap(log)) {
                        dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs); RecalculateTaskTrackedTime(log.TaskID); UpdateAllViews();
                    } else {
                        log.EndTime = origEnd;
                    }
                };
                var endEarlierItem = new ToolStripMenuItem("5分短縮 (早める)");
                endEarlierItem.Click += (s, ev) => {
                    string origEnd = log.EndTime;
                    log.EndTime = DateTime.Parse(log.EndTime).AddMinutes(-5).ToString("o");
                    if (ResolveTimeLogOverlap(log)) {
                        dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs); RecalculateTaskTrackedTime(log.TaskID); UpdateAllViews();
                    } else {
                        log.EndTime = origEnd;
                    }
                };
                adjustEndMenu.DropDownItems.Add(endLaterItem);
                adjustEndMenu.DropDownItems.Add(endEarlierItem);
                ctx.Items.Add(adjustEndMenu);

                var deleteMenu = new ToolStripMenuItem("この記録を削除");
                deleteMenu.Click += (s, ev) => {
                    if (MessageBox.Show("この時間記録を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                        AllTimeLogs.Remove(log);
                        dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                        RecalculateTaskTrackedTime(log.TaskID);
                        UpdateAllViews();
                    }
                };
                ctx.Items.Add(deleteMenu);

                ctx.Show(timelinePanel, e.Location);
            }
            else
            {
                int centerX = timelinePanel.Width / 2;
                if (e.X > centerX)
                {
                    ContextMenuStrip ctx = new ContextMenuStrip();
                    if (isDarkMode) ctx.Renderer = new DarkModeRenderer();

                
                    DateTime clickedTime = GetTimeFromY(e.Y);
                    DateTime startTime = clickedTime.AddMinutes(-(clickedTime.Minute % 15));
                    DateTime selectedDate = timelinePanel.Tag is DateTime ? ((DateTime)timelinePanel.Tag).Date : DateTime.Today;
                    
                    var addLogMenu = new ToolStripMenuItem("時間記録を追加");
                    addLogMenu.Click += (s, ev) => {
                        var form = new FormTimeLogEntry(startTime, startTime.AddMinutes(30), Projects, AllTasks);
                        ThemeManager.ApplyTheme(form, isDarkMode);
                        if (form.ShowDialog(this) == DialogResult.OK) {
                            if (ResolveTimeLogOverlap(form.ResultLog)) {
                                AllTimeLogs.Add(form.ResultLog);
                                dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                                RecalculateTaskTrackedTime(form.ResultLog.TaskID);
                                UpdateAllViews();
                            }
                        }
                    };
                    ctx.Items.Add(addLogMenu);

                    var fillGapMenu = new ToolStripMenuItem("この空き時間を埋める");
                    fillGapMenu.Click += (s, ev) => {
                        var logsOnDay = AllTimeLogs
                            .Where(l => !string.IsNullOrEmpty(l.StartTime) && !string.IsNullOrEmpty(l.EndTime) && DateTime.Parse(l.StartTime).Date == selectedDate.Date)
                            .OrderBy(l => DateTime.Parse(l.StartTime)).ToList();
                        
                        var prevLog = logsOnDay.LastOrDefault(l => DateTime.Parse(l.EndTime) <= clickedTime);
                        var nextLog = logsOnDay.FirstOrDefault(l => DateTime.Parse(l.StartTime) >= clickedTime);

                        DateTime fillStart = prevLog != null ? DateTime.Parse(prevLog.EndTime) : selectedDate.AddHours(Settings != null ? Settings.TimelineStartHour : 8);
                        DateTime fillEnd = nextLog != null ? DateTime.Parse(nextLog.StartTime) : selectedDate.AddHours(Settings != null ? Settings.TimelineEndHour : 24);

                        if (fillEnd > fillStart) {
                            var form = new FormTimeLogEntry(fillStart, fillEnd, Projects, AllTasks);
                            ThemeManager.ApplyTheme(form, isDarkMode);
                            if (form.ShowDialog(this) == DialogResult.OK) {
                                if (ResolveTimeLogOverlap(form.ResultLog)) {
                                    AllTimeLogs.Add(form.ResultLog);
                                    dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                                    RecalculateTaskTrackedTime(form.ResultLog.TaskID);
                                    UpdateAllViews();
                                }
                            }
                        }
                    };
                    ctx.Items.Add(fillGapMenu);

                    ctx.Show(timelinePanel, e.Location);
                }
            }
        }

        private void TimelinePanel_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(object[])))
            {
                var data = e.Data.GetData(typeof(object[])) as object[];
                if (data != null && data.Length == 3)
                {
                    e.Effect = DragDropEffects.Move;
                    return;
                }
            }
            e.Effect = DragDropEffects.None;
        }

        private void TimelinePanel_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(object[])))
            {
                var data = e.Data.GetData(typeof(object[])) as object[];
                if (data != null && data.Length == 3)
                {
                    string type = data[0] as string;
                    object item = data[1];
                    DateTime originalDate = (DateTime)data[2];

                    Point clientPoint = timelinePanel.PointToClient(new Point(e.X, e.Y));
                    DateTime dropTime = GetTimeFromY(clientPoint.Y);

                    if (type == "Event" && item is EventItem)
                    {
                        var evt = (EventItem)item;
                        DateTime oldStart;
                        if (DateTime.TryParse(evt.StartTime, out oldStart))
                        {
                            TimeSpan duration = TimeSpan.FromHours(1);
                            DateTime oldEnd;
                            if (DateTime.TryParse(evt.EndTime, out oldEnd) && oldEnd > oldStart) duration = oldEnd - oldStart;

                            string oldDateStr = oldStart.ToString("yyyy-MM-dd");
                            DateTime newStart = dropTime;
                            DateTime newEnd = dropTime.Add(duration);
                            string newDateStr = newStart.ToString("yyyy-MM-dd");

                            if (TestEventOverlap(newStart, newEnd, evt.ID)) {
                                if (MessageBox.Show("指定された時間帯は他の予定と重複しています。保存しますか？", "重複の警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) {
                                    return;
                                }
                            }

                            if (oldDateStr != newDateStr)
                            {
                                if (AllEvents.ContainsKey(oldDateStr)) AllEvents[oldDateStr].Remove(evt);
                                if (!AllEvents.ContainsKey(newDateStr)) AllEvents[newDateStr] = new List<EventItem>();
                                AllEvents[newDateStr].Add(evt);
                            }

                            evt.StartTime = newStart.ToString("o");
                            evt.EndTime = newEnd.ToString("o");
                            evt.IsAllDay = false;

                            dataService.SaveToJson(dataService.EventsFile, AllEvents);
                        }
                    }
                    else if (type == "Task" && item is TaskItem)
                    {
                        var task = (TaskItem)item;
                        DateTime originalTime = dropTime;
                        DateTime d;
                        if (!string.IsNullOrEmpty(task.期日) && DateTime.TryParse(task.期日, out d)) originalTime = dropTime.Date.Add(d.TimeOfDay);
                        task.期日 = originalTime.ToString("yyyy-MM-dd HH:mm");
                        dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                    }
                    else if (type == "Project" && item is ProjectItem)
                    {
                        var proj = (ProjectItem)item;
                        DateTime originalTime = dropTime;
                        DateTime d;
                        if (!string.IsNullOrEmpty(proj.ProjectDueDate) && DateTime.TryParse(proj.ProjectDueDate, out d)) originalTime = dropTime.Date.Add(d.TimeOfDay);
                        proj.ProjectDueDate = originalTime.ToString("yyyy-MM-dd HH:mm");
                        dataService.SaveToJson(dataService.ProjectsFile, Projects);
                    }
                    UpdateAllViews();
                }
            }
        }

        private bool ResolveTimeLogOverlap(TimeLog targetLog)
        {
            if (string.IsNullOrEmpty(targetLog.StartTime) || string.IsNullOrEmpty(targetLog.EndTime)) return true;
            DateTime targetStart = DateTime.Parse(targetLog.StartTime);
            DateTime targetEnd = DateTime.Parse(targetLog.EndTime);

            bool hasOverlap = false;
            foreach (var log in AllTimeLogs)
            {
                if (log == targetLog || string.IsNullOrEmpty(log.StartTime) || string.IsNullOrEmpty(log.EndTime)) continue;
                
                DateTime os = DateTime.Parse(log.StartTime);
                DateTime oe = DateTime.Parse(log.EndTime);

                if (targetStart < oe && targetEnd > os) {
                    hasOverlap = true;
                    break;
                }
            }

            if (!hasOverlap) return true;

            if (Settings != null && Settings.TimeLogOverlapBehavior == "Error")
            {
                MessageBox.Show("指定された時間帯は既存の記録と重複しています。", "重複エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            var logsToRemove = new List<TimeLog>();
            var logsToAdd = new List<TimeLog>();

            foreach (var log in AllTimeLogs.ToList())
            {
                if (log == targetLog || string.IsNullOrEmpty(log.StartTime) || string.IsNullOrEmpty(log.EndTime)) continue;
                
                DateTime os = DateTime.Parse(log.StartTime);
                DateTime oe = DateTime.Parse(log.EndTime);

                if (targetStart <= os && targetEnd >= oe) {
                    logsToRemove.Add(log);
                }
                else if (targetStart > os && targetEnd < oe) {
                    logsToAdd.Add(new TimeLog { ID = Guid.NewGuid().ToString(), TaskID = log.TaskID, Memo = log.Memo, StartTime = targetEnd.ToString("o"), EndTime = oe.ToString("o") });
                    log.EndTime = targetStart.ToString("o");
                }
                else if (targetStart > os && targetStart < oe) {
                    log.EndTime = targetStart.ToString("o");
                }
                else if (targetEnd > os && targetEnd < oe) {
                    log.StartTime = targetEnd.ToString("o");
                }
            }

            foreach (var l in logsToRemove) AllTimeLogs.Remove(l);
            AllTimeLogs.AddRange(logsToAdd);
            
            return true;
        }

        private void StopTracking()
        {
            if (string.IsNullOrEmpty(currentlyTrackingTaskID)) return;

            var logToStop = AllTimeLogs.LastOrDefault(l => l.TaskID == currentlyTrackingTaskID && string.IsNullOrEmpty(l.EndTime));
            if (logToStop != null)
            {
                logToStop.EndTime = DateTime.Now.ToString("o");
                dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                RecalculateTaskTrackedTime(currentlyTrackingTaskID);
            }
            
            trackingTimer.Stop();
            currentlyTrackingTaskID = null;
            currentTaskStartTime = null;
        }

        private void TrackingTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentlyTrackingTaskID)) return;

            var task = AllTasks.FirstOrDefault(t => t.ID == currentlyTrackingTaskID);
            if (task != null)
            {
                longTaskCheckSeconds++;
                int notificationMinutes = Settings != null ? Settings.LongTaskNotificationMinutes : 180;
                if (notificationMinutes > 0 && longTaskCheckSeconds >= notificationMinutes * 60 && !longTaskNotificationShown)
                {
                    longTaskNotificationShown = true;
                    MessageBox.Show(string.Format("タスク「{0}」を {1}分継続中です。まだ作業中ですか？", task.タスク, notificationMinutes), "長時間作業の確認", MessageBoxButtons.OK, MessageBoxIcon.Question);
                    longTaskCheckSeconds = 0;
                }

                // ポモドーロ時間の通知（設定値の倍数分が経過するごとに通知）
                int pomodoroMinutes = Settings != null ? Settings.PomodoroWorkMinutes : 25;
                if (pomodoroMinutes > 0 && longTaskCheckSeconds > 0 && longTaskCheckSeconds % (pomodoroMinutes * 60) == 0)
                {
                    int cycleCount = longTaskCheckSeconds / (pomodoroMinutes * 60);
                    ShowNotification("ポモドーロ", string.Format("「{0}」の作業開始から {1} 分（{2}サイクル目）経過しました。短い休憩を取りましょう！", task.タスク, pomodoroMinutes * cycleCount, cycleCount));
                }

                foreach (DataGridViewRow row in taskDataGridView.Rows)
                {
                    var t = row.Tag as TaskItem;
                    if (t != null && t.ID == task.ID)
                    {
                        double totalSec = t.TrackedTimeSeconds;
                        if (currentTaskStartTime.HasValue) totalSec += (DateTime.Now - currentTaskStartTime.Value).TotalSeconds;
                        row.Cells["TrackedTime"].Value = FormatTrackedTime(totalSec);
                        break;
                    }
                }
            }
        }

        private void IdleCheckTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentlyTrackingTaskID)) return;

            var lastInput = new NativeMethods.LASTINPUTINFO();
            lastInput.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(lastInput);
            
            if (NativeMethods.GetLastInputInfo(ref lastInput))
            {
                uint idleTimeMs = (uint)Environment.TickCount - lastInput.dwTime;
                int idleTimeoutMs = (Settings != null ? Settings.IdleTimeoutMinutes : 5) * 60 * 1000;

                if (idleTimeMs > idleTimeoutMs)
                {
                    if (trackingTimer.Enabled && !idleMessageShown)
                    {
                        trackingTimer.Stop();
                        idleMessageShown = true;
                        MessageBox.Show(string.Format("{0}分間操作がありませんでした。記録を一時停止しました。", Settings != null ? Settings.IdleTimeoutMinutes : 5), "非アクティブ検知", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    if (!trackingTimer.Enabled)
                    {
                        trackingTimer.Start();
                        idleMessageShown = false;
                    }
                }
            }
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                if (!string.IsNullOrEmpty(currentlyTrackingTaskID))
                {
                    suspendedTaskID = currentlyTrackingTaskID;
                    StopTracking();
                    UpdateDataGridView();
                }
            }
            else if (e.Mode == PowerModes.Resume)
            {
                if (!string.IsNullOrEmpty(suspendedTaskID))
                {
                    var taskToResume = AllTasks.FirstOrDefault(t => t.ID == suspendedTaskID);
                    if (taskToResume != null)
                    {
                        var confirmResult = MessageBox.Show(
                            string.Format("PCがスリープから復帰しました。\n\nタスク「{0}」の記録を再開しますか？", taskToResume.タスク),
                            "記録の再開",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question
                        );

                        if (confirmResult == DialogResult.Yes)
                        {
                            currentlyTrackingTaskID = suspendedTaskID;
                            currentTaskStartTime = DateTime.Now;
                            AllTimeLogs.Add(new TimeLog { ID = Guid.NewGuid().ToString(), TaskID = currentlyTrackingTaskID, StartTime = currentTaskStartTime.Value.ToString("o"), EndTime = null });
                            trackingTimer.Start();
                            longTaskCheckSeconds = 0;
                            longTaskNotificationShown = false;
                        }
                    }
                    suspendedTaskID = null;
                    UpdateDataGridView();
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Settings != null && Settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing && !forceExit)
            {
                e.Cancel = true;
                this.Hide();
                if (notifyIcon != null)
                {
                    notifyIcon.ShowBalloonTip(1000, "Task Manager", "最小化しました。トレイアイコンから復帰できます。", ToolTipIcon.Info);
                }
            }
            else
            {
                // 終了時に現在のウィンドウサイズとスプリッター位置を保存する
                if (Settings != null && Settings.RememberWindowSize && this.WindowState == FormWindowState.Normal)
                {
                    Settings.WindowWidth = this.Width;
                    Settings.WindowHeight = this.Height;
                    Settings.MainSplitterDistance = mainContainer.SplitterDistance;
                    Settings.FilesSplitterDistance = associatedFilesSplitContainer.SplitterDistance;
                    Settings.CalendarSplitterDistance = calendarSplitContainer.SplitterDistance;
                    Settings.CalendarLeftSplitterDistance = calendarLeftSplitContainer.SplitterDistance;
                }

                dataService.SaveToJson(dataService.ProjectsFile, Projects);
                dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                SyncWindowSizesFromDisk();
                dataService.SaveToJson(dataService.SettingsFile, Settings);
                dataService.SaveToJson(dataService.EventsFile, AllEvents);
                dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs);
                dataService.SaveToJson(dataService.CategoriesFile, Categories);
                if (notifyIcon != null) notifyIcon.Dispose();
                base.OnFormClosing(e);
            }
        }

        private void ApplyWindowSettings()
        {
            if (Settings != null)
            {
                this.TopMost = Settings.AlwaysOnTop;
                this.Opacity = Settings.WindowOpacity >= 0.1 ? Settings.WindowOpacity : 1.0;

                if (Settings.WindowWidth >= 800) this.Width = Settings.WindowWidth;
                if (Settings.WindowHeight >= 600) this.Height = Settings.WindowHeight;

                try {
                    if (Settings.MainSplitterDistance > 10 && Settings.MainSplitterDistance < this.Height)
                        mainContainer.SplitterDistance = Settings.MainSplitterDistance;
                        
                    if (Settings.FilesSplitterDistance > 10 && Settings.FilesSplitterDistance < this.Width)
                        associatedFilesSplitContainer.SplitterDistance = Settings.FilesSplitterDistance;
                        
                    if (Settings.CalendarSplitterDistance > 10 && Settings.CalendarSplitterDistance < this.Width)
                        calendarSplitContainer.SplitterDistance = Settings.CalendarSplitterDistance;
                        
                    if (Settings.CalendarLeftSplitterDistance > 10 && Settings.CalendarLeftSplitterDistance < this.Height)
                        calendarLeftSplitContainer.SplitterDistance = Settings.CalendarLeftSplitterDistance;
                } catch {}
            }
        }

        private bool ShowLoginDialog()
        {
            using (var form = new Form { Text = "ログイン", Size = new Size(300, 160), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, TopMost = true })
            {
                form.Controls.Add(new Label { Text = "パスコードを入力してください:", Location = new Point(10, 15), AutoSize = true });
                var txt = new TextBox { Location = new Point(10, 40), Size = new Size(260, 25), PasswordChar = '*' };
                form.Controls.Add(txt);
                var btnOk = new Button { Text = "OK", Location = new Point(90, 80), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "キャンセル", Location = new Point(180, 80), DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { btnOk, btnCancel });
                form.AcceptButton = btnOk; form.CancelButton = btnCancel;
                ThemeManager.ApplyTheme(form, isDarkMode);
                form.Shown += (s, ev) => txt.Focus();
                if (form.ShowDialog(this) == DialogResult.OK) return txt.Text == Settings.Passcode;
                return false;
            }
        }

        // ==========================================================
        // 定期タスク生成エンジン
        // ==========================================================

        private void InvokeRecurringTasks()
        {
            DateTime today = DateTime.Today;
            DateTime now = DateTime.Now;

            // 過去の未確認イベントの整理
            var pastUnconfirmedEvents = new List<EventItem>();
            foreach (var kvp in AllEvents)
            {
                DateTime keyDate;
                if (DateTime.TryParse(kvp.Key, out keyDate))
                {
                    if (keyDate.Date >= today) continue;
                    foreach (var evt in kvp.Value)
                    {
                        DateTime eEnd = DateTime.MaxValue;
                        DateTime parsedEnd;
                        if (!string.IsNullOrEmpty(evt.EndTime) && DateTime.TryParse(evt.EndTime, out parsedEnd)) eEnd = parsedEnd;
                        else eEnd = keyDate.AddDays(1);

                        if (eEnd < now && evt.Status != "Completed" && !string.IsNullOrEmpty(evt.ParentRuleID)) pastUnconfirmedEvents.Add(evt);
                    }
                }
            }

            if (pastUnconfirmedEvents.Count > 0)
            {
                if (MessageBox.Show(string.Format("過去の未確認イベントが {0} 件あります。これらをすべて実績（完了）として記録しますか？", pastUnconfirmedEvents.Count), "過去イベントの整理", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    foreach (var evt in pastUnconfirmedEvents) {
                        AllTimeLogs.Add(new TimeLog { ID = Guid.NewGuid().ToString(), TaskID = "", Memo = evt.Title, StartTime = evt.StartTime, EndTime = evt.EndTime });
                        evt.Status = "Completed";
                    }
                    dataService.SaveToJson(dataService.TimeLogsFile, AllTimeLogs); dataService.SaveToJson(dataService.EventsFile, AllEvents); UpdateAllViews();
                }
            }

            var rules = dataService.LoadFromJson<List<RecurringRule>>(dataService.RecurringRulesFile, new List<RecurringRule>());
            if (rules == null || rules.Count == 0) return;

            bool isDirty = false;

            foreach (var rule in rules)
            {
                if (!rule.IsActive) continue;
                if (rule.TriggerModes != null && rule.TriggerModes.Contains("OnCompletion")) continue;

                DateTime nextRunDate;
                if (!DateTime.TryParse(rule.NextRunDate, out nextRunDate))
                {
                    nextRunDate = today;
                    rule.NextRunDate = today.ToString("yyyy-MM-dd");
                }

                bool shouldGenerate = false;
                if (rule.TriggerModes != null && rule.TriggerModes.Contains("PreGeneration"))
                {
                    if (today >= nextRunDate.AddDays(-rule.PreGenDays)) shouldGenerate = true;
                }
                if (!shouldGenerate && (rule.TriggerModes == null || rule.TriggerModes.Contains("OnExpiration") || rule.TriggerModes.Count == 0))
                {
                    if (today >= nextRunDate.Date) shouldGenerate = true;
                }

                if (shouldGenerate)
                {
                    DateTime td;
                    DateTime targetDate = DateTime.TryParse(rule.TheoreticalDate, out td) ? td : nextRunDate;
                    if (targetDate.Date < today)
                    {
                        var nextOcc = GetNextRecurringDate(today, rule);
                        targetDate = nextOcc.TheoreticalDate;
                    }

                    var calcResult = InvokeItemGeneration(rule, targetDate);
                    if (calcResult != null)
                    {
                        rule.NextRunDate = calcResult.ActualDate.ToString("yyyy-MM-dd");
                        rule.TheoreticalDate = calcResult.TheoreticalDate.ToString("yyyy-MM-dd");
                        isDirty = true;
                    }
                }
            }

            if (isDirty)
            {
                dataService.SaveToJson(dataService.RecurringRulesFile, rules);
                dataService.SaveTasksToCsv(dataService.TasksFile, AllTasks);
                dataService.SaveToJson(dataService.ProjectsFile, Projects);
                dataService.SaveToJson(dataService.EventsFile, AllEvents);
                UpdateAllViews();
            }
        }

        private class RecurringDateResult {
            public DateTime TheoreticalDate { get; set; }
            public DateTime ActualDate { get; set; }
        }

        private RecurringDateResult InvokeItemGeneration(RecurringRule rule, DateTime baseDate)
        {
            string targetDateStr = baseDate.ToString("yyyy-MM-dd");
            string newId = Guid.NewGuid().ToString();

            int dueOffset = 0;
            object offsetObj;
            if (rule.Params != null && rule.Params.TryGetValue("DueOffset", out offsetObj))
            {
                if (offsetObj is int) dueOffset = (int)offsetObj;
                else {
                    int si;
                    if (offsetObj != null && int.TryParse(offsetObj.ToString(), out si)) dueOffset = si;
                }
            }
            DateTime finalDueDate = baseDate.AddDays(dueOffset);

            string dueWShift = "None";
            string dueHShift = "None";
            if (rule.Params != null)
            {
                object wObj;
                if (rule.Params.TryGetValue("DueWeekendShift", out wObj) && wObj != null) dueWShift = wObj.ToString();
                object hObj;
                if (rule.Params.TryGetValue("DueHolidayShift", out hObj) && hObj != null) dueHShift = hObj.ToString();
            }

            var holidays = GetHolidays();
            int loopCount = 0;
            while (loopCount < 30)
            {
                loopCount++;
                string dateStr = finalDueDate.ToString("yyyy-MM-dd");
                bool isHoliday = holidays.ContainsKey(dateStr);
                bool isWeekend = finalDueDate.DayOfWeek == DayOfWeek.Saturday || finalDueDate.DayOfWeek == DayOfWeek.Sunday;
                if (!isHoliday && !isWeekend) break;
                bool shifted = false;
                if (isHoliday) {
                    if (dueHShift == "Before") { finalDueDate = finalDueDate.AddDays(-1); shifted = true; }
                    else if (dueHShift == "After") { finalDueDate = finalDueDate.AddDays(1); shifted = true; }
                }
                if (!shifted && isWeekend) {
                    if (dueWShift == "Friday") 
                    { 
                        if (finalDueDate.DayOfWeek == DayOfWeek.Saturday)
                            finalDueDate = finalDueDate.AddDays(-1);
                        else
                            finalDueDate = finalDueDate.AddDays(-2);
                            
                        shifted = true;
                    }
                    else if (dueWShift == "Monday") 
                    { 
                        if (finalDueDate.DayOfWeek == DayOfWeek.Saturday)
                            finalDueDate = finalDueDate.AddDays(2);
                        else
                            finalDueDate = finalDueDate.AddDays(1);
                            
                        shifted = true;
                    }
                }
                if (!shifted) break;
            }

            if (rule.Type == "Task")
            {
                var bt = rule.BaseTask;
                string title = rule.TaskName;
                if (bt != null)
                {
                    string tName = bt.タスク;
                    if (!string.IsNullOrEmpty(tName)) title = tName;
                }

                bool taskExists = false;
                string targetDueDateStr = finalDueDate.ToString("yyyy-MM-dd");
                foreach (var t in AllTasks) {
                    bool matchTitle = (t.タスク == title);
                    bool matchDate = (t.期日 == targetDueDateStr);
                    if (matchTitle && matchDate) {
                        taskExists = true; break;
                    }
                }
                if (taskExists) return null;

                // 新規タスクの生成を安全に実行
                var newTask = new TaskItem();
                newTask.ID = newId;
                newTask.ProjectID = bt != null ? bt.ProjectID : null;
                newTask.期日 = finalDueDate.ToString("yyyy-MM-dd");
                
                string nPrio = "中";
                string nStat = "未実施";
                string nNoti = "全体設定に従う";
                string nCat = "";
                string nSub = "";
                if (bt != null) {
                    if (!string.IsNullOrEmpty(bt.優先度)) nPrio = bt.優先度;
                    if (!string.IsNullOrEmpty(bt.進捗度)) nStat = bt.進捗度;
                    if (!string.IsNullOrEmpty(bt.通知設定)) nNoti = bt.通知設定;
                    if (!string.IsNullOrEmpty(bt.カテゴリ)) nCat = bt.カテゴリ;
                    if (!string.IsNullOrEmpty(bt.サブカテゴリ)) nSub = bt.サブカテゴリ;
                }
                
                newTask.優先度 = nPrio;
                newTask.タスク = title;
                newTask.進捗度 = nStat;
                newTask.通知設定 = nNoti;
                newTask.カテゴリ = nCat;
                newTask.サブカテゴリ = nSub;
                newTask.保存日付 = DateTime.Today.ToString("yyyy-MM-dd");
                newTask.ParentRuleID = rule.RuleID;

                AllTasks.Add(newTask);
            }
            else if (rule.Type == "Project")
            {
                bool projExists = false;
                string targetDueDateStr = finalDueDate.ToString("yyyy-MM-dd");
                foreach (var p in Projects) {
                    if (p.ProjectName == rule.TaskName && p.ProjectDueDate == targetDueDateStr) {
                        projExists = true; break;
                    }
                }
                if (projExists) return null;

                var newProj = new ProjectItem();
                newProj.ProjectID = newId;
                newProj.ProjectName = rule.TaskName;
                newProj.ProjectDueDate = finalDueDate.ToString("yyyy-MM-dd");
                newProj.ProjectColor = "#D3D3D3";
                newProj.AutoArchiveTasks = true;
                newProj.ParentRuleID = rule.RuleID;
                Projects.Add(newProj);

                if (rule.Params != null && rule.Params.ContainsKey("SourceProjectID") && rule.Params["SourceProjectID"] != null)
                {
                    string sourceProjectId = rule.Params["SourceProjectID"].ToString();
                    var sourceProject = Projects.FirstOrDefault(p => p.ProjectID == sourceProjectId);
                    var srcTasks = AllTasks.Where(t => t.ProjectID == sourceProjectId).ToList();

                    TimeSpan dateOffset = TimeSpan.Zero;
                    if (sourceProject != null && !string.IsNullOrEmpty(sourceProject.ProjectDueDate))
                    {
                        DateTime srcDate;
                        if (DateTime.TryParse(sourceProject.ProjectDueDate, out srcDate))
                        {
                            dateOffset = finalDueDate - srcDate;
                        }
                    }

                    var clonedTasks = new List<TaskItem>();
                    foreach (var t in srcTasks)
                    {
                        var clonedTask = new TaskItem();
                        clonedTask.ID = Guid.NewGuid().ToString();
                        clonedTask.ProjectID = newId;
                        clonedTask.タスク = t.タスク;
                        clonedTask.優先度 = t.優先度;
                        clonedTask.進捗度 = "未実施";
                        clonedTask.通知設定 = t.通知設定;
                        clonedTask.カテゴリ = t.カテゴリ;
                        clonedTask.サブカテゴリ = t.サブカテゴリ;
                        clonedTask.保存日付 = DateTime.Today.ToString("yyyy-MM-dd");
                        clonedTask.完了日 = "";
                        clonedTask.TrackedTimeSeconds = 0;
                        clonedTask.WorkFiles = t.WorkFiles != null ? t.WorkFiles.ToList() : new List<WorkFile>();

                        DateTime parsedTDate;
                        if (!string.IsNullOrEmpty(t.期日) && DateTime.TryParse(t.期日, out parsedTDate)) {
                            clonedTask.期日 = parsedTDate.Add(dateOffset).ToString("yyyy-MM-dd");
                        } else {
                            clonedTask.期日 = "";
                        }
                        clonedTasks.Add(clonedTask);
                    }
                    if (clonedTasks.Count > 0) AllTasks.AddRange(clonedTasks);
                }
            }
            else if (rule.Type == "Event")
            {
                if (!AllEvents.ContainsKey(targetDateStr)) AllEvents[targetDateStr] = new List<EventItem>();

                bool eventExists = false;
                foreach (var e in AllEvents[targetDateStr]) {
                    if (e.Title == rule.TaskName) {
                        eventExists = true; break;
                    }
                }
                if (eventExists) return null;

                string startTimeStr = baseDate.ToString("yyyy-MM-ddT09:00:00");
                string endTimeStr = baseDate.ToString("yyyy-MM-ddT10:00:00");
                bool isAllDay = true;

                if (rule.Params != null)
                {
                    if (rule.Params.ContainsKey("StartTime") && rule.Params["StartTime"] != null && rule.Params.ContainsKey("EndTime") && rule.Params["EndTime"] != null) {
                        isAllDay = false;
                        startTimeStr = baseDate.ToString("yyyy-MM-dd") + "T" + rule.Params["StartTime"].ToString();
                        endTimeStr = baseDate.ToString("yyyy-MM-dd") + "T" + rule.Params["EndTime"].ToString();
                    } else if (rule.Params.ContainsKey("IsAllDay") && rule.Params["IsAllDay"] != null) {
                        bool.TryParse(rule.Params["IsAllDay"].ToString(), out isAllDay);
                    }
                }

                var newEvent = new EventItem();
                newEvent.ID = newId;
                newEvent.Title = rule.TaskName;
                newEvent.IsAllDay = isAllDay;
                newEvent.ParentRuleID = rule.RuleID;
                newEvent.Status = "Scheduled";
                newEvent.StartTime = startTimeStr;
                newEvent.EndTime = endTimeStr;

                AllEvents[targetDateStr].Add(newEvent);
            }

            rule.CurrentInstanceID = newId;
            return GetNextRecurringDate(baseDate, rule);
        }

        private RecurringDateResult GetNextRecurringDate(DateTime baseDate, RecurringRule rule)
        {
            DateTime nextTheo = baseDate;
            int interval = rule.IntervalDays > 0 ? rule.IntervalDays : 1;
            
            if (!string.IsNullOrEmpty(rule.Frequency))
            {
                switch (rule.Frequency)
                {
                    case "毎日": nextTheo = baseDate.AddDays(interval); break;
                    case "毎週": nextTheo = baseDate.AddDays(7 * interval); break;
                    case "毎月": nextTheo = baseDate.AddMonths(interval); break;
                    default: nextTheo = baseDate.AddDays(interval); break;
                }
            }

            DateTime actualDate = nextTheo;
            var holidays = GetHolidays();
            int loopCount = 0;
            while (loopCount < 30)
            {
                loopCount++;
                string dateStr = actualDate.ToString("yyyy-MM-dd");
                bool isHoliday = holidays.ContainsKey(dateStr);
                bool isWeekend = actualDate.DayOfWeek == DayOfWeek.Saturday || actualDate.DayOfWeek == DayOfWeek.Sunday;
                if (!isHoliday && !isWeekend) break;
                bool shifted = false;
                
                if (isHoliday) 
                {
                    if (rule.HolidayShift == "Before") { actualDate = actualDate.AddDays(-1); shifted = true; }
                    else if (rule.HolidayShift == "After") { actualDate = actualDate.AddDays(1); shifted = true; }
                }
                
                if (!shifted && isWeekend) 
                {
                    if (rule.WeekendShift == "Friday") 
                    { 
                        if (actualDate.DayOfWeek == DayOfWeek.Saturday)
                            actualDate = actualDate.AddDays(-1);
                        else
                            actualDate = actualDate.AddDays(-2);
                            
                        shifted = true;
                    }
                    else if (rule.WeekendShift == "Monday") 
                    { 
                        if (actualDate.DayOfWeek == DayOfWeek.Saturday)
                            actualDate = actualDate.AddDays(2);
                        else
                            actualDate = actualDate.AddDays(1);
                            
                        shifted = true;
                    }
                }
                
                if (!shifted) break;
            }
            return new RecurringDateResult { TheoreticalDate = nextTheo, ActualDate = actualDate };
        }
    }
}
