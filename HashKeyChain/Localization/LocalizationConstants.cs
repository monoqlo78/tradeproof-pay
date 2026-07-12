using System.Globalization;

namespace HashKeyChain.Localization;

/// <summary>
/// Central definition of the supported cultures and the default culture.
/// Internal enum values, JSON keys, tool names and blockchain values stay in
/// English; only display text is localized.
/// </summary>
public static class LocalizationConstants
{
    public const string DefaultCulture = "ja-JP";

    public static readonly string[] SupportedCultures = { "ja-JP", "en-US" };

    public static bool IsSupported(string? culture) =>
        !string.IsNullOrWhiteSpace(culture) &&
        SupportedCultures.Contains(culture, StringComparer.OrdinalIgnoreCase);

    public static CultureInfo[] SupportedCultureInfos =>
        SupportedCultures.Select(c => new CultureInfo(c)).ToArray();
}
