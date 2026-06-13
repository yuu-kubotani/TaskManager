using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using UniConsul.Models;
using UniConsul.Services;

namespace UniConsul.Forms
{
    public class FormTemplateTaskInput : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private TaskItem _existingTask;
        private DataService _dataService;
        private Dictionary<string, List<string>> _categories;

        public TaskItem ResultTask { get; private set; }

        private TextBox _txtTask;
        private ComboBox _cmbCategory;
        private ComboBox _cmbSubCategory;
        private ComboBox _cmbPriority;
        private ComboBox _cmbStatus;
        private Button _btnSave;
        private Button _btnCancel;

        public FormTemplateTaskInput(TaskItem existingTask, bool isDarkMode, DataService dataService)
        {
            _existingTask = existingTask;
            _dataService = dataService;
            _categories = _dataService.LoadFromJson<Dictionary<string, List<string>>>(_dataService.CategoriesFile, new Dictionary<string, List<string>>());

            InitializeComponent();
            LoadData();
            ThemeManager.ApplyTheme(this, isDarkMode);

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
            this.Name = "FormTemplateTaskInput";
            this.Text = _existingTask != null ? "テンプレートタスクの編集" : "テンプレートタスクの追加";
            this.Size = new Size(350, 350);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            var lblTask = new Label { Text = "タスク内容：", Location = new Point(15, 15), AutoSize = true };
            _txtTask = new TextBox { Multiline = true, Location = new Point(15, 35), Size = new Size(300, 60), AcceptsReturn = true };

            var lblCategory = new Label { Text = "カテゴリ：", Location = new Point(15, 110), AutoSize = true };
            _cmbCategory = new ComboBox { Location = new Point(15, 130), Size = new Size(140, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbCategory.SelectedIndexChanged += CmbCategory_SelectedIndexChanged;

            var lblSubCategory = new Label { Text = "サブカテゴリ：", Location = new Point(175, 110), AutoSize = true };
            _cmbSubCategory = new ComboBox { Location = new Point(175, 130), Size = new Size(140, 25), DropDownStyle = ComboBoxStyle.DropDownList };

            var lblPriority = new Label { Text = "優先度：", Location = new Point(15, 170), AutoSize = true };
            _cmbPriority = new ComboBox { Location = new Point(15, 190), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbPriority.Items.AddRange(new[] { "高", "中", "低" });

            var lblStatus = new Label { Text = "進捗度：", Location = new Point(175, 170), AutoSize = true };
            _cmbStatus = new ComboBox { Location = new Point(175, 190), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbStatus.Items.AddRange(new[] { "未実施", "保留", "実施中", "確認待ち", "完了済み" });

            _btnSave = new Button { Text = "OK", Location = new Point(80, 250), Size = new Size(80, 30) };
            _btnSave.Click += BtnSave_Click;

            _btnCancel = new Button { Text = "キャンセル", Location = new Point(170, 250), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
            _btnCancel.Click += (s, e) => this.Close();

            this.Controls.AddRange(new Control[] { lblTask, _txtTask, lblCategory, _cmbCategory, lblSubCategory, _cmbSubCategory, lblPriority, _cmbPriority, lblStatus, _cmbStatus, _btnSave, _btnCancel });

            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        private void LoadData()
        {
            _cmbCategory.Items.AddRange(_categories.Keys.ToArray());

            if (_existingTask != null)
            {
                _txtTask.Text = _existingTask.タスク;
                _cmbPriority.SelectedItem = _existingTask.優先度;
                _cmbStatus.SelectedItem = _existingTask.進捗度;

                if (!string.IsNullOrEmpty(_existingTask.カテゴリ) && _cmbCategory.Items.Contains(_existingTask.カテゴリ))
                {
                    _cmbCategory.SelectedItem = _existingTask.カテゴリ;
                    if (!string.IsNullOrEmpty(_existingTask.サブカテゴリ) && _cmbSubCategory.Items.Contains(_existingTask.サブカテゴリ))
                    {
                        _cmbSubCategory.SelectedItem = _existingTask.サブカテゴリ;
                    }
                }
            }
            else
            {
                _cmbPriority.SelectedIndex = 1; // 中
                _cmbStatus.SelectedIndex = 0;   // 未実施
            }
        }

        private void CmbCategory_SelectedIndexChanged(object sender, EventArgs e)
        {
            _cmbSubCategory.Items.Clear();
            if (_cmbCategory.SelectedItem != null)
            {
                string cat = _cmbCategory.SelectedItem.ToString();
                if (_categories.ContainsKey(cat))
                {
                    _cmbSubCategory.Items.AddRange(_categories[cat].ToArray());
                }
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtTask.Text))
            {
                MessageBox.Show("タスク内容は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ResultTask = new TaskItem
            {
                ID = Guid.NewGuid().ToString(),
                タスク = _txtTask.Text.Trim(),
                優先度 = _cmbPriority.SelectedItem != null ? _cmbPriority.SelectedItem.ToString() : null,
                進捗度 = _cmbStatus.SelectedItem != null ? _cmbStatus.SelectedItem.ToString() : null,
                カテゴリ = _cmbCategory.SelectedItem != null ? _cmbCategory.SelectedItem.ToString() : null,
                サブカテゴリ = _cmbSubCategory.SelectedItem != null ? _cmbSubCategory.SelectedItem.ToString() : null
            };

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
