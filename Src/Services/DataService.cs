using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text;
using System.Web.Script.Serialization;
using TaskManager.Models;

namespace TaskManager.Services
{
    public class DataService
    {
        private readonly string _appRoot;
        private readonly JavaScriptSerializer _serializer;

        public string DataFolder { get { return Path.Combine(_appRoot, "Data"); } }
        
        public string TasksFile { get { return Path.Combine(DataFolder, "tasks.csv"); } }
        public string ProjectsFile { get { return Path.Combine(DataFolder, "projects.json"); } }
        public string CategoriesFile { get { return Path.Combine(DataFolder, "categories.json"); } }
        public string TemplatesFile { get { return Path.Combine(DataFolder, "templates.json"); } }
        public string SettingsFile { get { return Path.Combine(DataFolder, "config.json"); } }
        public string EventsFile { get { return Path.Combine(DataFolder, "events.json"); } }
        public string TimeLogsFile { get { return Path.Combine(DataFolder, "timelogs.json"); } }
        public string RecurringRulesFile { get { return Path.Combine(DataFolder, "recurring_rules.json"); } }
        public string StatusLogsFile { get { return Path.Combine(DataFolder, "status_logs.json"); } }
        public string ArchivedTasksFile { get { return Path.Combine(DataFolder, "archived_tasks.csv"); } }
        public string ArchivedProjectsFile { get { return Path.Combine(DataFolder, "archived_projects.json"); } }
        public string BackupsFolder { get { return Path.Combine(_appRoot, "backup"); } }

        public DataService(string appRoot)
        {
            _appRoot = appRoot;
            if (!Directory.Exists(DataFolder)) Directory.CreateDirectory(DataFolder);
            _serializer = new JavaScriptSerializer();
            _serializer.MaxJsonLength = int.MaxValue;
        }

        // --- JSON 読み書き ---
        public T LoadFromJson<T>(string filePath, T defaultValue = default(T))
        {
            if (!File.Exists(filePath)) return defaultValue;

            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return defaultValue;
                return _serializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(string.Format("JSON読み込みエラー ({0}):\n{1}", filePath, ex.Message), "エラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                return defaultValue;
            }
        }

        public void SaveToJson<T>(string filePath, T data)
        {
            try
            {
                string json = _serializer.Serialize(data);
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(string.Format("JSON保存エラー ({0}):\n{1}", filePath, ex.Message), "エラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        // --- 設定 (AppSettings) ---
        public AppSettings LoadSettings()
        {
            var defaultSettings = new AppSettings();
            var loadedSettings = LoadFromJson<AppSettings>(SettingsFile, null);
            
            if (loadedSettings != null)
            {
                SaveToJson(SettingsFile, loadedSettings); // 古いキーの削除と新しいキーの追加を兼ねて上書き保存
                return loadedSettings;
            }
            
            SaveToJson(SettingsFile, defaultSettings);
            return defaultSettings;
        }

        // --- タスク (CSV) 読み書き ---
        public List<TaskItem> LoadTasksFromCsv(string filePath)
        {
            if (!File.Exists(filePath)) return new List<TaskItem>();

            var tasks = new List<TaskItem>();
            try
            {
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                if (lines.Length <= 1) return tasks;

                var headers = ParseCsvLine(lines[0]);
                
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    
                    var values = ParseCsvLine(lines[i]);
                    var task = new TaskItem
                    {
                        ID = Guid.NewGuid().ToString(),
                        優先度 = "中",
                        進捗度 = "未実施",
                        通知設定 = "全体設定に従う",
                        保存日付 = DateTime.Now.ToString("yyyy-MM-dd")
                    };

                    for (int j = 0; j < headers.Count && j < values.Count; j++)
                    {
                        string header = headers[j];
                        string value = values[j];

                        switch (header)
                        {
                            case "ID": if (!string.IsNullOrEmpty(value)) task.ID = value; break;
                            case "ProjectID": task.ProjectID = value; break;
                            case "期日": task.期日 = value; break;
                            case "優先度": task.優先度 = value; break;
                            case "タスク": task.タスク = value; break;
                            case "進捗度": task.進捗度 = value; break;
                            case "通知設定": task.通知設定 = value; break;
                            case "カテゴリ": task.カテゴリ = value; break;
                            case "サブカテゴリ": task.サブカテゴリ = value; break;
                            case "保存日付": task.保存日付 = value; break;
                            case "完了日": task.完了日 = value; break;
                            case "TrackedTimeSeconds": 
                                double sec;
                                if (double.TryParse(value, out sec)) task.TrackedTimeSeconds = sec; 
                                break;
                            case "VisibleDate": task.VisibleDate = value; break;
                            case "ParentRuleID": task.ParentRuleID = value; break;
                            case "ArchivedDate": task.ArchivedDate = value; break;
                            case "ProjectName": task.ProjectName = value; break;
                            case "WorkFiles":
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    try { task.WorkFiles = _serializer.Deserialize<List<WorkFile>>(value) ?? new List<WorkFile>(); }
                                    catch { task.WorkFiles = new List<WorkFile>(); }
                                }
                                break;
                        }
                    }
                    tasks.Add(task);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(string.Format("CSV読み込みエラー:\n{0}", ex.Message), "エラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            }
            return tasks;
        }

        public void SaveTasksToCsv(string filePath, List<TaskItem> tasks)
        {
            try
            {
                var headers = new[] { "ID", "ProjectID", "タスク", "進捗度", "優先度", "期日", "カテゴリ", "サブカテゴリ", "通知設定", "保存日付", "完了日", "TrackedTimeSeconds", "WorkFiles", "VisibleDate" };
                
                using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    writer.WriteLine(string.Join(",", headers.Select(h => EscapeCsv(h))));
                    
                    foreach (var task in tasks)
                    {
                        var workFilesJson = task.WorkFiles != null && task.WorkFiles.Any() ? _serializer.Serialize(task.WorkFiles) : "";

                        var values = new[] { task.ID, task.ProjectID, task.タスク, task.進捗度, task.優先度, task.期日, task.カテゴリ, task.サブカテゴリ, task.通知設定, task.保存日付, task.完了日, task.TrackedTimeSeconds.ToString(), workFilesJson, task.VisibleDate };
                        writer.WriteLine(string.Join(",", values.Select(v => EscapeCsv(v))));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(string.Format("CSV書き込みエラー:\n{0}", ex.Message), "エラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        public void SaveArchivedTasksToCsv(List<TaskItem> tasks)
        {
            try
            {
                var headers = new[] { "ID", "ProjectID", "ProjectName", "タスク", "進捗度", "優先度", "期日", "カテゴリ", "サブカテゴリ", "通知設定", "保存日付", "完了日", "TrackedTimeSeconds", "WorkFiles", "ArchivedDate" };
                
                using (var writer = new StreamWriter(ArchivedTasksFile, false, Encoding.UTF8))
                {
                    writer.WriteLine(string.Join(",", headers.Select(h => EscapeCsv(h))));
                    
                    foreach (var task in tasks)
                    {
                        var workFilesJson = task.WorkFiles != null && task.WorkFiles.Any() ? _serializer.Serialize(task.WorkFiles) : "";
                        var values = new[] { task.ID, task.ProjectID, task.ProjectName, task.タスク, task.進捗度, task.優先度, task.期日, task.カテゴリ, task.サブカテゴリ, task.通知設定, task.保存日付, task.完了日, task.TrackedTimeSeconds.ToString(), workFilesJson, task.ArchivedDate };
                        writer.WriteLine(string.Join(",", values.Select(v => EscapeCsv(v))));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(string.Format("アーカイブCSV書き込みエラー:\n{0}", ex.Message), "エラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }

        private List<string> ParseCsvLine(string line) { /* RFC準拠の簡易CSVパーサー実装 */ var r=new List<string>(); bool q=false; var b=new StringBuilder(); for(int i=0;i<line.Length;i++){ char c=line[i]; if(q){ if(c=='\"'){ if(i+1<line.Length&&line[i+1]=='\"'){ b.Append('\"'); i++; }else{ q=false; } }else{ b.Append(c); } }else{ if(c=='\"'){ q=true; }else if(c==','){ r.Add(b.ToString()); b.Clear(); }else{ b.Append(c); } } } r.Add(b.ToString()); return r; }

        // --- バックアップ機能 ---
        public void StartAutomaticBackup(AppSettings settings)
        {
            try
            {
                string backupRoot = string.IsNullOrEmpty(settings.BackupPath) ? BackupsFolder : settings.BackupPath;
                if (!Directory.Exists(backupRoot)) Directory.CreateDirectory(backupRoot);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd");
                string backupSubFolder = Path.Combine(backupRoot, timestamp);
                
                if (!Directory.Exists(backupSubFolder))
                {
                    Directory.CreateDirectory(backupSubFolder);
                    string[] filesToBackup = { TasksFile, ProjectsFile, CategoriesFile, TemplatesFile, SettingsFile, EventsFile, TimeLogsFile, StatusLogsFile, RecurringRulesFile };
                    
                    foreach (string file in filesToBackup)
                    {
                        if (File.Exists(file)) File.Copy(file, Path.Combine(backupSubFolder, Path.GetFileName(file)), true);
                    }
                }

                // 古いバックアップの削除
                int retentionDays = settings.BackupRetentionDays > 0 ? settings.BackupRetentionDays : 30;
                DateTime retentionCutoff = DateTime.Now.AddDays(-retentionDays);

                foreach (var dir in Directory.GetDirectories(backupRoot))
                {
                    string dirName = Path.GetFileName(dir);
                    DateTime dirDate;
                    if (DateTime.TryParseExact(dirName, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out dirDate))
                        if (dirDate < retentionCutoff) Directory.Delete(dir, true);
                }

                foreach (var file in Directory.GetFiles(backupRoot, "*.zip"))
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    DateTime fileDate;
                    if (DateTime.TryParseExact(fileName, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out fileDate))
                        if (fileDate < retentionCutoff) File.Delete(file);
                }
            }
            catch { }
        }

        public void CompressOldArchives(AppSettings settings)
        {
            try
            {
                int compressionDays = settings.ArchiveCompressionDays > 0 ? settings.ArchiveCompressionDays : 90;
                DateTime cutoffDate = DateTime.Now.AddDays(-compressionDays);

                string backupRoot = string.IsNullOrEmpty(settings.BackupPath) ? BackupsFolder : settings.BackupPath;
                if (!Directory.Exists(backupRoot)) return;

                foreach (var dir in Directory.GetDirectories(backupRoot))
                {
                    string dirName = Path.GetFileName(dir);
                    DateTime dirDate;
                    if (DateTime.TryParseExact(dirName, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out dirDate))
                    {
                        if (dirDate < cutoffDate) { string zipPath = dir + ".zip"; if (!File.Exists(zipPath)) { ZipFile.CreateFromDirectory(dir, zipPath, CompressionLevel.Optimal, false); Directory.Delete(dir, true); } }
                    }
                }
            }
            catch { }
        }

        public void ManualBackup(string customBackupPath = null)
        {
            string backupRoot = string.IsNullOrEmpty(customBackupPath) ? BackupsFolder : customBackupPath;
            if (!Directory.Exists(backupRoot)) Directory.CreateDirectory(backupRoot);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string backupSubFolder = Path.Combine(backupRoot, timestamp);
            Directory.CreateDirectory(backupSubFolder);

            string[] filesToBackup = { TasksFile, ProjectsFile, CategoriesFile, TemplatesFile, SettingsFile, EventsFile, TimeLogsFile, StatusLogsFile, RecurringRulesFile, ArchivedTasksFile, ArchivedProjectsFile };
            foreach (string file in filesToBackup)
            {
                if (File.Exists(file)) File.Copy(file, Path.Combine(backupSubFolder, Path.GetFileName(file)), true);
            }
        }

        public bool RestoreBackup(string backupFolderName, string customBackupPath = null)
        {
            string backupRoot = string.IsNullOrEmpty(customBackupPath) ? BackupsFolder : customBackupPath;
            string backupPath = Path.Combine(backupRoot, backupFolderName);
            if (!Directory.Exists(backupPath)) return false;

            string[] filesToRestore = { TasksFile, ProjectsFile, CategoriesFile, TemplatesFile, SettingsFile, EventsFile, TimeLogsFile, StatusLogsFile, RecurringRulesFile, ArchivedTasksFile, ArchivedProjectsFile };
            bool restored = false;
            foreach (string dest in filesToRestore)
            {
                string fileName = Path.GetFileName(dest);
                string source = Path.Combine(backupPath, fileName);
                if (File.Exists(source))
                {
                    File.Copy(source, dest, true);
                    restored = true;
                }
            }
            return restored;
        }

        public List<string> GetBackupList(string customBackupPath = null)
        {
            string backupRoot = string.IsNullOrEmpty(customBackupPath) ? BackupsFolder : customBackupPath;
            if (!Directory.Exists(backupRoot)) return new List<string>();

            var dirs = Directory.GetDirectories(backupRoot).Select(Path.GetFileName).ToList();
            dirs.Sort();
            dirs.Reverse();
            return dirs;
        }
    }
}
