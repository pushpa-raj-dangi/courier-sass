namespace Courier.SharedKernel;

// Dev-appropriate IObjectStorage backed by the local filesystem. Production targets S3/Azure
// Blob behind this same interface (ARCHITECTURE.md §5) — swapping the DI registration in
// Program.cs is the only change a real implementation needs.
public class LocalFileSystemObjectStorage : IObjectStorage
{
    private readonly string _rootPath;

    public LocalFileSystemObjectStorage(string rootPath)
    {
        _rootPath = rootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var key = $"{keyPrefix}/{Guid.NewGuid():N}";
        var path = Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);

        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No object found for key '{key}'.", path);
        }
        return Task.FromResult<Stream>(File.OpenRead(path));
    }
}
