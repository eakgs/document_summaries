namespace DocHub.Api.Models;

public class Document
{
    public int Id { get; set; }
    public required string FileName { get; set; }
    public required string BlobPath { get; set; }
    public string? Summary { get; set; }
    public bool IsSummaryDone { get; set; } = false;
    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
    public required string UploadedBy { get; set; }
}