using System.Text.RegularExpressions;

namespace PrinterInstall.Core.Gainscha;

internal static class GainschaLabelImportSdsComposer
{
    /// <summary>
    /// Reserved for templates captured with a real &lt;stock&gt; block (see Capture-GainschaLabelPreset.ps1).
    /// Preset Data is not valid stock Data — do not use for deploy until templates include captured stock blobs.
    /// </summary>
    public static string ComposeForImport(string templateContent, string userStockDisplayName)
    {
        ArgumentException.ThrowIfNullOrEmpty(templateContent);
        ArgumentException.ThrowIfNullOrEmpty(userStockDisplayName);

        if (templateContent.Contains("<stock>", StringComparison.OrdinalIgnoreCase))
            return templateContent;

        var presetMatch = Regex.Match(
            templateContent,
            @"<preset[^>]*>\s*Data=([^\r\n]+)",
            RegexOptions.IgnoreCase);

        if (!presetMatch.Success)
        {
            throw new InvalidOperationException(
                "Template SDS sem bloco <stock> precisa de <preset Data=...> para compor o stock USER.");
        }

        var stockBlock =
            $"<stock>{Environment.NewLine}" +
            $"Name={userStockDisplayName}{Environment.NewLine}" +
            $"Data={presetMatch.Groups[1].Value.Trim()}{Environment.NewLine}" +
            $"</stock>{Environment.NewLine}";

        return Regex.Replace(
            templateContent,
            @"(<driver version='[^']+'>\s*)",
            $"$1{stockBlock}",
            RegexOptions.IgnoreCase);
    }
}
