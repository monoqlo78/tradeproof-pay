using Microsoft.Extensions.Localization;

namespace HashKeyChain.Localization;

/// <inheritdoc />
public sealed class EnumLocalizer : IEnumLocalizer
{
    private readonly IStringLocalizer<SharedResource> _localizer;

    public EnumLocalizer(IStringLocalizer<SharedResource> localizer) => _localizer = localizer;

    public string Localize(Enum value)
    {
        var key = $"{value.GetType().Name}_{value}";
        var localized = _localizer[key];

        // Fall back to the internal (English) name if a translation is missing,
        // rather than showing an empty or broken label.
        return localized.ResourceNotFound ? value.ToString() : localized.Value;
    }

    public string Localize<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Localize((Enum)(object)value);
}

/// <summary>
/// Convenience extension so views can write <c>status.Localize(EnumLocalizer)</c>.
/// </summary>
public static class EnumLocalizerExtensions
{
    public static string Localize(this Enum value, IEnumLocalizer localizer) =>
        localizer.Localize(value);
}
