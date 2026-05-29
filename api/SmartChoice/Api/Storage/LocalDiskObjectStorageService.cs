using System.Security.Cryptography;
using System.Text;

namespace SmartChoice.Api.Storage;

public sealed record LocalDiskReadObject(
    Stream Content,
    string ContentType);

public sealed class LocalDiskObjectStorageService(
    ObjectStorageOptions options,
    IWebHostEnvironment hostEnvironment,
    ILogger<LocalDiskObjectStorageService> logger) : IObjectStorageService
{
    private readonly ObjectStorageOptions _options = options;
    private readonly ILogger<LocalDiskObjectStorageService> _logger = logger;
    private readonly string _rootPath = ResolveRootPath(options.LocalDiskRootPath, hostEnvironment.ContentRootPath);
    private readonly string? _publicBaseUrl = NormalizeUrl(options.PublicBaseUrl);

    public Task EnsureBucketReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_rootPath);
        _logger.LogInformation("Local object storage root ready at {RootPath}.", _rootPath);
        return Task.CompletedTask;
    }

    public async Task<StoredObject> UploadAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeAndValidateKey(key);
        if (!content.CanRead)
        {
            throw new ArgumentException("Upload stream must be readable.", nameof(content));
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var fullPath = ResolveFullPath(normalizedKey);
        var directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var fileStream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            useAsync: true);
        await content.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);

        var sizeBytes = content.CanSeek ? content.Length : new FileInfo(fullPath).Length;
        return new StoredObject(normalizedKey, contentType, sizeBytes);
    }

    public Task DeleteIfExistsAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedKey;
        try
        {
            normalizedKey = NormalizeAndValidateKey(key);
        }
        catch (ArgumentException)
        {
            return Task.CompletedTask;
        }

        var fullPath = ResolveFullPath(normalizedKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<SignedObjectUrl> GetReadUrlAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Signed URL TTL must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(_publicBaseUrl))
        {
            throw new InvalidOperationException(
                "ObjectStorage:PublicBaseUrl must be configured when ObjectStorage:Provider=LocalDisk.");
        }

        var normalizedKey = NormalizeAndValidateKey(key);
        var expiresAtUtc = DateTime.UtcNow.Add(ttl);
        var expiresUnix = new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds();
        var signature = CreateSignature(normalizedKey, expiresUnix);
        var encodedKey = string.Join(
            '/',
            normalizedKey.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var signedUrl = $"{_publicBaseUrl}/api/storage/local/{encodedKey}?expires={expiresUnix}&sig={signature}";

        return Task.FromResult(new SignedObjectUrl(normalizedKey, signedUrl, expiresAtUtc));
    }

    public bool IsSignedReadRequestValid(string key, long expiresUnix, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (expiresUnix <= 0)
        {
            return false;
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (nowUnix > expiresUnix)
        {
            return false;
        }

        string normalizedKey;
        try
        {
            normalizedKey = NormalizeAndValidateKey(key);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var expectedSignature = CreateSignature(normalizedKey, expiresUnix);
        return FixedTimeEquals(signature, expectedSignature);
    }

    public Task<LocalDiskReadObject?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedKey;
        try
        {
            normalizedKey = NormalizeAndValidateKey(key);
        }
        catch (ArgumentException)
        {
            return Task.FromResult<LocalDiskReadObject?>(null);
        }

        var fullPath = ResolveFullPath(normalizedKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<LocalDiskReadObject?>(null);
        }

        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            useAsync: true);
        var contentType = ResolveContentType(fullPath);
        return Task.FromResult<LocalDiskReadObject?>(new LocalDiskReadObject(stream, contentType));
    }

    private string ResolveFullPath(string normalizedKey)
    {
        var combinedPath = Path.GetFullPath(
            Path.Combine(
                _rootPath,
                normalizedKey.Replace('/', Path.DirectorySeparatorChar)));

        if (!combinedPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Object key path escapes root directory.", nameof(normalizedKey));
        }

        return combinedPath;
    }

    private string NormalizeAndValidateKey(string key)
    {
        var normalized = (key ?? string.Empty).Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Object key cannot be empty.", nameof(key));
        }

        var segments = normalized
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("Object key cannot be empty.", nameof(key));
        }

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                throw new ArgumentException("Object key cannot contain relative path segments.", nameof(key));
            }
        }

        return string.Join('/', segments);
    }

    private string CreateSignature(string normalizedKey, long expiresUnix)
    {
        var signingSecret = _options.LocalDiskSigningSecret.Trim();
        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            throw new InvalidOperationException("ObjectStorage:LocalDiskSigningSecret must be configured.");
        }

        var payload = $"{normalizedKey}\n{expiresUnix}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return ToBase64Url(hash);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ResolveRootPath(string configuredPath, string contentRootPath)
    {
        var rawPath = string.IsNullOrWhiteSpace(configuredPath) ? "App_Data/object-storage" : configuredPath.Trim();
        var combined = Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.Combine(contentRootPath, rawPath);
        return Path.GetFullPath(combined);
    }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimEnd('/');
    }

    private static string ResolveContentType(string fullPath)
    {
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
