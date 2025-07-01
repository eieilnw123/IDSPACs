namespace WorklistServiceApp.Configuration
{
    public class PdfMonitoringConfiguration
    {
        public const string SectionName = "PdfMonitoring";

        /// <summary>
        /// Folder path to monitor for incoming PDF files
        /// </summary>
        public string WatchFolderPath { get; set; } = @"C:\Schiller\SchillerServers\_Data\Export\PDF";

        /// <summary>
        /// File filter pattern (e.g., "*.pdf")
        /// </summary>
        public string FileFilter { get; set; } = "*.pdf";

        /// <summary>
        /// Enable subdirectory monitoring
        /// </summary>
        public bool IncludeSubdirectories { get; set; } = false;

        /// <summary>
        /// Minimum file size in bytes (to avoid processing incomplete files)
        /// </summary>
        public long MinimumFileSizeBytes { get; set; } = 1024; // 1KB

        /// <summary>
        /// Maximum file size in bytes (to avoid processing huge files)
        /// </summary>
        public long MaximumFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB

        /// <summary>
        /// File stabilization delay in seconds (wait for file to be fully written)
        /// </summary>
        public int FileStabilizationDelaySeconds { get; set; } = 3;

        /// <summary>
        /// Enable automatic file processing when detected
        /// </summary>
        public bool EnableAutoProcessing { get; set; } = true;

        /// <summary>
        /// Move processed files to archive folder
        /// </summary>
        public bool ArchiveProcessedFiles { get; set; } = true;

        /// <summary>
        /// Archive folder path for processed PDF files
        /// </summary>
        public string ArchiveFolderPath { get; set; } = @"C:\EKG_Archive\PDF";

        /// <summary>
        /// Delete original files after successful processing
        /// </summary>
        public bool DeleteAfterProcessing { get; set; } = false;

        /// <summary>
        /// Health check interval in minutes
        /// </summary>
        public int HealthCheckIntervalMinutes { get; set; } = 5;

        /// <summary>
        /// Maximum pending files before triggering alert
        /// </summary>
        public int MaxPendingFilesAlert { get; set; } = 50;

        /// <summary>
        /// HN extraction patterns (regex patterns to extract HN from filename)
        /// </summary>
        public string[] HNExtractionPatterns { get; set; } = new[]
        {
            @"HN(\d{6,10})",           // HN123456
            @"(\d{6,10})_EKG",         // 123456_EKG
            @"(\d{6,10})\.pdf",        // 123456.pdf
            @"EKG_(\d{6,10})",         // EKG_123456
            @"[^\d](\d{6,10})[^\d]"    // Any 6-10 digit number
        };

        /// <summary>
        /// Backup HN extraction from PDF content if filename fails
        /// </summary>
        public bool EnableContentHNExtraction { get; set; } = true;

        /// <summary>
        /// PDF content HN extraction patterns
        /// </summary>
        public string[] ContentHNPatterns { get; set; } = new[]
        {
            @"HN\s*:?\s*(\d{6,10})",
            @"ผู้ป่วย\s*:?\s*(\d{6,10})",
            @"Patient\s*ID\s*:?\s*(\d{6,10})",
            @"รหัส\s*:?\s*(\d{6,10})"
        };

        /// <summary>
        /// Enable file locking check before processing
        /// </summary>
        public bool CheckFileLocking { get; set; } = true;

        /// <summary>
        /// Maximum attempts to access locked file
        /// </summary>
        public int MaxLockRetryAttempts { get; set; } = 5;

        /// <summary>
        /// Delay between lock retry attempts in seconds
        /// </summary>
        public int LockRetryDelaySeconds { get; set; } = 2;

        /// <summary>
        /// Enable duplicate file detection
        /// </summary>
        public bool EnableDuplicateDetection { get; set; } = true;

        /// <summary>
        /// Duplicate detection method: FileSize, Hash, or FileName
        /// </summary>
        public string DuplicateDetectionMethod { get; set; } = "Hash";

        /// <summary>
        /// Create watch folder if it doesn't exist
        /// </summary>
        public bool CreateWatchFolderIfNotExists { get; set; } = true;
    }
}