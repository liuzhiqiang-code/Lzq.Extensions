namespace Lzq.Extensions.AI.Services;

public class RagService
{
    public async Task TextSplittingAsync(string longText)
    {
        var list = SmartTextSplitter.Split(longText, chunkSize: 100, chunkOverlap: 100);
    }
}


public static class SmartTextSplitter
{
    // 定义层级分隔符：从段落到句子，再到单词
    private static readonly string[] DefaultSeparators = { "\r\n\r\n", "\n\n", "\n", "。", "！", "？", "；", " ", "" };

    public static List<string> Split(string text, int chunkSize, int chunkOverlap)
    {
        var result = new List<string>();
        RecursiveSplit(text, DefaultSeparators, chunkSize, chunkOverlap, result);
        return result;
    }

    private static void RecursiveSplit(string text, string[] separators, int max, int overlap, List<string> result)
    {
        if (text.Length <= max)
        {
            result.Add(text);
            return;
        }

        // 尝试找到当前最合适的切分符
        string sep = separators.FirstOrDefault(s => string.IsNullOrEmpty(s) || text.Contains(s)) ?? "";
        var parts = string.IsNullOrEmpty(sep)
            ? text.Select(c => c.ToString()).ToList()
            : text.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries).ToList();

        string current = "";
        foreach (var part in parts)
        {
            // 尝试合并小块
            if ((current + (current.Length > 0 ? sep : "") + part).Length <= max)
            {
                current = current.Length == 0 ? part : current + sep + part;
            }
            else
            {
                if (!string.IsNullOrEmpty(current)) result.Add(current);

                // 如果单块依然超长，进入下一级递归
                if (part.Length > max)
                {
                    RecursiveSplit(part, separators.Skip(1).ToArray(), max, overlap, result);
                }
                else
                {
                    current = part;
                }
            }
        }
        if (!string.IsNullOrEmpty(current)) result.Add(current);
    }
}