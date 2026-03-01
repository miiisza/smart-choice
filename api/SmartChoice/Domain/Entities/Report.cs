using SmartChoice.Domain.Enums;
using SmartChoice.Domain.Exceptions;

namespace SmartChoice.Domain.Entities;

public sealed class Report
{
    public long Id { get; private set; }
    public long PollId { get; private set; }
    public long? ReporterUserId { get; private set; }
    public long? ReporterGuestTokenId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string? Details { get; private set; }
    public ReportStatus Status { get; private set; } = ReportStatus.Open;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; private set; }

    public Poll? Poll { get; private set; }
    public User? ReporterUser { get; private set; }
    public GuestToken? ReporterGuestToken { get; private set; }

    private Report()
    {
    }

    private Report(long pollId, long? reporterUserId, long? reporterGuestTokenId, string reasonCode, string? details)
    {
        if (pollId <= 0)
        {
            throw new DomainValidationException("PollId must be greater than zero.");
        }

        var hasUser = reporterUserId.HasValue;
        var hasGuest = reporterGuestTokenId.HasValue;
        if (hasUser == hasGuest)
        {
            throw new DomainValidationException("Report must be filed by either a user or a guest token.");
        }

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new DomainValidationException("ReasonCode is required.");
        }

        PollId = pollId;
        ReporterUserId = reporterUserId;
        ReporterGuestTokenId = reporterGuestTokenId;
        ReasonCode = reasonCode.Trim().ToUpperInvariant();
        Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        Status = ReportStatus.Open;
        CreatedAt = DateTime.UtcNow;
    }

    public static Report CreateByUser(long pollId, long reporterUserId, string reasonCode, string? details = null)
    {
        if (reporterUserId <= 0)
        {
            throw new DomainValidationException("ReporterUserId must be greater than zero.");
        }

        return new Report(pollId, reporterUserId, null, reasonCode, details);
    }

    public static Report CreateByGuest(long pollId, long reporterGuestTokenId, string reasonCode, string? details = null)
    {
        if (reporterGuestTokenId <= 0)
        {
            throw new DomainValidationException("ReporterGuestTokenId must be greater than zero.");
        }

        return new Report(pollId, null, reporterGuestTokenId, reasonCode, details);
    }

    public void SetStatus(ReportStatus status)
    {
        Status = status;
        if (status != ReportStatus.Open)
        {
            ReviewedAt = DateTime.UtcNow;
        }
    }
}
