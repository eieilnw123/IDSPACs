// Models/WorklistItem.cs
using System;

namespace WorklistServiceApp.Models
{
    public class WorklistItem
    {
        public int Id { get; set; }
        public string AccessionNumber { get; set; } = string.Empty;
        public string PatientID { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string PatientBirthDate { get; set; } = string.Empty; // YYYYMMDD format
        public string PatientSex { get; set; } = string.Empty;
        public string Modality { get; set; } = string.Empty;
        public string StudyDate { get; set; } = string.Empty;
        public string StudyTime { get; set; } = string.Empty;
        public string SeriesDate { get; set; } = string.Empty;
        public string SeriesTime { get; set; } = string.Empty;
        public string SeriesDescription { get; set; } = string.Empty;
        public string ScheduledDate { get; set; } = string.Empty;
        public string ScheduledTime { get; set; } = string.Empty;
        public string StationAET { get; set; } = string.Empty;
        public string ProcedureDescription { get; set; } = string.Empty;
        public string? StudyInstanceUID { get; set; }
        public string CharacterSet { get; set; } = string.Empty;
        public string WorklistStatus { get; set; } = "SCHEDULED";

        // File paths
        public string? PdfFilePath { get; set; }
        public string? JpegFilePath { get; set; }
        public string? DicomFilePath { get; set; }

        // Timestamps
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public DateTime? ProcessedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Processing info
        public string? ProcessingNotes { get; set; }
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; } = 0;

        // Computed properties
        public int? PatientAge
        {
            get
            {
                if (string.IsNullOrEmpty(PatientBirthDate) || PatientBirthDate.Length != 8)
                    return null;

                try
                {
                    var birthYear = int.Parse(PatientBirthDate.Substring(0, 4));
                    var birthMonth = int.Parse(PatientBirthDate.Substring(4, 2));
                    var birthDay = int.Parse(PatientBirthDate.Substring(6, 2));

                    var birthDate = new DateTime(birthYear, birthMonth, birthDay);
                    var today = DateTime.Today;

                    var age = today.Year - birthDate.Year;
                    if (birthDate.Date > today.AddYears(-age)) age--;

                    return age;
                }
                catch
                {
                    return null;
                }
            }
        }

        public string FormattedBirthDate
        {
            get
            {
                if (string.IsNullOrEmpty(PatientBirthDate) || PatientBirthDate.Length != 8)
                    return "";

                try
                {
                    var year = PatientBirthDate.Substring(0, 4);
                    var month = PatientBirthDate.Substring(4, 2);
                    var day = PatientBirthDate.Substring(6, 2);
                    return $"{day}/{month}/{year}";
                }
                catch
                {
                    return PatientBirthDate;
                }
            }
        }

        public string FormattedStudyDateTime
        {
            get
            {
                var dateStr = "";
                var timeStr = "";

                // Format study date
                if (!string.IsNullOrEmpty(StudyDate) && StudyDate.Length == 8)
                {
                    try
                    {
                        var year = StudyDate.Substring(0, 4);
                        var month = StudyDate.Substring(4, 2);
                        var day = StudyDate.Substring(6, 2);
                        dateStr = $"{day}/{month}/{year}";
                    }
                    catch
                    {
                        dateStr = StudyDate;
                    }
                }

                // Format study time
                if (!string.IsNullOrEmpty(StudyTime))
                {
                    try
                    {
                        if (StudyTime.Length >= 6)
                        {
                            var hour = StudyTime.Substring(0, 2);
                            var minute = StudyTime.Substring(2, 2);
                            var second = StudyTime.Substring(4, 2);
                            timeStr = $"{hour}:{minute}:{second}";
                        }
                        else if (StudyTime.Length >= 4)
                        {
                            var hour = StudyTime.Substring(0, 2);
                            var minute = StudyTime.Substring(2, 2);
                            timeStr = $"{hour}:{minute}";
                        }
                        else
                        {
                            timeStr = StudyTime;
                        }
                    }
                    catch
                    {
                        timeStr = StudyTime;
                    }
                }

                return $"{dateStr} {timeStr}".Trim();
            }
        }

        public string FormattedSeriesDateTime
        {
            get
            {
                var dateStr = "";
                var timeStr = "";

                // Format series date
                if (!string.IsNullOrEmpty(SeriesDate) && SeriesDate.Length == 8)
                {
                    try
                    {
                        var year = SeriesDate.Substring(0, 4);
                        var month = SeriesDate.Substring(4, 2);
                        var day = SeriesDate.Substring(6, 2);
                        dateStr = $"{day}/{month}/{year}";
                    }
                    catch
                    {
                        dateStr = SeriesDate;
                    }
                }

                // Format series time (already formatted as HH:MM:SS)
                if (!string.IsNullOrEmpty(SeriesTime))
                {
                    timeStr = SeriesTime;
                }

                return $"{dateStr} {timeStr}".Trim();
            }
        }

        public string FormattedScheduledDateTime
        {
            get
            {
                var dateStr = "";
                var timeStr = "";

                // Format scheduled date
                if (!string.IsNullOrEmpty(ScheduledDate) && ScheduledDate.Length == 8)
                {
                    try
                    {
                        var year = ScheduledDate.Substring(0, 4);
                        var month = ScheduledDate.Substring(4, 2);
                        var day = ScheduledDate.Substring(6, 2);
                        dateStr = $"{day}/{month}/{year}";
                    }
                    catch
                    {
                        dateStr = ScheduledDate;
                    }
                }

                // Format scheduled time
                if (!string.IsNullOrEmpty(ScheduledTime))
                {
                    try
                    {
                        if (ScheduledTime.Length >= 6)
                        {
                            var hour = ScheduledTime.Substring(0, 2);
                            var minute = ScheduledTime.Substring(2, 2);
                            var second = ScheduledTime.Substring(4, 2);
                            timeStr = $"{hour}:{minute}:{second}";
                        }
                        else if (ScheduledTime.Length >= 4)
                        {
                            var hour = ScheduledTime.Substring(0, 2);
                            var minute = ScheduledTime.Substring(2, 2);
                            timeStr = $"{hour}:{minute}";
                        }
                        else
                        {
                            timeStr = ScheduledTime;
                        }
                    }
                    catch
                    {
                        timeStr = ScheduledTime;
                    }
                }

                return $"{dateStr} {timeStr}".Trim();
            }
        }

        public string StatusDisplayName
        {
            get
            {
                return WorklistStatus switch
                {
                    "SCHEDULED" => "📋 รอไฟล์ PDF",
                    "PDF_RECEIVED" => "📄 ได้รับ PDF",
                    "JPEG_GENERATED" => "🖼️ แปลงเป็น JPEG",
                    "DICOM_CREATED" => "🏥 สร้าง DICOM",
                    "DICOM_SENT" => "📤 ส่งไป PACS",
                    "COMPLETED" => "✅ เสร็จสิ้น",
                    "FAILED" => "❌ ผิดพลาด",
                    "CANCELLED" => "⏹️ ยกเลิก",
                    _ => $"❓ {WorklistStatus}"
                };
            }
        }

        public string StatusBadgeClass
        {
            get
            {
                return WorklistStatus switch
                {
                    "SCHEDULED" => "badge bg-secondary",
                    "PDF_RECEIVED" => "badge bg-primary",
                    "JPEG_GENERATED" => "badge bg-info",
                    "DICOM_CREATED" => "badge bg-warning",
                    "DICOM_SENT" => "badge bg-success",
                    "COMPLETED" => "badge bg-success",
                    "FAILED" => "badge bg-danger",
                    "CANCELLED" => "badge bg-dark",
                    _ => "badge bg-light text-dark"
                };
            }
        }

        public bool HasPdf => !string.IsNullOrEmpty(PdfFilePath);
        public bool HasJpeg => !string.IsNullOrEmpty(JpegFilePath);
        public bool HasDicom => !string.IsNullOrEmpty(DicomFilePath);

        public string ProcessingProgress
        {
            get
            {
                var steps = new List<string>();

                if (HasPdf) steps.Add("📄 PDF");
                if (HasJpeg) steps.Add("🖼️ JPEG");
                if (HasDicom) steps.Add("🏥 DICOM");

                return steps.Any() ? string.Join(" → ", steps) : "⏳ รอการประมวลผล";
            }
        }

        public double ProgressPercentage
        {
            get
            {
                return WorklistStatus switch
                {
                    "SCHEDULED" => 10,
                    "PDF_RECEIVED" => 25,
                    "JPEG_GENERATED" => 50,
                    "DICOM_CREATED" => 75,
                    "DICOM_SENT" => 90,
                    "COMPLETED" => 100,
                    "FAILED" => 0,
                    "CANCELLED" => 0,
                    _ => 0
                };
            }
        }

        public string ProgressBarClass
        {
            get
            {
                return WorklistStatus switch
                {
                    "COMPLETED" => "progress-bar bg-success",
                    "FAILED" => "progress-bar bg-danger",
                    "CANCELLED" => "progress-bar bg-secondary",
                    _ => "progress-bar"
                };
            }
        }

        public TimeSpan? ProcessingDuration
        {
            get
            {
                if (ProcessedAt.HasValue)
                {
                    var endTime = CompletedAt ?? DateTime.Now;
                    return endTime - CreatedAt;
                }
                return null;
            }
        }

        public string FormattedProcessingDuration
        {
            get
            {
                var duration = ProcessingDuration;
                if (!duration.HasValue) return "";

                if (duration.Value.TotalHours >= 1)
                    return $"{duration.Value.Hours}h {duration.Value.Minutes}m";
                else if (duration.Value.TotalMinutes >= 1)
                    return $"{duration.Value.Minutes}m {duration.Value.Seconds}s";
                else
                    return $"{duration.Value.Seconds}s";
            }
        }

        public bool IsOverdue
        {
            get
            {
                if (string.IsNullOrEmpty(ScheduledDate)) return false;

                try
                {
                    var scheduledDateTime = DateTime.ParseExact(ScheduledDate, "yyyyMMdd", null);
                    return DateTime.Now.Date > scheduledDateTime.Date &&
                           WorklistStatus == "SCHEDULED";
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool IsToday
        {
            get
            {
                if (string.IsNullOrEmpty(ScheduledDate)) return false;

                try
                {
                    var scheduledDateTime = DateTime.ParseExact(ScheduledDate, "yyyyMMdd", null);
                    return DateTime.Now.Date == scheduledDateTime.Date;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string PatientDisplayName
        {
            get
            {
                var parts = new List<string>();

                if (!string.IsNullOrEmpty(PatientName))
                    parts.Add(PatientName);

                if (!string.IsNullOrEmpty(PatientSex))
                    parts.Add($"({PatientSex})");

                if (PatientAge.HasValue)
                    parts.Add($"{PatientAge}ปี");

                return parts.Any() ? string.Join(" ", parts) : PatientID;
            }
        }

        public override string ToString()
        {
            return $"[{PatientID}] {PatientName} - {WorklistStatus} ({AccessionNumber})";
        }
    }

    // Models/WorkflowStatus.cs
    public enum WorklistStatus
    {
        SCHEDULED,      // ดึง Worklist มาแล้ว รอ PDF
        PDF_RECEIVED,   // ได้รับไฟล์ PDF แล้ว
        JPEG_GENERATED, // แปลง PDF เป็น JPEG แล้ว
        DICOM_CREATED,  // สร้าง DICOM แล้ว
        DICOM_SENT,     // ส่ง DICOM แล้ว
        COMPLETED,      // เสร็จสิ้นทั้งหมด
        FAILED,         // ผิดพลาด
        CANCELLED       // ยกเลิก
    }

    public enum DatabaseAction
    {
        Inserted,
        Updated,
        Skipped
    }

    public class DatabaseOperationResult
    {
        public DatabaseAction Action { get; set; }
        public string AccessionNumber { get; set; } = string.Empty;
        public string PatientID { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Action}: {PatientID} ({PatientName}) - {Message}";
        }
    }

    // Status reporting classes
    public class WorklistSyncStats
    {
        public DateTime LastSyncTime { get; set; }
        public int TotalItems { get; set; }
        public int TodayItems { get; set; }
        public int PendingItems { get; set; }
        public int CompletedItems { get; set; }
        public int FailedItems { get; set; }

        public double CompletionRate => TotalItems > 0 ? (double)CompletedItems / TotalItems * 100 : 0;
        public double FailureRate => TotalItems > 0 ? (double)FailedItems / TotalItems * 100 : 0;

        public string FormattedLastSync => LastSyncTime == DateTime.MinValue ?
            "ยังไม่เคย sync" : LastSyncTime.ToString("dd/MM/yyyy HH:mm:ss");
    }

    public class WorklistSyncResult
    {
        public bool Success { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; } = string.Empty;
        public int NewItems { get; set; }
        public int UpdatedItems { get; set; }
        public int SkippedItems { get; set; }

        public int TotalProcessed => NewItems + UpdatedItems + SkippedItems;

        public string FormattedDuration
        {
            get
            {
                if (Duration.TotalHours >= 1)
                    return $"{Duration.Hours}h {Duration.Minutes}m {Duration.Seconds}s";
                else if (Duration.TotalMinutes >= 1)
                    return $"{Duration.Minutes}m {Duration.Seconds}s";
                else
                    return $"{Duration.TotalSeconds:F1}s";
            }
        }
    }

    // Service Status Classes
    public class PdfMonitoringStatus
    {
        public bool IsMonitoring { get; set; }
        public string WatchFolder { get; set; } = string.Empty;
        public bool FolderExists { get; set; }
        public int PendingFileCount { get; set; }
        public DateTime LastHealthCheck { get; set; }

        public string StatusText => IsMonitoring ? "🟢 กำลังตรวจสอบ" : "🔴 หยุดทำงาน";
        public string FolderStatusText => FolderExists ? "✅ พบโฟลเดอร์" : "❌ ไม่พบโฟลเดอร์";
    }

    public class PdfProcessingStatus
    {
        public int QueueSize { get; set; }
        public int ActiveProcessingCount { get; set; }
        public int MaxConcurrentProcessing { get; set; }
        public string TempJpegFolder { get; set; } = string.Empty;
        public int TempFileCount { get; set; }
        public DateTime LastStatusCheck { get; set; }

        public bool IsIdle => QueueSize == 0 && ActiveProcessingCount == 0;
        public bool IsBusy => ActiveProcessingCount >= MaxConcurrentProcessing;
        public string StatusText => IsIdle ? "⏸️ รอไฟล์" : IsBusy ? "🔥 ประมวลผลเต็มที่" : "⚡ กำลังประมวลผล";
    }

    public class DicomCreationStatus
    {
        public int QueueSize { get; set; }
        public int ActiveCreationCount { get; set; }
        public int MaxConcurrentCreation { get; set; }
        public string DicomOutputFolder { get; set; } = string.Empty;
        public int DicomFileCount { get; set; }
        public DateTime LastStatusCheck { get; set; }

        public bool IsIdle => QueueSize == 0 && ActiveCreationCount == 0;
        public bool IsBusy => ActiveCreationCount >= MaxConcurrentCreation;
        public string StatusText => IsIdle ? "⏸️ รอไฟล์" : IsBusy ? "🔥 สร้างเต็มที่" : "⚡ กำลังสร้าง DICOM";
    }

    public class DicomSendStatus
    {
        public int QueueSize { get; set; }
        public int ActiveSendCount { get; set; }
        public int MaxConcurrentSends { get; set; }
        public string PacsIP { get; set; } = string.Empty;
        public int PacsPort { get; set; }
        public string PacsAET { get; set; } = string.Empty;
        public int SentArchiveCount { get; set; }
        public int FailedArchiveCount { get; set; }
        public DateTime LastStatusCheck { get; set; }

        public bool IsIdle => QueueSize == 0 && ActiveSendCount == 0;
        public bool IsBusy => ActiveSendCount >= MaxConcurrentSends;
        public string StatusText => IsIdle ? "⏸️ รอไฟล์" : IsBusy ? "🔥 ส่งเต็มที่" : "⚡ กำลังส่ง DICOM";
        public string PacsConnection => $"{PacsIP}:{PacsPort} ({PacsAET})";
    }

    // Event Args Classes
    public class PdfFileDetectedEventArgs : EventArgs
    {
        public string PdfFilePath { get; set; } = string.Empty;
        public WorklistItem? WorklistItem { get; set; }
        public string ExtractedHN { get; set; } = string.Empty;
        public string ProcessingSource { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"PDF detected: {Path.GetFileName(PdfFilePath)} -> {ExtractedHN}";
        }
    }

    public class PdfProcessingCompletedEventArgs : EventArgs
    {
        public WorklistItem WorklistItem { get; set; } = new();
        public string PdfFilePath { get; set; } = string.Empty;
        public string? JpegFilePath { get; set; }
        public TimeSpan ProcessingDuration { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public override string ToString()
        {
            var status = Success ? "✅ Success" : "❌ Failed";
            return $"PDF Processing {status}: {WorklistItem.PatientID} in {ProcessingDuration.TotalSeconds:F1}s";
        }
    }

    public class DicomCreationCompletedEventArgs : EventArgs
    {
        public WorklistItem WorklistItem { get; set; } = new();
        public string JpegFilePath { get; set; } = string.Empty;
        public string? DicomFilePath { get; set; }
        public TimeSpan CreationDuration { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public override string ToString()
        {
            var status = Success ? "✅ Success" : "❌ Failed";
            return $"DICOM Creation {status}: {WorklistItem.PatientID} in {CreationDuration.TotalSeconds:F1}s";
        }
    }

    public class DicomSendCompletedEventArgs : EventArgs
    {
        public WorklistItem WorklistItem { get; set; } = new();
        public string DicomFilePath { get; set; } = string.Empty;
        public TimeSpan SendDuration { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? PacsResponse { get; set; }
        public int RetryCount { get; set; }

        public override string ToString()
        {
            var status = Success ? "✅ Success" : "❌ Failed";
            return $"DICOM Send {status}: {WorklistItem.PatientID} in {SendDuration.TotalSeconds:F1}s";
        }
    }

    // Workflow Summary Classes
    public class WorkflowSummary
    {
        public int AvailableWorklistItems { get; set; }  // Waiting for PDFs
        public int PendingPdfFiles { get; set; }         // PDFs in folder waiting to be processed
        public int CompletedToday { get; set; }          // Successfully completed today
        public Dictionary<string, int> CurrentStatusBreakdown { get; set; } = new();
        public bool PdfMonitoringActive { get; set; }
        public DateTime LastUpdated { get; set; }

        public int TotalActiveItems => CurrentStatusBreakdown.Values.Sum();
        public string FormattedLastUpdated => LastUpdated.ToString("HH:mm:ss");
    }

    public class WorkflowStatusReport
    {
        public Dictionary<string, int> DatabaseStatus { get; set; } = new();
        public WorklistSyncStats? SyncStatistics { get; set; }
        public PdfMonitoringStatus? PdfMonitoringStatus { get; set; }
        public PdfProcessingStatus? PdfProcessingStatus { get; set; }
        public DicomCreationStatus? DicomCreationStatus { get; set; }
        public DicomSendStatus? DicomSendStatus { get; set; }
        public DateTime GeneratedAt { get; set; }

        public bool IsHealthy =>
            (PdfMonitoringStatus?.IsMonitoring ?? false) &&
            (PdfMonitoringStatus?.FolderExists ?? false);

        public string OverallStatus => IsHealthy ? "🟢 ระบบทำงานปกติ" : "🔴 ระบบมีปัญหา";
    }

    public class WorkflowHealthCheck
    {
        public DateTime CheckTime { get; set; }
        public bool OverallHealthy { get; set; }
        public bool DatabaseHealthy { get; set; }
        public bool PdfFolderHealthy { get; set; }
        public bool PacsHealthy { get; set; }
        public bool ServiceQueuesHealthy { get; set; }
        public string? ErrorMessage { get; set; }

        public string HealthStatus
        {
            get
            {
                if (OverallHealthy) return "🟢 สุขภาพดี";
                if (!DatabaseHealthy) return "🔴 ปัญหา Database";
                if (!PdfFolderHealthy) return "🟡 ปัญหา PDF Folder";
                if (!PacsHealthy) return "🟡 ปัญหา PACS";
                if (!ServiceQueuesHealthy) return "🟡 ปัญหา Service Queue";
                return "🔴 ปัญหาไม่ทราบสาเหตุ";
            }
        }
    }
}