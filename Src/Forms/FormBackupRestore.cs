﻿using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TaskManager.Models;
using TaskManager.Services;

namespace TaskManager.Forms
{
    public class FormBackupRestore : Form
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private DataService _dataService;
        private AppSettings _settings;
        private ListBox _listBackups;
        private string _backupRoot;

        public FormBackupRestore(DataService dataService, AppSettings settings, bool isDarkMode)
        {
            _dataService = dataService;
            _settings = settings;
            _backupRoot = (settings != null && !string.IsNullOrEmpty(settings.BackupPath)) ? settings.BackupPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backup");

            if (!Directory.Exists(_backupRoot)) Directory.CreateDirectory(_backupRoot);

            this.Text = "バックアップの管理";
            this.Size = new Size(400, 350);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;

            var lbl = new Label { Text = "復元したいバックアップを選択してください:", Location = new Point(10, 10), AutoSize = true };
            _listBackups = new ListBox { Location = new Point(10, 35), Size = new Size(360, 210) };

            var btnManual = new Button { Text = "手動バックアップ", Location = new Point(10, 260), Size = new Size(120, 25) };
            var btnRestore = new Button { Text = "復元", Location = new Point(205, 260), Size = new Size(80, 25) };
            var btnCancel = new Button { Text = "キャンセル", Location = new Point(295, 260), Size = new Size(80, 25), DialogResult = DialogResult.Cancel };

            btnManual.Click += BtnManual_Click;
            btnRestore.Click += BtnRestore_Click;

            this.Controls.AddRange(new Control[] { lbl, _listBackups, btnManual, btnRestore, btnCancel });
            this.AcceptButton = btnRestore;
            this.CancelButton = btnCancel;

            LoadBackups();
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

        private void LoadBackups()
        {
            _listBackups.Items.Clear();
            var dirs = Directory.GetDirectories(_backupRoot).Select(Path.GetFileName).OrderByDescending(n => n).ToArray();
            _listBackups.Items.AddRange(dirs);
            if (_listBackups.Items.Count > 0) _listBackups.SelectedIndex = 0;
        }

        private void BtnManual_Click(object sender, EventArgs e)
        {
            try
            {
                string dest = Path.Combine(_backupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                Directory.CreateDirectory(dest);
                string[] files = { _dataService.TasksFile, _dataService.ProjectsFile, _dataService.CategoriesFile, _dataService.EventsFile, _dataService.TimeLogsFile, _dataService.SettingsFile, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "holidays.json") };
                foreach (var f in files) if (File.Exists(f)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
                
                MessageBox.Show("バックアップが完了しました。", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadBackups();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void BtnRestore_Click(object sender, EventArgs e)
        {
            if (_listBackups.SelectedItem == null) return;
            string selected = _listBackups.SelectedItem.ToString();
            
            if (MessageBox.Show(string.Format("バックアップ '{0}' から復元します。\n現在のデータは上書きされます。よろしいですか？", selected), "復元の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    string srcDir = Path.Combine(_backupRoot, selected);
                    foreach (var file in Directory.GetFiles(srcDir))
                        File.Copy(file, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileName(file)), true);
                    
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "復元エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
