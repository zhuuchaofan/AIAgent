using System.Net;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace LifeAgent.Api.Endpoints;

public static class MigrationEndpoints
{
    public static void MapMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/migration");

        // POST /api/migration/run-phase2a0
        group.MapPost("/run-phase2a0", async (
            bool? dryRun,
            HttpContext ctx,
            FirestoreDb db,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("MigrationEndpoints");

            // 1. 安全检查：是否启用了迁移接口
            var enableMigration = Environment.GetEnvironmentVariable("ENABLE_MIGRATION_API");
            if (!string.Equals(enableMigration, "true", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("迁移接口调用被拦截：ENABLE_MIGRATION_API 未配置或不为 true");
                return Results.NotFound(); // 404
            }

            // 2. 安全检查：校验 Migration Secret
            var secretHeader = ctx.Request.Headers["X-Migration-Secret"].ToString();
            var migrationSecret = Environment.GetEnvironmentVariable("MIGRATION_SECRET");
            if (string.IsNullOrEmpty(migrationSecret) || !string.Equals(secretHeader, migrationSecret))
            {
                logger.LogWarning("迁移接口调用被拦截：X-Migration-Secret 缺失或不匹配");
                return Results.StatusCode((int)HttpStatusCode.Forbidden); // 403
            }

            // 3. 安全检查：校验用户授权
            var userId = ctx.Items["userId"] as string;
            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("迁移接口调用被拦截：userId 缺失，未通过 FirebaseAuth 中间件");
                return Results.Unauthorized(); // 401
            }

            logger.LogInformation("启动 Phase 2A-0 存量数据迁移，当前用户: {UserId}, dryRun: {DryRun}", userId, dryRun ?? false);

            var collection = db.Collection("users").Document(userId).Collection("life_events");
            var snapshot = await collection.GetSnapshotAsync();

            int scannedCount = 0;
            int migratedCount = 0;
            int wouldMigrateCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            var batch = db.StartBatch();
            int batchWriteCount = 0;

            foreach (var doc in snapshot.Documents)
            {
                try
                {
                    scannedCount++;

                    // 幂等性校验：如果文档已经拥有 isDeleted 属性，直接跳过
                    if (doc.ContainsField("isDeleted"))
                    {
                        skippedCount++;
                        continue;
                    }

                    // 提取并补齐 updatedAt 初始值：优先取 createdAt，次取 CreateTime，最后取当前 UTC 时间
                    DateTime? createdAtVal = null;
                    if (doc.ContainsField("createdAt"))
                    {
                        try
                        {
                            createdAtVal = doc.GetValue<DateTime>("createdAt");
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "文档 {DocId} 的 createdAt 字段解析失败", doc.Id);
                        }
                    }

                    var updatedAtVal = createdAtVal ?? doc.CreateTime?.ToDateTime() ?? DateTime.UtcNow;

                    if (dryRun == true)
                    {
                        wouldMigrateCount++;
                        continue;
                    }

                    // 加入 WriteBatch 增量写入
                    batch.Update(doc.Reference, new Dictionary<string, object?>
                    {
                        { "isDeleted", false },
                        { "updatedAt", updatedAtVal.ToUniversalTime() },
                        { "deletedAt", null }
                    });

                    migratedCount++;
                    batchWriteCount++;

                    // 每批最多 500 writes 物理限制
                    if (batchWriteCount >= 500)
                    {
                        logger.LogInformation("分批写入：提交 500 条修改...");
                        await batch.CommitAsync();
                        batch = db.StartBatch();
                        batchWriteCount = 0;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "迁移文档 {DocId} 时发生异常", doc.Id);
                    failedCount++;
                }
            }

            // 提交剩余修改
            if (batchWriteCount > 0 && dryRun != true)
            {
                logger.LogInformation("分批写入：提交剩余 {Count} 条修改...", batchWriteCount);
                await batch.CommitAsync();
            }

            logger.LogInformation("Phase 2A-0 迁移完成：scanned={Scanned}, migrated={Migrated}, wouldMigrate={WouldMigrate}, skipped={Skipped}, failed={Failed}",
                scannedCount, migratedCount, wouldMigrateCount, skippedCount, failedCount);

            return Results.Ok(new MigrationResponse
            {
                Success = true,
                ScannedCount = scannedCount,
                MigratedCount = migratedCount,
                WouldMigrateCount = wouldMigrateCount,
                SkippedCount = skippedCount,
                FailedCount = failedCount
            });
        });
    }
}

public class MigrationResponse
{
    public bool Success { get; set; }
    public int ScannedCount { get; set; }
    public int MigratedCount { get; set; }
    public int WouldMigrateCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
}
