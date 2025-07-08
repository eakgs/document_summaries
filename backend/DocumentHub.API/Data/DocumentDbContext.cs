using Microsoft.EntityFrameworkCore;
using DocHub.Api.Models;

namespace DocHub.Api.Data;

public class DocumentDbContext : DbContext
{
    public DocumentDbContext(DbContextOptions<DocumentDbContext> options) : base(options)
    {
    }

    public DbSet<Document> Documents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            
            entity.Property(e => e.FileName)
                .IsRequired()
                .HasMaxLength(260);
            
            entity.Property(e => e.BlobPath)
                .IsRequired()
                .HasMaxLength(500);
              entity.Property(e => e.UploadedBy)
                .IsRequired()
                .HasMaxLength(100);
            
            entity.Property(e => e.UploadedUtc);
            
            entity.Property(e => e.IsSummaryDone);

            // Index for performance
            entity.HasIndex(e => e.IsSummaryDone);
        });
    }
}