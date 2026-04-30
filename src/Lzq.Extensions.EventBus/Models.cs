using MediatR;

namespace Lzq.Extensions.EventBus;

/// <summary>
/// 基础消息契约：事件标识与创建时间
/// </summary>
public interface IMessage
{
    /// <summary>
    /// 唯一标识（只读）
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// 创建时间（只读）
    /// </summary>
    DateTime CreateTime { get; }
}

/// <summary>
/// 命令接口：用于执行修改操作，返回特定结果
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>, IMessage { }

/// <summary>
/// 命令接口：用于执行修改操作，不返回结果
/// </summary>
public interface ICommand : IRequest, IMessage { }

/// <summary>
/// 查询接口：用于只读操作，必须返回结果
/// </summary>
public interface IQuery<out TResponse> : IRequest<TResponse>, IMessage { }

/// <summary>
/// 进程内事件接口：用于发布-订阅模式（多处理器）
/// </summary>
public interface ILocalEvent : INotification, IMessage { }



public abstract record BaseMessage 
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime CreateTime { get; init; } = DateTime.Now;
}
public abstract record BaseCommand : BaseMessage, ICommand
{
}

public abstract record BaseCommand<TResponse> : BaseMessage, ICommand<TResponse>
{
}

public abstract record BaseQuery<TResponse> : BaseMessage, IQuery<TResponse>
{
}

public abstract record BaseLocalEvent : BaseMessage, ILocalEvent
{
}