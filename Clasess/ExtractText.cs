using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System.IO;
using System.Linq;
using System.Net;

namespace Jornal
{
    internal class ExtractText
    {
        static public string ExtractTextFromPdf(string filePath)
        {
            using (PdfReader reader = new PdfReader(filePath))
            {
                System.Text.StringBuilder text = new System.Text.StringBuilder();
                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    text.AppendLine(PdfTextExtractor.GetTextFromPage(reader, i, new SimpleTextExtractionStrategy()));
                }

                return text.ToString();
            }
        }
        static public string ExtractTextFromHtml(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("HTML файл не найден", filePath);
            }

            string htmlContent = File.ReadAllText(filePath);

            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
            htmlDoc.LoadHtml(htmlContent);

            // Удаляем скрипты и стили
            foreach (var node in htmlDoc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
            {
                node.Remove();
            }
            // Получаем текст и декодируем HTML-сущности (включая &nbsp;)
            string text = WebUtility.HtmlDecode(htmlDoc.DocumentNode.InnerText);
            // Удаляем или заменяем &nbsp; и другие специальные пробелы
            text = text.Replace("\u00A0", " "); // Заменяем неразрывный пробел на обычный
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }
    }
}
