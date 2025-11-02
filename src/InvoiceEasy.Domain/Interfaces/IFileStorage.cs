namespace InvoiceEasy.Domain.Interfaces;

public interface IFileStorage
{
    Task<string> SaveFileAsync(Stream fileStream, string fileName, string folder);
    Task<Stream> GetFileAsync(string filePath);
    Task DeleteFileAsync(string filePath);
    string GetPublicUrl(string filePath, string baseUrl);
}

