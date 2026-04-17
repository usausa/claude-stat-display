namespace ClaudeStatDisplay;

using SkiaSharp;

internal sealed class Theme
{
    public required SKColor BgColor { get; init; }

    public required SKColor HeaderBg { get; init; }

    public required SKColor BorderColor { get; init; }

    public required SKColor TextPrimary { get; init; }

    public required SKColor TextSecondary { get; init; }

    public required SKColor AccentColor { get; init; }

    public required SKColor ColorGood { get; init; }

    public required SKColor ColorWarn { get; init; }

    public required SKColor ColorError { get; init; }

    public required SKColor BarBgColor { get; init; }

    public required string FontFamily { get; init; }

    public static readonly Theme ClaudeCode = new()
    {
        BgColor       = new SKColor(0x0D, 0x0D, 0x0D),
        HeaderBg      = new SKColor(0x1C, 0x1C, 0x1C),
        BorderColor   = new SKColor(0x33, 0x33, 0x33),
        TextPrimary   = new SKColor(0xF0, 0xED, 0xE8),
        TextSecondary = new SKColor(0x70, 0x6B, 0x65),
        AccentColor   = new SKColor(0xDA, 0x77, 0x56),
        ColorGood     = new SKColor(0x57, 0xA7, 0x73),
        ColorWarn     = new SKColor(0xD9, 0xA3, 0x1C),
        ColorError    = new SKColor(0xD9, 0x52, 0x52),
        BarBgColor    = new SKColor(0x24, 0x24, 0x24),
        FontFamily    = "Consolas"
    };
}
