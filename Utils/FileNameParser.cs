using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace WorklistServiceApp.Utils
{
    public class FileNameParser
    {
        private readonly ILogger<FileNameParser> _logger;

        public FileNameParser(ILogger<FileNameParser> logger)
        {
            _logger = logger;
        }

        public ParsedFileInfo ParsePdfFileName(string fileName)
        {
            var result = new ParsedFileInfo();

            try
            {
                // Remove extension
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

                _logger.LogDebug("🔍 Parsing filename: {FileName}", nameWithoutExt);

                // Try different patterns in order of specificity
                var patterns = new[]
                {
                    new PatternInfo
                    {
                        Name = "YYYYMMDD_HHMMSS_PatientID",
                        Pattern = @"^(\d{8})_(\d{6})_(.+)$",
                        DateGroup = 1,
                        TimeGroup = 2,
                        PatientIdGroup = 3,
                        Description = "Date_Time_PatientID format"
                    },
                    new PatternInfo
                    {
                        Name = "PatientID_YYYYMMDD_HHMMSS",
                        Pattern = @"^(.+)_(\d{8})_(\d{6})$",
                        DateGroup = 2,
                        TimeGroup = 3,
                        PatientIdGroup = 1,
                        Description = "PatientID_Date_Time format"
                    },
                    new PatternInfo
                    {
                        Name = "YYYYMMDD_PatientID",
                        Pattern = @"^(\d{8})_(.+)$",
                        DateGroup = 1,
                        TimeGroup = -1, // No time
                        PatientIdGroup = 2,
                        Description = "Date_PatientID format"
                    },
                    new PatternInfo
                    {
                        Name = "PatientID_YYYYMMDD",
                        Pattern = @"^(.+)_(\d{8})$",
                        DateGroup = 2,
                        TimeGroup = -1, // No time
                        PatientIdGroup = 1,
                        Description = "PatientID_Date format"
                    },
                    new PatternInfo
                    {
                        Name = "Complex_PatientID",
                        Pattern = @"^(\d{2,4}-\d{3,6})$",
                        DateGroup = -1, // No date
                        TimeGroup = -1, // No time
                        PatientIdGroup = 1,
                        Description = "Complex PatientID (XX-XXXXX)"
                    },
                    new PatternInfo
                    {
                        Name = "Simple_PatientID",
                        Pattern = @"^(\d{6,10})$",
                        DateGroup = -1, // No date
                        TimeGroup = -1, // No time
                        PatientIdGroup = 1,
                        Description = "Simple PatientID (6-10 digits)"
                    },
                    new PatternInfo
                    {
                        Name = "Alphanumeric_PatientID",
                        Pattern = @"^([A-Za-z0-9\-]{3,15})$",
                        DateGroup = -1, // No date
                        TimeGroup = -1, // No time
                        PatientIdGroup = 1,
                        Description = "Alphanumeric PatientID"
                    }
                };

                foreach (var patternInfo in patterns)
                {
                    var match = Regex.Match(nameWithoutExt, patternInfo.Pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        result = ProcessMatch(match, patternInfo);
                        if (result.Success)
                        {
                            _logger.LogDebug("✅ Pattern '{PatternName}' matched: {Description} → {Result}",
                                patternInfo.Name, patternInfo.Description, result.ToString());
                            return result;
                        }
                    }
                }

                _logger.LogWarning("❌ No pattern matched for filename: {FileName}", nameWithoutExt);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error parsing filename: {FileName}", fileName);
                return result;
            }
        }

        private ParsedFileInfo ProcessMatch(Match match, PatternInfo patternInfo)
        {
            var result = new ParsedFileInfo();

            try
            {
                // Extract PatientID
                if (patternInfo.PatientIdGroup > 0 && match.Groups.Count > patternInfo.PatientIdGroup)
                {
                    result.PatientID = match.Groups[patternInfo.PatientIdGroup].Value.Trim();
                }

                // Extract Date
                if (patternInfo.DateGroup > 0 && match.Groups.Count > patternInfo.DateGroup)
                {
                    result.SeriesDate = match.Groups[patternInfo.DateGroup].Value;

                    // Validate date format (YYYYMMDD)
                    if (!IsValidDateFormat(result.SeriesDate))
                    {
                        _logger.LogWarning("⚠️ Invalid date format: {Date}", result.SeriesDate);
                        result.SeriesDate = DateTime.Now.ToString("yyyyMMdd");
                    }
                }
                else
                {
                    // Use current date if not found in filename
                    result.SeriesDate = DateTime.Now.ToString("yyyyMMdd");
                }

                // Extract Time
                if (patternInfo.TimeGroup > 0 && match.Groups.Count > patternInfo.TimeGroup)
                {
                    result.RawTime = match.Groups[patternInfo.TimeGroup].Value;
                    result.SeriesTime = FormatTime(result.RawTime);

                    // Validate time format
                    if (!IsValidTimeFormat(result.RawTime))
                    {
                        _logger.LogWarning("⚠️ Invalid time format: {Time}", result.RawTime);
                        result.SeriesTime = DateTime.Now.ToString("HH:mm:ss");
                    }
                }
                else
                {
                    // Use current time if not found in filename
                    result.SeriesTime = DateTime.Now.ToString("HH:mm:ss");
                    result.RawTime = DateTime.Now.ToString("HHmmss");
                }

                // Set default series description
                result.SeriesDescription = DetermineSeriesDescription(result.PatientID);

                // Validate PatientID
                if (string.IsNullOrWhiteSpace(result.PatientID))
                {
                    _logger.LogWarning("⚠️ Empty PatientID extracted");
                    return result; // Success = false
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing match");
                return result; // Success = false
            }
        }

        private string FormatTime(string rawTime)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawTime))
                    return DateTime.Now.ToString("HH:mm:ss");

                // Remove any non-digit characters
                var digitsOnly = new string(rawTime.Where(char.IsDigit).ToArray());

                return digitsOnly.Length switch
                {
                    6 => // HHMMSS
                        $"{digitsOnly.Substring(0, 2)}:{digitsOnly.Substring(2, 2)}:{digitsOnly.Substring(4, 2)}",
                    4 => // HHMM
                        $"{digitsOnly.Substring(0, 2)}:{digitsOnly.Substring(2, 2)}:00",
                    2 => // HH
                        $"{digitsOnly}:00:00",
                    _ => DateTime.Now.ToString("HH:mm:ss") // Fallback to current time
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error formatting time: {RawTime}", rawTime);
                return DateTime.Now.ToString("HH:mm:ss");
            }
        }

        private bool IsValidDateFormat(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr) || dateStr.Length != 8)
                return false;

            try
            {
                var year = int.Parse(dateStr.Substring(0, 4));
                var month = int.Parse(dateStr.Substring(4, 2));
                var day = int.Parse(dateStr.Substring(6, 2));

                // Basic validation
                if (year < 1900 || year > 2100) return false;
                if (month < 1 || month > 12) return false;
                if (day < 1 || day > 31) return false;

                // Try to create actual date to validate
                var date = new DateTime(year, month, day);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsValidTimeFormat(string timeStr)
        {
            if (string.IsNullOrWhiteSpace(timeStr))
                return false;

            try
            {
                var digitsOnly = new string(timeStr.Where(char.IsDigit).ToArray());

                if (digitsOnly.Length == 6) // HHMMSS
                {
                    var hour = int.Parse(digitsOnly.Substring(0, 2));
                    var minute = int.Parse(digitsOnly.Substring(2, 2));
                    var second = int.Parse(digitsOnly.Substring(4, 2));

                    return hour >= 0 && hour <= 23 &&
                           minute >= 0 && minute <= 59 &&
                           second >= 0 && second <= 59;
                }
                else if (digitsOnly.Length == 4) // HHMM
                {
                    var hour = int.Parse(digitsOnly.Substring(0, 2));
                    var minute = int.Parse(digitsOnly.Substring(2, 2));

                    return hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string DetermineSeriesDescription(string patientId)
        {
            // Could be customized based on PatientID patterns or other logic
            return "12-Lead ECG Report";
        }

        public string FormatDateForDisplay(string yyyymmdd)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(yyyymmdd) || yyyymmdd.Length != 8)
                    return yyyymmdd;

                var year = yyyymmdd.Substring(0, 4);
                var month = yyyymmdd.Substring(4, 2);
                var day = yyyymmdd.Substring(6, 2);
                return $"{day}/{month}/{year}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error formatting date for display: {Date}", yyyymmdd);
                return yyyymmdd;
            }
        }

        public string FormatTimeForDisplay(string hhmmss)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hhmmss))
                    return "";

                // If already formatted (contains :)
                if (hhmmss.Contains(':'))
                    return hhmmss;

                // If raw format (HHMMSS)
                var digitsOnly = new string(hhmmss.Where(char.IsDigit).ToArray());
                return FormatTime(digitsOnly);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error formatting time for display: {Time}", hhmmss);
                return hhmmss;
            }
        }

        public bool IsValidPatientId(string patientId)
        {
            if (string.IsNullOrWhiteSpace(patientId))
                return false;

            // Define valid PatientID patterns
            var validPatterns = new[]
            {
                @"^\d{2,4}-\d{3,6}$",      // XX-XXXXX format
                @"^\d{6,10}$",             // 6-10 digits
                @"^[A-Za-z0-9\-]{3,15}$"   // Alphanumeric with dashes
            };

            return validPatterns.Any(pattern => Regex.IsMatch(patientId, pattern));
        }

        public List<string> GetSupportedPatterns()
        {
            return new List<string>
            {
                "YYYYMMDD_HHMMSS_PatientID.pdf (e.g., 20250625_095133_12-45325.pdf)",
                "PatientID_YYYYMMDD_HHMMSS.pdf (e.g., 12-45325_20250625_095133.pdf)",
                "YYYYMMDD_PatientID.pdf (e.g., 20250625_12-45325.pdf)",
                "PatientID_YYYYMMDD.pdf (e.g., 12-45325_20250625.pdf)",
                "PatientID.pdf (e.g., 12-45325.pdf, 123456789.pdf)"
            };
        }

        public ParsedFileInfo ParseWithCustomPattern(string fileName, string customPattern)
        {
            var result = new ParsedFileInfo();

            try
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var match = Regex.Match(nameWithoutExt, customPattern, RegexOptions.IgnoreCase);

                if (match.Success && match.Groups.Count > 1)
                {
                    result.PatientID = match.Groups[1].Value;
                    result.SeriesDate = DateTime.Now.ToString("yyyyMMdd");
                    result.SeriesTime = DateTime.Now.ToString("HH:mm:ss");
                    result.SeriesDescription = "12-Lead ECG Report";
                    result.Success = true;

                    _logger.LogDebug("✅ Custom pattern matched: {Pattern} → {PatientID}",
                        customPattern, result.PatientID);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error parsing with custom pattern: {Pattern}", customPattern);
                return result;
            }
        }
    }

    // Supporting classes
    internal class PatternInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty;
        public int DateGroup { get; set; } = -1;
        public int TimeGroup { get; set; } = -1;
        public int PatientIdGroup { get; set; } = -1;
        public string Description { get; set; } = string.Empty;
    }

    public class ParsedFileInfo
    {
        public bool Success { get; set; } = false;
        public string PatientID { get; set; } = string.Empty;
        public string SeriesDate { get; set; } = string.Empty;  // YYYYMMDD format
        public string SeriesTime { get; set; } = string.Empty;  // HH:MM:SS format
        public string RawTime { get; set; } = string.Empty;     // Original time from filename
        public string SeriesDescription { get; set; } = "12-Lead ECG Report";
        public DateTime ParsedDateTime { get; set; } = DateTime.Now;

        public string FormattedDate
        {
            get
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(SeriesDate) || SeriesDate.Length != 8)
                        return "";

                    var year = SeriesDate.Substring(0, 4);
                    var month = SeriesDate.Substring(4, 2);
                    var day = SeriesDate.Substring(6, 2);
                    return $"{day}/{month}/{year}";
                }
                catch
                {
                    return SeriesDate;
                }
            }
        }

        public string FormattedDateTime => $"{FormattedDate} {SeriesTime}".Trim();

        public bool HasValidDate => !string.IsNullOrWhiteSpace(SeriesDate) && SeriesDate.Length == 8;
        public bool HasValidTime => !string.IsNullOrWhiteSpace(SeriesTime) && SeriesTime.Contains(':');
        public bool HasValidPatientID => !string.IsNullOrWhiteSpace(PatientID) && PatientID.Length >= 3;

        public override string ToString()
        {
            return $"PatientID: {PatientID}, Date: {SeriesDate}, Time: {SeriesTime}, Success: {Success}";
        }

        public string ToDetailedString()
        {
            return $"PatientID: {PatientID}\n" +
                   $"Series Date: {SeriesDate} ({FormattedDate})\n" +
                   $"Series Time: {SeriesTime}\n" +
                   $"Description: {SeriesDescription}\n" +
                   $"Success: {Success}";
        }
    }
}