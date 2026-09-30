using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// Strict RFC 8259 reader for scene documents (ADR-0070): no comments, no trailing commas, and
/// duplicate object keys are rejected because a scene value must have exactly one meaning. Errors
/// carry the character offset so diagnostics can point at the JSON line and column.
/// </summary>
internal static class SceneJsonReader {
    internal static SceneJsonValue Parse(string Text){
        if(Text is null){ throw new ArgumentNullException(nameof(Text)); }
        var Position=0;
        SkipWhitespace(Text,ref Position);
        var Value=ReadValue(Text,ref Position,0);
        SkipWhitespace(Text,ref Position);
        if(Position!=Text.Length){ throw new SceneJsonException("Unexpected text after the JSON value.",Position); }
        return Value;
    }
    /// <summary>1-based line and column of <paramref name="Position"/>, counting \n as the line break.</summary>
    internal static (int Line,int Column)LineAndColumn(string Text,int Position){
        var Line=1;
        var LineStart=0;
        var End=Math.Min(Position,Text.Length);
        for(var Index=0;Index<End;Index++){
            if(Text[Index]=='\n'){
                Line++;
                LineStart=Index+1;
            }
        }
        return (Line,End-LineStart+1);
    }
    private const int MaximumDepth=128;
    private static SceneJsonValue ReadValue(string Text,ref int Position,int Depth){
        if(Depth>MaximumDepth){ throw new SceneJsonException("The document nests deeper than 128 levels.",Position); }
        if(Position>=Text.Length){ throw new SceneJsonException("Unexpected end of the document.",Position); }
        var Start=Position;
        switch(Text[Position]){
            case '{':return ReadObject(Text,ref Position,Depth);
            case '[':return ReadArray(Text,ref Position,Depth);
            case '"':{
                var Value=ReadString(Text,ref Position);
                return SceneJsonValue.CreateScalar(SceneJsonKind.String,Start,Position,Value);
            }
            case 't':ReadLiteral(Text,ref Position,"true");return SceneJsonValue.CreateScalar(SceneJsonKind.True,Start,Position,null);
            case 'f':ReadLiteral(Text,ref Position,"false");return SceneJsonValue.CreateScalar(SceneJsonKind.False,Start,Position,null);
            case 'n':ReadLiteral(Text,ref Position,"null");return SceneJsonValue.CreateScalar(SceneJsonKind.Null,Start,Position,null);
            default:
                if(Text[Position]=='-'||(Text[Position]>='0'&&Text[Position]<='9')){
                    ReadNumber(Text,ref Position);
                    return SceneJsonValue.CreateScalar(SceneJsonKind.Number,Start,Position,Text.Substring(Start,Position-Start));
                }
                throw new SceneJsonException($"Unexpected character '{Text[Position]}'.",Position);
        }
    }
    private static SceneJsonValue ReadObject(string Text,ref int Position,int Depth){
        var Start=Position;
        Position++;
        List<SceneJsonProperty>Properties=[];
        HashSet<string>Names=new(StringComparer.Ordinal);
        SkipWhitespace(Text,ref Position);
        if(Position<Text.Length&&Text[Position]=='}'){
            Position++;
            return SceneJsonValue.CreateObject(Start,Position,Properties);
        }
        while(true){
            SkipWhitespace(Text,ref Position);
            if(Position>=Text.Length||Text[Position]!='"'){ throw new SceneJsonException("Expected a property name.",Position); }
            var NameStart=Position;
            var Name=ReadString(Text,ref Position);
            if(!Names.Add(Name)){ throw new SceneJsonException($"Duplicate property \"{Name}\".",NameStart); }
            SkipWhitespace(Text,ref Position);
            Expect(Text,ref Position,':');
            SkipWhitespace(Text,ref Position);
            Properties.Add(new SceneJsonProperty(Name,NameStart,ReadValue(Text,ref Position,Depth+1)));
            SkipWhitespace(Text,ref Position);
            if(Position<Text.Length&&Text[Position]==','){
                Position++;
                continue;
            }
            Expect(Text,ref Position,'}');
            return SceneJsonValue.CreateObject(Start,Position,Properties);
        }
    }
    private static SceneJsonValue ReadArray(string Text,ref int Position,int Depth){
        var Start=Position;
        Position++;
        List<SceneJsonValue>Items=[];
        SkipWhitespace(Text,ref Position);
        if(Position<Text.Length&&Text[Position]==']'){
            Position++;
            return SceneJsonValue.CreateArray(Start,Position,Items);
        }
        while(true){
            SkipWhitespace(Text,ref Position);
            Items.Add(ReadValue(Text,ref Position,Depth+1));
            SkipWhitespace(Text,ref Position);
            if(Position<Text.Length&&Text[Position]==','){
                Position++;
                continue;
            }
            Expect(Text,ref Position,']');
            return SceneJsonValue.CreateArray(Start,Position,Items);
        }
    }
    private static string ReadString(string Text,ref int Position){
        var Start=Position;
        Position++;
        var Builder=new StringBuilder();
        while(true){
            if(Position>=Text.Length){ throw new SceneJsonException("Unterminated string.",Start); }
            var Character=Text[Position++];
            if(Character=='"'){ return Builder.ToString(); }
            if(Character<0x20){ throw new SceneJsonException("Control characters must be escaped in strings.",Position-1); }
            if(Character!='\\'){
                Builder.Append(Character);
                continue;
            }
            if(Position>=Text.Length){ throw new SceneJsonException("Unterminated escape sequence.",Position-1); }
            var Escape=Text[Position++];
            switch(Escape){
                case '"':Builder.Append('"');break;
                case '\\':Builder.Append('\\');break;
                case '/':Builder.Append('/');break;
                case 'b':Builder.Append('\b');break;
                case 'f':Builder.Append('\f');break;
                case 'n':Builder.Append('\n');break;
                case 'r':Builder.Append('\r');break;
                case 't':Builder.Append('\t');break;
                case 'u':
                    if(Position+4>Text.Length||!int.TryParse(Text.Substring(Position,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var Code)){
                        throw new SceneJsonException("Invalid \\u escape.",Position-2);
                    }
                    Builder.Append((char)Code);
                    Position+=4;
                    break;
                default:throw new SceneJsonException($"Invalid escape '\\{Escape}'.",Position-2);
            }
        }
    }
    private static void ReadNumber(string Text,ref int Position){
        var Start=Position;
        if(Text[Position]=='-'){ Position++; }
        if(Position>=Text.Length){ throw new SceneJsonException("Invalid number.",Start); }
        if(Text[Position]=='0'){
            Position++;
        } else if(Text[Position]>='1'&&Text[Position]<='9'){
            while(Position<Text.Length&&IsAsciiDigit(Text[Position])){ Position++; }
        } else {
            throw new SceneJsonException("Invalid number.",Start);
        }
        if(Position<Text.Length&&Text[Position]=='.'){
            Position++;
            if(Position>=Text.Length||!IsAsciiDigit(Text[Position])){ throw new SceneJsonException("Invalid number fraction.",Start); }
            while(Position<Text.Length&&IsAsciiDigit(Text[Position])){ Position++; }
        }
        if(Position<Text.Length&&(Text[Position]=='e'||Text[Position]=='E')){
            Position++;
            if(Position<Text.Length&&(Text[Position]=='+'||Text[Position]=='-')){ Position++; }
            if(Position>=Text.Length||!IsAsciiDigit(Text[Position])){ throw new SceneJsonException("Invalid number exponent.",Start); }
            while(Position<Text.Length&&IsAsciiDigit(Text[Position])){ Position++; }
        }
    }
    private static void ReadLiteral(string Text,ref int Position,string Literal){
        if(string.CompareOrdinal(Text,Position,Literal,0,Literal.Length)!=0){ throw new SceneJsonException($"Expected '{Literal}'.",Position); }
        Position+=Literal.Length;
    }
    private static void Expect(string Text,ref int Position,char Character){
        if(Position>=Text.Length||Text[Position]!=Character){ throw new SceneJsonException($"Expected '{Character}'.",Position); }
        Position++;
    }
    private static bool IsAsciiDigit(char Character)=>Character>='0'&&Character<='9';
    private static void SkipWhitespace(string Text,ref int Position){
        while(Position<Text.Length&&(Text[Position]==' '||Text[Position]=='\t'||Text[Position]=='\n'||Text[Position]=='\r')){ Position++; }
    }
}
