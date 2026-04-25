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

        private FlowLayoutPanel _pnlCharts;
        private Button _btnExport;
        private DateTimePicker _dtpStart, _dtpEnd;
        private Label _lblTotalTime;
        private TextBox _txtInsights;
        private SplitContainer _mainSplitContainer;

        // --- グラフ描画・集計用のデータクラス ---
        // これを定義することで、後から別のメソッドにデータを渡しやすくなります。
        private class ChartData
        {
            public string label { get; set; }
            public double value { get; set; }
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
            
            // ウィンドウサイズの自由変更と記憶を有効化
            ThemeManager.EnableDynamicResizing(this, _settings, () => _dataService.SaveToJson(_dataService.SettingsFile, _settings), _mainSplitContainer);
            
            GenerateReport();
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

        private void ApplyDarkCalendar(DateTimePicker dtp)
        {
            if (!_isDarkMode || Environment.OSVersion.Version.Major < 10) return;
            dtp.DropDown += (s, ev) => {
                IntPtr hMonthCal = SendMessage(dtp.Handle, 0x1008, IntPtr.Zero, IntPtr.Zero); // DTM_GETMONTHCAL
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
        }

        private void InitializeComponent()
        {
            this.Text = "レポート分析";
            this.Size = new Size(1200, 850);
            this.StartPosition = FormStartPosition.CenterParent;

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 40 };
            this.Controls.Add(pnlTop);

            pnlTop.Controls.Add(new Label { Text = "開始日:", Location = new Point(10, 12), AutoSize = true });
            _dtpStart = new DateTimePicker { Location = new Point(60, 10), Width = 120, Format = DateTimePickerFormat.Short };
            ApplyDarkCalendar(_dtpStart);
            pnlTop.Controls.Add(_dtpStart);

            pnlTop.Controls.Add(new Label { Text = "終了日:", Location = new Point(190, 12), AutoSize = true });
            _dtpEnd = new DateTimePicker { Location = new Point(240, 10), Width = 120, Format = DateTimePickerFormat.Short };
            ApplyDarkCalendar(_dtpEnd);
            pnlTop.Controls.Add(_dtpEnd);

            var btnGenerate = new Button { Text = "レポート生成", Location = new Point(380, 8), Size = new Size(100, 28) };
            btnGenerate.Click += (s, e) => GenerateReport();
            pnlTop.Controls.Add(btnGenerate);

            _btnExport = new Button { Text = "HTMLへエクスポート", Location = new Point(490, 8), Size = new Size(130, 28) };
            _btnExport.Click += BtnExport_Click;
            pnlTop.Controls.Add(_btnExport);

            _lblTotalTime = new Label { Text = "総時間: 0時間0分", Location = new Point(630, 12), AutoSize = true, Font = new Font("Meiryo UI", 10, FontStyle.Bold) };
            pnlTop.Controls.Add(_lblTotalTime);

            _mainSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 650
            };
            this.Controls.Add(_mainSplitContainer);
            _mainSplitContainer.BringToFront();

            // WebBrowserの代わりにネイティブなパネルを使用
            _pnlCharts = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
            _pnlCharts.BackColor = _isDarkMode ? Color.FromArgb(30, 30, 30) : Color.White;
            _mainSplitContainer.Panel1.Controls.Add(_pnlCharts);

            var grpInsights = new GroupBox { Text = "💡 分析とアドバイス", Dock = DockStyle.Fill, Padding = new Padding(10) };
            _mainSplitContainer.Panel2.Controls.Add(grpInsights);

            _txtInsights = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Meiryo UI", 10) };
            grpInsights.Controls.Add(_txtInsights);

            if (_allTimeLogs.Any())
            {
                var earliestItem = _allTimeLogs.Where(l => !string.IsNullOrEmpty(l.StartTime)).OrderBy(l => l.StartTime).FirstOrDefault();
                string earliestStr = earliestItem != null ? earliestItem.StartTime : null;
                DateTime earliest;
                if (!string.IsNullOrEmpty(earliestStr) && DateTime.TryParse(earliestStr, out earliest))
                    _dtpStart.Value = earliest.Date;
                else
                    _dtpStart.Value = DateTime.Today.AddDays(-30);
            }
            else
            {
                _dtpStart.Value = DateTime.Today.AddDays(-30);
            }
            _dtpEnd.Value = DateTime.Today;
        }

        private void GenerateReport()
        {
            var startDate = _dtpStart.Value.Date;
            var endDate = _dtpEnd.Value.Date.AddDays(1).AddSeconds(-1);

            var filteredLogs = _allTimeLogs.Where(l =>
                !string.IsNullOrEmpty(l.StartTime) && !string.IsNullOrEmpty(l.EndTime) &&
                DateTime.Parse(l.StartTime) <= endDate && DateTime.Parse(l.EndTime) >= startDate
            ).ToList();

            var dataForCharts = new List<dynamic>();
            double totalHours = 0;

            foreach (var log in filteredLogs)
            {
                var task = _allTasks.FirstOrDefault(t => t.ID == log.TaskID);
                if (task != null)
                {
                    var proj = _projects.FirstOrDefault(p => p.ProjectID == task.ProjectID);
                    double hours = (DateTime.Parse(log.EndTime) - DateTime.Parse(log.StartTime)).TotalHours;
                    totalHours += hours;

                    dataForCharts.Add(new
                    {
                        ProjectName = proj != null ? proj.ProjectName : "(未分類)",
                        Category = string.IsNullOrEmpty(task.カテゴリ) ? "(未分類)" : task.カテゴリ,
                        Status = string.IsNullOrEmpty(task.進捗度) ? "(未設定)" : task.進捗度,
                        Date = DateTime.Parse(log.StartTime).ToString("yyyy-MM-dd"),
                        Hours = hours
                    });
                }
            }

            int tMin = (int)(totalHours * 60);
            _lblTotalTime.Text = string.Format("総時間: {0}時間{1}分", tMin / 60, tMin % 60);

            var projectSummary = dataForCharts.GroupBy(d => d.ProjectName).Select(g => new ChartData { label = g.Key, value = g.Sum(x => (double)x.Hours) }).OrderByDescending(x => x.value).ToList();
            var categorySummary = dataForCharts.GroupBy(d => d.Category).Select(g => new ChartData { label = g.Key, value = g.Sum(x => (double)x.Hours) }).OrderByDescending(x => x.value).ToList();
            var statusSummary = dataForCharts.GroupBy(d => d.Status).Select(g => new ChartData { label = g.Key, value = g.Sum(x => (double)x.Hours) }).OrderByDescending(x => x.value).ToList();
            var trendData = dataForCharts.GroupBy(d => new { d.Date, d.Category }).Select(g => (dynamic)new { date = g.Key.Date, category = g.Key.Category, value = g.Sum(x => (double)x.Hours) }).ToList();

            var spProj = new List<ChartData>();
            var spCat = new List<ChartData>();
            
            var completedTasks = _allTasks.Where(t => t.進捗度 == "完了済み" && !string.IsNullOrEmpty(t.完了日) && !string.IsNullOrEmpty(t.保存日付)).ToList();
            if (completedTasks.Any())
            {
                var statusLogs = _dataService.LoadFromJson<List<StatusLog>>(_dataService.StatusLogsFile, new List<StatusLog>());
                var logsByTask = statusLogs.GroupBy(l => l.TaskID).ToDictionary(g => g.Key, g => g.OrderBy(l => DateTime.Parse(l.Timestamp)).ToList());
                var excludeStatuses = (_settings != null && _settings.LeadTimeExcludeStatuses != null) ? _settings.LeadTimeExcludeStatuses : new List<string>();

                var speeds = completedTasks.Select(t => {
                    DateTime comp, saved;
                    if (DateTime.TryParse(t.完了日, out comp) && DateTime.TryParse(t.保存日付, out saved))
                    {
                        TimeSpan totalDuration = comp - saved;
                        TimeSpan excludedDuration = TimeSpan.Zero;

                        if (excludeStatuses.Any() && logsByTask.ContainsKey(t.ID))
                        {
                            var taskLogs = logsByTask[t.ID];
                            string currentStatus = taskLogs.Count > 0 && !string.IsNullOrEmpty(taskLogs[0].OldStatus) ? taskLogs[0].OldStatus : "未実施";
                            DateTime lastTime = saved;

                            foreach (var log in taskLogs)
                            {
                                DateTime logTime;
                                if (!DateTime.TryParse(log.Timestamp, out logTime)) continue;
                                
                                if (logTime < saved) logTime = saved;
                                if (logTime > comp) logTime = comp;

                                if (logTime > lastTime && excludeStatuses.Contains(currentStatus))
                                    excludedDuration += (logTime - lastTime);

                                currentStatus = log.NewStatus;
                                lastTime = logTime;

                                if (lastTime >= comp) break;
                            }

                            if (lastTime < comp && excludeStatuses.Contains(currentStatus))
                                excludedDuration += (comp - lastTime);
                        }

                        double days = (totalDuration - excludedDuration).TotalDays;
                        return new { t.ProjectID, t.カテゴリ, Days = Math.Max(0, days) };
                    }
                    return null;
                }).Where(x => x != null).ToList();

                spProj = speeds.GroupBy(x => x.ProjectID).Select(g => {
                    var p = _projects.FirstOrDefault(proj => proj.ProjectID == g.Key);
                    return new ChartData { label = p != null ? p.ProjectName : "(未分類)", value = g.Average(x => x.Days) };
                }).OrderByDescending(x => x.value).ToList();

                spCat = speeds.Where(x => !string.IsNullOrEmpty(x.カテゴリ)).GroupBy(x => x.カテゴリ).Select(g => new ChartData { label = g.Key, value = g.Average(x => x.Days) }).OrderByDescending(x => x.value).ToList();
            }

            // インサイトの生成
            string insights = "💡 詳細分析コメント (インサイト)\r\n";
            
            int warnPercent = _settings != null && _settings.AnalysisWarnPercent > 0 ? _settings.AnalysisWarnPercent : 40;
            int unclassifiedWarningPercent = 15;
            int taskLongTermDays = 60;
            int categorySlowDays = 30;

            if (categorySummary.Count > 1) {
                insights += "• [良好]  時間の分類が適切に行われています。時間の使い方の内訳が明確で、振り返りやすい状態です。\r\n";
            } else {
                insights += "• [情報]  記録された時間のカテゴリが1つのみです。複数のカテゴリに分類すると、時間の使い方をより詳細に分析できます。\r\n";
            }

            if (totalHours > 0) {
                var unclassifiedProj = projectSummary.FirstOrDefault(p => p.label == "(未分類)");
                if (unclassifiedProj != null) {
                    double unclassProjPct = (unclassifiedProj.value / totalHours) * 100;
                    if (unclassProjPct >= warnPercent) {
                        insights += string.Format("• [警告]  「(未分類)」のプロジェクト時間が全体の {0:F1}% を占め、警告基準（{1}%）を超過しています。作業の目的や紐付けを見直すことをお勧めします。\r\n", unclassProjPct, warnPercent);
                    } else if (unclassProjPct >= unclassifiedWarningPercent) {
                        insights += string.Format("• [注意]  「(未分類)」のプロジェクト時間が全体の {0:F1}% を占めています。タスクを適切なプロジェクトに分類することで、振り返りの精度が上がります。\r\n", unclassProjPct);
                    }
                }

                var unclassifiedCat = categorySummary.FirstOrDefault(c => c.label == "(未分類)");
                if (unclassifiedCat != null) {
                    double unclassCatPct = (unclassifiedCat.value / totalHours) * 100;
                    if (unclassCatPct >= warnPercent) {
                        insights += string.Format("• [警告]  「(未分類)」のカテゴリ時間が全体の {0:F1}% に達しています。カテゴリ分けのルールを見直してください。\r\n", unclassCatPct);
                    }
                }

                var topProject = projectSummary.FirstOrDefault(p => p.label != "(未分類)");
                if (topProject != null) {
                    double projPercentage = (topProject.value / totalHours) * 100;
                    if (projPercentage >= warnPercent) {
                        insights += string.Format("• [警告]  プロジェクト「{0}」の作業時間が全体の {1:F1}% を占め、警告基準（{2}%）を超過しています。負荷が集中している可能性があるため、リソース配分を見直してください。\r\n", topProject.label, projPercentage, warnPercent);
                    }
                }

                var topCategory = categorySummary.FirstOrDefault(c => c.label != "(未分類)");
                if (topCategory != null) {
                    double percentage = (topCategory.value / totalHours) * 100;
                    if (percentage >= warnPercent) {
                        insights += string.Format("• [注意]  カテゴリ「{0}」の時間が全体の {1:F1}% を占めています。特定カテゴリの作業比率が高いため、意識的に他の作業とのバランスを取ることをお勧めします。\r\n", topCategory.label, percentage);
                    } else {
                        insights += string.Format("• [傾向]  「{0}」の時間は全体の {1:F1}% を占めています。このカテゴリの時間を調整したい場合、タイムブロッキングなどの手法が有効です。\r\n", topCategory.label, percentage);
                    }
                }
            }

            // タスク効率の分析（長期停滞タスクなど）
            var incompleteTasks = _allTasks.Where(t => t.進捗度 != "完了済み" && !string.IsNullOrEmpty(t.保存日付)).ToList();
            int longTermTaskCount = 0;
            foreach(var t in incompleteTasks) {
                DateTime savedDate;
                if (DateTime.TryParse(t.保存日付, out savedDate)) {
                    if ((DateTime.Today - savedDate.Date).TotalDays >= taskLongTermDays) {
                        longTermTaskCount++;
                    }
                }
            }
            if (longTermTaskCount > 0) {
                insights += string.Format("• [警告]  作成から {0} 日以上経過している「長期停滞タスク」が {1} 件あります。不要なタスクの削除や、完了条件の見直しを行ってください。\r\n", taskLongTermDays, longTermTaskCount);
            }

            // カテゴリの完了スピード分析
            if (completedTasks.Any()) {
                var slowCategories = spCat.Where(c => c.value >= categorySlowDays).ToList();
                if (slowCategories.Any()) {
                    string slowCatNames = string.Join("、", slowCategories.Select(c => c.label));
                    insights += string.Format("• [注意]  以下のカテゴリは、平均完了日数が {0} 日を超えており停滞傾向にあります: {1}\r\n", categorySlowDays, slowCatNames);
                } else if (spCat.All(c => c.value < 14)) {
                    insights += "• [良好]  すべてのカテゴリの平均完了日数が14日未満であり、非常に効率的にタスクが消化されています！\r\n";
                }
            }

            insights += "\r\n🚀 改善のためのヒント\r\n";
            insights += "• [工夫]「ポモドーロ・テクニック」: 大きなタスクに着手する際は、25分集中＋5分休憩のサイクルで区切ると、集中力が持続しやすくなります。\r\n";
            insights += "• [工夫]「タイムブロッキング」: 意図的に時間を確保したいカテゴリがあれば、カレンダー上でそのタスク専用の時間をあらかじめブロックする手法も有効です。\r\n";
            _txtInsights.Text = insights.Trim();

            // --- ネイティブチャートの描画 ---
            _pnlCharts.Controls.Clear();

            if (trendData.Any()) _pnlCharts.Controls.Add(CreateStackedBarChart("日別作業時間の推移 (カテゴリ別)", trendData));
            if (projectSummary.Any()) _pnlCharts.Controls.Add(CreatePieChart("プロジェクト別 (h)", projectSummary));
            if (categorySummary.Any()) _pnlCharts.Controls.Add(CreatePieChart("カテゴリ別 (h)", categorySummary));
            if (statusSummary.Any()) _pnlCharts.Controls.Add(CreatePieChart("ステータス別 (h)", statusSummary));
            if (spProj.Any()) _pnlCharts.Controls.Add(CreateBarChart("プロジェクト別 平均完了日数", spProj));
            if (spCat.Any()) _pnlCharts.Controls.Add(CreateBarChart("カテゴリ別 平均完了日数", spCat));
        }

        private Chart CreatePieChart(string title, List<ChartData> data)
        {
            var chart = new Chart { Size = new Size(300, 250), BackColor = Color.Transparent };
            var area = new ChartArea { BackColor = Color.Transparent };
            chart.ChartAreas.Add(area);
            chart.Titles.Add(new Title(title) { ForeColor = _isDarkMode ? Color.White : Color.Black, Font = new Font("Meiryo UI", 10, FontStyle.Bold) });
            chart.Legends.Add(new Legend { BackColor = Color.Transparent, ForeColor = _isDarkMode ? Color.White : Color.Black });

            var series = new Series(title) { ChartType = SeriesChartType.Pie };
            foreach (var d in data)
            {
                int pt = series.Points.AddY(d.value);
                series.Points[pt].LegendText = d.label;
                series.Points[pt].Label = "#PERCENT{P0}";
            }
            chart.Series.Add(series);
            return chart;
        }

        private Chart CreateBarChart(string title, List<ChartData> data)
        {
            var chart = new Chart { Size = new Size(400, 250), BackColor = Color.Transparent };
            var area = new ChartArea { BackColor = Color.Transparent };
            area.AxisX.LabelStyle.ForeColor = _isDarkMode ? Color.White : Color.Black;
            area.AxisY.LabelStyle.ForeColor = _isDarkMode ? Color.White : Color.Black;
            area.AxisX.LineColor = _isDarkMode ? Color.Gray : Color.LightGray;
            area.AxisY.LineColor = _isDarkMode ? Color.Gray : Color.LightGray;
            area.AxisX.MajorGrid.LineColor = _isDarkMode ? Color.FromArgb(60,60,60) : Color.LightGray;
            area.AxisY.MajorGrid.LineColor = _isDarkMode ? Color.FromArgb(60,60,60) : Color.LightGray;
            area.AxisX.Interval = 1;
            chart.ChartAreas.Add(area);
            
            chart.Titles.Add(new Title(title) { ForeColor = _isDarkMode ? Color.White : Color.Black, Font = new Font("Meiryo UI", 10, FontStyle.Bold) });
            var series = new Series(title) { ChartType = SeriesChartType.Column, IsVisibleInLegend = false, Color = Color.FromArgb(52, 152, 219) };
            foreach (var d in data) series.Points.AddXY(d.label, d.value);
            chart.Series.Add(series);
            return chart;
        }

        private Chart CreateStackedBarChart(string title, List<dynamic> trendData)
        {
            var chart = new Chart { Size = new Size(800, 300), BackColor = Color.Transparent };
            var area = new ChartArea { BackColor = Color.Transparent };
            area.AxisX.LabelStyle.ForeColor = _isDarkMode ? Color.White : Color.Black;
            area.AxisY.LabelStyle.ForeColor = _isDarkMode ? Color.White : Color.Black;
            area.AxisX.LineColor = _isDarkMode ? Color.Gray : Color.LightGray;
            area.AxisY.LineColor = _isDarkMode ? Color.Gray : Color.LightGray;
            area.AxisX.MajorGrid.LineColor = _isDarkMode ? Color.FromArgb(60,60,60) : Color.LightGray;
            area.AxisY.MajorGrid.LineColor = _isDarkMode ? Color.FromArgb(60,60,60) : Color.LightGray;
            chart.ChartAreas.Add(area);
            chart.Titles.Add(new Title(title) { ForeColor = _isDarkMode ? Color.White : Color.Black, Font = new Font("Meiryo UI", 10, FontStyle.Bold) });
            chart.Legends.Add(new Legend { BackColor = Color.Transparent, ForeColor = _isDarkMode ? Color.White : Color.Black });

            var dates = trendData.Select(d => (string)d.date).Distinct().OrderBy(d => d).ToList();
            var categories = trendData.Select(d => (string)d.category).Distinct().OrderBy(c => c).ToList();

            foreach (var cat in categories)
            {
                var series = new Series(cat) { ChartType = SeriesChartType.StackedColumn };
                foreach (var date in dates)
                {
                    var item = trendData.FirstOrDefault(d => d.date == date && d.category == cat);
                    double val = item != null ? (double)item.value : 0;
                    series.Points.AddXY(date, val);
                }
                chart.Series.Add(series);
            }
            return chart;
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            // HTMLファイルではなく、オフライン環境でも利用しやすいCSVテキストデータとしてエクスポートするように変更
            using (var sfd = new SaveFileDialog { Filter = "CSVファイル|*.csv", FileName = "ReportSummary_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("対象期間," + _dtpStart.Value.ToString("yyyy/MM/dd") + " - " + _dtpEnd.Value.ToString("yyyy/MM/dd"));
                    sb.AppendLine(_lblTotalTime.Text);
                    sb.AppendLine();
                    sb.AppendLine("--- 分析インサイト ---");
                    
                    // 改行を取り除いて、1行ずつCSVに出力
                    var lines = _txtInsights.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines) sb.AppendLine("\"" + line.Replace("\"", "\"\"") + "\"");

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("レポートのサマリーをCSV形式で保存しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }
}
