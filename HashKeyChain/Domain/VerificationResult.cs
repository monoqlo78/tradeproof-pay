namespace HashKeyChain.Domain;

/// <summary>
/// Outcome of the deterministic rule engine / document review (spec §11).
/// An AI "Low" risk level must never override a failed deterministic rule.
/// </summary>
public enum VerificationResult
{
    /// <summary>必須書類が揃い、重要項目一致、Confidence 基準以上。支払準備へ進める。</summary>
    Pass,

    /// <summary>Confidence が低い/表記ゆれ/曖昧さ。人間の確認が必要。</summary>
    ManualReview,

    /// <summary>必須書類不足・金額/当事者/ウォレット/数量/コンテナ不一致・期限超過・重複等。支払不可。</summary>
    Blocked,

    /// <summary>決定論ルール通過 + 人間確認完了 + 入金済 + 期限内 + 未決済。</summary>
    ReadyForApproval
}
