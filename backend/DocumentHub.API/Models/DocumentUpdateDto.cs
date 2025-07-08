// Create this file: Models/DocumentUpdateDto.cs
namespace DocHub.Api.Models
{
    public class DocumentUpdateDto
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public bool IsSummaryDone { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}