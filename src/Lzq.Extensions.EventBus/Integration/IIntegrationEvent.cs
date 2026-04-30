namespace Lzq.Extensions.EventBus.Integration;

/// <summary>
/// 集成事件：继承自本地事件，但会被管道拦截并持久化到发件箱
/// </summary>
public interface IIntegrationEvent : ILocalEvent 
{
    /// <summary>
    /// 消息所属的 Topic/Exchange 名称
    /// </summary>
    string TopicName { get; }
}

public abstract record BaseIntegrationEvent : BaseLocalEvent, IIntegrationEvent
{
    public virtual string TopicName => this.GetType().Name;
}