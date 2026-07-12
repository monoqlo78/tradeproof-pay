using System.Text.Json;
using HashKeyChain.Domain;
using HashKeyChain.Localization;

namespace HashKeyChain.Services.Analysis;

/// <summary>
/// Extracts structured fields from a trade document (spec §9). The real
/// implementation would call a document-intelligence / LLM service; the DemoMode
/// implementation is deterministic. Swapping this interface is how the app moves
/// from mock to Azure AI without touching the rule engine.
/// </summary>
public interface IDocumentAnalysisService
{
    bool IsMock { get; }

    Task<DocumentAnalysisResult> AnalyzeAsync(
        DocumentType declaredType, string fileName, byte[] content, Trade trade, CancellationToken ct = default);
}
