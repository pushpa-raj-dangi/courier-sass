using Courier.SharedKernel;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class LocalFileSystemObjectStorageTests
{
    private static LocalFileSystemObjectStorage CreateStorage(out string rootPath)
    {
        rootPath = Path.Combine(Path.GetTempPath(), "courier-storage-tests", Guid.NewGuid().ToString("N"));
        return new LocalFileSystemObjectStorage(rootPath);
    }

    [Fact]
    public async Task UploadAsync_ThenOpenReadAsync_RoundTripsContent()
    {
        var storage = CreateStorage(out var rootPath);
        try
        {
            var originalBytes = "hello kyc document"u8.ToArray();
            using var uploadStream = new MemoryStream(originalBytes);

            var key = await storage.UploadAsync("kyc/merchant-1", uploadStream, "text/plain");

            Assert.StartsWith("kyc/merchant-1/", key);

            await using var readStream = await storage.OpenReadAsync(key);
            using var reader = new MemoryStream();
            await readStream.CopyToAsync(reader);

            Assert.Equal(originalBytes, reader.ToArray());
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadAsync_TwiceWithSamePrefix_ProducesDistinctKeys()
    {
        var storage = CreateStorage(out var rootPath);
        try
        {
            using var stream1 = new MemoryStream("a"u8.ToArray());
            using var stream2 = new MemoryStream("b"u8.ToArray());

            var key1 = await storage.UploadAsync("kyc/merchant-1", stream1, "text/plain");
            var key2 = await storage.UploadAsync("kyc/merchant-1", stream2, "text/plain");

            Assert.NotEqual(key1, key2);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenReadAsync_UnknownKey_ThrowsFileNotFoundException()
    {
        var storage = CreateStorage(out var rootPath);
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync("kyc/merchant-1/does-not-exist"));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
