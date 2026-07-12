using System.Globalization;
using HashKeyChain;
using HashKeyChain.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace HashKeyChain.Tests;

/// <summary>
/// Unit tests for the localization helper services. They confirm that internal
/// (English) identifiers stay stable while the display strings switch culture.
/// </summary>
public class LocalizationServiceTests
{
    private static readonly IServiceProvider Provider = BuildProvider();

    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        return services.BuildServiceProvider();
    }

    private static IStringLocalizer<SharedResource> Localizer =>
        Provider.GetRequiredService<IStringLocalizer<SharedResource>>();

    private static T WithCulture<T>(string culture, Func<T> func)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var ci = new CultureInfo(culture);
            CultureInfo.CurrentCulture = ci;
            CultureInfo.CurrentUICulture = ci;
            return func();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("ja-JP", TradeStatus.Draft, "下書き")]
    [InlineData("en-US", TradeStatus.Draft, "Draft")]
    [InlineData("ja-JP", TradeStatus.AwaitingDocuments, "書類待ち")]
    [InlineData("en-US", TradeStatus.AwaitingDocuments, "Awaiting documents")]
    [InlineData("ja-JP", TradeStatus.ManualReview, "手動確認が必要")]
    [InlineData("ja-JP", TradeStatus.ReadyForApproval, "承認可能")]
    [InlineData("ja-JP", TradeStatus.Settled, "決済済み")]
    [InlineData("en-US", TradeStatus.Settled, "Settled")]
    public void TradeStatus_is_localized(string culture, TradeStatus status, string expected)
    {
        var localizer = new EnumLocalizer(Localizer);
        var result = WithCulture(culture, () => localizer.Localize(status));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ja-JP", DocumentType.CommercialInvoice, "商業送り状")]
    [InlineData("en-US", DocumentType.CommercialInvoice, "Commercial Invoice")]
    [InlineData("ja-JP", DocumentType.BillOfLading, "船荷証券")]
    [InlineData("en-US", DocumentType.AirWaybill, "Air Waybill")]
    public void DocumentType_is_localized(string culture, DocumentType docType, string expected)
    {
        var localizer = new EnumLocalizer(Localizer);
        var result = WithCulture(culture, () => localizer.Localize(docType));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ja-JP", Web3Message.ConnectWallet, "ウォレットを接続してください。")]
    [InlineData("en-US", Web3Message.ConnectWallet, "Connect your wallet.")]
    [InlineData("ja-JP", Web3Message.AlreadySettled, "この取引はすでに決済されています。")]
    public void Web3_message_is_localized(string culture, Web3Message message, string expected)
    {
        var localizer = new Web3MessageLocalizer(Localizer);
        var result = WithCulture(culture, () => localizer.Localize(message));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("AlreadySettled", "ja-JP", "この取引はすでに決済されています。")]
    [InlineData("DuplicateSettlement", "en-US", "This trade has already been settled.")]
    [InlineData("UnknownCustomError", "ja-JP", "トランザクションが失敗しました。")]
    public void Web3_custom_error_is_mapped_and_localized(string customError, string culture, string expected)
    {
        var localizer = new Web3MessageLocalizer(Localizer);
        var result = WithCulture(culture, () => localizer.LocalizeCustomError(customError));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ja-JP", "Japanese")]
    [InlineData("en-US", "English")]
    public void Agent_language_context_matches_ui_culture(string culture, string expectedLanguage)
    {
        var provider = new AgentLanguageContextProvider();
        var context = WithCulture(culture, () => provider.GetContext());

        Assert.Equal(culture, context.UiCulture);
        Assert.Equal(expectedLanguage, context.ResponseLanguage);
    }

    [Fact]
    public void Client_resources_switch_culture_but_keep_english_keys()
    {
        var provider = new ClientLocalizationProvider(Localizer);

        var ja = WithCulture("ja-JP", () => provider.GetResources());
        var en = WithCulture("en-US", () => provider.GetResources());

        // Keys are stable English identifiers.
        Assert.Contains("walletConnected", ja.Keys);
        Assert.Contains("walletConnected", en.Keys);

        // Values are translated.
        Assert.Equal("ウォレットを接続しました。", ja["walletConnected"]);
        Assert.Equal("Wallet connected.", en["walletConnected"]);
    }

    [Fact]
    public void DataAnnotations_messages_are_localized_in_both_cultures()
    {
        var jaRequired = WithCulture("ja-JP", () => Localizer["Validation_TradeReference_Required"].Value);
        var enRequired = WithCulture("en-US", () => Localizer["Validation_TradeReference_Required"].Value);
        Assert.Equal("取引参照番号を入力してください。", jaRequired);
        Assert.Equal("Enter the trade reference.", enRequired);

        // The StringLength template must format with min/max placeholders.
        var jaTemplate = WithCulture("ja-JP", () => Localizer["Validation_TradeReference_Length"].Value);
        var formatted = string.Format(jaTemplate, "TradeReference", 32, 4);
        Assert.Contains("4", formatted);
        Assert.Contains("32", formatted);
    }

    [Theory]
    [InlineData("ja-JP", true)]
    [InlineData("en-US", true)]
    [InlineData("EN-us", true)]
    [InlineData("fr-FR", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_recognizes_only_ja_and_en(string? culture, bool expected)
    {
        Assert.Equal(expected, LocalizationConstants.IsSupported(culture));
    }

    [Fact]
    public void Default_culture_is_japanese()
    {
        Assert.Equal("ja-JP", LocalizationConstants.DefaultCulture);
    }
}
