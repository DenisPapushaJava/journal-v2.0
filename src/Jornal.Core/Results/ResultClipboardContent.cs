using System.Globalization;
using System.Net;
using System.Text;

namespace Jornal.Core.Results;

public sealed record ResultClipboardContent(string Text, string Html, string Rtf)
{
    public static ResultClipboardContent FromText(string text) => new(text, CreateHtml(text), CreateRtf(text));

    private static string CreateHtml(string text)
    {
        var html = new StringBuilder("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;font-family:Arial,sans-serif;font-size:12pt;line-height:1.4;color:#202124;background-color:#ffffff;\"><tbody>");
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var count = lines.Length - (lines[^1].Length == 0 ? 1 : 0);
        var columns = lines.Max(line => line.Count(character => character == '\t') + 1);
        for (var index = 0; index < count; index++)
        {
            html.Append("<tr>");
            var fields = lines[index].Split('\t');
            for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                var field = fields[fieldIndex];
                var span = fieldIndex == fields.Length - 1 ? columns - fields.Length + 1 : 1;
                html.Append("<td colspan=\"").Append(span.ToString(CultureInfo.InvariantCulture))
                    .Append("\" style=\"padding:3px 12px 3px 0;vertical-align:top;white-space:")
                    .Append(fields.Length == 1 ? "pre-wrap" : "normal").Append(";\">");
                html.Append(field.Length == 0 ? "<br>" : WebUtility.HtmlEncode(field));
                html.Append("</td>");
            }
            html.Append("</tr>");
        }
        return html.Append("</tbody></table>").ToString();
    }

    private static string CreateRtf(string text)
    {
        // Самостоятельный простой RTF: Unicode, табуляции, абзацы. Цвет не зависит от темы приложения.
        var rtf = new StringBuilder(@"{\rtf1\ansi\ansicpg1251\deff0\uc1{\fonttbl{\f0\fswiss Arial;}}{\colortbl;\red0\green0\blue0;}\pard\f0\fs24\cf1 ");
        foreach (var character in text.ReplaceLineEndings("\n"))
        {
            switch (character)
            {
                case '\\': case '{': case '}': rtf.Append('\\').Append(character); break;
                case '\t': rtf.Append(@"\tab "); break;
                case '\n': rtf.Append(@"\par ").Append('\n'); break;
                default:
                    if (character is >= ' ' and <= '~') rtf.Append(character);
                    else rtf.Append(@"\u").Append(unchecked((short)character).ToString(CultureInfo.InvariantCulture)).Append('?');
                    break;
            }
        }
        return rtf.Append('}').ToString();
    }
}
