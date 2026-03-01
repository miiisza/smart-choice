namespace SmartChoice.Api.Storage;

public sealed record StoredObject(
    string Key,
    string Url,
    string ContentType,
    long SizeBytes);

public interface IObjectStorageService
{
    Task EnsureBucketReadyAsync(CancellationToken cancellationToken);
    Task<StoredObject> UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken);
}
