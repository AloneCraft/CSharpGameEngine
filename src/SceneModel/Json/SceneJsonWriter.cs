using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>書き込む JSON 値。オブジェクトと配列は複数行またはインライン形式を選ぶ。</summary>
internal sealed class SceneJsonOut {
    private SceneJsonOut(SceneJsonKind Kind,string?Text,List<KeyValuePair<string,SceneJsonOut>>?Properties,List<SceneJsonOut>?Items,bool Multiline){
        this.Kind=Kind;
        this.Text=Text;
        this.Properties=Properties;
        this.Items=Items;
        this.Multiline=Multiline;
    }
    internal SceneJsonKind Kind { get; }
    internal string?Text { get; }
    internal List<KeyValuePair<string,SceneJsonOut>>?Properties { get; }
    internal List<SceneJsonOut>?Items { get; }
    internal bool Multiline { get; }
    internal static SceneJsonOut Object(bool Multiline=false)=>new(SceneJsonKind.Object,null,[],null,Multiline);
    internal static SceneJsonOut Array(bool Multiline=false)=>new(SceneJsonKind.Array,null,null,[],Multiline);
    internal static SceneJsonOut String(string Value)=>new(SceneJsonKind.String,Value??throw new ArgumentNullException(nameof(Value)),null,null,false);
    /// <summary>有効な JSON 数値である <paramref name="Text"/> をそのまま書き込む。</summary>
    internal static SceneJsonOut Number(string Text)=>new(SceneJsonKind.Number,Text,null,null,false);
    internal static SceneJsonOut Boolean(bool Value)=>new(Value?SceneJsonKind.True:SceneJsonKind.False,null,null,null,false);
    internal static SceneJsonOut Null()=>new(SceneJsonKind.Null,null,null,null,false);
    internal SceneJsonOut Add(string Name,SceneJsonOut Value){
        if(this.Properties is null){ throw new InvalidOperationException("Only objects have properties."); }
        this.Properties.Add(new KeyValuePair<string,SceneJsonOut>(Name,Value));
        return this;
    }
    internal SceneJsonOut Add(SceneJsonOut Value){
        if(this.Items is null){ throw new InvalidOperationException("Only arrays have items."); }
        this.Items.Add(Value);
        return this;
    }
}
/// <summary>決定論的な Scene JSON テキスト（ADR-0070）。インデントはスペース2個、改行は "\n"、名前の後は ": "、波括弧の中のインライン値は ", " で区切る。数値はテキストを変更せずに書き込む。</summary>
internal static class SceneJsonWriter {
    internal static string Write(SceneJsonOut Value){
        var Builder=new StringBuilder();
        WriteValue(Builder,Value,0);
        Builder.Append('\n');
        return Builder.ToString();
    }
    /// <summary>プロパティ名の後に続く、値のインラインテキスト。</summary>
    internal static string WriteInline(SceneJsonOut Value){
        var Builder=new StringBuilder();
        WriteValue(Builder,Value,0,ForceInline:true);
        return Builder.ToString();
    }
    private static void WriteValue(StringBuilder Builder,SceneJsonOut Value,int Indent,bool ForceInline=false){
        switch(Value.Kind){
            case SceneJsonKind.Object:{
                var Properties=Value.Properties!;
                if(Properties.Count==0){
                    Builder.Append("{}");
                    return;
                }
                if(Value.Multiline&&!ForceInline){
                    Builder.Append("{\n");
                    for(var Index=0;Index<Properties.Count;Index++){
                        Builder.Append(' ',(Indent+1)*2);
                        WriteString(Builder,Properties[Index].Key);
                        Builder.Append(": ");
                        WriteValue(Builder,Properties[Index].Value,Indent+1);
                        Builder.Append(Index+1<Properties.Count?",\n":"\n");
                    }
                    Builder.Append(' ',Indent*2).Append('}');
                    return;
                }
                Builder.Append("{ ");
                for(var Index=0;Index<Properties.Count;Index++){
                    if(Index>0){ Builder.Append(", "); }
                    WriteString(Builder,Properties[Index].Key);
                    Builder.Append(": ");
                    WriteValue(Builder,Properties[Index].Value,Indent,ForceInline:true);
                }
                Builder.Append(" }");
                return;
            }
            case SceneJsonKind.Array:{
                var Items=Value.Items!;
                if(Items.Count==0){
                    Builder.Append("[]");
                    return;
                }
                if(Value.Multiline&&!ForceInline){
                    Builder.Append("[\n");
                    for(var Index=0;Index<Items.Count;Index++){
                        Builder.Append(' ',(Indent+1)*2);
                        WriteValue(Builder,Items[Index],Indent+1);
                        Builder.Append(Index+1<Items.Count?",\n":"\n");
                    }
                    Builder.Append(' ',Indent*2).Append(']');
                    return;
                }
                Builder.Append("[ ");
                for(var Index=0;Index<Items.Count;Index++){
                    if(Index>0){ Builder.Append(", "); }
                    WriteValue(Builder,Items[Index],Indent,ForceInline:true);
                }
                Builder.Append(" ]");
                return;
            }
            case SceneJsonKind.String:WriteString(Builder,Value.Text!);return;
            case SceneJsonKind.Number:Builder.Append(Value.Text);return;
            case SceneJsonKind.True:Builder.Append("true");return;
            case SceneJsonKind.False:Builder.Append("false");return;
            default:Builder.Append("null");return;
        }
    }
    internal static void WriteString(StringBuilder Builder,string Value){
        Builder.Append('"');
        foreach(var Character in Value){
            switch(Character){
                case '"':Builder.Append("\\\"");break;
                case '\\':Builder.Append("\\\\");break;
                case '\n':Builder.Append("\\n");break;
                case '\r':Builder.Append("\\r");break;
                case '\t':Builder.Append("\\t");break;
                default:
                    if(Character<0x20){
                        Builder.Append("\\u").Append(((int)Character).ToString("x4",CultureInfo.InvariantCulture));
                    } else {
                        Builder.Append(Character);
                    }
                    break;
            }
        }
        Builder.Append('"');
    }
}
/// <summary>
/// Scene の値に使う数値テキスト。float は同じ float に戻る最短形式（有効桁数6～9を試す）、double も同様（15～17桁）で書き込む。結果は .NET Framework と .
/// NET で同一なので、VSIX とツールは同じ値に同じテキストを出力する。
/// </summary>
internal static class SceneJsonNumbers {
    // 数値ポリシー（NUM001）における double の境界はこの2つのメソッドと、丸めと
    // 両ランタイムで正確な解析を行う SceneJsonDecimal。.NET Framework と .NET の書式化／解析は異なる。
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string FormatSingle(float Value){
        if(float.IsNaN(Value)||float.IsInfinity(Value)){ throw new ArgumentOutOfRangeException(nameof(Value),"JSON numbers must be finite."); }
        return Normalize(SceneJsonDecimal.Shortest(Value,6,9,false,Parsed=>(float)Parsed==Value)??SceneJsonDecimal.Format(Value,9));
    }
    /// <summary>double の場合は15～17桁の最短テキストで元の値へ戻す。decimal の場合は小数桁を保った正確なテキスト（1.50 は1.50のまま）。.NET Framework には decimal 用の "R" 形式がない。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string FormatOther(IFormattable Value){
        if(Value is double Double&&!double.IsNaN(Double)&&!double.IsInfinity(Double)){
            return Normalize(SceneJsonDecimal.Shortest(Double,15,17,false,Parsed=>Parsed==Double)??SceneJsonDecimal.Format(Double,17));
        }
        return Value.ToString(null,CultureInfo.InvariantCulture);
    }
    /// <summary>"G" は "1E-05" を出力する場合があるが、JSON では有効。"-0" は "0" にして、値ごとに表記を1つへ統一する。</summary>
    private static string Normalize(string Text)=>Text=="-0"?"0":Text;
}
