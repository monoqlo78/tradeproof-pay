namespace HashKeyChain.Localization;

/// <summary>
/// Trade lifecycle status (business spec §23, 18 states). These values are the
/// canonical English identifiers stored in the database and used in code. Never
/// render ToString() directly to the UI &mdash; use <see cref="IEnumLocalizer"/>
/// to obtain a localized label. State transitions are owned by the state-machine
/// service; UI/controllers must never mutate status directly.
/// </summary>
public enum TradeStatus
{
    /// <summary>取引条件の入力中。</summary>
    Draft,

    /// <summary>取引条件の承認待ち。</summary>
    PendingTradeApproval,

    /// <summary>取引条件は承認済みだが、エスクロー未入金。</summary>
    AwaitingFunding,

    /// <summary>エスクロー入金済み。</summary>
    Funded,

    /// <summary>必要書類の提出待ち。</summary>
    AwaitingDocuments,

    /// <summary>書類解析中。</summary>
    Analyzing,

    /// <summary>人間による確認が必要。</summary>
    ManualReview,

    /// <summary>書類を売主へ差し戻した状態。</summary>
    DocumentsRejected,

    /// <summary>重大な不一致があり支払不可。</summary>
    Blocked,

    /// <summary>自動照合を通過し、Trade Verifier の確認待ち。</summary>
    ReadyForVerification,

    /// <summary>書類確認完了、Buyer Approver の支払い承認待ち。</summary>
    ReadyForApproval,

    /// <summary>Buyer Approver が支払いを承認済み。</summary>
    Approved,

    /// <summary>トランザクション送信済みで Receipt 待ち。</summary>
    SettlementPending,

    /// <summary>決済完了。</summary>
    Settled,

    /// <summary>支払期限切れ。</summary>
    Expired,

    /// <summary>返金トランザクションの Receipt 待ち。</summary>
    RefundPending,

    /// <summary>返金完了。</summary>
    Refunded,

    /// <summary>未入金の状態で取消済み。</summary>
    Cancelled
}
