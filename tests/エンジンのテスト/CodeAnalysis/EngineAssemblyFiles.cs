using Xunit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// テストのコンパイルに渡す参照から、エンジンの DLL を見分ける。
/// DLL 名には接頭辞が付かないので（ADR-0099）、名前ではなく、CSharpGameEngine の名前空間の型を持つかで判定する。
/// </summary>
internal static class EngineAssemblyFiles {
    internal static bool IsEngineAssembly(string Path){
        try {
            using var Stream=File.OpenRead(Path);
            using var Reader=new PEReader(Stream);
            if(!Reader.HasMetadata){ return false; }
            var Metadata=Reader.GetMetadataReader();
            foreach(var Handle in Metadata.TypeDefinitions){
                var Namespace=Metadata.GetString(Metadata.GetTypeDefinition(Handle).Namespace);
                if(Namespace=="CSharpGameEngine"||Namespace.StartsWith("CSharpGameEngine.",StringComparison.Ordinal)){ return true; }
            }
            return false;
        } catch(BadImageFormatException) {
            return false;
        }
    }
}
