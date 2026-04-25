﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TaskManager.Models;

namespace TaskManager.Forms
{
    public class FormDailyReport : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private List<TimeLog> _timeLogs;
        private List<TaskItem> _tasks;
        private List<ProjectItem> _projects;
        private bool _isDarkMode;

        private DateTimePicker _dtpTargetDate;
        private TextBox _txtReportPreview;

        public FormDailyReport(List<TimeLog> timeLogs, List<TaskItem> tasks, List<ProjectItem> projects, bool isDarkMode)
        {
            _timeLogs = timeLogs ?? new List<TimeLog>();
            _tasks = tasks ?? new List<TaskItem>();
            _projects = projects ?? new List<ProjectItem>();
            _isDarkMode = isDarkMode;

            InitializeComponent();
            ThemeManager.ApplyTheme(this, _isDarkMode);
            GenerateReport();
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
            this.Size = new Size(500, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(10) };
            topPanel.Controls.Add(new Label { Text = "対象日:", Location = new Point(10, 15), AutoSize = true });
            
            _dtpTargetDate = new DateTimePicker { Location = new Point(70, 12), Width = 120, Format = DateTimePickerFormat.Short };
            _dtpTargetDate.ValueChanged += (s, e) => GenerateReport();
            topPanel.Controls.Add(_dtpTargetDate);

            var btnCopy = new Button { Text = "クリップボードにコピー", Location = new Point(200, 10), Width = 150, Height = 30 };
            btnCopy.Click += BtnCopy_Click;
            topPanel.Controls.Add(btnCopy);

            var btnClose = new Button { Text = "閉じる", Location = new Point(360, 10), Width = 100, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            topPanel.Controls.Add(btnClose);

            this.Controls.Add(topPanel);

            _txtReportPreview = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = false,
                Font = new Font("Meiryo UI", 10)
            };
            
            var paddingPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            paddingPanel.Controls.Add(_txtReportPreview);
            this.Controls.Add(paddingPanel);

            this.CancelButton = btnClose;
        }

        private void GenerateReport()
        {
            var targetDate = _dtpTargetDate.Value.Date;
            
            var dailyLogs = _timeLogs.Where(l => 
                !string.IsNullOrEmpty(l.StartTime) && 
                !string.IsNullOrEmpty(l.EndTime) && 
                DateTime.Parse(l.StartTime).Date == targetDate
            ).ToList();

            double totalSeconds = 0;
            var categoryTime = new Dictionary<string, double>();
            var projectTime = new Dictionary<string, double>();
            var comments = new List<string>();

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

                if (!string.IsNullOrWhiteSpace(log.Memo))
                {
                    comments.Add(log.Memo);
                }
                else if (task != null)
                {
                    comments.Add(task.タスク);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Format("【日報】 {0:yyyy年MM月dd日}", targetDate));
            sb.AppendLine("--------------------------------------------------");
            
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

            sb.AppendLine("■ 実施した作業内容・メモ:");
            foreach (var comment in comments.Distinct())
            {
                sb.AppendLine(string.Format("  - {0}", comment));
            }

            _txtReportPreview.Text = sb.ToString();
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
