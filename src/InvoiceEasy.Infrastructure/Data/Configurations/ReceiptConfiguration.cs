using System;
using System.Collections.Generic;
using System.Text.Json;
using InvoiceEasy.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceEasy.Infrastructure.Data.Configurations;

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.HasKey(r => r.Id);
        
        builder.Property(r => r.FileName)
            .IsRequired()
            .HasMaxLength(255);
            
        builder.Property(r => r.FilePath)
            .IsRequired()
            .HasMaxLength(1000);
            
        builder.Property(r => r.MerchantName)
            .HasMaxLength(255);
            
        builder.Property(r => r.TotalAmount)
            .HasPrecision(18, 2);

        var dictionaryComparer = new ValueComparer<Dictionary<string, string>>(
            (d1, d2) => d1 == null ? d2 == null : d2 != null && d1.Count == d2.Count && !d1.Except(d2).Any(),
            d => d == null ? 0 : d.GetHashCode(),
            d => d == null ? new Dictionary<string, string>() : new Dictionary<string, string>(d));

        var jsonOptions = new JsonSerializerOptions();
        
        builder.Property(r => r.ExtractedData)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, jsonOptions) ?? new Dictionary<string, string>())
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(dictionaryComparer);
            
        builder.Property(r => r.UploadDate)
            .IsRequired();
    }
}
