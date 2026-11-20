using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs;

public sealed class UpdateCountryRequest
{
    [RegularExpression("^([A-Za-z]{2})?$", ErrorMessage = "CountryCode must be an ISO alpha-2 code.")]
    public string? CountryCode { get; set; }
}
