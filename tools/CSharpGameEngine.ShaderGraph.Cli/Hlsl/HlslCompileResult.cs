namespace CSharpGameEngine.Compiler.Hlsl;

public sealed class HlslCompileResult {
    private static readonly byte[]EmptyBytecode=[];

    private HlslCompileResult(bool Success,byte[]Bytecode,IReadOnlyList<HlslDiagnostic>Diagnostics,byte[]?DxcReflection=null){
        this.Success=Success;
        this.Bytecode=Bytecode;
        this.Diagnostics=Diagnostics;
        this.DxcReflection=DxcReflection??[];
    }

    public bool Success { get;}

    /// <summary>DXIL bytecode. Always empty when <see cref="Success"/> is false.</summary>
    public byte[]Bytecode { get;}

    /// <summary>DXC_OUT_REFLECTION data from a real compile, separate from artifact entry metadata.</summary>
    public byte[]DxcReflection { get;}
    public IReadOnlyList<HlslDiagnostic>Diagnostics { get;}

    public static HlslCompileResult Succeeded(byte[]Bytecode,IReadOnlyList<HlslDiagnostic>Diagnostics,byte[]?DxcReflection=null)=>
        Bytecode.Length==0
            ?throw new ArgumentException("A successful compile result must carry non-empty bytecode.",nameof(Bytecode))
            :new HlslCompileResult(true,Bytecode,Diagnostics,DxcReflection);

    public static HlslCompileResult Failed(IReadOnlyList<HlslDiagnostic>Diagnostics)=>
        new(false,EmptyBytecode,Diagnostics);
}
