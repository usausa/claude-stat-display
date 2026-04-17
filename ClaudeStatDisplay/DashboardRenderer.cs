namespace ClaudeStatDisplay;

using System.Globalization;

using SkiaSharp;

internal static class DashboardRenderer
{
    // ──── Canvas dimensions ──────────────────────────────────────────────────────
    private const int CanvasW = 1280;
    private const int CanvasH = 480;
    private const int HeaderH = 70;
    private const int PanelPad = 24;    // horizontal panel margin
    private const int PanelPadTop = 14; // gap between header bottom and first content row
    private const int MidX = 640;       // X coordinate of centre divider

    // ──── Font sizes ──────────────────────────────────────────────────────────────
    private const float FontSizeTitle = 36f;   // header title, model name
    private const float FontSizeKv = 28f;      // key-value rows, context bar label
    private const float FontSizeSmall = 24f;   // section labels, rate value/status

    // ──── Vertical layout ────────────────────────────────────────────────────────
    private const float HeaderTitleY = 52f;    // baseline Y for text in header
    private const float ModelGap = 18f;        // gap after model name line
    private const float SectionPreGap = 8f;    // gap before TOKEN USAGE / RATE LIMITS label
    private const float CtxSectionGap = 10f;   // gap before CONTEXT WINDOW label
    private const float LabelLineGap = 6f;     // gap after a section-label line
    private const float KvLineGap = 12f;       // gap after a key-value row
    private const float RateRowGap = 16f;      // vertical gap between 5H and 7D rows
    private const float RateBarGap = 5f;       // gap below a rate-limit bar

    // ──── Bar dimensions ─────────────────────────────────────────────────────────
    private const float CtxBarH = 24f;         // context-window bar height
    private const float RateBarH = 28f;        // rate-limit bar height
    private const float BarRadius = 3f;        // corner radius for all bars
    private const float RateBarInnerPad = 6f;  // right padding for text inside rate bar

    // ──── Rate-row layout ────────────────────────────────────────────────────────
    private const float RateLabelW = 52f;      // width reserved for "5H" / "7D" axis label

    // ──── Left-panel model section height ────────────────────────────────────────
    // Used to align TOKEN USAGE (left) with RATE LIMITS (right) vertically.
    // = DrawLabel advance + model-line advance + pre-section gap
    // = (FontSizeSmall + LabelLineGap) + (FontSizeTitle + ModelGap) + SectionPreGap
    // = (24 + 6) + (36 + 18) + 8 = 92
    private const int ModelSectionH = (int)((FontSizeSmall + LabelLineGap) + (FontSizeTitle + ModelGap) + SectionPreGap);

    private static readonly Theme T = Theme.ClaudeCode;

    private static readonly SKTypeface FontNormal = ResolveTypeface(bold: false);
    private static readonly SKTypeface FontBold = ResolveTypeface(bold: true);

    internal static byte[] Render(DisplayState state)
    {
        var info = new SKImageInfo(CanvasW, CanvasH, SKColorType.Rgba8888, SKAlphaType.Premul);
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

    // ──── Header ──────────────────────────────────────────────────────────────────

    private static void DrawHeader(SKCanvas canvas)
    {
        using var bg = new SKPaint { Color = T.HeaderBg, IsAntialias = true };
        canvas.DrawRect(SKRect.Create(0, 0, CanvasW, HeaderH), bg);

        DrawText(canvas, "CLAUDE API MONITOR", PanelPad, HeaderTitleY, T.AccentColor, FontSizeTitle, bold: true);

        var ts = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss", CultureInfo.InvariantCulture);
        var tsW = MeasureText(ts, FontSizeTitle);
        DrawText(canvas, ts, CanvasW - PanelPad - tsW, HeaderTitleY, T.TextSecondary, FontSizeTitle);
    }

    private static void DrawDividers(SKCanvas canvas)
    {
        using var p = new SKPaint { Color = T.BorderColor, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        canvas.DrawLine(0, HeaderH, CanvasW, HeaderH, p);
        canvas.DrawLine(MidX, HeaderH, MidX, CanvasH, p);
    }

    // ──── Left panel ──────────────────────────────────────────────────────────────

    private static void DrawLeftPanel(SKCanvas canvas, DisplayState state)
    {
        float x      = PanelPad;
        float y      = HeaderH + PanelPadTop;
        float rightX = MidX - PanelPad;
        float panelW = MidX - (PanelPad * 2);

        // MODEL ─────────────────────────────────────────────────────────────────
        DrawLabel(canvas, "MODEL", x, ref y);

        var modelColor = state.Model is null ? T.TextSecondary : T.AccentColor;
        DrawText(canvas, state.Model ?? "—", x, y + FontSizeTitle, modelColor, FontSizeTitle);
        y += FontSizeTitle + ModelGap;

        // TOKEN USAGE ── aligned with RATE LIMITS on right panel
        y += SectionPreGap;
        DrawLabel(canvas, "TOKEN USAGE", x, ref y);

        var u = state.Usage;
        DrawKeyValue(canvas, "Input",         FormatNullableInt(u.InputTokens),             x, rightX, ref y);
        DrawKeyValue(canvas, "Output",        FormatNullableInt(u.OutputTokens),             x, rightX, ref y);
        DrawKeyValue(canvas, "Cache read",    FormatNullableInt(u.CacheReadInputTokens),     x, rightX, ref y);
        DrawKeyValue(canvas, "Cache created", FormatNullableInt(u.CacheCreationInputTokens), x, rightX, ref y);

        // CONTEXT WINDOW ────────────────────────────────────────────────────────
        var ctxSize = ClaudeProxyMiddleware.GetContextWindowSize(state.Model);

        y += CtxSectionGap;
        DrawLabel(canvas, "CONTEXT WINDOW", x, ref y);

        if ((ctxSize > 0) && (u.InputTokens is not null))
        {
            var total    = u.InputTokens.Value + (u.CacheReadInputTokens ?? 0) + (u.CacheCreationInputTokens ?? 0);
            var frac     = Math.Clamp((float)total / ctxSize, 0f, 1f);
            var barColor = frac >= 0.9f ? T.ColorError : frac >= 0.7f ? T.ColorWarn : T.ColorGood;
            DrawBar(canvas, x, y, panelW, CtxBarH, frac, barColor);
            y += CtxBarH + LabelLineGap;
            DrawText(canvas, FormattableString.Invariant($"{total:N0} / {ctxSize:N0}  ({frac * 100:F1}%)"), x, y + FontSizeKv, T.TextSecondary, FontSizeKv);
        }
        else
        {
            DrawBar(canvas, x, y, panelW, CtxBarH, 0f, T.ColorGood);
            y += CtxBarH + LabelLineGap;
            var ctxLabel = ctxSize > 0 ? FormattableString.Invariant($"— / {ctxSize:N0}") : "—";
            DrawText(canvas, ctxLabel, x, y + FontSizeKv, T.TextSecondary, FontSizeKv);
        }
    }

    // ──── Right panel ─────────────────────────────────────────────────────────────

    private static void DrawRightPanel(SKCanvas canvas, DisplayState state)
    {
        float x      = MidX + PanelPad;
        float y      = HeaderH + PanelPadTop + ModelSectionH;  // aligned with TOKEN USAGE
        float panelW = CanvasW - MidX - (PanelPad * 2);

        DrawLabel(canvas, "RATE LIMITS", x, ref y);
        y += SectionPreGap;

        var rl = state.RateLimit;

        DrawRateRow(
            canvas, "5H",
            rl.FiveHourUtilization,
            rl.FiveHourStatus,
            rl.FiveHourReset?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            x, ref y, panelW);
        y += RateRowGap;

        DrawRateRow(
            canvas, "7D",
            rl.SevenDayUtilization,
            rl.SevenDayStatus,
            rl.SevenDayReset?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
            x, ref y, panelW);
    }

    private static void DrawRateRow(
        SKCanvas canvas, string label, double? utilization, string? status, string? resetTime,
        float x, ref float y, float panelW)
    {
        var barX  = x + RateLabelW;
        var barW  = panelW - RateLabelW;
        var color = StatusColor(status);

        // Axis label ("5H" / "7D")
        DrawText(canvas, label, x, y + FontSizeSmall, T.TextSecondary, FontSizeKv);

        // Progress bar
        DrawBar(canvas, barX, y, barW, RateBarH, (float)(utilization ?? 0.0), color);

        // Percentage text inside bar, right-aligned
        var pctTxt = utilization.HasValue ? FormattableString.Invariant($"{utilization.Value * 100:F1}%") : "—";
        var pctW   = MeasureText(pctTxt, FontSizeSmall);
        DrawText(canvas, pctTxt, (barX + barW) - pctW - RateBarInnerPad, (y + RateBarH) - 5, T.TextPrimary, FontSizeSmall);

        y += RateBarH + RateBarGap;

        // Status badge (only when not "allowed") + reset time
        if ((status is not null) && (status != "allowed"))
        {
            DrawText(canvas, $"[{status}]", barX, y + FontSizeSmall, color, FontSizeSmall);
        }

        var resetText = resetTime ?? "—";
        var resetW    = MeasureText(resetText, FontSizeSmall);
        DrawText(canvas, resetText, (barX + barW) - resetW, y + FontSizeSmall, T.TextSecondary, FontSizeSmall);

        y += FontSizeSmall + LabelLineGap;
    }

    // ──── Primitive helpers ───────────────────────────────────────────────────────

    private static void DrawLabel(SKCanvas canvas, string text, float x, ref float y)
    {
        DrawText(canvas, text, x, y + FontSizeSmall, T.TextSecondary, FontSizeSmall);
        y += FontSizeSmall + LabelLineGap;
    }

    private static void DrawKeyValue(SKCanvas canvas, string key, string value, float leftX, float rightX, ref float y)
    {
        DrawText(canvas, key, leftX, y + FontSizeKv, T.TextSecondary, FontSizeKv);
        var valueW = MeasureText(value, FontSizeKv);
        DrawText(canvas, value, rightX - valueW, y + FontSizeKv, T.TextPrimary, FontSizeKv);
        y += FontSizeKv + KvLineGap;
    }

    private static void DrawBar(SKCanvas canvas, float x, float y, float w, float h, float fraction, SKColor fillColor)
    {
        using var bg = new SKPaint { Color = T.BarBgColor, IsAntialias = true };
        canvas.DrawRoundRect(SKRect.Create(x, y, w, h), BarRadius, BarRadius, bg);

        var fw = Math.Max(0f, Math.Min(w, w * fraction));
        if (fw > (BarRadius * 2))
        {
            using var fg = new SKPaint { Color = fillColor, IsAntialias = true };
            canvas.DrawRoundRect(SKRect.Create(x, y, fw, h), BarRadius, BarRadius, fg);
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
        using var font  = new SKFont(typeface, size) { Edging = SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y, font, paint);
    }

    private static float MeasureText(string text, float size, bool bold = false)
    {
        var typeface = bold ? FontBold : FontNormal;
        using var font = new SKFont(typeface, size);
        return font.MeasureText(text);
    }

    private static string FormatNullableInt(int? value)
        => value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "—";

    private static SKTypeface ResolveTypeface(bool bold)
    {
        if (bold)
        {
            var boldPath = Path.Combine("Assets", "Roboto-Bold.ttf");
            if (File.Exists(boldPath))
            {
                var tf = SKTypeface.FromFile(boldPath);
                if (tf is not null)
                {
                    return tf;
                }
            }
        }

        var mediumPath = Path.Combine("Assets", "Roboto-Medium.ttf");
        if (File.Exists(mediumPath))
        {
            var tf = SKTypeface.FromFile(mediumPath);
            if (tf is not null)
            {
                return tf;
            }
        }

        return bold
            ? (SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default)
            : SKTypeface.Default;
    }
}
