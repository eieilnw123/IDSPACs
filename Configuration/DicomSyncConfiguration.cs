using System.ComponentModel.DataAnnotations;

namespace WorklistServiceApp.Configuration
{
    public class DicomSyncConfiguration
    {
        public const string SectionName = "DicomSync";

        /// <summary>
        /// DICOM Server IP Address
        /// </summary>
        [Required]
        [RegularExpression(@"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$|^[\w\.-]+$",
            ErrorMessage = "ServerHost must be valid IP address or hostname")]
        public string ServerHost { get; set; } = "172.16.10.240";

        /// <summary>
        /// DICOM Server Port
        /// </summary>
        [Range(1, 65535, ErrorMessage = "ServerPort must be between 1-65535")]
        public int ServerPort { get; set; } = 204;

        /// <summary>
        /// DICOM Server AE Title
        /// </summary>
        [Required]
        [StringLength(16, MinimumLength = 1, ErrorMessage = "ServerAE must be 1-16 characters")]
        public string ServerAE { get; set; } = "WORKGATE";

        /// <summary>
        /// Our Client AE Title
        /// </summary>
        [Required]
        [StringLength(16, MinimumLength = 1, ErrorMessage = "CallingAE must be 1-16 characters")]
        public string CallingAE { get; set; } = "Ekg_er";

        /// <summary>
        /// Sync interval in minutes
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "SyncIntervalMinutes must be >= 1")]
        public int SyncIntervalMinutes { get; set; } = 1;

        /// <summary>
        /// Query days ahead (how many days in the future to query)
        /// </summary>
        //ถ้าเป็น 0 คือเอาแค่ Today
       [Range(-365, 365, ErrorMessage = "QueryDaysAhead must be between -365 to 365")]
        public int QueryDaysAhead { get; set; } = 0;

        /// <summary>
        /// Query days behind (how many days in the past to query)
        /// </summary>
        [Range(0, 365, ErrorMessage = "QueryDaysBehind must be between 0-365")]
        public int QueryDaysBehind { get; set; } = 1;

        /// <summary>
        /// Connection timeout in seconds
        /// </summary>
        [Range(5, 300, ErrorMessage = "ConnectionTimeoutSeconds must be between 5-300")]
        public int ConnectionTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum retry attempts
        /// </summary>
        [Range(0, 10, ErrorMessage = "MaxRetryAttempts must be between 0-10")]
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Retry delay in seconds
        /// </summary>
        [Range(1, 60, ErrorMessage = "RetryDelaySeconds must be between 1-60")]
        public int RetryDelaySeconds { get; set; } = 10;

        /// <summary>
        /// Maximum number of worklist items to process per sync
        /// </summary>
        [Range(1, 10000, ErrorMessage = "MaxItemsPerSync must be between 1-10000")]
        public int MaxItemsPerSync { get; set; } = 1000;

        // Properties ที่ไม่ต้อง validation
        public string FilterModality { get; set; } = "ECG"; // ใช้ single value แทน array
        public bool EnableAutoRetry { get; set; } = true;
        public bool EnableDicomLogging { get; set; } = false;
        public string[] SupportedCharacterSets { get; set; } = new[]
        {
            "ISO_IR 166", "ISO_IR 192", "TIS620", "UTF-8"
        };
        public string DefaultCharacterSet { get; set; } = "ISO_IR 166";
        public bool SyncOnStartup { get; set; } = true;
    }
}