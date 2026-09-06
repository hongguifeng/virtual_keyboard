using VirtualKeyboard.Core.Layouts;

namespace VirtualKeyboard.Core.Tests.Layouts;

public sealed class PasswordActionPolicyTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("7")]
    [InlineData("!")]
    [InlineData(" ")]
    public void SingleStandardCharactersAreAllowed(string value)
    {
        PasswordActionCheck result = PasswordActionPolicy.Check(new(LayoutActionTypes.Text, value: value));

        Assert.True(result.IsAllowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("\n")]
    public void PhrasesAndControlsAreRejectedWithoutReturningText(string value)
    {
        PasswordActionCheck result = PasswordActionPolicy.Check(new(LayoutActionTypes.Text, value: value));

        Assert.False(result.IsAllowed);
        Assert.Equal("password.textNotSingleCharacter", result.ReasonCode);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("D7")]
    [InlineData("Space")]
    [InlineData("Backspace")]
    [InlineData("Enter")]
    [InlineData("Delete")]
    [InlineData("OemTilde")]
    [InlineData("OemQuestion")]
    public void StandardAndEditingKeysAreAllowed(string key)
    {
        Assert.True(PasswordActionPolicy.Check(new(LayoutActionTypes.Key, virtualKey: key)).IsAllowed);
    }

    [Fact]
    public void ScanCodeAndUnknownKeyAreRejected()
    {
        Assert.Equal("password.keyNotAllowlisted", PasswordActionPolicy.Check(new(LayoutActionTypes.Key, scanCode: 30)).ReasonCode);
        Assert.Equal("password.keyNotAllowlisted", PasswordActionPolicy.Check(new(LayoutActionTypes.Key, virtualKey: "Power")).ReasonCode);
    }

    [Fact]
    public void HotkeysAreAlwaysDisabledToAvoidDataLeakage()
    {
        PasswordActionCheck result = PasswordActionPolicy.Check(
            new(LayoutActionTypes.Hotkey, virtualKey: "A", modifiers: ["Control"]));

        Assert.False(result.IsAllowed);
        Assert.Equal("password.hotkeyDisabled", result.ReasonCode);
    }

    [Theory]
    [InlineData("Shift", true)]
    [InlineData("CapsLock", true)]
    [InlineData("Control", false)]
    [InlineData("Alt", false)]
    public void OnlyCharacterStateModifiersAreAllowed(string modifier, bool expected)
    {
        Assert.Equal(expected, PasswordActionPolicy.Check(new(LayoutActionTypes.Modifier, modifier: modifier)).IsAllowed);
    }

    [Fact]
    public void UnknownActionTypeIsRejected()
    {
        PasswordActionCheck result = PasswordActionPolicy.Check(new("command"));

        Assert.Equal("password.actionTypeNotAllowed", result.ReasonCode);
    }
}
