namespace LifeAgent.Api.Services.Agent.PendingActions;

public static class PendingActionTransitionPolicy
{
    public static PendingActionStoreResult? ValidateStatusUpdate(
        PendingActionRecord record,
        PendingActionStatusUpdate update)
    {
        if (record.Status != update.ExpectedStatus)
        {
            return PendingActionStoreResult.Failed(
                record.Status,
                "status_mismatch",
                "Pending action status did not match expected status.");
        }

        return ValidateOwnedStatusChange(record, update.NewStatus);
    }

    public static PendingActionStoreResult? ValidateTargetStatus(
        PendingActionRecord record,
        string newStatus)
    {
        if (newStatus == PendingActionStatus.Executed)
        {
            return PendingActionStoreResult.Failed(
                record.Status,
                "execution_not_enabled",
                "Pending action store cannot mark records as executed.");
        }

        if (record.Status == PendingActionStatus.Cancelled &&
            newStatus == PendingActionStatus.Confirmed)
        {
            return PendingActionStoreResult.Failed(
                record.Status,
                "cancelled_cannot_confirm",
                "Cancelled pending action cannot be confirmed.");
        }

        return null;
    }

    public static PendingActionStoreResult? ValidateOwnedStatusChange(
        PendingActionRecord record,
        string newStatus)
    {
        if (IsLocked(record.Status))
        {
            return PendingActionStoreResult.Failed(
                record.Status,
                "terminal_status",
                "Finalized pending action cannot change status.");
        }

        return ValidateTargetStatus(record, newStatus);
    }

    public static PendingActionStoreResult? ValidateMutableMetadataUpdate(PendingActionRecord record)
    {
        if (IsLocked(record.Status))
        {
            return PendingActionStoreResult.Failed(
                record.Status,
                "terminal_status",
                "Finalized pending action cannot change metadata.");
        }

        return null;
    }

    private static bool IsLocked(string status)
    {
        return status == PendingActionStatus.Confirmed || PendingActionStatus.IsTerminal(status);
    }
}
