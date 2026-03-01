using SmartChoice.Domain.Enums;

namespace SmartChoice.Api.Contracts;

public sealed record PollPhotoDto(
    long Id,
    string PhotoUrl,
    string? ThumbnailUrl,
    byte DisplayOrder);

public sealed record PollDto(
    long Id,
    long AuthorUserId,
    string Question,
    PollStatus Status,
    double Latitude,
    double Longitude,
    int RadiusMeters,
    DateTime? StartsAt,
    DateTime? EndsAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyCollection<PollPhotoDto> Photos);

public sealed record FeedPollDto(
    long Id,
    string Question,
    DateTime CreatedAt,
    DateTime? EndsAt,
    double Latitude,
    double Longitude,
    int RadiusMeters,
    double DistanceMeters,
    IReadOnlyCollection<PollPhotoDto> Photos);

public sealed record FeedPageDto(
    int Page,
    int PageSize,
    bool HasNextPage,
    string Sort,
    IReadOnlyCollection<FeedPollDto> Items);

public sealed record VoteDto(
    long Id,
    long PollId,
    long PollPhotoId,
    long? VoterUserId,
    long? GuestTokenId,
    DateTime VotedAt);

public sealed record PollResultOptionDto(
    long PollPhotoId,
    string PhotoUrl,
    byte DisplayOrder,
    int VoteCount,
    decimal Percentage);

public sealed record PollWinnerDto(
    long PollPhotoId,
    string PhotoUrl,
    int VoteCount,
    decimal Percentage);

public sealed record PollResultsDto(
    long PollId,
    PollStatus Status,
    int TotalVotes,
    PollWinnerDto? Winner,
    IReadOnlyCollection<PollResultOptionDto> Options);
