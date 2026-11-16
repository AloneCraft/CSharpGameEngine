using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;
namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class NumericSnapshotProfileTests {
    [Fact]
    public void RealSnapshotArtifactsCrossActualStorageProfilesAndRejectOverflowAtomically(){
        using var Source=Compile("Fixed",128,64);
        var Value="1.0000000000009094947017729282379150390625";
        var Snapshot=(byte[])Source.Call("Capture",Value,"2")!;
        var Common=(byte[])Source.Call("Capture","1.25","2")!;
        var Overflow=(byte[])Source.Call("Capture","1","1099511627776")!;
        foreach(var Profile in new[]{("Fixed",64,32,"1"),("Fixed",128,64,Value),("Fixed",192,96,Value),("Float",0,0,"1"),("Double",0,0,Value)}){
            using var Target=Compile(Profile.Item1,Profile.Item2,Profile.Item3);
            Assert.Equal(Source.Call("Schema"),Target.Call("Schema"));
            Assert.Equal(Common,(byte[])Target.Call("Capture","1.25","2")!);
            Assert.Equal("OK",Target.Call("Restore",Snapshot,true,Profile.Item4,"2"));
            Assert.Equal("OK",Target.Call("PendingAndUpdate",Snapshot,true,Profile.Item4,"2"));
            if(Profile.Item1=="Fixed"&&Profile.Item2==64){
                Assert.Equal("OK",Target.Call("Restore",Overflow,false,"9","9"));
                Assert.Equal("OK",Target.Call("PendingAndUpdate",Overflow,false,"9","9"));
            }
        }
    }
    private static ProfileAssembly Compile(string Mode,int Width,int Fraction){
        var Custom=Mode=="Fixed"&&(Width!=128||Fraction!=64);
        var Symbols=Mode!="Fixed"?new[]{"CGE_NUMERIC_"+Mode.ToUpperInvariant()}:Custom?new[]{"CGE_NUMERIC_CUSTOM_FIXED","CGE_FIXED_"+Width}.Concat(Fraction<32||Fraction>Width-4?["CGE_FIXED_EXTENDED_MATH"]:[]).ToArray():[];
        var Options=new CSharpParseOptions(LanguageVersion.Preview,preprocessorSymbols:Symbols);
        var Base=GameSystemGeneratorTestHelper.CreateCompilation(Probe);
        var Trees=Base.SyntaxTrees.Select(Tree=>CSharpSyntaxTree.ParseText(Tree.GetText(),Options,Tree.FilePath)).ToList();
        Trees.AddRange(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"TestAssets","Numerics"),"*.cs")
            .Where(Path=>Mode=="Fixed"&&!Custom||System.IO.Path.GetFileName(Path)!="LogicalScalar.cs")
            .Select(Path=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path),Options,Path)));
        if(Custom){ Trees.Add(CSharpSyntaxTree.ParseText($"namespace CSharpGameEngine.Numerics;internal static class NumericBuildProfile {{internal const int TotalBits={Width};internal const int FractionalBits={Fraction};}}",Options)); }
        if(Mode!="Fixed"){ Trees.Add(CSharpSyntaxTree.ParseText("global using LogicalScalar=System."+(Mode=="Float"?"Single":"Double")+";",Options)); }
        var References=Base.References.Where(static Reference=>System.IO.Path.GetFileName(Reference.Display) is not ("Numerics.dll" or "Entities.dll"));
        var Compilation=CSharpCompilation.Create("SnapshotProfile_"+Guid.NewGuid().ToString("N"),Trees,References,Base.Options.WithNullableContextOptions(NullableContextOptions.Enable).WithGeneralDiagnosticOption(ReportDiagnostic.Error));
        GeneratorDriver Driver=CSharpGeneratorDriver.Create([new GameSystemGenerator().AsSourceGenerator()],parseOptions:Options);
        Driver.RunGeneratorsAndUpdateCompilation(Compilation,out var Generated,out var Diagnostics);
        Assert.DoesNotContain(Diagnostics,static Diagnostic=>Diagnostic.Severity==DiagnosticSeverity.Error);
        using var Stream=new MemoryStream();var Emitted=Generated.Emit(Stream);Assert.True(Emitted.Success,string.Join(Environment.NewLine,Emitted.Diagnostics));
        Stream.Position=0;var Context=new AssemblyLoadContext(Compilation.AssemblyName!,true);
        return new(Context,Context.LoadFromStream(Stream));
    }
    private sealed class ProfileAssembly(AssemblyLoadContext Context,Assembly Assembly):IDisposable {
        internal object?Call(string Method,params object[]Arguments)=>Assembly.GetType("CSharpGameEngine.Entities.ProfileProbe")!.GetMethod(Method)!.Invoke(null,Arguments);
        public void Dispose()=>Context.Unload();
    }
    private const string Probe="""
        using CSharpGameEngine.Entities;
        using CSharpGameEngine.Numerics;
        namespace Portable {
            public struct Position {public LogicalScalar X;}
            internal struct System:ISystem {public void Update(in Position Value){}}
        }
        namespace CSharpGameEngine.Entities {
            public static class ProfileProbe {
                private static readonly CSharpGameEngine.Contracts.WorldCellId Cell=CSharpGameEngine.Contracts.WorldCellId.Parse("30000000-0000-5000-8000-000000000001");
                private static readonly CSharpGameEngine.Contracts.WorldCellId OtherCell=CSharpGameEngine.Contracts.WorldCellId.Parse("30000000-0000-5000-8000-000000000002");
                private static readonly CSharpGameEngine.Contracts.PersistentEntityId First=CSharpGameEngine.Contracts.PersistentEntityId.Parse("40000000-0000-5000-8000-000000000001");
                private static readonly CSharpGameEngine.Contracts.PersistentEntityId Second=CSharpGameEngine.Contracts.PersistentEntityId.Parse("40000000-0000-5000-8000-000000000002");
                public static string Schema()=>World.SnapshotSchemaFingerprint;
                public static byte[]Capture(string A,string B){
                    var World=new World();World.AttachToCell(Cell,First,World.Create(new Portable.Position{X=LogicalScalar.ParseInvariant(A)}));
                    World.AttachToCell(OtherCell,Second,World.Create(new Portable.Position{X=LogicalScalar.ParseInvariant(B)}));
                    return World.CaptureSnapshot(7,new string('0',64));
                }
                public static string Restore(byte[]Artifact,bool Expected,string A,string B){
                    var World=new World();var One=World.AttachToCell(Cell,First,World.Create(new Portable.Position{X=9}));
                    var Two=World.AttachToCell(OtherCell,Second,World.Create(new Portable.Position{X=9}));
                    var Result=World.RestoreSnapshot(Artifact);
                    if(Result.Succeeded!=Expected)return Result.Error.Message;
                    if(!World.TryGetComponent(One,out Portable.Position PA)||!World.TryGetComponent(Two,out Portable.Position PB))return "entity changed";
                    if(PA.X.ToStringInvariant()!=A||PB.X.ToStringInvariant()!=B)return "wrong values or partial mutation";
                    return "OK";
                }
                public static string PendingAndUpdate(byte[]Artifact,bool Expected,string A,string B){
                    var World=new World();var Initial=World.RestoreSnapshot(Artifact);
                    if(Initial.Succeeded!=Expected)return "wrong pending validation";
                    if(!Expected)return "OK";
                    if(Initial.Pending?.CellCount!=2)return "missing pending cells";
                    var Merged=World.UpdateSnapshot(Artifact,8,new string('0',64));
                    var One=World.AttachToCell(Cell,First,World.Create(new Portable.Position{X=9}));
                    var FirstPass=World.RestoreSnapshot(Merged);
                    if(!FirstPass.Succeeded||FirstPass.Pending?.CellCount!=1)return "wrong merged pending cells";
                    if(!World.TryGetComponent(One,out Portable.Position PA)||PA.X.ToStringInvariant()!=A)return "wrong first cell";
                    var Two=World.AttachToCell(OtherCell,Second,World.Create(new Portable.Position{X=9}));
                    var Last=World.ApplyPendingSnapshot(FirstPass.Pending);
                    if(!Last.Succeeded||Last.Pending is not null)return "pending restore failed";
                    if(!World.TryGetComponent(Two,out Portable.Position PB)||PB.X.ToStringInvariant()!=B)return "wrong second cell";
                    return "OK";
                }
            }
        }
        """;
}
