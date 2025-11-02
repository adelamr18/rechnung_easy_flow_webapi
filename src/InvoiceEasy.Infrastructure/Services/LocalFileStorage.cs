using InvoiceEasy.Domain.Interfaces;
using System.IO;

namespace InvoiceEasy.Infrastructure.Services;

public class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(string rootPath)
    {
        _rootPath = rootPath;
        // Ensure directories exist
        Directory.CreateDirectory(Path.Combine(_rootPath, "invoices"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "receipts"));
    }

    public async Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder)
    {
        var sanitizedFileName = SanitizeFileName(fileName);
        var uniqueFileName = $"{Guid.NewGuid()}_{sanitizedFileName}";
        var folderPath = Path.Combine(_rootPath, folder);
        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, uniqueFileName);

        using (var fileStreamOut = new FileStream(filePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fileStreamOut);
        }

        return filePath;
    }

    public Task<Stream> GetFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        return Task.FromResult<Stream>(File.OpenRead(filePath));
    }

    public Task DeleteFileAsync(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public string GetPublicUrl(string filePath, string baseUrl)
    {
        var relativePath = filePath.Replace(_rootPath, "").Replace("\\", "/").TrimStart('/');
        return $"{baseUrl}/api/files/{relativePath}";
    }

    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}

