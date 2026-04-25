using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TaskManager.Models;
using TaskManager.Services;

namespace TaskManager.Forms
{
    public class FormTemplate : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private readonly DataService _dataService;
        private readonly List<ProjectItem> _projects;
        private Dictionary<string, List<TaskItem>> _templates;

        public ProjectItem NewProject { get; private set; }
        public List<TaskItem> NewTasks { get; private set; }

        private ComboBox comboTemplate;
        private TextBox textProjectName;
        private Button btnImport, btnCancel;

        public FormTemplate(DataService dataService, List<ProjectItem> projects, bool isDarkMode)
        {
            _dataService = dataService;
            _projects = projects;
            _templates = _dataService.LoadFromJson<Dictionary<string, List<TaskItem>>>(_dataService.TemplatesFile, new Dictionary<string, List<TaskItem>>());

            InitializeComponent();
            ThemeManager.ApplyTheme(this, isDarkMode);
            
            if (_templates.Count == 0) {
                MessageBox.Show("利用できるテンプレートがありません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Load += (s, e) => this.Close();
            } else {
                comboTemplate.Items.AddRange(_templates.Keys.ToArray());
                comboTemplate.SelectedIndex = 0;
            }
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
            this.Text = "テンプレートから新規プロジェクト作成";
            this.Size = new Size(400, 220);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false; this.MinimizeBox = false;

            this.Controls.Add(new Label { Text = "1. 使用するテンプレートを選択:", Location = new Point(15, 15), AutoSize = true });
            comboTemplate = new ComboBox { Location = new Point(15, 40), Size = new Size(350, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            comboTemplate.SelectedIndexChanged += (s, e) => UpdatePlaceholder();
            this.Controls.Add(comboTemplate);

            this.Controls.Add(new Label { Text = "2. 作成するプロジェクト名:", Location = new Point(15, 85), AutoSize = true });
            textProjectName = new TextBox { Location = new Point(15, 110), Size = new Size(350, 25) };
            this.Controls.Add(textProjectName);

            btnImport = new Button { Text = "取り込み", Location = new Point(100, 150) };
            btnImport.Click += BtnImport_Click;
            this.Controls.Add(btnImport);

            btnCancel = new Button { Text = "キャンセル", Location = new Point(200, 150) };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnImport; this.CancelButton = btnCancel;
        }

        private void UpdatePlaceholder() { if (string.IsNullOrWhiteSpace(textProjectName.Text) || textProjectName.Text.Contains("_")) textProjectName.Text = string.Format("{0}_{1:yyyy-MM-dd}", comboTemplate.SelectedItem, DateTime.Now); }

        private async void BtnImport_Click(object sender, EventArgs e)
        {
            string pName = textProjectName.Text.Trim();
            if (string.IsNullOrWhiteSpace(pName)) { MessageBox.Show("プロジェクト名を入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (_projects.Any(p => p.ProjectName == pName)) { MessageBox.Show("同じ名前のプロジェクトが既に存在します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            NewProject = new ProjectItem { ProjectID = Guid.NewGuid().ToString(), ProjectName = pName, Notification = "全体設定に従う", AutoArchiveTasks = true, ProjectColor = "#D3D3D3" };
            NewTasks = new List<TaskItem>();
            
            foreach (var t in _templates[comboTemplate.SelectedItem.ToString()]) {
                var nt = new TaskItem { ID = Guid.NewGuid().ToString(), ProjectID = NewProject.ProjectID, タスク = t.タスク, 優先度 = t.優先度, 進捗度 = string.IsNullOrEmpty(t.進捗度) ? "未実施" : t.進捗度, カテゴリ = t.カテゴリ, サブカテゴリ = t.サブカテゴリ, 保存日付 = DateTime.Today.ToString("yyyy-MM-dd"), 通知設定 = "全体設定に従う" };
                NewTasks.Add(nt);
            }

            await ShowSaveFeedbackAndClose("プロジェクトとタスクを作成しました");
        }

        private async System.Threading.Tasks.Task ShowSaveFeedbackAndClose(string message)
        {
            foreach (Control c in this.Controls) { var b = c as Button; if (b != null) b.Enabled = false; }
            var lbl = new Label { Text = message, Font = new Font("Meiryo UI", 9, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(180, 0, 0, 0), AutoSize = true, Padding = new Padding(10) };
            this.Controls.Add(lbl);
            lbl.Location = new Point((this.ClientSize.Width - lbl.Width) / 2, (this.ClientSize.Height - lbl.Height) / 2);
            lbl.BringToFront();
            await System.Threading.Tasks.Task.Delay(1200);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
