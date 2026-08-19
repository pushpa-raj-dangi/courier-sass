namespace Courier.SharedKernel;

public interface IObjectStorage
{
    // Stores content under a key derived from keyPrefix (implementations append a unique
    // suffix), returning the final storage key callers must persist to retrieve it later.
    Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
}
