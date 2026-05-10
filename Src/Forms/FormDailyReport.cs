﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TaskManager.Models;
using TaskManager.Services;

namespace TaskManager.Forms
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
        private TextBox _txtReportPreview;

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
            LoadReport();
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
            this.Text = "日報の出力";
            this.Size = new Size(580, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;

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

            _txtReportPreview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = false,
                AcceptsReturn = true,
                Font = new Font("Meiryo UI", 10)
            };
            
            var paddingPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            paddingPanel.Controls.Add(_txtReportPreview);
            this.Controls.Add(paddingPanel);

            this.CancelButton = btnClose;
        }

        private void LoadReport()
        {
            string dateStr = _dtpTargetDate.Value.ToString("yyyy-MM-dd");
            if (_savedReports.ContainsKey(dateStr))
            {
                _txtReportPreview.Text = _savedReports[dateStr];
                _txtReportPreview.SelectionStart = 0;
                _txtReportPreview.ScrollToCaret();
            }
            else
            {
                GenerateReport();
            }
        }

        private void GenerateReport()
        {
            var targetDate = _dtpTargetDate.Value.Date;
            string dateStr = targetDate.ToString("yyyy-MM-dd");
            
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("【日報】 {0:yyyy年MM月dd日}", targetDate));
            sb.AppendLine("--------------------------------------------------");

            sb.AppendLine("■ 本日のイベント:");
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
            sb.AppendLine();

            sb.AppendLine("■ 完了したタスク:");
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
            sb.AppendLine();

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

                string taskNameOrMemo = "";
                if (!string.IsNullOrWhiteSpace(log.Memo))
                {
                    taskNameOrMemo = "[メモ] " + log.Memo;
                }
                else if (task != null)
                {
                    taskNameOrMemo = task.タスク;
                }
                if (!string.IsNullOrEmpty(taskNameOrMemo)) {
                    if (!taskTime.ContainsKey(taskNameOrMemo)) taskTime[taskNameOrMemo] = 0;
                    taskTime[taskNameOrMemo] += sec;
                }
            }

            var tsTotal = TimeSpan.FromSeconds(totalSeconds);
            sb.AppendLine(string.Format("■ 総作業時間: {0}時間 {1}分", (int)tsTotal.TotalHours, tsTotal.Minutes));
            sb.AppendLine();

            sb.AppendLine("■ カテゴリ別内訳:");
            foreach (var kvp in categoryTime.OrderByDescending(x => x.Value))
            {
                var ts = TimeSpan.FromSeconds(kvp.Value);
                sb.AppendLine(string.Format("  - {0}: {1}h {2}m", kvp.Key, (int)ts.TotalHours, ts.Minutes));
            }
            sb.AppendLine();

            sb.AppendLine("■ プロジェクト別内訳:");
            foreach (var kvp in projectTime.OrderByDescending(x => x.Value))
            {
                var ts = TimeSpan.FromSeconds(kvp.Value);
                sb.AppendLine(string.Format("  - {0}: {1}h {2}m", kvp.Key, (int)ts.TotalHours, ts.Minutes));
            }
            sb.AppendLine();

            sb.AppendLine("■ 実施した作業内容 (費やした時間):");
            foreach (var kvp in taskTime.OrderByDescending(x => x.Value))
            {
                var ts = TimeSpan.FromSeconds(kvp.Value);
                sb.AppendLine(string.Format("  - {0} ({1}時間 {2}分)", kvp.Key, (int)ts.TotalHours, ts.Minutes));
            }

            sb.AppendLine();
            sb.AppendLine("■ 所感・コメント:");
            sb.AppendLine();

            _txtReportPreview.Text = sb.ToString();
            _txtReportPreview.SelectionStart = 0;
            _txtReportPreview.ScrollToCaret();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string dateStr = _dtpTargetDate.Value.ToString("yyyy-MM-dd");
            _savedReports[dateStr] = _txtReportPreview.Text;
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
            if (!string.IsNullOrEmpty(_txtReportPreview.Text))
            {
                Clipboard.SetText(_txtReportPreview.Text);
                MessageBox.Show("クリップボードにコピーしました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
