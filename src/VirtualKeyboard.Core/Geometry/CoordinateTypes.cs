namespace VirtualKeyboard.Core.Geometry;

public readonly record struct PhysicalPixelPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public readonly record struct PhysicalPixelSize(double Width, double Height)
{
    public bool IsFinite => double.IsFinite(Width) && double.IsFinite(Height);
    public bool IsPositive => IsFinite && Width > 0 && Height > 0;
}

public readonly record struct PhysicalPixelRect(double X, double Y, double Width, double Height)
{
    private const double CoordinateLimit = 10_000_000;

    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PhysicalPixelPoint Location => new(X, Y);
    public PhysicalPixelSize Size => new(Width, Height);

    public bool IsValid =>
        double.IsFinite(X) && double.IsFinite(Y) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        Math.Abs(X) <= CoordinateLimit && Math.Abs(Y) <= CoordinateLimit &&
        Width >= 0 && Height >= 0 && Width <= CoordinateLimit && Height <= CoordinateLimit &&
        (Width > 0 || Height > 0) && double.IsFinite(Right) && double.IsFinite(Bottom);
}

public readonly record struct DipSize
{
    public DipSize(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
    }

    public double Width { get; }
    public double Height { get; }
}

public readonly record struct DpiScale
{
    public const double DefaultDpi = 96;

    public DpiScale(double scaleX, double scaleY)
    {
        if (!double.IsFinite(scaleX) || scaleX <= 0) throw new ArgumentOutOfRangeException(nameof(scaleX));
        if (!double.IsFinite(scaleY) || scaleY <= 0) throw new ArgumentOutOfRangeException(nameof(scaleY));
        ScaleX = scaleX;
        ScaleY = scaleY;
    }

    public double ScaleX { get; }
    public double ScaleY { get; }

    public static DpiScale FromDpi(uint dpiX, uint dpiY)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dpiX);
        ArgumentOutOfRangeException.ThrowIfZero(dpiY);
        return new(dpiX / DefaultDpi, dpiY / DefaultDpi);
    }

    public PhysicalPixelSize ToPhysicalPixels(DipSize size) =>
        new(size.Width * ScaleX, size.Height * ScaleY);

    public DipSize ToDips(PhysicalPixelSize size)
    {
        if (!size.IsPositive) throw new ArgumentOutOfRangeException(nameof(size));
        return new(size.Width / ScaleX, size.Height / ScaleY);
    }
}
