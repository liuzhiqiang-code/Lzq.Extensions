namespace Lzq.Core.Models;

public abstract record PagedRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}