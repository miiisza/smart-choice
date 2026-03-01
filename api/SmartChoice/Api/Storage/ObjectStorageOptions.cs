namespace SmartChoice.Api.Storage;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string BucketName { get; init; } = "smart-choice-polls";
    public string Region { get; init; } = "us-east-1";
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string? ServiceUrl { get; init; }
    public string? PublicBaseUrl { get; init; }
    public bool ForcePathStyle { get; init; } = true;
    public bool EnsureBucketExistsOnStartup { get; init; } = true;
    public bool MakeBucketPublicOnStartup { get; init; } = true;
    public int ThumbnailWidth { get; init; } = 480;
    public long MaxUploadBytes { get; init; } = 10 * 1024 * 1024;
}
