using System;
using System.Collections.Generic;

namespace TaskManager.Models
{
    public class WorkFile
    {
        public string DisplayName { get; set; }
        public string Type { get; set; }
        public string Content { get; set; }
        public string DateAdded { get; set; }
    }

    public class TaskItem
    {
        public TaskItem()
        {
            WorkFiles = new List<WorkFile>();
        }

        public string ID { get; set; }
        public string ProjectID { get; set; }
        public string 期日 { get; set; } // PowerShell側のCSVヘッダーに合わせる
        public string 優先度 { get; set; }
        public string タスク { get; set; }
        public string 進捗度 { get; set; }
        public string 通知設定 { get; set; }
        public string カテゴリ { get; set; }
        public string サブカテゴリ { get; set; }
        public string 保存日付 { get; set; }
        public string 完了日 { get; set; }
        public double TrackedTimeSeconds { get; set; }
        public List<WorkFile> WorkFiles { get; set; }
        public string VisibleDate { get; set; }
        public string ParentRuleID { get; set; }
        public string ArchivedDate { get; set; } // アーカイブ用
        public string ProjectName { get; set; } // アーカイブ用
    }

    public class ProjectItem
    {
        public ProjectItem()
        {
            ProjectID = Guid.NewGuid().ToString();
            ProjectColor = "#D3D3D3";
            WorkFiles = new List<WorkFile>();
            AutoArchiveTasks = true;
            Notification = "全体設定に従う";
        }

        public string ProjectID { get; set; }
        public string ProjectName { get; set; }
        public string ProjectDueDate { get; set; }
        public List<WorkFile> WorkFiles { get; set; }
        public string Notification { get; set; }
        public string ProjectColor { get; set; }
        public bool AutoArchiveTasks { get; set; }
        public string ParentRuleID { get; set; }
        public string ArchivedDate { get; set; } // アーカイブ用
    }

    public class EventItem
    {
        public EventItem()
        {
            ID = Guid.NewGuid().ToString();
        }

        public string ID { get; set; }
        public string Title { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public bool IsAllDay { get; set; }
        public string ParentRuleID { get; set; }
        public string Status { get; set; }
    }

    public class TimeLog
    {
        public string ID { get; set; }
        public string TaskID { get; set; }
        public string Memo { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
    }

    public class StatusLog
    {
        public string TaskID { get; set; }
        public string OldStatus { get; set; }
        public string NewStatus { get; set; }
        public string Timestamp { get; set; }
    }

    public class RecurringRule
    {
        public RecurringRule()
        {
            RuleID = Guid.NewGuid().ToString();
            Type = "Task";
            TriggerModes = new List<string> { "OnExpiration" };
            CalculationBase = "Generation";
            IntervalDays = 1;
            IntervalUnit = "Day";
            Frequency = "毎日";
            PreGenDays = 0;
            WeekendShift = "None";
            HolidayShift = "None";
            Params = new Dictionary<string, object>();
            IsActive = true;
        }

        public string RuleID { get; set; }
        public string Type { get; set; }
        public string TaskName { get; set; }
        public List<string> TriggerModes { get; set; }
        public string CalculationBase { get; set; }
        public int IntervalDays { get; set; }
        public string IntervalUnit { get; set; }
        public string Frequency { get; set; }
        public int PreGenDays { get; set; }
        public string WeekendShift { get; set; }
        public string HolidayShift { get; set; }
        public Dictionary<string, object> Params { get; set; }
        public string NextRunDate { get; set; }
        public string TheoreticalDate { get; set; }
        public TaskItem BaseTask { get; set; }
        public bool IsActive { get; set; }
        public string CurrentInstanceID { get; set; }
        public string LastGeneratedDate { get; set; }
    }

    public class NotificationItem
    {
        public string Type { get; set; }
        public DateTime NotifyDate { get; set; }
        public DateTime DueDate { get; set; }
        public object ItemObject { get; set; }
    }
}
