using System;
using System.Text.RegularExpressions;

namespace LifeAgent.Api.Services;

/// <summary>
/// LLM 响应清洗与提取公共帮助类
/// </summary>
public static class LlmHelper
{
    /// <summary>
    /// 清洗 LLM 返回的原始响应文本，提取出首尾花括号包裹的合法 JSON 对象字符串。
    /// </summary>
    /// <param name="raw">大模型输出的原始字符串</param>
    /// <returns>仅包含 JSON 对象的子字符串，若无则返回空字符串</returns>
    public static string ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // 1. 剥离 Markdown 代码块标记（如 ```json 和 ```）
        string cleaned = raw.Trim();
        if (cleaned.StartsWith("```"))
        {
            var match = Regex.Match(cleaned, @"^```(?:json)?\s*(.*?)\s*```$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success)
            {
                cleaned = match.Groups[1].Value.Trim();
            }
        }

        // 2. 截取字符串中第一个 '{' 到最后一个 '}' 之间的内容
        int firstBrace = cleaned.IndexOf('{');
        int lastBrace = cleaned.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace >= firstBrace)
        {
            return cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return string.Empty;
    }

    /// <summary>
    /// 递归将 System.Text.Json.JsonElement 转换为原始 .NET 类型 (string, long, double, bool, dict, list 等)，
    /// 防止 Google Cloud Firestore Serialization 报错。
    /// </summary>
    public static object? ConvertJsonElement(object value)
    {
        if (value is System.Text.Json.JsonElement je)
        {
            switch (je.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    return je.GetString();
                case System.Text.Json.JsonValueKind.Number:
                    if (je.TryGetInt64(out long l)) return l;
                    return je.GetDouble();
                case System.Text.Json.JsonValueKind.True:
                    return true;
                case System.Text.Json.JsonValueKind.False:
                    return false;
                case System.Text.Json.JsonValueKind.Null:
                    return null;
                case System.Text.Json.JsonValueKind.Object:
                    var dict = new Dictionary<string, object?>();
                    foreach (var prop in je.EnumerateObject())
                    {
                        dict[prop.Name] = ConvertJsonElement(prop.Value);
                    }
                    return dict;
                case System.Text.Json.JsonValueKind.Array:
                    var list = new List<object?>();
                    foreach (var item in je.EnumerateArray())
                    {
                        list.Add(ConvertJsonElement(item));
                    }
                    return list;
            }
        }
        return value;
    }
}
