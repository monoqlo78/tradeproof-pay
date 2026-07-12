namespace HashKeyChain.Domain;

/// <summary>
/// Business roles (spec §4). A single user may hold multiple roles; the UI always
/// shows which role the user is currently acting as. Values are English
/// identifiers; display labels are localized via IEnumLocalizer.
/// </summary>
public enum TradeRole
{
    /// <summary>買主側実務担当者。下書き作成・条件入力・書類確認。最終決済の承認権限は持たない。</summary>
    BuyerOperator,

    /// <summary>買主側責任者。条件承認・エスクロー承認・最終支払い承認・ウォレット署名。</summary>
    BuyerApprover,

    /// <summary>売主側担当者。自社関連取引の閲覧・書類提出・再提出。</summary>
    Seller,

    /// <summary>貿易書類確認担当。AI解析結果と原本確認・審査完了・Manual Review 承認/差戻し。署名はしない。</summary>
    TradeVerifier,

    /// <summary>デモユーザー・企業・設定管理。支払承認は代行しない。監査記録は削除不可。</summary>
    Administrator
}
