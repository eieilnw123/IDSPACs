using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using System.Drawing;
using System.Drawing.Imaging;
using WorklistServiceApp.Configuration;
using WorklistServiceApp.Data;
using WorklistServiceApp.Models;

namespace WorklistServiceApp.Services
{
    public class DicomCreationService : BackgroundService
    {
        private readonly ILogger<DicomCreationService> _logger;
        private readonly DatabaseService _databaseService;
        private readonly DicomCreationConfiguration _config;
        private readonly PdfProcessingConfiguration _pdfConfig;

        private readonly Queue<DicomCreationTask> _creationQueue;
        private readonly SemaphoreSlim _creationSemaphore;
        private readonly object _queueLock = new object();
        private readonly Timer? _statisticsTimer;
        private readonly DicomCreationStatistics _statistics;

        // Events
        public event EventHandler<DicomCreationCompletedEventArgs>? DicomCreationCompleted = null!;

        public DicomCreationService(
         ILogger<DicomCreationService> logger,
         DatabaseService databaseService,
         IOptions<DicomCreationConfiguration> config,
         IOptions<PdfProcessingConfiguration> pdfConfig)
        {
            _logger = logger;
            _databaseService = databaseService;
            _config = config.Value;
            _pdfConfig = pdfConfig.Value;
            _creationQueue = new Queue<DicomCreationTask>();
            _creationSemaphore = new SemaphoreSlim(_config.MaxConcurrentCreation, _config.MaxConcurrentCreation);
            _statistics = new DicomCreationStatistics();


        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await InitializeService();

                _logger.LogInformation("🏥 DICOM Creation Service started");
                _logger.LogInformation("📁 DICOM output folder: {Folder}", _config.DicomOutputFolderPath);
                _logger.LogInformation("⚙️ Max concurrent creation: {Max}", _config.MaxConcurrentCreation);
                _logger.LogInformation("🏥 Institution: {Institution}", _config.InstitutionName);

                // Process pending items on startup
                await ProcessPendingJpegItems();

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
                        _logger.LogError(ex, "❌ Error in DICOM creation loop");
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Critical error in DICOM Creation Service");
            }
        }

        private async Task InitializeService()
        {
            try
            {
                // Create output folder
                Directory.CreateDirectory(_config.DicomOutputFolderPath);

                // Create archive folders if enabled
                if (_config.ArchiveCreatedFiles)
                {
                    Directory.CreateDirectory(_config.ArchiveFolderPath);
                }

                _logger.LogInformation("⚙️ DICOM Creation Service initialized");
                _logger.LogInformation("📦 Transfer Syntax: {TransferSyntax}", _config.TransferSyntaxUID);
                _logger.LogInformation("🏷️ SOP Class: {SOPClass}", _config.SOPClassUID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize DICOM Creation Service");
                throw;
            }
        }

        public void QueueJpegForDicomCreation(WorklistItem worklistItem, string jpegFilePath)
        {
            // Check queue size limit
            lock (_queueLock)
            {
                if (_creationQueue.Count >= _config.MaxQueueSize)
                {
                    _logger.LogWarning("⚠️ DICOM creation queue is full ({Count}/{Max}). Rejecting new file: {PatientID}",
                        _creationQueue.Count, _config.MaxQueueSize, worklistItem.PatientID);
                    return;
                }
            }

            var task = new DicomCreationTask
            {
                WorklistItem = worklistItem,
                JpegFilePath = jpegFilePath,
                QueuedAt = DateTime.Now,
                TaskId = Guid.NewGuid().ToString()
            };

            lock (_queueLock)
            {
                _creationQueue.Enqueue(task);
            }

            _statistics.ItemsQueued++;

            _logger.LogInformation("🏥 JPEG queued for DICOM creation: {PatientID} - {AccessionNumber} (TaskId: {TaskId}, Queue: {QueueCount})",
                worklistItem.PatientID, worklistItem.AccessionNumber, task.TaskId, _creationQueue.Count);
        }

        private async Task ProcessQueue()
        {
            DicomCreationTask? task = null;

            lock (_queueLock)
            {
                if (_creationQueue.Count > 0)
                {
                    task = _creationQueue.Dequeue();
                }
            }

            if (task != null)
            {
                // Process task in background to not block the queue
                _ = Task.Run(async () => await ProcessDicomCreationTask(task));
            }
        }

        private async Task ProcessDicomCreationTask(DicomCreationTask task)
        {
            await _creationSemaphore.WaitAsync();

            try
            {
                _logger.LogInformation("🔄 Starting DICOM creation: {PatientID} - {AccessionNumber} (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, task.WorklistItem.AccessionNumber, task.TaskId);

                var startTime = DateTime.Now;
                _statistics.ItemsProcessing++;

                // Create timeout cancellation token
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_config.CreationTimeoutMinutes));

                // Update status to processing
                await _databaseService.UpdateWorklistStatus(
                    task.WorklistItem.AccessionNumber,
                    WorklistStatus.JPEG_GENERATED,
                    $"DICOM creation started (TaskId: {task.TaskId})");

                // Create DICOM file
                var dicomFilePath = await CreateDicomFile(task.JpegFilePath, task.WorklistItem, timeoutCts.Token);

                if (string.IsNullOrEmpty(dicomFilePath))
                {
                    await HandleCreationFailure(task, "DICOM file creation failed");
                    return;
                }

                // Update database with DICOM path
                await _databaseService.UpdateFilePaths(task.WorklistItem.AccessionNumber, dicomPath: dicomFilePath);
                await _databaseService.UpdateWorklistStatus(
                    task.WorklistItem.AccessionNumber,
                    WorklistStatus.DICOM_CREATED,
                    $"DICOM created: {Path.GetFileName(dicomFilePath)} (TaskId: {task.TaskId})");

                var endTime = DateTime.Now;
                var duration = endTime - startTime;

                _statistics.ItemsProcessed++;
                _statistics.TotalCreationTime += duration;
                _statistics.ItemsProcessing--;

                _logger.LogInformation("✅ DICOM creation completed: {PatientID} - Duration: {Duration}ms (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, duration.TotalMilliseconds, task.TaskId);

                // Update WorklistItem with DICOM path
                task.WorklistItem.DicomFilePath = dicomFilePath;

                // Raise completion event
                DicomCreationCompleted?.Invoke(this, new DicomCreationCompletedEventArgs
                {
                    WorklistItem = task.WorklistItem,
                    JpegFilePath = task.JpegFilePath,
                    DicomFilePath = dicomFilePath,
                    CreationDuration = duration,
                    TaskId = task.TaskId,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _statistics.ItemsFailed++;
                _statistics.ItemsProcessing--;

                _logger.LogError(ex, "❌ DICOM creation failed: {PatientID} (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, task.TaskId);

                await HandleCreationFailure(task, ex.Message);
            }
            finally
            {
                _creationSemaphore.Release();
            }
        }

        private async Task<string?> CreateDicomFile(string jpegFilePath, WorklistItem worklistItem, CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(jpegFilePath))
                {
                    _logger.LogError("❌ JPEG file not found: {JpegPath}", jpegFilePath);
                    return null;
                }

                // Generate DICOM filename
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var dicomFileName = $"{worklistItem.PatientID}_{worklistItem.AccessionNumber}_{timestamp}.dcm";
                var dicomFilePath = Path.Combine(_config.DicomOutputFolderPath, dicomFileName);

                _logger.LogDebug("🔄 Creating DICOM file: {JpegPath} → {DicomPath}", jpegFilePath, dicomFilePath);

                // Create DICOM dataset
                var dicomDataset = CreateDicomDataset(worklistItem);

                // Add image data to DICOM
                await AddImageDataToDicom(dicomDataset, jpegFilePath, cancellationToken);

                // Create DICOM file
                var dicomFile = new DicomFile(dicomDataset);

                // Validate DICOM if enabled
                if (_config.EnableValidation)
                {
                    if (!ValidateDicomFile(dicomFile))
                    {
                        _logger.LogError("❌ DICOM validation failed: {DicomPath}", dicomFilePath);
                        return null;
                    }
                }

                // Save DICOM file
                await dicomFile.SaveAsync(dicomFilePath);

                // Archive if enabled
                if (_config.ArchiveCreatedFiles)
                {
                    await ArchiveDicomFile(dicomFilePath);
                }

                var fileInfo = new FileInfo(dicomFilePath);
                _logger.LogInformation("🏥 DICOM file created successfully: {FileName} ({Size} KB)",
                    Path.GetFileName(dicomFilePath), fileInfo.Length / 1024);

                return dicomFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DICOM file creation failed: {JpegPath}", jpegFilePath);
                return null;
            }
        }

        private DicomDataset CreateDicomDataset(WorklistItem worklistItem)
        {
            var dataset = new DicomDataset();

            // Patient Module
            dataset.AddOrUpdate(DicomTag.PatientName, worklistItem.PatientName);
            dataset.AddOrUpdate(DicomTag.PatientID, worklistItem.PatientID);
            dataset.AddOrUpdate(DicomTag.PatientBirthDate, "");
            dataset.AddOrUpdate(DicomTag.PatientSex, "");

            // Study Module
            dataset.AddOrUpdate(DicomTag.StudyInstanceUID,
            worklistItem.StudyInstanceUID ?? DicomUIDGenerator.GenerateDerivedFromUUID().UID);
            dataset.AddOrUpdate(DicomTag.StudyDate, worklistItem.StudyDate);
            dataset.AddOrUpdate(DicomTag.StudyTime, DateTime.Now.ToString("HHmmss"));
            dataset.AddOrUpdate(DicomTag.ReferringPhysicianName, _config.ReferringPhysicianName);
            dataset.AddOrUpdate(DicomTag.StudyID, $"{_config.StudyIDPrefix}{worklistItem.AccessionNumber}");
            dataset.AddOrUpdate(DicomTag.AccessionNumber, worklistItem.AccessionNumber);
            dataset.AddOrUpdate(DicomTag.StudyDescription, _config.StudyDescription);

            // Series Module
            dataset.AddOrUpdate(DicomTag.Modality, worklistItem.Modality);
            dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID());
            dataset.AddOrUpdate(DicomTag.SeriesNumber, "1");
            dataset.AddOrUpdate(DicomTag.SeriesDate, DateTime.Now.ToString("yyyyMMdd"));
            dataset.AddOrUpdate(DicomTag.SeriesTime, DateTime.Now.ToString("HHmmss"));
            dataset.AddOrUpdate(DicomTag.SeriesDescription, _config.SeriesDescription);
            dataset.AddOrUpdate(DicomTag.BodyPartExamined, _config.BodyPartExamined);

            // Equipment Module
            dataset.AddOrUpdate(DicomTag.Manufacturer, _config.Manufacturer);
            dataset.AddOrUpdate(DicomTag.ManufacturerModelName, _config.ManufacturerModelName);
            dataset.AddOrUpdate(DicomTag.DeviceSerialNumber, _config.DeviceSerialNumber);
            dataset.AddOrUpdate(DicomTag.SoftwareVersions, _config.SoftwareVersion);

            // Instance Module
            dataset.AddOrUpdate(DicomTag.SOPClassUID, _config.SOPClassUID);
            dataset.AddOrUpdate(DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID());
            dataset.AddOrUpdate(DicomTag.InstanceNumber, "1");
            dataset.AddOrUpdate(DicomTag.ContentDate, DateTime.Now.ToString("yyyyMMdd"));
            dataset.AddOrUpdate(DicomTag.ContentTime, DateTime.Now.ToString("HHmmss"));

            // Institution Module
            dataset.AddOrUpdate(DicomTag.InstitutionName, _config.InstitutionName);

            // Character Set
            dataset.AddOrUpdate(DicomTag.SpecificCharacterSet, _config.CharacterSet);

            return dataset;
        }

        private async Task AddImageDataToDicom(DicomDataset dataset, string jpegFilePath, CancellationToken cancellationToken)
        {
            using (var image = new Bitmap(jpegFilePath))
            {
                // Image attributes
                dataset.AddOrUpdate(DicomTag.PhotometricInterpretation, _config.PhotometricInterpretation);
                dataset.AddOrUpdate(DicomTag.Rows, (ushort)image.Height);
                dataset.AddOrUpdate(DicomTag.Columns, (ushort)image.Width);
                dataset.AddOrUpdate(DicomTag.BitsAllocated, _config.BitsAllocated);
                dataset.AddOrUpdate(DicomTag.BitsStored, _config.BitsStored);
                dataset.AddOrUpdate(DicomTag.HighBit, _config.HighBit);
                dataset.AddOrUpdate(DicomTag.PixelRepresentation, _config.PixelRepresentation);
                dataset.AddOrUpdate(DicomTag.SamplesPerPixel, (ushort)3); // RGB

                if (_config.PhotometricInterpretation == "RGB")
                {
                    dataset.AddOrUpdate(DicomTag.PlanarConfiguration, _config.PlanarConfiguration);
                }

                // Convert image to pixel data
                var pixelData = ConvertImageToPixelData(image);
                dataset.AddOrUpdate(DicomTag.PixelData, pixelData);
            }
        }

        private byte[] ConvertImageToPixelData(Bitmap image)
        {
            var width = image.Width;
            var height = image.Height;
            var pixelData = new byte[width * height * 3]; // RGB = 3 bytes per pixel
            var index = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    pixelData[index++] = pixel.R;
                    pixelData[index++] = pixel.G;
                    pixelData[index++] = pixel.B;
                }
            }

            return pixelData;
        }

        private bool ValidateDicomFile(DicomFile dicomFile)
        {
            try
            {
                // Basic validation checks
                var dataset = dicomFile.Dataset;

                // Required tags
                if (!dataset.Contains(DicomTag.SOPClassUID) ||
                    !dataset.Contains(DicomTag.SOPInstanceUID) ||
                    !dataset.Contains(DicomTag.StudyInstanceUID) ||
                    !dataset.Contains(DicomTag.SeriesInstanceUID))
                {
                    _logger.LogError("❌ DICOM validation failed: Missing required UIDs");
                    return false;
                }

                // Patient information
                if (!dataset.Contains(DicomTag.PatientID) ||
                    !dataset.Contains(DicomTag.PatientName))
                {
                    _logger.LogError("❌ DICOM validation failed: Missing patient information");
                    return false;
                }

                // Image data
                if (!dataset.Contains(DicomTag.PixelData))
                {
                    _logger.LogError("❌ DICOM validation failed: Missing pixel data");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ DICOM validation error");
                return false;
            }
        }

        private async Task ArchiveDicomFile(string dicomFilePath)
        {
            try
            {
                var fileName = Path.GetFileName(dicomFilePath);
                var archivePath = Path.Combine(_config.ArchiveFolderPath, fileName);

                File.Copy(dicomFilePath, archivePath, overwrite: true);

                _logger.LogDebug("📦 DICOM file archived: {ArchivePath}", archivePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to archive DICOM file: {DicomPath}", dicomFilePath);
            }
        }

        private async Task ProcessPendingJpegItems()
        {
            try
            {
                // แทนที่จะ query by status, ให้สแกน JPEG folder
                var tempJpegFolder = _pdfConfig.TempJpegFolderPath ?? @"C:\EKG_Temp\JPEG";

                if (!Directory.Exists(tempJpegFolder))
                {
                    _logger.LogInformation("📁 JPEG temp folder not found: {Folder}", tempJpegFolder);
                    return;
                }

                var jpegFiles = Directory.GetFiles(tempJpegFolder, "*.jpg")
                    .Concat(Directory.GetFiles(tempJpegFolder, "*.jpeg"))
                    .ToArray();

                if (jpegFiles.Any())
                {
                    _logger.LogInformation("🖼️ Found {Count} JPEG files to process", jpegFiles.Length);

                    foreach (var jpegFile in jpegFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(jpegFile);

                        // Extract HN from filename (เช่น "47411-23.jpeg" → "47411-23")
                        var extractedHN = ExtractHNFromJpegFilename(fileName);

                        if (!string.IsNullOrEmpty(extractedHN))
                        {
                            // ลองหาด้วย status ต่างๆ (เรียงตามลำดับความสำคัญ)
                            var worklistItem = await _databaseService.GetWorklistItemByPatientID(extractedHN, WorklistStatus.JPEG_GENERATED) ??
                                             await _databaseService.GetWorklistItemByPatientID(extractedHN, WorklistStatus.PDF_RECEIVED) ??
                                             await _databaseService.GetWorklistItemByPatientID(extractedHN, WorklistStatus.SCHEDULED);

                            if (worklistItem != null)
                            {
                                // ตรวจสอบว่ายังไม่ได้สร้าง DICOM
                                if (string.IsNullOrEmpty(worklistItem.DicomFilePath) &&
                                    worklistItem.WorklistStatus != WorklistStatus.DICOM_CREATED.ToString() &&
                                    worklistItem.WorklistStatus != WorklistStatus.COMPLETED.ToString())
                                {
                                    // อัปเดต JpegFilePath ใน database
                                    await _databaseService.UpdateFilePaths(worklistItem.AccessionNumber, jpegPath: jpegFile);

                                    // Queue สำหรับ DICOM creation
                                    QueueJpegForDicomCreation(worklistItem, jpegFile);

                                    _logger.LogInformation("✅ Matched JPEG {FileName} with Patient {PatientID} - {PatientName}",
                                        Path.GetFileName(jpegFile), worklistItem.PatientID, worklistItem.PatientName);
                                }
                                else
                                {
                                    _logger.LogDebug("⏭️ JPEG {FileName} already processed (Status: {Status})",
                                        Path.GetFileName(jpegFile), worklistItem.WorklistStatus);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("❓ No worklist item found for HN: {HN} (File: {FileName})",
                                    extractedHN, Path.GetFileName(jpegFile));
                            }
                        }
                        else
                        {
                            _logger.LogWarning("❌ Could not extract HN from filename: {FileName}", Path.GetFileName(jpegFile));
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("📁 No JPEG files found in temp folder");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing pending JPEG items");
            }
        }

        private string? ExtractHNFromJpegFilename(string fileName)
        {
            try
            {
                // Pattern สำหรับไฟล์ JPEG ที่สร้างจาก PdfProcessingService
                // ตัวอย่าง: "47411-23_ACC001_20240624_143052" → "47411-23"
                // หรือ: "123456_ACC001_20240624_143052" → "123456"

                var patterns = new[]
                {
                    @"^([^_]+)_",                    // เอาก่อน underscore แรก
                    @"^(\d{4,10}-\d{1,3})",         // รูปแบบ xxxx-xx 
                    @"^(\d{6,10})",                 // รูปแบบตัวเลข 6-10 หลัก
                    @"^([A-Za-z0-9\-]{4,15})"       // รูปแบบ alphanumeric 4-15 ตัว
                };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(fileName, pattern);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var hn = match.Groups[1].Value;
                        _logger.LogDebug("🔍 Extracted HN '{HN}' from filename '{FileName}' using pattern '{Pattern}'",
                            hn, fileName, pattern);
                        return hn;
                    }
                }

                _logger.LogDebug("❌ No HN pattern matched for filename: {FileName}", fileName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extracting HN from filename: {FileName}", fileName);
                return null;
            }
        }

        private async Task HandleCreationFailure(DicomCreationTask task, string errorMessage)
        {
            try
            {
                // Check if retry is enabled and under retry limit
                if (_config.EnableRetryOnFailure && task.RetryCount < _config.MaxRetryAttempts)
                {
                    task.RetryCount++;
                    task.LastRetryAt = DateTime.Now;

                    _logger.LogWarning("🔄 Retrying DICOM creation (attempt {Retry}/{Max}): {PatientID} (TaskId: {TaskId})",
                        task.RetryCount, _config.MaxRetryAttempts, task.WorklistItem.PatientID, task.TaskId);

                    // Add delay before retry
                    await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));

                    // Re-queue the task
                    lock (_queueLock)
                    {
                        _creationQueue.Enqueue(task);
                    }

                    return;
                }

                // Max retries reached or retry disabled
                await _databaseService.IncrementRetryCount(task.WorklistItem.AccessionNumber);
                await _databaseService.UpdateWorklistStatus(
                    task.WorklistItem.AccessionNumber,
                    WorklistStatus.FAILED,
                    $"DICOM creation failed: {errorMessage} (TaskId: {task.TaskId})",
                    errorMessage);

                // Raise failure event
                DicomCreationCompleted?.Invoke(this, new DicomCreationCompletedEventArgs
                {
                    WorklistItem = task.WorklistItem,
                    JpegFilePath = task.JpegFilePath,
                    TaskId = task.TaskId,
                    Success = false,
                    ErrorMessage = errorMessage
                });

                _logger.LogError("❌ DICOM creation failed for {PatientID}: {Error} (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, errorMessage, task.TaskId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error handling creation failure");
            }
        }

        private void ReportStatistics(object? state)
        {
            try
            {
                var avgCreationTime = _statistics.ItemsProcessed > 0
                    ? _statistics.TotalCreationTime.TotalMilliseconds / _statistics.ItemsProcessed
                    : 0;

                _logger.LogInformation("📊 DICOM Creation Statistics: " +
                    "Queued: {Queued}, Processing: {Processing}, Created: {Created}, Failed: {Failed}, " +
                    "Avg Time: {AvgTime:F1}ms, Queue Size: {QueueSize}",
                    _statistics.ItemsQueued, _statistics.ItemsProcessing, _statistics.ItemsProcessed,
                    _statistics.ItemsFailed, avgCreationTime, _creationQueue.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error reporting statistics");
            }
        }

        // Public methods for monitoring and control
        public DicomCreationStatus GetCreationStatus()
        {
            lock (_queueLock)
            {
                return new DicomCreationStatus
                {
                    QueueSize = _creationQueue.Count,
                    ActiveCreationCount = _config.MaxConcurrentCreation - _creationSemaphore.CurrentCount,
                    MaxConcurrentCreation = _config.MaxConcurrentCreation,
                    DicomOutputFolder = _config.DicomOutputFolderPath,
                    DicomFileCount = Directory.Exists(_config.DicomOutputFolderPath) ?
                        Directory.GetFiles(_config.DicomOutputFolderPath, "*.dcm").Length : 0,
                    LastStatusCheck = DateTime.Now
                };
            }
        }

        public async Task<int> RetryFailedItems()
        {
            _logger.LogInformation("🔄 Retrying failed DICOM creation items");

            var failedItems = await _databaseService.GetWorklistItemsByStatus(WorklistStatus.FAILED);
            var retryCount = 0;

            foreach (var item in failedItems.Where(x => !string.IsNullOrEmpty(x.JpegFilePath) && x.RetryCount < _config.MaxRetryAttempts))
            {
                if (File.Exists(item.JpegFilePath))
                {
                    QueueJpegForDicomCreation(item, item.JpegFilePath);
                    retryCount++;
                }
            }

            _logger.LogInformation("🔄 Queued {Count} items for retry", retryCount);
            return retryCount;
        }

        public override void Dispose()
        {
            _statisticsTimer?.Dispose();
            _creationSemaphore?.Dispose();
            base.Dispose();
        }
    }

    // Supporting classes
    public class DicomCreationTask
    {
        public string TaskId { get; set; } = string.Empty;
        public WorklistItem WorklistItem { get; set; } = new();
        public string JpegFilePath { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; }
        public int RetryCount { get; set; } = 0;
        public DateTime? LastRetryAt { get; set; }
    }

    public class DicomCreationStatistics
    {
        public int ItemsQueued { get; set; }
        public int ItemsProcessing { get; set; }
        public int ItemsProcessed { get; set; }
        public int ItemsFailed { get; set; }
        public TimeSpan TotalCreationTime { get; set; }
    }
}