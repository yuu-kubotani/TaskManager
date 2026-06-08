using System.Collections.Generic;

namespace UniConsul.Models
{
    public class AutoLogEntry
    {
        public string Timestamp { get; set; }
        public string ProcessName { get; set; }
        public string WindowTitle { get; set; }
        public string FilePath { get; set; }
        public int DurationSeconds { get; set; }
        public InferenceResult Inference { get; set; }
    }

    public class InferenceResult
    {
        public string TaskID { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public int TotalScore { get; set; }
        public Dictionary<string, int> ScoreDetails { get; set; }
    }

    public class AutoTrackerSettings
    {
        public bool EnableAutoTracking { get; set; }
        public int SmoothingMinutes { get; set; }
        public List<string> ExcludedProcesses { get; set; }
        public List<string> ExcludedKeywords { get; set; }
        public List<string> CommonPathExclusions { get; set; }
        public List<string> LearningDictionary { get; set; }
        public Dictionary<string, int> InferenceWeights { get; set; }

        public AutoTrackerSettings()
        {
            SmoothingMinutes = 5;
            ExcludedProcesses = new List<string>();
            ExcludedKeywords = new List<string>();
            CommonPathExclusions = new List<string>();
            LearningDictionary = new List<string>();
            InferenceWeights = new Dictionary<string, int>();
        }
    }
}

namespace UniConsul.Services
{
    // 自動記録のバックグラウンド処理を担うサービス（外枠）
    public class AutoTrackerService : System.IDisposable
    {
        private DataService _dataService;
        public bool IsManualTrackingActive { get; set; }

        public AutoTrackerService(DataService dataService)
        {
            _dataService = dataService;
        }

        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
}