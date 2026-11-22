namespace TaskMan.Api.Contracts;

public sealed record ReplyResponse(
    Guid Id,
    string Body,
    UserSummaryResponse Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AttachmentResponse> Attachments);

public sealed record CreateReplyRequest(string? Body);

public sealed record UpdateReplyRequest(string? Body);

public sealed class ReplyListQuery
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 50;
}
