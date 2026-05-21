namespace Lzq.Extensions.AI.Consts;

public class ChatClientConst
{
    //public static readonly AISetting DeepSeekChat = new AISetting
    //{
    //    ConfigId = "DeepSeekChat",
    //    Url = "https://api.deepseek.com/v1",
    //    KeySecret = "sk-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
    //    Model = "deepseek-chat",
    //};

    // 系统内置硅基流动DeepSeek模型
    public static readonly AISetting DeepSeek_V4_Flash = new AISetting
    {
        ConfigId = "DeepSeek_V4_Flash",
        Url = "https://api.siliconflow.cn/v1",
        KeySecret = "sk-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
        Model = "deepseek-ai/DeepSeek-V4-Flash",
    };
    public static readonly AISetting DeepSeek_V32 = new AISetting
    {
        ConfigId = "DeepSeek_V32",
        Url = "https://api.siliconflow.cn/v1",
        KeySecret = "sk-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
        Model = "deepseek-ai/DeepSeek-V3.2",
    };
    public static readonly AISetting MiniMaxM25 = new AISetting
    {
        ConfigId = "MiniMaxM25",
        Url = "https://api.siliconflow.cn/v1",
        KeySecret = "sk-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
        Model = "MiniMaxAI/MiniMax-M2.5", // 模型名称以官方为准
    };
}