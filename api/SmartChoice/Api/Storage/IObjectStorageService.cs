namespace SmartChoice.Api.Storage;

public sealed record StoredObject(
    string Key,
    string ContentType,
    long SizeBytes);

public sealed record SignedObjectUrl(
    string Key,
    string Url,
    DateTime ExpiresAtUtc);

public interface IObjectStorageService
{
    Task EnsureBucketReadyAsync(CancellationToken cancellationToken);
    Task<StoredObject> UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken);
    Task<SignedObjectUrl> GetReadUrlAsync(string key, TimeSpan ttl, CancellationToken cancellationToken);
}
