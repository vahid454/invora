namespace Invora.Contracts.Common;

public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalItems)
{
    public long TotalPages => (TotalItems + PageSize - 1) / PageSize;
}
