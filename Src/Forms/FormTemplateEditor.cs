﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UniConsul.Models;
using UniConsul.Services;

namespace UniConsul.Forms
{
    public class FormTemplateEditor : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private DataService _dataService;
        private Dictionary<string, List<TaskItem>> _templates;
        private bool _isDarkMode;

        private ListBox _listTemplates;
        private ListView _listTasks;
        private Button _btnNewTemplate, _btnRenameTemplate, _btnDeleteTemplate;
        private Button _btnAddTask, _btnEditTask, _btnDeleteTask;
        private SplitContainer _mainSplitContainer;

        public FormTemplateEditor(DataService dataService, bool isDarkMode)
        {
            _dataService = dataService;
            _isDarkMode = isDarkMode;

            // テンプレートの読み込みと編集用ディープコピー
            var rawTemplates = _dataService.LoadFromJson<Dictionary<string, List<TaskItem>>>(_dataService.TemplatesFile, new Dictionary<string, List<TaskItem>>());
            _templates = new Dictionary<string, List<TaskItem>>();
            foreach (var kvp in rawTemplates) { _templates[kvp.Key] = new List<TaskItem>(kvp.Value); }

            InitializeComponent();
            ThemeManager.ApplyTheme(this, _isDarkMode);

            // 全体設定をロードして、ウィンドウサイズとスプリッター位置の記憶を有効化
            var settings = _dataService.LoadSettings();
            if (settings != null && settings.WindowSizes != null && settings.WindowSizes.ContainsKey(this.Name)) {
                var parts = settings.WindowSizes[this.Name].Split(',');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) this.Size = new Size(Math.Max(300, w), Math.Max(200, h));
                if (parts.Length >= 3 && int.TryParse(parts[2], out int sp)) try { _mainSplitContainer.SplitterDistance = sp; } catch {}
            }

            ThemeManager.EnableDynamicResizing(this, settings, () => _dataService.SaveToJson(_dataService.SettingsFile, settings), _mainSplitContainer);

            // 境界線が左に寄りすぎている場合や右に行き過ぎている場合、適切な位置に強制補正する
            if (this.Width < 900) this.Width = 950;
            if (_mainSplitContainer.SplitterDistance < 200 || _mainSplitContainer.SplitterDistance > 400) _mainSplitContainer.SplitterDistance = 310;

            LoadTemplates();

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
            this.Name = "FormTemplateEditor";
            this.Text = "テンプレートの編集";
            this.Size = new Size(950, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable; // ユーザーが自由にサイズ変更できるようにする
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            _mainSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 310,
                Padding = new Padding(15, 15, 15, 10)
            };
            this.Controls.Add(_mainSplitContainer);

            // --- 左側: テンプレート一覧 ---
            var grpTemplates = new GroupBox { Text = "テンプレート一覧", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
            var pnlTempInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            grpTemplates.Controls.Add(pnlTempInner);

            var pnlTempBtn = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
            _btnNewTemplate = new Button { Text = "新規", Width = 70, Height = 28, Margin = new Padding(0, 0, 5, 0) };
            _btnRenameTemplate = new Button { Text = "名前変更", Width = 90, Height = 28, Margin = new Padding(0, 0, 5, 0) };
            _btnDeleteTemplate = new Button { Text = "削除", Width = 70, Height = 28, Margin = new Padding(0, 0, 0, 0) };
            pnlTempBtn.Controls.AddRange(new Control[] { _btnNewTemplate, _btnRenameTemplate, _btnDeleteTemplate });

            _listTemplates = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Meiryo UI", 10) };
            _listTemplates.SelectedIndexChanged += ListTemplates_SelectedIndexChanged;
            _listTemplates.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Delete) BtnDeleteTemplate_Click(s, e);
            };

            pnlTempInner.Controls.Add(_listTemplates);
            pnlTempInner.Controls.Add(pnlTempBtn);
            _mainSplitContainer.Panel1.Controls.Add(grpTemplates);

            // --- 右側: タスク一覧 ---
            var grpTasks = new GroupBox { Text = "テンプレート内のタスク", Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0) };
            var pnlTaskInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            grpTasks.Controls.Add(pnlTaskInner);

            var pnlTaskBtn = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
            _btnAddTask = new Button { Text = "追加", Width = 70, Height = 28, Margin = new Padding(0, 0, 5, 0) };
            _btnEditTask = new Button { Text = "編集", Width = 70, Height = 28, Margin = new Padding(0, 0, 5, 0) };
            _btnDeleteTask = new Button { Text = "削除", Width = 70, Height = 28, Margin = new Padding(0, 0, 0, 0) };
            pnlTaskBtn.Controls.AddRange(new Control[] { _btnAddTask, _btnEditTask, _btnDeleteTask });

            _listTasks = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Meiryo UI", 9),
                OwnerDraw = true
            };
            _listTasks.Columns.Add("タスク内容", 300); // カラム幅を広げる
            _listTasks.Columns.Add("優先度", 60);
            _listTasks.Columns.Add("進捗度", 80);
            _listTasks.Columns.Add("カテゴリ", 150); // カラム幅を広げる
            _listTasks.DrawColumnHeader += (s, e) =>
            {
                Color bgColor = _isDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
                Color fgColor = _isDarkMode ? Color.White : SystemColors.ControlText;
                using (var brush = new SolidBrush(bgColor)) e.Graphics.FillRectangle(brush, e.Bounds);
                ControlPaint.DrawBorder3D(e.Graphics, e.Bounds, Border3DStyle.RaisedInner);
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.LeftAndRightPadding;
                TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, e.Bounds, fgColor, flags);
            };
            _listTasks.DrawItem += (s, e) => e.DrawDefault = true;
            _listTasks.DrawSubItem += (s, e) => e.DrawDefault = true;
            _listTasks.DoubleClick += BtnEditTask_Click;
            _listTasks.SelectedIndexChanged += ListTasks_SelectedIndexChanged;
            _listTasks.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Delete) BtnDeleteTask_Click(s, e);
            };

            pnlTaskInner.Controls.Add(_listTasks);
            pnlTaskInner.Controls.Add(pnlTaskBtn);
            _mainSplitContainer.Panel2.Controls.Add(grpTasks);

            // --- 下部: 保存・キャンセル ---
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 55 };
            var btnSave = new Button { Text = "保存して閉じる", Size = new Size(130, 30), Location = new Point(500, 10), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var btnCancel = new Button { Text = "キャンセル", Size = new Size(100, 30), Location = new Point(650, 10), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            btnSave.Click += BtnSave_Click;
            pnlBottom.Controls.AddRange(new Control[] { btnSave, btnCancel });
            this.Controls.Add(pnlBottom);

            // Events
            _btnNewTemplate.Click += BtnNewTemplate_Click;
            _btnRenameTemplate.Click += BtnRenameTemplate_Click;
            _btnDeleteTemplate.Click += BtnDeleteTemplate_Click;
            _btnAddTask.Click += BtnAddTask_Click;
            _btnEditTask.Click += BtnEditTask_Click;
            _btnDeleteTask.Click += BtnDeleteTask_Click;
        }

        private void LoadTemplates()
        {
            string selected = _listTemplates.SelectedItem != null ? _listTemplates.SelectedItem.ToString() : null;
            _listTemplates.Items.Clear();
            foreach (var key in _templates.Keys.OrderBy(k => k)) { _listTemplates.Items.Add(key); }
            
            if (selected != null && _listTemplates.Items.Contains(selected)) _listTemplates.SelectedItem = selected;
            else if (_listTemplates.Items.Count > 0) _listTemplates.SelectedIndex = 0;
            else UpdateTasksView();
        }

        private void ListTemplates_SelectedIndexChanged(object sender, EventArgs e) { UpdateTasksView(); }
        
        private void ListTasks_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool hasTask = _listTasks.SelectedItems.Count > 0;
            _btnEditTask.Enabled = hasTask;
            _btnDeleteTask.Enabled = hasTask;
        }

        private void UpdateTasksView()
        {
            _listTasks.Items.Clear();
            bool hasTemplate = _listTemplates.SelectedItem != null;
            
            _btnRenameTemplate.Enabled = hasTemplate;
            _btnDeleteTemplate.Enabled = hasTemplate;
            _btnAddTask.Enabled = hasTemplate;
            _btnEditTask.Enabled = false;
            _btnDeleteTask.Enabled = false;

            if (hasTemplate)
            {
                string tempName = _listTemplates.SelectedItem.ToString();
                if (_templates.ContainsKey(tempName))
                {
                    foreach (var task in _templates[tempName])
                    {
                        var item = new ListViewItem(task.タスク) { Tag = task };
                        item.SubItems.Add(task.優先度);
                        item.SubItems.Add(task.進捗度);
                        item.SubItems.Add(task.カテゴリ);
                        _listTasks.Items.Add(item);
                    }
                }
            }
        }

        // --- テンプレート操作 ---
        private void BtnNewTemplate_Click(object sender, EventArgs e)
        {
            string newName = Prompt.ShowDialog("新しいテンプレート名を入力してください:", "新規テンプレート");
            if (!string.IsNullOrWhiteSpace(newName))
            {
                if (_templates.ContainsKey(newName)) { MessageBox.Show("その名前は既に存在します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                _templates[newName] = new List<TaskItem>();
                LoadTemplates();
                _listTemplates.SelectedItem = newName;
            }
        }

        private void BtnRenameTemplate_Click(object sender, EventArgs e)
        {
            if (_listTemplates.SelectedItem == null) return;
            string oldName = _listTemplates.SelectedItem.ToString();
            string newName = Prompt.ShowDialog("名前の変更:", "テンプレート名の変更", oldName);
            if (!string.IsNullOrWhiteSpace(newName) && newName != oldName)
            {
                if (_templates.ContainsKey(newName)) { MessageBox.Show("その名前は既に存在します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                var content = _templates[oldName];
                _templates.Remove(oldName);
                _templates[newName] = content;
                LoadTemplates();
                _listTemplates.SelectedItem = newName;
            }
        }

        private void BtnDeleteTemplate_Click(object sender, EventArgs e)
        {
            if (_listTemplates.SelectedItem == null) return;
            string name = _listTemplates.SelectedItem.ToString();
            if (MessageBox.Show(string.Format("テンプレート「{0}」を削除しますか？", name), "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _templates.Remove(name);
                LoadTemplates();
            }
        }

        // --- タスク操作 ---
        private void BtnAddTask_Click(object sender, EventArgs e)
        {
            if (_listTemplates.SelectedItem == null) return;
            string tempName = _listTemplates.SelectedItem.ToString();
            
            var form = new FormTemplateTaskInput(null, _isDarkMode, _dataService);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                _templates[tempName].Add(form.ResultTask);
                UpdateTasksView();
            }
        }

        private void BtnEditTask_Click(object sender, EventArgs e)
        {
            if (_listTemplates.SelectedItem == null || _listTasks.SelectedItems.Count == 0) return;
            string tempName = _listTemplates.SelectedItem.ToString();
            var task = (TaskItem)_listTasks.SelectedItems[0].Tag;
            
            var form = new FormTemplateTaskInput(task, _isDarkMode, _dataService);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                task.タスク = form.ResultTask.タスク;
                task.優先度 = form.ResultTask.優先度;
                task.進捗度 = form.ResultTask.進捗度;
                task.カテゴリ = form.ResultTask.カテゴリ;
                UpdateTasksView();
            }
        }

        private void BtnDeleteTask_Click(object sender, EventArgs e)
        {
            if (_listTemplates.SelectedItem == null || _listTasks.SelectedItems.Count == 0) return;
            if (MessageBox.Show("選択したタスクを削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                string tempName = _listTemplates.SelectedItem.ToString();
                var task = (TaskItem)_listTasks.SelectedItems[0].Tag;
                _templates[tempName].Remove(task);
                UpdateTasksView();
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            _dataService.SaveToJson(_dataService.TemplatesFile, _templates);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
