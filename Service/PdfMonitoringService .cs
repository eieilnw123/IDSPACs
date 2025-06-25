using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using WorklistServiceApp.Configuration;
using WorklistServiceApp.Data;
using WorklistServiceApp.Models;
using WorklistServiceApp.Utils;

namespace WorklistServiceApp.Services
{
    public class PdfMonitoringService : BackgroundService
    {
        private readonly ILogger<PdfMonitoringService> _logger;
        private readonly DatabaseService _databaseService;
        private readonly FileSystemWatcher _pdfWatcher;
        private readonly FileNameParser _fileNameParser;
        private readonly PdfMonitoringConfiguration _config;

        private const int MaxRetryAttempts = 3;

        // Events for other services to subscribe to
        public event EventHandler<PdfFileDetectedEventArgs> PdfFileDetected = null!;

        public PdfMonitoringService(
            ILogger<PdfMonitoringService> logger,
            DatabaseService databaseService,
            FileNameParser fileNameParser,
            IOptions<PdfMonitoringConfiguration> config)
        {
            _logger = logger;
            _databaseService = databaseService;
            _fileNameParser = fileNameParser;
            _config = config.Value;
            _pdfWatcher = new FileSystemWatcher();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                InitializeService();

                _logger.LogInformation("👁️ PDF Monitoring Service started");
                _logger.LogInformation("📁 Watching folder: {Folder}", _config.WatchFolderPath);

                // Process any existing PDF files on startup
                await ProcessExistingPdfFiles();

                // Keep the service running
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(_config.HealthCheckIntervalMinutes), stoppingToken);

                    // Periodic cleanup and health check
                    await PerformHealthCheck();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Critical error in PDF Monitoring Service");
            }
        }

        private void InitializeService()
        {
            // Ensure watch folder exists
            if (!Directory.Exists(_config.WatchFolderPath))
            {
                if (_config.CreateWatchFolderIfNotExists)
                {
                    _logger.LogWarning("⚠️ PDF watch folder does not exist, creating: {Folder}", _config.WatchFolderPath);
                    Directory.CreateDirectory(_config.WatchFolderPath);
                }
                else
                {
                    _logger.LogError("❌ PDF watch folder does not exist: {Folder}", _config.WatchFolderPath);
                    throw new DirectoryNotFoundException($"PDF watch folder not found: {_config.WatchFolderPath}");
                }
            }

            // Create archive folder if enabled
            if (_config.ArchiveProcessedFiles && !Directory.Exists(_config.ArchiveFolderPath))
            {
                Directory.CreateDirectory(_config.ArchiveFolderPath);
                _logger.LogInformation("📁 Created archive folder: {Folder}", _config.ArchiveFolderPath);
            }

            // Configure file system watcher
            _pdfWatcher.Path = _config.WatchFolderPath;
            _pdfWatcher.Filter = _config.FileFilter;
            _pdfWatcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size;
            _pdfWatcher.IncludeSubdirectories = _config.IncludeSubdirectories;
            _pdfWatcher.EnableRaisingEvents = true;

            // Event handlers
            _pdfWatcher.Created += OnPdfFileCreated;
            _pdfWatcher.Renamed += OnPdfFileRenamed;
            _pdfWatcher.Error += OnWatcherError;

            _logger.LogInformation("⚙️ PDF File Watcher configured successfully");
            _logger.LogInformation("📊 Configuration: Filter={Filter}, Subdirs={Subdirs}, AutoProcess={AutoProcess}",
                _config.FileFilter, _config.IncludeSubdirectories, _config.EnableAutoProcessing);
        }

        private async void OnPdfFileCreated(object sender, FileSystemEventArgs e)
        {
            _logger.LogInformation("📄 New PDF file detected: {FileName}", e.Name);
            if (_config.EnableAutoProcessing)
            {
                await ProcessPdfFileAsync(e.FullPath, "File Created");
            }
        }

        private async void OnPdfFileRenamed(object sender, RenamedEventArgs e)
        {
            _logger.LogInformation("📄 PDF file renamed: {OldName} → {NewName}", e.OldName, e.Name);
            if (_config.EnableAutoProcessing)
            {
                await ProcessPdfFileAsync(e.FullPath, "File Renamed");
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _logger.LogError(e.GetException(), "❌ File system watcher error");

            // Try to restart the watcher
            Task.Run(async () =>
            {
                await Task.Delay(5000); // Wait 5 seconds
                try
                {
                    _pdfWatcher.EnableRaisingEvents = false;
                    await Task.Delay(1000);
                    _pdfWatcher.EnableRaisingEvents = true;
                    _logger.LogInformation("🔄 File system watcher restarted");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to restart file system watcher");
                }
            });
        }

        private async Task ProcessPdfFileAsync(string pdfPath, string source)
        {
            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    _logger.LogDebug("🔍 Processing PDF file (Attempt {Attempt}/{Max}): {Path}",
                        attempt, MaxRetryAttempts, pdfPath);

                    // Wait for file to be completely written
                    await WaitForFileReady(pdfPath);

                    // Validate file
                    if (!await ValidatePdfFile(pdfPath))
                    {
                        _logger.LogWarning("⚠️ PDF file validation failed: {Path}", pdfPath);
                        return;
                    }

                    // Check for duplicates if enabled
                    if (_config.EnableDuplicateDetection && await IsDuplicateFile(pdfPath))
                    {
                        _logger.LogWarning("⚠️ Duplicate PDF file detected: {Path}", pdfPath);
                        await HandleUnmatchedPdfFile(pdfPath, "Duplicate file detected");
                        return;
                    }

                    // Parse filename to extract PatientID, SeriesDate, SeriesTime
                    var fileName = Path.GetFileName(pdfPath);
                    var parsedInfo = _fileNameParser.ParsePdfFileName(fileName);

                    if (!parsedInfo.Success || string.IsNullOrEmpty(parsedInfo.PatientID))
                    {
                        _logger.LogWarning("⚠️ Cannot extract PatientID from filename: {FileName}", fileName);
                        await HandleUnmatchedPdfFile(pdfPath, "PatientID extraction failed");
                        return;
                    }

                    _logger.LogInformation("🔍 Parsed info: {ParsedInfo}", parsedInfo.ToString());

                    // Find matching worklist item
                    var worklistItem = await _databaseService.GetWorklistItemByPatientID(parsedInfo.PatientID, WorklistStatus.SCHEDULED);

                    if (worklistItem == null)
                    {
                        _logger.LogWarning("⚠️ No matching worklist item found for PatientID: {PatientID}", parsedInfo.PatientID);
                        await HandleUnmatchedPdfFile(pdfPath, $"No worklist found for PatientID: {parsedInfo.PatientID}");
                        return;
                    }

                    _logger.LogInformation("✅ Match found! PatientID: {PatientID} → AccessionNumber: {AccessionNumber}",
                        parsedInfo.PatientID, worklistItem.AccessionNumber);

                    // Update database with PDF info and Series information
                    await _databaseService.UpdateWorklistStatus(worklistItem.AccessionNumber,
                        WorklistStatus.PDF_RECEIVED, $"PDF file received: {fileName} (Source: {source})");

                    await _databaseService.UpdateFilePaths(worklistItem.AccessionNumber, pdfPath: pdfPath);

                    // Update Series information from filename
                    await _databaseService.UpdateSeriesInfo(
                        worklistItem.AccessionNumber,
                        parsedInfo.SeriesDate,
                        parsedInfo.SeriesTime,
                        parsedInfo.SeriesDescription);

                    // Update the worklistItem object with parsed info
                    worklistItem.SeriesDate = parsedInfo.SeriesDate;
                    worklistItem.SeriesTime = parsedInfo.SeriesTime;
                    worklistItem.SeriesDescription = parsedInfo.SeriesDescription;
                    worklistItem.PdfFilePath = pdfPath;

                    // Archive processed file if enabled
                    if (_config.ArchiveProcessedFiles)
                    {
                        await ArchiveProcessedFile(pdfPath);
                    }

                    // Raise event for other services
                    PdfFileDetected?.Invoke(this, new PdfFileDetectedEventArgs
                    {
                        PdfFilePath = pdfPath,
                        WorklistItem = worklistItem,
                        ExtractedHN = parsedInfo.PatientID,
                        ProcessingSource = source
                    });

                    _logger.LogInformation("🎯 PDF processing completed successfully for PatientID: {PatientID}", parsedInfo.PatientID);
                    return; // Success, exit retry loop

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error processing PDF file (Attempt {Attempt}/{Max}): {Path}",
                        attempt, MaxRetryAttempts, pdfPath);

                    if (attempt == MaxRetryAttempts)
                    {
                        _logger.LogError("❌ All retry attempts failed for PDF file: {Path}", pdfPath);
                        await HandleFailedPdfFile(pdfPath, ex.Message);
                    }
                    else
                    {
                        // Wait before retry
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
                    }
                }
            }
        }

        private async Task WaitForFileReady(string filePath)
        {
            // Wait for initial delay
            await Task.Delay(_config.FileStabilizationDelaySeconds * 1000);

            if (!_config.CheckFileLocking)
                return;

            // Check if file is still being written
            var maxWaitTime = TimeSpan.FromSeconds(30);
            var startTime = DateTime.Now;

            for (int attempt = 0; attempt < _config.MaxLockRetryAttempts; attempt++)
            {
                try
                {
                    using (var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        // File is ready if we can open it exclusively
                        _logger.LogDebug("✅ File is ready for processing: {FilePath}", filePath);
                        break;
                    }
                }
                catch (IOException)
                {
                    // File is still being written
                    if (DateTime.Now - startTime > maxWaitTime)
                    {
                        _logger.LogWarning("⚠️ File lock timeout: {FilePath}", filePath);
                        break;
                    }

                    _logger.LogDebug("⏳ File still locked, waiting... (Attempt {Attempt}/{Max})",
                        attempt + 1, _config.MaxLockRetryAttempts);
                    await Task.Delay(_config.LockRetryDelaySeconds * 1000);
                }
            }
        }

        private async Task<bool> ValidatePdfFile(string pdfPath)
        {
            try
            {
                if (!File.Exists(pdfPath))
                {
                    _logger.LogWarning("⚠️ PDF file does not exist: {Path}", pdfPath);
                    return false;
                }

                var fileInfo = new FileInfo(pdfPath);

                // Check file size
                if (fileInfo.Length < _config.MinimumFileSizeBytes)
                {
                    _logger.LogWarning("⚠️ PDF file too small ({Size} bytes, min: {MinSize}): {Path}",
                        fileInfo.Length, _config.MinimumFileSizeBytes, pdfPath);
                    return false;
                }

                if (fileInfo.Length > _config.MaximumFileSizeBytes)
                {
                    _logger.LogWarning("⚠️ PDF file too large ({Size} bytes, max: {MaxSize}): {Path}",
                        fileInfo.Length, _config.MaximumFileSizeBytes, pdfPath);
                    return false;
                }

                // Basic PDF header check
                using (var fs = File.OpenRead(pdfPath))
                {
                    var buffer = new byte[4];
                    await fs.ReadAsync(buffer, 0, 4);
                    var header = System.Text.Encoding.ASCII.GetString(buffer);

                    if (!header.StartsWith("%PDF"))
                    {
                        _logger.LogWarning("⚠️ File is not a valid PDF: {Path}", pdfPath);
                        return false;
                    }
                }

                _logger.LogDebug("✅ PDF file validation passed: {Path}", pdfPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ PDF file validation error: {Path}", pdfPath);
                return false;
            }
        }

        private async Task<bool> IsDuplicateFile(string pdfPath)
        {
            try
            {
                if (!_config.EnableDuplicateDetection)
                    return false;

                return _config.DuplicateDetectionMethod.ToUpper() switch
                {
                    "FILESIZE" => await CheckDuplicateByFileSize(pdfPath),
                    "HASH" => await CheckDuplicateByHash(pdfPath),
                    "FILENAME" => await CheckDuplicateByFilename(pdfPath),
                    _ => false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error checking for duplicate file: {Path}", pdfPath);
                return false;
            }
        }

        private async Task<bool> CheckDuplicateByFileSize(string pdfPath)
        {
            var fileInfo = new FileInfo(pdfPath);
            var fileName = Path.GetFileName(pdfPath);

            // Check if any file with same size exists in archive
            if (Directory.Exists(_config.ArchiveFolderPath))
            {
                var archiveFiles = Directory.GetFiles(_config.ArchiveFolderPath, "*.pdf");
                foreach (var archiveFile in archiveFiles)
                {
                    var archiveInfo = new FileInfo(archiveFile);
                    if (archiveInfo.Length == fileInfo.Length)
                    {
                        _logger.LogDebug("🔍 Found file with same size: {ArchiveFile}", archiveFile);
                        return true;
                    }
                }
            }

            return false;
        }

        private async Task<bool> CheckDuplicateByHash(string pdfPath)
        {
            try
            {
                using var md5 = System.Security.Cryptography.MD5.Create();
                using var stream = File.OpenRead(pdfPath);
                var hashBytes = await Task.Run(() => md5.ComputeHash(stream));
                var hash = Convert.ToHexString(hashBytes);

                // Store hash in a simple cache file
                var hashCacheFile = Path.Combine(_config.ArchiveFolderPath, ".hashes.txt");

                if (File.Exists(hashCacheFile))
                {
                    var existingHashes = await File.ReadAllLinesAsync(hashCacheFile);
                    if (existingHashes.Contains(hash))
                    {
                        _logger.LogDebug("🔍 Found duplicate hash: {Hash}", hash);
                        return true;
                    }
                }

                // Add new hash to cache
                await File.AppendAllTextAsync(hashCacheFile, hash + Environment.NewLine);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error computing file hash: {Path}", pdfPath);
                return false;
            }
        }

        private async Task<bool> CheckDuplicateByFilename(string pdfPath)
        {
            var fileName = Path.GetFileName(pdfPath);

            if (Directory.Exists(_config.ArchiveFolderPath))
            {
                var archiveFiles = Directory.GetFiles(_config.ArchiveFolderPath, "*.pdf");
                return archiveFiles.Any(f => Path.GetFileName(f).Contains(Path.GetFileNameWithoutExtension(fileName)));
            }

            return false;
        }

        private async Task ProcessExistingPdfFiles()
        {
            try
            {
                if (!Directory.Exists(_config.WatchFolderPath))
                    return;

                var existingPdfFiles = Directory.GetFiles(_config.WatchFolderPath, _config.FileFilter);

                if (existingPdfFiles.Length > 0)
                {
                    _logger.LogInformation("📁 Found {Count} existing PDF files to process", existingPdfFiles.Length);

                    foreach (var pdfFile in existingPdfFiles)
                    {
                        if (_config.EnableAutoProcessing)
                        {
                            await ProcessPdfFileAsync(pdfFile, "Startup Scan");
                        }

                        // Small delay between files to prevent overwhelming
                        await Task.Delay(100);
                    }
                }
                else
                {
                    _logger.LogInformation("📁 No existing PDF files found in watch folder");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing existing PDF files");
            }
        }

        private async Task ArchiveProcessedFile(string pdfPath)
        {
            try
            {
                if (!_config.ArchiveProcessedFiles)
                    return;

                var fileName = Path.GetFileName(pdfPath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var newFileName = $"{timestamp}_{fileName}";
                var archivePath = Path.Combine(_config.ArchiveFolderPath, newFileName);

                if (_config.DeleteAfterProcessing)
                {
                    File.Move(pdfPath, archivePath);
                    _logger.LogInformation("📁 Moved PDF to archive: {ArchivePath}", archivePath);
                }
                else
                {
                    File.Copy(pdfPath, archivePath);
                    _logger.LogInformation("📁 Copied PDF to archive: {ArchivePath}", archivePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to archive processed PDF file: {Path}", pdfPath);
            }
        }

        private async Task HandleUnmatchedPdfFile(string pdfPath, string reason)
        {
            try
            {
                // Move unmatched files to a separate folder for review
                var unmatchedFolder = Path.Combine(Path.GetDirectoryName(_config.WatchFolderPath)!, "UnmatchedPDFs");
                Directory.CreateDirectory(unmatchedFolder);

                var fileName = Path.GetFileName(pdfPath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var newFileName = $"{timestamp}_{fileName}";
                var newPath = Path.Combine(unmatchedFolder, newFileName);

                File.Move(pdfPath, newPath);

                _logger.LogWarning("📁 Moved unmatched PDF to: {NewPath} (Reason: {Reason})", newPath, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to move unmatched PDF file: {Path}", pdfPath);
            }
        }

        private async Task HandleFailedPdfFile(string pdfPath, string errorMessage)
        {
            try
            {
                // Move failed files to a separate folder for review
                var failedFolder = Path.Combine(Path.GetDirectoryName(_config.WatchFolderPath)!, "FailedPDFs");
                Directory.CreateDirectory(failedFolder);

                var fileName = Path.GetFileName(pdfPath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var newFileName = $"{timestamp}_{fileName}";
                var newPath = Path.Combine(failedFolder, newFileName);

                if (File.Exists(pdfPath))
                {
                    File.Move(pdfPath, newPath);
                    _logger.LogError("📁 Moved failed PDF to: {NewPath} (Error: {Error})", newPath, errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to move failed PDF file: {Path}", pdfPath);
            }
        }

        private async Task PerformHealthCheck()
        {
            try
            {
                // Check if watcher is still enabled
                if (!_pdfWatcher.EnableRaisingEvents)
                {
                    _logger.LogWarning("⚠️ PDF file watcher is disabled, attempting to restart...");
                    _pdfWatcher.EnableRaisingEvents = true;
                }

                // Check if watch folder still exists
                if (!Directory.Exists(_config.WatchFolderPath))
                {
                    _logger.LogWarning("⚠️ PDF watch folder missing, recreating: {Folder}", _config.WatchFolderPath);
                    if (_config.CreateWatchFolderIfNotExists)
                    {
                        Directory.CreateDirectory(_config.WatchFolderPath);
                    }
                }

                // Check pending files count
                var fileCount = Directory.Exists(_config.WatchFolderPath) ?
                    Directory.GetFiles(_config.WatchFolderPath, _config.FileFilter).Length : 0;

                if (fileCount > _config.MaxPendingFilesAlert)
                {
                    _logger.LogWarning("⚠️ High number of pending PDF files: {FileCount} (Alert threshold: {Threshold})",
                        fileCount, _config.MaxPendingFilesAlert);
                }

                _logger.LogDebug("💗 Health check: Monitoring {Folder}, {FileCount} PDF files present",
                    _config.WatchFolderPath, fileCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Health check failed");
            }
        }

        // Public methods for manual operations and monitoring
        public async Task<int> ProcessAllPendingFiles()
        {
            _logger.LogInformation("🔄 Manual processing of all pending PDF files requested");

            var processedCount = 0;
            try
            {
                if (!Directory.Exists(_config.WatchFolderPath))
                    return 0;

                var pendingFiles = Directory.GetFiles(_config.WatchFolderPath, _config.FileFilter);

                foreach (var file in pendingFiles)
                {
                    try
                    {
                        await ProcessPdfFileAsync(file, "Manual Processing");
                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to process file: {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in manual processing");
            }

            _logger.LogInformation("✅ Manual processing completed: {ProcessedCount} files", processedCount);
            return processedCount;
        }

        public PdfMonitoringStatus GetMonitoringStatus()
        {
            var pendingFileCount = 0;
            var folderExists = Directory.Exists(_config.WatchFolderPath);

            if (folderExists)
            {
                try
                {
                    pendingFileCount = Directory.GetFiles(_config.WatchFolderPath, _config.FileFilter).Length;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Could not count pending files");
                }
            }

            return new PdfMonitoringStatus
            {
                IsMonitoring = _pdfWatcher.EnableRaisingEvents,
                WatchFolder = _config.WatchFolderPath,
                FolderExists = folderExists,
                PendingFileCount = pendingFileCount,
                LastHealthCheck = DateTime.Now
            };
        }

        public void StopMonitoring()
        {
            _pdfWatcher.EnableRaisingEvents = false;
            _logger.LogInformation("⏸️ PDF monitoring stopped");
        }

        public void StartMonitoring()
        {
            _pdfWatcher.EnableRaisingEvents = true;
            _logger.LogInformation("▶️ PDF monitoring started");
        }

        public override void Dispose()
        {
            _pdfWatcher?.Dispose();
            base.Dispose();
        }
    }
}