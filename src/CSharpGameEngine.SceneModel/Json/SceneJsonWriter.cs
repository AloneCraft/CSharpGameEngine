using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>A JSON value to write. Objects and arrays choose multi-line or inline layout.</summary>
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
    /// <summary>A number written exactly as <paramref name="Text"/> (already a valid JSON number).</summary>
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
/// <summary>
/// Deterministic scene JSON text (ADR-0070): two-space indentation, "\n" line breaks, ": " after names,
/// inline values separated by ", " inside braces. Numbers are written from their text unchanged.
/// </summary>
internal static class SceneJsonWriter {
    internal static string Write(SceneJsonOut Value){
        var Builder=new StringBuilder();
        WriteValue(Builder,Value,0);
        Builder.Append('\n');
        return Builder.ToString();
    }
    /// <summary>The inline text of a value, as it appears after a property name.</summary>
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
/// Number text for scene values. A float is written in the shortest form that parses back to the
/// same float (trying 6 to 9 significant digits), and a double likewise (15 to 17 digits), identical on
/// .NET Framework and .NET, so the VSIX and the tools write the same text for the same value.
/// </summary>
internal static class SceneJsonNumbers {
    // The double boundary of the numeric policy (NUM001) is these two methods and SceneJsonDecimal, which rounds and
    // parses exactly on both runtimes (.NET Framework's own formatting and parsing differ from .NET's).
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string FormatSingle(float Value){
        if(float.IsNaN(Value)||float.IsInfinity(Value)){ throw new ArgumentOutOfRangeException(nameof(Value),"JSON numbers must be finite."); }
        return Normalize(SceneJsonDecimal.Shortest(Value,6,9,false,Parsed=>(float)Parsed==Value)??SceneJsonDecimal.Format(Value,9));
    }
    /// <summary>
    /// A double's shortest text of 15 to 17 digits that reads back to it, or a decimal's exact text with its scale (1.50
    /// stays 1.50; .NET Framework has no "R" for decimal).
    /// </summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    internal static string FormatOther(IFormattable Value){
        if(Value is double Double&&!double.IsNaN(Double)&&!double.IsInfinity(Double)){
            return Normalize(SceneJsonDecimal.Shortest(Double,15,17,false,Parsed=>Parsed==Double)??SceneJsonDecimal.Format(Double,17));
        }
        return Value.ToString(null,CultureInfo.InvariantCulture);
    }
    /// <summary>"G" may produce "1E-05"; JSON accepts it. "-0" becomes "0" so a value has one spelling.</summary>
    private static string Normalize(string Text)=>Text=="-0"?"0":Text;
}
