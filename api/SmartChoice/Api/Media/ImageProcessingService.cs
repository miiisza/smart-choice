using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace SmartChoice.Api.Media;

public sealed record ThumbnailResult(
    byte[] Content,
    string ContentType,
    int OriginalWidth,
    int OriginalHeight,
    int ThumbnailWidth,
    int ThumbnailHeight);

public sealed class ImageProcessingService
{
    public async Task<ThumbnailResult> CreateThumbnailAsync(
        Stream sourceStream,
        int maxWidth,
        CancellationToken cancellationToken)
    {
        if (maxWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Max thumbnail width must be greater than zero.");
        }

        if (sourceStream.CanSeek)
        {
            sourceStream.Position = 0;
        }

        try
        {
            using var image = await Image.LoadAsync(sourceStream, cancellationToken);
            var originalWidth = image.Width;
            var originalHeight = image.Height;

            image.Mutate(context =>
            {
                context.AutoOrient();
                context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxWidth, maxWidth)
                });
            });

            await using var thumbnailStream = new MemoryStream();
            await image.SaveAsJpegAsync(thumbnailStream, new JpegEncoder { Quality = 82 }, cancellationToken);

            return new ThumbnailResult(
                thumbnailStream.ToArray(),
                "image/jpeg",
                originalWidth,
                originalHeight,
                image.Width,
                image.Height);
        }
        catch (UnknownImageFormatException ex)
        {
            throw new InvalidDataException("Uploaded file is not a supported image.", ex);
        }
    }
}
