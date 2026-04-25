using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TaskManager.Services;

namespace TaskManager.Forms
{
    public class FormCategoryEditor : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private DataService _dataService;
        private Dictionary<string, List<string>> _categories;
        private ListBox _listCat;
        private ListBox _listSub;

        public FormCategoryEditor(DataService dataService, Dictionary<string, List<string>> categories, bool isDarkMode)
        {
            _dataService = dataService;
            _categories = categories ?? new Dictionary<string, List<string>>();

            this.Text = "カテゴリ編集";
            this.Size = new Size(500, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;

            var splitContainer = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 240 };
            
            // 左側: カテゴリ
            var groupCat = new GroupBox { Text = "カテゴリ", Dock = DockStyle.Fill };
            _listCat = new ListBox { Dock = DockStyle.Fill };
            _listCat.SelectedIndexChanged += (s, e) => RefreshSubCategories();
            
            var panelCatBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35 };
            var btnAddCat = new Button { Text = "追加", Width = 55 };
            var btnRenCat = new Button { Text = "名前変更", Width = 70 };
            var btnDelCat = new Button { Text = "削除", Width = 55 };
            
            btnAddCat.Click += BtnAddCat_Click;
            btnRenCat.Click += BtnRenCat_Click;
            btnDelCat.Click += BtnDelCat_Click;
            
            panelCatBtns.Controls.AddRange(new Control[] { btnAddCat, btnRenCat, btnDelCat });
            groupCat.Controls.Add(_listCat);
            groupCat.Controls.Add(panelCatBtns);
            splitContainer.Panel1.Controls.Add(groupCat);

            // 右側: サブカテゴリ
            var groupSub = new GroupBox { Text = "サブカテゴリ", Dock = DockStyle.Fill };
            _listSub = new ListBox { Dock = DockStyle.Fill };
            
            var panelSubBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35 };
            var btnAddSub = new Button { Text = "追加", Width = 55 };
            var btnRenSub = new Button { Text = "名前変更", Width = 70 };
            var btnDelSub = new Button { Text = "削除", Width = 55 };
            
            btnAddSub.Click += BtnAddSub_Click;
            btnRenSub.Click += BtnRenSub_Click;
            btnDelSub.Click += BtnDelSub_Click;
            
            panelSubBtns.Controls.AddRange(new Control[] { btnAddSub, btnRenSub, btnDelSub });
            groupSub.Controls.Add(_listSub);
            groupSub.Controls.Add(panelSubBtns);
            splitContainer.Panel2.Controls.Add(groupSub);

            // 下部: 保存
            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var btnSave = new Button { Text = "保存して閉じる", Width = 120, Location = new Point(190, 5), DialogResult = DialogResult.OK };
            btnSave.Click += (s, e) => {
                _dataService.SaveToJson(_dataService.CategoriesFile, _categories);
                this.Close();
            };
            panelBottom.Controls.Add(btnSave);

            this.Controls.AddRange(new Control[] { splitContainer, panelBottom });
            this.AcceptButton = btnSave;
            
            RefreshCategories();
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

        private void RefreshCategories()
        {
            _listCat.Items.Clear();
            foreach (var cat in _categories.Keys.OrderBy(k => k)) _listCat.Items.Add(cat);
            RefreshSubCategories();
        }

        private void RefreshSubCategories()
        {
            _listSub.Items.Clear();
            if (_listCat.SelectedItem != null && _categories.ContainsKey(_listCat.SelectedItem.ToString()))
            {
                foreach (var sub in _categories[_listCat.SelectedItem.ToString()].OrderBy(k => k)) _listSub.Items.Add(sub);
            }
        }

        private void BtnAddCat_Click(object sender, EventArgs e)
        {
            string newName = Prompt.ShowDialog("新しいカテゴリ名を入力してください:", "カテゴリ追加");
            if (!string.IsNullOrWhiteSpace(newName)) {
                if (_categories.ContainsKey(newName)) MessageBox.Show("そのカテゴリは既に存在します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else { _categories[newName] = new List<string>(); RefreshCategories(); _listCat.SelectedItem = newName; }
            }
        }

        private void BtnRenCat_Click(object sender, EventArgs e)
        {
            if (_listCat.SelectedItem == null) return;
            string oldName = _listCat.SelectedItem.ToString();
            string newName = Prompt.ShowDialog("新しいカテゴリ名を入力してください:", "カテゴリ名変更", oldName);
            if (!string.IsNullOrWhiteSpace(newName) && newName != oldName) {
                if (_categories.ContainsKey(newName)) MessageBox.Show("そのカテゴリ名は既に存在します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else { _categories[newName] = _categories[oldName]; _categories.Remove(oldName); RefreshCategories(); _listCat.SelectedItem = newName; }
            }
        }

        private void BtnDelCat_Click(object sender, EventArgs e)
        {
            if (_listCat.SelectedItem == null) return;
            string name = _listCat.SelectedItem.ToString();
            if (MessageBox.Show(string.Format("カテゴリ '{0}' を削除しますか？\n含まれるサブカテゴリも削除されます。", name), "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                _categories.Remove(name); RefreshCategories();
            }
        }

        private void BtnAddSub_Click(object sender, EventArgs e)
        {
            if (_listCat.SelectedItem == null) return;
            string catName = _listCat.SelectedItem.ToString();
            string subName = Prompt.ShowDialog("新しいサブカテゴリ名を入力してください:", "サブカテゴリ追加");
            if (!string.IsNullOrWhiteSpace(subName)) {
                if (_categories[catName].Contains(subName)) MessageBox.Show("そのサブカテゴリは既に存在します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else { _categories[catName].Add(subName); RefreshSubCategories(); _listSub.SelectedItem = subName; }
            }
        }

        private void BtnRenSub_Click(object sender, EventArgs e)
        {
            if (_listCat.SelectedItem == null || _listSub.SelectedItem == null) return;
            string catName = _listCat.SelectedItem.ToString(); string oldSubName = _listSub.SelectedItem.ToString();
            string newSubName = Prompt.ShowDialog("新しいサブカテゴリ名を入力してください:", "サブカテゴリ名変更", oldSubName);
            if (!string.IsNullOrWhiteSpace(newSubName) && newSubName != oldSubName) {
                if (_categories[catName].Contains(newSubName)) MessageBox.Show("そのサブカテゴリ名は既に存在します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else { _categories[catName].Remove(oldSubName); _categories[catName].Add(newSubName); RefreshSubCategories(); _listSub.SelectedItem = newSubName; }
            }
        }

        private void BtnDelSub_Click(object sender, EventArgs e) { if (_listCat.SelectedItem == null || _listSub.SelectedItem == null) return; if (MessageBox.Show(string.Format("サブカテゴリ '{0}' を削除しますか？", _listSub.SelectedItem), "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) { _categories[_listCat.SelectedItem.ToString()].Remove(_listSub.SelectedItem.ToString()); RefreshSubCategories(); } }
    }
}
