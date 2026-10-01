using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// Writes an edited scene back into the author's text (M0095, ADR-0070). The editor produces the whole
/// scene in the canonical layout; this keeps every part of the old text whose value did not change and
/// replaces only the values that did: a changed scalar by its new text, and an object or array whose keys
/// or length changed by its new canonical text. The result reads as exactly the new document; if it did
/// not, the canonical text is returned instead.
/// </summary>
internal static class SceneJsonTextMerge {
    internal static string Merge(string OldText,string NewText){
        if(OldText is null){ throw new ArgumentNullException(nameof(OldText)); }
        if(NewText is null){ throw new ArgumentNullException(nameof(NewText)); }
        SceneJsonValue Old;
        SceneJsonValue New;
        try {
            Old=SceneJsonReader.Parse(OldText);
            New=SceneJsonReader.Parse(NewText);
        } catch(SceneJsonException){
            return NewText;
        }
        var Edits=new List<KeyValuePair<SceneJsonValue,SceneJsonValue>>();
        Collect(Old,New,Edits);
        if(Edits.Count==0){ return OldText; }
        var Builder=new StringBuilder(OldText);
        for(var Index=Edits.Count-1;Index>=0;Index--){
            var Replaced=Edits[Index].Key;
            var Replacement=Edits[Index].Value;
            Builder.Remove(Replaced.Start,Replaced.End-Replaced.Start);
            Builder.Insert(Replaced.Start,NewText.Substring(Replacement.Start,Replacement.End-Replacement.Start));
        }
        var Merged=Builder.ToString();
        try {
            return SameValue(SceneJsonReader.Parse(Merged),New)?Merged:NewText;
        } catch(SceneJsonException){
            return NewText;
        }
    }
    /// <summary>Edits in document order: each pairs an old value with the new value that replaces it.</summary>
    private static void Collect(SceneJsonValue Old,SceneJsonValue New,List<KeyValuePair<SceneJsonValue,SceneJsonValue>>Edits){
        if(SameValue(Old,New)){ return; }
        if(Old.Kind==SceneJsonKind.Object&&New.Kind==SceneJsonKind.Object&&SameNames(Old,New)){
            for(var Index=0;Index<Old.Properties.Count;Index++){
                Collect(Old.Properties[Index].Value,New.Properties[Index].Value,Edits);
            }
            return;
        }
        if(Old.Kind==SceneJsonKind.Array&&New.Kind==SceneJsonKind.Array&&Old.Items.Count==New.Items.Count){
            for(var Index=0;Index<Old.Items.Count;Index++){
                Collect(Old.Items[Index],New.Items[Index],Edits);
            }
            return;
        }
        Edits.Add(new KeyValuePair<SceneJsonValue,SceneJsonValue>(Old,New));
    }
    private static bool SameNames(SceneJsonValue Old,SceneJsonValue New){
        if(Old.Properties.Count!=New.Properties.Count){ return false; }
        for(var Index=0;Index<Old.Properties.Count;Index++){
            if(!string.Equals(Old.Properties[Index].Name,New.Properties[Index].Name,StringComparison.Ordinal)){ return false; }
        }
        return true;
    }
    /// <summary>The same JSON value: numbers compare by value ("1.0" equals "1"), objects by names in order.</summary>
    internal static bool SameValue(SceneJsonValue Old,SceneJsonValue New){
        if(Old.Kind!=New.Kind){ return false; }
        switch(Old.Kind){
            case SceneJsonKind.String:return string.Equals(Old.Text,New.Text,StringComparison.Ordinal);
            case SceneJsonKind.Number:return string.Equals(Old.Text,New.Text,StringComparison.Ordinal)||SameNumber(Old.Text!,New.Text!);
            case SceneJsonKind.Object:
                if(!SameNames(Old,New)){ return false; }
                for(var Index=0;Index<Old.Properties.Count;Index++){
                    if(!SameValue(Old.Properties[Index].Value,New.Properties[Index].Value)){ return false; }
                }
                return true;
            case SceneJsonKind.Array:
                if(Old.Items.Count!=New.Items.Count){ return false; }
                for(var Index=0;Index<Old.Items.Count;Index++){
                    if(!SameValue(Old.Items[Index],New.Items[Index])){ return false; }
                }
                return true;
            default:return true;
        }
    }
    /// <summary>
    /// Exact comparison of two JSON number texts without rounding them into a number type: both become
    /// sign, significant digits and exponent ("1.0" and "1" are both 0.1e1), so large integers never
    /// compare equal by rounding.
    /// </summary>
    private static bool SameNumber(string Old,string New)=>
        NormalizeNumber(Old) is { }OldForm&&string.Equals(OldForm,NormalizeNumber(New),StringComparison.Ordinal);
    internal static string?NormalizeNumber(string Text){
        var Negative=Text.StartsWith("-",StringComparison.Ordinal);
        var Body=Negative?Text.Substring(1):Text;
        var ExponentIndex=Body.IndexOfAny(['e','E']);
        var Mantissa=ExponentIndex<0?Body:Body.Substring(0,ExponentIndex);
        if(!int.TryParse(ExponentIndex<0?"0":Body.Substring(ExponentIndex+1),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var Exponent)){
            return null;
        }
        var Dot=Mantissa.IndexOf('.');
        var Digits=Dot<0?Mantissa:Mantissa.Remove(Dot,1);
        Exponent+=Dot<0?Mantissa.Length:Dot;
        var Leading=0;
        while(Leading<Digits.Length&&Digits[Leading]=='0'){
            Leading++;
            Exponent--;
        }
        Digits=Digits.Substring(Leading).TrimEnd('0');
        if(Digits.Length==0){ return "0"; }
        return (Negative?"-":string.Empty)+"0."+Digits+"e"+Exponent.ToString(CultureInfo.InvariantCulture);
    }
}
