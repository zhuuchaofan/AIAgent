using System.Collections.Generic;
using System.Threading.Tasks;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class MockRagAnswerGenerator : IRagAnswerGenerator
{
    public Task<string> GenerateAnswerAsync(string systemInstruction, string userPrompt, List<ChatMessage> history)
    {
        if (userPrompt.Contains("越界") || userPrompt.Contains("out of bounds"))
        {
            return Task.FromResult("根据您的训练计划 [1]，您应该训练间歇骑行。但这里有一个不存在的引用 [99]。");
        }
        if (userPrompt.Contains("事实") || userPrompt.Contains("no citation"))
        {
            return Task.FromResult("您下周二的训练项目是 18km 间歇有氧耐力骑行。我完全没有使用任何标号引用。");
        }
        if (userPrompt.Contains("部分") || userPrompt.Contains("partial"))
        {
            return Task.FromResult("第一部分是骑行 18km [1]。第二部分是有氧慢跑，但我没有写它的引用。");
        }
        if (userPrompt.Contains("拒答") || userPrompt.Contains("抱歉"))
        {
            return Task.FromResult("抱歉，在您上传的个人资料中，我没有找到相关信息来回答该问题。");
        }

        return Task.FromResult("根据您上传的《2026年骑行训练计划.pdf》，下周二您的训练项目是 **「18km 间歇有氧耐力骑行」** [1]。建议在骑行中平均心率控制在 140-150 bpm 区间 [2]。");
    }
}
