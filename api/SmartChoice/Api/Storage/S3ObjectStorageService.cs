using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

namespace SmartChoice.Api.Storage;

public sealed class S3ObjectStorageService(
    IAmazonS3 s3Client,
    ObjectStorageOptions options,
    ILogger<S3ObjectStorageService> logger) : IObjectStorageService
{
    private readonly IAmazonS3 _s3Client = s3Client;
    private readonly ObjectStorageOptions _options = options;
    private readonly ILogger<S3ObjectStorageService> _logger = logger;
    private readonly string _normalizedBucketName = options.BucketName.Trim();

    public async Task EnsureBucketReadyAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnsureBucketExistsOnStartup)
        {
            return;
        }

        var bucketExists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, _normalizedBucketName);
        if (!bucketExists)
        {
            await _s3Client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = _normalizedBucketName
            }, cancellationToken);

            _logger.LogInformation("Created object storage bucket {BucketName}.", _normalizedBucketName);
        }

        if (_options.MakeBucketPublicOnStartup)
        {
            _logger.LogWarning(
                "Object storage bucket {BucketName} is configured as public. This is not recommended for production.",
                _normalizedBucketName);
            return;
        }

        await EnsureBucketIsPrivateAsync(cancellationToken);
    }

    public async Task<StoredObject> UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var normalizedKey = key.Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new ArgumentException("Object key cannot be empty.", nameof(key));
        }

        if (!content.CanRead)
        {
            throw new ArgumentException("Upload stream must be readable.", nameof(content));
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var sizeBytes = content.CanSeek ? content.Length : 0;

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _normalizedBucketName,
            Key = normalizedKey,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = contentType
        }, cancellationToken);

        return new StoredObject(
            normalizedKey,
            contentType,
            sizeBytes);
    }

    public async Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken)
    {
        var normalizedKey = key.Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return;
        }

        try
        {
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _normalizedBucketName,
                Key = normalizedKey
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound
                                           || ex.ErrorCode is "NoSuchKey" or "NoSuchBucket")
        {
            // Object is already missing, no action needed.
        }
    }

    public Task<SignedObjectUrl> GetReadUrlAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedKey = key.Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            throw new ArgumentException("Object key cannot be empty.", nameof(key));
        }

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Signed URL TTL must be greater than zero.");
        }

        var expiresAtUtc = DateTime.UtcNow.Add(ttl);
        var url = _s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _normalizedBucketName,
            Key = normalizedKey,
            Verb = HttpVerb.GET,
            Expires = expiresAtUtc
        });

        return Task.FromResult(new SignedObjectUrl(normalizedKey, url, expiresAtUtc));
    }

    private async Task EnsureBucketIsPrivateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _s3Client.DeleteBucketPolicyAsync(
                new DeleteBucketPolicyRequest { BucketName = _normalizedBucketName },
                cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "NoSuchBucketPolicy" or "NoSuchBucket")
        {
            // Bucket has no policy yet or does not exist (handled earlier).
        }

        try
        {
            await _s3Client.PutPublicAccessBlockAsync(new PutPublicAccessBlockRequest
            {
                BucketName = _normalizedBucketName,
                PublicAccessBlockConfiguration = new PublicAccessBlockConfiguration
                {
                    BlockPublicAcls = true,
                    IgnorePublicAcls = true,
                    BlockPublicPolicy = true,
                    RestrictPublicBuckets = true
                }
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "NotImplemented" or "UnsupportedOperation")
        {
            // Some S3-compatible providers (or configurations) may not support PublicAccessBlock.
            _logger.LogInformation(
                "PublicAccessBlock is not supported by object storage provider. Bucket policy was still reset.");
        }
    }
}
