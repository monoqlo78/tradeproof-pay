namespace HashKeyChain.Localization;

/// <summary>
/// Resolves a localized display string for an enum value. The lookup key is
/// <c>{EnumTypeName}_{Value}</c> in the shared resource, e.g. <c>TradeStatus_Draft</c>.
/// </summary>
public interface IEnumLocalizer
{
    string Localize(Enum value);

    string Localize<TEnum>(TEnum value) where TEnum : struct, Enum;
}
