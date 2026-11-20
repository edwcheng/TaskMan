namespace TaskMan.Api.Contracts;

public sealed record ForumSummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    bool IsArchived);

public sealed record ForumResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    int SortOrder,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    int TaskCount,
    int OpenTaskCount,
    DateTimeOffset? LastActivityAt);

public sealed record CreateForumRequest(
    string? Name,
    string? Slug,
    string? Description,
    int? SortOrder,
    bool? IsArchived);

public sealed record UpdateForumRequest(
    string? Name,
    string? Slug,
    string? Description,
    int? SortOrder,
    bool? IsArchived);
