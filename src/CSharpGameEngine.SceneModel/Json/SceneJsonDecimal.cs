using System;
using System.Globalization;
using System.Numerics;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// 二進浮動小数点値の十進表記を、.NET Framework（VSIX）と .NET（ツール）で一致させる。正確な二進値から桁を一度だけ、偶数への丸めで決定し、.NET と同じく正しく丸める解析方法で
/// テキストを読み戻す。.NET Framework は15桁または17桁へ丸めた後にさらに桁数を減らし、同値をゼロから遠ざけて丸める。また double の解析を正確に丸めない。その結果、値によって異な
/// るテキストを出力していた（.NET では312985.12となる float 312985.125 を312985.13とするなど）。また、double の出力テキストを読み戻すと異なる double
///  になる場合もあった。
/// </summary>
/// <remarks>各メソッドが扱うのは <see cref="BigInteger"/> または double のみ。これが数値ポリシー（NUM001）の外部境界。</remarks>
internal static class SceneJsonDecimal {
    /// <summary>
    /// <paramref name="FirstDigits"/> 桁から <paramref name="LastDigits"/> 桁までで、解析結果が
    /// <paramref name="ReadsBack"/> の条件を満たす最初のテキストを求める。桁数に応じて "G" と同じ配置にするか、
    /// <paramref name="NotationDigits"/> が指定されていればそれを使う。<paramref name="PositionalOnly"/> の場合は指数表記を除外する。該当す
    /// るものがなければ null。ゼロは "0" または "-0"。
    /// </summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string?Shortest(double Value,int FirstDigits,int LastDigits,bool PositionalOnly,Func<double,bool>ReadsBack,int NotationDigits=0){
        var Bits=BitConverter.DoubleToInt64Bits(Value);
        var Negative=Bits<0;
        if(Value==0d){ return ReadsBack(Value)?(Negative?"-0":"0"):null; }
        Decompose(Bits,out var Mantissa,out var Exponent);
        var Magnitude=DecimalExponent(Mantissa,Exponent);
        for(var Digits=FirstDigits;Digits<=LastDigits;Digits++){
            Round(Mantissa,Exponent,Magnitude,Digits,out var Significand,out var TextExponent);
            var Text=Layout(Negative,Significand,TextExponent,NotationDigits>0?NotationDigits:Digits);
            if(PositionalOnly&&Text.IndexOf('E')>=0){ continue; }
            if(ReadsBack(Parse(Negative,Significand,TextExponent-(Digits-1)))){ return Text; }
        }
        return null;
    }
    /// <summary>.NET の "G" + 桁数指定と同じ形式で出力する、<paramref name="Digits"/> 有効桁数への丸め値。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string Format(double Value,int Digits)=>Shortest(Value,Digits,Digits,false,static Parsed=>true)!;
    /// <summary>有限でゼロではない double の絶対値を <paramref name="Mantissa"/> × 2^<paramref name="Exponent"/> で表す。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static void Decompose(long Bits,out BigInteger Mantissa,out int Exponent){
        var Biased=(int)((Bits>>52)&0x7FF);
        var Fraction=Bits&0xFFFFFFFFFFFFFL;
        Mantissa=Biased==0?Fraction:Fraction|(1L<<52);
        Exponent=Biased==0?-1074:Biased-1075;
    }
    /// <summary>K は 10^K &lt;= <paramref name="Mantissa"/> × 2^<paramref name="Exponent"/> &lt; 10^(K+1) を満たす整数。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static int DecimalExponent(BigInteger Mantissa,int Exponent){
        var Numerator=Exponent>=0?Mantissa<<Exponent:Mantissa;
        var Denominator=Exponent>=0?BigInteger.One:BigInteger.One<<-Exponent;
        var Magnitude=Numerator.ToString(CultureInfo.InvariantCulture).Length-Denominator.ToString(CultureInfo.InvariantCulture).Length;
        while(ComparePowerOfTen(Numerator,Denominator,Magnitude)<0){ Magnitude--; }
        while(ComparePowerOfTen(Numerator,Denominator,Magnitude+1)>=0){ Magnitude++; }
        return Magnitude;
    }
    /// <summary>Numerator / Denominator と 10^Power を比較する。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static int ComparePowerOfTen(BigInteger Numerator,BigInteger Denominator,int Power)=>
        Power>=0?Numerator.CompareTo(Denominator*BigInteger.Pow(10,Power)):(Numerator*BigInteger.Pow(10,-Power)).CompareTo(Denominator);
    /// <summary>
    /// <paramref name="Digits"/> 有効桁数へ偶数丸めした値。最初の桁の十進指数が <paramref name="TextExponent"/> となる、
    /// <paramref name="Digits"/> 桁の <paramref name="Significand"/> で表す。
    /// </summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static void Round(BigInteger Mantissa,int Exponent,int Magnitude,int Digits,out BigInteger Significand,out int TextExponent){
        var Numerator=Exponent>=0?Mantissa<<Exponent:Mantissa;
        var Denominator=Exponent>=0?BigInteger.One:BigInteger.One<<-Exponent;
        var Scale=Digits-1-Magnitude;
        if(Scale>=0){ Numerator*=BigInteger.Pow(10,Scale); }else{ Denominator*=BigInteger.Pow(10,-Scale); }
        Significand=BigInteger.DivRem(Numerator,Denominator,out var Remainder);
        var Half=(Remainder<<1).CompareTo(Denominator);
        if(Half>0||Half==0&&!Significand.IsEven){ Significand+=BigInteger.One; }
        TextExponent=Magnitude;
        if(Significand==BigInteger.Pow(10,Digits)){ Significand/=10;TextExponent++; }
    }
    /// <summary>.NET の "G" 形式。指数が -5 より大きく <paramref name="NotationDigits"/> 未満なら位取り表記にする。末尾のゼロは出力しない。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static string Layout(bool Negative,BigInteger Significand,int TextExponent,int NotationDigits){
        var Digits=Significand.ToString(CultureInfo.InvariantCulture).TrimEnd('0');
        var Sign=Negative?"-":string.Empty;
        if(TextExponent>-5&&TextExponent<NotationDigits){
            if(TextExponent<0){ return Sign+"0."+new string('0',-TextExponent-1)+Digits; }
            if(Digits.Length<=TextExponent+1){ return Sign+Digits+new string('0',TextExponent+1-Digits.Length); }
            return Sign+Digits.Substring(0,TextExponent+1)+"."+Digits.Substring(TextExponent+1);
        }
        var Fraction=Digits.Length>1?"."+Digits.Substring(1):string.Empty;
        return Sign+Digits.Substring(0,1)+Fraction+"E"+(TextExponent<0?"-":"+")+Math.Abs(TextExponent).ToString("00",CultureInfo.InvariantCulture);
    }
    /// <summary>
    /// JSON 数値のテキストを正しく丸めて解析したとき、<paramref name="Value"/> に戻るかを判定する。対象は float、double、decimal、整数定数。これ以外の値や、
    /// 400を超える10の累乗、1000を超える指数では false を返す。このような定数を作成者が記述する必要はなく、正確な演算が遅くなるためである。変換処理はその場合、値から生成したテキストを出力する
    /// 。
    /// </summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static bool ReadsBack(string Text,object?Value){
        var Negative=Text.StartsWith("-",StringComparison.Ordinal);
        var Body=Negative?Text.Substring(1):Text;
        var ExponentAt=Body.IndexOfAny(['e','E']);
        var Mantissa=ExponentAt<0?Body:Body.Substring(0,ExponentAt);
        if(!long.TryParse(ExponentAt<0?"0":Body.Substring(ExponentAt+1),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var Exponent)){
            return false;
        }
        var Dot=Mantissa.IndexOf('.');
        // long 型に収まり範囲制限もできてから、小数点以下の桁を取り除く。指数の桁あふれを防ぐ（-2^31 は Abs を取れない）。
        var LongPower=Exponent<-1000||Exponent>1000?Exponent:Exponent-(Dot<0?0:Mantissa.Length-Dot-1);
        if(LongPower<-400||LongPower>400
            ||!BigInteger.TryParse(Dot<0?Mantissa:Mantissa.Remove(Dot,1),NumberStyles.None,CultureInfo.InvariantCulture,out var Significand)){
            return false;
        }
        var Power=(int)LongPower;
        // Parse は0以外の仮数を必要とする。ゼロは符号にかかわらずゼロへ戻る。
        switch(Value){
            case float Single:return RoundsToSingle(Negative,Significand,Power,Single);
            case double Double:return Significand.IsZero?Double==0d:Parse(Negative,Significand,Power)==Double;
            case decimal Exact:return decimal.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var Read)&&Read==Exact;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Power>=0&&(Negative?-Significand:Significand)*BigInteger.Pow(10,Power)
                    ==new BigInteger(Convert.ToDecimal(Value,CultureInfo.InvariantCulture));
            default:return false;
        }
    }
    /// <summary>
    /// ±<paramref name="Significand"/> × 10^<paramref name="Power"/> を C# の float リテラルと同じく偶数への最近接丸めで単精度に変換し
    /// た場合、<paramref name="Single"/> になるかを判定する。最近接 double へ丸めた後に再度丸めると、厳密値では同値でない中点に来ることがある。そのため隣接 float との
    /// 中点（double で正確に表現可能）に対して値を比較する。
    /// </summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static bool RoundsToSingle(bool Negative,BigInteger Significand,int Power,float Single){
        if(float.IsInfinity(Single)||float.IsNaN(Single)){ return false; }
        var Lower=(Single+Neighbour(Single,-1))/2d;
        var Upper=(Single+Neighbour(Single,1))/2d;
        var Even=(BitConverter.ToInt32(BitConverter.GetBytes(Single),0)&1)==0;
        var AboveLower=CompareExact(Negative,Significand,Power,Lower);
        var BelowUpper=CompareExact(Negative,Significand,Power,Upper);
        return (AboveLower>0||AboveLower==0&&Even)&&(BelowUpper<0||BelowUpper==0&&Even);
    }
    /// <summary>有限値 <paramref name="Single"/> の次にある、<paramref name="Direction"/> 方向（+1なら大きい側、-1なら小さい側）の float。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static double Neighbour(float Single,int Direction){
        if(Single==0f){ return Direction*(double)float.Epsilon; }
        var Bits=BitConverter.ToInt32(BitConverter.GetBytes(Single),0);
        var Next=BitConverter.ToSingle(BitConverter.GetBytes(Single>0f==Direction>0?Bits+1:Bits-1),0);
        // 最大の float を超えた場合、その次の値は1つ前の値と同じ距離だけ離れる。
        return float.IsInfinity(Next)?2d*Single-Neighbour(Single,-Direction):Next;
    }
    /// <summary>±<paramref name="Significand"/> × 10^<paramref name="Power"/> と有限値 <paramref name="Bound"/> を正確に比較する。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static int CompareExact(bool Negative,BigInteger Significand,int Power,double Bound){
        var Sign=Significand.IsZero?0:Negative?-1:1;
        var BoundSign=Math.Sign(Bound);
        if(Sign!=BoundSign||Sign==0){ return Sign.CompareTo(BoundSign); }
        Decompose(BitConverter.DoubleToInt64Bits(Math.Abs(Bound)),out var Mantissa,out var Exponent);
        var Left=Significand*(Power>=0?BigInteger.Pow(10,Power):BigInteger.One);
        var Right=Mantissa*(Power<0?BigInteger.Pow(10,-Power):BigInteger.One);
        if(Exponent>=0){ Right<<=Exponent; }else{ Left<<=-Exponent; }
        return Sign*Left.CompareTo(Right);
    }
    /// <summary><paramref name="Significand"/> × 10^<paramref name="Power"/> に最も近い double（同値なら偶数へ丸める）。表現可能範囲外なら無限大。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static double Parse(bool Negative,BigInteger Significand,int Power){
        var Numerator=Power>=0?Significand*BigInteger.Pow(10,Power):Significand;
        var Denominator=Power>=0?BigInteger.One:BigInteger.Pow(10,-Power);
        // 2^52 <= value / 2^Exponent < 2^53 を満たす二進指数。非正規化数では非正規化指数を使う。
        var Exponent=BitLength(Numerator)-BitLength(Denominator)-53;
        while(ComparePowerOfTwo(Numerator,Denominator,Exponent+52)<0){ Exponent--; }
        while(ComparePowerOfTwo(Numerator,Denominator,Exponent+53)>=0){ Exponent++; }
        if(Exponent<-1074){ Exponent=-1074; }
        if(Exponent<0){ Numerator<<=-Exponent; }else{ Denominator<<=Exponent; }
        var Mantissa=BigInteger.DivRem(Numerator,Denominator,out var Remainder);
        var Half=(Remainder<<1).CompareTo(Denominator);
        if(Half>0||Half==0&&!Mantissa.IsEven){ Mantissa+=BigInteger.One; }
        if(Mantissa==BigInteger.One<<53){ Mantissa>>=1;Exponent++; }
        if(Exponent>971){ return Negative?double.NegativeInfinity:double.PositiveInfinity; }
        var Bits=Mantissa<BigInteger.One<<52?(long)Mantissa:((long)(Exponent+1075)<<52)|((long)Mantissa&0xFFFFFFFFFFFFFL);
        return BitConverter.Int64BitsToDouble(Negative?Bits|long.MinValue:Bits);
    }
    /// <summary>Numerator / Denominator と 2^Power を比較する。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static int ComparePowerOfTwo(BigInteger Numerator,BigInteger Denominator,int Power)=>
        Power>=0?Numerator.CompareTo(Denominator<<Power):(Numerator<<-Power).CompareTo(Denominator);
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static int BitLength(BigInteger Value){
        var Bytes=Value.ToByteArray();
        var Top=Bytes.Length-1;
        while(Top>0&&Bytes[Top]==0){ Top--; }
        var Length=Top*8;
        for(int Byte=Bytes[Top];Byte!=0;Byte>>=1){ Length++; }
        return Length;
    }
}
