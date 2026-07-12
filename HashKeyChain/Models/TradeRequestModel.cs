using System.ComponentModel.DataAnnotations;

namespace HashKeyChain.Models;

/// <summary>
/// Demo input model showing localized DataAnnotations. The ErrorMessage values
/// are resource keys (see Resources/SharedResource.*.resx) resolved for the
/// current culture by the localized validator.
/// </summary>
public sealed class TradeRequestModel
{
    [Required(ErrorMessage = "Validation_TradeReference_Required")]
    [StringLength(32, MinimumLength = 4, ErrorMessage = "Validation_TradeReference_Length")]
    public string? TradeReference { get; set; }

    [Required(ErrorMessage = "Validation_Amount_Required")]
    [Range(1, 1_000_000, ErrorMessage = "Validation_Amount_Range")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Validation_Email_Required")]
    [EmailAddress(ErrorMessage = "Validation_Email_Invalid")]
    public string? Email { get; set; }
}
