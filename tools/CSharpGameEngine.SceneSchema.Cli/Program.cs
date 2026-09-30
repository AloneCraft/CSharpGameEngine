using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
namespace CSharpGameEngine.SceneSchema.Cli;
/// <summary>
/// <c>CSharpGameEngine.SceneSchema.Cli &lt;game assembly&gt; &lt;output schema&gt;</c> (ADR-0070). Copies the
/// generated scene JSON Schema out of the assembly's metadata. The file is rewritten only when its
/// content changes, so the VS JSON editor does not reload it on every build.
/// </summary>
internal static class Program {
    private static int Main(string[] Arguments){
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
            Console.Error.WriteLine($"CGE-SCENE-SCHEMA failed: {Error.Message}");
            return 1;
        }
    }
}
internal static class SceneSchemaExtractor {
    internal const string Namespace="CSharpGameEngine.Generated";
    internal const string TypeName="CgeGeneratedSceneSchema";
    internal const string FieldName="Json";
    /// <summary>Reads the string constant <c>CSharpGameEngine.Generated.CgeGeneratedSceneSchema.Json</c>.</summary>
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
    /// <summary>Writes UTF-8 without a BOM when the file is missing or different; returns whether it wrote.</summary>
    internal static bool WriteIfChanged(string Path,string Content){
        if(File.Exists(Path)&&File.ReadAllText(Path,Encoding.UTF8)==Content){ return false; }
        var Directory=System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if(Directory is not null){ System.IO.Directory.CreateDirectory(Directory); }
        File.WriteAllText(Path,Content,new UTF8Encoding(encoderShouldEmitUTF8Identifier:false));
        return true;
    }
}
