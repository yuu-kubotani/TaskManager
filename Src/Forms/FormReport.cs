// 最新版: コンパイルエラー修復済み
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using System.Text;
using System.Windows.Forms.DataVisualization.Charting;
using TaskManager.Models;
using TaskManager.Services;
using TaskManager.Utils;

namespace TaskManager.Forms
{
    public class FormReport : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        private DataService _dataService;
        private List<TimeLog> _allTimeLogs;
        private List<TaskItem> _allTasks;
        private List<ProjectItem> _projects;
        private AppSettings _settings;
        private bool _isDarkMode;
        private bool _isColorVisionSupport;

        private TabControl _tabControl;
        private RadioButton _radioLine, _radioStacked;
        private Chart _chartDailyProject, _chartDailyCategory, _chartDailyStatus;
        private Chart _chartTotalProjectBar, _chartTotalProjectPie;
        private Chart _chartTotalCategoryBar, _chartTotalCategoryPie;
        private Chart _chartTotalStatusBar, _chartTotalStatusPie;
        private Chart _chartSpeedProject, _chartSpeedCategory;

        private Button _btnExport;
        private DateTimePicker _dtpStartDaily, _dtpEndDaily;
        private DateTimePicker _dtpStartTotal, _dtpEndTotal;
        private DateTimePicker _dtpStartSpeed, _dtpEndSpeed;
        private CheckBox _chkAllTimeSpeed, _chkUseExcludeSpeed;
        private Label _lblTotalTimeTotal;
        private TextBox _txtInsights;
        private SplitContainer _mainSplitContainer;

        private class ChartData
        {
            public string label { get; set; }
            public double value { get; set; }
        }

        private class ReportLogData
        {
            public string ProjectName { get; set; }
            public string Category { get; set; }
            public string Status { get; set; }
            public string Date { get; set; }
            public double Hours { get; set; }
        }

        public FormReport(DataService dataService, List<TimeLog> allTimeLogs, List<TaskItem> allTasks, List<ProjectItem> projects, AppSettings settings, bool isDarkMode)
        {
            _dataService = dataService;
            _allTimeLogs = allTimeLogs;
            _allTasks = allTasks;
            _projects = projects;
            _settings = settings;
            _isDarkMode = isDarkMode;
            _isColorVisionSupport = _settings != null && _settings.EnableColorVisionSupport;

            InitializeComponent();
            ThemeManager.ApplyTheme(this, _isDarkMode);
            
            ThemeManager.EnableDynamicResizing(this, _settings, () => _dataService.SaveToJson(_dataService.SettingsFile, _settings), _mainSplitContainer);
            
            GenerateDailyReport();
            GenerateTotalReport();
            GenerateSpeedReport();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try {
                int useImmersiveDarkMode = _isDarkMode ? 1 : 0;
                DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.Text = "レポート分析";
            this.Size = new Size(1200, 850);
            this.StartPosition = FormStartPosition.CenterParent;

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 45 };
            this.Controls.Add(pnlTop);

            _btnExport = new Button { Text = "HTMLへエクスポート", Location = new Point(15, 8), Size = new Size(150, 30) };
            _btnExport.Click += BtnExport_Click;
            pnlTop.Controls.Add(_btnExport);

            _mainSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 650
            };
            this.Controls.Add(_mainSplitContainer);
            _mainSplitContainer.BringToFront();

            _tabControl = new TabControl { Dock = DockStyle.Fill };
            _mainSplitContainer.Panel1.Controls.Add(_tabControl);

            DateTime defaultStart = DateTime.Today.AddDays(-30);
            if (_allTimeLogs.Any() && !string.IsNullOrEmpty(_allTimeLogs.First().StartTime))
            {
                DateTime ds;
                if (DateTime.TryParse(_allTimeLogs.First().StartTime, out ds))
                    defaultStart = ds.Date;
            }

            // --- タブ1: 日別推移 ---
            var tabDaily = new TabPage("日別推移");
            if (_isDarkMode) { tabDaily.BackColor = Color.FromArgb(30, 30, 30); tabDaily.ForeColor = Color.White; }
            
            var pnlDailyTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(5) };
            _dtpStartDaily = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Value = defaultStart };
            _dtpEndDaily = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Value = DateTime.Today };
            var btnGenDaily = new Button { Text = "更新", Width = 60 }; btnGenDaily.Click += (s, e) => GenerateDailyReport();
            _radioLine = new RadioButton { Text = "📈 折れ線グラフ", Checked = true, AutoSize = true, Margin = new Padding(15, 3, 0, 0) };
            _radioStacked = new RadioButton { Text = "📊 積み上げ棒グラフ", AutoSize = true, Margin = new Padding(5, 3, 0, 0) };
            EventHandler radioChanged = (s, e) => { if (((RadioButton)s).Checked) GenerateDailyReport(); };
            _radioLine.CheckedChanged += radioChanged; _radioStacked.CheckedChanged += radioChanged;
            pnlDailyTop.Controls.AddRange(new Control[] { new Label { Text = "開始:", AutoSize = true, Margin = new Padding(3, 5, 0, 0) }, _dtpStartDaily, new Label { Text = "終了:", AutoSize = true, Margin = new Padding(3, 5, 0, 0) }, _dtpEndDaily, btnGenDaily, _radioLine, _radioStacked });
            tabDaily.Controls.Add(pnlDailyTop);
            UIUtility.ApplyDarkCalendar(_dtpStartDaily, this); UIUtility.ApplyDarkCalendar(_dtpEndDaily, this);
            
            var dailyChartLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            for (int i = 0; i < 3; i++) dailyChartLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            
            _chartDailyProject = CreateBaseChart("プロジェクト別");
            _chartDailyCategory = CreateBaseChart("カテゴリ別");
            _chartDailyStatus = CreateBaseChart("ステータス別");
            
            dailyChartLayout.Controls.Add(_chartDailyProject, 0, 0);
            dailyChartLayout.Controls.Add(_chartDailyCategory, 0, 1);
            dailyChartLayout.Controls.Add(_chartDailyStatus, 0, 2);
            tabDaily.Controls.Add(dailyChartLayout);
            dailyChartLayout.BringToFront();

            // --- タブ2: 合計時間 ---
            var tabTotal = new TabPage("合計時間");
            if (_isDarkMode) { tabTotal.BackColor = Color.FromArgb(30, 30, 30); tabTotal.ForeColor = Color.White; }
            
            var pnlTotalTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(5) };
            _dtpStartTotal = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Value = defaultStart };
            _dtpEndTotal = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Value = DateTime.Today };
            var btnGenTotal = new Button { Text = "更新", Width = 60 }; btnGenTotal.Click += (s, e) => GenerateTotalReport();
            _lblTotalTimeTotal = new Label { Text = "総時間: 0時間0分", AutoSize = true, Font = new Font("Meiryo UI", 10, FontStyle.Bold), Margin = new Padding(15, 5, 0, 0) };
            pnlTotalTop.Controls.AddRange(new Control[] { new Label { Text = "開始:", AutoSize = true, Margin = new Padding(3, 5, 0, 0) }, _dtpStartTotal, new Label { Text = "終了:", AutoSize = true, Margin = new Padding(3, 5, 0, 0) }, _dtpEndTotal, btnGenTotal, _lblTotalTimeTotal });
            tabTotal.Controls.Add(pnlTotalTop);
            UIUtility.ApplyDarkCalendar(_dtpStartTotal, this); UIUtility.ApplyDarkCalendar(_dtpEndTotal, this);

            var totalLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            for (int i = 0; i < 3; i++) totalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            _chartTotalProjectBar = CreateBaseChart("棒グラフ"); _chartTotalProjectPie = CreateBaseChart("円グラフ");
            _chartTotalCategoryBar = CreateBaseChart("棒グラフ"); _chartTotalCategoryPie = CreateBaseChart("円グラフ");
            _chartTotalStatusBar = CreateBaseChart("棒グラフ"); _chartTotalStatusPie = CreateBaseChart("円グラフ");

            _chartTotalProjectBar.Legends[0].Enabled = false; _chartTotalCategoryBar.Legends[0].Enabled = false; _chartTotalStatusBar.Legends[0].Enabled = false;

            totalLayout.Controls.Add(CreateGroupedSplit("プロジェクト別", _chartTotalProjectBar, _chartTotalProjectPie), 0, 0);
            totalLayout.Controls.Add(CreateGroupedSplit("カテゴリ別", _chartTotalCategoryBar, _chartTotalCategoryPie), 1, 0);
            totalLayout.Controls.Add(CreateGroupedSplit("ステータス別", _chartTotalStatusBar, _chartTotalStatusPie), 2, 0);
            tabTotal.Controls.Add(totalLayout);
            totalLayout.BringToFront();

            // --- タブ3: 完了スピード ---
            var tabSpeed = new TabPage("⏱️ 完了スピード");
            if (_isDarkMode) { tabSpeed.BackColor = Color.FromArgb(30, 30, 30); tabSpeed.ForeColor = Color.White; }
            
            var pnlSpeedTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(5) };
            _dtpStartSpeed = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Value = defaultStart };
            _dtpEndSpeed = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 110, Value = DateTime.Today };
            _chkAllTimeSpeed = new CheckBox { Text = "全期間", AutoSize = true, Margin = new Padding(10, 4, 0, 0) };
            _chkUseExcludeSpeed = new CheckBox { Text = "除外ステータスを考慮する(実質日数)", AutoSize = true, Margin = new Padding(10, 4, 0, 0) };
            var btnGenSpeed = new Button { Text = "更新", Width = 60 }; btnGenSpeed.Click += (s, e) => GenerateSpeedReport();
            _chkAllTimeSpeed.CheckedChanged += (s, e) => { _dtpStartSpeed.Enabled = !_chkAllTimeSpeed.Checked; _dtpEndSpeed.Enabled = !_chkAllTimeSpeed.Checked; };
            pnlSpeedTop.Controls.AddRange(new Control[] { new Label { Text = "開始:", AutoSize = true, Margin = new Padding(3, 5, 0, 0) }, _dtpStartSpeed, new Label { Text = "終了:", AutoSize = true, Margin = new Padding(3, 5, 0, 0) }, _dtpEndSpeed, _chkAllTimeSpeed, _chkUseExcludeSpeed, btnGenSpeed });
            tabSpeed.Controls.Add(pnlSpeedTop);
            UIUtility.ApplyDarkCalendar(_dtpStartSpeed, this); UIUtility.ApplyDarkCalendar(_dtpEndSpeed, this);

            var speedLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            speedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            speedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            _chartSpeedProject = CreateBaseChart("プロジェクト別 平均完了日数 (日)");
            _chartSpeedCategory = CreateBaseChart("カテゴリ別 平均完了日数 (日)");
            _chartSpeedProject.Legends[0].Enabled = false; _chartSpeedCategory.Legends[0].Enabled = false;
            _chartSpeedProject.ChartAreas[0].AxisX.LabelStyle.Angle = 0; _chartSpeedCategory.ChartAreas[0].AxisX.LabelStyle.Angle = 0;

            speedLayout.Controls.Add(_chartSpeedProject, 0, 0);
            speedLayout.Controls.Add(_chartSpeedCategory, 1, 0);
            tabSpeed.Controls.Add(speedLayout);
            speedLayout.BringToFront();

            _tabControl.TabPages.AddRange(new TabPage[] { tabDaily, tabTotal, tabSpeed });

            var grpInsights = new GroupBox { Text = "💡 分析とアドバイス", Dock = DockStyle.Fill, Padding = new Padding(10) };
            _mainSplitContainer.Panel2.Controls.Add(grpInsights);

            _txtInsights = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Meiryo UI", 10) };
            grpInsights.Controls.Add(_txtInsights);
        }

        private Chart CreateBaseChart(string title)
        {
            var chart = new Chart { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var area = new ChartArea { Name = "MainArea", BackColor = Color.Transparent };
            area.AxisX.LabelStyle.Angle = -45;
            area.AxisX.Interval = 1;
            
            if (_isDarkMode) {
                chart.BackColor = Color.FromArgb(30, 30, 30);
                chart.ForeColor = Color.White;
                area.BackColor = Color.FromArgb(30, 30, 30);
                area.AxisX.LabelStyle.ForeColor = Color.White;
                area.AxisY.LabelStyle.ForeColor = Color.White;
                area.AxisX.LineColor = Color.Gray;
                area.AxisY.LineColor = Color.Gray;
                area.AxisX.MajorGrid.LineColor = Color.FromArgb(60, 60, 60);
                area.AxisY.MajorGrid.LineColor = Color.FromArgb(60, 60, 60);
            } else {
                area.AxisX.MajorGrid.LineColor = Color.Gainsboro;
                area.AxisY.MajorGrid.LineColor = Color.Gainsboro;
            }
            chart.ChartAreas.Add(area);
            var t = chart.Titles.Add(title);
            t.Font = new Font("Meiryo UI", 10, FontStyle.Bold);
            if (_isDarkMode) t.ForeColor = Color.White;
            var lg = new Legend("Default") { Docking = Docking.Top, Font = new Font("Meiryo UI", 8), BackColor = Color.Transparent };
            if (_isDarkMode) lg.ForeColor = Color.White;
            chart.Legends.Add(lg);
            return chart;
        }

        private GroupBox CreateGroupedSplit(string title, Chart top, Chart bottom)
        {
            var grp = new GroupBox { Text = title, Dock = DockStyle.Fill };
            if (_isDarkMode) grp.ForeColor = Color.White;
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(top);
            split.Panel2.Controls.Add(bottom);
            grp.Controls.Add(split);
            return grp;
        }

        private List<ReportLogData> GetFilteredLogs(DateTime start, DateTime end)
        {
            var filteredLogs = _allTimeLogs.Where(l =>
                !string.IsNullOrEmpty(l.StartTime) && !string.IsNullOrEmpty(l.EndTime) &&
                DateTime.Parse(l.StartTime).Date <= end.Date && DateTime.Parse(l.EndTime).Date >= start.Date
            ).ToList();

            var data = new List<ReportLogData>();
            foreach (var log in filteredLogs)
            {
                var task = _allTasks.FirstOrDefault(t => t.ID == log.TaskID);
                if (task != null)
                {
                    var proj = _projects.FirstOrDefault(p => p.ProjectID == task.ProjectID);
                    data.Add(new ReportLogData
                    {
                        ProjectName = proj != null ? proj.ProjectName : "(未分類)",
                        Category = string.IsNullOrEmpty(task.カテゴリ) ? "(未分類)" : task.カテゴリ,
                        Status = string.IsNullOrEmpty(task.進捗度) ? "(未設定)" : task.進捗度,
                        Date = DateTime.Parse(log.StartTime).ToString("yyyy-MM-dd"),
                        Hours = (DateTime.Parse(log.EndTime) - DateTime.Parse(log.StartTime)).TotalHours
                    });
                }
            }
            return data;
        }

        private void GenerateDailyReport()
        {
            var start = _dtpStartDaily.Value.Date;
            var end = _dtpEndDaily.Value.Date.AddDays(1).AddSeconds(-1);
            var dataForCharts = GetFilteredLogs(start, end);

            _chartDailyProject.Series.Clear(); _chartDailyCategory.Series.Clear(); _chartDailyStatus.Series.Clear();
            SeriesChartType currentChartType = _radioStacked.Checked ? SeriesChartType.StackedColumn : SeriesChartType.Line;
            var dateRange = new List<string>();
            for (var d = start; d <= end; d = d.AddDays(1)) dateRange.Add(d.ToString("yyyy-MM-dd"));

            Action<Chart, Func<ReportLogData, string>> populateTrendChart = (chart, keySelector) => {
                var allKeys = dataForCharts.Select(keySelector).Distinct().OrderBy(k => k).ToList();
                foreach (var key in allKeys) {
                    var series = chart.Series.Add(key);
                    series.ChartType = currentChartType;
                    if (currentChartType == SeriesChartType.Line) series.BorderWidth = 2;
                    foreach (var day in dateRange) {
                        double hours = dataForCharts.Where(d => keySelector(d) == key && d.Date == day).Sum(d => d.Hours);
                        series.Points.AddXY(DateTime.Parse(day).ToString("MM/dd"), hours);
                    }
                }
            };
            populateTrendChart(_chartDailyProject, d => d.ProjectName);
            populateTrendChart(_chartDailyCategory, d => d.Category);
            populateTrendChart(_chartDailyStatus, d => d.Status);
        }

        private void GenerateTotalReport()
        {
            var start = _dtpStartTotal.Value.Date;
            var end = _dtpEndTotal.Value.Date.AddDays(1).AddSeconds(-1);
            var dataForCharts = GetFilteredLogs(start, end);
            
            double totalHours = dataForCharts.Sum(d => d.Hours);
            int tMin = (int)(totalHours * 60);
            _lblTotalTimeTotal.Text = string.Format("総時間: {0}時間{1}分", tMin / 60, tMin % 60);

            var projectSummary = dataForCharts.GroupBy(d => d.ProjectName).Select(g => new ChartData { label = g.Key, value = g.Sum(x => (double)x.Hours) }).OrderByDescending(x => x.value).ToList();
            var categorySummary = dataForCharts.GroupBy(d => d.Category).Select(g => new ChartData { label = g.Key, value = g.Sum(x => (double)x.Hours) }).OrderByDescending(x => x.value).ToList();
            var statusSummary = dataForCharts.GroupBy(d => d.Status).Select(g => new ChartData { label = g.Key, value = g.Sum(x => (double)x.Hours) }).OrderByDescending(x => x.value).ToList();

            Chart[] charts = { _chartTotalProjectBar, _chartTotalProjectPie, _chartTotalCategoryBar, _chartTotalCategoryPie, _chartTotalStatusBar, _chartTotalStatusPie };
            foreach (var c in charts) c.Series.Clear();

            Action<Chart, Chart, List<ChartData>> populateTotalTimeCharts = (chartBar, chartPie, summaryData) => {
                var seriesBar = chartBar.Series.Add("s"); seriesBar.ChartType = SeriesChartType.Column;
                var seriesPie = chartPie.Series.Add("s"); seriesPie.ChartType = SeriesChartType.Pie;
                seriesPie["PieLabelStyle"] = "Outside"; seriesPie.Label = "#VALX (#PERCENT{P1})"; seriesPie.LabelForeColor = _isDarkMode ? Color.White : Color.Black;
                foreach (var item in summaryData) {
                    if (item.value > 0) {
                        int pt = seriesBar.Points.AddXY(item.label, item.value);
                        seriesBar.Points[pt].IsValueShownAsLabel = true; seriesBar.Points[pt].LabelForeColor = _isDarkMode ? Color.White : Color.Black; seriesBar.Points[pt].LabelFormat = "F2";
                        seriesPie.Points.AddXY(item.label, item.value);
                    }
                }
            };
            populateTotalTimeCharts(_chartTotalProjectBar, _chartTotalProjectPie, projectSummary);
            populateTotalTimeCharts(_chartTotalCategoryBar, _chartTotalCategoryPie, categorySummary);
            populateTotalTimeCharts(_chartTotalStatusBar, _chartTotalStatusPie, statusSummary);
            
            UpdateInsights();
        }

        private void GenerateSpeedReport()
        {
            _chartSpeedProject.Series.Clear(); _chartSpeedCategory.Series.Clear();
            
            var archivedTasks = _dataService.LoadTasksFromCsv(_dataService.ArchivedTasksFile) ?? new List<TaskItem>();
            var allPotentialTasks = _allTasks.Concat(archivedTasks).ToList();

            DateTime start = _chkAllTimeSpeed.Checked ? DateTime.MinValue : _dtpStartSpeed.Value.Date;
            DateTime end = _chkAllTimeSpeed.Checked ? DateTime.MaxValue : _dtpEndSpeed.Value.Date.AddDays(1).AddSeconds(-1);

            // 実質完了日数（サイクルタイム）は、StartedAt（実施中になった時間）が存在するタスクのみ計算対象とする
            var completedTasks = allPotentialTasks.Where(t => t.進捗度 == "完了済み" && !string.IsNullOrEmpty(t.StartedAt)).ToList();
            var validSpeeds = new List<dynamic>();
            var excludeStatuses = (_chkUseExcludeSpeed.Checked && _settings != null && _settings.LeadTimeExcludeStatuses != null) 
                                  ? _settings.LeadTimeExcludeStatuses : new List<string>();
            
            var statusLogs = _dataService.LoadFromJson<List<StatusLog>>(_dataService.StatusLogsFile, new List<StatusLog>());
            var logsByTask = statusLogs.GroupBy(l => l.TaskID).ToDictionary(g => g.Key, g => g.OrderBy(l => {
                DateTime parsed; return DateTime.TryParse(l.Timestamp, out parsed) ? parsed : DateTime.MinValue;
            }).ToList());

            foreach (var t in completedTasks)
            {
                DateTime startTime;
                if (!DateTime.TryParse(t.StartedAt, out startTime)) continue;

                DateTime compTime;
                if (!string.IsNullOrEmpty(t.CompletedAt) && DateTime.TryParse(t.CompletedAt, out compTime)) { }
                else if (DateTime.TryParse(t.完了日, out compTime)) { compTime = compTime.Date.AddHours(23).AddMinutes(59).AddSeconds(59); }
                else { continue; }

                if (compTime < start || compTime > end) continue;
                if (compTime < startTime) compTime = startTime;

                TimeSpan totalDuration = compTime - startTime;
                TimeSpan excludedDuration = TimeSpan.Zero;

                if (excludeStatuses.Any() && logsByTask.ContainsKey(t.ID))
                {
                    var taskLogs = logsByTask[t.ID];
                    string currentStatus = "未実施";
                    var firstLog = taskLogs.FirstOrDefault(l => { DateTime pt; return DateTime.TryParse(l.Timestamp, out pt) && pt >= startTime; });
                    if (firstLog != null && !string.IsNullOrEmpty(firstLog.OldStatus)) currentStatus = firstLog.OldStatus;

                    DateTime lastTime = startTime;
                    foreach (var log in taskLogs)
                    {
                        DateTime logTime;
                        if (!DateTime.TryParse(log.Timestamp, out logTime)) continue;
                        if (logTime < startTime) continue;
                        if (logTime > compTime) logTime = compTime;

                        if (logTime > lastTime && excludeStatuses.Contains(currentStatus)) excludedDuration += (logTime - lastTime);
                        currentStatus = log.NewStatus ?? "未実施";
                        lastTime = logTime;
                        if (lastTime >= compTime) break;
                    }
                    if (lastTime < compTime && excludeStatuses.Contains(currentStatus)) excludedDuration += (compTime - lastTime);
                }

                double days = Math.Max(0, (totalDuration - excludedDuration).TotalDays);
                validSpeeds.Add(new { t.ProjectID, t.カテゴリ, Days = days });
            }

            var spProj = validSpeeds.GroupBy(x => (string)x.ProjectID).Select(g => {
                var p = _projects.FirstOrDefault(proj => proj.ProjectID == g.Key);
                return new ChartData { label = p != null ? p.ProjectName : "(未分類)", value = g.Average(x => (double)x.Days) };
            }).OrderByDescending(x => x.value).ToList();

            var spCat = validSpeeds.Where(x => !string.IsNullOrEmpty((string)x.カテゴリ)).GroupBy(x => (string)x.カテゴリ)
                .Select(g => new ChartData { label = g.Key, value = g.Average(x => (double)x.Days) })
                .OrderByDescending(x => x.value).ToList();

            Action<Chart, List<ChartData>> populateSpeedChart = (chart, speedData) => {
                var series = chart.Series.Add("s"); series.ChartType = SeriesChartType.Bar; series.IsValueShownAsLabel = true; 
                series.LabelForeColor = _isDarkMode ? Color.White : Color.Black; series.LabelFormat = "F1";
                foreach (var item in speedData) series.Points.AddXY(item.label, item.value);
            };
            populateSpeedChart(_chartSpeedProject, spProj);
            populateSpeedChart(_chartSpeedCategory, spCat);
            
            UpdateInsights();
        }

        private void UpdateInsights()
        {
            string insights = "💡 詳細分析コメント (インサイト)\r\n";
            
            int unclassifiedCriticalPercent = _settings != null && _settings.UnclassifiedCriticalPercent > 0 ? _settings.UnclassifiedCriticalPercent : 40;
            int unclassifiedWarningPercent = _settings != null && _settings.UnclassifiedWarningPercent > 0 ? _settings.UnclassifiedWarningPercent : 15;
            int taskLongTermDays = _settings != null && _settings.TaskLongTermDays > 0 ? _settings.TaskLongTermDays : 60;
            int taskMediumTermDays = _settings != null && _settings.TaskMediumTermDays > 0 ? _settings.TaskMediumTermDays : 21;
            int taskEfficientDays = _settings != null && _settings.TaskEfficientDays > 0 ? _settings.TaskEfficientDays : 14;
            int categorySlowDays = _settings != null && _settings.CategorySlowDays > 0 ? _settings.CategorySlowDays : 30;
            int zeroDayTaskCount = _settings != null && _settings.ZeroDayTaskCount > 0 ? _settings.ZeroDayTaskCount : 5;
            
            var startT = _dtpStartTotal.Value.Date;
            var endT = _dtpEndTotal.Value.Date.AddDays(1).AddSeconds(-1);
            var dataForCharts = GetFilteredLogs(startT, endT);
            double totalHours = dataForCharts.Sum(d => d.Hours);
            var projectSummary = dataForCharts.GroupBy(d => d.ProjectName).Select(g => new ChartData { label = g.Key, value = g.Sum(x => (double)x.Hours) }).ToList();
            var categorySummary = dataForCharts.GroupBy(d => d.Category).Select(g => new ChartData { label = g.Key, value = g.Sum(x => (double)x.Hours) }).ToList();

            if (categorySummary.Count > 1) {
                insights += "• [良好]  時間の分類が適切に行われています。時間の使い方の内訳が明確で、振り返りやすい状態です。\r\n";
            } else {
                insights += "• [情報]  記録された時間のカテゴリが1つのみです。複数のカテゴリに分類すると、時間の使い方をより詳細に分析できます。\r\n";
            }

            if (totalHours > 0) {
                var unclassifiedProj = projectSummary.FirstOrDefault(p => p.label == "(未分類)");
                if (unclassifiedProj != null) {
                    double unclassProjPct = (unclassifiedProj.value / totalHours) * 100;
                    if (unclassProjPct >= unclassifiedCriticalPercent) {
                        insights += string.Format("• [最重要課題]  「(未分類)」のプロジェクト時間が全体の {0:F1}% を占め、最重要課題の基準（{1}%）を超過しています。作業の目的や紐付けを見直すことをお勧めします。\r\n", unclassProjPct, unclassifiedCriticalPercent);
                    } else if (unclassProjPct >= unclassifiedWarningPercent) {
                        insights += string.Format("• [課題]  「(未分類)」のプロジェクト時間が全体の {0:F1}% を占めています。タスクを適切なプロジェクトに分類することで、振り返りの精度が上がります。\r\n", unclassProjPct);
                    }
                }

                var unclassifiedCat = categorySummary.FirstOrDefault(c => c.label == "(未分類)");
                if (unclassifiedCat != null) {
                    double unclassCatPct = (unclassifiedCat.value / totalHours) * 100;
                    if (unclassCatPct >= unclassifiedCriticalPercent) {
                        insights += string.Format("• [最重要課題]  「(未分類)」のカテゴリ時間が全体の {0:F1}% に達しています。カテゴリ分けのルールを見直してください。\r\n", unclassCatPct);
                    }
                }

                var topProject = projectSummary.FirstOrDefault(p => p.label != "(未分類)");
                if (topProject != null) {
                    double projPercentage = (topProject.value / totalHours) * 100;
                    if (projPercentage >= unclassifiedCriticalPercent) {
                        insights += string.Format("• [最重要課題]  プロジェクト「{0}」の作業時間が全体の {1:F1}% を占め、警告基準（{2}%）を超過しています。負荷が集中している可能性があるため、リソース配分を見直してください。\r\n", topProject.label, projPercentage, unclassifiedCriticalPercent);
                    }
                }

                var topCategory = categorySummary.FirstOrDefault(c => c.label != "(未分類)");
                if (topCategory != null) {
                    double percentage = (topCategory.value / totalHours) * 100;
                    if (percentage >= unclassifiedCriticalPercent) {
                        insights += string.Format("• [注意]  カテゴリ「{0}」の時間が全体の {1:F1}% を占めています。特定カテゴリの作業比率が高いため、意識的に他の作業とのバランスを取ることをお勧めします。\r\n", topCategory.label, percentage);
                    } else {
                        insights += string.Format("• [傾向]  「{0}」の時間は全体の {1:F1}% を占めています。このカテゴリの時間を調整したい場合、タイムブロッキングなどの手法が有効です。\r\n", topCategory.label, percentage);
                    }
                }
            }

            var incompleteTasks = _allTasks.Where(t => t.進捗度 != "完了済み" && !string.IsNullOrEmpty(t.保存日付)).ToList();
            int longTermTaskCount = 0;
            int mediumTermTaskCount = 0;
            foreach(var t in incompleteTasks) {
                DateTime savedDate;
                if (DateTime.TryParse(t.保存日付, out savedDate)) {
                    double elapsed = (DateTime.Today - savedDate.Date).TotalDays;
                    if (elapsed >= taskLongTermDays) {
                        longTermTaskCount++;
                    } else if (elapsed >= taskMediumTermDays) {
                        mediumTermTaskCount++;
                    }
                }
            }
            if (longTermTaskCount > 0) {
                insights += string.Format("• [警告]  作成から {0} 日以上経過している「長期停滞タスク」が {1} 件あります。不要なタスクの削除や、完了条件の見直しを行ってください。\r\n", taskLongTermDays, longTermTaskCount);
            }
            else if (mediumTermTaskCount > 0) {
                insights += string.Format("• [注意]  作成から {0} 日以上経過している「やや遅いタスク」が {1} 件あります。優先度を見直してください。\r\n", taskMediumTermDays, mediumTermTaskCount);
            }

            if (_chartSpeedCategory.Series.Count > 0 && _chartSpeedCategory.Series[0].Points.Count > 0) {
                var spCat = _chartSpeedCategory.Series[0].Points.Select(p => new ChartData { label = p.AxisLabel, value = p.YValues[0] }).ToList();
                var slowCategories = spCat.Where(c => c.value >= categorySlowDays).ToList();
                if (slowCategories.Any()) {
                    string slowCatNames = string.Join("、", slowCategories.Select(c => c.label));
                    insights += string.Format("• [カテゴリ停滞]  以下のカテゴリは、平均完了日数が {0} 日を超えており停滞傾向にあります: {1}\r\n", categorySlowDays, slowCatNames);
                } else if (spCat.All(c => c.value < taskEfficientDays)) {
                    insights += string.Format("• [効率的]  すべてのカテゴリの平均完了日数が{0}日未満であり、非常に効率的にタスクが消化されています！\r\n", taskEfficientDays);
                }

                int zeroDayCount = _allTasks.Count(t => {
                    DateTime comp, saved;
                    return t.進捗度 == "完了済み" && DateTime.TryParse(t.完了日, out comp) && DateTime.TryParse(t.保存日付, out saved) && (comp - saved).TotalDays == 0;
                });
                if (zeroDayCount >= zeroDayTaskCount) {
                    insights += string.Format("• [指摘]  登録したその日に完了したタスクが {0} 件あります。タスクの粒度が細かい、または事前計画を行わずに着手している可能性があります。\r\n", zeroDayCount);
                }
                if (spCat.Any()) {
                    var avgCt = spCat.Average(c => c.value);
                    insights += string.Format("• [分析]  タスク着手から完了(実質サイクルタイム)の全体平均は {0:F1} 日です。\r\n", avgCt);
                    var fastestCat = spCat.OrderBy(c => c.value).First();
                    insights += string.Format("  - 最も早く完了する傾向にあるのは「{0}」({1:F1} 日) です。\r\n", fastestCat.label, fastestCat.value);
                }
            }

            insights += "\r\n🚀 改善のためのヒント\r\n";
            insights += "• [工夫]「ポモドーロ・テクニック」: 大きなタスクに着手する際は、25分集中＋5分休憩のサイクルで区切ると、集中力が持続しやすくなります。\r\n";
            insights += "• [工夫]「タイムブロッキング」: 意図的に時間を確保したいカテゴリがあれば、カレンダー上でそのタスク専用の時間をあらかじめブロックする手法も有効です。\r\n";
            _txtInsights.Text = insights.Trim();
            _txtInsights.SelectionStart = 0;
            _txtInsights.ScrollToCaret();
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog { Filter = "HTMLファイル|*.html", FileName = "ReportSummary_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    // グラフ画像をBase64文字列に変換するヘルパー関数
                    Func<Chart, string> getChartImage = (chart) => {
                        using (var ms = new MemoryStream()) {
                            chart.SaveImage(ms, ChartImageFormat.Png);
                            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                        }
                    };

                    var sb = new StringBuilder();
                    sb.AppendLine("<!DOCTYPE html>");
                    sb.AppendLine("<html lang=\"ja\"><head><meta charset=\"UTF-8\"><title>分析レポートサマリー</title>");
                    sb.AppendLine("<style>");
                    sb.AppendLine("body { font-family: 'Segoe UI', 'Meiryo UI', sans-serif; background-color: #f0f2f5; color: #333; margin: 0; padding: 20px; }");
                    sb.AppendLine(".container { max-width: 1200px; margin: auto; }");
                    sb.AppendLine("h1 { text-align: center; color: #2c3e50; margin-bottom: 5px; }");
                    sb.AppendLine(".subtitle { text-align: center; color: #7f8c8d; margin-bottom: 30px; }");
                    sb.AppendLine(".card { background: white; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.05); padding: 20px; margin-bottom: 30px; }");
                    sb.AppendLine(".card h2 { margin-top: 0; color: #34495e; border-bottom: 2px solid #3498db; padding-bottom: 10px; margin-bottom: 20px; }");
                    sb.AppendLine("pre { background: #f8f9fa; padding: 15px; border-radius: 5px; white-space: pre-wrap; font-family: inherit; font-size: 15px; line-height: 1.6; border-left: 4px solid #3498db; margin: 0; }");
                    sb.AppendLine(".grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }");
                    sb.AppendLine(".grid-3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 20px; }");
                    sb.AppendLine(".col-stack { display: flex; flex-direction: column; gap: 15px; }");
                    sb.AppendLine(".chart-container { text-align: center; background: #fafafa; padding: 10px; border-radius: 6px; }");
                    sb.AppendLine(".chart-container img { max-width: 100%; height: auto; border: 1px solid #eaeaea; border-radius: 4px; background: white; }");
                    sb.AppendLine(".chart-title { font-weight: bold; margin-bottom: 10px; color: #555; font-size: 14px; }");
                    sb.AppendLine("</style>");
                    sb.AppendLine("</head><body>");
                    sb.AppendLine("<div class=\"container\">");
                    sb.AppendLine("<h1>タスクマネージャー 分析レポート</h1>");
                    sb.AppendLine(string.Format("<div class=\"subtitle\">出力日時: {0:yyyy/MM/dd HH:mm} &nbsp;|&nbsp; {1}</div>", DateTime.Now, _lblTotalTimeTotal.Text));

                    sb.AppendLine("<div class=\"card\"><h2>💡 分析インサイト</h2>");
                    sb.AppendLine(string.Format("<pre>{0}</pre></div>", System.Net.WebUtility.HtmlEncode(_txtInsights.Text)));

                    sb.AppendLine("<div class=\"card\"><h2>📈 日別推移</h2><div class=\"grid-3\">");
                    sb.AppendLine(string.Format("<div class=\"chart-container\"><div class=\"chart-title\">プロジェクト別</div><img src=\"{0}\" /></div>", getChartImage(_chartDailyProject)));
                    sb.AppendLine(string.Format("<div class=\"chart-container\"><div class=\"chart-title\">カテゴリ別</div><img src=\"{0}\" /></div>", getChartImage(_chartDailyCategory)));
                    sb.AppendLine(string.Format("<div class=\"chart-container\"><div class=\"chart-title\">ステータス別</div><img src=\"{0}\" /></div>", getChartImage(_chartDailyStatus)));
                    sb.AppendLine("</div></div>");

                    sb.AppendLine("<div class=\"card\"><h2>📊 合計時間</h2><div class=\"grid-3\">");
                    sb.AppendLine(string.Format("<div class=\"col-stack\"><div class=\"chart-container\"><div class=\"chart-title\">プロジェクト別 (円)</div><img src=\"{0}\" /></div><div class=\"chart-container\"><div class=\"chart-title\">プロジェクト別 (棒)</div><img src=\"{1}\" /></div></div>", getChartImage(_chartTotalProjectPie), getChartImage(_chartTotalProjectBar)));
                    sb.AppendLine(string.Format("<div class=\"col-stack\"><div class=\"chart-container\"><div class=\"chart-title\">カテゴリ別 (円)</div><img src=\"{0}\" /></div><div class=\"chart-container\"><div class=\"chart-title\">カテゴリ別 (棒)</div><img src=\"{1}\" /></div></div>", getChartImage(_chartTotalCategoryPie), getChartImage(_chartTotalCategoryBar)));
                    sb.AppendLine(string.Format("<div class=\"col-stack\"><div class=\"chart-container\"><div class=\"chart-title\">ステータス別 (円)</div><img src=\"{0}\" /></div><div class=\"chart-container\"><div class=\"chart-title\">ステータス別 (棒)</div><img src=\"{1}\" /></div></div>", getChartImage(_chartTotalStatusPie), getChartImage(_chartTotalStatusBar)));
                    sb.AppendLine("</div></div>");

                    sb.AppendLine("<div class=\"card\"><h2>⏱️ 完了スピード (実質日数)</h2><div class=\"grid-2\">");
                    sb.AppendLine(string.Format("<div class=\"chart-container\"><div class=\"chart-title\">プロジェクト別 平均完了日数</div><img src=\"{0}\" /></div>", getChartImage(_chartSpeedProject)));
                    sb.AppendLine(string.Format("<div class=\"chart-container\"><div class=\"chart-title\">カテゴリ別 平均完了日数</div><img src=\"{0}\" /></div>", getChartImage(_chartSpeedCategory)));
                    sb.AppendLine("</div></div></div></body></html>");

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    
                    if (MessageBox.Show("レポートをHTML形式で保存しました。\n今すぐブラウザで開いて確認しますか？", "完了", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start(sfd.FileName);
                    }
                }
            }
        }
    }
}
