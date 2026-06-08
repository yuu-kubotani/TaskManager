using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using UniConsul.Models;

namespace UniConsul.Services
{
    public class DataService
    {
        private static readonly object _fileLock = new object();
        private readonly string _appRoot;
        private readonly JsonSerializerOptions _jsonOptions;

        public static event EventHandler DataUpdated;

        public string AppRoot { get { return _appRoot; } }
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
        public string DailyReportsFile { get { return Path.Combine(DataFolder, "daily_reports.json"); } }
        public string BackupsFolder { get { return Path.Combine(_appRoot, "backup"); } }

        public string GetAutoLogsDirectory()
        {
            lock (_fileLock)
            {
                string path = Path.Combine(DataFolder, "AutoLogs");
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                return path;
            }
        }

        // --- 自動追跡ログのクリーンアップ機能 ---
        public int LogRetentionDays { get; set; } = 30;

        public void CleanupOldAutoLogs()
        {
            lock (_fileLock)
            {
                try
                {
                    string autoLogsDir = GetAutoLogsDirectory();
                    if (Directory.Exists(autoLogsDir))
                    {
                        DateTime threshold = DateTime.Today.AddDays(-LogRetentionDays);
                        foreach (string file in Directory.GetFiles(autoLogsDir, "*.json"))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(file);
                            DateTime logDate;
                            if (DateTime.TryParse(fileName, out logDate) && logDate < threshold)
                            {
                                File.Delete(file);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message); // デバッグ用ログは残す
                    System.Windows.Forms.MessageBox.Show($"データの処理中にエラーが発生しました。\n詳細: {ex.Message}", "システムエラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        public DataService(string appRoot)
        {
            lock (_fileLock)
            {
                // ユーザーローカルの AppData\Roaming\UniConsul フォルダをベースにする
                string appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UniConsul");
                
                // Dataフォルダと backup フォルダの物理パスをこの appDataRoot の下に紐付ける
                _appRoot = appDataRoot;
                string dataFolder = Path.Combine(appDataRoot, "Data");
                string backupFolder = Path.Combine(appDataRoot, "backup");

                // --- 既存データの自動移行 (マイグレーション) ---
                string oldDataFolder = Path.Combine(appRoot, "Data");
                string oldBackupFolder = Path.Combine(appRoot, "backup");
                string oldHolidays = Path.Combine(appRoot, "holidays.json");
                string newHolidays = Path.Combine(appDataRoot, "holidays.json");

                if (!Directory.Exists(dataFolder) && Directory.Exists(oldDataFolder))
                {
                    Directory.CreateDirectory(appDataRoot);
                    CopyDirectory(oldDataFolder, dataFolder);
                }
                if (!Directory.Exists(backupFolder) && Directory.Exists(oldBackupFolder))
                {
                    Directory.CreateDirectory(appDataRoot);
                    CopyDirectory(oldBackupFolder, backupFolder);
                }

                // 起動時にこれらのデータフォルダが存在しない場合は、自動でフォルダを作成する
                if (!Directory.Exists(dataFolder)) Directory.CreateDirectory(dataFolder);
                if (!Directory.Exists(backupFolder)) Directory.CreateDirectory(backupFolder);

                // holidays.json の初回コピー（インストーラーで配置されたファイルをAppDataへ）
                if (!File.Exists(newHolidays) && File.Exists(oldHolidays))
                {
                    try { File.Copy(oldHolidays, newHolidays, true); } catch { }
                }

                _jsonOptions = new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            try {
                Directory.CreateDirectory(destDir);
                foreach (string file in Directory.GetFiles(sourceDir)) File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
                foreach (string dir in Directory.GetDirectories(sourceDir)) CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            } catch (Exception ex) {
                Console.WriteLine("Data migration failed: " + ex.Message);
            }
        }

        // --- JSON 読み書き ---
        public T LoadFromJson<T>(string filePath, T defaultValue = default(T))
        {
            lock (_fileLock)
            {
                if (!File.Exists(filePath)) return defaultValue;

                try
                {
                    string json = File.ReadAllText(filePath, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(json)) return defaultValue;
                    return JsonSerializer.Deserialize<T>(json, _jsonOptions);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message); // デバッグ用ログは残す
                    System.Windows.Forms.MessageBox.Show($"データの処理中にエラーが発生しました。\n詳細: {ex.Message}", "システムエラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    return defaultValue;
                }
            }
        }

        public void SaveToJson<T>(string filePath, T data)
        {
            lock (_fileLock)
            {
                try
                {
                    string json = JsonSerializer.Serialize(data, _jsonOptions);
                    File.WriteAllText(filePath, json, Encoding.UTF8);
                    DataUpdated?.Invoke(null, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message); // デバッグ用ログは残す
                    System.Windows.Forms.MessageBox.Show($"データの処理中にエラーが発生しました。\n詳細: {ex.Message}", "システムエラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        // --- 設定 (AppSettings) ---
        public AppSettings LoadSettings()
        {
            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(SettingsFile))
                    {
                        string json = File.ReadAllText(SettingsFile, Encoding.UTF8);
                        
                        if (string.IsNullOrWhiteSpace(json))
                        {
                            throw new FormatException("Config file is empty.");
                        }
                        
                        var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                        if (loadedSettings != null)
                        {
                            string newJson = JsonSerializer.Serialize(loadedSettings, _jsonOptions);
                            if (json != newJson)
                            {
                                File.WriteAllText(SettingsFile, newJson, Encoding.UTF8);
                            }
                            return loadedSettings;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Config load failed, creating default: {ex.Message}");
                    
                    try
                    {
                        var defaultConfig = new AppSettings();
                        string defaultJson = JsonSerializer.Serialize(defaultConfig, _jsonOptions);
                        File.WriteAllText(SettingsFile, defaultJson, Encoding.UTF8);
                        return defaultConfig;
                    }
                    catch
                    {
                        return new AppSettings();
                    }
                }
                
                var defaultSettingsFallback = new AppSettings();
                SaveToJson(SettingsFile, defaultSettingsFallback);
                return defaultSettingsFallback;
            }
        }

        // --- タスク (CSV) 読み書き ---
        public List<TaskItem> LoadTasksFromCsv(string filePath)
        {
            lock (_fileLock)
            {
                if (!File.Exists(filePath)) return new List<TaskItem>();

                var tasks = new List<TaskItem>();
                try
                {
                    using (var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(filePath, Encoding.UTF8))
                    {
                        parser.TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited;
                        parser.SetDelimiters(",");
                        parser.HasFieldsEnclosedInQuotes = true;

                        if (!parser.EndOfData)
                        {
                            string[] headers = parser.ReadFields();
                            if (headers == null) return tasks;

                            while (!parser.EndOfData)
                            {
                                string[] values = parser.ReadFields();
                                if (values == null) continue;

                                var task = new TaskItem
                                {
                                    ID = Guid.NewGuid().ToString(),
                                    優先度 = "中",
                                    進捗度 = "未実施",
                                    通知設定 = "全体設定に従う",
                                    保存日付 = DateTime.Now.ToString("yyyy-MM-dd")
                                };

                                for (int j = 0; j < headers.Length && j < values.Length; j++)
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
                                                try { task.WorkFiles = JsonSerializer.Deserialize<List<WorkFile>>(value, _jsonOptions) ?? new List<WorkFile>(); }
                                                catch { task.WorkFiles = new List<WorkFile>(); }
                                            }
                                            break;
                                        case "TargetHours":
                                            double targetHrs;
                                            if (double.TryParse(value, out targetHrs)) task.TargetHours = targetHrs;
                                            break;
                                        case "StartedAt": task.StartedAt = value; break;
                                        case "CompletedAt": task.CompletedAt = value; break;
                                    }
                                }
                                tasks.Add(task);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message); // デバッグ用ログは残す
                    System.Windows.Forms.MessageBox.Show($"データの処理中にエラーが発生しました。\n詳細: {ex.Message}", "システムエラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
                return tasks;
            }
        }

        public void SaveTasksToCsv(string filePath, List<TaskItem> tasks)
        {
            lock (_fileLock)
            {
                try
                {
                    var headers = new[] { "ID", "ProjectID", "タスク", "進捗度", "優先度", "期日", "カテゴリ", "サブカテゴリ", "通知設定", "保存日付", "完了日", "TrackedTimeSeconds", "WorkFiles", "VisibleDate", "TargetHours", "StartedAt", "CompletedAt" };
                    
                    using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
                    {
                        writer.WriteLine(string.Join(",", headers.Select(h => EscapeCsv(h))));
                        
                        foreach (var task in tasks)
                        {
                            var workFilesJson = task.WorkFiles != null && task.WorkFiles.Any() ? JsonSerializer.Serialize(task.WorkFiles, _jsonOptions) : "";
                            var targetHrsStr = task.TargetHours.HasValue ? task.TargetHours.Value.ToString() : "";

                            var values = new[] { task.ID, task.ProjectID, task.タスク, task.進捗度, task.優先度, task.期日, task.カテゴリ, task.サブカテゴリ, task.通知設定, task.保存日付, task.完了日, task.TrackedTimeSeconds.ToString(), workFilesJson, task.VisibleDate, targetHrsStr, task.StartedAt, task.CompletedAt };
                            writer.WriteLine(string.Join(",", values.Select(v => EscapeCsv(v))));
                        }
                        DataUpdated?.Invoke(null, EventArgs.Empty);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message); // デバッグ用ログは残す
                    System.Windows.Forms.MessageBox.Show($"データの処理中にエラーが発生しました。\n詳細: {ex.Message}", "システムエラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        public void SaveArchivedTasksToCsv(List<TaskItem> tasks)
        {
            lock (_fileLock)
            {
                try
                {
                    var headers = new[] { "ID", "ProjectID", "ProjectName", "タスク", "進捗度", "優先度", "期日", "カテゴリ", "サブカテゴリ", "通知設定", "保存日付", "完了日", "TrackedTimeSeconds", "WorkFiles", "ArchivedDate", "TargetHours", "StartedAt", "CompletedAt" };
                    
                    using (var writer = new StreamWriter(ArchivedTasksFile, false, Encoding.UTF8))
                    {
                        writer.WriteLine(string.Join(",", headers.Select(h => EscapeCsv(h))));
                        
                        foreach (var task in tasks)
                        {
                            var workFilesJson = task.WorkFiles != null && task.WorkFiles.Any() ? JsonSerializer.Serialize(task.WorkFiles, _jsonOptions) : "";
                            var targetHrsStr = task.TargetHours.HasValue ? task.TargetHours.Value.ToString() : "";
                            var values = new[] { task.ID, task.ProjectID, task.ProjectName, task.タスク, task.進捗度, task.優先度, task.期日, task.カテゴリ, task.サブカテゴリ, task.通知設定, task.保存日付, task.完了日, task.TrackedTimeSeconds.ToString(), workFilesJson, task.ArchivedDate, targetHrsStr, task.StartedAt, task.CompletedAt };
                            writer.WriteLine(string.Join(",", values.Select(v => EscapeCsv(v))));
                        }
                        DataUpdated?.Invoke(null, EventArgs.Empty);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message); // デバッグ用ログは残す
                    System.Windows.Forms.MessageBox.Show($"データの処理中にエラーが発生しました。\n詳細: {ex.Message}", "システムエラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        private string EscapeCsv(string field)
        {
            if (field == null) return "\"\"";
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        // --- バックアップ機能 ---
        public void StartAutomaticBackup(AppSettings settings)
        {
            lock (_fileLock)
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
                        string[] filesToBackup = { TasksFile, ProjectsFile, CategoriesFile, TemplatesFile, SettingsFile, EventsFile, TimeLogsFile, StatusLogsFile, RecurringRulesFile, DailyReportsFile };
                        
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
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message); // デバッグ用ログは残す
                    System.Windows.Forms.MessageBox.Show($"データの処理中にエラーが発生しました。\n詳細: {ex.Message}", "システムエラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        public void CompressOldArchives(AppSettings settings)
        {
            lock (_fileLock)
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
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message); // デバッグ用ログは残す
                    System.Windows.Forms.MessageBox.Show($"データの処理中にエラーが発生しました。\n詳細: {ex.Message}", "システムエラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
        }

        public void ManualBackup(string customBackupPath = null)
        {
            lock (_fileLock)
            {
                string backupRoot = string.IsNullOrEmpty(customBackupPath) ? BackupsFolder : customBackupPath;
                if (!Directory.Exists(backupRoot)) Directory.CreateDirectory(backupRoot);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupSubFolder = Path.Combine(backupRoot, timestamp);
                Directory.CreateDirectory(backupSubFolder);

                string[] filesToBackup = { TasksFile, ProjectsFile, CategoriesFile, TemplatesFile, SettingsFile, EventsFile, TimeLogsFile, StatusLogsFile, RecurringRulesFile, ArchivedTasksFile, ArchivedProjectsFile, DailyReportsFile };
                foreach (string file in filesToBackup)
                {
                    if (File.Exists(file)) File.Copy(file, Path.Combine(backupSubFolder, Path.GetFileName(file)), true);
                }
            }
        }

        public bool RestoreBackup(string backupFolderName, string customBackupPath = null)
        {
            lock (_fileLock)
            {
                string backupRoot = string.IsNullOrEmpty(customBackupPath) ? BackupsFolder : customBackupPath;
                string backupPath = Path.Combine(backupRoot, backupFolderName);
                if (!Directory.Exists(backupPath)) return false;

                string[] filesToRestore = { TasksFile, ProjectsFile, CategoriesFile, TemplatesFile, SettingsFile, EventsFile, TimeLogsFile, StatusLogsFile, RecurringRulesFile, ArchivedTasksFile, ArchivedProjectsFile, DailyReportsFile };
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
        }

        public List<string> GetBackupList(string customBackupPath = null)
        {
            lock (_fileLock)
            {
                string backupRoot = string.IsNullOrEmpty(customBackupPath) ? BackupsFolder : customBackupPath;
                if (!Directory.Exists(backupRoot)) return new List<string>();

                var dirs = Directory.GetDirectories(backupRoot).Select(Path.GetFileName).ToList();
                dirs.Sort();
                dirs.Reverse();
                return dirs;
            }
        }

        public Dictionary<string, string> GetHolidays()
        {
            lock (_fileLock)
            {
                string path = Path.Combine(_appRoot, "holidays.json");
                if (File.Exists(path))
                {
                    try
                    {
                        var loaded = LoadFromJson<Dictionary<string, string>>(path, new Dictionary<string, string>());
                        if (loaded != null && loaded.Count > 0) return loaded;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message); // デバッグ用ログは残す
                        System.Windows.Forms.MessageBox.Show($"データの処理中にエラーが発生しました。\n詳細: {ex.Message}", "システムエラー", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    }
                }

                return new Dictionary<string, string> {
                    { "2026-01-01", "元日" }, { "2026-01-12", "成人の日" }, { "2026-02-11", "建国記念の日" }, { "2026-02-23", "天皇誕生日" },
                    { "2026-03-20", "春分の日" }, { "2026-04-29", "昭和の日" }, { "2026-05-03", "憲法記念日" }, { "2026-05-04", "みどりの日" },
                    { "2026-05-05", "こどもの日" }, { "2026-05-06", "振替休日" }, { "2026-07-20", "海の日" }, { "2026-08-11", "山の日" },
                    { "2026-09-21", "敬老の日" }, { "2026-09-22", "国民の休日" }, { "2026-09-23", "秋分の日" }, { "2026-10-12", "スポーツの日" },
                    { "2026-11-03", "文化の日" }, { "2026-11-23", "勤労感謝の日" }
                };
            }
        }
    }
}
