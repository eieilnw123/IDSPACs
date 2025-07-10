using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text;
using WorklistServiceApp.Configuration;
using WorklistServiceApp.Data;
using WorklistServiceApp.Models;
using idspacsgateway.Services; 
namespace WorklistServiceApp.Services
{
    public class WorklistSyncService : BackgroundService
    {
        private readonly ILogger<WorklistSyncService> _logger;
        private readonly DatabaseService _databaseService;
        private readonly DicomSyncConfiguration _config;
        private Timer? _syncTimer;
        private bool _isSyncing = false;
        private readonly LoggingService _loggingService; 
        public WorklistSyncService(
            ILogger<WorklistSyncService> logger,
            DatabaseService databaseService,
            IOptions<DicomSyncConfiguration> config,
             LoggingService loggingService)
        {
            _logger = logger;
            _databaseService = databaseService;
            _config = config.Value;
            _loggingService = loggingService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await InitializeDicom();

                // เริ่มต้นด้วยการ sync ครั้งแรก
                await PerformWorklistSync();

                // ตั้ง timer สำหรับ sync ตาม config
                _syncTimer = new Timer(async _ => await PerformWorklistSync(),
                    null, TimeSpan.Zero, TimeSpan.FromMinutes(_config.SyncIntervalMinutes));

                _logger.LogInformation("🔄 WorklistSyncService started - syncing every {Interval} minutes",
                    _config.SyncIntervalMinutes);

                // Keep service running
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Critical error in WorklistSyncService");
            }
        }

        private async Task InitializeDicom()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            new DicomSetupBuilder()
                .RegisterServices(s =>
                    s.AddFellowOakDicom()
                     .AddLogging(config => config.AddConsole())
                     .Configure<DicomServiceOptions>(o => { })
                )
                .Build();

            _logger.LogInformation("⚙️ DICOM system initialized");
        }

        private async Task PerformWorklistSync()
        {
            if (_isSyncing)
            {
                _logger.LogDebug("🔄 Sync already in progress, skipping");
                return;
            }

            _isSyncing = true;

            try
            {
                _logger.LogInformation("🔄 Starting worklist synchronization...");
                await _loggingService.SendLogToClients("info", "🔄 Starting worklist synchronization...", "WorklistSync");

                var worklistItems = await GetWorklistFromDicomServer();
                _logger.LogInformation("📥 Retrieved {Count} worklist items from DICOM server", worklistItems.Count);
                await _loggingService.SendLogToClients("info", $"📥 Retrieved {worklistItems.Count} worklist items from DICOM server", "WorklistSync");
                var savedCount = 0;
                var updatedCount = 0;
                var skippedCount = 0;

                foreach (var dataset in worklistItems)
                {
                    try
                    {
                        var result = await _databaseService.SaveOrUpdateWorklistItem(dataset);

                        switch (result.Action)
                        {
                            case Models.DatabaseAction.Inserted:
                                savedCount++;
                                _logger.LogInformation("💾 New: {PatientName} ({PatientID}) - {AccessionNumber}",
                                    result.PatientName, result.PatientID, result.AccessionNumber);
                                await _loggingService.SendLogToClients("info", $"💾 New: {result.PatientName} ({result.PatientID})", "WorklistSync");
                                break;
                            case Models.DatabaseAction.Updated:
                                updatedCount++;
                                _logger.LogInformation("🔄 Updated: {PatientName} ({PatientID}) - {AccessionNumber}",
                                    result.PatientName, result.PatientID, result.AccessionNumber);
                                await _loggingService.SendLogToClients("info", $"🔄 Updated: {result.PatientName} ({result.PatientID})", "WorklistSync");
                                break;
                            case Models.DatabaseAction.Skipped:
                                skippedCount++;
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        skippedCount++;
                        var accessionNumber = dataset.GetSingleValueOrDefault(DicomTag.AccessionNumber, "Unknown");
                        _logger.LogError(ex, "❌ Failed to process worklist item: {AccessionNumber}", accessionNumber);
                        await _loggingService.SendLogToClients("error", $"❌ Failed to process: {accessionNumber}", "WorklistSync");
                    }
                }

                _logger.LogInformation("📊 Sync Summary: {New} new, {Updated} updated, {Skipped} unchanged",
                    savedCount, updatedCount, skippedCount);

                // Update sync statistics
                await _databaseService.UpdateSyncStatistics(savedCount, updatedCount, skippedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during worklist synchronization");
                await _loggingService.SendLogToClients("error", $"❌ Sync error: {ex.Message}", "WorklistSync");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private async Task<List<DicomDataset>> GetWorklistFromDicomServer()
        {
            var worklistItems = new List<DicomDataset>();

            try
            {
                // ตรวจสอบว่าเป็น Development environment
                var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

                if (environment == "Development")
                {
                    _logger.LogInformation("🎭 Development Mode: Using Mock DICOM Worklist Data");
                    return await GetMockWorklistData();
                }



                var cfind = DicomCFindRequest.CreateWorklistQuery();

                // Query parameters - ค้นหาข้อมูลตาม config
                var sps = new DicomDataset();

                // ใช้ modalities จาก config หรือ default เป็น EKG
                var modality = string.IsNullOrWhiteSpace(_config.FilterModality) ? "ECG" : _config.FilterModality;
                sps.AddOrUpdate(DicomTag.Modality, modality);

                // ค้นหาตามช่วงวันที่จาก config
                var dateRange = GetDateRange();
                sps.AddOrUpdate(DicomTag.ScheduledProcedureStepStartDate, dateRange);

                cfind.Dataset.AddOrUpdate(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps));

                // Response handler
                cfind.OnResponseReceived = (DicomCFindRequest rq, DicomCFindResponse rp) =>
                {
                    if (rp.HasDataset)
                    {
                        try
                        {
                            var patientName = GetCorrectPatientName(rp.Dataset);
                            var patientID = rp.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, "");
                            var accessionNumber = rp.Dataset.GetSingleValueOrDefault(DicomTag.AccessionNumber, "");

                            _logger.LogDebug("📄 Found: {PatientName} | {PatientID} | {AccessionNumber}",
                                patientName, patientID, accessionNumber);

                            worklistItems.Add(rp.Dataset);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Failed to parse worklist item");
                        }
                    }
                };

                // Execute query using config
                _logger.LogInformation("🔌 Connecting to DICOM server: {ServerHost}:{Port}",
                    _config.ServerHost, _config.ServerPort);

                var client = DicomClientFactory.Create(
                    _config.ServerHost,
                    _config.ServerPort,
                    false,
                    _config.CallingAE,
                    _config.ServerAE);

                await client.AddRequestAsync(cfind);
                await client.SendAsync();

                _logger.LogInformation("✅ DICOM query completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to retrieve worklist from DICOM server");
                throw;
            }

            return worklistItems;
        }


        private async Task<List<DicomDataset>> GetMockWorklistData()
        {
            var mockWorklistItems = new List<DicomDataset>();

            // Mock Patient 1
            var dataset1 = new DicomDataset();
            dataset1.AddOrUpdate(DicomTag.PatientID, "999");
            dataset1.AddOrUpdate(DicomTag.PatientName, "Test");
            dataset1.AddOrUpdate(DicomTag.PatientBirthDate, "19710526");
            dataset1.AddOrUpdate(DicomTag.PatientSex, "M");
            dataset1.AddOrUpdate(DicomTag.AccessionNumber, "11395391");
            dataset1.AddOrUpdate(DicomTag.StudyDate, "");
            dataset1.AddOrUpdate(DicomTag.StudyInstanceUID, "1.2.410.2000010.66.101.11395391.48795680");
            dataset1.AddOrUpdate(DicomTag.SpecificCharacterSet, "");

            // Scheduled Procedure Step
            var sps1 = new DicomDataset();
            sps1.AddOrUpdate(DicomTag.ScheduledProcedureStepStartDate, "20250704");
            sps1.AddOrUpdate(DicomTag.ScheduledProcedureStepStartTime, "140427");
            sps1.AddOrUpdate(DicomTag.ScheduledStationAETitle, "ECG");
            sps1.AddOrUpdate(DicomTag.ScheduledProcedureStepDescription, "EKG (Electro Cardiography)");
            sps1.AddOrUpdate(DicomTag.Modality, "ECG");
            dataset1.AddOrUpdate(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps1));

            mockWorklistItems.Add(dataset1);


            _logger.LogInformation("🎭 Generated {Count} mock worklist items", mockWorklistItems.Count);

            // จำลองการรอเวลา (เหมือนการ query จริง)
            await Task.Delay(1000);

            return mockWorklistItems;
        }



        private string GetDateRange()
        {
            var today = DateTime.Now;

            // สำหรับวันเดียว (QueryDaysAhead = 0)
            if (_config.QueryDaysAhead == 0)
            {
                return today.ToString("yyyyMMdd"); // ส่งแค่วันเดียว ไม่ใช่ range
            }

            var startDate = today.AddDays(-_config.QueryDaysBehind);  // ย้อนหลัง
            var endDate = today.AddDays(_config.QueryDaysAhead);

            if (_config.QueryDaysAhead < 0)
            {
                startDate = endDate;
                endDate = today;
            }

            return $"{startDate:yyyyMMdd}-{endDate:yyyyMMdd}";
        }
        private string GetCorrectPatientName(DicomDataset dataset)
        {
            try
            {
                var characterSet = dataset.GetSingleValueOrDefault(DicomTag.SpecificCharacterSet, "");
                var patientNameElement = dataset.GetDicomItem<DicomElement>(DicomTag.PatientName);
                if (patientNameElement == null) return "";

                byte[] rawBytes = patientNameElement.Buffer.Data;

                // Check for multi-language name (contains = delimiter)
                string rawString = Encoding.UTF8.GetString(rawBytes);
                if (rawString.Contains("="))
                {
                    var parts = rawString.Split('=');
                    var local = parts.Length > 1 ? parts[1] : parts[0];
                    return local.Trim();
                }

                // Use Character Set mapping first
                Encoding encoding = GetEncodingFromCharacterSet(characterSet);
                string decodedName = encoding.GetString(rawBytes);

                // Validate result and use auto-detection if needed
                if (!IsValidDecodedName(decodedName))
                    decodedName = AutoDetectEncoding(rawBytes);

                return decodedName;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to decode patient name");
                return "ไม่สามารถอ่านชื่อได้";
            }
        }

        private string AutoDetectEncoding(byte[] rawBytes)
        {
            var encodingTests = new[]
            {
                ("UTF-8", Encoding.UTF8),
                ("TIS-620", GetSafeEncoding("TIS-620")),
                ("Windows-874", GetSafeEncoding("windows-874")),
                ("ISO-8859-1", Encoding.GetEncoding("ISO-8859-1")),
                ("ASCII", Encoding.ASCII)
            };

            foreach (var (name, encoding) in encodingTests)
            {
                if (encoding == null) continue;
                try
                {
                    var decoded = encoding.GetString(rawBytes);
                    if (IsValidDecodedName(decoded))
                    {
                        if (name == "UTF-8" && decoded.Any(c => c >= 0x0E00 && c <= 0x0E7F))
                            return decoded;
                        else if (name != "UTF-8")
                            return decoded;
                    }
                }
                catch { }
            }
            return Encoding.UTF8.GetString(rawBytes);
        }

        private bool IsValidDecodedName(string decodedName)
        {
            if (string.IsNullOrWhiteSpace(decodedName)) return false;
            if (decodedName.Count(c => c == '?') > decodedName.Length * 0.3) return false;
            if (decodedName.All(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
            return true;
        }

        private static Encoding? GetSafeEncoding(string encodingName)
        {
            try { return Encoding.GetEncoding(encodingName); }
            catch { return null; }
        }

        private static Encoding GetEncodingFromCharacterSet(string characterSet)
        {
            return characterSet switch
            {
                // Single-byte character sets
                "ISO_IR 100" => Encoding.GetEncoding("ISO-8859-1"),    // Latin alphabet No. 1 (Western Europe)
                "ISO_IR 101" => Encoding.GetEncoding("ISO-8859-2"),    // Latin alphabet No. 2 (Central/Eastern Europe)
                "ISO_IR 109" => Encoding.GetEncoding("ISO-8859-3"),    // Latin alphabet No. 3 (South Europe)
                "ISO_IR 110" => Encoding.GetEncoding("ISO-8859-4"),    // Latin alphabet No. 4 (North Europe)
                "ISO_IR 144" => Encoding.GetEncoding("ISO-8859-5"),    // Latin/Cyrillic alphabet
                "ISO_IR 127" => Encoding.GetEncoding("ISO-8859-6"),    // Latin/Arabic alphabet
                "ISO_IR 126" => Encoding.GetEncoding("ISO-8859-7"),    // Latin/Greek alphabet
                "ISO_IR 138" => Encoding.GetEncoding("ISO-8859-8"),    // Latin/Hebrew alphabet
                "ISO_IR 148" => Encoding.GetEncoding("ISO-8859-9"),    // Latin alphabet No. 5 (Turkish)
                "ISO_IR 203" => Encoding.GetEncoding("ISO-8859-15"),   // Latin alphabet No. 9

                // Multi-byte character sets - without code extensions
                "ISO_IR 192" => Encoding.UTF8,                         // Unicode in UTF-8
                "ISO_IR 13" => Encoding.GetEncoding("shift_jis"),      // Japanese (JIS X 0201 + JIS X 0208)
                "ISO_IR 166" or "TIS620" => Encoding.GetEncoding("TIS-620"), // Thai (TIS 620-2533)
                "ISO_IR 149" => Encoding.GetEncoding("ks_c_5601-1987"), // Korean (KS X 1001)
                "GB18030" => Encoding.GetEncoding("GB18030"),          // Chinese (GB 18030)
                "ISO_IR 58" => Encoding.GetEncoding("GB2312"),         // Chinese simplified (GB 2312-80)

                // Multi-byte character sets - with code extensions (ISO 2022)
                "ISO 2022 IR 6" => Encoding.ASCII,                     // ASCII (G0)
                "ISO 2022 IR 13" => Encoding.GetEncoding("shift_jis"), // Japanese Katakana (G1)
                "ISO 2022 IR 87" => Encoding.GetEncoding("iso-2022-jp"), // Japanese Kanji (G0)
                "ISO 2022 IR 159" => Encoding.GetEncoding("iso-2022-jp"), // Japanese Kanji supplement (G0)
                "ISO 2022 IR 149" => Encoding.GetEncoding("iso-2022-kr"), // Korean (G1)
                "ISO 2022 IR 58" => Encoding.GetEncoding("iso-2022-cn"), // Chinese simplified (G1)

                // Windows code pages (commonly used)
                "WINDOWS-1252" => Encoding.GetEncoding("windows-1252"), // Western European
                "WINDOWS-1251" => Encoding.GetEncoding("windows-1251"), // Cyrillic
                "WINDOWS-1256" => Encoding.GetEncoding("windows-1256"), // Arabic
                "WINDOWS-1255" => Encoding.GetEncoding("windows-1255"), // Hebrew
                "WINDOWS-1253" => Encoding.GetEncoding("windows-1253"), // Greek
                "WINDOWS-1254" => Encoding.GetEncoding("windows-1254"), // Turkish
                "WINDOWS-874" => Encoding.GetEncoding("windows-874"),   // Thai
                "WINDOWS-932" => Encoding.GetEncoding("windows-932"),   // Japanese (Shift JIS)
                "WINDOWS-936" => Encoding.GetEncoding("windows-936"),   // Chinese Simplified (GBK)
                "WINDOWS-949" => Encoding.GetEncoding("windows-949"),   // Korean
                "WINDOWS-950" => Encoding.GetEncoding("windows-950"),   // Chinese Traditional (Big5)

                // Additional encodings
                "BIG5" => Encoding.GetEncoding("big5"),                 // Chinese Traditional
                "EUC-JP" => Encoding.GetEncoding("euc-jp"),            // Japanese Extended Unix Code
                "EUC-KR" => Encoding.GetEncoding("euc-kr"),            // Korean Extended Unix Code
                "KOI8-R" => Encoding.GetEncoding("koi8-r"),            // Russian
                "KOI8-U" => Encoding.GetEncoding("koi8-u"),            // Ukrainian

                // Default fallback
                "" or null => Encoding.UTF8,                           // Empty or null defaults to UTF-8
                _ => Encoding.UTF8                                      // Unknown encoding defaults to UTF-8
            };
        }

        public async Task<Models.WorklistSyncResult> ManualSync()
        {
            _logger.LogInformation("🔄 Manual sync triggered");
            var startTime = DateTime.Now;

            try
            {
                await PerformWorklistSync();
                var endTime = DateTime.Now;

                return new Models.WorklistSyncResult
                {
                    Success = true,
                    StartTime = startTime,
                    EndTime = endTime,
                    Duration = endTime - startTime,
                    Message = "Manual sync completed successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Manual sync failed");
                return new Models.WorklistSyncResult
                {
                    Success = false,
                    StartTime = startTime,
                    EndTime = DateTime.Now,
                    Message = $"Manual sync failed: {ex.Message}"
                };
            }
        }

        public async Task<Models.WorklistSyncStats> GetSyncStatistics()
        {
            return await _databaseService.GetSyncStatistics();
        }

        public override void Dispose()
        {
            _syncTimer?.Dispose();
            base.Dispose();
        }
    }

}
