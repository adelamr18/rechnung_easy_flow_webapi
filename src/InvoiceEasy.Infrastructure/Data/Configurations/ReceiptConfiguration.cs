using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using InvoiceEasy.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceEasy.Infrastructure.Data.Configurations;

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly ValueComparer<Dictionary<string, string>> DictionaryComparer =
        new(
            (left, right) => DictionariesEqual(left, right),
            dictionary => DictionaryHashCode(dictionary),
            dictionary => DictionarySnapshot(dictionary));

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

        builder.Property(r => r.ExtractedData)
            .HasConversion(
                value => JsonSerializer.Serialize(value ?? new Dictionary<string, string>(), SerializerOptions),
                value => string.IsNullOrEmpty(value)
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(value, SerializerOptions) ?? new Dictionary<string, string>())
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(DictionaryComparer);

        builder.Property(r => r.UploadDate)
            .IsRequired();
    }

    private static bool DictionariesEqual(
        Dictionary<string, string>? left,
        Dictionary<string, string>? right) =>
        left == null && right == null
            ? true
            : left != null && right != null &&
              left.Count == right.Count &&
              left.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                  .SequenceEqual(right.OrderBy(kv => kv.Key, StringComparer.Ordinal));

    private static int DictionaryHashCode(Dictionary<string, string>? source) =>
        source == null
            ? 0
            : source.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Aggregate(
                    0,
                    (hash, kv) => HashCode.Combine(
                        hash,
                        kv.Key.GetHashCode(StringComparison.Ordinal),
                        (kv.Value ?? string.Empty).GetHashCode(StringComparison.Ordinal)));

    private static Dictionary<string, string> DictionarySnapshot(Dictionary<string, string>? source) =>
        source == null
            ? new Dictionary<string, string>()
            : source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
