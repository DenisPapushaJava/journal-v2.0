namespace Jornal.Parsing.Specifications;

public enum SpecificationField
{
    Header, CuttingType, Material, Dimensions, SheetCount, CutLength, Weight, PartCount, PunchCount, CuttingDuration
}

public enum SpecificationIssueSeverity { Warning, Error }

public sealed record SpecificationIssue(SpecificationField Field, string Code, string Message,
    SpecificationIssueSeverity Severity = SpecificationIssueSeverity.Error, IReadOnlyList<HtmlValueSource>? Sources = null);

// Индексы таблицы, строки и ячейки — с нуля, как в SourceDocument; значение не зависит от имени внешнего файла.
public sealed record HtmlValueSource(int Table, int Row, int Cell, string Label, string RawText);

public sealed record SpecificationValue<T>(T Value, IReadOnlyList<HtmlValueSource> Sources);

public sealed record SpecificationHeader(DateOnly OrderDate, string OriginalIdentifier, string JournalNumber, string OrderName);
public sealed record SheetDimensions(decimal ThicknessMillimeters, decimal LengthMillimeters, decimal WidthMillimeters);

public sealed record SpecificationParseResult(
    string Profile,
    SpecificationValue<SpecificationHeader>? Header,
    SpecificationValue<string>? CuttingType,
    SpecificationValue<string>? Material,
    SpecificationValue<SheetDimensions>? Dimensions,
    SpecificationValue<int>? SheetCount,
    SpecificationValue<decimal>? CutLengthMillimeters,
    SpecificationValue<decimal>? WeightKilograms,
    SpecificationValue<int>? PartCount,
    SpecificationValue<int>? PunchCount,
    SpecificationValue<TimeSpan>? CuttingDuration,
    IReadOnlyList<SpecificationIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == SpecificationIssueSeverity.Error);
    public bool IsComplete => !HasErrors && Header is not null && CuttingType is not null && Material is not null &&
        Dimensions is not null && SheetCount is not null && CutLengthMillimeters is not null && WeightKilograms is not null &&
        PartCount is not null && PunchCount is not null;
}
