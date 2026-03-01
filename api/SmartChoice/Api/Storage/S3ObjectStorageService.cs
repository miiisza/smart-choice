using System.Text.Json;
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
    private readonly string? _normalizedPublicBaseUrl = NormalizeUrl(options.PublicBaseUrl ?? options.ServiceUrl);

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
            var policyJson = JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new[]
                {
                    new
                    {
                        Sid = "AllowPublicRead",
                        Effect = "Allow",
                        Principal = "*",
                        Action = "s3:GetObject",
                        Resource = $"arn:aws:s3:::{_normalizedBucketName}/*"
                    }
                }
            });

            await _s3Client.PutBucketPolicyAsync(new PutBucketPolicyRequest
            {
                BucketName = _normalizedBucketName,
                Policy = policyJson
            }, cancellationToken);
        }
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
            BuildPublicObjectUrl(normalizedKey),
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

    private string BuildPublicObjectUrl(string key)
    {
        var encodedKey = string.Join('/',
            key.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        if (!string.IsNullOrWhiteSpace(_normalizedPublicBaseUrl))
        {
            return $"{_normalizedPublicBaseUrl}/{_normalizedBucketName}/{encodedKey}";
        }

        return $"https://{_normalizedBucketName}.s3.{_options.Region}.amazonaws.com/{encodedKey}";
    }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimEnd('/');
    }
}
