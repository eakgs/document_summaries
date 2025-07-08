using DocHub.Api.Data;
using DocHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DocHub.Api.Services;

public interface IDocumentProcessingService
{
    Task ProcessDocumentSummaryAsync(int documentId);
}

public class DocumentProcessingService : IDocumentProcessingService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(IServiceProvider serviceProvider, ILogger<DocumentProcessingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task ProcessDocumentSummaryAsync(int documentId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            var openAIService = scope.ServiceProvider.GetRequiredService<IOpenAIService>();
            var blobService = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();

            _logger.LogInformation($"🔄 Starting summary processing for document ID: {documentId}");

            // Get the document from database
            var document = await dbContext.Documents.FindAsync(documentId);
            if (document == null)
            {
                _logger.LogWarning($"❌ Document with ID {documentId} not found");
                return;
            }

            // Simulate downloading file content from blob storage
            // In a real implementation, you would download the actual file content
            var simulatedContent = $"This is a sample document content for {document.FileName}. " +
                                 $"The document was uploaded on {document.UploadedUtc} by {document.UploadedBy}. " +
                                 $"This document contains important information that needs to be summarized by AI.";

            // Generate AI summary
            var summary = await openAIService.GenerateDocumentSummaryAsync(document.FileName, simulatedContent);

            // Update document with summary
            document.Summary = summary;
            document.IsSummaryDone = true;

            await dbContext.SaveChangesAsync();

            _logger.LogInformation($"✅ Summary processing completed for document ID: {documentId}");
            _logger.LogInformation($"📝 Generated summary: {summary[..Math.Min(summary.Length, 100)]}...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Error processing summary for document ID: {documentId}");
        }
    }
}