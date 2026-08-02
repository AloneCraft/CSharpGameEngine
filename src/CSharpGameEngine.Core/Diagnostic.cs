namespace CSharpGameEngine.Core;

public readonly struct Diagnostic
{
    public Diagnostic(string code, DiagnosticSeverity severity, string message, SourceLocation location)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Diagnostic code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Diagnostic message is required.", nameof(message));
        }

        Code = code;
        Severity = severity;
        Message = message;
        Location = location;
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    public string Message { get; }

    public SourceLocation Location { get; }

    public override string ToString()
    {
        if (Location.IsUnknown)
        {
            return $"[{Severity}] {Code}: {Message}";
        }

        return $"[{Severity}] {Code}: {Message} @ {Location}";
    }
}
