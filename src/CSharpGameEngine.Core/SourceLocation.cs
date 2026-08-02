namespace CSharpGameEngine.Core;

public readonly struct SourceLocation : IEquatable<SourceLocation>
{
    public static SourceLocation Unknown { get; } = new(null, 0, 0);

    public SourceLocation(string? filePath, int line, int column)
    {
        if (line < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        if (column < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
        Line = line;
        Column = column;
    }

    public string? FilePath { get; }

    /// <summary>
    /// 1始まりの行番号。Unknown のときは 0。
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// 1始まりの列番号。Unknown のときは 0。
    /// </summary>
    public int Column { get; }

    public bool IsUnknown => FilePath is null && Line == 0 && Column == 0;

    public override string ToString()
    {
        if (IsUnknown)
        {
            return "<unknown>";
        }

        if (FilePath is null)
        {
            return $"(Line: {Line}, Column: {Column})";
        }

        return $"{FilePath}({Line},{Column})";
    }

    public bool Equals(SourceLocation other) =>
        StringComparer.Ordinal.Equals(FilePath, other.FilePath) &&
        Line == other.Line &&
        Column == other.Column;

    public override bool Equals(object? obj) => obj is SourceLocation other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(FilePath ?? string.Empty), Line, Column);

    public static bool operator ==(SourceLocation left, SourceLocation right) => left.Equals(right);

    public static bool operator !=(SourceLocation left, SourceLocation right) => !left.Equals(right);
}
