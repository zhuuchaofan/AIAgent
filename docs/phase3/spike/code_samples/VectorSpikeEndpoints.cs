// ┌──────────────────────────────────────────────────────────────────────┐
// │  SPIKE FILE — Phase 3 Step 0 技术验证                                │
// │  目的：暴露隐藏 debug 端点，仅在 Development 环境注册                   │
// │  完成后此文件应被删除，绝不允许合并进正式业务逻辑。                      │
// └──────────────────────────────────────────────────────────────────────┘

using LifeAgent.Api.Spike;

namespace LifeAgent.Api.Spike;

/// <summary>
/// Phase 3 Step 0 Spike — 隐藏 Debug Endpoints
/// 仅在 Development 环境下通过 Program.cs 注册
/// 端点前缀：/debug/spike/
/// </summary>
public static class VectorSpikeEndpoints
{
    public static void MapVectorSpikeEndpoints(this WebApplication app)
    {
        // 安全守卫：只在 Development 模式下注册
        if (!app.Environment.IsDevelopment())
            return;

        var group = app.MapGroup("/debug/spike")
            .WithTags("spike");

        // ─────────────────────────────────────────────────────────────
        // POST /debug/spike/vector-write
        // 验证点 1 & 2：写入两个 768 维 VectorValue 测试文档
        // ─────────────────────────────────────────────────────────────
        group.MapPost("/vector-write", async (
            VectorSpikeService spikeSvc) =>
        {
            var result = await spikeSvc.WriteTestChunksAsync();
            return result.WriteSuccess
                ? Results.Ok(new
                {
                    success = true,
                    message = "✅ VectorValue 写入成功",
                    details = result
                })
                : Results.Problem(
                    detail: result.ErrorMessage,
                    title: "❌ VectorValue 写入失败",
                    statusCode: 500);
        });

        // ─────────────────────────────────────────────────────────────
        // GET /debug/spike/sdk-get
        // 验证点 1.5：SDK 直接 GET 含有原生向量的文档时字段的反序列化表现
        // ─────────────────────────────────────────────────────────────
        group.MapGet("/sdk-get", async (
            VectorSpikeService spikeSvc) =>
        {
            var res = await spikeSvc.GetTestChunkViaSdkAsync();
            return Results.Ok(new { message = res });
        });

        // ─────────────────────────────────────────────────────────────
        // GET /debug/spike/sdk-search
        // 验证点 3：SDK FindNearest Top-5 COSINE 检索
        // ─────────────────────────────────────────────────────────────
        group.MapGet("/sdk-search", async (
            VectorSpikeService spikeSvc) =>
        {
            var result = await spikeSvc.SdkFindNearestAsync();
            return result.Success
                ? Results.Ok(new
                {
                    success = true,
                    message = $"✅ SDK FindNearest 返回 {result.ReturnedCount} 条结果",
                    details = result
                })
                : Results.Problem(
                    detail: result.ErrorMessage,
                    title: "❌ SDK FindNearest 失败",
                    statusCode: 500);
        });

        // ─────────────────────────────────────────────────────────────
        // GET /debug/spike/rest-query
        // 验证点 4：REST runQuery/findNearest 端点路径与 payload 格式
        // ─────────────────────────────────────────────────────────────
        group.MapGet("/rest-query", async (
            VectorSpikeService spikeSvc) =>
        {
            var result = await spikeSvc.RestFindNearestAsync();
            return Results.Ok(new
            {
                success    = result.RestApiSuccess,
                message    = result.RestApiSuccess
                               ? $"✅ REST runQuery 成功，解析到 {result.ParsedHits.Count} 条 Hit"
                               : $"⚠️ REST 请求 HTTP {result.HttpStatusCode}",
                actualUrl        = result.ActualUrl,
                parentPath       = result.ParentPath,
                httpStatus       = result.HttpStatusCode,
                tokenObtained    = result.TokenObtained,
                hits             = result.ParsedHits,
                // 注意：payload 可能含 768 个浮点数，截断显示前 200 字符
                payloadPreview   = result.ActualRequestPayload.Length > 200
                                     ? result.ActualRequestPayload[..200] + "...(截断)"
                                     : result.ActualRequestPayload,
                // 原始响应，用于 snapshot testing 对齐
                rawResponsePreview = result.ActualResponseJson.Length > 2000
                                       ? result.ActualResponseJson[..2000] + "...(截断)"
                                       : result.ActualResponseJson,
                error = result.ErrorMessage
            });
        });

        // ─────────────────────────────────────────────────────────────
        // DELETE /debug/spike/cleanup
        // 清理 Spike 写入的测试数据
        // ─────────────────────────────────────────────────────────────
        group.MapDelete("/cleanup", async (
            VectorSpikeService spikeSvc) =>
        {
            var ok = await spikeSvc.CleanupTestDataAsync();
            return ok
                ? Results.Ok(new { success = true, message = "✅ Spike 测试数据已清理" })
                : Results.Problem(title: "❌ 测试数据清理失败", statusCode: 500);
        });

        // ─────────────────────────────────────────────────────────────
        // GET /debug/spike/full-run
        // 一键完整 Spike 验证：写入 → SDK 检索 → REST 检索 → 汇总报告
        // ─────────────────────────────────────────────────────────────
        group.MapGet("/full-run", async (
            VectorSpikeService spikeSvc) =>
        {
            var report = new SpikeSummaryReport
            {
                StartedAt = DateTime.UtcNow.ToString("O")
            };

            // Step 1: 写入测试数据
            report.WriteResult = await spikeSvc.WriteTestChunksAsync();

            // Step 2: SDK FindNearest
            if (report.WriteResult.WriteSuccess)
            {
                // 等待 Firestore 稍微 propagate（向量索引写入延迟）
                await Task.Delay(2000);
                report.SdkResult = await spikeSvc.SdkFindNearestAsync();
            }

            // Step 3: REST runQuery
            report.RestResult = await spikeSvc.RestFindNearestAsync();

            report.FinishedAt = DateTime.UtcNow.ToString("O");
            report.OverallSuccess =
                report.WriteResult.WriteSuccess &&
                report.SdkResult?.Success == true;

            report.Recommendation = report.OverallSuccess
                ? "✅ Spike 全部验证通过，建议进入 Phase 3 正式开发"
                : "❌ Spike 存在失败项，请检查详情后决策";

            return Results.Ok(report);
        });
    }
}

// ─────────────────────────────────────────────────────────────────────
// Spike 汇总报告 DTO（仅用于 Spike）
// ─────────────────────────────────────────────────────────────────────

public class SpikeSummaryReport
{
    public string StartedAt { get; set; } = "";
    public string FinishedAt { get; set; } = "";
    public bool OverallSuccess { get; set; }
    public string Recommendation { get; set; } = "";
    public SpikeWriteResult? WriteResult { get; set; }
    public SpikeFindNearestResult? SdkResult { get; set; }
    public SpikeRestResult? RestResult { get; set; }
}
