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

    /// <summary>DXIL バイトコード。<see cref="Success"/> が false の場合は常に空。</summary>
    public byte[]Bytecode { get;}

    /// <summary>実際のコンパイルで得た DXC_OUT_REFLECTION データ。成果物エントリのメタデータとは別。</summary>
    public byte[]DxcReflection { get;}
    public IReadOnlyList<HlslDiagnostic>Diagnostics { get;}

    public static HlslCompileResult Succeeded(byte[]Bytecode,IReadOnlyList<HlslDiagnostic>Diagnostics,byte[]?DxcReflection=null)=>
        Bytecode.Length==0
            ?throw new ArgumentException("A successful compile result must carry non-empty bytecode.",nameof(Bytecode))
            :new HlslCompileResult(true,Bytecode,Diagnostics,DxcReflection);

    public static HlslCompileResult Failed(IReadOnlyList<HlslDiagnostic>Diagnostics)=>
        new(false,EmptyBytecode,Diagnostics);
}
