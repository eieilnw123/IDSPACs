// APIs/DashboardController.cs
using Microsoft.AspNetCore.Mvc;
using idspacsgateway.Models;
using idspacsgateway.Services;
using WorklistServiceApp.Data;
using WorklistServiceApp.Services;
using WorklistServiceApp.Models;

namespace idspacsgateway.APIs
{
    [ApiController]
    [Route("api/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly DatabaseService _databaseService;
        private readonly PdfMonitoringService _pdfMonitoringService;
        private readonly PdfProcessingService _pdfProcessingService;
        private readonly DicomCreationService _dicomCreationService;
        private readonly WorklistSyncService _worklistSyncService;
        private readonly LoggingService _loggingService;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            DatabaseService databaseService,
            PdfMonitoringService pdfMonitoringService,
            PdfProcessingService pdfProcessingService,
            DicomCreationService dicomCreationService,
            WorklistSyncService worklistSyncService,
            LoggingService loggingService,
            ILogger<DashboardController> logger)
        {
            _databaseService = databaseService;
            _pdfMonitoringService = pdfMonitoringService;
            _pdfProcessingService = pdfProcessingService;
            _dicomCreationService = dicomCreationService;
            _worklistSyncService = worklistSyncService;
            _loggingService = loggingService;
            _logger = logger;
        }

        [HttpGet("status")]
        public async Task<ActionResult<DashboardStatusResponse>> GetStatus()
        {
            try
            {
                var syncStats = await _databaseService.GetSyncStatistics();
                var statusSummary = await _databaseService.GetStatusSummary();
                var pdfStatus = _pdfMonitoringService.GetMonitoringStatus();
                var procStatus = _pdfProcessingService.GetProcessingStatus();
                var dicomStatus = _dicomCreationService.GetCreationStatus();

                var response = new DashboardStatusResponse
                {
                    // Worklist
                    TotalItems = syncStats.TotalItems,
                    CompletedItems = syncStats.CompletedItems,
                    PendingItems = syncStats.PendingItems,
                    FailedItems = syncStats.FailedItems,
                    TodayItems = syncStats.TodayItems,
                    LastSyncTime = syncStats.FormattedLastSync,
                    CompletionRate = syncStats.CompletionRate,

                    // PDF Monitoring
                    PdfPendingCount = pdfStatus.PendingFileCount,
                    PdfIsMonitoring = pdfStatus.IsMonitoring,
                    PdfFolderExists = pdfStatus.FolderExists,
                    PdfStatusText = pdfStatus.StatusText,

                    // PDF Processing
                    ProcQueueSize = procStatus.QueueSize,
                    ProcActiveCount = procStatus.ActiveProcessingCount,
                    ProcMaxConcurrent = procStatus.MaxConcurrentProcessing,
                    ProcTempFileCount = procStatus.TempFileCount,

                    // DICOM Creation
                    DicomQueueSize = dicomStatus.QueueSize,
                    DicomActiveCount = dicomStatus.ActiveCreationCount,
                    DicomFileCount = dicomStatus.DicomFileCount,
                    DicomMaxConcurrent = dicomStatus.MaxConcurrentCreation,

                    // Performance
                    SuccessRate = CalculateSuccessRate(statusSummary),
                    SystemHealth = DetermineSystemHealth(pdfStatus, procStatus, dicomStatus),
                    LastUpdated = DateTime.Now
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard status");
                return StatusCode(500, new { error = "Failed to get dashboard status" });
            }
        }

        [HttpGet("worklist")]
        public async Task<ActionResult<WorklistResponse>> GetWorklist(
            int page = 1,
            int pageSize = 10,
            string? status = null,
            string? search = null)
        {
            try
            {
                var items = await _databaseService.GetAllWorklistItemsAsync();

                // Apply filters
                if (!string.IsNullOrEmpty(status) && status != "all")
                {
                    items = items.Where(x => x.WorklistStatus == status).ToList();
                }

                if (!string.IsNullOrEmpty(search))
                {
                    var searchLower = search.ToLower();
                    items = items.Where(x =>
                        x.PatientID.ToLower().Contains(searchLower) ||
                        x.PatientName.ToLower().Contains(searchLower) ||
                        x.AccessionNumber.ToLower().Contains(searchLower)).ToList();
                }

                // Pagination
                var totalCount = items.Count;
                var pagedItems = items
                    .OrderByDescending(x => x.UpdatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(MapToDto)
                    .ToList();

                var response = new WorklistResponse
                {
                    Items = pagedItems,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting worklist");
                return StatusCode(500, new { error = "Failed to get worklist" });
            }
        }

        [HttpPost("actions/sync")]
        public async Task<ActionResult<ManualActionResponse>> TriggerManualSync()
        {
            try
            {
                await _loggingService.SendLogToClients("info", "🔄 Manual sync triggered by dashboard", "Dashboard");

                var result = await _worklistSyncService.ManualSync();

                var response = new ManualActionResponse
                {
                    Success = result.Success,
                    Message = result.Message
                };

                if (result.Success)
                {
                    await _loggingService.SendLogToClients("info", $"✅ Manual sync completed: {result.Message}", "Dashboard");
                }
                else
                {
                    await _loggingService.SendLogToClients("error", $"❌ Manual sync failed: {result.Message}", "Dashboard");
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in manual sync");
                await _loggingService.SendLogToClients("error", $"❌ Manual sync error: {ex.Message}", "Dashboard");
                return StatusCode(500, new ManualActionResponse { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("actions/retry-failed")]
        public async Task<ActionResult<ManualActionResponse>> RetryFailedItems()
        {
            try
            {
                await _loggingService.SendLogToClients("info", "🔄 Retrying failed items...", "Dashboard");

                var count = await _dicomCreationService.RetryFailedItems();

                var response = new ManualActionResponse
                {
                    Success = true,
                    Message = $"Retry completed: {count} items reprocessed",
                    Count = count
                };

                await _loggingService.SendLogToClients("info", $"✅ Retry completed: {count} items reprocessed", "Dashboard");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying failed items");
                await _loggingService.SendLogToClients("error", $"❌ Retry failed: {ex.Message}", "Dashboard");
                return StatusCode(500, new ManualActionResponse { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("actions/process-pending")]
        public async Task<ActionResult<ManualActionResponse>> ProcessPendingFiles()
        {
            try
            {
                await _loggingService.SendLogToClients("info", "📄 Processing pending PDF files...", "Dashboard");

                var count = await _pdfMonitoringService.ProcessAllPendingFiles();

                var response = new ManualActionResponse
                {
                    Success = true,
                    Message = $"Processing completed: {count} files processed",
                    Count = count
                };

                await _loggingService.SendLogToClients("info", $"✅ Processing completed: {count} files processed", "Dashboard");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pending files");
                await _loggingService.SendLogToClients("error", $"❌ Processing failed: {ex.Message}", "Dashboard");
                return StatusCode(500, new ManualActionResponse { Success = false, Message = ex.Message });
            }
        }

        // Helper methods
        private WorklistItemDto MapToDto(WorklistServiceApp.Models.WorklistItem item)
        {
            return new WorklistItemDto
            {
                AccessionNumber = item.AccessionNumber,
                PatientId = item.PatientID,
                PatientName = item.PatientName,
                PatientSex = item.PatientSex,
                PatientAge = item.PatientAge,
                Status = item.WorklistStatus,
                Progress = (int)item.ProgressPercentage,
                HasPdf = item.HasPdf,
                HasJpeg = item.HasJpeg,
                HasDicom = item.HasDicom,
                ScheduledTime = item.FormattedScheduledDateTime,
                UpdatedTime = item.UpdatedAt.ToString("HH:mm:ss"),
                ErrorMessage = item.ErrorMessage
            };
        }

        private double CalculateSuccessRate(Dictionary<string, int> statusSummary)
        {
            var total = statusSummary.Values.Sum();
            if (total == 0) return 0;

            var completed = statusSummary.GetValueOrDefault("COMPLETED", 0);
            var dicomCreated = statusSummary.GetValueOrDefault("DICOM_CREATED", 0);

            return ((double)(completed + dicomCreated) / total) * 100;
        }

        private string DetermineSystemHealth(
            WorklistServiceApp.Models.PdfMonitoringStatus pdfStatus,
            WorklistServiceApp.Models.PdfProcessingStatus procStatus,
            WorklistServiceApp.Models.DicomCreationStatus dicomStatus)
        {
            if (!pdfStatus.IsMonitoring || !pdfStatus.FolderExists)
                return "critical";

            if (procStatus.QueueSize > 20 || dicomStatus.QueueSize > 10)
                return "warning";

            return "healthy";
        }
    }
}