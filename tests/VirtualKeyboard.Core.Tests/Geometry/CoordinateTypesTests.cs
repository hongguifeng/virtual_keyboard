using VirtualKeyboard.Core.Geometry;

namespace VirtualKeyboard.Core.Tests.Geometry;

public sealed class CoordinateTypesTests
{
    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(168, 1.75)]
    [InlineData(192, 2.0)]
    public void DpiScaleConvertsDipSizesAtRequiredDpi(uint dpi, double expectedScale)
    {
        DpiScale scale = DpiScale.FromDpi(dpi, dpi);
        PhysicalPixelSize pixels = scale.ToPhysicalPixels(new DipSize(400, 200));
        Assert.Equal(expectedScale, scale.ScaleX);
        Assert.Equal(400 * expectedScale, pixels.Width);
        Assert.Equal(200 * expectedScale, pixels.Height);
        Assert.Equal(new DipSize(400, 200), scale.ToDips(pixels));
    }

    [Fact]
    public void PhysicalRectSupportsNegativeDesktopCoordinates()
    {
        var rect = new PhysicalPixelRect(-1920, -500, 800, 400);
        Assert.True(rect.IsValid);
        Assert.Equal(-1120, rect.Right);
        Assert.Equal(-100, rect.Bottom);
    }

    [Theory]
    [InlineData(double.NaN, 0, 10, 10)]
    [InlineData(double.PositiveInfinity, 0, 10, 10)]
    [InlineData(0, 0, -1, 10)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10_000_001, 0, 10, 10)]
    public void PhysicalRectRejectsInvalidValues(double x, double y, double width, double height) =>
        Assert.False(new PhysicalPixelRect(x, y, width, height).IsValid);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    public void DipSizeRejectsInvalidDimensions(double width, double height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DipSize(width, height));

    [Fact]
    public void DpiScaleSupportsDifferentAxisScales()
    {
        DpiScale scale = DpiScale.FromDpi(120, 144);
        Assert.Equal(new PhysicalPixelSize(125, 150), scale.ToPhysicalPixels(new DipSize(100, 100)));
    }
}
