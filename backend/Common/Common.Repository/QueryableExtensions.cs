using Common.Domain.Lists;
using Microsoft.EntityFrameworkCore;

namespace Common.Repository;

public static class QueryableExtensions
{
    /// <summary>
    /// The list recipe of ArchitectureSimple.md §7.3: count → sort by property name → page.
    /// <paramref name="defaultSortBy"/> applies when the client sends no sort, so a page is never taken from an unordered set.
    /// </summary>
    public static async Task<PagedList<T>> ToSortedPagedList<T>(this IQueryable<T> query, IDynamicListParams listParams,
        string defaultSortBy, string? tieBreaker, CancellationToken ct) where T : class
    {
        var totalItems = await query.CountAsync(ct).ConfigureAwait(false);
        var (pageIndex, pageSize) = listParams.NormalizePaging();

        var items = await query
            .OrderByPropertyName(string.IsNullOrWhiteSpace(listParams.SortBy) ? defaultSortBy : listParams.SortBy, listParams.SortDirection, tieBreaker)
            .AddPagination(pageIndex, pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedList<T>(items, pageIndex, pageSize, totalItems);
    }
}
