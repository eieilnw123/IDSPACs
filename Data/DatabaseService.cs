using FellowOakDicom;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text;
using WorklistServiceApp.Models;

namespace WorklistServiceApp.Data
{
    public class DatabaseService
    {
        private readonly ILogger<DatabaseService> _logger;
        private const string DbPath = "Data/worklist.db";

        public DatabaseService(ILogger<DatabaseService> logger)
        {
            _logger = logger;
            EnsureDatabase();
        }

        private void EnsureDatabase()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
          
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS WorklistItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        AccessionNumber TEXT UNIQUE,
                        PatientID TEXT,
                        PatientName TEXT,
                        PatientBirthDate TEXT,
                        PatientSex TEXT,
                        Modality TEXT,
                        StudyDate TEXT,
                        StudyTime TEXT,
                        SeriesDate TEXT,
                        SeriesTime TEXT,
                        SeriesDescription TEXT,
                        ScheduledDate TEXT,
                        ScheduledTime TEXT,
                        StationAET TEXT,
                        ProcedureDescription TEXT,
                        StudyInstanceUID TEXT,
                        CharacterSet TEXT,
                        WorklistStatus TEXT DEFAULT 'SCHEDULED',
                        
                        -- File paths
                        PdfFilePath TEXT,
                        JpegFilePath TEXT,
                        DicomFilePath TEXT,
                        
                        -- Timestamps
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                        ProcessedAt TEXT,
                        CompletedAt TEXT,
                        
                        -- Processing info
                        ProcessingNotes TEXT,
                        ErrorMessage TEXT,
                        RetryCount INTEGER DEFAULT 0
                    );

                    -- Add missing columns if they don't exist (for existing databases)
                    PRAGMA table_info(WorklistItems);
                    ";

                command.ExecuteNonQuery();

                // Add missing columns if database already exists
                try
                {
                    var alterCommands = new[]
                    {
                        "ALTER TABLE WorklistItems ADD COLUMN PatientBirthDate TEXT DEFAULT '';",
                        "ALTER TABLE WorklistItems ADD COLUMN PatientSex TEXT DEFAULT '';",
                        "ALTER TABLE WorklistItems ADD COLUMN StudyTime TEXT DEFAULT '';",
                        "ALTER TABLE WorklistItems ADD COLUMN SeriesDate TEXT DEFAULT '';",
                        "ALTER TABLE WorklistItems ADD COLUMN SeriesTime TEXT DEFAULT '';",
                        "ALTER TABLE WorklistItems ADD COLUMN SeriesDescription TEXT DEFAULT '';"
                    };

                    foreach (var alterCommand in alterCommands)
                    {
                        try
                        {
                            var alterCmd = connection.CreateCommand();
                            alterCmd.CommandText = alterCommand;
                            alterCmd.ExecuteNonQuery();
                        }
                        catch
                        {
                            // Column might already exist, ignore error
                        }
                    }
                }
                catch
                {
                    // Ignore ALTER errors
                }

                // Continue with the rest of the schema
                var remainingCommand = connection.CreateCommand();
                remainingCommand.CommandText = @"
                    CREATE TABLE IF NOT EXISTS SyncStatistics (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SyncDate TEXT DEFAULT CURRENT_TIMESTAMP,
                        NewItems INTEGER DEFAULT 0,
                        UpdatedItems INTEGER DEFAULT 0,
                        SkippedItems INTEGER DEFAULT 0,
                        TotalItems INTEGER DEFAULT 0,
                        Duration INTEGER DEFAULT 0,
                        Success INTEGER DEFAULT 1
                    );

                    -- Indexes for performance
                    CREATE INDEX IF NOT EXISTS idx_worklist_patient_id ON WorklistItems(PatientID);
                    CREATE INDEX IF NOT EXISTS idx_worklist_accession ON WorklistItems(AccessionNumber);
                    CREATE INDEX IF NOT EXISTS idx_worklist_status ON WorklistItems(WorklistStatus);
                    CREATE INDEX IF NOT EXISTS idx_worklist_scheduled_date ON WorklistItems(ScheduledDate);
                    CREATE INDEX IF NOT EXISTS idx_sync_date ON SyncStatistics(SyncDate);
                    ";
                remainingCommand.ExecuteNonQuery();

                _logger.LogInformation("🗄️ Database initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize database");
                throw;
            }
        }

        public async Task<DatabaseOperationResult> SaveOrUpdateWorklistItem(DicomDataset dataset)
        {
            try
            {
                var accessionNumber = dataset.GetSingleValueOrDefault(DicomTag.AccessionNumber, "");
                var patientID = dataset.GetSingleValueOrDefault(DicomTag.PatientID, "");
                var characterSet = dataset.GetSingleValueOrDefault(DicomTag.SpecificCharacterSet, "");
                var patientName = ExtractPatientName(dataset);
                var studyDate = dataset.GetSingleValueOrDefault(DicomTag.StudyDate, "");
                var modality = ExtractModality(dataset);
                var (scheduledDate, scheduledTime, stationAET, procedureDescription) = ExtractScheduledProcedureStepInfo(dataset);
                var studyInstanceUID = dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "") ??
                                       DicomUIDGenerator.GenerateDerivedFromUUID().UID;

                // เพิ่มการดึงข้อมูลเพิ่มเติม
                var patientBirthDate = dataset.GetSingleValueOrDefault(DicomTag.PatientBirthDate, "");
                var patientSex = dataset.GetSingleValueOrDefault(DicomTag.PatientSex, "");
                var studyTime = dataset.GetSingleValueOrDefault(DicomTag.StudyTime, "");

                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();


                // Check if item already exists
                var existingItem = await GetWorklistItemByAccessionNumber(accessionNumber);

                if (existingItem != null)
                {
                    // Update existing item (only if not completed)
                    if (existingItem.WorklistStatus == Models.WorklistStatus.COMPLETED.ToString())
                    {
                        return new DatabaseOperationResult
                        {
                            Action = Models.DatabaseAction.Skipped,
                            AccessionNumber = accessionNumber,
                            PatientID = patientID,
                            PatientName = patientName,
                            Message = "Item already completed, skipped update"
                        };
                    }

                    var updateCommand = connection.CreateCommand();
                    updateCommand.CommandText = @"
                        UPDATE WorklistItems 
                        SET PatientName = $pn, 
                            Modality = $mod, 
                            StudyDate = $sd,
                            StudyTime = $st,
                            ScheduledDate = $spsd, 
                            ScheduledTime = $spst,
                            StationAET = $aet, 
                            ProcedureDescription = $desc,
                            CharacterSet = $cs,
                            StudyInstanceUID = $suid,
                            PatientBirthDate = $pbd,
                            PatientSex = $psex,
                            UpdatedAt = CURRENT_TIMESTAMP
                        WHERE AccessionNumber = $an;";

                    updateCommand.Parameters.AddWithValue("$pn", patientName);
                    updateCommand.Parameters.AddWithValue("$mod", modality);
                    updateCommand.Parameters.AddWithValue("$sd", studyDate);
                    updateCommand.Parameters.AddWithValue("$st", studyTime);
                    updateCommand.Parameters.AddWithValue("$spsd", scheduledDate);
                    updateCommand.Parameters.AddWithValue("$spst", scheduledTime);
                    updateCommand.Parameters.AddWithValue("$aet", stationAET);
                    updateCommand.Parameters.AddWithValue("$desc", procedureDescription);
                    updateCommand.Parameters.AddWithValue("$cs", characterSet);
                    updateCommand.Parameters.AddWithValue("$suid", studyInstanceUID);
                    updateCommand.Parameters.AddWithValue("$pbd", patientBirthDate);
                    updateCommand.Parameters.AddWithValue("$psex", patientSex);
                    updateCommand.Parameters.AddWithValue("$an", accessionNumber);

                    updateCommand.ExecuteNonQuery();

                    return new DatabaseOperationResult
                    {
                        Action = Models.DatabaseAction.Updated,
                        AccessionNumber = accessionNumber,
                        PatientID = patientID,
                        PatientName = patientName,
                        Message = "Worklist item updated"
                    };
                }
                else
                {
                    // Insert new item
                    var insertCommand = connection.CreateCommand();
                    insertCommand.CommandText = @"
                        INSERT INTO WorklistItems
                        (AccessionNumber, PatientID, PatientName, Modality, StudyDate, StudyTime, ScheduledDate, ScheduledTime, 
                         StationAET, ProcedureDescription, StudyInstanceUID, CharacterSet, WorklistStatus, PatientBirthDate, PatientSex)
                        VALUES ($an, $pid, $pn, $mod, $sd, $st, $spsd, $spst, $aet, $desc, $suid, $cs, $status, $pbd, $psex);";

                    insertCommand.Parameters.AddWithValue("$an", accessionNumber);
                    insertCommand.Parameters.AddWithValue("$pid", patientID);
                    insertCommand.Parameters.AddWithValue("$pn", patientName);
                    insertCommand.Parameters.AddWithValue("$mod", modality);
                    insertCommand.Parameters.AddWithValue("$sd", studyDate);
                    insertCommand.Parameters.AddWithValue("$st", studyTime);
                    insertCommand.Parameters.AddWithValue("$spsd", scheduledDate);
                    insertCommand.Parameters.AddWithValue("$spst", scheduledTime);
                    insertCommand.Parameters.AddWithValue("$aet", stationAET);
                    insertCommand.Parameters.AddWithValue("$desc", procedureDescription);
                    insertCommand.Parameters.AddWithValue("$suid", studyInstanceUID);
                    insertCommand.Parameters.AddWithValue("$cs", characterSet);
                    insertCommand.Parameters.AddWithValue("$status", Models.WorklistStatus.SCHEDULED.ToString());
                    insertCommand.Parameters.AddWithValue("$pbd", patientBirthDate);
                    insertCommand.Parameters.AddWithValue("$psex", patientSex);

                    insertCommand.ExecuteNonQuery();

                    return new DatabaseOperationResult
                    {
                        Action = Models.DatabaseAction.Inserted,
                        AccessionNumber = accessionNumber,
                        PatientID = patientID,
                        PatientName = patientName,
                        Message = "New worklist item added"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to save/update worklist item");
                throw;
            }
        }

        public async Task<Models.WorklistItem?> GetWorklistItemByAccessionNumber(string accessionNumber)
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM WorklistItems WHERE AccessionNumber = $an;";
            command.Parameters.AddWithValue("$an", accessionNumber);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return MapReaderToWorklistItem(reader);
            }

            return null;
        }

        public async Task<Models.WorklistItem?> GetWorklistItemByPatientID(string patientID, Models.WorklistStatus status = Models.WorklistStatus.SCHEDULED)
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM WorklistItems 
                WHERE PatientID = $pid AND WorklistStatus = $status
                ORDER BY ScheduledDate DESC, ScheduledTime DESC
                LIMIT 1;";
            command.Parameters.AddWithValue("$pid", patientID);
            command.Parameters.AddWithValue("$status", status.ToString());

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return MapReaderToWorklistItem(reader);
            }

            return null;
        }

        public async Task<List<Models.WorklistItem>> GetWorklistItemsByStatus(Models.WorklistStatus status)
        {
            var items = new List<Models.WorklistItem>();

            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM WorklistItems 
                WHERE WorklistStatus = $status
                ORDER BY ScheduledDate DESC, ScheduledTime DESC;";
            command.Parameters.AddWithValue("$status", status.ToString());

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(MapReaderToWorklistItem(reader));
            }

            return items;
        }

        public async Task<bool> UpdateWorklistStatus(string accessionNumber, Models.WorklistStatus newStatus, string notes = "", string errorMessage = "")
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE WorklistItems 
                    SET WorklistStatus = $status, 
                        UpdatedAt = CURRENT_TIMESTAMP,
                        ProcessingNotes = CASE WHEN $notes != '' THEN $notes ELSE ProcessingNotes END,
                        ErrorMessage = CASE WHEN $error != '' THEN $error ELSE ErrorMessage END,
                        ProcessedAt = CASE WHEN $status IN ('PDF_RECEIVED', 'JPEG_GENERATED', 'DICOM_CREATED') 
                                          THEN CURRENT_TIMESTAMP ELSE ProcessedAt END,
                        CompletedAt = CASE WHEN $status = 'COMPLETED' THEN CURRENT_TIMESTAMP ELSE CompletedAt END
                    WHERE AccessionNumber = $an;";

                command.Parameters.AddWithValue("$status", newStatus.ToString());
                command.Parameters.AddWithValue("$an", accessionNumber);
                command.Parameters.AddWithValue("$notes", notes);
                command.Parameters.AddWithValue("$error", errorMessage);

                var rowsAffected = command.ExecuteNonQuery();

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("📊 Status updated: {AccessionNumber} → {Status}", accessionNumber, newStatus);
                    return true;
                }

                _logger.LogWarning("⚠️ No rows updated for AccessionNumber: {AccessionNumber}", accessionNumber);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update worklist status");
                return false;
            }
        }

        public async Task<bool> UpdateFilePaths(string accessionNumber, string pdfPath = "", string jpegPath = "", string dicomPath = "")
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE WorklistItems 
                    SET PdfFilePath = CASE WHEN $pdf != '' THEN $pdf ELSE PdfFilePath END,
                        JpegFilePath = CASE WHEN $jpeg != '' THEN $jpeg ELSE JpegFilePath END,
                        DicomFilePath = CASE WHEN $dicom != '' THEN $dicom ELSE DicomFilePath END,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE AccessionNumber = $an;";

                command.Parameters.AddWithValue("$pdf", pdfPath);
                command.Parameters.AddWithValue("$jpeg", jpegPath);
                command.Parameters.AddWithValue("$dicom", dicomPath);
                command.Parameters.AddWithValue("$an", accessionNumber);

                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update file paths");
                return false;
            }
        }

        public async Task<bool> UpdateSeriesInfo(string accessionNumber, string seriesDate, string seriesTime, string seriesDescription)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE WorklistItems 
                    SET SeriesDate = $seriesDate,
                        SeriesTime = $seriesTime,
                        SeriesDescription = $seriesDescription,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE AccessionNumber = $an;";

                command.Parameters.AddWithValue("$seriesDate", seriesDate);
                command.Parameters.AddWithValue("$seriesTime", seriesTime);
                command.Parameters.AddWithValue("$seriesDescription", seriesDescription);
                command.Parameters.AddWithValue("$an", accessionNumber);

                var rowsAffected = command.ExecuteNonQuery();

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("📊 Series info updated: {AccessionNumber} → {SeriesDate} {SeriesTime}",
                        accessionNumber, seriesDate, seriesTime);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update series info");
                return false;
            }
        }

        public async Task UpdateSyncStatistics(int newItems, int updatedItems, int skippedItems)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO SyncStatistics (NewItems, UpdatedItems, SkippedItems, TotalItems)
                    VALUES ($new, $updated, $skipped, $total);";

                var totalItems = newItems + updatedItems + skippedItems;
                command.Parameters.AddWithValue("$new", newItems);
                command.Parameters.AddWithValue("$updated", updatedItems);
                command.Parameters.AddWithValue("$skipped", skippedItems);
                command.Parameters.AddWithValue("$total", totalItems);

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update sync statistics");
            }
        }

        public async Task<Models.WorklistSyncStats> GetSyncStatistics()
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            // Get latest sync info
            var syncCommand = connection.CreateCommand();
            syncCommand.CommandText = @"
                SELECT * FROM SyncStatistics 
                ORDER BY SyncDate DESC 
                LIMIT 1;";

            DateTime lastSyncTime = DateTime.MinValue;
            using (var syncReader = syncCommand.ExecuteReader())
            {
                if (syncReader.Read())
                {
                    DateTime.TryParse(syncReader.GetString("SyncDate"), out lastSyncTime);
                }
            }

            // Get worklist statistics
            var statsCommand = connection.CreateCommand();
            statsCommand.CommandText = @"
                SELECT 
                    COUNT(*) as TotalItems,
                    COUNT(CASE WHEN ScheduledDate = $today THEN 1 END) as TodayItems,
                    COUNT(CASE WHEN WorklistStatus IN ('SCHEDULED', 'PDF_RECEIVED', 'JPEG_GENERATED', 'DICOM_CREATED') THEN 1 END) as PendingItems,
                    COUNT(CASE WHEN WorklistStatus = 'COMPLETED' THEN 1 END) as CompletedItems,
                    COUNT(CASE WHEN WorklistStatus = 'FAILED' THEN 1 END) as FailedItems
                FROM WorklistItems;";

            statsCommand.Parameters.AddWithValue("$today", DateTime.Now.ToString("yyyyMMdd"));

            using var statsReader = statsCommand.ExecuteReader();
            if (statsReader.Read())
            {
                return new Models.WorklistSyncStats
                {
                    LastSyncTime = lastSyncTime,
                    TotalItems = statsReader.GetInt32("TotalItems"),
                    TodayItems = statsReader.GetInt32("TodayItems"),
                    PendingItems = statsReader.GetInt32("PendingItems"),
                    CompletedItems = statsReader.GetInt32("CompletedItems"),
                    FailedItems = statsReader.GetInt32("FailedItems")
                };
            }

            return new Models.WorklistSyncStats();
        }

        public async Task<Dictionary<string, int>> GetStatusSummary()
        {
            var summary = new Dictionary<string, int>();

            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT WorklistStatus, COUNT(*) as Count 
                FROM WorklistItems 
                GROUP BY WorklistStatus;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var status = reader.GetString("WorklistStatus");
                var count = reader.GetInt32("Count");
                summary[status] = count;
            }

            return summary;
        }

        public async Task<List<Models.WorklistItem>> GetAllWorklistItemsAsync()
        {
            var items = new List<Models.WorklistItem>();

            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM WorklistItems 
                ORDER BY UpdatedAt DESC;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(MapReaderToWorklistItem(reader));
            }

            return items;
        }

        public async Task<List<Models.WorklistItem>> GetRecentItems(int limit = 50)
        {
            var items = new List<Models.WorklistItem>();

            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM WorklistItems 
                ORDER BY UpdatedAt DESC 
                LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(MapReaderToWorklistItem(reader));
            }

            return items;
        }

        public async Task<bool> IncrementRetryCount(string accessionNumber)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    UPDATE WorklistItems 
                    SET RetryCount = RetryCount + 1,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE AccessionNumber = $an;";
                command.Parameters.AddWithValue("$an", accessionNumber);

                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to increment retry count");
                return false;
            }
        }

        public async Task TestConnectionAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={DbPath}");
                await connection.OpenAsync();

                // Test with a simple query
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM WorklistItems";
                var count = await command.ExecuteScalarAsync();

                _logger.LogInformation("✅ Database connection test successful. Total items: {Count}", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Database connection test failed");
                throw;
            }
        }

        private Models.WorklistItem MapReaderToWorklistItem(SqliteDataReader reader)
        {
            return new Models.WorklistItem
            {
                Id = reader.GetInt32("Id"),
                AccessionNumber = reader.GetString("AccessionNumber"),
                PatientID = reader.GetString("PatientID"),
                PatientName = reader.GetString("PatientName"),
                PatientBirthDate = GetStringOrDefault(reader, "PatientBirthDate"),
                PatientSex = GetStringOrDefault(reader, "PatientSex"),
                Modality = reader.GetString("Modality"),
                StudyDate = reader.GetString("StudyDate"),
                StudyTime = GetStringOrDefault(reader, "StudyTime"),
                SeriesDate = GetStringOrDefault(reader, "SeriesDate"),
                SeriesTime = GetStringOrDefault(reader, "SeriesTime"),
                SeriesDescription = GetStringOrDefault(reader, "SeriesDescription"),
                ScheduledDate = reader.GetString("ScheduledDate"),
                ScheduledTime = reader.GetString("ScheduledTime"),
                StationAET = reader.GetString("StationAET"),
                ProcedureDescription = reader.GetString("ProcedureDescription"),
                StudyInstanceUID = reader.IsDBNull("StudyInstanceUID") ? null : reader.GetString("StudyInstanceUID"),
                CharacterSet = reader.IsDBNull("CharacterSet") ? "" : reader.GetString("CharacterSet"),
                WorklistStatus = reader.GetString("WorklistStatus"),
                PdfFilePath = reader.IsDBNull("PdfFilePath") ? null : reader.GetString("PdfFilePath"),
                JpegFilePath = reader.IsDBNull("JpegFilePath") ? null : reader.GetString("JpegFilePath"),
                DicomFilePath = reader.IsDBNull("DicomFilePath") ? null : reader.GetString("DicomFilePath"),
                CreatedAt = DateTime.Parse(reader.GetString("CreatedAt")),
                UpdatedAt = DateTime.Parse(reader.GetString("UpdatedAt")),
                ProcessedAt = reader.IsDBNull("ProcessedAt") ? null : DateTime.Parse(reader.GetString("ProcessedAt")),
                CompletedAt = reader.IsDBNull("CompletedAt") ? null : DateTime.Parse(reader.GetString("CompletedAt")),
                ProcessingNotes = reader.IsDBNull("ProcessingNotes") ? null : reader.GetString("ProcessingNotes"),
                ErrorMessage = reader.IsDBNull("ErrorMessage") ? null : reader.GetString("ErrorMessage"),
                RetryCount = reader.GetInt32("RetryCount")
            };
        }

        // Helper method สำหรับ handle missing columns
        private string GetStringOrDefault(SqliteDataReader reader, string columnName)
        {
            try
            {
                var ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
            }
            catch
            {
                return ""; // Column doesn't exist
            }
        }

        // Helper methods for extracting data from DICOM datasets
        private string ExtractPatientName(DicomDataset dataset)
        {
            try
            {
                var characterSet = dataset.GetSingleValueOrDefault(DicomTag.SpecificCharacterSet, "");
                var patientNameElement = dataset.GetDicomItem<DicomElement>(DicomTag.PatientName);
                if (patientNameElement == null) return "ไม่มีข้อมูลชื่อผู้ป่วย";

                byte[] rawBytes = patientNameElement.Buffer.Data;
                string rawString = Encoding.UTF8.GetString(rawBytes);

                if (rawString.Contains("="))
                {
                    var parts = rawString.Split('=');
                    var local = parts.Length > 1 ? parts[1] : parts[0];
                    return local.Trim();
                }

                Encoding encoding = GetEncodingFromCharacterSet(characterSet);
                return encoding.GetString(rawBytes);
            }
            catch
            {
                return "ไม่สามารถอ่านชื่อได้";
            }
        }

        private string ExtractModality(DicomDataset dataset)
        {
            try
            {
                var modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, "");
                if (!string.IsNullOrEmpty(modality)) return modality;

                var spsSequence = dataset.GetSequence(DicomTag.ScheduledProcedureStepSequence);
                if (spsSequence != null && spsSequence.Items.Count > 0)
                {
                    foreach (var spsItem in spsSequence.Items)
                    {
                        modality = spsItem.GetSingleValueOrDefault(DicomTag.Modality, "");
                        if (!string.IsNullOrEmpty(modality)) return modality;
                    }
                }
                return "ไม่ระบุ";
            }
            catch
            {
                return "ไม่ระบุ";
            }
        }

        private (string scheduledDate, string scheduledTime, string stationAET, string procedureDescription) ExtractScheduledProcedureStepInfo(DicomDataset dataset)
        {
            try
            {
                var spsSequence = dataset.GetSequence(DicomTag.ScheduledProcedureStepSequence);
                if (spsSequence != null && spsSequence.Items.Count > 0)
                {
                    var spsItem = spsSequence.Items[0];
                    var scheduledDate = spsItem.GetSingleValueOrDefault(DicomTag.ScheduledProcedureStepStartDate, "ไม่มีวันที่");
                    var scheduledTime = spsItem.GetSingleValueOrDefault(DicomTag.ScheduledProcedureStepStartTime, "ไม่มีเวลา");
                    var stationAET = spsItem.GetSingleValueOrDefault(DicomTag.ScheduledStationAETitle, "ไม่ระบุ");
                    var procedureDescription = spsItem.GetSingleValueOrDefault(DicomTag.ScheduledProcedureStepDescription, "ไม่มีรายละเอียด");
                    return (scheduledDate, scheduledTime, stationAET, procedureDescription);
                }
                return ("ไม่มีวันที่", "ไม่มีเวลา", "ไม่ระบุ", "ไม่มีรายละเอียด");
            }
            catch
            {
                return ("ไม่มีวันที่", "ไม่มีเวลา", "ไม่ระบุ", "ไม่มีรายละเอียด");
            }
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
    }
}