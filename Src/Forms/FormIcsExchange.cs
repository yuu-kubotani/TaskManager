﻿﻿﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using TaskManager.Models;
using TaskManager.Services;

namespace TaskManager.Forms
{
    public class FormIcsExchange : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private DataService _dataService;
        private List<TaskItem> _tasks;
        private Dictionary<string, List<EventItem>> _events;
        private List<ProjectItem> _projects;

        private TextBox txtImportPath;
        private RadioButton radioSkip, radioOverwrite;
        private CheckBox chkImportAsHolidays;

        public FormIcsExchange(DataService dataService, List<TaskItem> tasks, Dictionary<string, List<EventItem>> events, List<ProjectItem> projects, bool isDarkMode)
        {
            _dataService = dataService;
            _tasks = tasks;
            _events = events;
            _projects = projects;

            this.Text = "ICS連携 (インポート/エクスポート)";
            this.Size = new Size(400, 370);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            var tabControl = new TabControl { Dock = DockStyle.Fill };
            var tabExport = new TabPage("エクスポート");
            tabControl.TabPages.Add(tabExport);
            this.Controls.Add(tabControl);

            var grpPeriod = new GroupBox { Text = "期間指定", Location = new Point(15, 15), Size = new Size(350, 60) };
            var radioAll = new RadioButton { Text = "全期間", Location = new Point(15, 25), AutoSize = true, Checked = true };
            grpPeriod.Controls.Add(radioAll);
            tabExport.Controls.Add(grpPeriod);

            var grpTarget = new GroupBox { Text = "出力対象", Location = new Point(15, 90), Size = new Size(350, 60) };
            var chkTasks = new CheckBox { Text = "タスク", Location = new Point(15, 25), AutoSize = true, Checked = true };
            var chkEvents = new CheckBox { Text = "イベント", Location = new Point(100, 25), AutoSize = true, Checked = true };
            grpTarget.Controls.AddRange(new Control[] { chkTasks, chkEvents });
            tabExport.Controls.Add(grpTarget);

            var btnExport = new Button { Text = "ICSファイルを保存", Location = new Point(80, 180), Size = new Size(220, 35) };
            btnExport.Click += (s, e) => ExportToIcs(chkTasks.Checked, chkEvents.Checked);
            tabExport.Controls.Add(btnExport);

            // --- インポートタブの追加 ---
            var tabImport = new TabPage("インポート");
            tabControl.TabPages.Add(tabImport);

            var grpFile = new GroupBox { Text = "ファイル選択", Location = new Point(15, 15), Size = new Size(350, 60) };
            txtImportPath = new TextBox { Location = new Point(15, 25), Size = new Size(240, 23) };
            var btnBrowse = new Button { Text = "参照", Location = new Point(265, 23), Size = new Size(70, 27) };
            btnBrowse.Click += (s, e) => {
                using (var ofd = new OpenFileDialog { Filter = "iCalendar ファイル (*.ics)|*.ics|すべてのファイル (*.*)|*.*" }) {
                    if (ofd.ShowDialog() == DialogResult.OK) txtImportPath.Text = ofd.FileName;
                }
            };
            grpFile.Controls.AddRange(new Control[] { txtImportPath, btnBrowse });
            tabImport.Controls.Add(grpFile);

            var grpConflict = new GroupBox { Text = "重複時の処理", Location = new Point(15, 90), Size = new Size(350, 60) };
            radioSkip = new RadioButton { Text = "スキップ（既存優先）", Location = new Point(15, 25), AutoSize = true, Checked = true };
            radioOverwrite = new RadioButton { Text = "上書き（インポート優先）", Location = new Point(150, 25), AutoSize = true };
            grpConflict.Controls.AddRange(new Control[] { radioSkip, radioOverwrite });
            tabImport.Controls.Add(grpConflict);

            chkImportAsHolidays = new CheckBox { Text = "祝日カレンダーとしてインポート (営業日計算に反映)", Location = new Point(15, 155), AutoSize = true };
            tabImport.Controls.Add(chkImportAsHolidays);

            var btnImport = new Button { Text = "インポート開始", Location = new Point(115, 185), Size = new Size(150, 35) };
            btnImport.Click += BtnImport_Click;
            tabImport.Controls.Add(btnImport);

            ThemeManager.ApplyTheme(this, isDarkMode);
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

        private void ExportToIcs(bool includeTasks, bool includeEvents)
        {
            using (var sfd = new SaveFileDialog { Filter = "iCalendar ファイル (*.ics)|*.ics", FileName = "exported_tasks.ics" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("BEGIN:VCALENDAR");
                    sb.AppendLine("VERSION:2.0");
                    sb.AppendLine("PRODID:-//TaskManager//NONSGML v1.0//EN");
                    string dtStamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");

                    if (includeTasks) {
                        foreach (var t in _tasks.Where(x => !string.IsNullOrEmpty(x.期日))) {
                            DateTime dt;
                            if (DateTime.TryParse(t.期日, out dt)) {
                                sb.AppendLine("BEGIN:VEVENT");
                                string safeTitle = (t.タスク ?? "").Replace("\r", "").Replace("\n", " ");
                                sb.AppendLine(string.Format("SUMMARY:{0}", safeTitle));
                                sb.AppendLine(string.Format("DTSTART;VALUE=DATE:{0:yyyyMMdd}", dt));
                                sb.AppendLine(string.Format("DTEND;VALUE=DATE:{0:yyyyMMdd}", dt.AddDays(1)));
                                sb.AppendLine(string.Format("UID:{0}\r\nDTSTAMP:{1}\r\nEND:VEVENT", t.ID, dtStamp));
                            }
                        }
                    }
                    if (includeEvents) {
                        foreach (var kvp in _events) {
                            foreach (var ev in kvp.Value.Where(x => !string.IsNullOrEmpty(x.StartTime))) {
                                DateTime dt;
                                if (DateTime.TryParse(ev.StartTime, out dt)) {
                                    sb.AppendLine("BEGIN:VEVENT");
                                    string safeTitle = (ev.Title ?? "").Replace("\r", "").Replace("\n", " ");
                                    sb.AppendLine(string.Format("SUMMARY:{0}", safeTitle));
                                    sb.AppendLine(string.Format("DTSTART;VALUE=DATE:{0:yyyyMMdd}", dt));
                                    sb.AppendLine(string.Format("DTEND;VALUE=DATE:{0:yyyyMMdd}", dt.AddDays(1)));
                                    sb.AppendLine(string.Format("UID:{0}\r\nDTSTAMP:{1}\r\nEND:VEVENT", ev.ID, dtStamp));
                                }
                            }
                        }
                    }
                    sb.AppendLine("END:VCALENDAR");
                    File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(false));
                    MessageBox.Show("エクスポートが完了しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void BtnImport_Click(object sender, EventArgs e)
        {
            string filePath = txtImportPath.Text;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) {
                MessageBox.Show("有効なファイルを選択してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try {
                string content = File.ReadAllText(filePath, Encoding.UTF8);
                content = Regex.Replace(content, "\r\n[ \t]", ""); // Unfolding

                bool importAsHolidays = chkImportAsHolidays != null && chkImportAsHolidays.Checked;
                string holidaysPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "holidays.json");
                Dictionary<string, string> holidays = null;
                if (importAsHolidays) {
                    holidays = _dataService.LoadFromJson<Dictionary<string, string>>(holidaysPath, new Dictionary<string, string>());
                }

                var matches = Regex.Matches(content, @"(?s)BEGIN:VEVENT(.*?)END:VEVENT");
                int countImported = 0, countSkipped = 0, countUpdated = 0;

                string defaultProjectId = "";
                var defaultProject = _projects.FirstOrDefault(p => p.ProjectName == "未分類");
                if (defaultProject != null) {
                    defaultProjectId = defaultProject.ProjectID;
                } else {
                    defaultProjectId = Guid.NewGuid().ToString();
                    _projects.Add(new ProjectItem { ProjectID = defaultProjectId, ProjectName = "未分類", AutoArchiveTasks = true });
                }

                foreach (Match match in matches) {
                    string block = match.Groups[1].Value;
                    string[] lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    var props = new Dictionary<string, string>();
                    foreach (var line in lines) {
                        var m = Regex.Match(line, @"^([^;:]+)(?:;[^:]*)?:(.*)$");
                        if (m.Success) {
                            string key = m.Groups[1].Value.ToUpper();
                            if (!props.ContainsKey(key)) props[key] = m.Groups[2].Value;
                        }
                    }

                    string uid = props.ContainsKey("UID") ? props["UID"] : Guid.NewGuid().ToString();
                    string summary = props.ContainsKey("SUMMARY") ? props["SUMMARY"] : "(No Title)";
                    string description = props.ContainsKey("DESCRIPTION") ? props["DESCRIPTION"].Replace("\\n", "\r\n") : "";

                    string dtStartStr = props.ContainsKey("DTSTART") ? props["DTSTART"] : null;
                    string dtEndStr = props.ContainsKey("DTEND") ? props["DTEND"] : null;
                    DateTime startTime = DateTime.MinValue, endTime = DateTime.MinValue;
                    bool isAllDay = false;

                    if (dtStartStr != null) {
                        if (Regex.IsMatch(dtStartStr, @"^\d{8}$")) { isAllDay = true; startTime = DateTime.ParseExact(dtStartStr, "yyyyMMdd", null); }
                        else if (Regex.IsMatch(dtStartStr, @"^\d{8}T\d{6}Z$")) { startTime = DateTime.ParseExact(dtStartStr, "yyyyMMddTHHmmssZ", null).ToLocalTime(); }
                        else if (Regex.IsMatch(dtStartStr, @"^\d{8}T\d{6}$")) { startTime = DateTime.ParseExact(dtStartStr, "yyyyMMddTHHmmss", null); }
                    }
                    if (dtEndStr != null) {
                        if (Regex.IsMatch(dtEndStr, @"^\d{8}$")) { endTime = DateTime.ParseExact(dtEndStr, "yyyyMMdd", null); }
                        else if (Regex.IsMatch(dtEndStr, @"^\d{8}T\d{6}Z$")) { endTime = DateTime.ParseExact(dtEndStr, "yyyyMMddTHHmmssZ", null).ToLocalTime(); }
                        else if (Regex.IsMatch(dtEndStr, @"^\d{8}T\d{6}$")) { endTime = DateTime.ParseExact(dtEndStr, "yyyyMMddTHHmmss", null); }
                    }
                    if (startTime == DateTime.MinValue) continue;
                    if (endTime == DateTime.MinValue) endTime = startTime.AddHours(1);

                    if (importAsHolidays) {
                        if (isAllDay) {
                            string dateKey = startTime.ToString("yyyy-MM-dd");
                            holidays[dateKey] = summary;
                            countImported++;
                        }
                        continue;
                    }

                    var existingTask = _tasks.FirstOrDefault(t => t.ID == uid);
                    EventItem existingEvent = null;
                    string existingEventDateKey = null;

                    if (existingTask == null) {
                        foreach (var kvp in _events) {
                            existingEvent = kvp.Value.FirstOrDefault(ev => ev.ID == uid);
                            if (existingEvent != null) { existingEventDateKey = kvp.Key; break; }
                        }
                    }

                    if (existingTask != null) {
                        if (radioSkip.Checked) { countSkipped++; continue; }
                        existingTask.タスク = summary;
                        existingTask.期日 = isAllDay ? startTime.ToString("yyyy-MM-dd") : startTime.ToString("yyyy-MM-dd HH:mm");
                        var catMatch = Regex.Match(description, @"カテゴリ:\s*(.*?)(\r\n|$)"); if (catMatch.Success) existingTask.カテゴリ = catMatch.Groups[1].Value.Trim();
                        var statMatch = Regex.Match(description, @"進捗:\s*(.*?)(\r\n|$)"); if (statMatch.Success) existingTask.進捗度 = statMatch.Groups[1].Value.Trim();
                        var prioMatch = Regex.Match(description, @"優先度:\s*(.*?)(\r\n|$)"); if (prioMatch.Success) existingTask.優先度 = prioMatch.Groups[1].Value.Trim();
                        countUpdated++;
                    } else if (existingEvent != null) {
                        if (radioSkip.Checked) { countSkipped++; continue; }
                        string newDateKey = startTime.ToString("yyyy-MM-dd");
                        existingEvent.Title = summary; existingEvent.StartTime = startTime.ToString("o"); existingEvent.EndTime = endTime.ToString("o"); existingEvent.IsAllDay = isAllDay;
                        if (newDateKey != existingEventDateKey) {
                            _events[existingEventDateKey].Remove(existingEvent);
                            if (!_events.ContainsKey(newDateKey)) _events[newDateKey] = new List<EventItem>();
                            _events[newDateKey].Add(existingEvent);
                        }
                        countUpdated++;
                    } else {
                        bool isTask = description.Contains("カテゴリ:") || description.Contains("進捗:");
                        if (isTask) {
                            string cat = "", stat = "未実施", prio = "中";
                            var catMatch = Regex.Match(description, @"カテゴリ:\s*(.*?)(\r\n|$)"); if (catMatch.Success) cat = catMatch.Groups[1].Value.Trim();
                            var statMatch = Regex.Match(description, @"進捗:\s*(.*?)(\r\n|$)"); if (statMatch.Success) stat = statMatch.Groups[1].Value.Trim();
                            var prioMatch = Regex.Match(description, @"優先度:\s*(.*?)(\r\n|$)"); if (prioMatch.Success) prio = prioMatch.Groups[1].Value.Trim();
                            _tasks.Add(new TaskItem {
                                ID = uid, ProjectID = defaultProjectId, タスク = summary, 進捗度 = stat, 優先度 = prio,
                                期日 = isAllDay ? startTime.ToString("yyyy-MM-dd") : startTime.ToString("yyyy-MM-dd HH:mm"),
                                カテゴリ = cat, サブカテゴリ = "", 通知設定 = "全体設定に従う", 保存日付 = DateTime.Now.ToString("yyyy-MM-dd")
                            });
                        } else {
                            string dateKey = startTime.ToString("yyyy-MM-dd");
                            if (!_events.ContainsKey(dateKey)) _events[dateKey] = new List<EventItem>();
                            _events[dateKey].Add(new EventItem { ID = uid, Title = summary, StartTime = startTime.ToString("o"), EndTime = endTime.ToString("o"), IsAllDay = isAllDay });
                        }
                        countImported++;
                    }
                }

                if (importAsHolidays) {
                    _dataService.SaveToJson(holidaysPath, holidays);
                    MessageBox.Show(string.Format("祝日のインポートが完了しました。\n追加・更新: {0} 件", countImported), "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                    return;
                }

                _dataService.SaveTasksToCsv(_dataService.TasksFile, _tasks);
                _dataService.SaveToJson(_dataService.ProjectsFile, _projects);
                _dataService.SaveToJson(_dataService.EventsFile, _events);
                
                MessageBox.Show(string.Format("インポートが完了しました。\n新規: {0} 件\n更新: {1} 件\nスキップ: {2} 件", countImported, countUpdated, countSkipped), "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
                this.Close();
            } catch (Exception ex) {
                MessageBox.Show(string.Format("インポート中にエラーが発生しました: {0}", ex.Message), "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
