using System;
using Google.Cloud.Firestore;

namespace LifeAgent.Api.Models;

/// <summary>
/// 提醒事项实体，对应 Firestore 路径：users/{userId}/reminders/{reminderId}
/// </summary>
[FirestoreData]
public class Reminder
{
    [FirestoreDocumentId]
    public string Id { get; set; } = string.Empty;

    [FirestoreProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    [FirestoreProperty("sourceEventId")]
    public string SourceEventId { get; set; } = string.Empty;

    [FirestoreProperty("title")]
    public string Title { get; set; } = string.Empty;

    [FirestoreProperty("description")]
    public string? Description { get; set; }

    [FirestoreProperty("dueAt")]
    public DateTime DueAt { get; set; }

    [FirestoreProperty("timezone")]
    public string Timezone { get; set; } = string.Empty;

    /// <summary>状态：pending, completed, cancelled</summary>
    [FirestoreProperty("status")]
    public string Status { get; set; } = "pending";

    [FirestoreProperty("repeatRule")]
    public string RepeatRule { get; set; } = "none";

    [FirestoreProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [FirestoreProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [FirestoreProperty("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [FirestoreProperty("cancelledAt")]
    public DateTime? CancelledAt { get; set; }

    [FirestoreProperty("llmConfidence")]
    public double LlmConfidence { get; set; }

    [FirestoreProperty("rawText")]
    public string RawText { get; set; } = string.Empty;
}
