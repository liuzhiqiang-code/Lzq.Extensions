namespace Lzq.Core.Models;

public record PagedResponse<TEntity>(
    List<TEntity> Items,
    long Total
)
{
}