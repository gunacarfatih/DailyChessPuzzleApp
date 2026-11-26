
using System.ComponentModel.DataAnnotations;
using DailyChessPuzzleApi.Helpers;

namespace DailyChessPuzzleApi.DTOs;

public sealed class UpdateCountryRequest : IValidatableObject
{
    [Required(ErrorMessage = "CountryCode is required.")]
    [StringLength(
        2,
        MinimumLength = 2,
        ErrorMessage = "CountryCode must contain exactly two letters."
    )]
    public string CountryCode { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (!CountryCodeHelper.IsValid(CountryCode))
        {
            yield return new ValidationResult(
                "CountryCode must be a valid ISO 3166-1 alpha-2 code.",
                new[] { nameof(CountryCode) }
            );
        }
    }
}
