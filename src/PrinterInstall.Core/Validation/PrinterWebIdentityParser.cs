using System.Net;
using System.Text.RegularExpressions;
using PrinterInstall.Core.Models;

namespace PrinterInstall.Core.Validation;

internal static partial class PrinterWebIdentityParser
{
    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>|<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 200)]
    private static partial Regex NonVisibleContent();

    [GeneratedRegex(@"<(title|h1|header)\b[^>]*>(?<content>.*?)</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 200)]
    private static partial Regex IdentitySections();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex Images();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex Metadata();

    [GeneratedRegex("(?<name>[a-z][a-z0-9:_-]*)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex Attributes();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Tags();

    [GeneratedRegex(@"<(?:td|th)\b[^>]*>(?<label>.*?)</(?:td|th)>\s*<td\b[^>]*>(?<value>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 200)]
    private static partial Regex TableFields();

    [GeneratedRegex(@"<(?<tag>div|span|p)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex NamedElements();

    [GeneratedRegex(@"<(?:frame|iframe)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex Frames();

    [GeneratedRegex(@"<a\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex Links();

    [GeneratedRegex(@"<script\b[^>]*>(?<content>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 200)]
    private static partial Regex Scripts();

    [GeneratedRegex("(?:\\b(?:(?:window|document|top)\\.)?location(?:\\.href)?\\s*=\\s*|\\blocation\\.(?:replace|assign)\\s*\\(\\s*)['\"](?<path>[^'\"]+)['\"]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex LiteralRedirects();

    internal static PrinterIdentityResult Parse(string host, string html, string source = "HTTP Web Config")
    {
        var visible = NonVisibleContent().Replace(html, " ");
        var fragments = new List<string>();
        foreach (Match section in IdentitySections().Matches(visible))
        {
            // A marca pode estar no texto alternativo do logotipo e o modelo, no título ou cabeçalho.
            fragments.Add(ReadText(section.Groups["content"].Value));
        }

        foreach (Match metadata in Metadata().Matches(visible))
            if (IsIdentityLabel(ReadAttribute(metadata.Value, "name")) ||
                string.Equals(ReadAttribute(metadata.Value, "name"), "author", StringComparison.OrdinalIgnoreCase))
                fragments.Add(ReadAttribute(metadata.Value, "content"));

        foreach (Match field in TableFields().Matches(visible))
            if (IsIdentityLabel(ReadText(field.Groups["label"].Value)))
                fragments.Add(ReadText(field.Groups["value"].Value));

        foreach (Match element in NamedElements().Matches(visible))
        {
            var identifiers = (ReadAttribute(element.Value, "id") + " " + ReadAttribute(element.Value, "class"))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!identifiers.Any(IsIdentityLabel))
                continue;
            var start = element.Index + element.Length;
            var end = visible.IndexOf("</" + element.Groups["tag"].Value, start, StringComparison.OrdinalIgnoreCase);
            if (end >= start)
                fragments.Add(ReadText(visible[start..end]));
        }

        // Não usa menus, scripts ou textos do corpo que possam citar outros produtos.
        return PrinterIdentityParser.Parse(host, string.Join(" ", fragments), source);
    }

    internal static IReadOnlyList<string> GetNavigationTargets(string html)
    {
        var targets = new List<string>();
        var visible = NonVisibleContent().Replace(html, " ");
        AddMetaRefreshTargets(visible, targets);
        foreach (Match frame in Frames().Matches(visible))
            targets.Add(ReadAttribute(frame.Value, "src"));

        // Página de informações do firmware Gainscha; não percorre menus de controle ou configuração.
        foreach (Match link in Links().Matches(visible))
        {
            var href = ReadAttribute(link.Value, "href");
            if (href.EndsWith("jb_status_look.shtml", StringComparison.OrdinalIgnoreCase))
                targets.Add(href);
        }

        // Lê apenas endereços literais, sem executar JavaScript nem seguir links de configuração.
        foreach (Match script in Scripts().Matches(html))
        {
            var content = script.Groups["content"].Value;
            AddMetaRefreshTargets(content, targets);
            foreach (Match redirect in LiteralRedirects().Matches(content))
                targets.Add(WebUtility.HtmlDecode(redirect.Groups["path"].Value));
        }
        return targets.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal).Take(4).ToArray();
    }

    private static void AddMetaRefreshTargets(string html, List<string> targets)
    {
        foreach (Match metadata in Metadata().Matches(html))
        {
            if (!string.Equals(ReadAttribute(metadata.Value, "http-equiv"), "refresh", StringComparison.OrdinalIgnoreCase))
                continue;
            var content = ReadAttribute(metadata.Value, "content");
            var url = content.IndexOf("url", StringComparison.OrdinalIgnoreCase);
            var equals = url >= 0 ? content.IndexOf('=', url) : -1;
            if (equals >= 0)
                targets.Add(content[(equals + 1)..].Trim().Trim('\'', '"'));
        }
    }

    private static string ReadText(string content)
    {
        var withLogo = Images().Replace(content, image => " " + ReadAttribute(image.Value, "alt") + " ");
        return WebUtility.HtmlDecode(Tags().Replace(withLogo, " ")).Trim();
    }

    private static bool IsIdentityLabel(string label)
    {
        var normalized = new string(label.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized is "model" or "modelname" or "printermodel" or "devicemodel" or "productmodel" or "productname" or
            "modelo" or "nomedomodelo" or "manufacturer" or "fabricante" or "vendor" or "brand" or "marca";
    }

    private static string ReadAttribute(string tag, string name)
    {
        foreach (Match attribute in Attributes().Matches(tag))
            if (string.Equals(attribute.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
                return WebUtility.HtmlDecode(attribute.Groups["value"].Value);
        return "";
    }
}
