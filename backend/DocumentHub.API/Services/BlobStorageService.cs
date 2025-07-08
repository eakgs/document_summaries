using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Text;

namespace DocHub.Api.Services;

public interface IBlobStorageService
{
    Task<string> UploadFileAsync(IFormFile file, string fileName);
    Task<bool> DeleteFileAsync(string blobPath);
    string GetFileUrl(string blobPath);
    Task<string> DownloadFileContentAsync(string blobPath);
    Task<Stream> DownloadFileStreamAsync(string blobPath);
}

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;

    public BlobStorageService(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BlobStorage");
        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerName = configuration["BlobStorage:ContainerName"] ?? "documents";
    }

    public async Task<string> UploadFileAsync(IFormFile file, string fileName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            var fileExtension = Path.GetExtension(fileName);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var blobName = $"{timestamp}_{Guid.NewGuid():N}_{fileNameWithoutExtension}{fileExtension}";
            
            var blobClient = containerClient.GetBlobClient(blobName);
            var contentType = GetContentType(fileName);
            
            using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobHttpHeaders 
            { 
                ContentType = contentType 
            });

            Console.WriteLine($"✅ File uploaded to blob: {blobName}");
            return blobName;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error uploading to blob storage: {ex.Message}");
            throw;
        }
    }

    public async Task<string> DownloadFileContentAsync(string blobPath)
    {
        try
        {
            Console.WriteLine($"📥 Downloading content from blob: {blobPath}");
            
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);
            
            // Check if blob exists
            var exists = await blobClient.ExistsAsync();
            if (!exists.Value)
            {
                throw new FileNotFoundException($"Blob not found: {blobPath}");
            }

            // Get blob properties to determine content type
            var properties = await blobClient.GetPropertiesAsync();
            var contentType = properties.Value.ContentType;
            var fileExtension = Path.GetExtension(blobPath).ToLowerInvariant();
            
            Console.WriteLine($"📄 Blob content type: {contentType}, Extension: {fileExtension}");

            // FIXED: Proper disposal of download response
            var response = await blobClient.DownloadStreamingAsync();
            
            // Read the content immediately and dispose the response
            switch (fileExtension)
            {
                case ".txt":
                    using (var stream = response.Value.Content)
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var content = await reader.ReadToEndAsync();
                        Console.WriteLine($"✅ Downloaded {content.Length} characters from {blobPath}");
                        return content;
                    }

                case ".pdf":
                    Console.WriteLine($"📄 PDF file downloaded: {blobPath}");
                    var pdfSize = properties.Value.ContentLength;
                    return $"PDF content from {blobPath} ({pdfSize} bytes) - requires PDF parsing library for text extraction. " +
                           $"Consider implementing iTextSharp, PdfSharp, or UglyToad.PdfPig for full text extraction.";

                case ".docx":
                    Console.WriteLine($"📝 DOCX file downloaded: {blobPath}");
                    var docxSize = properties.Value.ContentLength;
                    return $"DOCX content from {blobPath} ({docxSize} bytes) - requires OpenXML library for text extraction. " +
                           $"Consider implementing DocumentFormat.OpenXml or NPOI for full text extraction.";

                case ".jpg":
                case ".jpeg":
                case ".png":
                    Console.WriteLine($"🖼️ Image file downloaded: {blobPath}");
                    var imageSize = properties.Value.ContentLength;
                    return $"Image file {blobPath} ({imageSize} bytes) - visual content requiring OCR for text extraction. " +
                           $"Consider implementing Azure Computer Vision or Tesseract for OCR capabilities.";

                default:
                    // Try to read as text for unknown types
                    try
                    {
                        using var stream = response.Value.Content;
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        var content = await reader.ReadToEndAsync();
                        Console.WriteLine($"✅ Downloaded {content.Length} characters from {blobPath} (as text)");
                        return content;
                    }
                    catch
                    {
                        Console.WriteLine($"❌ Could not read {blobPath} as text");
                        var fileSize = properties.Value.ContentLength;
                        return $"Binary file {blobPath} ({fileSize} bytes) - content not readable as text. " +
                               $"Specialized processing may be required for this file type.";
                    }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error downloading from blob storage: {ex.Message}");
            throw new Exception($"Failed to download content from {blobPath}: {ex.Message}");
        }
    }

    public async Task<Stream> DownloadFileStreamAsync(string blobPath)
    {
        try
        {
            Console.WriteLine($"📥 Downloading stream from blob: {blobPath}");
            
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);
            
            var exists = await blobClient.ExistsAsync();
            if (!exists.Value)
            {
                throw new FileNotFoundException($"Blob not found: {blobPath}");
            }
            
            var response = await blobClient.DownloadStreamingAsync();
            Console.WriteLine($"✅ Stream downloaded successfully from {blobPath}");
            
            // Return the content stream - caller is responsible for disposal
            return response.Value.Content;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error downloading stream: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(string blobPath)
    {
        try
        {
            Console.WriteLine($"🗑️ Deleting blob: {blobPath}");
            
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);
            
            var response = await blobClient.DeleteIfExistsAsync();
            
            if (response.Value)
            {
                Console.WriteLine($"✅ Blob deleted successfully: {blobPath}");
            }
            else
            {
                Console.WriteLine($"⚠️ Blob not found for deletion: {blobPath}");
            }
            
            return response.Value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error deleting blob: {ex.Message}");
            return false;
        }
    }

    public string GetFileUrl(string blobPath)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(blobPath);
            var url = blobClient.Uri.ToString();
            
            Console.WriteLine($"🔗 Generated URL for {blobPath}: {url}");
            return url;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error generating URL for {blobPath}: {ex.Message}");
            return $"Error generating URL: {ex.Message}";
        }
    }

    private static string GetContentType(string fileName)
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
}