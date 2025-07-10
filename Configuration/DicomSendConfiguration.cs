namespace WorklistServiceApp.Configuration
{
    public class DicomSendConfiguration
    {
        public const string SectionName = "DicomSend";

        /// <summary>
        /// PACS Server IP Address
        /// </summary>
        public string PacsServerHost { get; set; } = "172.16.10.202";

        /// <summary>
        /// PACS Server Port
        /// </summary>
        public int PacsServerPort { get; set; } = 104;

        /// <summary>
        /// PACS Server AE Title
        /// </summary>
        public string PacsServerAE { get; set; } = "NETGATE1";

        /// <summary>
        /// Our Client AE Title for sending
        /// </summary>
        public string CallingAE { get; set; } = "Ekg_er";

        /// <summary>
        /// Maximum number of concurrent send operations
        /// </summary>
        public int MaxConcurrentSends { get; set; } = 2;

        /// <summary>
        /// Connection timeout in seconds
        /// </summary>
        public int ConnectionTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Association timeout in seconds
        /// </summary>
        public int AssociationTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// C-STORE timeout in seconds
        /// </summary>
        public int CStoreTimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Enable automatic retry on send failure
        /// </summary>
        public bool EnableAutoRetry { get; set; } = true;

        /// <summary>
        /// Maximum retry attempts per file
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Retry delay in seconds
        /// </summary>
        public int RetryDelaySeconds { get; set; } = 30;

        /// <summary>
        /// Exponential backoff for retries
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        /// <summary>
        /// Enable connection verification before sending
        /// </summary>
        public bool VerifyConnectionBeforeSend { get; set; } = true;

        /// <summary>
        /// Enable C-ECHO for connection testing
        /// </summary>
        public bool EnableCEcho { get; set; } = true;

        /// <summary>
        /// Queue processing interval in seconds
        /// </summary>
        public int QueueProcessingIntervalSeconds { get; set; } = 5;

        /// <summary>
        /// Maximum queue size before rejecting new files
        /// </summary>
        public int MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// Archive successfully sent files
        /// </summary>
        public bool ArchiveSentFiles { get; set; } = true;

        /// <summary>
        /// Archive folder for successfully sent DICOM files
        /// </summary>
        public string SentArchiveFolderPath { get; set; } = @"C:\EKG_Archive\DICOM_Sent";

        /// <summary>
        /// Archive failed send attempts
        /// </summary>
        public bool ArchiveFailedFiles { get; set; } = true;

        /// <summary>
        /// Archive folder for failed DICOM sends
        /// </summary>
        public string FailedArchiveFolderPath { get; set; } = @"C:\EKG_Archive\DICOM_Failed";

        /// <summary>
        /// Delete original files after successful send
        /// </summary>
        public bool DeleteAfterSuccessfulSend { get; set; } = false;

        /// <summary>
        /// Enable detailed logging for DICOM operations
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = true;

        /// <summary>
        /// Log successful sends
        /// </summary>
        public bool LogSuccessfulSends { get; set; } = true;

        /// <summary>
        /// Log failed sends
        /// </summary>
        public bool LogFailedSends { get; set; } = true;

        /// <summary>
        /// Enable send statistics
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Statistics reporting interval in minutes
        /// </summary>
        public int StatisticsReportingIntervalMinutes { get; set; } = 15;

        /// <summary>
        /// Health check interval for PACS connectivity
        /// </summary>
        public int HealthCheckIntervalMinutes { get; set; } = 5;

        /// <summary>
        /// Alternative PACS servers for failover
        /// </summary>
        public PacsServerConfig[]? AlternativeServers { get; set; }

        /// <summary>
        /// Enable failover to alternative servers
        /// </summary>
        public bool EnableFailover { get; set; } = false;

        /// <summary>
        /// Failover timeout before trying alternative server
        /// </summary>
        public int FailoverTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Supported Transfer Syntaxes for C-STORE
        /// </summary>
        public string[] SupportedTransferSyntaxes { get; set; } = new[]
        {
            "1.2.840.10008.1.2",       // Implicit VR Little Endian
            "1.2.840.10008.1.2.1",     // Explicit VR Little Endian
            "1.2.840.10008.1.2.2",     // Explicit VR Big Endian
            "1.2.840.10008.1.2.4.50",  // JPEG Baseline
            "1.2.840.10008.1.2.4.90",  // JPEG 2000 Lossless
            "1.2.840.10008.1.2.5"      // RLE Lossless
        };

        /// <summary>
        /// Preferred Transfer Syntax
        /// </summary>
        public string PreferredTransferSyntax { get; set; } = "1.2.840.10008.1.2.1";

        /// <summary>
        /// Maximum PDU size in bytes
        /// </summary>
        public uint MaxPDUSize { get; set; } = 16384;

        /// <summary>
        /// Enable compression for large files
        /// </summary>
        public bool EnableCompressionForLargeFiles { get; set; } = false;

        /// <summary>
        /// File size threshold for compression (bytes)
        /// </summary>
        public long CompressionThresholdBytes { get; set; } = 10 * 1024 * 1024; // 10MB

        /// <summary>
        /// Enable notification on send completion
        /// </summary>
        public bool EnableSendNotifications { get; set; } = false;

        /// <summary>
        /// Notification email addresses
        /// </summary>
        public string[]? NotificationEmails { get; set; }

        /// <summary>
        /// Send notification only on failures
        /// </summary>
        public bool NotifyOnFailuresOnly { get; set; } = true;
    }

    public class PacsServerConfig
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string AETitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}