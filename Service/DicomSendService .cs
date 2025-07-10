using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using WorklistServiceApp.Configuration;
using WorklistServiceApp.Data;
using WorklistServiceApp.Models;
using idspacsgateway.Services;

namespace WorklistServiceApp.Services
{
    public class DicomSendService : BackgroundService
    {
        private readonly ILogger<DicomSendService> _logger;
        private readonly DatabaseService _databaseService;
        private readonly LoggingService _loggingService;
        private readonly DicomSendConfiguration _config;

        private readonly Queue<DicomSendTask> _sendQueue;
        private readonly SemaphoreSlim _sendSemaphore;
        private readonly object _queueLock = new object();
        private readonly Timer? _statisticsTimer;
        private readonly DicomSendStatistics _statistics;

        // Events
        public event EventHandler<DicomSendCompletedEventArgs>? DicomSendCompleted;

        public DicomSendService(
            ILogger<DicomSendService> logger,
            DatabaseService databaseService,
            LoggingService loggingService,
            IOptions<DicomSendConfiguration> config)
        {
            _logger = logger;
            _databaseService = databaseService;
            _loggingService = loggingService;
            _config = config.Value;
            _sendQueue = new Queue<DicomSendTask>();
            _sendSemaphore = new SemaphoreSlim(_config.MaxConcurrentSends, _config.MaxConcurrentSends);
            _statistics = new DicomSendStatistics();

            // Setup statistics timer if enabled
            if (_config.EnableStatistics && _config.StatisticsReportingIntervalMinutes > 0)
            {
                _statisticsTimer = new Timer(ReportStatistics, null,
                    TimeSpan.FromMinutes(_config.StatisticsReportingIntervalMinutes),
                    TimeSpan.FromMinutes(_config.StatisticsReportingIntervalMinutes));
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await InitializeService();

                _logger.LogInformation("📤 DICOM Send Service started");
                _logger.LogInformation("🏥 PACS Server: {Host}:{Port} ({AET})",
                    _config.PacsServerHost, _config.PacsServerPort, _config.PacsServerAE);
                _logger.LogInformation("⚙️ Max concurrent sends: {Max}", _config.MaxConcurrentSends);

                await _loggingService.SendLogToClients("info",
                    $"📤 DICOM Send Service started - PACS: {_config.PacsServerHost}:{_config.PacsServerPort}", "DicomSend");

                // Process pending items on startup
                await ProcessPendingDicomItems();

                // Main processing loop
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessQueue();
                        await Task.Delay(TimeSpan.FromSeconds(_config.QueueProcessingIntervalSeconds), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error in DICOM send loop");
                        await _loggingService.SendLogToClients("error", $"❌ DICOM send loop error: {ex.Message}", "DicomSend");
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Critical error in DICOM Send Service");
                await _loggingService.SendLogToClients("error", $"❌ Critical DICOM Send Service error: {ex.Message}", "DicomSend");
            }
        }

        private async Task InitializeService()
        {
            try
            {
                // Setup DICOM
                new DicomSetupBuilder()
                    .RegisterServices(s => s.AddFellowOakDicom())
                    .Build();

                // Create archive folders if enabled
                if (_config.ArchiveSentFiles)
                {
                    Directory.CreateDirectory(_config.SentArchiveFolderPath);
                }

                if (_config.ArchiveFailedFiles)
                {
                    Directory.CreateDirectory(_config.FailedArchiveFolderPath);
                }

                // Test PACS connection if enabled
                if (_config.VerifyConnectionBeforeSend)
                {
                    await TestPacsConnection();
                }

                _logger.LogInformation("⚙️ DICOM Send Service initialized");
                await _loggingService.SendLogToClients("info", "⚙️ DICOM Send Service initialized", "DicomSend");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize DICOM Send Service");
                await _loggingService.SendLogToClients("error", $"❌ DICOM Send Service init failed: {ex.Message}", "DicomSend");
                throw;
            }
        }

        public void QueueDicomForSend(WorklistItem worklistItem, string dicomFilePath)
        {
            // Check queue size limit
            lock (_queueLock)
            {
                if (_sendQueue.Count >= _config.MaxQueueSize)
                {
                    _logger.LogWarning("⚠️ DICOM send queue is full ({Count}/{Max}). Rejecting file: {PatientID}",
                        _sendQueue.Count, _config.MaxQueueSize, worklistItem.PatientID);
                    return;
                }
            }

            var task = new DicomSendTask
            {
                WorklistItem = worklistItem,
                DicomFilePath = dicomFilePath,
                QueuedAt = DateTime.Now,
                TaskId = Guid.NewGuid().ToString()
            };

            lock (_queueLock)
            {
                _sendQueue.Enqueue(task);
            }

            _statistics.ItemsQueued++;

            _logger.LogInformation("📤 DICOM queued for send: {PatientID} - {AccessionNumber} (TaskId: {TaskId}, Queue: {QueueCount})",
                worklistItem.PatientID, worklistItem.AccessionNumber, task.TaskId, _sendQueue.Count);

            _ = _loggingService.SendLogToClients("info",
                $"📤 DICOM queued: {worklistItem.PatientID} - Queue: {_sendQueue.Count}", "DicomSend");
        }

        private async Task ProcessQueue()
        {
            DicomSendTask? task = null;

            lock (_queueLock)
            {
                if (_sendQueue.Count > 0)
                {
                    task = _sendQueue.Dequeue();
                }
            }

            if (task != null)
            {
                // Process task in background to not block the queue
                _ = Task.Run(async () => await ProcessDicomSendTask(task));
            }
        }

        private async Task ProcessDicomSendTask(DicomSendTask task)
        {
            await _sendSemaphore.WaitAsync();

            try
            {
                _logger.LogInformation("📤 Starting DICOM send: {PatientID} - {AccessionNumber} (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, task.WorklistItem.AccessionNumber, task.TaskId);

                await _loggingService.SendLogToClients("info",
                    $"📤 Sending DICOM: {task.WorklistItem.PatientID}", "DicomSend");

                var startTime = DateTime.Now;
                _statistics.ItemsProcessing++;

                // Update status to sending
                await _databaseService.UpdateWorklistStatus(
                    task.WorklistItem.AccessionNumber,
                    WorklistStatus.DICOM_CREATED,
                    $"DICOM sending started (TaskId: {task.TaskId})");

                // Send DICOM file
                var success = await SendDicomFile(task.DicomFilePath, task.WorklistItem, task.TaskId);

                var endTime = DateTime.Now;
                var duration = endTime - startTime;

                if (success)
                {
                    _statistics.ItemsProcessed++;
                    _statistics.TotalSendTime += duration;

                    // Update database status
                    await _databaseService.UpdateWorklistStatus(
                        task.WorklistItem.AccessionNumber,
                        WorklistStatus.COMPLETED,
                        $"DICOM sent successfully (TaskId: {task.TaskId})");

                    // Archive sent file if enabled
                    if (_config.ArchiveSentFiles)
                    {
                        await ArchiveSentFile(task.DicomFilePath);
                    }

                    // Delete original file if enabled
                    if (_config.DeleteAfterSuccessfulSend)
                    {
                        await DeleteOriginalFile(task.DicomFilePath);
                    }

                    _logger.LogInformation("✅ DICOM send completed: {PatientID} - Duration: {Duration}ms (TaskId: {TaskId})",
                        task.WorklistItem.PatientID, duration.TotalMilliseconds, task.TaskId);

                    await _loggingService.SendLogToClients("info",
                        $"✅ DICOM sent: {task.WorklistItem.PatientID} ({duration.TotalSeconds:F1}s)", "DicomSend");

                    // Raise completion event
                    DicomSendCompleted?.Invoke(this, new DicomSendCompletedEventArgs
                    {
                        WorklistItem = task.WorklistItem,
                        DicomFilePath = task.DicomFilePath,
                        SendDuration = duration,
                        TaskId = task.TaskId,
                        Success = true
                    });
                }
                else
                {
                    await HandleSendFailure(task, "DICOM send failed");
                }

                _statistics.ItemsProcessing--;
            }
            catch (Exception ex)
            {
                _statistics.ItemsFailed++;
                _statistics.ItemsProcessing--;

                _logger.LogError(ex, "❌ DICOM send failed: {PatientID} (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, task.TaskId);

                await _loggingService.SendLogToClients("error",
                    $"❌ DICOM send failed: {task.WorklistItem.PatientID} - {ex.Message}", "DicomSend");

                await HandleSendFailure(task, ex.Message);
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        private async Task<bool> SendDicomFile(string dicomFilePath, WorklistItem worklistItem, string taskId)
        {
            try
            {
                if (!File.Exists(dicomFilePath))
                {
                    _logger.LogError("❌ DICOM file not found: {DicomPath}", dicomFilePath);
                    return false;
                }

                // Create DICOM client
                var client = DicomClientFactory.Create(
                    _config.PacsServerHost,
                    _config.PacsServerPort,
                    false,
                    _config.CallingAE,
                    _config.PacsServerAE);

                //// Configure timeouts
                //client.ServiceOptions.LogDataPDUs = _config.EnableDetailedLogging;
                //client.ServiceOptions.LogDimseDataPdus = _config.EnableDetailedLogging;

                // Test connection with C-ECHO if enabled
                if (_config.EnableCEcho)
                {
                    var echoRequest = new DicomCEchoRequest();
                    var echoSuccess = false;

                    echoRequest.OnResponseReceived += (req, response) =>
                    {
                        echoSuccess = response.Status == DicomStatus.Success;
                        _logger.LogDebug("C-ECHO Response: {Status}", response.Status);
                    };

                    await client.AddRequestAsync(echoRequest);
                    await client.SendAsync();

                    if (!echoSuccess)
                    {
                        _logger.LogError("❌ C-ECHO failed for PACS connection");
                        return false;
                    }
                }

                // Create C-STORE request
                var storeRequest = new DicomCStoreRequest(dicomFilePath);
                var storeSuccess = false;
                string? errorMessage = null;

                storeRequest.OnResponseReceived += (req, response) =>
                {
                    storeSuccess = response.Status == DicomStatus.Success;
                    if (!storeSuccess)
                    {
                        errorMessage = $"C-STORE failed with status: {response.Status}";
                    }

                    _logger.LogDebug("C-STORE Response: {Status} for file: {FileName}",
                        response.Status, Path.GetFileName(dicomFilePath));
                };

                // Send the DICOM file
                await client.AddRequestAsync(storeRequest);
                await client.SendAsync();

                if (!storeSuccess)
                {
                    _logger.LogError("❌ C-STORE failed: {Error}", errorMessage);
                    return false;
                }

                _logger.LogInformation("📤 DICOM file sent successfully: {FileName} to {Host}:{Port}",
                    Path.GetFileName(dicomFilePath), _config.PacsServerHost, _config.PacsServerPort);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DICOM send error: {DicomPath}", dicomFilePath);
                return false;
            }
        }

        private async Task TestPacsConnection()
        {
            try
            {
                _logger.LogInformation("🔌 Testing PACS connection: {Host}:{Port}",
                    _config.PacsServerHost, _config.PacsServerPort);

                var client = DicomClientFactory.Create(
                    _config.PacsServerHost,
                    _config.PacsServerPort,
                    false,
                    _config.CallingAE,
                    _config.PacsServerAE);

                var echoRequest = new DicomCEchoRequest();
                var success = false;

                echoRequest.OnResponseReceived += (req, response) =>
                {
                    success = response.Status == DicomStatus.Success;
                };

                await client.AddRequestAsync(echoRequest);
                await client.SendAsync();

                if (success)
                {
                    _logger.LogInformation("✅ PACS connection test successful");
                    await _loggingService.SendLogToClients("info", "✅ PACS connection test successful", "DicomSend");
                }
                else
                {
                    _logger.LogWarning("⚠️ PACS connection test failed");
                    await _loggingService.SendLogToClients("warning", "⚠️ PACS connection test failed", "DicomSend");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ PACS connection test error");
                await _loggingService.SendLogToClients("error", $"❌ PACS connection test error: {ex.Message}", "DicomSend");
            }
        }

        private async Task ProcessPendingDicomItems()
        {
            try
            {
                // Find items that have DICOM but not sent yet
                var pendingItems = await _databaseService.GetWorklistItemsByStatus(WorklistStatus.DICOM_CREATED);

                if (pendingItems.Any())
                {
                    _logger.LogInformation("📤 Found {Count} pending DICOM items to send", pendingItems.Count);
                    await _loggingService.SendLogToClients("info", $"📤 Found {pendingItems.Count} pending DICOM items", "DicomSend");

                    foreach (var item in pendingItems)
                    {
                        if (!string.IsNullOrEmpty(item.DicomFilePath) && File.Exists(item.DicomFilePath))
                        {
                            QueueDicomForSend(item, item.DicomFilePath);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ DICOM file missing for item: {AccessionNumber}", item.AccessionNumber);
                            await _databaseService.UpdateWorklistStatus(
                                item.AccessionNumber,
                                WorklistStatus.FAILED,
                                "DICOM file missing for send");
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("📤 No pending DICOM items found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing pending DICOM items");
                await _loggingService.SendLogToClients("error", $"❌ Error processing pending DICOM items: {ex.Message}", "DicomSend");
            }
        }

        private async Task HandleSendFailure(DicomSendTask task, string errorMessage)
        {
            try
            {
                // Check if retry is enabled and under retry limit
                if (_config.EnableAutoRetry && task.RetryCount < _config.MaxRetryAttempts)
                {
                    task.RetryCount++;
                    task.LastRetryAt = DateTime.Now;

                    var delay = _config.UseExponentialBackoff
                        ? TimeSpan.FromSeconds(_config.RetryDelaySeconds * Math.Pow(2, task.RetryCount - 1))
                        : TimeSpan.FromSeconds(_config.RetryDelaySeconds);

                    _logger.LogWarning("🔄 Retrying DICOM send (attempt {Retry}/{Max}): {PatientID} (TaskId: {TaskId}) in {Delay}s",
                        task.RetryCount, _config.MaxRetryAttempts, task.WorklistItem.PatientID, task.TaskId, delay.TotalSeconds);

                    await _loggingService.SendLogToClients("warning",
                        $"🔄 Retrying DICOM send: {task.WorklistItem.PatientID} (attempt {task.RetryCount}/{_config.MaxRetryAttempts})", "DicomSend");

                    // Add delay before retry
                    await Task.Delay(delay);

                    // Re-queue the task
                    lock (_queueLock)
                    {
                        _sendQueue.Enqueue(task);
                    }

                    return;
                }

                // Max retries reached or retry disabled
                await _databaseService.IncrementRetryCount(task.WorklistItem.AccessionNumber);
                await _databaseService.UpdateWorklistStatus(
                    task.WorklistItem.AccessionNumber,
                    WorklistStatus.FAILED,
                    $"DICOM send failed: {errorMessage} (TaskId: {task.TaskId})",
                    errorMessage);

                // Archive failed file if enabled
                if (_config.ArchiveFailedFiles)
                {
                    await ArchiveFailedFile(task.DicomFilePath);
                }

                // Raise failure event
                DicomSendCompleted?.Invoke(this, new DicomSendCompletedEventArgs
                {
                    WorklistItem = task.WorklistItem,
                    DicomFilePath = task.DicomFilePath,
                    TaskId = task.TaskId,
                    Success = false,
                    ErrorMessage = errorMessage
                });

                _logger.LogError("❌ DICOM send failed for {PatientID}: {Error} (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, errorMessage, task.TaskId);

                await _loggingService.SendLogToClients("error",
                    $"❌ DICOM send failed: {task.WorklistItem.PatientID} - {errorMessage}", "DicomSend");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error handling send failure");
            }
        }

        private async Task ArchiveSentFile(string dicomFilePath)
        {
            try
            {
                var fileName = Path.GetFileName(dicomFilePath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var newFileName = $"{timestamp}_{fileName}";
                var archivePath = Path.Combine(_config.SentArchiveFolderPath, newFileName);

                File.Copy(dicomFilePath, archivePath, overwrite: true);
                _logger.LogDebug("📦 DICOM file archived (sent): {ArchivePath}", archivePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to archive sent DICOM file: {DicomPath}", dicomFilePath);
            }
        }

        private async Task ArchiveFailedFile(string dicomFilePath)
        {
            try
            {
                var fileName = Path.GetFileName(dicomFilePath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var newFileName = $"{timestamp}_{fileName}";
                var archivePath = Path.Combine(_config.FailedArchiveFolderPath, newFileName);

                File.Copy(dicomFilePath, archivePath, overwrite: true);
                _logger.LogDebug("📦 DICOM file archived (failed): {ArchivePath}", archivePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to archive failed DICOM file: {DicomPath}", dicomFilePath);
            }
        }

        private async Task DeleteOriginalFile(string dicomFilePath)
        {
            try
            {
                if (File.Exists(dicomFilePath))
                {
                    File.Delete(dicomFilePath);
                    _logger.LogDebug("🗑️ Original DICOM file deleted: {DicomPath}", dicomFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to delete original DICOM file: {DicomPath}", dicomFilePath);
            }
        }

        private void ReportStatistics(object? state)
        {
            try
            {
                var avgSendTime = _statistics.ItemsProcessed > 0
                    ? _statistics.TotalSendTime.TotalMilliseconds / _statistics.ItemsProcessed
                    : 0;

                _logger.LogInformation("📊 DICOM Send Statistics: " +
                    "Queued: {Queued}, Sending: {Sending}, Sent: {Sent}, Failed: {Failed}, " +
                    "Avg Time: {AvgTime:F1}ms, Queue Size: {QueueSize}",
                    _statistics.ItemsQueued, _statistics.ItemsProcessing, _statistics.ItemsProcessed,
                    _statistics.ItemsFailed, avgSendTime, _sendQueue.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error reporting statistics");
            }
        }

        // Public methods for monitoring and control
        public DicomSendStatus GetSendStatus()
        {
            lock (_queueLock)
            {
                return new DicomSendStatus
                {
                    QueueSize = _sendQueue.Count,
                    ActiveSendCount = _config.MaxConcurrentSends - _sendSemaphore.CurrentCount,
                    MaxConcurrentSends = _config.MaxConcurrentSends,
                    PacsIP = _config.PacsServerHost,
                    PacsPort = _config.PacsServerPort,
                    PacsAET = _config.PacsServerAE,
                    SentArchiveCount = Directory.Exists(_config.SentArchiveFolderPath) ?
                        Directory.GetFiles(_config.SentArchiveFolderPath, "*.dcm").Length : 0,
                    FailedArchiveCount = Directory.Exists(_config.FailedArchiveFolderPath) ?
                        Directory.GetFiles(_config.FailedArchiveFolderPath, "*.dcm").Length : 0,
                    LastStatusCheck = DateTime.Now
                };
            }
        }

        public async Task<int> RetryFailedItems()
        {
            _logger.LogInformation("🔄 Retrying failed DICOM send items");
            await _loggingService.SendLogToClients("info", "🔄 Retrying failed DICOM send items", "DicomSend");

            var failedItems = await _databaseService.GetWorklistItemsByStatus(WorklistStatus.FAILED);
            var retryCount = 0;

            foreach (var item in failedItems.Where(x => !string.IsNullOrEmpty(x.DicomFilePath) && x.RetryCount < _config.MaxRetryAttempts))
            {
                if (File.Exists(item.DicomFilePath))
                {
                    QueueDicomForSend(item, item.DicomFilePath);
                    retryCount++;
                }
            }

            _logger.LogInformation("🔄 Queued {Count} items for retry", retryCount);
            await _loggingService.SendLogToClients("info", $"🔄 Queued {retryCount} items for DICOM send retry", "DicomSend");
            return retryCount;
        }

        public override void Dispose()
        {
            _statisticsTimer?.Dispose();
            _sendSemaphore?.Dispose();
            base.Dispose();
        }
    }

    // Supporting classes
    public class DicomSendTask
    {
        public string TaskId { get; set; } = string.Empty;
        public WorklistItem WorklistItem { get; set; } = new();
        public string DicomFilePath { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; }
        public int RetryCount { get; set; } = 0;
        public DateTime? LastRetryAt { get; set; }
    }

    public class DicomSendStatistics
    {
        public int ItemsQueued { get; set; }
        public int ItemsProcessing { get; set; }
        public int ItemsProcessed { get; set; }
        public int ItemsFailed { get; set; }
        public TimeSpan TotalSendTime { get; set; }
    }
}