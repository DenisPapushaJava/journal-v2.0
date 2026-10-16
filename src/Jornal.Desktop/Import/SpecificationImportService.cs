using System.Globalization;
using Jornal.Core.Pricing;
using Jornal.Core.Orders;
using Jornal.Parsing.Documents;
using Jornal.Parsing.Specifications;

namespace Jornal.Desktop.Import;

public interface ISpecificationImportService
{
    Task<SpecificationImportPreview> ReadAsync(string path, CancellationToken cancellationToken);
}

public sealed class SpecificationImportService : ISpecificationImportService
{
    public static string? CheckFiles(IReadOnlyList<string> paths) => paths.Count != 1
        ? "Перетащите одну спецификацию. Несколько файлов пока не импортируются одновременно."
        : string.IsNullOrWhiteSpace(paths[0]) ? "Перетащите файл из локальной или подключённой сетевой папки. Папки не импортируются."
        : Path.GetExtension(paths[0]).ToLowerInvariant() is not (".html" or ".htm" or ".pdf")
            ? "Выберите HTML БЛОК/Wintech (.html, .htm) или PDF HansLaser (.pdf)."
            : null;

    public Task<SpecificationImportPreview> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (CheckFiles([path]) is { } error) throw new NotSupportedException(error);
        return Task.Run(async () =>
        {
            var document = await new DocumentReader().ReadAsync(path, cancellationToken);
            var report = document.Kind == DocumentKind.Pdf ? new HansPdfSpecificationParser().Parse(document, cancellationToken) :
                new BlockHtmlSpecificationParser().Parse(document, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return SpecificationImportPreview.Create(Path.GetFileName(path), report);
        }, cancellationToken);
    }
}

public sealed record ImportedField(string Value, string Evidence);

public sealed record SpecificationImportPreview(string FileName, SpecificationParseResult Report,
    IReadOnlyDictionary<string, ImportedField> Fields, int? PartCount, int? PunchCount)
{
    public string Diagnostics => string.Join("\n\n", Report.Issues.Select(issue => issue.Message +
        (issue.Sources is { Count: > 0 } sources ? "\nВ документе: " + string.Join("; ", sources.Select(source => Short(source.RawText)).Distinct()) : "")));
    public string Duration => Report.CuttingDuration is { } duration
        ? "Время резки: " + CuttingDuration.Format(CuttingDuration.RoundUpMinutes(duration.Value))
        : "Время резки не распознано";
    public string Counts => $"Детали: {PartCount?.ToString() ?? "не распознано"} · Пробивки: {PunchCount?.ToString() ?? "не распознано"}";

    public static SpecificationImportPreview Create(string fileName, SpecificationParseResult report)
    {
        var fields = new Dictionary<string, ImportedField>();
        bool Bad(SpecificationField field) => report.Issues.Any(issue => issue.Field == field && issue.Severity == SpecificationIssueSeverity.Error);
        void Add(string name, SpecificationField field, string? value, IEnumerable<SpecificationValueSource>? sources)
        {
            sources ??= report.Issues.Where(issue => issue.Field == field).SelectMany(issue => issue.Sources ?? []);
            var evidence = string.Join("\n", sources.Distinct().Select(source => source switch
            {
                HtmlValueSource html => $"Таблица {html.Table + 1}, строка {html.Row + 1}: {Short(html.Label)} {Short(html.RawText)}",
                PdfValueSource pdf => $"Страница {pdf.Page}, область ({pdf.Left:0.#}; {pdf.Bottom:0.#})–({pdf.Right:0.#}; {pdf.Top:0.#}): {Short(pdf.Label)} {Short(pdf.RawText)}",
                _ => $"{Short(source.Label)} {Short(source.RawText)}"
            }));
            fields[name] = new(Bad(field) ? "" : value ?? "", evidence);
        }
        string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
        var header = report.Header;
        Add("OrderDate", SpecificationField.Header, header?.Value.OrderDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), header?.Sources);
        Add("SpecificationNumber", SpecificationField.Header, header?.Value.JournalNumber, header?.Sources);
        Add("CuttingType", SpecificationField.CuttingType, report.CuttingType?.Value, report.CuttingType?.Sources);
        Add("Material", SpecificationField.Material, report.Material?.Value, report.Material?.Sources);
        var layouts = report.SheetLayouts.Select(layout => layout.Value.Dimensions).Distinct().ToArray();
        var layoutSources = report.SheetLayouts.SelectMany(layout => layout.Sources).ToArray();
        Add("Thickness", SpecificationField.Dimensions, layouts.Select(size => size.ThicknessMillimeters).Distinct().Count() == 1
            ? Number(layouts[0].ThicknessMillimeters) : null, layoutSources);
        if (report.Issues.Any(issue => issue.Code == "MaterialThicknessMismatch")) fields["Thickness"] = new("", fields["Thickness"].Evidence);
        Add("SheetSize", SpecificationField.Dimensions, layouts.Length > 0 ? string.Join(", ", layouts.Select(size =>
            Number(size.WidthMillimeters) + "x" + Number(size.LengthMillimeters)).Distinct()) : null, layoutSources);
        Add("SheetCount", SpecificationField.SheetCount, report.SheetCount is { } count ? Number(count.Value) : null, report.SheetCount?.Sources);
        Add("Weight", SpecificationField.Weight, report.WeightKilograms is { } weight ? Number(weight.Value) : null, report.WeightKilograms?.Sources);
        Add("CutLength", SpecificationField.CutLength, report.CutLengthMillimeters is { } length && !Bad(SpecificationField.CutLength)
            ? (report.Profile == HansPdfSpecificationParser.Profile ? HansSpecificationNormalizer.LegacyCutLengthMeters(length.Value) :
                BlockSpecificationNormalizer.LegacyCutLengthMeters(length.Value)).ToString(CultureInfo.InvariantCulture) : null, report.CutLengthMillimeters?.Sources);
        Add("CuttingTime", SpecificationField.CuttingDuration, report.CuttingDuration is { } duration
            ? CuttingDuration.Format(CuttingDuration.RoundUpMinutes(duration.Value)) : null, report.CuttingDuration?.Sources);
        Add("MaterialId", SpecificationField.MaterialId, report.MaterialId?.Value, report.MaterialId?.Sources);
        Add("Gas", SpecificationField.CuttingGas, report.CuttingGas?.Value, report.CuttingGas?.Sources);
        var parts = !Bad(SpecificationField.PartCount) ? report.PartCount?.Value : null;
        var punches = !Bad(SpecificationField.PunchCount) ? report.PunchCount?.Value : null;
        fields["Coefficient"] = new(parts is > 0 && punches is >= 0 ? Number(CuttingPriceCalculator.CalculatePunchCoefficient(parts.Value, punches.Value)) : "",
            "По итогам заказа. Пробивки / детали: " + (punches?.ToString() ?? "?") + " / " + (parts?.ToString() ?? "?"));
        return new(fileName, report, fields, parts, punches);
    }

    private static string Short(string text) => text.Length <= 240 ? text : text[..240] + "…";
}
