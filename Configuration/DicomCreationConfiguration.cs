namespace WorklistServiceApp.Configuration
{
    public class DicomCreationConfiguration
    {
        public const string SectionName = "DicomCreation";

        /// <summary>
        /// Maximum number of concurrent DICOM creation tasks
        /// </summary>
        public int MaxConcurrentCreation { get; set; } = 3;

        /// <summary>
        /// Output folder for created DICOM files
        /// </summary>
        public string DicomOutputFolderPath { get; set; } = @"C:\EKG_Output\DICOM";

        /// <summary>
        /// DICOM Transfer Syntax UID
        /// </summary>
        public string TransferSyntaxUID { get; set; } = "1.2.840.10008.1.2"; // Implicit VR Little Endian

        /// <summary>
        /// SOP Class UID for Secondary Capture Image Storage
        /// </summary>
        public string SOPClassUID { get; set; } = "1.2.840.10008.5.1.4.1.1.7"; // Secondary Capture Image Storage

        /// <summary>
        /// Implementation Class UID (your organization's UID)
        /// </summary>
        public string ImplementationClassUID { get; set; } = "1.2.840.10008.1.2.1.99";

        /// <summary>
        /// Implementation Version Name
        /// </summary>
        public string ImplementationVersionName { get; set; } = "EKG_WORKLIST_v1.0";

        /// <summary>
        /// Institution Name
        /// </summary>
        public string InstitutionName { get; set; } = "โรงพยาบาล";

        /// <summary>
        /// Manufacturer
        /// </summary>
        public string Manufacturer { get; set; } = "EKG Workflow System";

        /// <summary>
        /// Manufacturer Model Name
        /// </summary>
        public string ManufacturerModelName { get; set; } = "EKG Processor v1.0";

        /// <summary>
        /// Software Version
        /// </summary>
        public string SoftwareVersion { get; set; } = "1.0.0";

        /// <summary>
        /// Device Serial Number
        /// </summary>
        public string DeviceSerialNumber { get; set; } = "EKG001";

        /// <summary>
        /// Photometric Interpretation: MONOCHROME1, MONOCHROME2, RGB
        /// </summary>
        public string PhotometricInterpretation { get; set; } = "RGB";

        /// <summary>
        /// Bits Allocated (8, 16)
        /// </summary>
        public ushort BitsAllocated { get; set; } = 8;

        /// <summary>
        /// Bits Stored (8, 16)
        /// </summary>
        public ushort BitsStored { get; set; } = 8;

        /// <summary>
        /// High Bit (7, 15)
        /// </summary>
        public ushort HighBit { get; set; } = 7;

        /// <summary>
        /// Pixel Representation (0 = unsigned, 1 = signed)
        /// </summary>
        public ushort PixelRepresentation { get; set; } = 0;

        /// <summary>
        /// Planar Configuration (0 = color-by-pixel, 1 = color-by-plane)
        /// </summary>
        public ushort PlanarConfiguration { get; set; } = 0;

        /// <summary>
        /// Enable DICOM compression
        /// </summary>
        public bool EnableCompression { get; set; } = false;

        /// <summary>
        /// Compression type: None, JPEG, JPEG2000, RLE
        /// </summary>
        public string CompressionType { get; set; } = "None";

        /// <summary>
        /// JPEG compression quality (1-100)
        /// </summary>
        public int CompressionQuality { get; set; } = 90;

        /// <summary>
        /// Patient ID prefix for generated IDs
        /// </summary>
        public string PatientIDPrefix { get; set; } = "EKG";

        /// <summary>
        /// Study ID prefix for generated IDs
        /// </summary>
        public string StudyIDPrefix { get; set; } = "ST";

        /// <summary>
        /// Series ID prefix for generated IDs
        /// </summary>
        public string SeriesIDPrefix { get; set; } = "SE";

        /// <summary>
        /// Series Description
        /// </summary>
        public string SeriesDescription { get; set; } = "EKG Report Images";

        /// <summary>
        /// Study Description
        /// </summary>
        public string StudyDescription { get; set; } = "EKG (Electro Cardiography)";

        /// <summary>
        /// Referring Physician Name
        /// </summary>
        public string ReferringPhysicianName { get; set; } = "EKG^System";

        /// <summary>
        /// Performing Physician Name
        /// </summary>
        public string PerformingPhysicianName { get; set; } = "EKG^Technician";

        /// <summary>
        /// Body Part Examined
        /// </summary>
        public string BodyPartExamined { get; set; } = "HEART";

        /// <summary>
        /// Enable multi-frame DICOM for multiple images
        /// </summary>
        public bool EnableMultiFrame { get; set; } = false;

        /// <summary>
        /// Create separate DICOM file for each image
        /// </summary>
        public bool CreateSeparateFiles { get; set; } = true;

        /// <summary>
        /// Processing timeout per DICOM creation in minutes
        /// </summary>
        public int CreationTimeoutMinutes { get; set; } = 5;

        /// <summary>
        /// Enable DICOM validation before saving
        /// </summary>
        public bool EnableValidation { get; set; } = true;

        /// <summary>
        /// Archive created DICOM files
        /// </summary>
        public bool ArchiveCreatedFiles { get; set; } = true;

        /// <summary>
        /// Archive folder path
        /// </summary>
        public string ArchiveFolderPath { get; set; } = @"C:\EKG_Archive\DICOM_Created";

        /// <summary>
        /// Enable retry on creation failure
        /// </summary>
        public bool EnableRetryOnFailure { get; set; } = true;

        /// <summary>
        /// Maximum retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Retry delay in seconds
        /// </summary>
        public int RetryDelaySeconds { get; set; } = 10;

        /// <summary>
        /// Queue processing interval in seconds
        /// </summary>
        public int QueueProcessingIntervalSeconds { get; set; } = 3;

        /// <summary>
        /// Maximum queue size
        /// </summary>
        public int MaxQueueSize { get; set; } = 50;

        /// <summary>
        /// Character set for DICOM tags
        /// </summary>
        public string CharacterSet { get; set; } = "ISO_IR 166"; // Thai

        /// <summary>
        /// Timezone for DICOM timestamps
        /// </summary>
        public string TimeZone { get; set; } = "+0700"; // Thailand timezone
    }
}