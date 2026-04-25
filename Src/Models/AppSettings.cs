using System.Collections.Generic;

namespace TaskManager.Models
{
    public class AppSettings
    {
        public AppSettings()
        {
            WindowOpacity = 1.0;
            EnableSoundEffects = true;
            StartupView = "List";
            ShowTooltips = true;
            DateFormat = "yyyy/MM/dd";
            TimelineStartHour = 8;
            TimelineEndHour = 24;
            ListDensity = "Standard";
            ShowStrikethrough = true;
            ShowKanbanDone = true;
            ShowIcons = true;
            LeadTimeExcludeStatuses = new List<string>();
            BackupIntervalDays = 1;
            ArchiveCompressionDays = 90;
            LongTaskNotificationMinutes = 180;
            IdleTimeoutMinutes = 5;
            DoubleClickAction = "Edit";
            CalendarWeekStart = 0;
            ColorWeekend = true;
            NotificationButtonDays = 7;
            GlobalNotification = "当日";
            NotificationStyle = "Dialog";
            DayStartHour = 0;
            DefaultSort = "DueDate";
            AlertDaysRed = 0;
            AlertDaysYellow = 1;
            DefaultPriority = "中";
            DefaultDueOffset = 7;
            AutoArchiveDays = 30;
            AnalysisWarnPercent = 40;
            PomodoroWorkMinutes = 25;
            BackupRetentionDays = 30;
            AutoArchiveProjectsDays = 60;
            ArchiveTasksOnProjectCompletion = false;
            ArchiveTasksOnCompletion = false;
            TimeLogOverlapBehavior = "Error";
            EventNotificationEnabled = true;
            EventNotificationMinutes = 15;
            EnableEventOverlapWarning = true;
            EnableColorVisionSupport = false;
            WindowWidth = 1280;
            WindowHeight = 1024;
            MainSplitterDistance = 600;
            CalendarSplitterDistance = 840;
            CalendarLeftSplitterDistance = 400;
            FilesSplitterDistance = 380;
            RememberWindowSize = true;
            WindowSizes = new Dictionary<string, string>();
        }

        public bool RunAtStartup { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool AlwaysOnTop { get; set; }
        public double WindowOpacity { get; set; }
        public string Passcode { get; set; }
        public bool EnableSoundEffects { get; set; }
        public string StartupView { get; set; }
        public bool ShowTooltips { get; set; }
        public string DateFormat { get; set; }
        public int TimelineStartHour { get; set; }
        public int TimelineEndHour { get; set; }
        public string ListDensity { get; set; }
        public bool ShowStrikethrough { get; set; }
        public bool ShowKanbanDone { get; set; }
        public bool ShowIcons { get; set; }
        public bool IsDarkMode { get; set; }
        public bool HideCompletedTasks { get; set; }
        public List<string> LeadTimeExcludeStatuses { get; set; }
        public int BackupIntervalDays { get; set; }
        public string BackupPath { get; set; }
        public int ArchiveCompressionDays { get; set; }
        public int LongTaskNotificationMinutes { get; set; }
        public int IdleTimeoutMinutes { get; set; }
        public string DoubleClickAction { get; set; }
        public int CalendarWeekStart { get; set; }
        public bool ColorWeekend { get; set; }
        public int NotificationButtonDays { get; set; }
        public string GlobalNotification { get; set; }
        public string NotificationStyle { get; set; }
        public int DayStartHour { get; set; }
        public string DefaultSort { get; set; }
        public int AlertDaysRed { get; set; }
        public int AlertDaysYellow { get; set; }
        public string DefaultPriority { get; set; }
        public int DefaultDueOffset { get; set; }
        public int AutoArchiveDays { get; set; }
        public int AnalysisWarnPercent { get; set; }
        public int PomodoroWorkMinutes { get; set; }
        public int BackupRetentionDays { get; set; }
        public int AutoArchiveProjectsDays { get; set; }
        public bool ArchiveTasksOnProjectCompletion { get; set; }
        public bool ArchiveTasksOnCompletion { get; set; }
        public string TimeLogOverlapBehavior { get; set; }
        public bool EventNotificationEnabled { get; set; }
        public int EventNotificationMinutes { get; set; }
        public bool EnableEventOverlapWarning { get; set; }
        public bool EnableColorVisionSupport { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool RememberWindowSize { get; set; }
        public int MainSplitterDistance { get; set; }
        public int CalendarSplitterDistance { get; set; }
        public int CalendarLeftSplitterDistance { get; set; }
        public int FilesSplitterDistance { get; set; }
        public Dictionary<string, string> WindowSizes { get; set; }
    }
}
