using System.Data;

namespace Lzq.Core.Interfaces;

public interface IUnitOfWork
{
    string? ContextID { get; }

    Task BeginTranAsync(IsolationLevel isolationLevel);
    Task CommitTranAsync();
    bool IsAnyTran();
    Task RollbackTranAsync();
}
