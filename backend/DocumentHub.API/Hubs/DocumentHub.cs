// Create this file: Hubs/DocumentHub.cs
using Microsoft.AspNetCore.SignalR;

namespace DocHub.Api.Hubs
{
    public class DocumentHub : Hub
    {
        private readonly ILogger<DocumentHub> _logger;

        public DocumentHub(ILogger<DocumentHub> logger)
        {
            _logger = logger;
        }

        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation($"Client {Context.ConnectionId} joined group {groupName}");
        }

        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation($"Client {Context.ConnectionId} left group {groupName}");
        }

        public override async Task OnConnectedAsync()
        {
            // Add user to a default group for document updates
            await Groups.AddToGroupAsync(Context.ConnectionId, "DocumentUpdates");
            _logger.LogInformation($"Client {Context.ConnectionId} connected and added to DocumentUpdates group");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "DocumentUpdates");
            _logger.LogInformation($"Client {Context.ConnectionId} disconnected");
            await base.OnDisconnectedAsync(exception);
        }
    }
}