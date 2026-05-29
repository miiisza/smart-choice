namespace SmartChoice.Api.Storage;

public enum ObjectStorageProvider
{
    S3Compatible = 0,
    LocalDisk = 1
}

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public ObjectStorageProvider Provider { get; init; } = ObjectStorageProvider.LocalDisk;
    public string BucketName { get; init; } = "matchme-photos";
    public string Region { get; init; } = "us-east-1";
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string? ServiceUrl { get; init; }
    public string? PublicBaseUrl { get; init; }
    public string LocalDiskRootPath { get; init; } = "App_Data/object-storage";
    public string LocalDiskSigningSecret { get; init; } = "dev_local_storage_signing_secret_change_me";
    public bool ForcePathStyle { get; init; } = true;
    public bool EnsureBucketExistsOnStartup { get; init; } = true;
    public bool MakeBucketPublicOnStartup { get; init; } = false;
    public int ThumbnailWidth { get; init; } = 480;
    public int SignedUrlTtlMinutes { get; init; } = 5;
    public long MaxUploadBytes { get; init; } = 10 * 1024 * 1024;
}
