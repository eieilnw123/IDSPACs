using WorklistServiceApp;
using WorklistServiceApp.Configuration;
using WorklistServiceApp.Data;
using WorklistServiceApp.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Set encoding for Thai character support
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// Add Razor Pages UI
builder.Services.AddRazorPages();

// ✅ Utility services
builder.Services.AddSingleton<WorklistServiceApp.Utils.FileNameParser>();


// ✅ Configuration bindings
builder.Services.Configure<DicomCreationConfiguration>(
    builder.Configuration.GetSection(DicomCreationConfiguration.SectionName));
builder.Services.Configure<DicomSendConfiguration>(
    builder.Configuration.GetSection(DicomSendConfiguration.SectionName));
builder.Services.Configure<DicomSyncConfiguration>(
    builder.Configuration.GetSection(DicomSyncConfiguration.SectionName));
builder.Services.Configure<PdfMonitoringConfiguration>(
    builder.Configuration.GetSection(PdfMonitoringConfiguration.SectionName));
builder.Services.Configure<PdfProcessingConfiguration>(
    builder.Configuration.GetSection(PdfProcessingConfiguration.SectionName));

// ✅ Database service
builder.Services.AddSingleton<DatabaseService>();

// ✅ Register services as singletons first (so they can be injected into each other)
builder.Services.AddSingleton<WorklistSyncService>();
builder.Services.AddSingleton<PdfMonitoringService>();
builder.Services.AddSingleton<PdfProcessingService>();
builder.Services.AddSingleton<DicomCreationService>();
// builder.Services.AddSingleton<DicomSendService>(); // Add when ready

// ✅ Register as hosted services (will use the same singleton instances)
builder.Services.AddHostedService<WorklistSyncService>(provider =>
    provider.GetRequiredService<WorklistSyncService>());
builder.Services.AddHostedService<PdfMonitoringService>(provider =>
    provider.GetRequiredService<PdfMonitoringService>());
builder.Services.AddHostedService<PdfProcessingService>(provider =>
    provider.GetRequiredService<PdfProcessingService>());
builder.Services.AddHostedService<DicomCreationService>(provider =>
    provider.GetRequiredService<DicomCreationService>());

// ✅ Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
});
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

// ✅ Wire up service events and test configurations
await InitializeApplication(app);

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

// Add a simple health check endpoint
app.MapGet("/health", async (DatabaseService dbService) =>
{
    try
    {
        var stats = await dbService.GetSyncStatistics();
        return Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.Now,
            totalItems = stats.TotalItems,
            lastSync = stats.LastSyncTime
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Database connection failed: {ex.Message}");
    }
});

// Add a simple status endpoint
app.MapGet("/status", (
    WorklistSyncService syncService,
    PdfMonitoringService pdfMonitoringService,
    PdfProcessingService pdfProcessingService,
    DicomCreationService dicomCreationService) =>
{
    return Results.Ok(new
    {
        timestamp = DateTime.Now,
        services = new
        {
            pdfMonitoring = pdfMonitoringService.GetMonitoringStatus(),
            pdfProcessing = pdfProcessingService.GetProcessingStatus(),
            dicomCreation = dicomCreationService.GetCreationStatus()
        }
    });
});

Console.WriteLine("🚀 EKG Worklist Service Web Application starting...");
Console.WriteLine($"🌐 Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"📱 URLs: {string.Join(", ", app.Urls)}");


// แสดงข้อมูลเพิ่มเติมใน Development
if (app.Environment.IsDevelopment())
{
    Console.WriteLine("🔧 Development Mode Features:");
    Console.WriteLine("   - Detailed error pages");
    Console.WriteLine("   - Debug endpoints available");
    Console.WriteLine("   - Hot reload enabled");
    Console.WriteLine("📊 Available endpoints:");
    Console.WriteLine("   - GET  /health");
    Console.WriteLine("   - GET  /status");
    Console.WriteLine("   - GET  /debug/services");
    Console.WriteLine("   - GET  /debug/pdf-folder");
    Console.WriteLine("   - POST /debug/add-test-patient/{id}");
}



app.Run();

// ✅ Initialize application and wire up services
static async Task InitializeApplication(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("🔧 Initializing application...");

        // Test database connection by trying to get sync statistics
        var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
        try
        {
            var stats = await dbService.GetSyncStatistics();
            logger.LogInformation("✅ Database connection: OK - Total items: {Count}", stats.TotalItems);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Database connection test failed");
            throw;
        }

        // Create required folders
        await CreateRequiredFolders(app.Services, logger);

        // Wire up service events
        WireUpServiceEvents(app.Services, logger);

        logger.LogInformation("✅ Application initialized successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Application initialization failed");
        throw;
    }
}

static async Task CreateRequiredFolders(IServiceProvider services, ILogger logger)
{
    var pdfConfig = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PdfMonitoringConfiguration>>();
    var processingConfig = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PdfProcessingConfiguration>>();
    var creationConfig = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DicomCreationConfiguration>>();

    var foldersToCreate = new[]
    {
        pdfConfig.Value.WatchFolderPath,
        pdfConfig.Value.ArchiveFolderPath,
        processingConfig.Value.TempJpegFolderPath,
        creationConfig.Value.DicomOutputFolderPath,
        creationConfig.Value.ArchiveFolderPath
    };

    foreach (var folder in foldersToCreate)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            logger.LogInformation("📁 Created folder: {Folder}", folder);
        }
    }
}

static void WireUpServiceEvents(IServiceProvider services, ILogger logger)
{
    try
    {
        logger.LogInformation("🔗 Wiring up service events...");

        // Get services (these are singletons)
        var pdfMonitoringService = services.GetRequiredService<PdfMonitoringService>();
        var pdfProcessingService = services.GetRequiredService<PdfProcessingService>();
        var dicomCreationService = services.GetRequiredService<DicomCreationService>();

        // 📄 PDF File Detected → 🖼️ Queue for Processing
        pdfMonitoringService.PdfFileDetected += (sender, e) =>
        {
            logger.LogInformation("🔗 Event: PDF detected for {PatientID} → Queueing for processing",
                e.WorklistItem?.PatientID ?? e.ExtractedHN);

            if (e.WorklistItem != null)
            {
                pdfProcessingService.QueuePdfForProcessing(e.WorklistItem, e.PdfFilePath);
            }
        };

        // 🖼️ PDF Processing Completed → 🏥 Queue for DICOM Creation
        pdfProcessingService.PdfProcessingCompleted += (sender, e) =>
        {
            if (e.Success && !string.IsNullOrEmpty(e.JpegFilePath))
            {
                logger.LogInformation("🔗 Event: PDF processing completed for {PatientID} → Queueing for DICOM creation",
                    e.WorklistItem.PatientID);
                dicomCreationService.QueueJpegForDicomCreation(e.WorklistItem, e.JpegFilePath);
            }
            else
            {
                logger.LogWarning("🔗 Event: PDF processing failed for {PatientID}: {Error}",
                    e.WorklistItem.PatientID, e.ErrorMessage);
            }
        };

        // 🏥 DICOM Creation Completed → 📤 Ready for Sending (เตรียมไว้สำหรับ DicomSendService)
        dicomCreationService.DicomCreationCompleted += (sender, e) =>
        {
            if (e.Success && !string.IsNullOrEmpty(e.DicomFilePath))
            {
                logger.LogInformation("🔗 Event: DICOM creation completed for {PatientID} → Ready for sending to PACS",
                    e.WorklistItem.PatientID);
                // TODO: เมื่อมี DicomSendService แล้ว ให้เรียก:
                // dicomSendService.QueueDicomForSending(e.WorklistItem, e.DicomFilePath);
            }
            else
            {
                logger.LogWarning("🔗 Event: DICOM creation failed for {PatientID}: {Error}",
                    e.WorklistItem.PatientID, e.ErrorMessage);
            }
        };

        logger.LogInformation("✅ Service events wired up successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Failed to wire up service events");
        throw;
    }
}