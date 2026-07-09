namespace LifeAgent.Api.Services.Agent.PendingActions;

public static class PendingActionStatus
{
    public const string PreviewCreated = "preview_created";
    public const string ConfirmationRequired = "confirmation_required";
    public const string ConfirmationSubmitted = "confirmation_submitted";
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
    public const string ConfirmationBlocked = "confirmation_blocked";
    public const string ExecutionBlocked = "execution_blocked";
    public const string ExecutionReady = "execution_ready";
    public const string Executed = "executed";

    public static bool IsActive(string status)
    {
        return status is PreviewCreated or ConfirmationRequired or ConfirmationSubmitted or Confirmed;
    }

    public static bool IsTerminal(string status)
    {
        return status is Cancelled or Rejected or Expired or ConfirmationBlocked or ExecutionBlocked or Executed;
    }
}
