// Program.cs
using WorklistServiceApp;
using WorklistServiceApp.Configuration;
using WorklistServiceApp.Data;
using WorklistServiceApp.Services;
using idspacsgateway.Hubs;
using idspacsgateway.Services;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ===== SERILOG CONFIGURATION =====
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "Logs/ekg-gateway-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "Logs/ekg-errors-.log",
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 90,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Set encoding for Thai character support
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// Add services
builder.Services.AddRazorPages();
builder.Services.AddSignalR(); // เพิ่ม SignalR
builder.Services.AddControllers(); // เพิ่ม Controllers สำหรับ API

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

// ✅ Core services (existing)
builder.Services.AddSingleton<WorklistSyncService>();
builder.Services.AddSingleton<PdfMonitoringService>();
builder.Services.AddSingleton<PdfProcessingService>();
builder.Services.AddSingleton<DicomCreationService>();
builder.Services.AddSingleton<DicomSendService>(); // เพิ่มบรรทัดนี้

// ✅ Dashboard services (new)
builder.Services.AddSingleton<LoggingService>();

// ✅ Register as hosted services
builder.Services.AddHostedService<WorklistSyncService>(provider =>
    provider.GetRequiredService<WorklistSyncService>());
builder.Services.AddHostedService<PdfMonitoringService>(provider =>
    provider.GetRequiredService<PdfMonitoringService>());
builder.Services.AddHostedService<PdfProcessingService>(provider =>
    provider.GetRequiredService<PdfProcessingService>());
builder.Services.AddHostedService<DicomCreationService>(provider =>
    provider.GetRequiredService<DicomCreationService>());
builder.Services.AddHostedService<DicomSendService>(provider =>
    provider.GetRequiredService<DicomSendService>());



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

// ✅ Map services
app.MapRazorPages();
app.MapControllers(); // เพิ่ม API Controllers
app.MapHub<LogHub>("/logHub"); // เพิ่ม SignalR Hub

// เพิ่ม File & Log Management APIs ตามที่เขียนไว้ด้านบน
// (ใส่โค้ด API endpoints ทั้งหมดที่เขียนไว้ในขั้นตอนที่ 6)

// Default route to dashboard
app.MapGet("/", () => Results.Redirect("/dashboard"));

// Health check endpoint
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

// Cleanup when app stops
app.Lifetime.ApplicationStopping.Register(Log.CloseAndFlush);

Console.WriteLine("🚀 EKG Worklist Service with Hybrid Dashboard starting...");
Console.WriteLine($"🌐 Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"📱 URLs: {string.Join(", ", app.Urls)}");
Console.WriteLine($"🖥️ Dashboard: https://localhost:7104/dashboard");

if (app.Environment.IsDevelopment())
{
    Console.WriteLine("🔧 Development Mode Features:");
    Console.WriteLine("   - File logging enabled");
    Console.WriteLine("   - Real-time logs via SignalR");
    Console.WriteLine("   - Dashboard polling every 20 seconds");
    Console.WriteLine("📊 Available endpoints:");
    Console.WriteLine("   - GET  /dashboard");
    Console.WriteLine("   - GET  /health");
    Console.WriteLine("   - GET  /api/dashboard/status");
    Console.WriteLine("   - GET  /api/dashboard/worklist");
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

        // Test database connection
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

static async Task CreateRequiredFolders(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger)
{
    var pdfConfig = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PdfMonitoringConfiguration>>();
    var processingConfig = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PdfProcessingConfiguration>>();
    var creationConfig = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DicomCreationConfiguration>>();

    var foldersToCreate = new[]
    {
        "Logs", // เพิ่ม Logs folder
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

static void WireUpServiceEvents(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger)
{
    try
    {
        logger.LogInformation("🔗 Wiring up service events...");

        // Get services
        var pdfMonitoringService = services.GetRequiredService<PdfMonitoringService>();
        var pdfProcessingService = services.GetRequiredService<PdfProcessingService>();
        var dicomCreationService = services.GetRequiredService<DicomCreationService>();
        var loggingService = services.GetRequiredService<LoggingService>(); // เพิ่มบรรทัดนี้

        // 📄 PDF File Detected → 🖼️ Queue for Processing + Log
        pdfMonitoringService.PdfFileDetected += async (sender, e) =>
        {
            await loggingService.SendLogToClients("info",
                $"📄 PDF detected: {e.ExtractedHN} → {Path.GetFileName(e.PdfFilePath)}", "PdfMonitoring");

            if (e.WorklistItem != null)
            {
                pdfProcessingService.QueuePdfForProcessing(e.WorklistItem, e.PdfFilePath);
            }
        };

   

        // 🖼️ PDF Processing Completed → 🏥 Queue for DICOM Creation + Log
        pdfProcessingService.PdfProcessingCompleted += async (sender, e) =>
        {
            if (e.Success && !string.IsNullOrEmpty(e.JpegFilePath))
            {
                await loggingService.SendLogToClients("info",
                    $"🖼️ PDF processed: {e.WorklistItem.PatientID} → JPEG created", "PdfProcessing");
                dicomCreationService.QueueJpegForDicomCreation(e.WorklistItem, e.JpegFilePath);


            }
            else
            {
                await loggingService.SendLogToClients("error",
                    $"❌ PDF processing failed: {e.WorklistItem.PatientID} - {e.ErrorMessage}", "PdfProcessing");
            }
        };

        var dicomSendService = services.GetRequiredService<DicomSendService>(); 
        // 🏥 DICOM Creation Completed → Log
        dicomCreationService.DicomCreationCompleted += async (sender, e) =>
        {
            if (e.Success && !string.IsNullOrEmpty(e.DicomFilePath))
            {
                await loggingService.SendLogToClients("info",
                    $"🏥 DICOM created: {e.WorklistItem.PatientID} → {Path.GetFileName(e.DicomFilePath)}", "DicomCreation");

                dicomSendService.QueueDicomForSend(e.WorklistItem, e.DicomFilePath);
            }
            else
            {
                await loggingService.SendLogToClients("error",
                    $"❌ DICOM creation failed: {e.WorklistItem.PatientID} - {e.ErrorMessage}", "DicomCreation");
            }
        };

        // 📤 DICOM Send Completed → Log
        dicomSendService.DicomSendCompleted += async (sender, e) =>
        {
            if (e.Success)
            {
                await loggingService.SendLogToClients("info",
                    $"📤 DICOM sent to PACS: {e.WorklistItem.PatientID}", "DicomSend");
            }
            else
            {
                await loggingService.SendLogToClients("error",
                    $"❌ DICOM send failed: {e.WorklistItem.PatientID} - {e.ErrorMessage}", "DicomSend");
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