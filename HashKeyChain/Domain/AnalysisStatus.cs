namespace HashKeyChain.Domain;

/// <summary>
/// Analysis status of a document version (spec §8/§9). A failed analysis never
/// deletes the document; it can be re-run.
/// </summary>
public enum AnalysisStatus
{
    /// <summary>解析待ち。</summary>
    Pending,

    /// <summary>解析中。</summary>
    Analyzing,

    /// <summary>解析完了。</summary>
    Completed,

    /// <summary>解析失敗（再実行可能）。</summary>
    Failed
}
