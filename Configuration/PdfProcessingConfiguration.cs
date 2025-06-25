namespace WorklistServiceApp.Configuration
{
    public class PdfProcessingConfiguration
    {
        public const string SectionName = "PdfProcessing";

        /// <summary>
        /// Maximum number of concurrent PDF processing tasks
        /// </summary>
        public int MaxConcurrentProcessing { get; set; } = 2;

        /// <summary>
        /// Temporary folder for JPEG output
        /// </summary>
        public string TempJpegFolderPath { get; set; } = @"C:\EKG_Temp\JPEG";
        /// <summary>
        /// Minimum file size in bytes (to avoid processing incomplete files)
        /// </summary>
        public long MinimumFileSizeBytes { get; set; } = 1024; // 1KB

        /// <summary>
        /// Maximum file size in bytes (to avoid processing huge files)
        /// </summary>
        public long MaximumFileSizeBytes { get; set; } = 52428800; // 50MB
        /// <summary>
        /// JPEG image quality (1-100, higher = better quality)
        /// </summary>
        public int JpegQuality { get; set; } = 95;

        /// <summary>
        /// DPI (Dots Per Inch) for PDF to image conversion
        /// </summary>
        public int ConversionDPI { get; set; } = 300;

        /// <summary>
        /// Maximum image width in pixels (0 = no limit)
        /// </summary>
        public int MaxImageWidth { get; set; } = 2480; // A4 at 300 DPI

        /// <summary>
        /// Maximum image height in pixels (0 = no limit)
        /// </summary>
        public int MaxImageHeight { get; set; } = 3508; // A4 at 300 DPI

        /// <summary>
        /// Convert all pages or first page only
        /// </summary>
        public bool ConvertAllPages { get; set; } = true;

        /// <summary>
        /// Image format: JPEG, PNG, TIFF
        /// </summary>
        public string OutputImageFormat { get; set; } = "JPEG";

        /// <summary>
        /// Enable image compression
        /// </summary>
        public bool EnableCompression { get; set; } = true;

        /// <summary>
        /// Processing timeout per file in minutes
        /// </summary>
        public int ProcessingTimeoutMinutes { get; set; } = 10;

        /// <summary>
        /// Cleanup temporary files after DICOM creation
        /// </summary>
        public bool CleanupTempFiles { get; set; } = true;

        /// <summary>
        /// Cleanup delay in minutes (allow time for DICOM creation)
        /// </summary>
        public int CleanupDelayMinutes { get; set; } = 30;

        /// <summary>
        /// Maximum age of temp files before forced cleanup (hours)
        /// </summary>
        public int MaxTempFileAgeHours { get; set; } = 24;

        /// <summary>
        /// Enable automatic temp folder cleanup on startup
        /// </summary>
        public bool CleanupOnStartup { get; set; } = true;

        /// <summary>
        /// Image processing library: PDFiumSharp, ImageSharp, MagickNET
        /// </summary>
        public string ProcessingLibrary { get; set; } = "PDFiumSharp";

        /// <summary>
        /// Enable OCR for text extraction (if supported)
        /// </summary>
        public bool EnableOCR { get; set; } = false;

        /// <summary>
        /// OCR language: eng, tha, eng+tha
        /// </summary>
        public string OCRLanguage { get; set; } = "eng+tha";

        /// <summary>
        /// Enable image enhancement (noise reduction, contrast adjustment)
        /// </summary>
        public bool EnableImageEnhancement { get; set; } = false;

        /// <summary>
        /// Contrast adjustment factor (1.0 = no change)
        /// </summary>
        public float ContrastFactor { get; set; } = 1.1f;

        /// <summary>
        /// Brightness adjustment (-100 to 100)
        /// </summary>
        public int BrightnessAdjustment { get; set; } = 0;

        /// <summary>
        /// Enable retry on processing failure
        /// </summary>
        public bool EnableRetryOnFailure { get; set; } = true;

        /// <summary>
        /// Maximum retry attempts per file
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Retry delay in seconds
        /// </summary>
        public int RetryDelaySeconds { get; set; } = 5;

        /// <summary>
        /// Queue processing interval in seconds
        /// </summary>
        public int QueueProcessingIntervalSeconds { get; set; } = 2;

        /// <summary>
        /// Maximum queue size before rejecting new files
        /// </summary>
        public int MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// Enable processing statistics
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Statistics reporting interval in minutes
        /// </summary>
        public int StatisticsReportingIntervalMinutes { get; set; } = 15;
    }
}