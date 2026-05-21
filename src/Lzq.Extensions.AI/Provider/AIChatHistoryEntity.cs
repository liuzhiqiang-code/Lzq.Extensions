using Lzq.Extensions.SqlSugar.Entities;
using SqlSugar;

namespace Lzq.Extensions.AI.Provider;

[Tenant("AgentForge"), SugarTable("ai_chat_history")]
public class AIChatHistoryEntity : BaseFullEntity
{
    [SugarColumn(ColumnName = "key")]
    public string? Key { get; set; }

    [SugarColumn(ColumnName = "session_id")]
    public string? SessionId { get; set; }

    [SugarColumn(ColumnName = "turn_id")]
    public int? TurnId { get; set; }

    [SugarColumn(ColumnName = "role")]
    public string Role { get; internal set; }

    [SugarColumn(ColumnName = "content", ColumnDataType = "text", IsJson = true)]
    public List<StreamingEventArgs>? Content { get; set; }

    [SugarColumn(ColumnName = "serialized_message", ColumnDataType = "text")]
    public string? SerializedMessage { get; set; }
}
