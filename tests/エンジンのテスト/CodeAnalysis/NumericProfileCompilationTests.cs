using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.Loader;
using System.Numerics;
namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class NumericProfileCompilationTests {
    [Theory]
    [InlineData(64,16)]
    [InlineData(64,32)]
    [InlineData(128,48)]
    [InlineData(128,64)]
    [InlineData(192,96)]
    [InlineData(192,120)]
    [InlineData(64,62)]
    [InlineData(128,126)]
    [InlineData(192,190)]
    [InlineData(64,0)]
    [InlineData(128,0)]
    [InlineData(192,0)]
    public void SelectedFixedStorageExecutesMathAndPersistenceAtItsActualWidth(int Width,int Fraction){
        var Custom=Width!=128||Fraction!=64;
        var Symbols=Custom?new[]{"CGE_NUMERIC_CUSTOM_FIXED","CGE_FIXED_"+Width}.Concat(Fraction<32||Fraction>Width-4?["CGE_FIXED_EXTENDED_MATH"]:[]).ToArray():[];
        var Options=new CSharpParseOptions(LanguageVersion.Preview,preprocessorSymbols:Symbols);
        var Sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"TestAssets","Numerics"),"*.cs")
            .Where(Path=>!Custom||System.IO.Path.GetFileName(Path)!="LogicalScalar.cs")
            .Select(Path=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path),Options,path:Path)).ToList();
        Sources.Add(CSharpSyntaxTree.ParseText($"namespace CSharpGameEngine.Numerics; internal static class NumericBuildProfile {{ internal const int TotalBits={Width}; internal const int FractionalBits={Fraction}; }}",Options));
        Sources.Add(CSharpSyntaxTree.ParseText("global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic;",Options));
        Sources.Add(CSharpSyntaxTree.ParseText("""
            using CSharpGameEngine.Numerics;
            public static class Probe {
                public static byte[] ReadMathRaw(bool Atan) {
                    var Half=LogicalScalar.FromRatio(1,2);
                    LogicalScalar Value;
                    if(Atan){if(!LogicalMath.TryAtan2(Half,Half,out Value))throw new InvalidOperationException();}
                    else{if(!LogicalMath.TrySinCos(Half,out Value,out _))throw new InvalidOperationException();}
                    var Bytes=new byte[LogicalScalar.SerializedSize];Value.WriteLittleEndian(Bytes);return Bytes;
                }
                public static string Run(int Width,int Fraction) {
                    if(LogicalScalar.TotalBits!=Width||LogicalScalar.FractionalBits!=Fraction)return "profile mismatch";
                    if(System.Runtime.InteropServices.Marshal.SizeOf<LogicalScalar>()!=Width/8)return "storage mismatch";
                    if(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<LogicalScalar>())return "managed storage";
                    if(LogicalNumeric.ProfileId!="fixed-"+Width+"-"+Fraction)return "runtime profile identity";
                    if(LogicalNumeric.FloorToInt64(-LogicalNumeric.PrecisionStep)!=-1)return "negative discrete boundary";
                    if(LogicalNumeric.ScaleByRatio(LogicalScalar.MaxValue,3,3)!=LogicalScalar.MaxValue)return "ratio intermediate overflow";
                    if(Fraction==0){
                        if(!LogicalMath.TrySinCos(LogicalScalar.One,out var Sine,out var Cosine)||Sine!=LogicalScalar.One||Cosine!=LogicalScalar.One)return "integer trigonometry mismatch";
                        if(!LogicalMath.TryAtan2(LogicalScalar.One,LogicalScalar.One,out var Diagonal)||Diagonal!=LogicalScalar.One)return "integer atan mismatch";
                        return "OK";
                    }
                    if(Width-Fraction<4){
                        var Half=LogicalScalar.FromRatio(1,2);
                        if(!LogicalMath.TrySinCos(Half,out var Sine,out var Cosine)||!Sine.TryToSingle(out var LimitedSin)||!Cosine.TryToSingle(out var LimitedCos)||MathF.Abs(LimitedSin-MathF.Sin(.5f))>.000001f||MathF.Abs(LimitedCos-MathF.Cos(.5f))>.000001f)return "limited-range trigonometry mismatch";
                        if(!LogicalMath.TryAtan2(Half,Half,out var Diagonal)||!Diagonal.TryToSingle(out var A)||MathF.Abs(A-MathF.PI/4)>.000001f)return "limited-range atan mismatch";
                        if(!LogicalMath.TryAdvanceRadians(LogicalScalar.Zero,LogicalScalar.Zero,ulong.MaxValue,out var LimitedPhase)||LimitedPhase!=LogicalScalar.Zero)return "limited-range phase mismatch";
                        if(LogicalMath.TryAdvanceRadians(LogicalScalar.MaxValue,Half,1,out var Rejected)||Rejected!=LogicalScalar.Zero)return "out-of-range phase accepted";
                        return "OK";
                    }
                    var Value=LogicalScalar.ParseInvariant("1.25");
                    if(!LogicalScalar.TryParseInvariant(" 1.25 ",out var Padded)||Padded!=Value)return "text input mismatch";
                    if((Value+Value).ToStringInvariant()!="2.5")return "addition mismatch";
                    if((Value*Value).ToStringInvariant()!="1.5625")return "multiplication mismatch";
                    if((Value/Value)!=LogicalScalar.One)return "division mismatch";
                    if(!LogicalScalar.TrySquareRoot(LogicalScalar.ParseInvariant("2.25"),out var Root)||Root.ToStringInvariant()!="1.5")return "sqrt mismatch";
                    if(!LogicalScalar.TryReadCanonical(Value.ToCanonicalBytes(),out var Loaded)||Loaded!=Value)return "persistence mismatch";
                    if(!LogicalScalar.TryFromSingle(1.25f,out var Single)||Single!=Value||!Value.TryToSingle(out var Projected)||Projected!=1.25f)return "IEEE mismatch";
                    if(!LogicalScalar.FromRatio(1,355687428096000).TryToSingle(out _))return "coefficient mismatch";
                    if(!LogicalMath.TrySinCos(Value,out var Sin,out var Cos)||!Sin.TryToSingle(out var S)||!Cos.TryToSingle(out var C)||MathF.Abs(S-MathF.Sin(1.25f))>.001f||MathF.Abs(C-MathF.Cos(1.25f))>.001f)return "trigonometry mismatch";
                    if(!new LogicalVector3(3,4,0).TryNormalize(out var Direction)||!Direction.R.TryToSingle(out var R)||MathF.Abs(R-.6f)>.001f)return "vector mismatch";
                    if(!LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitU,Value,out var Rotation)||!Rotation.TryRotate(LogicalVector3.UnitF,out _))return "rotation mismatch";
                    if(!LogicalMath.TryAdvanceRadians(Value,LogicalScalar.FromRatio(1,7),ulong.MaxValue,out var Phase)||Phase<LogicalScalar.Zero||Phase>=LogicalMath.TwoPi)return "phase mismatch";
                    return "OK";
                }
            }
            """,Options));
        var References=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(Path=>!EngineAssemblyFiles.IsEngineAssembly(Path))
            .Append(typeof(CSharpGameEngine.Contracts.AngleAttribute).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(Path=>MetadataReference.CreateFromFile(Path));
        var Compilation=CSharpCompilation.Create("NumericProfile"+Width+"F"+Fraction,Sources,References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,nullableContextOptions:NullableContextOptions.Enable,generalDiagnosticOption:ReportDiagnostic.Error));
        using var Stream=new MemoryStream();
        var Emitted=Compilation.Emit(Stream);
        Assert.True(Emitted.Success,string.Join(Environment.NewLine,Emitted.Diagnostics));
        Stream.Position=0;
        var Context=new AssemblyLoadContext(Compilation.AssemblyName!,isCollectible:true);
        try{
            var Assembly=Context.LoadFromStream(Stream);
            var Method=Assembly.GetType("Probe")!.GetMethod("Run")!;
            Assert.Equal("OK",Method.Invoke(null,[Width,Fraction]));
            if(Fraction>64){
                var RawMethod=Assembly.GetType("Probe")!.GetMethod("ReadMathRaw")!;
                foreach(var Atan in new[]{false,true}){
                    var Raw=new BigInteger((byte[])RawMethod.Invoke(null,[Atan])!);
                    Assert.InRange(BigInteger.Abs(Raw-MathOracle(Fraction,Atan)),BigInteger.Zero,new BigInteger(32));
                }
            }
        }finally{ Context.Unload(); }
    }
    private static BigInteger MathOracle(int Fraction,bool Atan){
        const int Guard=128;
        var Scale=BigInteger.One<<(Fraction+Guard);
        var Sum=BigInteger.Zero;
        if(Atan){ Sum=4*AtanInverse(5,Scale)-AtanInverse(239,Scale); }else{
            var Factorial=BigInteger.One;
            for(var Power=1;Power<300;Power++){
                Factorial*=Power;
                if(Power%2==0){ continue; }
                var Term=Scale/(Factorial<<Power);
                if(Term.IsZero){ break; }
                Sum+=(Power/2)%2==0?Term:-Term;
            }
        }
        var Rounded=Sum>>Guard;
        var Remainder=Sum-(Rounded<<Guard);
        var Half=BigInteger.One<<(Guard-1);
        return Remainder>Half||(Remainder==Half&&!Rounded.IsEven)?Rounded+1:Rounded;
    }
    private static BigInteger AtanInverse(int Denominator,BigInteger Scale){
        var Power=new BigInteger(Denominator);
        var Sum=BigInteger.Zero;
        for(var Index=0;Index<300;Index++){
            var Term=Scale/(Power*(2*Index+1));
            if(Term.IsZero){ break; }
            Sum+=Index%2==0?Term:-Term;
            Power*=Denominator*Denominator;
        }
        return Sum;
    }
}
