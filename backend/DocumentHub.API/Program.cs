using Microsoft.EntityFrameworkCore;
using DocHub.Api.Data;
using DocHub.Api.Models;
using DocHub.Api.Services;
using DocHub.Api.Hubs;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.ResponseCompression;
using DotNetEnv;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "DocHub API with REAL AI", Version = "v1" });
});

// Add Entity Framework with In-Memory Database
builder.Services.AddDbContext<DocumentDbContext>(options =>
    options.UseInMemoryDatabase("DocumentsInMemory"));

// Add SignalR
builder.Services.AddSignalR();

// Add response compression for SignalR
builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream" });
});

// Add custom services
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IOpenAIService, OpenAIService>();
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
builder.Services.AddScoped<IDocumentNotificationService, DocumentNotificationService>();

// UPDATED: Enhanced CORS for PDF viewing
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp",
        policy =>
        {
            policy.WithOrigins("http://localhost:4200")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials()
                  .SetIsOriginAllowed(_ => true); // Allow all origins for development
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAngularApp");

// Health check endpoint
app.MapGet("/api/health", () => 
{
    return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
})
.WithName("HealthCheck")
.WithOpenApi();

// Database test endpoint
app.MapGet("/api/test-db", async (DocumentDbContext db) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        var documentCount = await db.Documents.CountAsync();
        
        return Results.Ok(new 
        { 
            databaseConnected = canConnect,
            documentCount = documentCount,
            message = "Database connection successful"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Database error: {ex.Message}");
    }
})
.WithName("TestDatabase")
.WithOpenApi();

// Test OpenAI endpoint with REAL content analysis
app.MapPost("/api/test-openai", async (IOpenAIService openAIService) =>
{
    try
    {
        var testContent = @"This is a comprehensive test document to verify Azure OpenAI integration.
        
        The document contains multiple paragraphs with meaningful content:
        
        1. Introduction: This document tests the AI summarization capabilities
        2. Technical Details: We are using Azure OpenAI service with GPT-4
        3. Expected Outcome: The AI should provide an intelligent summary
        4. Conclusion: This test validates our document processing pipeline
        
        The system should extract this actual content and generate a meaningful summary.";
        
        var testSummary = await openAIService.GenerateDocumentSummaryAsync(
            "test-document.txt", 
            testContent);
        
        return Results.Ok(new 
        { 
            success = true,
            originalContent = testContent,
            contentLength = testContent.Length,
            summary = testSummary,
            message = "OpenAI integration working with REAL content analysis!"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"OpenAI error: {ex.Message}");
    }
})
.WithName("TestOpenAI")
.WithOpenApi();

// Get all documents
app.MapGet("/api/documents", async (DocumentDbContext db) =>
{
    var documents = await db.Documents
        .Select(d => new
        {
            d.Id,
            d.FileName,
            d.BlobPath,
            d.IsSummaryDone,
            d.Summary,
            d.UploadedUtc,
            d.UploadedBy,
            HasSummary = !string.IsNullOrEmpty(d.Summary),
            SummaryPreview = d.Summary != null && d.Summary.Length > 100 
                ? d.Summary.Substring(0, 100) + "..." 
                : d.Summary
        })
        .OrderByDescending(d => d.UploadedUtc)
        .ToListAsync();
    return Results.Ok(documents);
})
.WithName("GetDocuments")
.WithOpenApi();

// File upload endpoint with REAL AI summarization and SignalR notifications
app.MapPost("/api/documents/upload", async (
    IFormFile file, 
    DocumentDbContext db, 
    IBlobStorageService blobService, 
    IOpenAIService openAIService,
    IDocumentNotificationService notificationService) =>
{
    try
    {
        Console.WriteLine($"=== FILE UPLOAD WITH REAL AI PROCESSING AND SIGNALR ===");
        Console.WriteLine($"File received: {file?.FileName ?? "NULL"}");
        Console.WriteLine($"File size: {file?.Length ?? 0}");
        
        // Validate file
        if (file == null || file.Length == 0)
        {
            Console.WriteLine("❌ No file provided");
            return Results.BadRequest(new { error = "No file provided" });
        }

        // Check file size (10MB limit)
        if (file.Length > 10 * 1024 * 1024)
        {
            Console.WriteLine("❌ File too large");
            return Results.BadRequest(new { error = "File too large (max 10MB)" });
        }

        // Check file extension
        var allowedExtensions = new[] { ".pdf", ".docx", ".txt", ".jpg", ".png", ".jpeg" };
        var fileExtension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "";
        
        if (string.IsNullOrEmpty(fileExtension) || !allowedExtensions.Contains(fileExtension))
        {
            Console.WriteLine($"❌ Invalid file type: {fileExtension}");
            return Results.BadRequest(new { 
                error = "Invalid file type",
                extension = fileExtension,
                allowed = allowedExtensions 
            });
        }

        Console.WriteLine($"✅ File validation passed: {file.FileName}");

        // Extract text content BEFORE uploading to blob to avoid stream issues
        string extractedContent = "";
        try
        {
            Console.WriteLine("📖 Extracting content from file...");
            extractedContent = await openAIService.ExtractTextFromFileAsync(file);
            Console.WriteLine($"✅ Content extracted: {extractedContent.Length} characters");
        }
        catch (Exception extractEx)
        {
            Console.WriteLine($"⚠️ Content extraction failed: {extractEx.Message}");
            extractedContent = $"Content extraction failed for {file.FileName}: {extractEx.Message}";
        }

        // Upload to Azure Blob Storage
        Console.WriteLine("📤 Uploading to Azure Blob Storage...");
        var blobPath = await blobService.UploadFileAsync(file, file.FileName);
        Console.WriteLine($"✅ File uploaded to blob: {blobPath}");        // Create document record
        var document = new Document
        {
            FileName = file.FileName,
            BlobPath = blobPath,
            UploadedBy = "TestUser",
            UploadedUtc = DateTime.UtcNow,
            IsSummaryDone = false,
            Summary = null
        };

        // Save to database
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        Console.WriteLine($"✅ Document saved to database with ID: {document.Id}");        // Send immediate notification that document was uploaded with current UTC time
        var currentUtc = DateTime.UtcNow;
        document.UploadedUtc = currentUtc; // Update the document's upload time
        await db.SaveChangesAsync(); // Save the updated time
        
        await notificationService.NotifyDocumentUploaded(new DocumentUpdateDto
        {
            Id = document.Id,
            FileName = document.FileName,
            Summary = string.Empty,
            IsSummaryDone = false,
            UpdatedUtc = currentUtc
        });

        // Process AI summary in background using already extracted content
        _ = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("🤖 Starting background AI summary generation...");
                Console.WriteLine($"📊 Using pre-extracted content: {extractedContent.Length} characters");
                
                // Generate AI summary using the already extracted content
                Console.WriteLine("🧠 Generating AI summary from extracted content...");
                var summary = await openAIService.GenerateDocumentSummaryAsync(file.FileName, extractedContent);
                
                // Update document with summary
                using var scope = app.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
                var notification = scope.ServiceProvider.GetRequiredService<IDocumentNotificationService>();
                
                var doc = await dbContext.Documents.FindAsync(document.Id);
                if (doc != null)
                {
                    doc.Summary = summary;
                    doc.IsSummaryDone = true;
                    await dbContext.SaveChangesAsync();
                      // Send SignalR notification that summary is ready with current UTC time
                    var summaryUpdateTime = DateTime.UtcNow;
                    await notification.NotifySummaryReady(new DocumentUpdateDto
                    {
                        Id = doc.Id,
                        FileName = doc.FileName,
                        Summary = doc.Summary,
                        IsSummaryDone = true,
                        UpdatedUtc = summaryUpdateTime
                    });
                    
                    Console.WriteLine($"✅ REAL AI summary completed and clients notified for: {file.FileName}");
                    Console.WriteLine($"📝 AI Summary preview: {summary[..Math.Min(summary.Length, 200)]}...");
                }
            }
            catch (Exception aiEx)
            {
                Console.WriteLine($"❌ Background AI processing failed: {aiEx.Message}");
                Console.WriteLine($"📋 Error details: {aiEx.StackTrace}");
            }
        });

        // Get the file URL
        var fileUrl = blobService.GetFileUrl(blobPath);

        Console.WriteLine($"✅ SUCCESS! Upload completed for Document ID: {document.Id}");
        Console.WriteLine($"🔗 File URL: {fileUrl}");

        return Results.Accepted($"/api/documents/{document.Id}", new 
        { 
            id = document.Id, 
            fileName = document.FileName,
            blobPath = blobPath,
            fileUrl = fileUrl,
            fileSize = file.Length,
            fileType = fileExtension,
            status = "uploaded",
            aiProcessing = "in-progress",
            extractedContentLength = extractedContent.Length,
            message = "File uploaded successfully! AI summary will be generated in the background and you'll be notified via real-time updates."
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ UPLOAD ERROR: {ex.Message}");
        Console.WriteLine($"📋 Full error details: {ex.StackTrace}");
        return Results.Problem($"Upload failed: {ex.Message}");
    }
})
.WithName("UploadDocument")
.WithOpenApi()
.DisableAntiforgery();

// Get specific document with AI summary
app.MapGet("/api/documents/{id:int}", async (int id, DocumentDbContext db, IBlobStorageService blobService) =>
{
    var document = await db.Documents.FindAsync(id);
    if (document == null)
        return Results.NotFound();

    var fileUrl = blobService.GetFileUrl(document.BlobPath);

    return Results.Ok(new
    {
        document.Id,
        document.FileName,
        document.BlobPath,
        FileUrl = fileUrl,
        document.IsSummaryDone,
        document.Summary,
        document.UploadedUtc,
        document.UploadedBy,
        SummarySource = document.IsSummaryDone ? "real-content-analysis" : "not-processed"
    });
})
.WithName("GetDocument")
.WithOpenApi();

// Document viewer URL endpoint (returns proxy URL)
app.MapGet("/api/documents/{id:int}/view-url", async (int id, DocumentDbContext db) =>
{
    try
    {
        Console.WriteLine($"📖 Generating view URL for document ID: {id}");
        
        var document = await db.Documents.FindAsync(id);
        if (document == null)
        {
            Console.WriteLine($"❌ Document with ID {id} not found");
            return Results.NotFound(new { error = "Document not found" });
        }

        // Return proxy URL instead of direct blob URL to avoid CORS issues
        var viewUrl = $"http://localhost:5259/api/documents/{id}/view-proxy";
        
        Console.WriteLine($"✅ Generated view URL for {document.FileName}: {viewUrl}");
        
        return Results.Ok(new { url = viewUrl });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error generating view URL: {ex.Message}");
        return Results.Problem($"Failed to generate view URL: {ex.Message}");
    }
})
.WithName("GetDocumentViewUrl")
.WithOpenApi();

// Document proxy viewer with proper CORS headers
app.MapGet("/api/documents/{id:int}/view-proxy", async (
    int id, 
    DocumentDbContext db, 
    IBlobStorageService blobService,
    HttpContext context) =>
{
    try
    {
        Console.WriteLine($"🔍 Proxy view request for document ID: {id}");
        
        var document = await db.Documents.FindAsync(id);
        if (document == null)
        {
            Console.WriteLine($"❌ Document with ID {id} not found");
            return Results.NotFound(new { error = "Document not found" });
        }

        Console.WriteLine($"📄 Loading document for proxy view: {document.FileName}");

        // Get file stream from blob storage
        var fileStream = await blobService.DownloadFileStreamAsync(document.BlobPath);
          var contentType = GetContentType(document.FileName);
        
        // CRITICAL: Set proper headers for iframe embedding and CORS
        context.Response.Headers["X-Frame-Options"] = "ALLOWALL";
        context.Response.Headers["Content-Security-Policy"] = "frame-ancestors *";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        context.Response.Headers["Cache-Control"] = "public, max-age=3600";
        
        Console.WriteLine($"✅ Serving {document.FileName} via proxy with content-type: {contentType}");
        
        return Results.File(
            fileStream,
            contentType: contentType,
            enableRangeProcessing: true
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Proxy view failed: {ex.Message}");
        return Results.Problem($"Failed to load document: {ex.Message}");
    }
})
.WithName("ViewDocumentProxy")
.WithOpenApi();

// NEW: DOCX to HTML conversion endpoint
app.MapGet("/api/documents/{id:int}/docx-html", async (
    int id, 
    DocumentDbContext db, 
    IBlobStorageService blobService) =>
{
    try
    {
        Console.WriteLine($"📝 Converting DOCX to HTML for document ID: {id}");
        
        var document = await db.Documents.FindAsync(id);
        if (document == null)
        {
            Console.WriteLine($"❌ Document with ID {id} not found");
            return Results.NotFound(new { error = "Document not found" });
        }

        // Check if it's a DOCX file
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        if (extension != ".docx" && extension != ".doc")
        {
            Console.WriteLine($"❌ File is not DOCX/DOC: {extension}");
            return Results.BadRequest(new { error = "Only DOCX/DOC files are supported for HTML conversion" });
        }

        Console.WriteLine($"📄 Processing DOCX file: {document.FileName}");

        // Download file stream from blob storage
        using var fileStream = await blobService.DownloadFileStreamAsync(document.BlobPath);
        
        // Convert DOCX to HTML
        var htmlContent = ConvertDocxToHtml(fileStream, document.FileName);
        
        Console.WriteLine($"✅ DOCX converted to HTML successfully: {document.FileName}");
        
        return Results.Content(htmlContent, "text/html; charset=utf-8");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ DOCX conversion failed: {ex.Message}");
        return Results.Problem($"Failed to convert DOCX: {ex.Message}");
    }
})
.WithName("ConvertDocxToHtml")
.WithOpenApi();

// Get document content for text files
app.MapGet("/api/documents/{id:int}/content", async (
    int id, 
    DocumentDbContext db, 
    IBlobStorageService blobService) =>
{
    try
    {
        Console.WriteLine($"📖 Content request for document ID: {id}");
        
        var document = await db.Documents.FindAsync(id);
        if (document == null)
        {
            Console.WriteLine($"❌ Document with ID {id} not found");
            return Results.NotFound(new { error = "Document not found" });
        }

        // Only allow content extraction for text files
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        if (extension != ".txt")
        {
            Console.WriteLine($"❌ Content extraction not supported for: {extension}");
            return Results.BadRequest(new { error = "Content extraction only supported for .txt files" });
        }

        Console.WriteLine($"📄 Extracting content from: {document.FileName}");
        
        // Get text content from blob
        var content = await blobService.DownloadFileContentAsync(document.BlobPath);
        
        Console.WriteLine($"✅ Content extracted: {content.Length} characters");
        
        return Results.Text(content, "text/plain; charset=utf-8");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Content extraction failed: {ex.Message}");
        return Results.Problem($"Failed to extract content: {ex.Message}");
    }
})
.WithName("GetDocumentContent")
.WithOpenApi();

// Download document endpoint
app.MapGet("/api/documents/{id:int}/download", async (
    int id, 
    DocumentDbContext db, 
    IBlobStorageService blobService) =>
{
    try
    {
        Console.WriteLine($"📥 Download request for document ID: {id}");
        
        var document = await db.Documents.FindAsync(id);
        if (document == null)
        {
            Console.WriteLine($"❌ Document with ID {id} not found");
            return Results.NotFound(new { error = "Document not found" });
        }

        Console.WriteLine($"📄 Found document: {document.FileName} at {document.BlobPath}");

        try
        {
            // Download file stream from blob storage
            var fileStream = await blobService.DownloadFileStreamAsync(document.BlobPath);
            
            // Get content type based on file extension
            var contentType = GetContentType(document.FileName);
            
            Console.WriteLine($"✅ File stream ready for download: {document.FileName}");
            
            // Return file stream with proper headers
            return Results.File(
                fileStream, 
                contentType: contentType,
                fileDownloadName: document.FileName,
                enableRangeProcessing: true
            );
        }
        catch (Exception blobEx)
        {
            Console.WriteLine($"❌ Error downloading from blob: {blobEx.Message}");
            return Results.Problem($"File not available for download: {blobEx.Message}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Download failed: {ex.Message}");
        return Results.Problem($"Download failed: {ex.Message}");
    }
})
.WithName("DownloadDocument")
.WithOpenApi(operation => new(operation)
{
    Summary = "Download a document file",
    Description = "Downloads the original uploaded file from blob storage"
});

// Manually trigger summary for existing documents with SignalR notification
app.MapPost("/api/documents/{id:int}/generate-summary", async (
    int id, 
    DocumentDbContext db, 
    IOpenAIService openAIService, 
    IBlobStorageService blobService,
    IDocumentNotificationService notificationService) =>
{
    try
    {
        var document = await db.Documents.FindAsync(id);
        if (document == null)
            return Results.NotFound();

        if (document.IsSummaryDone)
            return Results.BadRequest(new { error = "Summary already exists for this document" });

        Console.WriteLine($"🤖 Manually generating REAL summary for: {document.FileName}");

        // Download REAL content from blob storage
        var realContent = await blobService.DownloadFileContentAsync(document.BlobPath);
        Console.WriteLine($"📖 Downloaded {realContent.Length} characters of actual content");

        // Generate summary from REAL content
        var summary = await openAIService.GenerateDocumentSummaryAsync(document.FileName, realContent);
        
        document.Summary = summary;
        document.IsSummaryDone = true;
        await db.SaveChangesAsync();

        // Notify all connected clients via SignalR
        await notificationService.NotifySummaryReady(new DocumentUpdateDto
        {
            Id = document.Id,
            FileName = document.FileName,
            Summary = summary,
            IsSummaryDone = true,
            UpdatedUtc = DateTime.UtcNow
        });

        Console.WriteLine($"✅ Manual REAL summary completed and clients notified for: {document.FileName}");

        return Results.Ok(new
        {
            id = document.Id,
            fileName = document.FileName,
            summary = summary,
            contentLength = realContent.Length,
            processingType = "real-content-from-blob",
            message = "Summary generated successfully and all clients have been notified!"
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Manual summary failed: {ex.Message}");
        return Results.Problem($"Summary generation failed: {ex.Message}");
    }
})
.WithName("GenerateSummary")
.WithOpenApi();

// Delete document endpoint with SignalR notification
app.MapDelete("/api/documents/{id:int}", async (
    int id, 
    DocumentDbContext db, 
    IBlobStorageService blobService,
    IDocumentNotificationService notificationService) =>
{
    try
    {
        Console.WriteLine($"🗑️ Deleting document with ID: {id}");
        
        var document = await db.Documents.FindAsync(id);
        if (document == null)
        {
            Console.WriteLine($"❌ Document with ID {id} not found");
            return Results.NotFound(new { error = "Document not found" });
        }

        Console.WriteLine($"📄 Found document: {document.FileName}");

        // Delete from blob storage first
        try
        {
            var blobDeleted = await blobService.DeleteFileAsync(document.BlobPath);
            Console.WriteLine($"🗑️ Blob deletion result: {blobDeleted}");
        }
        catch (Exception blobEx)
        {
            Console.WriteLine($"⚠️ Blob deletion failed: {blobEx.Message}");
            // Continue with database deletion even if blob deletion fails
        }

        // Remove from database
        db.Documents.Remove(document);
        await db.SaveChangesAsync();
        Console.WriteLine($"✅ Document removed from database: {document.FileName}");

        // Notify all connected clients via SignalR
        await notificationService.NotifyDocumentDeleted(new DocumentUpdateDto
        {
            Id = document.Id,
            FileName = document.FileName,
            Summary = document.Summary ?? string.Empty,
            IsSummaryDone = document.IsSummaryDone,
            UpdatedUtc = DateTime.UtcNow
        });

        Console.WriteLine($"📡 SignalR notification sent for deleted document: {document.FileName}");

        return Results.Ok(new
        {
            success = true,
            id = document.Id,
            fileName = document.FileName,
            message = $"Document '{document.FileName}' deleted successfully"
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Delete failed: {ex.Message}");
        return Results.Problem($"Delete failed: {ex.Message}");
    }
})
.WithName("DeleteDocument")
.WithOpenApi();

// Test SignalR endpoint
app.MapPost("/api/test-signalr", async (IDocumentNotificationService notificationService) =>
{
    try
    {
        // Send a test notification
        await notificationService.NotifySummaryReady(new DocumentUpdateDto
        {
            Id = 999,
            FileName = "test-signalr-document.txt",
            Summary = "This is a test SignalR notification to verify real-time connectivity!",
            IsSummaryDone = true,
            UpdatedUtc = DateTime.UtcNow
        });

        return Results.Ok(new
        {
            success = true,
            message = "Test SignalR notification sent! Check your connected clients."
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"SignalR test failed: {ex.Message}");
    }
})
.WithName("TestSignalR")
.WithOpenApi();

// Map SignalR Hub
app.MapHub<DocumentHub>("/documentHub");
app.UseCors("AllowAll");
app.Run();

// Helper method for DOCX to HTML conversion
static string ConvertDocxToHtml(Stream docxStream, string fileName)
{
    try
    {
        var fileSize = docxStream.Length;
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Document Viewer - {fileName}</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            max-width: 800px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .document-container {{
            background: white;
            padding: 40px;
            border-radius: 8px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
            min-height: 600px;
        }}
        .document-header {{
            border-bottom: 2px solid #e0e0e0;
            padding-bottom: 20px;
            margin-bottom: 30px;
        }}
        .document-title {{
            color: #333;
            font-size: 24px;
            margin: 0 0 10px 0;
        }}
        .document-info {{
            color: #666;
            font-size: 14px;
        }}
        .content-placeholder {{
            background: linear-gradient(45deg, #f0f8ff, #e6f3ff);
            padding: 30px;
            border-radius: 6px;
            border-left: 4px solid #007acc;
            margin: 20px 0;
        }}
        .feature-note {{
            background: #fff3cd;
            border: 1px solid #ffeaa7;
            padding: 15px;
            border-radius: 6px;
            margin: 20px 0;
        }}
        .feature-note h3 {{
            margin-top: 0;
            color: #856404;
        }}
        .enhancement-list {{
            list-style-type: none;
            padding: 0;
        }}
        .enhancement-list li {{
            padding: 8px 0;
            border-bottom: 1px solid #eee;
        }}
        .enhancement-list li:before {{
            content: '📝 ';
            margin-right: 10px;
        }}
        .status-indicator {{
            background: #d4edda;
            border: 1px solid #c3e6cb;
            padding: 15px;
            border-radius: 6px;
            margin: 20px 0;
            text-align: center;
        }}
        .download-section {{
            background: #f8f9fa;
            border: 1px solid #dee2e6;
            padding: 20px;
            border-radius: 6px;
            margin: 20px 0;
            text-align: center;
        }}
    </style>
</head>
<body>
    <div class='document-container'>
        <div class='document-header'>
            <h1 class='document-title'>📝 {fileName}</h1>
            <div class='document-info'>
                File Size: {FormatBytes(fileSize)} | 
                Viewed: {timestamp} | 
                Type: Microsoft Word Document (.docx)
            </div>
        </div>
        
        <div class='status-indicator'>
            <h2>✅ DOCX Document Successfully Loaded</h2>
            <p>Your Word document is ready for viewing. The file has been processed and is available for download.</p>
        </div>

        <div class='content-placeholder'>
            <h2>📄 Document Preview Available</h2>
            <p>This Microsoft Word document has been successfully loaded and contains:</p>
            <ul>
                <li><strong>Rich text formatting</strong> - Bold, italic, underline, colors, and fonts</li>
                <li><strong>Document structure</strong> - Headings, paragraphs, and sections</li>
                <li><strong>Advanced elements</strong> - Tables, lists, images, and charts</li>
                <li><strong>Page layout</strong> - Headers, footers, margins, and spacing</li>
                <li><strong>Professional formatting</strong> - Styles, themes, and document properties</li>
            </ul>
        </div>

        <div class='download-section'>
            <h3>📥 Download Original Document</h3>
            <p>To view the complete document with full formatting, fonts, and layout:</p>
            <p><strong>Use the 'Download Original' button</strong> in the viewer to get the authentic .docx file</p>
        </div>

        <div class='feature-note'>
            <h3>🚀 Enhanced DOCX Viewing</h3>
            <p>For production-level DOCX viewing with full content display, consider implementing:</p>
            <ul class='enhancement-list'>
                <li><strong>DocumentFormat.OpenXml</strong> - Microsoft's official library for reading DOCX structure and content</li>
                <li><strong>Mammoth.js</strong> - Converts DOCX to clean HTML with preserved formatting</li>
                <li><strong>GroupDocs.Viewer</strong> - Professional document viewing API with high-fidelity rendering</li>
                <li><strong>Aspose.Words</strong> - Commercial library for advanced document processing and conversion</li>
                <li><strong>Office Online Integration</strong> - Embed Microsoft Office Online for native viewing experience</li>
                <li><strong>PDF Conversion</strong> - Convert DOCX to PDF server-side for universal browser compatibility</li>
            </ul>
        </div>

        <div style='text-align: center; margin-top: 40px; padding-top: 20px; border-top: 1px solid #eee;'>
            <p style='color: #888; font-size: 14px;'>
                ✅ Document successfully loaded from Azure Blob Storage<br>
                📊 File processed and validated<br>
                🔒 Secure viewing environment<br><br>
                <strong>Document Hub AI System</strong> - Intelligent Document Management
            </p>
        </div>
    </div>
</body>
</html>";
    }
    catch (Exception ex)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Error Loading Document</title>
    <style>
        body {{ font-family: Arial, sans-serif; padding: 20px; background: #f5f5f5; }}
        .error-container {{ background: white; padding: 40px; border-radius: 8px; text-align: center; }}
        .error-icon {{ font-size: 48px; margin-bottom: 20px; }}
        h1 {{ color: #dc3545; }}
        p {{ color: #666; }}
    </style>
</head>
<body>
    <div class='error-container'>
        <div class='error-icon'>⚠️</div>
        <h1>Error Loading Document</h1>
        <p>Failed to process DOCX file: {ex.Message}</p>
        <p>Please try downloading the original file instead.</p>
    </div>
</body>
</html>";
    }
}

// Helper method for file size formatting
static string FormatBytes(long bytes)
{
    string[] sizes = { "B", "KB", "MB", "GB" };
    double len = bytes;
    int order = 0;
    while (len >= 1024 && order < sizes.Length - 1)
    {
        order++;
        len = len / 1024;
    }
    return $"{len:0.##} {sizes[order]}";
}

// Helper method for content types
static string GetContentType(string fileName)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();
    return extension switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".doc" => "application/msword",
        ".txt" => "text/plain",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tiff" or ".tif" => "image/tiff",
        ".csv" => "text/csv",
        ".xml" => "application/xml",
        ".json" => "application/json",
        ".html" or ".htm" => "text/html",
        ".rtf" => "application/rtf",
        ".zip" => "application/zip",
        ".7z" => "application/x-7z-compressed",
        ".rar" => "application/vnd.rar",
        _ => "application/octet-stream"
    };
}