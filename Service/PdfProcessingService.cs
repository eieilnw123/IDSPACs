using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFiumSharp;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using WorklistServiceApp.Configuration;
using WorklistServiceApp.Data;
using WorklistServiceApp.Models;

namespace WorklistServiceApp.Services
{
    public class PdfProcessingService : BackgroundService
    {
        private readonly ILogger<PdfProcessingService> _logger;
        private readonly DatabaseService _databaseService;
        private readonly PdfProcessingConfiguration _config;
        private readonly Queue<PdfProcessingTask> _processingQueue;
        private readonly SemaphoreSlim _processingSemaphore;
        private readonly object _queueLock = new object();
        private readonly Timer? _statisticsTimer;
        private readonly PdfProcessingStatistics _statistics;

        // Events
        public event EventHandler<PdfProcessingCompletedEventArgs>? PdfProcessingCompleted;

        public PdfProcessingService(
            ILogger<PdfProcessingService> logger,
            DatabaseService databaseService,
            IOptions<PdfProcessingConfiguration> config)
        {
            _logger = logger;
            _databaseService = databaseService;
            _config = config.Value;
            _processingQueue = new Queue<PdfProcessingTask>();
            _processingSemaphore = new SemaphoreSlim(_config.MaxConcurrentProcessing, _config.MaxConcurrentProcessing);
            _statistics = new PdfProcessingStatistics();

            // Setup statistics timer if enabled
            if (_config.EnableStatistics && _config.StatisticsReportingIntervalMinutes > 0)
            {
                _statisticsTimer = new Timer(ReportStatistics, null,
                    TimeSpan.FromMinutes(_config.StatisticsReportingIntervalMinutes),
                    TimeSpan.FromMinutes(_config.StatisticsReportingIntervalMinutes));
            }
        }

        [SupportedOSPlatform("windows")]
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                InitializeService();

                _logger.LogInformation("🖼️ PDF Processing Service started");
                _logger.LogInformation("📁 JPEG output folder: {Folder}", _config.TempJpegFolderPath);
                _logger.LogInformation("⚙️ Max concurrent processing: {Max}", _config.MaxConcurrentProcessing);
                _logger.LogInformation("🎨 JPEG Quality: {Quality}%, DPI: {DPI}", _config.JpegQuality, _config.ConversionDPI);

                // Process pending items on startup
                await ProcessPendingPdfItems();

                // Main processing loop
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        ProcessQueue();
                        await Task.Delay(TimeSpan.FromSeconds(_config.QueueProcessingIntervalSeconds), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error in PDF processing loop");
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Critical error in PDF Processing Service");
            }
        }

        private void InitializeService()
        {
            try
            {
                // Create temporary JPEG folder
                Directory.CreateDirectory(_config.TempJpegFolderPath);

                // Clean up old temporary files on startup if enabled
                if (_config.CleanupOnStartup)
                {
                    CleanupOldTempFiles();
                }

                _logger.LogInformation("⚙️ PDF Processing Service initialized");
                _logger.LogInformation("📋 Using {Library} for PDF processing", _config.ProcessingLibrary);

                if (_config.EnableOCR)
                {
                    _logger.LogInformation("🔤 OCR enabled with language: {Language}", _config.OCRLanguage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize PDF Processing Service");
                throw;
            }
        }

        public void QueuePdfForProcessing(WorklistItem worklistItem, string pdfFilePath)
        {
            // Check queue size limit
            lock (_queueLock)
            {
                if (_processingQueue.Count >= _config.MaxQueueSize)
                {
                    _logger.LogWarning("⚠️ PDF processing queue is full ({Count}/{Max}). Rejecting new file: {PatientID}",
                        _processingQueue.Count, _config.MaxQueueSize, worklistItem.PatientID);
                    return;
                }
            }

            var task = new PdfProcessingTask
            {
                WorklistItem = worklistItem,
                PdfFilePath = pdfFilePath,
                QueuedAt = DateTime.Now,
                TaskId = Guid.NewGuid().ToString()
            };

            lock (_queueLock)
            {
                _processingQueue.Enqueue(task);
            }

            _statistics.ItemsQueued++;

            _logger.LogInformation("📋 PDF queued for processing: {PatientID} - {AccessionNumber} (TaskId: {TaskId}, Queue: {QueueCount})",
                worklistItem.PatientID, worklistItem.AccessionNumber, task.TaskId, _processingQueue.Count);
        }

        [SupportedOSPlatform("windows")]
        private void ProcessQueue()
        {
            PdfProcessingTask? task = null;

            lock (_queueLock)
            {
                if (_processingQueue.Count > 0)
                {
                    task = _processingQueue.Dequeue();
                }
            }

            if (task != null)
            {
                // Process task in background to not block the queue
                _ = Task.Run(async () => await ProcessPdfTask(task));
            }
        }

        [SupportedOSPlatform("windows")]
        private async Task ProcessPdfTask(PdfProcessingTask task)
        {
            await _processingSemaphore.WaitAsync();

            try
            {
                _logger.LogInformation("🔄 Starting PDF processing: {PatientID} - {AccessionNumber} (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, task.WorklistItem.AccessionNumber, task.TaskId);

                var startTime = DateTime.Now;
                _statistics.ItemsProcessing++;

                // Create timeout cancellation token
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_config.ProcessingTimeoutMinutes));

                // Update status to processing
                await _databaseService.UpdateWorklistStatus(
                    task.WorklistItem.AccessionNumber,
                    WorklistStatus.PDF_RECEIVED,
                    $"PDF processing started (TaskId: {task.TaskId})");

                // Convert PDF to JPEG(s)
                var jpegPaths = await ConvertPdfToJpeg(task.PdfFilePath, task.WorklistItem, timeoutCts.Token);

                if (jpegPaths == null || !jpegPaths.Any())
                {
                    await HandleProcessingFailure(task, "PDF to JPEG conversion failed");
                    return;
                }

                // For now, use the first JPEG (or combined if multiple pages)
                var primaryJpegPath = jpegPaths.First();

                // Update database with JPEG path
                await _databaseService.UpdateFilePaths(task.WorklistItem.AccessionNumber, jpegPath: primaryJpegPath);
                await _databaseService.UpdateWorklistStatus(
                    task.WorklistItem.AccessionNumber,
                    WorklistStatus.JPEG_GENERATED,
                    $"JPEG created: {Path.GetFileName(primaryJpegPath)} (TaskId: {task.TaskId})");

                var endTime = DateTime.Now;
                var duration = endTime - startTime;

                _statistics.ItemsProcessed++;
                _statistics.TotalProcessingTime += duration;
                _statistics.ItemsProcessing--;

                _logger.LogInformation("✅ PDF processing completed: {PatientID} - Duration: {Duration}ms (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, duration.TotalMilliseconds, task.TaskId);

                // Update WorklistItem with JPEG path
                task.WorklistItem.JpegFilePath = primaryJpegPath;

                // Raise completion event
                PdfProcessingCompleted?.Invoke(this, new PdfProcessingCompletedEventArgs
                {
                    WorklistItem = task.WorklistItem,
                    PdfFilePath = task.PdfFilePath,
                    JpegFilePath = primaryJpegPath,
                    ProcessingDuration = duration,
                    TaskId = task.TaskId,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _statistics.ItemsFailed++;
                _statistics.ItemsProcessing--;

                _logger.LogError(ex, "❌ PDF processing failed: {PatientID} (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, task.TaskId);

                await HandleProcessingFailure(task, ex.Message);
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }

        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("windows")]
        private async Task<List<string>?> ConvertPdfToJpeg(string pdfPath, WorklistItem worklistItem, CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(pdfPath))
                {
                    _logger.LogError("❌ PDF file not found: {PdfPath}", pdfPath);
                    return null;
                }

                var fileInfo = new FileInfo(pdfPath);
                if (fileInfo.Length > 52428800)
                {
                    _logger.LogError("❌ PDF file too large: {Size} bytes (max: {MaxSize})",
                        fileInfo.Length, 52428800);
                    return null;
                }

                var jpegPaths = new List<string>();
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                using (var document = new PdfDocument(pdfPath))
                {
                    if (document.Pages.Count == 0)
                    {
                        _logger.LogError("❌ PDF document has no pages: {PdfPath}", pdfPath);
                        return null;
                    }

                    int pagesToProcess = _config.ConvertAllPages ? document.Pages.Count : 1;
                    _logger.LogInformation("📄 Processing {PageCount} page(s) from PDF", pagesToProcess);

                    for (int pageIndex = 0; pageIndex < pagesToProcess; pageIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var jpegFileName = pagesToProcess == 1
                            ? $"{worklistItem.PatientID}_{worklistItem.AccessionNumber}_{timestamp}.jpg"
                            : $"{worklistItem.PatientID}_{worklistItem.AccessionNumber}_{timestamp}_p{pageIndex + 1}.jpg";

                        var jpegPath = Path.Combine(_config.TempJpegFolderPath, jpegFileName);

                        var page = document.Pages[pageIndex];

                        // 🔧 ใหม่: ใช้ page dimensions โดยตรง
                        var pageWidth = page.Width;
                        var pageHeight = page.Height;

                        _logger.LogDebug("📐 Page dimensions: {W}x{H} pts", pageWidth, pageHeight);

                        var dpi = _config.ConversionDPI;

                        // 🔧 เพิ่ม margin เล็กน้อยเพื่อป้องกันการตัดขอบ
                        var marginFactor = 1.02; // เพิ่ม 2%
                        var width = (int)(pageWidth * dpi / 72.0 * marginFactor);
                        var height = (int)(pageHeight * dpi / 72.0 * marginFactor);

                        _logger.LogDebug("📐 Render size (DPI {DPI}, Margin {Margin:P0}): {W}x{H} px",
                            dpi, marginFactor - 1, width, height);

                        using (var pdfBitmap = new PDFiumBitmap(width, height, true))
                        {
                            try
                            {
                                // 🔧 ใช้ render แบบที่ PDFiumSharp รองรับ
                                page.Render(pdfBitmap);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "⚠️ PDF rendering failed, using fallback method");
                                // ถ้าไม่ได้ ให้สร้าง bitmap เปล่า
                                // PDFiumSharp อาจจะมีปัญหากับไฟล์บางไฟล์
                            }

                            using (var originalImage = CreateBitmapFromPDFium(pdfBitmap))
                            {
                                // 🔧 ตรวจสอบขนาดภาพที่ได้
                                _logger.LogDebug("🖼️ Generated image size: {W}x{H} px",
                                    originalImage.Width, originalImage.Height);

                                using (var processedImage = _config.EnableImageEnhancement
                                    ? EnhanceImage(originalImage)
                                    : new Bitmap(originalImage))
                                {
                                    using (var finalImage = ResizeImageIfNeeded(processedImage))
                                    {
                                        SaveImageAsJpeg(finalImage, jpegPath);
                                    }
                                }
                            }
                        }

                        if (!ValidateJpegFile(jpegPath))
                        {
                            _logger.LogError("❌ Generated JPEG file validation failed: {JpegPath}", jpegPath);
                            return null;
                        }

                        jpegPaths.Add(jpegPath);

                        var jpegFileInfo = new FileInfo(jpegPath);
                        _logger.LogInformation("🖼️ Page {Page} converted: {FileName} ({Size:N0} KB)",
                            pageIndex + 1, Path.GetFileName(jpegPath), jpegFileInfo.Length / 1024);
                    }
                }

                _logger.LogInformation("✅ PDF converted to {Count} JPEG file(s)", jpegPaths.Count);
                return jpegPaths;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ PDF to JPEG conversion failed: {PdfPath}", pdfPath);
                return null;
            }
        }
        [SupportedOSPlatform("windows")]
        private Bitmap EnhanceImage(Image originalImage)
        {
            try
            {
                var bitmap = new Bitmap(originalImage);

                // Apply brightness and contrast adjustments
                if (_config.BrightnessAdjustment != 0 || Math.Abs(_config.ContrastFactor - 1.0f) > 0.01f)
                {
                    bitmap = AdjustBrightnessContrast(bitmap, _config.BrightnessAdjustment, _config.ContrastFactor);
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Image enhancement failed, using original image");
                return new Bitmap(originalImage);
            }
        }

        [SupportedOSPlatform("windows")]
        private Bitmap AdjustBrightnessContrast(Bitmap original, int brightness, float contrast)
        {
            var result = new Bitmap(original.Width, original.Height);

            for (int x = 0; x < original.Width; x++)
            {
                for (int y = 0; y < original.Height; y++)
                {
                    var pixel = original.GetPixel(x, y);

                    // Apply contrast and brightness
                    int r = (int)(((pixel.R / 255.0f - 0.5f) * contrast + 0.5f) * 255.0f + brightness);
                    int g = (int)(((pixel.G / 255.0f - 0.5f) * contrast + 0.5f) * 255.0f + brightness);
                    int b = (int)(((pixel.B / 255.0f - 0.5f) * contrast + 0.5f) * 255.0f + brightness);

                    // Clamp values
                    r = Math.Max(0, Math.Min(255, r));
                    g = Math.Max(0, Math.Min(255, g));
                    b = Math.Max(0, Math.Min(255, b));

                    result.SetPixel(x, y, Color.FromArgb(pixel.A, r, g, b));
                }
            }

            return result;
        }

        [SupportedOSPlatform("windows")]
        private Bitmap ResizeImageIfNeeded(Bitmap originalImage)
        {
            if (_config.MaxImageWidth == 0 && _config.MaxImageHeight == 0)
                return new Bitmap(originalImage);

            var needsResize = false;
            var newWidth = originalImage.Width;
            var newHeight = originalImage.Height;

            if (_config.MaxImageWidth > 0 && originalImage.Width > _config.MaxImageWidth)
            {
                var ratio = (double)_config.MaxImageWidth / originalImage.Width;
                newWidth = _config.MaxImageWidth;
                newHeight = (int)(originalImage.Height * ratio);
                needsResize = true;
            }

            if (_config.MaxImageHeight > 0 && newHeight > _config.MaxImageHeight)
            {
                var ratio = (double)_config.MaxImageHeight / newHeight;
                newHeight = _config.MaxImageHeight;
                newWidth = (int)(newWidth * ratio);
                needsResize = true;
            }

            if (!needsResize)
                return new Bitmap(originalImage);

            var resized = new Bitmap(newWidth, newHeight);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.DrawImage(originalImage, 0, 0, newWidth, newHeight);
            }

            _logger.LogDebug("🔄 Image resized from {OriginalWidth}x{OriginalHeight} to {NewWidth}x{NewHeight}",
                originalImage.Width, originalImage.Height, newWidth, newHeight);

            return resized;
        }

        [SupportedOSPlatform("windows")]
        private void SaveImageAsJpeg(Bitmap image, string jpegPath)
        {
            var jpegEncoder = GetJpegEncoder();
            if (jpegEncoder == null)
            {
                throw new InvalidOperationException("JPEG encoder not found");
            }

            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_config.JpegQuality);

            image.Save(jpegPath, jpegEncoder, encoderParams);
        }

        [SupportedOSPlatform("windows")]
        private static ImageCodecInfo? GetJpegEncoder()
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            return codecs.FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        }

        [SupportedOSPlatform("windows")]
        private bool ValidateJpegFile(string jpegPath)
        {
            try
            {
                if (!File.Exists(jpegPath))
                    return false;

                var fileInfo = new FileInfo(jpegPath);
                if (fileInfo.Length == 0)
                    return false;

                // Try to load the image to verify it's valid
                using (var image = Image.FromFile(jpegPath))
                {
                    // Basic checks
                    if (image.Width == 0 || image.Height == 0)
                        return false;

                    // Check minimum quality requirements
                    const int minWidth = 100;
                    const int minHeight = 100;

                    if (image.Width < minWidth || image.Height < minHeight)
                    {
                        _logger.LogWarning("⚠️ JPEG image too small: {Width}x{Height}", image.Width, image.Height);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ JPEG validation failed: {JpegPath}", jpegPath);
                return false;
            }
        }

        private async Task ProcessPendingPdfItems()
        {
            try
            {
                // Find items that have PDF but no JPEG
                var pendingItems = await _databaseService.GetWorklistItemsByStatus(WorklistStatus.PDF_RECEIVED);

                if (pendingItems.Any())
                {
                    _logger.LogInformation("📋 Found {Count} pending PDF items to process", pendingItems.Count);

                    foreach (var item in pendingItems)
                    {
                        if (!string.IsNullOrEmpty(item.PdfFilePath) && File.Exists(item.PdfFilePath))
                        {
                            QueuePdfForProcessing(item, item.PdfFilePath);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ PDF file missing for item: {AccessionNumber}", item.AccessionNumber);
                            await _databaseService.UpdateWorklistStatus(
                                item.AccessionNumber,
                                WorklistStatus.FAILED,
                                "PDF file missing");
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("📋 No pending PDF items found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing pending PDF items");
            }
        }

        private async Task HandleProcessingFailure(PdfProcessingTask task, string errorMessage)
        {
            try
            {
                // Check if retry is enabled and under retry limit
                if (_config.EnableRetryOnFailure && task.RetryCount < _config.MaxRetryAttempts)
                {
                    task.RetryCount++;
                    task.LastRetryAt = DateTime.Now;

                    _logger.LogWarning("🔄 Retrying PDF processing (attempt {Retry}/{Max}): {PatientID} (TaskId: {TaskId})",
                        task.RetryCount, _config.MaxRetryAttempts, task.WorklistItem.PatientID, task.TaskId);

                    // Add delay before retry
                    await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds));

                    // Re-queue the task
                    lock (_queueLock)
                    {
                        _processingQueue.Enqueue(task);
                    }

                    return;
                }

                // Max retries reached or retry disabled
                await _databaseService.IncrementRetryCount(task.WorklistItem.AccessionNumber);
                await _databaseService.UpdateWorklistStatus(
                    task.WorklistItem.AccessionNumber,
                    WorklistStatus.FAILED,
                    $"PDF processing failed: {errorMessage} (TaskId: {task.TaskId})",
                    errorMessage);

                // Raise failure event
                PdfProcessingCompleted?.Invoke(this, new PdfProcessingCompletedEventArgs
                {
                    WorklistItem = task.WorklistItem,
                    PdfFilePath = task.PdfFilePath,
                    TaskId = task.TaskId,
                    Success = false,
                    ErrorMessage = errorMessage
                });

                _logger.LogError("❌ PDF processing failed for {PatientID}: {Error} (TaskId: {TaskId})",
                    task.WorklistItem.PatientID, errorMessage, task.TaskId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error handling processing failure");
            }
        }

        private void CleanupOldTempFiles()
        {
            try
            {
                if (!Directory.Exists(_config.TempJpegFolderPath))
                    return;

                var cutoffTime = DateTime.Now.AddHours(-_config.MaxTempFileAgeHours);
                var tempFiles = Directory.GetFiles(_config.TempJpegFolderPath, "*.jpg");
                var deletedCount = 0;

                foreach (var file in tempFiles)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffTime)
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Failed to delete old temp file: {File}", file);
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    _logger.LogInformation("🗑️ Cleaned up {Count} old temporary JPEG files", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during temp file cleanup");
            }
        }

        private void ReportStatistics(object? state)
        {
            try
            {
                var avgProcessingTime = _statistics.ItemsProcessed > 0
                    ? _statistics.TotalProcessingTime.TotalMilliseconds / _statistics.ItemsProcessed
                    : 0;

                _logger.LogInformation("📊 PDF Processing Statistics: " +
                    "Queued: {Queued}, Processing: {Processing}, Processed: {Processed}, Failed: {Failed}, " +
                    "Avg Time: {AvgTime:F1}ms, Queue Size: {QueueSize}",
                    _statistics.ItemsQueued, _statistics.ItemsProcessing, _statistics.ItemsProcessed,
                    _statistics.ItemsFailed, avgProcessingTime, _processingQueue.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error reporting statistics");
            }
        }

        // Public methods for monitoring and control
        public PdfProcessingStatus GetProcessingStatus()
        {
            lock (_queueLock)
            {
                return new PdfProcessingStatus
                {
                    QueueSize = _processingQueue.Count,
                    ActiveProcessingCount = _config.MaxConcurrentProcessing - _processingSemaphore.CurrentCount,
                    MaxConcurrentProcessing = _config.MaxConcurrentProcessing,
                    TempJpegFolder = _config.TempJpegFolderPath,
                    TempFileCount = Directory.Exists(_config.TempJpegFolderPath) ?
                        Directory.GetFiles(_config.TempJpegFolderPath, "*.jpg").Length : 0,
                    LastStatusCheck = DateTime.Now
                };
            }
        }

        public async Task<int> RetryFailedItems()
        {
            _logger.LogInformation("🔄 Retrying failed PDF processing items");

            var failedItems = await _databaseService.GetWorklistItemsByStatus(WorklistStatus.FAILED);
            var retryCount = 0;

            foreach (var item in failedItems.Where(x => !string.IsNullOrEmpty(x.PdfFilePath) && x.RetryCount < _config.MaxRetryAttempts))
            {
                if (File.Exists(item.PdfFilePath))
                {
                    QueuePdfForProcessing(item, item.PdfFilePath);
                    retryCount++;
                }
            }

            _logger.LogInformation("🔄 Queued {Count} items for retry", retryCount);
            return retryCount;
        }

        public override void Dispose()
        {
            _statisticsTimer?.Dispose();
            _processingSemaphore?.Dispose();
            base.Dispose();
        }

        // 🔧 ปรับปรุง CreateBitmapFromPDFium ให้ดีกว่า
        [SupportedOSPlatform("windows")]
        private static Bitmap CreateBitmapFromPDFium(PDFiumBitmap pdfiumBitmap)
        {
            try
            {
                // วิธีที่ 1: ใช้ MemoryStream (เก็บไว้เป็น fallback)
                using var ms = new MemoryStream();
                pdfiumBitmap.Save(ms);
                ms.Position = 0;

                using var rawBitmap = new Bitmap(ms);

                // สร้าง bitmap ใหม่ด้วยพื้นหลังขาว
                var finalBitmap = new Bitmap(rawBitmap.Width, rawBitmap.Height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(finalBitmap))
                {
                    // ตั้งค่า rendering quality
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    // วาดพื้นหลังขาว
                    g.Clear(Color.White);

                    // วาดภาพ PDF
                    g.DrawImage(rawBitmap, 0, 0, rawBitmap.Width, rawBitmap.Height);
                }

                return finalBitmap;
            }
            catch (Exception)
            {
                // Fallback: สร้าง bitmap เปล่าสีขาวถ้าเกิดข้อผิดพลาด
                var fallbackBitmap = new Bitmap(1, 1);
                using (Graphics g = Graphics.FromImage(fallbackBitmap))
                {
                    g.Clear(Color.White);
                }
                return fallbackBitmap;
            }
        }
    }

    // Supporting classes
    public class PdfProcessingTask
    {
        public string TaskId { get; set; } = string.Empty;
        public WorklistItem WorklistItem { get; set; } = new();
        public string PdfFilePath { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; }
        public int RetryCount { get; set; } = 0;
        public DateTime? LastRetryAt { get; set; }
    }

    public class PdfProcessingStatistics
    {
        public int ItemsQueued { get; set; }
        public int ItemsProcessing { get; set; }
        public int ItemsProcessed { get; set; }
        public int ItemsFailed { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
    }
}