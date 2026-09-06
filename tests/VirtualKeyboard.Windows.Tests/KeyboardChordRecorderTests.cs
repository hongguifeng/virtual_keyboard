using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class KeyboardChordRecorderTests
{
    [Theory]
    [InlineData(0x5B, WindowsKeyboardKey.LeftWindows)]
    [InlineData(0x5C, WindowsKeyboardKey.RightWindows)]
    [InlineData(0xA0, WindowsKeyboardKey.Shift)]
    [InlineData(0xA1, WindowsKeyboardKey.Shift)]
    [InlineData(0xA2, WindowsKeyboardKey.Control)]
    [InlineData(0xA3, WindowsKeyboardKey.Control)]
    [InlineData(0xA4, WindowsKeyboardKey.Alt)]
    [InlineData(0xA5, WindowsKeyboardKey.Alt)]
    [InlineData(0x09, WindowsKeyboardKey.Tab)]
    public void MapsSupportedVirtualKeys(uint virtualKey, WindowsKeyboardKey expected)
    {
        Assert.True(KeyboardChordRecorder.TryMapVirtualKey(virtualKey, out WindowsKeyboardKey actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x14)]
    [InlineData(0xFE)]
    [InlineData(0x1_0000)]
    public void RejectsUnsupportedVirtualKeys(uint virtualKey) =>
        Assert.False(KeyboardChordRecorder.TryMapVirtualKey(virtualKey, out _));
}
