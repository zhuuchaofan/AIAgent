namespace LifeAgent.Api.Services.Agent;

public static class AgentIntentNames
{
    public const string LifeEvent = "life_event";
    public const string Memory = "memory";
    public const string Reminder = "reminder";
    public const string Rag = "rag";
    public const string Document = "document";
    public const string Unknown = "unknown";
}

public static class AgentActionTypes
{
    public const string CreateLifeEvent = "create_life_event";
    public const string CreateLifeEventPreview = "create_life_event_preview";
    public const string SaveMemoryPreview = "save_memory_preview";
    public const string CreateReminderPreview = "create_reminder_preview";
    public const string Reminder = "reminder_action";
    public const string ReadonlyRag = "preview_readonly_rag";
    public const string Document = "document_action";
    public const string Invalid = "invalid_action";
}

public static class AgentModes
{
    public const string PreviewConfirmation = "preview_confirmation";
    public const string PreviewReadonlyRag = "preview_readonly_rag";
    public const string PreviewContractError = "preview_contract_error";
}
