using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using UniConsul.Forms;

namespace TaskManager
{
    static class Program
    {
        // アプリ固有のミューテックス名（多重起動防止用）
        private static Mutex mutex = new Mutex(false, "TaskManager_Unique_App_Mutex_v13");

        [STAThread]
        static void Main()
        {
            // --- 多重起動の防止 ---
            if (!mutex.WaitOne(0, false))
            {
                MessageBox.Show("タスクマネージャーは既に起動しています。\nタスクトレイ等のアイコンを確認してください。", "起動エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return; // 既に起動している場合は2つ目を開かずに終了
            }

            // --- グローバルエラー処理（クラッシュ時にログを残す） ---
            Application.ThreadException += (sender, e) => { LogError(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => { LogError(e.ExceptionObject as Exception); };

            try
            {
                ApplicationConfiguration.Initialize();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new FormMain());
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        private static void LogError(Exception ex)
        {
            if (ex == null) return;
            try {
                string appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskManager");
                if (!Directory.Exists(appRoot)) Directory.CreateDirectory(appRoot);
                string logFile = Path.Combine(appRoot, "error_log.txt");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}]\r\n{ex.Message}\r\n{ex.StackTrace}\r\n----------------------------------------\r\n");
                MessageBox.Show("予期せぬエラーが発生しました。\n詳細は設定の「データフォルダを開く」から error_log.txt を確認してください。\n\n" + ex.Message, "システムエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } catch { }
        }
    }
}
