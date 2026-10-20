using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MermaidMcpServer.Rendering;

// Mermaider красит SVG через CSS-переменные и color-mix() — это понимают только браузеры.
// Остальные средства просмотра (IDE, Svg.Skia/PNG, Inkscape, Office) заливают такие узлы
// чёрным, и текст в них пропадает. Здесь var()/color-mix() в атрибутах заменяются
// готовыми #rrggbb, а размеры шрифта — пикселями.
// ponytail: считается только color-mix(in srgb, ...) — других Mermaider не выдаёт.
// Живое переключение темы через --bg/--fg после этого не работает: цвета зафиксированы.
public static partial class SvgColorResolver
{
    private static readonly HashSet<string> PaintAttributes = ["fill", "stroke", "stop-color", "color", "font-size"];

    [GeneratedRegex(@"(--[\w-]+)\s*:\s*([^;}]+)")]
    private static partial Regex CustomProperty();

    [GeneratedRegex(@"^(.*?)\s+(\d+(?:\.\d+)?)%$", RegexOptions.Singleline)]
    private static partial Regex Percentage();

    [GeneratedRegex(@"^#(?:([0-9a-fA-F]{6})|([0-9a-fA-F]{3}))$")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"^(\d*\.?\d+)rem$")]
    private static partial Regex Rem();

    public static string Resolve(string svg)
    {
        var doc = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);

        // Определения переменных берём из самого SVG: сначала <style>, затем style корня (он приоритетнее).
        var props = new Dictionary<string, string>();
        foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "style"))
            foreach (Match m in CustomProperty().Matches(style.Value))
                props[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        foreach (Match m in CustomProperty().Matches((string?)doc.Root!.Attribute("style") ?? string.Empty))
            props[m.Groups[1].Value] = m.Groups[2].Value.Trim();

        var attributes = doc.Descendants().Attributes()
            .Where(a => PaintAttributes.Contains(a.Name.LocalName) &&
                        (a.Value.Contains("var(") || a.Value.Contains("color-mix(")))
            .ToList();

        foreach (var attribute in attributes)
        {
            var value = Evaluate(attribute.Value, props, 0);
            if (attribute.Name.LocalName == "font-size" && Rem().Match(value) is { Success: true } rem)
                value = string.Create(CultureInfo.InvariantCulture, $"{double.Parse(rem.Groups[1].Value, CultureInfo.InvariantCulture) * 16:0.##}");
            attribute.Value = value;
        }

        return attributes.Count == 0 ? svg : doc.Root.ToString(SaveOptions.DisableFormatting);
    }

    // Возвращает значение с раскрытыми var() и color-mix(); если вычислить нельзя — исходную строку.
    private static string Evaluate(string expression, Dictionary<string, string> props, int depth)
    {
        var e = expression.Trim();
        if (depth > 16)
            return e;

        if (e.StartsWith("var(") && e.EndsWith(')'))
        {
            var args = SplitTopLevel(e[4..^1]);
            if (props.TryGetValue(args[0].Trim(), out var defined))
                return Evaluate(defined, props, depth + 1);
            return args.Length > 1 ? Evaluate(string.Join(",", args[1..]), props, depth + 1) : e;
        }

        if (e.StartsWith("color-mix(") && e.EndsWith(')'))
        {
            var args = SplitTopLevel(e[10..^1]);
            if (args.Length != 3 || !args[0].Trim().StartsWith("in srgb"))
                return e;

            var (first, firstShare) = ParseMixOperand(args[1], props, depth);
            var (second, secondShare) = ParseMixOperand(args[2], props, depth);
            if (first is null || second is null)
                return e;

            // Без процентов — поровну; один процент задан — второй дополняет до 100.
            var p1 = firstShare ?? (secondShare is null ? 50 : 100 - secondShare.Value);
            var p2 = secondShare ?? (100 - p1);
            var w1 = p1 / (p1 + p2);

            var channels = first.Zip(second, (a, b) => (int)Math.Round(a * w1 + b * (1 - w1)));
            return "#" + string.Concat(channels.Select(c => c.ToString("x2", CultureInfo.InvariantCulture)));
        }

        return e;
    }

    private static (int[]? Rgb, double? Share) ParseMixOperand(string operand, Dictionary<string, string> props, int depth)
    {
        var text = operand.Trim();
        double? share = null;
        if (Percentage().Match(text) is { Success: true } m)
        {
            text = m.Groups[1].Value;
            share = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        var color = HexColor().Match(Evaluate(text, props, depth + 1));
        if (!color.Success)
            return (null, share);

        var hex = color.Groups[1].Success
            ? color.Groups[1].Value
            : string.Concat(color.Groups[2].Value.Select(c => $"{c}{c}"));
        return ([Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..], 16)], share);
    }

    // Делит по запятым верхнего уровня: color-mix(in srgb, var(--a, #fff) 20%, #000) → 3 части.
    private static string[] SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var level = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') level++;
            else if (text[i] == ')') level--;
            else if (text[i] == ',' && level == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return [.. parts];
    }
}
