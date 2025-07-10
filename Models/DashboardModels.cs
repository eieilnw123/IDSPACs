// Models/DashboardModels.cs
namespace idspacsgateway.Models
{
    public class DashboardStatusResponse
    {
        // Worklist
        public int TotalItems { get; set; }
        public int CompletedItems { get; set; }
        public int PendingItems { get; set; }
        public int FailedItems { get; set; }
        public int TodayItems { get; set; }
        public string LastSyncTime { get; set; } = "";
        public double CompletionRate { get; set; }

        // PDF Monitoring
        public int PdfPendingCount { get; set; }
        public bool PdfIsMonitoring { get; set; }
        public bool PdfFolderExists { get; set; }
        public string PdfStatusText { get; set; } = "";

        // PDF Processing
        public int ProcQueueSize { get; set; }
        public int ProcActiveCount { get; set; }
        public int ProcMaxConcurrent { get; set; }
        public int ProcTempFileCount { get; set; }

        // DICOM Creation
        public int DicomQueueSize { get; set; }
        public int DicomActiveCount { get; set; }
        public int DicomFileCount { get; set; }
        public int DicomMaxConcurrent { get; set; }

        //  DICOM Send 
        public int DicomSendQueueSize { get; set; }    // คิวส่ง DICOM
        public int DicomSendActiveCount { get; set; }  // กำลังส่ง
        public int PacsSentCount { get; set; }         // ส่งสำเร็จ
        public int PacsFailedCount { get; set; }       // ส่งล้มเหลว

        // Performance
        public double SuccessRate { get; set; }
        public string SystemHealth { get; set; } = "healthy";
        public DateTime LastUpdated { get; set; }
    }

    public class WorklistItemDto
    {
        public string AccessionNumber { get; set; } = "";
        public string PatientId { get; set; } = "";
        public string PatientName { get; set; } = "";
        public string PatientSex { get; set; } = "";
        public int? PatientAge { get; set; }
        public string Status { get; set; } = "";
        public int Progress { get; set; }
        public bool HasPdf { get; set; }
        public bool HasJpeg { get; set; }
        public bool HasDicom { get; set; }
        public string ScheduledTime { get; set; } = "";
        public string UpdatedTime { get; set; } = "";
        public string? ErrorMessage { get; set; }
    }

    public class WorklistResponse
    {
        public List<WorklistItemDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class LogEntry
    {
        public string Timestamp { get; set; } = "";
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
        public string Source { get; set; } = "";
    }

    public class FileLocationResponse
    {
        public string FileName { get; set; } = "";
        public bool Found { get; set; }
        public List<FileLocation> Locations { get; set; } = new();
    }

    public class FileLocation
    {
        public string Folder { get; set; } = "";
        public bool Exists { get; set; }
        public string? FilePath { get; set; }
        public long Size { get; set; }
        public string SizeFormatted { get; set; } = "";
        public DateTime? Created { get; set; }
    }

    public class ManualActionResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int? Count { get; set; }
    }
}