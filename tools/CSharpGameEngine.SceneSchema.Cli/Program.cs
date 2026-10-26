using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
namespace CSharpGameEngine.SceneSchema.Cli;
/// <summary>
/// <c>CSharpGameEngine.SceneSchema.Cli &lt;game assembly&gt; &lt;output schema&gt;</c> (ADR-0070)。生成されたシーン JSON Schema をアセンブリのメタデータからコピーする。Visual Studio の JSON エディターがビルドのたびに再読み込みしないよう、内容が変わった場合にのみファイルを書き換える。
/// </summary>
internal static class Program {
    private static int Main(string[] Arguments)=>Run(Arguments);

    internal static int Run(string[] Arguments){
        if(Arguments.Length!=2){
            Console.Error.WriteLine("usage: CSharpGameEngine.SceneSchema.Cli <game assembly> <output schema>");
            return 2;
        }
        try {
            if(!SceneSchemaExtractor.TryRead(Arguments[0],out var Schema)){
                Console.WriteLine($"CGE-SCENE-SCHEMA none assembly={Arguments[0]}");
                return 0;
            }
            var Written=SceneSchemaExtractor.WriteIfChanged(Arguments[1],Schema);
            Console.WriteLine($"CGE-SCENE-SCHEMA {(Written?"written":"unchanged")} path={Arguments[1]}");
            return 0;
        } catch(Exception Error) when(Error is IOException or BadImageFormatException or UnauthorizedAccessException){
            // MSBuild の標準形式で出力すると、MSB3073 だけでなく原因をスキーマの位置でビルド一覧に示せる。
            Console.Error.WriteLine($"{Arguments[1]}: error CGE-SCENE-SCHEMA: {Error.Message}");
            return 1;
        }
    }
}
internal static class SceneSchemaExtractor {
    internal const string Namespace="CSharpGameEngine.Generated";
    internal const string TypeName="CgeGeneratedSceneSchema";
    internal const string FieldName="Json";
    /// <summary>文字列定数 <c>CSharpGameEngine.Generated.CgeGeneratedSceneSchema.Json</c> を読み取る。</summary>
    internal static bool TryRead(string AssemblyPath,out string Schema){
        Schema=string.Empty;
        using var Stream=File.OpenRead(AssemblyPath);
        using var Pe=new PEReader(Stream);
        if(!Pe.HasMetadata){ return false; }
        var Reader=Pe.GetMetadataReader();
        foreach(var TypeHandle in Reader.TypeDefinitions){
            var Type=Reader.GetTypeDefinition(TypeHandle);
            if(Reader.GetString(Type.Name)!=TypeName||Reader.GetString(Type.Namespace)!=Namespace){ continue; }
            foreach(var FieldHandle in Type.GetFields()){
                var Field=Reader.GetFieldDefinition(FieldHandle);
                if(Reader.GetString(Field.Name)!=FieldName||Field.GetDefaultValue().IsNil){ continue; }
                var Constant=Reader.GetConstant(Field.GetDefaultValue());
                if(Constant.TypeCode!=ConstantTypeCode.String){ return false; }
                var Blob=Reader.GetBlobReader(Constant.Value);
                Schema=Blob.ReadUTF16(Blob.Length);
                return true;
            }
        }
        return false;
    }
    /// <summary>ファイルがない場合、または内容が異なる場合に BOM なし UTF-8 で書き込む。実際に書き込んだかどうかを返す。</summary>
    internal static bool WriteIfChanged(string Path,string Content){
        if(File.Exists(Path)&&File.ReadAllText(Path,Encoding.UTF8)==Content){ return false; }
        var Directory=System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if(Directory is not null){ System.IO.Directory.CreateDirectory(Directory); }
        File.WriteAllText(Path,Content,new UTF8Encoding(encoderShouldEmitUTF8Identifier:false));
        return true;
    }
}
