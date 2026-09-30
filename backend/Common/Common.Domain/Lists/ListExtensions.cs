using System.Linq.Expressions;
using System.Reflection;
using Common.Domain.Exceptions;

namespace Common.Domain.Lists;

/// <summary>
/// Dynamic sort and paging over IQueryable (ArchitectureSimple.md §7.3). Expression-tree based, so it translates
/// to SQL and also runs in memory. Nullable sort keys go last in both directions; an optional tie-breaker keeps
/// pages stable when many rows share the sort value.
/// </summary>
public static class ListExtensions
{
    public const int DefaultPageSize = 40;
    public const int MaxPageSize = 500;

    public static (int PageIndex, int PageSize) NormalizePaging(this IDynamicListParams listParams)
        => (Math.Max(0, listParams.PageIndex ?? 0), Math.Clamp(listParams.PageSize ?? DefaultPageSize, 1, MaxPageSize));

    public static IOrderedQueryable<T> OrderByPropertyName<T>(this IQueryable<T> source, string sortBy, SortDirection direction,
        string? tieBreaker = null)
    {
        var property = FindProperty<T>(sortBy)
                       ?? throw new ValidationException("sortBy", $"\"{sortBy}\" is not a sortable property of {typeof(T).Name}.");

        var ordered = ApplyOrder(source, property, direction, first: true);

        if (tieBreaker is not null && !string.Equals(tieBreaker, property.Name, StringComparison.OrdinalIgnoreCase))
        {
            var tie = FindProperty<T>(tieBreaker)
                      ?? throw new ArgumentException($"\"{tieBreaker}\" is not a property of {typeof(T).Name}.", nameof(tieBreaker));
            ordered = ApplyOrder(ordered, tie, direction, first: false);
        }

        return ordered;
    }

    public static IQueryable<T> AddPagination<T>(this IQueryable<T> source, int pageIndex, int pageSize)
        => source.Skip(pageIndex * pageSize).Take(pageSize);

    public static PagedList<T> AsPagedList<T>(this IEnumerable<T> items, IDynamicListParams listParams, int totalItems) where T : class
    {
        var (pageIndex, pageSize) = listParams.NormalizePaging();
        return new PagedList<T>(items.ToList(), pageIndex, pageSize, totalItems);
    }

    private static PropertyInfo? FindProperty<T>(string name)
        => typeof(T).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

    private static IOrderedQueryable<T> ApplyOrder<T>(IQueryable<T> source, PropertyInfo property, SortDirection direction, bool first)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var access = Expression.Property(parameter, property);
        var query = source;

        var nullable = !property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) is not null;
        if (nullable)
        {
            // nulls last in both directions: order by "is null" (false < true) before the value itself
            var isNull = Expression.Lambda(Expression.Equal(access, Expression.Constant(null, property.PropertyType)), parameter);
            query = Call(query, first ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy), typeof(bool), isNull);
            first = false;
        }

        var keySelector = Expression.Lambda(access, parameter);
        var ascending = direction == SortDirection.Asc;
        var method = first
            ? (ascending ? nameof(Queryable.OrderBy) : nameof(Queryable.OrderByDescending))
            : (ascending ? nameof(Queryable.ThenBy) : nameof(Queryable.ThenByDescending));

        return (IOrderedQueryable<T>)Call(query, method, property.PropertyType, keySelector);
    }

    private static IQueryable<T> Call<T>(IQueryable<T> source, string method, Type keyType, LambdaExpression keySelector)
        => source.Provider.CreateQuery<T>(Expression.Call(typeof(Queryable), method, [typeof(T), keyType], source.Expression, Expression.Quote(keySelector)));
}
