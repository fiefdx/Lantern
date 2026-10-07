using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T1.4: the input rules of the whole app live in <see cref="ValidationRules"/>, so they are
/// testable without a single form. A null return means "acceptable"; anything else is the exact
/// string the UI shows.
/// </summary>
public class ValidationRulesTests
{
    [Theory]
    [InlineData("Haitao")]
    [InlineData("A")]
    [InlineData("Haitao Wu")]
    [InlineData("1234567890123456")]
    public void GoodNicknamesAreAccepted(string raw)
        => Assert.Null(ValidationRules.NicknameError(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyNicknameIsRefused(string? raw)
        => Assert.Equal("Nickname is required.", ValidationRules.NicknameError(raw));

    [Fact]
    public void ANicknameOverTheLimitIsRefused()
    {
        var tooLong = new string('x', ValidationRules.NicknameMaxLength + 1);

        Assert.Equal($"Nickname must be {ValidationRules.NicknameMaxLength} characters or fewer.",
            ValidationRules.NicknameError(tooLong));

        // Exactly on the limit is fine; the rule is inclusive.
        Assert.Null(ValidationRules.NicknameError(new string('x', ValidationRules.NicknameMaxLength)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t \n ")]
    public void AnEmptyRoomNameIsRefused(string? raw)
        => Assert.Equal("Room name is required.", ValidationRules.RoomNameError(raw));

    [Fact]
    public void ARoomNameOverTheLimitIsRefused()
    {
        Assert.Null(ValidationRules.RoomNameError(new string('x', ValidationRules.RoomNameMaxLength)));
        Assert.NotNull(ValidationRules.RoomNameError(new string('x', ValidationRules.RoomNameMaxLength + 1)));
    }

    [Fact]
    public void DuplicatesIgnoreCaseAndSurroundingSpace()
    {
        var existing = new[] { "Room 1", " Study " };

        Assert.True(ValidationRules.IsDuplicateRoomName("room 1", existing));
        Assert.True(ValidationRules.IsDuplicateRoomName("  study", existing));
        Assert.False(ValidationRules.IsDuplicateRoomName("Room 2", existing));
        Assert.False(ValidationRules.IsDuplicateRoomName("Room 1", []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void AnEmptyMessageIsRefused(string? raw)
        => Assert.Equal("Message is empty.", ValidationRules.MessageError(raw));

    [Fact]
    public void AMessageOverTheLimitIsRefused()
    {
        Assert.Null(ValidationRules.MessageError(new string('x', ValidationRules.MessageMaxLength)));
        Assert.NotNull(ValidationRules.MessageError(new string('x', ValidationRules.MessageMaxLength + 1)));
    }

    [Fact]
    public void AMultiLineMessageCountsItsLineBreaks()
    {
        // The box accepts Shift+Enter, so the length rule has to see what was actually typed.
        var body = string.Join(Environment.NewLine, Enumerable.Repeat("0123456789", ValidationRules.MessageMaxLength / 10 + 1));

        Assert.NotNull(ValidationRules.MessageError(body));
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData("", 100)]
    [InlineData("   ", 100)]
    public void APictureWithoutANameIsRefused(string? name, long bytes)
        => Assert.Equal("Picture has no file name.", ValidationRules.PictureError(name, bytes));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void AnEmptyPictureIsRefused(long bytes)
        => Assert.Equal("Picture file is empty.", ValidationRules.PictureError("a.png", bytes));

    [Fact]
    public void APictureOverTheLimitIsRefused()
    {
        Assert.Null(ValidationRules.PictureError("a.png", ValidationRules.MaxPictureBytes));
        Assert.NotNull(ValidationRules.PictureError("a.png", ValidationRules.MaxPictureBytes + 1));
    }

    [Theory]
    [InlineData("  Haitao   Wu  ", "Haitao Wu")]
    [InlineData("\n\tRoom 1\r\n", "Room 1")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeTrimsAndCollapsesInnerWhitespace(string? raw, string expected)
        => Assert.Equal(expected, ValidationRules.Normalize(raw));

    [Fact]
    public void TheBoolHelpersAgreeWithTheErrorHelpers()
    {
        Assert.True(ValidationRules.IsValidNickname("Haitao"));
        Assert.False(ValidationRules.IsValidNickname(""));
        Assert.True(ValidationRules.IsValidRoomName("Room 1"));
        Assert.False(ValidationRules.IsValidRoomName(""));
        Assert.True(ValidationRules.IsValidMessage("hi"));
        Assert.False(ValidationRules.IsValidMessage("  "));
        Assert.True(ValidationRules.IsValidPicture("a.png", 10));
        Assert.False(ValidationRules.IsValidPicture("a.png", 0));
    }

    [Fact]
    public void TheLimitsAreTheOnesTheProposalAsksFor()
    {
        // Locked down on purpose: the UI quotes these numbers in its placeholder text and its
        // tooltips, so changing one here must be a deliberate decision.
        Assert.Equal(1, ValidationRules.NicknameMinLength);
        Assert.Equal(16, ValidationRules.NicknameMaxLength);
        Assert.Equal(24, ValidationRules.RoomNameMaxLength);
        Assert.Equal(500, ValidationRules.MessageMaxLength);
        Assert.Equal(5L * 1024 * 1024, ValidationRules.MaxPictureBytes);
    }
}
