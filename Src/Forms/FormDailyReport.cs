﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using UniConsul.Models;
using UniConsul.Services;

namespace UniConsul.Forms
{
    public class FormDailyReport : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private DataService _dataService;
        private List<TimeLog> _timeLogs;
        private List<TaskItem> _tasks;
        private List<ProjectItem> _projects;
        private Dictionary<string, List<EventItem>> _events;
        private Dictionary<string, string> _savedReports;
        private bool _isDarkMode;

        private DateTimePicker _dtpTargetDate;
        private TextBox _txtEvents, _txtCompletedTasks, _txtTotalTime, _txtCategory, _txtProject, _txtWorkDetails, _txtComments;

        public FormDailyReport(DataService dataService, List<TimeLog> timeLogs, List<TaskItem> tasks, List<ProjectItem> projects, Dictionary<string, List<EventItem>> events, bool isDarkMode)
        {
            _dataService = dataService;
            _timeLogs = timeLogs ?? new List<TimeLog>();
            _tasks = tasks ?? new List<TaskItem>();
            _projects = projects ?? new List<ProjectItem>();
            _events = events ?? new Dictionary<string, List<EventItem>>();
            _savedReports = _dataService.LoadFromJson<Dictionary<string, string>>(_dataService.DailyReportsFile, new Dictionary<string, string>());
            _isDarkMode = isDarkMode;

            InitializeComponent();
            ThemeManager.ApplyTheme(this, _isDarkMode);
            
            // 💡 テキスト入力枠の背景色を少し明るくして可読性を上げる
            if (_isDarkMode)
            {
                Color lighterDark = Color.FromArgb(45, 45, 48);
                _txtEvents.BackColor = lighterDark;
                _txtCompletedTasks.BackColor = lighterDark;
                _txtTotalTime.BackColor = lighterDark;
                _txtCategory.BackColor = lighterDark;
                _txtProject.BackColor = lighterDark;
                _txtWorkDetails.BackColor = lighterDark;
                _txtComments.BackColor = lighterDark;
            }
            
            LoadReport();
            
            var settings = _dataService.LoadSettings();
            if (settings != null && settings.WindowSizes != null && settings.WindowSizes.ContainsKey(this.Name)) {
                var parts = settings.WindowSizes[this.Name].Split(',');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) this.Size = new Size(Math.Max(300, w), Math.Max(200, h));
            }

            ThemeManager.EnableDynamicResizing(this, settings, () => _dataService.SaveToJson(_dataService.SettingsFile, settings));

            UniConsul.Utils.IconHelper.SetAppIcon(this);
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
            this.Name = "FormDailyReport";
            this.Text = "日報の出力";
            this.Size = new Size(580, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(10) };
            topPanel.Controls.Add(new Label { Text = "対象日:", Location = new Point(10, 15), AutoSize = true });
            
            _dtpTargetDate = new DateTimePicker { Location = new Point(65, 12), Width = 110, Format = DateTimePickerFormat.Short };
            _dtpTargetDate.ValueChanged += (s, e) => LoadReport();
            topPanel.Controls.Add(_dtpTargetDate);

            var btnSave = new Button { Text = "保存", Location = new Point(185, 10), Width = 70, Height = 30 };
            btnSave.Click += BtnSave_Click;
            topPanel.Controls.Add(btnSave);

            var btnRegenerate = new Button { Text = "自動生成", Location = new Point(260, 10), Width = 80, Height = 30 };
            btnRegenerate.Click += BtnRegenerate_Click;
            topPanel.Controls.Add(btnRegenerate);

            var btnCopy = new Button { Text = "コピー", Location = new Point(345, 10), Width = 70, Height = 30 };
            btnCopy.Click += BtnCopy_Click;
            topPanel.Controls.Add(btnCopy);

            var btnClose = new Button { Text = "閉じる", Location = new Point(420, 10), Width = 80, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            topPanel.Controls.Add(btnClose);

            this.Controls.Add(topPanel);

            var scrollPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            scrollPanel.AutoScrollMargin = new Size(0, 30); // 💡 一番下が見切れないようにスクロール下部に余白を追加
            this.Controls.Add(scrollPanel);
            scrollPanel.BringToFront(); // 💡 入力領域を一番手前に持ってくる（上部と被らないようにする）

            _txtEvents = CreateSection(scrollPanel, "■ 本日のイベント:");
            _txtCompletedTasks = CreateSection(scrollPanel, "■ 完了したタスク:");
            _txtTotalTime = CreateSection(scrollPanel, "■ 総作業時間:");
            _txtCategory = CreateSection(scrollPanel, "■ カテゴリ別内訳:");
            _txtProject = CreateSection(scrollPanel, "■ プロジェクト別内訳:");
            _txtWorkDetails = CreateSection(scrollPanel, "■ 実施した作業内容 (費やした時間):");
            _txtComments = CreateSection(scrollPanel, "■ 所感・コメント:");

            // 💡 確実に「所感・コメント」が隠れないよう、一番下に透明なスペーサーを置く
            var bottomSpacer = new Panel { Size = new Size(10, 40), BackColor = Color.Transparent };
            scrollPanel.Controls.Add(bottomSpacer);
            
            // リサイズ時にテキストボックスの幅を追従させる
            scrollPanel.Resize += (s, e) => {
                int w = scrollPanel.ClientSize.Width - 25;
                if (w > 0) {
                    _txtEvents.Width = w; _txtCompletedTasks.Width = w; _txtTotalTime.Width = w;
                    _txtCategory.Width = w; _txtProject.Width = w; _txtWorkDetails.Width = w; _txtComments.Width = w;
                }
            };

            this.CancelButton = btnClose;
        }

        private TextBox CreateSection(FlowLayoutPanel parent, string title)
        {
            var lbl = new Label { Text = title, AutoSize = true, Font = new Font("Meiryo UI", 9, FontStyle.Bold), Margin = new Padding(0, 10, 0, 5) };
            parent.Controls.Add(lbl);
            
            var txt = new TextBox { Multiline = true, Size = new Size(parent.ClientSize.Width - 25, 60), AcceptsReturn = true, Font = new Font("Meiryo UI", 9.5f), ScrollBars = ScrollBars.None };
            txt.TextChanged += (s, e) => {
                int padding = 10;
                Size sz = new Size(txt.ClientSize.Width, int.MaxValue);
                TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
                sz = TextRenderer.MeasureText(txt.Text + "\r\n", txt.Font, sz, flags);
                int newHeight = sz.Height + (txt.Height - txt.ClientSize.Height) + padding;
                if (newHeight < 40) newHeight = 40;
                if (txt.Height != newHeight) {
                    txt.Height = newHeight;
                    parent.PerformLayout(); // 高さの変更をスクロール領域の最大値に即座に反映させる
                }
            };
            
            parent.Controls.Add(txt);
            return txt;
        }

        private void LoadReport()
        {
            string dateStr = _dtpTargetDate.Value.ToString("yyyy-MM-dd");
            if (_savedReports.ContainsKey(dateStr))
            {
                ParseReportText(_savedReports[dateStr]);
            }
            else
            {
                GenerateReport();
            }
        }

        private void ParseReportText(string text)
        {
            _txtEvents.Text = ExtractSection(text, "■ 本日のイベント:", "■ 完了したタスク:");
            _txtCompletedTasks.Text = ExtractSection(text, "■ 完了したタスク:", "■ 総作業時間:");
            _txtTotalTime.Text = ExtractSection(text, "■ 総作業時間:", "■ カテゴリ別内訳:");
            _txtCategory.Text = ExtractSection(text, "■ カテゴリ別内訳:", "■ プロジェクト別内訳:");
            _txtProject.Text = ExtractSection(text, "■ プロジェクト別内訳:", "■ 実施した作業内容 (費やした時間):");
            _txtWorkDetails.Text = ExtractSection(text, "■ 実施した作業内容 (費やした時間):", "■ 所感・コメント:");
            _txtComments.Text = ExtractSection(text, "■ 所感・コメント:", null);
        }

        private string ExtractSection(string text, string startHeader, string endHeader)
        {
            int startIdx = text.IndexOf(startHeader);
            if (startIdx == -1) return "";
            startIdx += startHeader.Length;
            
            int endIdx = text.Length;
            if (endHeader != null)
            {
                int tempEnd = text.IndexOf(endHeader, startIdx);
                if (tempEnd != -1) endIdx = tempEnd;
            }
            
            return text.Substring(startIdx, endIdx - startIdx).Trim(new char[] { '\r', '\n', ' ' });
        }

        private void GenerateReport()
        {
            var targetDate = _dtpTargetDate.Value.Date;
            string dateStr = targetDate.ToString("yyyy-MM-dd");
            
            _txtEvents.Text = GenerateEventsText(dateStr);
            _txtCompletedTasks.Text = GenerateCompletedTasksText(dateStr);
            
            GenerateTimeLogSections(targetDate);

            _txtComments.Text = "";
        }

        private string GenerateEventsText(string dateStr)
        {
            var sb = new StringBuilder();
            if (_events.ContainsKey(dateStr) && _events[dateStr].Count > 0)
            {
                foreach (var ev in _events[dateStr].OrderBy(e => e.StartTime))
                {
                    string timeStr = "終日";
                    if (!ev.IsAllDay)
                    {
                        DateTime st, et;
                        bool hasStart = DateTime.TryParse(ev.StartTime, out st);
                        bool hasEnd = DateTime.TryParse(ev.EndTime, out et);
                        if (hasStart && hasEnd) timeStr = string.Format("{0:HH:mm} - {1:HH:mm}", st, et);
                        else if (hasStart) timeStr = st.ToString("HH:mm");
                    }
                    sb.AppendLine(string.Format("  - {0} ({1})", ev.Title, timeStr));
                }
            }
            else
            {
                sb.AppendLine("  (なし)");
            }
            return sb.ToString().TrimEnd();
        }

        private string GenerateCompletedTasksText(string dateStr)
        {
            var sb = new StringBuilder();
            var completedTasks = _tasks.Where(t => t.進捗度 == "完了済み" && t.完了日 == dateStr).ToList();
            if (completedTasks.Count > 0)
            {
                foreach (var t in completedTasks)
                {
                    string projName = "(未分類)";
                    var proj = _projects.FirstOrDefault(p => p.ProjectID == t.ProjectID);
                    if (proj != null) projName = proj.ProjectName;
                    sb.AppendLine(string.Format("  - {0} [{1}]", t.タスク, projName));
                }
            }
            else
            {
                sb.AppendLine("  (なし)");
            }
            return sb.ToString().TrimEnd();
        }

        private void GenerateTimeLogSections(DateTime targetDate)
        {
            var dailyLogs = _timeLogs.Where(l => 
                !string.IsNullOrEmpty(l.StartTime) && 
                !string.IsNullOrEmpty(l.EndTime) && 
                DateTime.Parse(l.StartTime).Date == targetDate
            ).ToList();

            double totalSeconds = 0;
            var categoryTime = new Dictionary<string, double>();
            var projectTime = new Dictionary<string, double>();
            var taskTime = new Dictionary<string, double>();

            foreach (var log in dailyLogs)
            {
                var st = DateTime.Parse(log.StartTime);
                var et = DateTime.Parse(log.EndTime);
                double sec = (et - st).TotalSeconds;
                totalSeconds += sec;

                string catName = "(未設定)";
                string projName = "(未分類)";

                var task = _tasks.FirstOrDefault(t => t.ID == log.TaskID);
                if (task != null)
                {
                    catName = string.IsNullOrEmpty(task.カテゴリ) ? "(未設定)" : task.カテゴリ;
                    var proj = _projects.FirstOrDefault(p => p.ProjectID == task.ProjectID);
                    if (proj != null && !string.IsNullOrEmpty(proj.ProjectName)) projName = proj.ProjectName;
                }

                if (!categoryTime.ContainsKey(catName)) categoryTime[catName] = 0;
                categoryTime[catName] += sec;

                if (!projectTime.ContainsKey(projName)) projectTime[projName] = 0;
                projectTime[projName] += sec;

                string taskNameOrMemo = !string.IsNullOrWhiteSpace(log.Memo) ? "[メモ] " + log.Memo : (task != null ? task.タスク : "");
                if (!string.IsNullOrEmpty(taskNameOrMemo)) {
                    if (!taskTime.ContainsKey(taskNameOrMemo)) taskTime[taskNameOrMemo] = 0;
                    taskTime[taskNameOrMemo] += sec;
                }
            }

            var tsTotal = TimeSpan.FromSeconds(totalSeconds);
            _txtTotalTime.Text = string.Format("{0}時間 {1}分", (int)tsTotal.TotalHours, tsTotal.Minutes);

            _txtCategory.Text = FormatDictionaryToText(categoryTime);
            _txtProject.Text = FormatDictionaryToText(projectTime);

            var sbWork = new StringBuilder();
            foreach (var kvp in taskTime.OrderByDescending(x => x.Value))
            {
                var ts = TimeSpan.FromSeconds(kvp.Value);
                sbWork.AppendLine(string.Format("  - {0} ({1}時間 {2}分)", kvp.Key, (int)ts.TotalHours, ts.Minutes));
            }
            _txtWorkDetails.Text = sbWork.ToString().TrimEnd();
        }

        private string FormatDictionaryToText(Dictionary<string, double> timeDict)
        {
            var sb = new StringBuilder();
            foreach (var kvp in timeDict.OrderByDescending(x => x.Value))
            {
                var ts = TimeSpan.FromSeconds(kvp.Value);
                sb.AppendLine(string.Format("  - {0}: {1}h {2}m", kvp.Key, (int)ts.TotalHours, ts.Minutes));
            }
            return sb.ToString().TrimEnd();
        }

        private string BuildReportString()
        {
            var targetDate = _dtpTargetDate.Value.Date;
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("【日報】 {0:yyyy年MM月dd日}", targetDate));
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine("■ 本日のイベント:");
            if (!string.IsNullOrWhiteSpace(_txtEvents.Text)) sb.AppendLine(_txtEvents.Text);
            sb.AppendLine();
            sb.AppendLine("■ 完了したタスク:");
            if (!string.IsNullOrWhiteSpace(_txtCompletedTasks.Text)) sb.AppendLine(_txtCompletedTasks.Text);
            sb.AppendLine();
            sb.AppendLine("■ 総作業時間: " + _txtTotalTime.Text.Trim());
            sb.AppendLine();
            sb.AppendLine("■ カテゴリ別内訳:");
            if (!string.IsNullOrWhiteSpace(_txtCategory.Text)) sb.AppendLine(_txtCategory.Text);
            sb.AppendLine();
            sb.AppendLine("■ プロジェクト別内訳:");
            if (!string.IsNullOrWhiteSpace(_txtProject.Text)) sb.AppendLine(_txtProject.Text);
            sb.AppendLine();
            sb.AppendLine("■ 実施した作業内容 (費やした時間):");
            if (!string.IsNullOrWhiteSpace(_txtWorkDetails.Text)) sb.AppendLine(_txtWorkDetails.Text);
            sb.AppendLine();
            sb.AppendLine("■ 所感・コメント:");
            if (!string.IsNullOrWhiteSpace(_txtComments.Text)) sb.AppendLine(_txtComments.Text);
            
            return sb.ToString();
        }


        private void BtnSave_Click(object sender, EventArgs e)
        {
            string dateStr = _dtpTargetDate.Value.ToString("yyyy-MM-dd");
            _savedReports[dateStr] = BuildReportString();
            _dataService.SaveToJson(_dataService.DailyReportsFile, _savedReports);
            MessageBox.Show("日報を保存しました。", "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnRegenerate_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("自動生成すると、現在編集中の内容は上書きされます。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                GenerateReport();
            }
        }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            string reportText = BuildReportString();
            if (!string.IsNullOrEmpty(reportText))
            {
                Clipboard.SetText(reportText);
                MessageBox.Show("クリップボードにコピーしました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
