using Azure.AI.OpenAI;
using Azure;
using System.Text;

namespace DocHub.Api.Services;

public interface IOpenAIService
{
    Task<string> GenerateDocumentSummaryAsync(string fileName, string fileContent);
    Task<string> ExtractTextFromFileAsync(IFormFile file);
    Task<string> AnalyzeUploadedDocumentAsync(IFormFile file);
    Task<string> ProcessDocumentFromBlobAsync(string fileName, string blobPath);
}

public class OpenAIService : IOpenAIService
{
    private readonly IConfiguration _configuration;
    private readonly OpenAIClient _openAIClient;
    private readonly string _deploymentName;
    private readonly IBlobStorageService _blobService;

    public OpenAIService(IConfiguration configuration, IBlobStorageService blobService)
    {
        _configuration = configuration;
        _blobService = blobService;

        var apiKey = _configuration["OpenAI:ApiKey"] ?? throw new ArgumentNullException("OpenAI:ApiKey not configured");
        var endpoint = _configuration["OpenAI:Endpoint"] ?? throw new ArgumentNullException("OpenAI:Endpoint not configured");
        _deploymentName = _configuration["OpenAI:DeploymentName"] ?? throw new ArgumentNullException("OpenAI:DeploymentName not configured");

        _openAIClient = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
    }

    public async Task<string> GenerateDocumentSummaryAsync(string fileName, string fileContent)
    {
        try
        {
            if (string.IsNullOrEmpty(fileContent))
            {
                return $"Error: No content available for {fileName}";
            }

            var systemMessage = "You are a professional document analyst. Provide clear, concise summaries focused on key information.";
            var prompt = $"Please analyze and summarize the following document. Focus on key points and main ideas.\n\nDocument Name: {fileName}\nContent:\n{fileContent}";

            var options = new CompletionsOptions
            {
                Prompts = { $"{systemMessage}\n\n{prompt}" },
                Temperature = 0.7f,
                MaxTokens = 1000
            };

            try
            {
                Response<Completions> response = await _openAIClient.GetCompletionsAsync(options);
                var summary = response.Value.Choices[0].Text;

                return string.IsNullOrEmpty(summary) 
                    ? "Error: Unable to generate summary" 
                    : summary;
            }
            catch (RequestFailedException ex)
            {
                return $"OpenAI API error: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            return $"Error generating summary: {ex.Message}";
        }
    }

    public async Task<string> ExtractTextFromFileAsync(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            return $"Error extracting text: {ex.Message}";
        }
    }

    public async Task<string> AnalyzeUploadedDocumentAsync(IFormFile file)
    {
        try
        {
            var content = await ExtractTextFromFileAsync(file);
            return await GenerateDocumentSummaryAsync(file.FileName, content);
        }
        catch (Exception ex)
        {
            return $"Error analyzing document: {ex.Message}";
        }
    }

    public async Task<string> ProcessDocumentFromBlobAsync(string fileName, string blobPath)
    {
        try
        {
            var content = await _blobService.DownloadFileContentAsync(blobPath);
            return await GenerateDocumentSummaryAsync(fileName, content);
        }
        catch (Exception ex)
        {
            return $"Error processing document from blob: {ex.Message}";
        }
    }
}