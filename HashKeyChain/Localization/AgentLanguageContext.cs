using System.Globalization;

namespace HashKeyChain.Localization;

/// <summary>
/// Structured language context passed to the AI agent. The keys are English and
/// must not be translated; only the values describe the language to respond in.
/// </summary>
public sealed record AgentLanguageContext(string UiCulture, string ResponseLanguage);

public interface IAgentLanguageContextProvider
{
    AgentLanguageContext GetContext();

    /// <summary>
    /// The system-prompt directive that tells the agent to answer in the current
    /// UI language while keeping identifiers in English.
    /// </summary>
    string GetSystemPromptDirective();
}

/// <inheritdoc />
public sealed class AgentLanguageContextProvider : IAgentLanguageContextProvider
{
    public AgentLanguageContext GetContext()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        var responseLanguage = ResolveResponseLanguage(culture);
        return new AgentLanguageContext(culture, responseLanguage);
    }

    public string GetSystemPromptDirective()
    {
        var context = GetContext();
        return context.ResponseLanguage == "Japanese"
            ? "現在のUI言語（日本語）を標準の回答言語として使用してください。専門用語は必要に応じて英語を併記してください。" +
              "ツール名、JSONキー、内部Enum、ウォレットアドレス、Transaction Hash、Contract Addressは翻訳しないでください。"
            : "Use the current UI language (English) as your default response language. " +
              "Do not translate tool names, JSON keys, internal enums, wallet addresses, transaction hashes or contract addresses.";
    }

    private static string ResolveResponseLanguage(string culture) =>
        culture.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "Japanese" : "English";
}
