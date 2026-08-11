using System.Collections;

namespace CSharpGameEngine.Core;

public sealed class DiagnosticBag : IReadOnlyCollection<Diagnostic>
{
    private List<Diagnostic>? Items;
    private bool hasErrors;

    public int Count => this.Items?.Count ?? 0;

    public bool HasErrors => this.hasErrors;

    public void Add(Diagnostic diagnostic)
    {
        this.Items ??= [];
        this.Items.Add(diagnostic);

        if (diagnostic.Severity == DiagnosticSeverity.Error)
        {
            this.hasErrors = true;
        }
    }

    public IEnumerator<Diagnostic> GetEnumerator()
    {
        return this.Items is null
            ? ((IEnumerable<Diagnostic>)Array.Empty<Diagnostic>()).GetEnumerator()
            : this.Items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
