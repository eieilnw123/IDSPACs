// Hubs/LogHub.cs
using Microsoft.AspNetCore.SignalR;
using idspacsgateway.Models;

namespace idspacsgateway.Hubs
{
    public class LogHub : Hub
    {
        private readonly ILogger<LogHub> _logger;

        public LogHub(ILogger<LogHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Log viewer connected: {ConnectionId}", Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, "LogViewers");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Log viewer disconnected: {ConnectionId}", Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "LogViewers");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinLogGroup()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "LogViewers");
            _logger.LogDebug("Client joined log group: {ConnectionId}", Context.ConnectionId);
        }
    }
}