using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.Loader;
namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class FloatingNumericProfileTests {
    [Theory]
    [InlineData("Float","System.Single",4)]
    [InlineData("Double","System.Double",8)]
    public void NativeProfileStoresPrimitiveValuesAndRunsMathWithoutFixedPoint(string Mode,string TypeName,int Size){
        var Options=new CSharpParseOptions(LanguageVersion.Preview,preprocessorSymbols:["CGE_NUMERIC_"+Mode.ToUpperInvariant()]);
        var Sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"TestAssets","Numerics"),"*.cs")
            .Where(Path=>System.IO.Path.GetFileName(Path)!="LogicalScalar.cs")
            .Select(Path=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path),Options,path:Path)).ToList();
        Sources.Add(CSharpSyntaxTree.ParseText("global using LogicalScalar="+TypeName+"; global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic;",Options));
        Sources.Add(CSharpSyntaxTree.ParseText("""
            using CSharpGameEngine.Numerics;
            public static class Probe {
                public static string Run(string TypeName,int Size) {
                    if(typeof(LogicalVector3).GetProperty("R")!.PropertyType.FullName!=TypeName)return "wrapped coordinate";
                    if(LogicalNumeric.ProfileId!=(Size==4?"float32":"float64")||LogicalNumeric.CacheProfileId!=LogicalNumeric.ProfileId)return "runtime profile identity";
                    if(System.Runtime.InteropServices.Marshal.SizeOf<LogicalVector3>()!=Size*3)return "coordinate layout mismatch";
                    if(System.Runtime.InteropServices.Marshal.SizeOf<LogicalQuaternion>()!=Size*4)return "rotation layout mismatch";
                    var Smallest=new LogicalVector3(LogicalScalar.Epsilon,0,0);
                    if(Smallest.R==0||Smallest.R!=LogicalScalar.Epsilon||!LogicalScalar.TryReadCanonical(Smallest.R.ToCanonicalBytes(),out var SmallestSaved)||SmallestSaved!=Smallest.R)return "native minimum changed";
                    var Point=new LogicalVector3(LogicalScalar.FromSingle(float.Epsilon),LogicalScalar.FromSingle(-0f),LogicalScalar.One);
                    if(!Point.R.TryToSingle(out var Tiny)||BitConverter.SingleToUInt32Bits(Tiny)!=1)return "subnormal changed";
                    if(!Point.U.TryToSingle(out var Zero)||BitConverter.SingleToUInt32Bits(Zero)!=0x80000000)return "negative zero changed";
                    if(!LogicalScalar.TryAdd(LogicalScalar.One,LogicalScalar.FromRatio(1,4),out var Sum)||Sum.ToStringInvariant()!="1.25")return "arithmetic mismatch";
                    if(!LogicalScalar.TryReadCanonical(Sum.ToCanonicalBytes(),out var Saved)||Saved!=Sum)return "canonical persistence mismatch";
                    var Bytes=new byte[LogicalScalar.SerializedSize];Sum.WriteLittleEndian(Bytes);
                    if(!LogicalScalar.TryReadLittleEndian(Bytes,out Saved)||Saved!=Sum)return "raw persistence mismatch";
                    if(LogicalScalar.TryDivide(LogicalScalar.One,LogicalScalar.PositiveInfinity,out _)||LogicalScalar.TryAdd(LogicalScalar.MaxValue,LogicalScalar.MaxValue,out _))return "nonfinite accepted";
                    if(LogicalScalar.TryParseInvariant("Infinity",out _))return "nonfinite text accepted";
                    if(!LogicalScalar.TryFromScaledInt64(1,64,out var Scaled)||!Scaled.TryToScaledInt64(64,out var Raw)||Raw!=1)return "scaled conversion mismatch";
                    if(!LogicalMath.TrySinCos(Sum,out var Sine,out var Cosine)||!Sine.TryToSingle(out var S)||!Cosine.TryToSingle(out var C)||MathF.Abs(S-MathF.Sin(1.25f))>.000001f||MathF.Abs(C-MathF.Cos(1.25f))>.000001f)return "trigonometry mismatch";
                    if(!new LogicalVector3(3,4,0).TryNormalize(out var Direction)||!Direction.R.TryToSingle(out var R)||MathF.Abs(R-.6f)>.000001f)return "normalization mismatch";
                    if(!LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitU,Sum,out var Rotation)||!Rotation.TryRotate(LogicalVector3.UnitF,out _))return "rotation mismatch";
                    if(!LogicalMath.TryAdvanceRadians(Sum,LogicalScalar.FromRatio(1,7),ulong.MaxValue,out var Phase)||Phase<0||Phase>=LogicalMath.TwoPi)return "phase mismatch";
                    if(LogicalMath.TrySinCos(LogicalScalar.NaN,out _,out _))return "nonfinite angle accepted";
                    if(Size==8&&LogicalScalar.One+LogicalScalar.FromRatio(1,1_000_000_000_000)==LogicalScalar.One)return "double precision lost";
                    if(!LogicalNumeric.TryDivRemFloor(LogicalScalar.FromRatio(-5,4),LogicalScalar.FromRatio(1,2),out var Cell,out var Rest)||Cell!=-3||Rest!=LogicalScalar.FromRatio(1,4))return "negative grid boundary";
                    var Large=LogicalScalar.FromInt64(Size==4?16_777_213:9_007_199_254_740_989);
                    if(LogicalNumeric.FloorQuotient(Large,LogicalScalar.FromInt64(2),3)!=(Size==4?25_165_819:13_510_798_882_111_483))return "rounded discrete quotient";
                    if(!LogicalNumeric.TryScaledFloorQuotient(-LogicalScalar.Epsilon,LogicalScalar.One,1,out Cell)||Cell!=-1)return "negative subnormal index";
                    if(LogicalNumeric.TryScaledFloorQuotient(LogicalScalar.MaxValue,LogicalScalar.One,1,out Cell)||Cell!=0)return "index overflow accepted";
                    if(LogicalNumeric.FloorToInt64(LogicalScalar.FromInt64(long.MinValue))!=long.MinValue)return "minimum index rejected";
                    if(LogicalNumeric.ScaleByRatio(LogicalScalar.FromRatio(5,4),2,5)!=LogicalScalar.FromRatio(1,2))return "ratio scaling";
                    return "OK";
                }
            }
            """,Options));
        var References=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(Path=>!EngineAssemblyFiles.IsEngineAssembly(Path))
            .Append(typeof(CSharpGameEngine.Contracts.AngleAttribute).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(Path=>MetadataReference.CreateFromFile(Path));
        var Compilation=CSharpCompilation.Create("PrimitiveProfile"+Mode,Sources,References,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,nullableContextOptions:NullableContextOptions.Enable,generalDiagnosticOption:ReportDiagnostic.Error));
        using var Stream=new MemoryStream();
        var Emitted=Compilation.Emit(Stream);
        Assert.True(Emitted.Success,string.Join(Environment.NewLine,Emitted.Diagnostics));
        Stream.Position=0;
        var Context=new AssemblyLoadContext(Compilation.AssemblyName!,isCollectible:true);
        try{
            var Assembly=Context.LoadFromStream(Stream);
            Assert.Equal("OK",Assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null,[TypeName,Size]));
        }finally{ Context.Unload(); }
    }
}
