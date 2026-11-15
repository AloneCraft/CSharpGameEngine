using System.Collections;

namespace CSharpGameEngine.Core;

public sealed class DiagnosticBag:IReadOnlyCollection<Diagnostic>{
    private List<Diagnostic>?Items;
    private bool HasErrorsValue;

    public int Count=>this.Items?.Count??0;

    public bool HasErrors=>this.HasErrorsValue;

    public void Add(Diagnostic Diagnostic){
        this.Items??=[];
        this.Items.Add(Diagnostic);

        if(Diagnostic.Severity==DiagnosticSeverity.Error){
            this.HasErrorsValue=true;
        }
    }

    public IEnumerator<Diagnostic>GetEnumerator(){
        return this.Items is null
            ?((IEnumerable<Diagnostic>)Array.Empty<Diagnostic>()).GetEnumerator()
            :this.Items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()=>this.GetEnumerator();
}
