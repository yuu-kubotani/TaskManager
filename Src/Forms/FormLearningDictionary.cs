﻿using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using UniConsul.Models;
using UniConsul.Services;

namespace UniConsul.Forms
{
    public class FormLearningDictionary : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private DataService _dataService;
        private List<TaskItem> _tasks;
        private AppSettings _settings;
        private ListBox _listRules;

        public FormLearningDictionary(DataService dataService, List<TaskItem> tasks, Dictionary<string, List<string>> categories, bool isDarkMode)
        {
            _dataService = dataService;
            _tasks = tasks;
            _settings = _dataService.LoadSettings();

            if (_settings.AutoTracker == null) _settings.AutoTracker = new AutoTrackerSettings();
            if (_settings.AutoTracker.LearningDictionary == null) _settings.AutoTracker.LearningDictionary = new List<string>();

            this.Name = "FormLearningDictionary";
            this.Text = "学習辞書の管理";
            this.Size = new Size(650, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            var lblInfo = new Label
            {
                Text = "自動記録でタスクやカテゴリを紐付けるための「学習辞書ルール」の一覧です。\n" +
                       "ルールの追加は、タイムライン上の「未分類」記録を右クリックして行ってください。",
                Dock = DockStyle.Top, Height = 40, Padding = new Padding(10), AutoSize = true
            };
            this.Controls.Add(lblInfo);

            _listRules = new ListBox { Dock = DockStyle.Fill, Font = new Font("Meiryo UI", 9.5f) };
            RefreshList();
            this.Controls.Add(_listRules);
            _listRules.BringToFront();

            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            var btnDelete = new Button { Text = "選択したルールを削除", Location = new Point(15, 10), Size = new Size(180, 30), ForeColor = Color.Red };
            btnDelete.Click += BtnDelete_Click;
            var btnClose = new Button { Text = "閉じる", Location = new Point(530, 10), Size = new Size(90, 30), Anchor = AnchorStyles.Right | AnchorStyles.Top, DialogResult = DialogResult.OK };
            
            pnlBottom.Controls.AddRange(new Control[] { btnDelete, btnClose });
            this.Controls.Add(pnlBottom);

            ThemeManager.ApplyTheme(this, isDarkMode);
            
            if (_settings != null && _settings.WindowSizes != null && _settings.WindowSizes.ContainsKey(this.Name)) {
                var parts = _settings.WindowSizes[this.Name].Split(',');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) this.Size = new Size(Math.Max(300, w), Math.Max(200, h));
            }

            ThemeManager.EnableDynamicResizing(this, _settings, () => _dataService.SaveToJson(_dataService.SettingsFile, _settings));
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

        private void RefreshList()
        {
            _listRules.Items.Clear();
            foreach (var rule in _settings.AutoTracker.LearningDictionary) {
                _listRules.Items.Add(FormatRuleForDisplay(rule));
            }
        }

        private string FormatRuleForDisplay(string rawRule)
        {
            try
            {
                var parts = rawRule.Split(new[] { "::" }, 3, StringSplitOptions.None);
                if (parts.Length >= 2)
                {
                    string target = parts[0];
                    string matchType = parts.Length == 3 ? parts[1] : "Title";
                    string keyword = parts.Length == 3 ? parts[2] : parts[1];

                    string targetDisplay = "";
                    if (target.StartsWith("CAT:")) {
                        targetDisplay = "[カテゴリ] " + target.Substring(4).Replace("|", " > ");
                    } else {
                        var task = _tasks.FirstOrDefault(t => t.ID == target);
                        targetDisplay = task != null ? "[タスク] " + task.タスク : "[不明なタスク]";
                    }

                    string matchDisplay = matchType == "Process" ? "プロセス名に一致" : (matchType == "Regex" ? "正規表現で一致" : "タイトル/パスに含む");
                    return string.Format("{0} ⇔ 「{1}」({2})", targetDisplay, keyword, matchDisplay);
                }
            }
            catch { }
            return rawRule;
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (_listRules.SelectedIndex >= 0 && MessageBox.Show("選択したルールを削除しますか？", "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                _settings.AutoTracker.LearningDictionary.RemoveAt(_listRules.SelectedIndex);
                _dataService.SaveToJson(_dataService.SettingsFile, _settings);
                RefreshList();
            }
        }
    }
}