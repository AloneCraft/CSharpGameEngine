using System.Collections;

namespace CSharpGameEngine.Core;

public sealed class DiagnosticBag : IReadOnlyCollection<Diagnostic>
{
    private List<Diagnostic>? items;
    private bool hasErrors;

    public int Count => this.items?.Count ?? 0;

    public bool HasErrors => this.hasErrors;

    public void Add(Diagnostic diagnostic)
    {
        this.items ??= [];
        this.items.Add(diagnostic);

        if (diagnostic.Severity == DiagnosticSeverity.Error)
        {
            this.hasErrors = true;
        }
    }

    public IEnumerator<Diagnostic> GetEnumerator()
    {
        return this.items is null
            ? ((IEnumerable<Diagnostic>)Array.Empty<Diagnostic>()).GetEnumerator()
            : this.items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
