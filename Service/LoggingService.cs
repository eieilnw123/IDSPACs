// Services/LoggingService.cs
using Microsoft.AspNetCore.SignalR;
using idspacsgateway.Hubs;
using idspacsgateway.Models;

namespace idspacsgateway.Services
{
    public class LoggingService
    {
        private readonly IHubContext<LogHub> _hubContext;
        private readonly ILogger<LoggingService> _logger;

        public LoggingService(IHubContext<LogHub> hubContext, ILogger<LoggingService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task SendLogToClients(string level, string message, string source = "System")
        {
            try
            {
                var logEntry = new LogEntry
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                    Level = level.ToUpper(),
                    Message = message,
                    Source = source
                };

                // Send to SignalR clients
                await _hubContext.Clients.Group("LogViewers").SendAsync("NewLogEntry", logEntry);

                // Also log to console/file
                switch (level.ToLower())
                {
                    case "error":
                        _logger.LogError("[{Source}] {Message}", source, message);
                        break;
                    case "warning":
                    case "warn":
                        _logger.LogWarning("[{Source}] {Message}", source, message);
                        break;
                    case "info":
                        _logger.LogInformation("[{Source}] {Message}", source, message);
                        break;
                    case "debug":
                        _logger.LogDebug("[{Source}] {Message}", source, message);
                        break;
                    default:
                        _logger.LogInformation("[{Source}] {Message}", source, message);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send log to clients");
            }
        }

        public async Task SendSystemAlert(string message, string level = "warning")
        {
            await SendLogToClients(level, $"🚨 ALERT: {message}", "System");
        }

        public async Task SendFileProcessingLog(string fileName, string action, bool success, string? error = null)
        {
            var icon = action switch
            {
                "detected" => "📄",
                "processing" => "🔄",
                "converted" => "🖼️",
                "created" => "🏥",
                _ => "📋"
            };

            var message = success
                ? $"{icon} {action}: {fileName}"
                : $"❌ {action} failed: {fileName} - {error}";

            await SendLogToClients(success ? "info" : "error", message, "FileProcessing");
        }
    }
}