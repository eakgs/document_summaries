// Create this file: Services/DocumentNotificationService.cs
using Microsoft.AspNetCore.SignalR;
using DocHub.Api.Hubs;
using DocHub.Api.Models;

namespace DocHub.Api.Services
{
    public interface IDocumentNotificationService
    {
        Task NotifySummaryReady(DocumentUpdateDto document);
        Task NotifyDocumentUploaded(DocumentUpdateDto document);
        Task NotifyDocumentDeleted(DocumentUpdateDto document);
    }

    public class DocumentNotificationService : IDocumentNotificationService
    {
        private readonly IHubContext<DocumentHub> _hubContext;
        private readonly ILogger<DocumentNotificationService> _logger;

        public DocumentNotificationService(
            IHubContext<DocumentHub> hubContext,
            ILogger<DocumentNotificationService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task NotifySummaryReady(DocumentUpdateDto document)
        {
            try
            {
                await _hubContext.Clients.Group("DocumentUpdates")
                    .SendAsync("SummaryReady", document);
                
                _logger.LogInformation("📡 SignalR: Notified clients about summary ready for document {DocumentId} ({FileName})", 
                    document.Id, document.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ SignalR: Failed to notify clients about summary ready for document {DocumentId}", 
                    document.Id);
            }
        }

        public async Task NotifyDocumentUploaded(DocumentUpdateDto document)
        {
            try
            {
                await _hubContext.Clients.Group("DocumentUpdates")
                    .SendAsync("DocumentUploaded", document);
                
                _logger.LogInformation("📡 SignalR: Notified clients about new document upload {DocumentId} ({FileName})", 
                    document.Id, document.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ SignalR: Failed to notify clients about document upload {DocumentId}", 
                    document.Id);
            }
        }

        public async Task NotifyDocumentDeleted(DocumentUpdateDto document)
        {
            try
            {
                await _hubContext.Clients.Group("DocumentUpdates")
                    .SendAsync("DocumentDeleted", document);
                
                _logger.LogInformation("📡 SignalR: Notified clients about document deletion {DocumentId} ({FileName})", 
                    document.Id, document.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ SignalR: Failed to notify clients about document deletion {DocumentId}", 
                    document.Id);
            }
        }
    }
}