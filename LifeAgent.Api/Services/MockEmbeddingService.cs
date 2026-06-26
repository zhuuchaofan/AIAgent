using System.Security.Cryptography;
using System.Text;

namespace LifeAgent.Api.Services;

public class MockEmbeddingService : IEmbeddingService
{
    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var vec = new float[768];
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(vec);
        }

        // 用简单的 Hash 算子获得稳定的 seed 种子，保证同一文本生成的结果一模一样
        int seed = 0;
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            seed = BitConverter.ToInt32(bytes, 0);
        }

        var rng = new Random(seed);
        double norm = 0;
        for (int i = 0; i < 768; i++)
        {
            vec[i] = (float)(rng.NextDouble() * 2 - 1);
            norm += vec[i] * vec[i];
        }
        
        // 归一化处理（余弦相似度必须是归一化向量，模为 1）
        norm = Math.Sqrt(norm);
        if (norm > 0)
        {
            for (int i = 0; i < 768; i++)
            {
                vec[i] = (float)(vec[i] / norm);
            }
        }

        return Task.FromResult(vec);
    }
}
