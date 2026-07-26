namespace DokployMonitor.Web.Models;

/// <summary>
/// Paging state shared by every list screen.
///
/// The pager only needs the current page, the page size and the total row count; the
/// rest (page count, offsets, whether there is a next page) is derived, so callers cannot
/// put it into an inconsistent state.
/// </summary>
public sealed record PageInfo
{
    /// <summary>Page sizes offered in the UI.</summary>
    public static readonly int[] AllowedSizes = [25, 50, 100, 200];

    public const int DefaultSize = 50;

    public required int Page { get; init; }
    public required int Size { get; init; }
    public required int TotalCount { get; init; }

    public int PageCount => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)Size);
    public int Skip => (Page - 1) * Size;
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < PageCount;
    public int FirstRow => TotalCount == 0 ? 0 : Skip + 1;
    public int LastRow => Math.Min(Skip + Size, TotalCount);

    /// <summary>Clamps user input: page and size always land inside valid bounds.</summary>
    public static PageInfo Create(int? page, int? size, int totalCount)
    {
        var pageSize = size is { } requested && AllowedSizes.Contains(requested) ? requested : DefaultSize;
        var pageCount = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PageInfo
        {
            Page = Math.Clamp(page ?? 1, 1, pageCount),
            Size = pageSize,
            TotalCount = totalCount,
        };
    }
}
