using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Google.Apis.Auth;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

namespace LifeAgent.Api.Endpoints;

public class IngestionException : Exception
{
    public IngestionException(string message) : base(message) { }
}

public static class InternalDocumentEndpoints
{
    public static void MapInternalDocumentEndpoints(this WebApplication app)
    {
        // 注册私有后台回调端点
        app.MapPost("/internal/api/v1/documents/process", ProcessDocumentAsync);
    }

    public static async Task<IResult> ProcessDocumentAsync(
        HttpContext httpContext,
        ProcessDocumentRequest request,
        [FromServices] IDocumentRepository repository,
        [FromServices] ICloudStorageService storage,
        [FromServices] IDocumentTextExtractor extractor,
        [FromServices] IChunker chunker,
        [FromServices] IEmbeddingService embeddingService,
        [FromServices] IFirestoreVectorStore vectorStore,
        [FromServices] IWebHostEnvironment env,
        [FromServices] IOptions<RagOptions> ragOptions,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("InternalDocumentEndpoints");
        var options = ragOptions.Value;

        // 1. OIDC Token 安全验证框架
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Unauthorized: Missing or invalid Authorization header scheme.");
            return Results.Json(new { success = false, message = "Missing or invalid bearer token." }, statusCode: 401);
        }

        var token = authHeader.Substring(7).Trim();

        // 区分开发环境与生产环境的验签逻辑
        if (env.IsDevelopment() && token == "dev-token")
        {
            logger.LogInformation("Development environment: Bypassed real OIDC verification via dev-token.");
        }
        else
        {
            try
            {
                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { options.InternalProcessAudience }
                };

                // 使用 Google 原生 SDK 进行 OIDC 令牌的安全验签
                var payload = await GoogleJsonWebSignature.ValidateAsync(token, settings);
                logger.LogInformation("OIDC Token signature and audience validated successfully. Issuer={Iss}, Subject={Sub}", payload.Issuer, payload.Subject);
            }
            catch (Google.Apis.Auth.InvalidJwtException ex)
            {
                logger.LogWarning(ex, "Unauthorized: Google OIDC Token verification failed.");
                return Results.Json(new { success = false, message = "OIDC Token validation failed." }, statusCode: 401);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Internal error validating Google OIDC token.");
                return Results.Json(new { success = false, message = "Authentication service error." }, statusCode: 500);
            }
        }

        // 2. 零信任 Payload 结构与逻辑完整性校验
        if (request == null || string.IsNullOrEmpty(request.DocumentId) || string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.GcsPath))
        {
            logger.LogWarning("Worker Ingestion: Received invalid empty request payload.");
            return Results.BadRequest(new { success = false, message = "Required parameters (documentId, userId, gcsPath) are missing." });
        }

        // A. 跨租户越权回查：根据 documentId 和 userId 严格查询元数据
        var doc = await repository.GetAsync(request.UserId, request.DocumentId);
        if (doc == null)
        {
            logger.LogWarning("Audit Log Alert: Document {DocId} does not exist or does not belong to User {UserId}.", request.DocumentId, request.UserId);
            return Results.Json(new { success = false, message = "Document not found or ownership mismatch." }, statusCode: 403);
        }

        // B. GCS 隔离路径前缀防伪造校验
        var expectedPrefix = $"users/{request.UserId}/documents/{request.DocumentId}/";
        
        string objectPath;
        try
        {
            var (_, parsedObjectPath) = GoogleCloudStorageService.ParseGcsPath(request.GcsPath);
            objectPath = parsedObjectPath;
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Audit Log Alert: Invalid GCS path format submitted: {Path}", request.GcsPath);
            return Results.BadRequest(new { success = false, message = "Invalid GCS path structure." });
        }

        if (!objectPath.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            logger.LogWarning("Audit Log Alert: Tenant Isolation Bypass Attempt! User {UserId} submitted GCS Path {GcsPath} which violates the expected path prefix: {Expected}",
                request.UserId, request.GcsPath, expectedPrefix);
            return Results.Json(new { success = false, message = "Access denied: GCS path structure validation failed." }, statusCode: 403);
        }

        // C. 文档状态流转控制
        if (doc.Status != "processing")
        {
            if (doc.Status == "success")
            {
                logger.LogInformation("Worker Ingestion: Document {DocId} is already in 'success' status. Skipping execution.", request.DocumentId);
                return Results.Ok(new { success = true, message = "Document already processed successfully." });
            }
            logger.LogWarning("Worker Ingestion: Ignored process request. Document {DocId} is in status '{Status}', only 'processing' state can be digested.",
                request.DocumentId, doc.Status);
            return Results.BadRequest(new { success = false, message = $"Conflict: Document is in state '{doc.Status}'." });
        }

        logger.LogInformation("All checks passed. Starting real Ingestion Pipeline for Document {DocId}", request.DocumentId);

        try
        {
            // 为防止重复任务或重试脏数据覆盖，前置清理该文档历史所有的 chunks
            await vectorStore.DeleteChunksByDocumentIdAsync(request.UserId, request.DocumentId);

            // 1. 从 GCS 读取文件流
            using Stream fileStream = await storage.DownloadFileByPathAsync(request.GcsPath);

            // 2. 调用 IDocumentTextExtractor 抽取文本
            var extractionResult = await extractor.ExtractTextAsync(fileStream, doc.MimeType);
            if (!extractionResult.Success)
            {
                throw new IngestionException(extractionResult.ErrorMessage ?? "Failed to extract text from document.");
            }

            // 3. 调用 IChunker 生成 chunks（Phase 3.5: 应用最大 chunk 数量限制）
            var maxChunks = options.MaxChunksPerDocument > 0 ? options.MaxChunksPerDocument : 200;

            // 传入 maxChunks + 1 探测文档是否真正超限：
            //   - 返回数量 > maxChunks → 文档确实超限，需要截断
            //   - 返回数量 == maxChunks → 恰好等于上限，不截断
            //   - 返回数量 < maxChunks → 未达上限，不截断
            var allChunks = chunker.SplitDocument(request.UserId, request.DocumentId, doc.FileName, extractionResult.Pages, maxChunks + 1);

            var isTruncated = allChunks.Count > maxChunks;

            if (isTruncated)
            {
                logger.LogWarning("Document {DocId} generated {TotalChunks} chunks, exceeding the limit of {Limit}. Truncating.",
                    request.DocumentId, allChunks.Count, maxChunks);
            }

            // 截断时只保留前 maxChunks 个，后续 chunk 不做 embedding、不写入
            var chunks = isTruncated ? allChunks.Take(maxChunks).ToList() : allChunks;

            if (chunks == null || chunks.Count == 0)
            {
                throw new IngestionException("No valid content or chunks could be generated from the document.");
            }

            // 4. 调用 IEmbeddingService 生成 768 维向量
            var embeddings = new List<float[]>();
            foreach (var chunk in chunks)
            {
                var embedding = await embeddingService.GenerateEmbeddingAsync(chunk.Content);
                if (embedding == null)
                {
                    throw new IngestionException("Embedding service returned null vector.");
                }
                if (embedding.Length != 768)
                {
                    throw new IngestionException($"Expected 768 dimensions embedding, but got {embedding.Length}.");
                }
                embeddings.Add(embedding);
            }

            // 5. 调用 RestFirestoreVectorStore 写入 chunks + vectors
            await vectorStore.WriteChunksAsync(request.UserId, chunks, embeddings);

            // 6. 更新 document metadata 为 success
            doc.Status = "success";
            doc.ChunkCount = chunks.Count;
            doc.IsTruncated = isTruncated;
            doc.UpdatedAt = DateTime.UtcNow;
            doc.ErrorMessage = null;

            await repository.UpdateAsync(doc);
            logger.LogInformation("Ingestion succeeded. Document {DocId} is now 'success'", request.DocumentId);

            return Results.Ok(new { success = true, message = "Document successfully ingested.", chunkCount = chunks.Count });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingestion Pipeline failed for Document {DocId}.", request.DocumentId);

            // 失败时 errorMessage 脱敏
            string desensitizedError = ex is IngestionException 
                ? ex.Message 
                : "An unexpected internal error occurred during document parsing or vector storage.";

            // 发生异常时，状态更新为 failed
            doc.Status = "failed";
            doc.ErrorMessage = desensitizedError;
            doc.UpdatedAt = DateTime.UtcNow;

            try
            {
                await repository.UpdateAsync(doc);
            }
            catch (Exception dbEx)
            {
                logger.LogError(dbEx, "Failed to update document status to 'failed' in database for Document {DocId}", request.DocumentId);
            }

            // 尽量清理已落库的 chunks，避免脏向量残留
            try
            {
                await vectorStore.DeleteChunksByDocumentIdAsync(request.UserId, request.DocumentId);
            }
            catch (Exception cleanEx)
            {
                logger.LogError(cleanEx, "Failed to perform cascade cleanup of chunks for Document {DocId}", request.DocumentId);
            }

            return Results.Json(new { success = false, message = desensitizedError }, statusCode: 500);
        }
    }
}

public class ProcessDocumentRequest
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("gcsPath")]
    public string GcsPath { get; set; } = "";
}
