using System.Text;

namespace MermaidMcpServer.Mermaid;

// Stateless: the diagram code is carried in the preview URL itself, so no server storage is needed.
public static class MermaidPreview
{
    public static string ToBase64Url(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    public static string BuildPreviewUrl(string baseUrl, string code)
        => $"{baseUrl.TrimEnd('/')}/?code={ToBase64Url(code)}";
}
