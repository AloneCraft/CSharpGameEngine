using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using CSharpGameEngine.SceneAnalysis.Json;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// Numbers whose scene JSON text must be the same on .NET (the tools, SceneModel.Tests) and .NET Framework (the VSIX,
/// VSExtension.Tests): floats and doubles from random bit patterns and from short binary fractions (whose decimal
/// digits tie, such as 312985.125), and angles. Both test projects compare the digest of the texts with
/// <see cref="Digest"/>; the .NET tests also compare every text with .NET's own formatting.
/// </summary>
internal static class SceneJsonNumberSamples {
    internal const string Digest="cca90291ffdcc13c2a009142f346edb6369d605c5f811a349300eb920e6a9c30";

    private sealed class Generator {
        private ulong State=0x9E3779B97F4A7C15UL;
        internal ulong Next(){ this.State^=this.State<<13;this.State^=this.State>>7;this.State^=this.State<<17;return this.State; }
        internal int Below(int Limit)=>(int)(this.Next()%(ulong)Limit);
    }

    internal static List<float>Singles(){
        var Random=new Generator();
        var Result=new List<float>();
        for(var Index=0;Index<20000;Index++){
            var Value=Single((uint)Random.Next());
            if(!float.IsNaN(Value)&&!float.IsInfinity(Value)){ Result.Add(Value); }
        }
        for(var Index=0;Index<20000;Index++){
            var Fraction=(uint)Random.Next()&0x7FFFFFu&~((1u<<Random.Below(23))-1u);
            Result.Add(Single((uint)Random.Below(2)<<31|(uint)(117+Random.Below(41))<<23|Fraction));
        }
        return Result;
    }

    internal static List<double>Doubles(){
        var Random=new Generator();
        var Result=new List<double>();
        for(var Index=0;Index<20000;Index++){
            var Value=BitConverter.Int64BitsToDouble((long)Random.Next());
            if(!double.IsNaN(Value)&&!double.IsInfinity(Value)){ Result.Add(Value); }
        }
        for(var Index=0;Index<20000;Index++){
            var Fraction=(long)Random.Next()&0xFFFFFFFFFFFFFL&~((1L<<Random.Below(52))-1L);
            Result.Add(BitConverter.Int64BitsToDouble((long)Random.Below(2)<<63|(long)(993+Random.Below(91))<<52|Fraction));
        }
        return Result;
    }

    internal static List<float>Angles(){
        var Random=new Generator();
        var Result=new List<float>();
        for(var Index=0;Index<10000;Index++){
            var Fraction=(uint)Random.Next()&0x7FFFFFu&~((1u<<Random.Below(23))-1u);
            Result.Add(Single((uint)Random.Below(2)<<31|(uint)(107+Random.Below(30))<<23|Fraction));
        }
        return Result;
    }

    internal static string ComputeDigest(){
        var Text=new StringBuilder();
        foreach(var Value in Singles()){ Text.Append(SceneJsonNumbers.FormatSingle(Value)).Append('\n'); }
        foreach(var Value in Doubles()){ Text.Append(SceneJsonNumbers.FormatOther(Value)).Append('\n'); }
        foreach(var Value in Angles()){
            Text.Append(SceneJsonAngles.FormatTurns(Value,SceneAngleUnit.Radians)).Append(' ')
                .Append(SceneJsonAngles.FormatTurns(Value,SceneAngleUnit.Degrees)).Append(' ')
                .Append(SceneJsonAngles.FormatDegrees(Value,SceneAngleUnit.Radians)).Append(' ')
                .Append(SceneJsonAngles.FormatDegrees(Value,SceneAngleUnit.Degrees)).Append('\n');
        }
        using var Sha=SHA256.Create();
        var Hash=Sha.ComputeHash(Encoding.UTF8.GetBytes(Text.ToString()));
        var Hex=new StringBuilder();
        foreach(var Byte in Hash){ Hex.Append(Byte.ToString("x2",System.Globalization.CultureInfo.InvariantCulture)); }
        return Hex.ToString();
    }

    private static float Single(uint Bits)=>BitConverter.ToSingle(BitConverter.GetBytes(Bits),0);
}
