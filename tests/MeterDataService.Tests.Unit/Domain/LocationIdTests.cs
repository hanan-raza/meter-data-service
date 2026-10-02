using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Domain;

public class LocationIdTests
{
    [Theory]
    [InlineData("41373559241")]
    [InlineData("10000000009")]
    public void MarketLocationId_accepts_ids_with_correct_check_digit(string maLoId) =>
        MarketLocationId.IsValid(maLoId).ShouldBeTrue();

    [Theory]
    [InlineData("41373559242")] // wrong check digit
    [InlineData("01373559241")] // leading zero not allowed
    [InlineData("4137355924")]  // too short
    [InlineData("4137355924A")] // non-digit
    [InlineData("")]
    [InlineData(null)]
    public void MarketLocationId_rejects_invalid_ids(string? maLoId) =>
        MarketLocationId.IsValid(maLoId).ShouldBeFalse();

    [Fact]
    public void MarketLocationId_check_digit_weights_even_positions_twice()
    {
        // Odd positions 4+3+3+5+2 = 17, even positions (1+7+5+9+4)*2 = 52, total 69 → 1.
        MarketLocationId.CalculateCheckDigit("4137355924").ShouldBe(1);
    }

    [Fact]
    public void MeterLocationId_accepts_33_character_id_with_country_prefix() =>
        MeterLocationId.IsValid("DE0001234567890000000000000000001").ShouldBeTrue();

    [Theory]
    [InlineData("DE000123456789000000000000000001")]   // 32 characters
    [InlineData("de0001234567890000000000000000001")]  // lower-case country code
    [InlineData("120001234567890000000000000000001")]  // missing country code
    [InlineData("DE000123456789000000000000000000-")]  // invalid character
    [InlineData(null)]
    public void MeterLocationId_rejects_invalid_ids(string? meLoId) =>
        MeterLocationId.IsValid(meLoId).ShouldBeFalse();
}
