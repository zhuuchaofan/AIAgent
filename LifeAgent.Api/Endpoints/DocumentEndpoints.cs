using System.Security.Claims;
using Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

namespace LifeAgent.Api.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/documents")
            .WithTags("documents");

        group.MapPost("/", UploadDocumentAsync).DisableAntiforgery().RequireRateLimiting("high-cost");
        group.MapGet("/", GetDocumentsAsync);
        group.MapDelete("/{documentId}", DeleteDocumentAsync);
    }

    public static async Task<IResult> UploadDocumentAsync(
        HttpContext httpContext,
        IFormFile file,
        [FromServices] FileValidator validator,
        [FromServices] ICloudStorageService storageService,
        [FromServices] IDocumentRepository repository,
        [FromServices] ICloudTasksService tasksService,
        [FromServices] ILogger<RestFirestoreVectorStore> logger)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        if (file == null || file.Length == 0)
        {
            return Results.BadRequest(new { success = false, message = "No file uploaded or file is empty." });
        }

        // 1. 校验大小与类型
        var (isValid, errorMsg) = validator.ValidateFile(file.FileName, file.Length, file.ContentType);
        if (!isValid)
        {
            return Results.BadRequest(new { success = false, message = errorMsg });
        }

        var documentId = "doc_" + Guid.NewGuid().ToString("N");

        // 2. 上传文件到 GCS
        string gcsPath;
        try
        {
            using var stream = file.OpenReadStream();
            gcsPath = await storageService.UploadFileAsync(userId, documentId, file.FileName, stream, file.ContentType);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GCS Upload failed for Document: {DocId}", documentId);
            return Results.Json(new { success = false, message = "Failed to upload file to Cloud Storage." }, statusCode: 500);
        }

        // 3. 写入 Firestore metadata 标记状态为 processing
        var document = new KnowledgeDocument
        {
            Id = documentId,
            UserId = userId,
            FileName = file.FileName,
            FileSize = file.Length,
            MimeType = file.ContentType,
            GcsPath = gcsPath,
            Status = "processing",
            ChunkCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            await repository.CreateAsync(document);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Firestore metadata creation failed for Document: {DocId}. Triggering cleanup...", documentId);
            // 事务性清理：数据库记录创建失败时，物理清除已上传的 GCS 文件
            try
            {
                await storageService.DeleteFileByPathAsync(gcsPath);
            }
            catch (Exception cleanEx)
            {
                logger.LogError(cleanEx, "Ingestion cleanup: Failed to delete GCS object {Path}", gcsPath);
            }
            
            return Results.Json(new { success = false, message = "Failed to create document metadata." }, statusCode: 500);
        }

        // 4. 投递 Cloud Tasks Ingestion Job
        try
        {
            await tasksService.EnqueueIngestTaskAsync(userId, documentId, gcsPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cloud Tasks dispatch failed for Document: {DocId}. Flagging failed status...", documentId);
            
            // 投递失败处理：更新状态为 failed 并清除 GCS 文件，保持事务一致
            document.Status = "failed";
            document.ErrorMessage = "Failed to dispatch asynchronous processing task.";
            
            try
            {
                await repository.UpdateAsync(document);
                await storageService.DeleteFileByPathAsync(gcsPath);
            }
            catch (Exception cleanEx)
            {
                logger.LogError(cleanEx, "Ingestion task cleanup error for Document: {DocId}", documentId);
            }

            return Results.Json(new { success = false, message = "Failed to schedule document processing job." }, statusCode: 500);
        }

        return Results.Accepted($"/api/v1/documents/{documentId}", new
        {
            success = true,
            documentId = documentId,
            status = "processing"
        });
    }

    public static async Task<IResult> GetDocumentsAsync(
        HttpContext httpContext,
        [FromServices] IDocumentRepository repository,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized" }, statusCode: 401);
        }

        var docs = await repository.ListAsync(userId, limit, cursor);
        return Results.Ok(new
        {
            success = true,
            data = docs
        });
    }

    public static async Task<IResult> DeleteDocumentAsync(
        HttpContext httpContext,
        string documentId,
        [FromServices] IDocumentRepository repository,
        [FromServices] ICloudStorageService storageService,
        [FromServices] ILogger<RestFirestoreVectorStore> logger)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized" }, statusCode: 401);
        }

        // 验证文档存在性与越权归属校验
        var doc = await repository.GetAsync(userId, documentId);
        if (doc == null)
        {
            return Results.NotFound(new { success = false, message = "Document not found or access denied." });
        }

        try
        {
            // 物理删除 GCS 中的源文件
            if (!string.IsNullOrEmpty(doc.GcsPath))
            {
                try
                {
                    await storageService.DeleteFileByPathAsync(doc.GcsPath);
                }
                catch (GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // 文件已被上游清理（如上传失败时的回滚），正常放行
                    logger.LogWarning("GCS file already deleted for Document {DocId}, skipping GCS cleanup: {GcsPath}", documentId, doc.GcsPath);
                }
            }

            // 删除元数据
            await repository.DeleteAsync(userId, documentId);
            
            _logger_LogInfo(logger, "Successfully deleted Document: {DocId} for User: {UserId}", documentId, userId);
            return Results.Ok(new { success = true, message = "Document successfully deleted." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete Document {DocId}", documentId);
            return Results.Json(new { success = false, message = "An error occurred while deleting the document." }, statusCode: 500);
        }
    }

    private static void _logger_LogInfo(ILogger logger, string message, string docId, string userId)
    {
        logger.LogInformation(message, docId, userId);
    }
}
