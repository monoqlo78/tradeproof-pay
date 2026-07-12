namespace HashKeyChain.Domain;

/// <summary>
/// Audited operations (spec §21). Every important operation is appended to the
/// audit trail. Regular users cannot edit or delete audit entries.
/// </summary>
public enum AuditAction
{
    TradeCreated,
    TradeConditionsChanged,
    TradeConditionsApproved,
    EscrowFunded,
    DocumentUploaded,
    DocumentSuperseded,
    DocumentAnalyzed,
    DocumentReanalyzed,
    MismatchDetected,
    ManualReviewRaised,
    DocumentRejected,
    DocumentVerified,
    PaymentApproved,
    WalletOperationStarted,
    TransactionSubmitted,
    ReceiptConfirmed,
    SettlementCompleted,
    SettlementFailed,
    Refunded,
    TradeCancelled,
    AgentToolExecuted,
    UserConfirmation,
    PermissionDenied
}
