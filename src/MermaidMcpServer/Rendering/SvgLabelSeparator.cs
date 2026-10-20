using System.Globalization;
using System.Xml.Linq;

namespace MermaidMcpServer.Rendering;

// Постобработка SVG от Mermaider: разводит подписи рёбер, которые лежат друг на друге,
// на узлах или выходят за край холста. Параллельные рёбра (A->B дважды) библиотека
// рисует одним путём и ставит обе подписи в одну точку — ни обновление версии, ни
// увеличение интервалов этого не лечат.
// ponytail: жадный перебор смещений по одной подписи за раз; если из ~24 положений
// ни одно не свободно, подпись остаётся на месте. Для очень плотных графов — увеличить LayerSpacing.
public static class SvgLabelSeparator
{
    private const double Gap = 8;
    private const int Steps = 6;

    private readonly record struct Box(double X, double Y, double W, double H)
    {
        public double R => this.X + this.W;
        public double B => this.Y + this.H;

        public bool Hits(Box o) =>
            Math.Min(this.R, o.R) - Math.Max(this.X, o.X) > 1 &&
            Math.Min(this.B, o.B) - Math.Max(this.Y, o.Y) > 1;

        public bool Inside(Box o) => this.X >= o.X && this.Y >= o.Y && this.R <= o.R && this.B <= o.B;
    }

    public static string Separate(string svg)
    {
        var doc = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
        var ns = doc.Root!.Name.Namespace;

        static double Num(XElement e, string name) =>
            double.Parse(e.Attribute(name)!.Value, CultureInfo.InvariantCulture);

        static bool HasClass(XElement e, string cls) =>
            ((string?)e.Attribute("class"))?.Split(' ').Contains(cls) == true;

        var vb = doc.Root.Attribute("viewBox")?.Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.Parse(s, CultureInfo.InvariantCulture))
            .ToArray();
        if (vb is not { Length: 4 })
            return svg;
        var canvas = new Box(vb[0], vb[1], vb[2], vb[3]);

        var nodes = new List<Box>();
        var labels = new List<(XElement Group, Box Box)>();
        foreach (var g in doc.Descendants(ns + "g"))
        {
            if (HasClass(g, "node"))
            {
                var rect = g.Elements(ns + "rect").FirstOrDefault();
                if (rect is not null)
                {
                    nodes.Add(new Box(Num(rect, "x"), Num(rect, "y"), Num(rect, "width"), Num(rect, "height")));
                }
                else if (g.Elements(ns + "circle").FirstOrDefault() is { } circle)
                {
                    var r = Num(circle, "r");
                    nodes.Add(new Box(Num(circle, "cx") - r, Num(circle, "cy") - r, r * 2, r * 2));
                }
            }
            else if (HasClass(g, "edge-label") && g.Element(ns + "rect") is { } labelRect)
            {
                labels.Add((g, new Box(Num(labelRect, "x"), Num(labelRect, "y"), Num(labelRect, "width"), Num(labelRect, "height"))));
            }
        }

        var placed = new List<Box>();
        var changed = false;
        foreach (var (group, box) in labels)
        {
            bool Free(Box b) => b.Inside(canvas) && !nodes.Any(b.Hits) && !placed.Any(b.Hits);

            if (Free(box))
            {
                placed.Add(box);
                continue;
            }

            // Сначала «рядом с линией» (сдвиг на полразмера + зазор), затем всё дальше.
            var candidates = new List<(double Dx, double Dy)>();
            for (var k = 0; k < Steps; k++)
            {
                var dy = box.H / 2 + Gap + k * (box.H + Gap);
                var dx = box.W / 2 + Gap + k * (box.W + Gap);
                candidates.Add((0, -dy));
                candidates.Add((0, dy));
                candidates.Add((-dx, 0));
                candidates.Add((dx, 0));
            }

            var spot = candidates
                .OrderBy(c => Math.Abs(c.Dx) + Math.Abs(c.Dy))
                .Select(c => (c.Dx, c.Dy, Moved: box with { X = box.X + c.Dx, Y = box.Y + c.Dy }))
                .FirstOrDefault(c => Free(c.Moved));

            if (spot.Moved == default)
            {
                placed.Add(box);
                continue;
            }

            group.SetAttributeValue("transform",
                string.Create(CultureInfo.InvariantCulture, $"translate({spot.Dx:0.##},{spot.Dy:0.##})"));
            placed.Add(spot.Moved);
            changed = true;
        }

        return changed ? doc.Root.ToString(SaveOptions.DisableFormatting) : svg;
    }
}
