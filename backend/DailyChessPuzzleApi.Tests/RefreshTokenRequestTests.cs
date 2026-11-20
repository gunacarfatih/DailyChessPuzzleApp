using System.ComponentModel.DataAnnotations;
using DailyChessPuzzleApi.DTOs.Auth;
using Xunit;

namespace DailyChessPuzzleApi.Tests;

public class RefreshTokenRequestTests
{
    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void RefreshToken_must_be_present_and_long_enough(string token)
    {
        var request = new RefreshTokenRequest { RefreshToken = token };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.False(valid);
    }

    [Fact]
    public void RefreshToken_accepts_expected_token_length()
    {
        var request = new RefreshTokenRequest { RefreshToken = new string('a', 64) };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);

        Assert.True(valid);
    }
}
