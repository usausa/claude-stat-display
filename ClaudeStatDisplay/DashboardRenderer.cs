namespace ClaudeStatDisplay;

using SkiaSharp;

internal static class DashboardRenderer
{
    private const int W       = 1280;
    private const int H       = 480;
    private const int HeaderH = 70;
    private const int Pad     = 24;
    private const int PadTop  = 14;
    private const int MidX    = 640;

    // Left panel の MODEL セクション高さ。TOKEN USAGE と RATE LIMITS の縦位置を揃えるために使用。
    // = DrawLabel(24+6=30) + model text(36+18=54) + pre-section gap(8)
    private const int ModelSectionH = 92;

    private static readonly Theme T = Theme.ClaudeCode;

    // テキスト描画の効率化のためタイプフェイスをキャッシュ
    private static readonly SKTypeface FontNormal =
        SKTypeface.FromFamilyName(T.FontFamily, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? SKTypeface.Default;

    private static readonly SKTypeface FontBold =
        SKTypeface.FromFamilyName(T.FontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? SKTypeface.Default;

    internal static byte[] Render(DisplayState state)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(T.BgColor);

        DrawHeader(canvas);
        DrawDividers(canvas);
        DrawLeftPanel(canvas, state);
        DrawRightPanel(canvas, state);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return encoded.ToArray();
    }

    // ──── Header ─────────────────────────────────────────────────────────────────

    private static void DrawHeader(SKCanvas canvas)
    {
        using var bg = new SKPaint();
        bg.Color = T.HeaderBg;
        bg.IsAntialias = true;
        canvas.DrawRect(SKRect.Create(0, 0, W, HeaderH), bg);

        DrawText(canvas, "CLAUDE API MONITOR", Pad, 52, T.AccentColor, 36, bold: true);

        var ts = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var tsW = MeasureText(ts, 36);
        DrawText(canvas, ts, W - Pad - tsW, 52, T.TextSecondary, 36);
    }

    private static void DrawDividers(SKCanvas canvas)
    {
        using var p = new SKPaint();
        p.Color = T.BorderColor;
        p.StrokeWidth = 1;
        p.Style = SKPaintStyle.Stroke;
        canvas.DrawLine(0, HeaderH, W, HeaderH, p);
        canvas.DrawLine(MidX, HeaderH, MidX, H, p);
    }

    // ──── Left panel ─────────────────────────────────────────────────────────────

    private static void DrawLeftPanel(SKCanvas canvas, DisplayState state)
    {
        float x      = Pad;
        float y      = HeaderH + PadTop;
        float rightX = MidX - Pad;
        float panelW = MidX - (Pad * 2);

        // MODEL ─────────────────────────────────────────────────────────────────
        DrawLabel(canvas, "MODEL", x, ref y);

        var modelColor = state.Model is null ? T.TextSecondary : T.AccentColor;
        DrawText(canvas, state.Model ?? "—", x, y + 36, modelColor, 36);
        y += 36 + 18;

        // TOKEN USAGE ── aligned with RATE LIMITS on right panel (y = HeaderH + PadTop + ModelSectionH)
        y += 8;
        DrawLabel(canvas, "TOKEN USAGE", x, ref y);

        var u = state.Usage;
        DrawKV(canvas, "Input",         FormatNullableInt(u.InputTokens),               x, rightX, ref y);
        DrawKV(canvas, "Output",        FormatNullableInt(u.OutputTokens),               x, rightX, ref y);
        DrawKV(canvas, "Cache read",    FormatNullableInt(u.CacheReadInputTokens),       x, rightX, ref y);
        DrawKV(canvas, "Cache created", FormatNullableInt(u.CacheCreationInputTokens),   x, rightX, ref y);

        // CONTEXT WINDOW ────────────────────────────────────────────────────────
        var ctxSize = ClaudeProxyMiddleware.GetContextWindowSize(state.Model);

        y += 10;
        DrawLabel(canvas, "CONTEXT WINDOW", x, ref y);

        if ((ctxSize > 0) && (u.InputTokens is not null))
        {
            var total    = u.InputTokens.Value + (u.CacheReadInputTokens ?? 0) + (u.CacheCreationInputTokens ?? 0);
            var frac     = Math.Clamp((float)total / ctxSize, 0f, 1f);
            var barColor = frac >= 0.9f ? T.ColorError : frac >= 0.7f ? T.ColorWarn : T.ColorGood;
            DrawBar(canvas, x, y, panelW, 24, frac, barColor);
            y += 24 + 6;
            DrawText(canvas, $"{total:N0} / {ctxSize:N0}  ({frac * 100:F1}%)", x, y + 28, T.TextSecondary, 28);
        }
        else
        {
            DrawBar(canvas, x, y, panelW, 24, 0f, T.ColorGood);
            y += 24 + 6;
            var ctxLabel = ctxSize > 0 ? $"— / {ctxSize:N0}" : "—";
            DrawText(canvas, ctxLabel, x, y + 28, T.TextSecondary, 28);
        }
    }

    // ──── Right panel ─────────────────────────────────────────────────────────────

    private static void DrawRightPanel(SKCanvas canvas, DisplayState state)
    {
        float x      = MidX + Pad;
        // Start at the same y as TOKEN USAGE on the left panel
        float y      = HeaderH + PadTop + ModelSectionH;
        float panelW = W - MidX - (Pad * 2);

        DrawLabel(canvas, "RATE LIMITS", x, ref y);
        y += 8;

        var rl = state.RateLimit;

        DrawRateRow(
            canvas, "5H",
            rl.FiveHourUtilization,
            rl.FiveHourStatus,
            rl.FiveHourReset?.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture),
            x, ref y, panelW);
        y += 16;

        DrawRateRow(
            canvas, "7D",
            rl.SevenDayUtilization,
            rl.SevenDayStatus,
            rl.SevenDayReset?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture),
            x, ref y, panelW);
    }

    private static void DrawRateRow(
        SKCanvas canvas, string label, double? utilization, string? status, string? resetTime,
        float x, ref float y, float panelW)
    {
        const float LabelW = 52f;
        const float BarH   = 28f;
        var barX = x + LabelW;
        var barW = panelW - LabelW;
        var color = StatusColor(status);

        // Axis label ("5H" / "7D")
        DrawText(canvas, label, x, y + 24, T.TextSecondary, 28);

        // Progress bar
        DrawBar(canvas, barX, y, barW, BarH, (float)(utilization ?? 0.0), color);

        // Percentage text inside bar, right-aligned
        var pctTxt = utilization.HasValue ? $"{utilization.Value * 100:F1}%" : "—";
        var pctW = MeasureText(pctTxt, 24);
        DrawText(canvas, pctTxt, (barX + barW) - pctW - 6, (y + BarH) - 5, T.TextPrimary, 24);

        y += BarH + 5;

        // Status badge (only when not "allowed") + reset time
        if ((status is not null) && (status != "allowed"))
        {
            DrawText(canvas, $"[{status}]", barX, y + 24, color, 24);
        }

        var resetText = resetTime ?? "—";
        var resetW = MeasureText(resetText, 24);
        DrawText(canvas, resetText, (barX + barW) - resetW, y + 24, T.TextSecondary, 24);

        y += 24 + 6;
    }

    // ──── Primitive helpers ────────────────────────────────────────────────────────

    private static void DrawLabel(SKCanvas canvas, string text, float x, ref float y)
    {
        DrawText(canvas, text, x, y + 24, T.TextSecondary, 24);
        y += 24 + 6;
    }

    private static void DrawKV(SKCanvas canvas, string key, string value, float leftX, float rightX, ref float y)
    {
        DrawText(canvas, key, leftX, y + 28, T.TextSecondary, 28);
        var valueW = MeasureText(value, 28);
        DrawText(canvas, value, rightX - valueW, y + 28, T.TextPrimary, 28);
        y += 28 + 12;
    }

    private static void DrawBar(SKCanvas canvas, float x, float y, float w, float h, float fraction, SKColor fillColor)
    {
        const float R = 3f;

        using var bg = new SKPaint();
        bg.Color = T.BarBgColor;
        bg.IsAntialias = true;
        canvas.DrawRoundRect(SKRect.Create(x, y, w, h), R, R, bg);

        var fw = Math.Max(0f, Math.Min(w, w * fraction));
        if (fw > (R * 2))
        {
            using var fg = new SKPaint();
            fg.Color = fillColor;
            fg.IsAntialias = true;
            canvas.DrawRoundRect(SKRect.Create(x, y, fw, h), R, R, fg);
        }
    }

    private static SKColor StatusColor(string? status) => status switch
    {
        "allowed"         => T.ColorGood,
        "allowed_warning" => T.ColorWarn,
        "rejected"        => T.ColorError,
        _                 => T.TextSecondary
    };

    private static void DrawText(SKCanvas canvas, string text, float x, float y, SKColor color, float size, bool bold = false)
    {
        var typeface = bold ? FontBold : FontNormal;
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint();
        paint.Color = color;
        paint.IsAntialias = true;
        canvas.DrawText(text, x, y, font, paint);
    }

    private static float MeasureText(string text, float size, bool bold = false)
    {
        var typeface = bold ? FontBold : FontNormal;
        using var font = new SKFont(typeface, size);
        return font.MeasureText(text);
    }

    private static string FormatNullableInt(int? value)
        => value.HasValue ? $"{value.Value:N0}" : "—";
}
