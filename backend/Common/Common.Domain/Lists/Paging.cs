using Common.Domain.Requests;

namespace Common.Domain.Lists;

// Paging, sorting and filtering contract shared with clients (ArchitectureSimple.md §5.2, §13.2).

public enum SortDirection
{
    Asc,
    Desc,
}

public interface IDynamicListParams
{
    int? PageIndex { get; }          // zero-based
    int? PageSize { get; }
    string? SortBy { get; }          // property name of the Model, case-insensitive
    SortDirection SortDirection { get; }
    string? SearchParam { get; }
}

public abstract record DynamicListQuery<TResponse> : IRequest<TResponse>, IDynamicListParams
{
    public int? PageIndex { get; init; }
    public int? PageSize { get; init; }
    public string? SortBy { get; init; }
    public SortDirection SortDirection { get; init; }
    public string? SearchParam { get; init; }
}

public sealed record PagedList<T>(IReadOnlyCollection<T> Items, int PageIndex, int PageSize, int TotalItems) where T : class;
