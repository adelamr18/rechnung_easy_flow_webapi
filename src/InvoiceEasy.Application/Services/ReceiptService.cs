using InvoiceEasy.Domain.Interfaces;
using InvoiceEasy.Domain.Interfaces.Repositories;
using InvoiceEasy.Domain.Interfaces.Services;
using InvoiceEasy.Domain.Models;
using Microsoft.Extensions.Logging;
using System.IO;

namespace InvoiceEasy.Application.Services;

public class ReceiptService : IReceiptService
{
    private readonly IFileStorage _fileStorage;
    private readonly IReceiptRepository _receiptRepository;
    private readonly ILogger<ReceiptService> _logger;

    public ReceiptService(
        IFileStorage fileStorage,
        IReceiptRepository receiptRepository,
        ILogger<ReceiptService> logger)
    {
        _fileStorage = fileStorage;
        _receiptRepository = receiptRepository;
        _logger = logger;
    }

    public async Task<Receipt> ProcessReceiptUploadAsync(Stream fileStream, string fileName)
    {
        if (fileStream == null || fileStream.Length == 0)
        {
            throw new ArgumentException("No file uploaded");
        }

        var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!IsSupportedFileType(fileExtension))
        {
            throw new NotSupportedException($"File type {fileExtension} is not supported");
        }

        try
        {
            // Save the file
            var filePath = await _fileStorage.SaveFileAsync(
                fileStream,
                fileName,
                "receipts");

            // Create receipt record
            var receipt = new Receipt
            {
                FileName = fileName,
                FilePath = filePath,
                UploadDate = DateTime.UtcNow
            };

            // TODO: Add receipt processing logic here (OCR, data extraction, etc.)
            // For now, we'll just save the basic receipt info

            return await _receiptRepository.AddAsync(receipt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing receipt upload");
            throw new Exception("Error processing receipt", ex);
        }
    }

    public async Task<Receipt> GetReceiptAsync(Guid id)
    {
        var receipt = await _receiptRepository.GetByIdAsync(id);
        if (receipt == null)
        {
            throw new FileNotFoundException($"Receipt with ID {id} not found");
        }
        return receipt;
    }

    public Task<IEnumerable<Receipt>> GetAllReceiptsAsync()
    {
        return _receiptRepository.GetAllAsync();
    }

    public async Task DeleteReceiptAsync(Guid id)
    {
        var receipt = await _receiptRepository.GetByIdAsync(id);
        if (receipt != null)
        {
            // Delete the physical file
            await _fileStorage.DeleteFileAsync(receipt.FilePath);
            
            // Delete the database record
            await _receiptRepository.DeleteAsync(id);
        }
    }

    private bool IsSupportedFileType(string fileExtension) =>
        fileExtension.ToLowerInvariant() switch
        {
            ".pdf" => true,
            ".jpg" or ".jpeg" or ".png" => true,
            _ => false
        };
}
