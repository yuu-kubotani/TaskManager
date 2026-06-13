﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UniConsul.Models;
using UniConsul.Services;

namespace UniConsul.Forms
{
    /// <summary>
    /// アーカイブされたプロジェクトとタスクを閲覧、検索、復元、または完全に削除するための画面を提供します。
    /// </summary>
    public class FormArchiveView : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // --- 依存関係と状態 ---
        private readonly DataService _dataService;
        private readonly List<TaskItem> _activeTasks;
        private readonly List<ProjectItem> _activeProjects;
        private readonly bool _isDarkMode;

        // --- UI コントロール ---
        private TextBox _txtSearch;
        private Button _btnSearch;
        private Button _btnRestore;
        private Button _btnDelete;
        private Button _btnClose;
        private ListView _listArchiveItems;

        // --- アーカイブデータ ---
        private List<TaskItem> _archivedTasks;
        private List<ProjectItem> _archivedProjects;

        /// <summary>
        /// アーカイブ画面を初期化します。
        /// </summary>
        /// <param name="dataService">データの読み書きを行うサービス</param>
        /// <param name="activeTasks">現在アクティブなタスクのリスト（復元時に使用）</param>
        /// <param name="activeProjects">現在アクティブなプロジェクトのリスト（復元時に使用）</param>
        /// <param name="isDarkMode">ダークモードが有効かどうか</param>
        public FormArchiveView(DataService dataService, List<TaskItem> activeTasks, List<ProjectItem> activeProjects, bool isDarkMode)
        {
            _dataService = dataService;
            _activeTasks = activeTasks;
            _activeProjects = activeProjects;
            _isDarkMode = isDarkMode;

            InitializeComponent();
            ThemeManager.ApplyTheme(this, _isDarkMode);
            LoadData();

            UniConsul.Utils.IconHelper.SetAppIcon(this);

            var settings = _dataService.LoadSettings();
            if (settings != null && settings.WindowSizes != null && settings.WindowSizes.ContainsKey(this.Name)) {
                var parts = settings.WindowSizes[this.Name].Split(',');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) this.Size = new Size(Math.Max(300, w), Math.Max(200, h));
            }

            ThemeManager.EnableDynamicResizing(this, settings, () => _dataService.SaveToJson(_dataService.SettingsFile, settings));
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
            this.Name = "FormArchiveView";
            this.Text = "アーカイブビュー";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(600, 400);
            this.AutoScaleMode = AutoScaleMode.Dpi;

            // --- Top Panel (Search) ---
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 8, 10, 0) };
            this.Controls.Add(topPanel);

            _btnSearch = new Button { Text = "検索", Dock = DockStyle.Right, Width = 80 };
            _btnSearch.Click += (s, e) => LoadData(_txtSearch.Text);
            topPanel.Controls.Add(_btnSearch);

            _txtSearch = new TextBox { Dock = DockStyle.Fill };
            _txtSearch.KeyDown += (s, e) => 
            { 
                if (e.KeyCode == Keys.Enter) 
                { 
                    e.SuppressKeyPress = true; 
                    LoadData(_txtSearch.Text); 
                } 
            };
            topPanel.Controls.Add(_txtSearch);

            // --- Bottom Panel (Actions) ---
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(10) };
            this.Controls.Add(bottomPanel);

            _btnClose = new Button { Text = "閉じる", Dock = DockStyle.Right, Width = 100, DialogResult = DialogResult.Cancel };
            bottomPanel.Controls.Add(_btnClose);
            this.CancelButton = _btnClose;

            _btnRestore = new Button { Text = "選択項目を復元", Dock = DockStyle.Left, Width = 150 };
            _btnRestore.Click += BtnRestore_Click;
            bottomPanel.Controls.Add(_btnRestore);

            _btnDelete = new Button { Text = "完全に削除", Dock = DockStyle.Left, Width = 120, ForeColor = Color.Red };
            _btnDelete.Click += BtnDelete_Click;
            bottomPanel.Controls.Add(_btnDelete);

            // --- Center ListView ---
            _listArchiveItems = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                GridLines = true,
                HideSelection = false,
                OwnerDraw = true
            };
            _listArchiveItems.Columns.Add("種類", 80);
            _listArchiveItems.Columns.Add("プロジェクト", 150);
            _listArchiveItems.Columns.Add("タスク", 250);
            _listArchiveItems.Columns.Add("アーカイブ日", 120);
            _listArchiveItems.Columns.Add("元の期日", 120);
            _listArchiveItems.Columns.Add("カテゴリ", 100);
            _listArchiveItems.DrawColumnHeader += (s, e) =>
            {
                Color bgColor = _isDarkMode ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
                Color fgColor = _isDarkMode ? Color.White : SystemColors.ControlText;
                using (var brush = new SolidBrush(bgColor)) e.Graphics.FillRectangle(brush, e.Bounds);
                ControlPaint.DrawBorder3D(e.Graphics, e.Bounds, Border3DStyle.RaisedInner);
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.LeftAndRightPadding;
                TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, e.Bounds, fgColor, flags);
            };
            _listArchiveItems.DrawItem += (s, e) => e.DrawDefault = true;
            _listArchiveItems.DrawSubItem += (s, e) => e.DrawDefault = true;
            _listArchiveItems.DoubleClick += (s, e) => _btnRestore.PerformClick();

            // スクロールや更新時のちらつきを防止するための DoubleBuffered の有効化
            typeof(ListView).InvokeMember("DoubleBuffered", 
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, 
                null, _listArchiveItems, new object[] { true });
            
            this.Controls.Add(_listArchiveItems);
            _listArchiveItems.BringToFront();
        }

        /// <summary>
        /// アーカイブされたデータをファイルから読み込み、リストビューに表示します。
        /// </summary>
        /// <param name="searchTerm">検索キーワード（指定された場合、名前で部分一致検索を行います）</param>
        private void LoadData(string searchTerm = "")
        {
            _listArchiveItems.BeginUpdate();
            _listArchiveItems.Items.Clear();

            // アーカイブデータの再読み込み
            _archivedTasks = _dataService.LoadTasksFromCsv(_dataService.ArchivedTasksFile);
            _archivedProjects = _dataService.LoadFromJson<List<ProjectItem>>(_dataService.ArchivedProjectsFile, new List<ProjectItem>());

            var displayItems = new List<ListViewItem>();

            // プロジェクトをリストビューの項目として追加
            foreach (var p in _archivedProjects)
            {
                // Null参照エラーを防ぎつつ、大文字小文字を区別せずに検索する
                if (!string.IsNullOrEmpty(searchTerm) && (p.ProjectName ?? "").IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var lvi = new ListViewItem("プロジェクト") { Tag = p };
                lvi.SubItems.AddRange(new[] { p.ProjectName, "", p.ArchivedDate ?? "", p.ProjectDueDate ?? "", "" });
                displayItems.Add(lvi);
            }

            // タスクをリストビューの項目として追加
            foreach (var t in _archivedTasks)
            {
                // Null参照エラーを防ぎつつ、大文字小文字を区別せずに検索する
                if (!string.IsNullOrEmpty(searchTerm) && (t.タスク ?? "").IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var lvi = new ListViewItem("タスク") { Tag = t };
                lvi.SubItems.AddRange(new[] { t.ProjectName ?? "", t.タスク, t.ArchivedDate ?? "", t.期日 ?? "", t.カテゴリ ?? "" });
                displayItems.Add(lvi);
            }

            // アーカイブ日付の降順（新しい順）で並べ替えてから画面に反映
            var sortedItems = displayItems.OrderByDescending(i => i.SubItems[3].Text).ToArray();
            _listArchiveItems.Items.AddRange(sortedItems);
            
            _listArchiveItems.EndUpdate();
        }

        /// <summary>
        /// 選択されたアイテム（プロジェクト・タスク）をアクティブなデータとして復元します。
        /// </summary>
        private void BtnRestore_Click(object sender, EventArgs e)
        {
            if (_listArchiveItems.SelectedItems.Count == 0)
            {
                MessageBox.Show("復元するアイテムを選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("選択した項目を復元しますか？", "復元の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            bool isDirty = false;
            
            // 1. 選択されたアイテムをプロジェクトとタスクに分類
            var projectsToRestore = _listArchiveItems.SelectedItems
                .Cast<ListViewItem>()
                .Where(i => i.Tag is ProjectItem)
                .Select(i => (ProjectItem)i.Tag)
                .ToList();

            var tasksToRestore = _listArchiveItems.SelectedItems
                .Cast<ListViewItem>()
                .Where(i => i.Tag is TaskItem)
                .Select(i => (TaskItem)i.Tag)
                .ToList();

            // 2. プロジェクトの復元処理
            foreach (var p in projectsToRestore)
            {
                // アーカイブ状態を解除
                p.ArchivedDate = null;
                
                // アクティブなプロジェクト一覧に存在しなければ追加
                if (!_activeProjects.Any(ap => ap.ProjectID == p.ProjectID))
                {
                    _activeProjects.Add(p);
                }
                _archivedProjects.Remove(p);
                
                // 紐づくタスクも復元対象のリストに含める（プロジェクト復元時はタスクも一括復元する仕様）
                var linkedTasks = _archivedTasks.Where(t => t.ProjectID == p.ProjectID).ToList();
                foreach (var lt in linkedTasks)
                {
                    if (!tasksToRestore.Contains(lt))
                    {
                        tasksToRestore.Add(lt);
                    }
                }
                isDirty = true;
            }

            // 3. タスクの復元処理
            var activeProjectIds = _activeProjects.Select(p => p.ProjectID).ToList();
            ProjectItem inboxProject = null;

            foreach (var t in tasksToRestore)
            {
                // 復元先の親プロジェクトが存在しない場合のフォールバック処理
                if (!activeProjectIds.Contains(t.ProjectID))
                {
                    if (inboxProject == null)
                    {
                        // 「未分類」プロジェクトを探すか、無ければ新規作成する
                        inboxProject = _activeProjects.FirstOrDefault(p => p.ProjectName == "未分類") 
                                       ?? new ProjectItem { ProjectID = Guid.NewGuid().ToString(), ProjectName = "未分類", AutoArchiveTasks = true };
                                       
                        if (!_activeProjects.Contains(inboxProject))
                        {
                            _activeProjects.Add(inboxProject);
                        }
                    }
                    t.ProjectID = inboxProject.ProjectID;
                }

                // アーカイブ状態を解除
                t.ArchivedDate = null;
                t.ProjectName = null;
                if (t.進捗度 == "完了済み")
                {
                    // 復元した日が完了日になるようリセット
                    t.完了日 = DateTime.Today.ToString("yyyy-MM-dd");
                }

                // アクティブタスク一覧に追加し、アーカイブからは削除
                if (!_activeTasks.Any(at => at.ID == t.ID))
                {
                    _activeTasks.Add(t);
                }
                _archivedTasks.Remove(t);
                isDirty = true;
            }

            // 4. 変更があればデータを保存し、画面を更新
            if (isDirty)
            {
                _dataService.SaveToJson(_dataService.ProjectsFile, _activeProjects);
                _dataService.SaveTasksToCsv(_dataService.TasksFile, _activeTasks);
                _dataService.SaveToJson(_dataService.ArchivedProjectsFile, _archivedProjects);
                _dataService.SaveArchivedTasksToCsv(_archivedTasks);
                
                this.DialogResult = DialogResult.OK; // メイン画面へリロードを指示
                LoadData(_txtSearch.Text);
            }
        }

        /// <summary>
        /// 選択されたアイテム（プロジェクト・タスク）をアーカイブデータから完全に削除します。
        /// </summary>
        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (_listArchiveItems.SelectedItems.Count == 0) return;
            
            if (MessageBox.Show("選択した項目を完全に削除しますか？\nこの操作は取り消せません。", "削除の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            bool isDirty = false;
            foreach (ListViewItem item in _listArchiveItems.SelectedItems)
            {
                // タスクの完全削除
                var t = item.Tag as TaskItem;
                var p = item.Tag as ProjectItem;
                if (t != null)
                {
                    _archivedTasks.Remove(t);
                    isDirty = true;
                }
                // プロジェクトの完全削除
                else if (p != null)
                {
                    _archivedProjects.Remove(p);
                    // プロジェクトに関連するアーカイブタスクもすべて削除
                    _archivedTasks.RemoveAll(at => at.ProjectID == p.ProjectID);
                    isDirty = true;
                }
            }

            // 変更があればファイルに保存して表示を更新
            if (isDirty)
            {
                _dataService.SaveToJson(_dataService.ArchivedProjectsFile, _archivedProjects);
                _dataService.SaveArchivedTasksToCsv(_archivedTasks);
                LoadData(_txtSearch.Text);
            }
        }
    }
}
