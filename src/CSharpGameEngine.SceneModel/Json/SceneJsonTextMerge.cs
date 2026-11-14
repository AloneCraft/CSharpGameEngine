using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// M0095、ADR-0070：編集済み Scene を作成者のテキストへ書き戻す。エディターは Scene 全体を正準レイアウトで生成するが、値が変わっていない古いテキスト部分はすべて保持し、変わった
/// 値だけを置き換える。変更されたスカラーは新しいテキストにし、新しいドキュメントが追加したキーはプロパティを1つ挿入し、キーを削除したオブジェクトや長さの変わった配列は正準テキストに置き換える。作成者の
/// キー順もレイアウトとして保つ。ノードの空の <c>components</c> や <c>children</c>、コンポーネントの空の <c>members</c> は省略する。結果が新しい
/// ドキュメントと完全に一致しない場合は、正準テキスト全体を返す。
/// </summary>
internal static class SceneJsonTextMerge {
    /// <summary>Scene ドキュメント内の値の位置。空の場合に省略できるキーが決まる。</summary>
    private enum Role { Other,Document,Node,Component }

    private readonly struct Edit {
        internal Edit(int Start,int End,string Text,int Sequence){
            this.Start=Start;
            this.End=End;
            this.Text=Text;
            this.Sequence=Sequence;
        }
        internal int Start { get; }
        internal int End { get; }
        internal string Text { get; }
        internal int Sequence { get; }
    }

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
        var Edits=new List<Edit>();
        Collect(Old,New,Role.Document,OldText,NewText,Edits);
        if(Edits.Count==0){ return OldText; }
        var Builder=new StringBuilder(OldText);
        // 末尾から適用するため、前にある位置が保たれる。同じ位置への挿入は収集した順を保つ。
        foreach(var Change in Edits.OrderByDescending(static Change=>Change.Start).ThenByDescending(static Change=>Change.Sequence)){
            Builder.Remove(Change.Start,Change.End-Change.Start);
            Builder.Insert(Change.Start,Change.Text);
        }
        var Merged=Builder.ToString();
        try {
            return SameValue(SceneJsonReader.Parse(Merged),New)?Merged:NewText;
        } catch(SceneJsonException){
            return NewText;
        }
    }

    /// <summary>編集前後の生成結果が同じ数値は、読み込み時に丸められていても元の文書の表記を維持する。</summary>
    internal static string Merge(string OldText,string BaselineText,string NewText){
        try {
            var Old=SceneJsonReader.Parse(OldText);
            var Baseline=SceneJsonReader.Parse(BaselineText);
            var New=SceneJsonReader.Parse(NewText);
            var Edits=new List<Edit>();
            PreserveUnchangedNumbers(Old,Baseline,New,OldText,Edits);
            PreserveReparentedNodes(Old,Baseline,New,OldText,Edits);
            var Builder=new StringBuilder(NewText);
            foreach(var Change in Edits.OrderByDescending(static Change=>Change.Start)){
                Builder.Remove(Change.Start,Change.End-Change.Start);
                Builder.Insert(Change.Start,Change.Text);
            }
            return Merge(OldText,Builder.ToString());
        } catch(SceneJsonException){
            return Merge(OldText,NewText);
        }
    }

    // 別の親へ移したノードは配列の位置だけでは対応できない。全ノードの ID が一意な場合だけ元のノードを探す。
    private static void PreserveReparentedNodes(SceneJsonValue Old,SceneJsonValue Baseline,SceneJsonValue New,string OldText,List<Edit>Edits){
        if(!TryIndexNodes(Old,out var OldNodes)||!TryIndexNodes(Baseline,out var BaselineNodes)||!TryIndexNodes(New,out var NewNodes)){ return; }
        foreach(var Pair in NewNodes){
            if(OldNodes.TryGetValue(Pair.Key,out var OldNode)&&BaselineNodes.TryGetValue(Pair.Key,out var BaselineNode)
                &&string.Equals(OldNode.Parent,BaselineNode.Parent,StringComparison.Ordinal)
                &&!string.Equals(OldNode.Parent,Pair.Value.Parent,StringComparison.Ordinal)){
                PreserveUnchangedNumbers(OldNode.Node,BaselineNode.Node,Pair.Value.Node,OldText,Edits);
            }
        }
    }
    private static bool TryIndexNodes(SceneJsonValue Document,out Dictionary<string,(string?Parent,SceneJsonValue Node)>Nodes){
        var Index=new Dictionary<string,(string?Parent,SceneJsonValue Node)>(StringComparer.Ordinal);
        bool Visit(SceneJsonValue Node,string?Parent){
            if(Node.Kind!=SceneJsonKind.Object||HasDuplicateNames(Node)||Node.Get("id") is not { Kind:SceneJsonKind.String,Text:{ Length:>0 }Id }||Index.ContainsKey(Id)){ return false; }
            Index.Add(Id,(Parent,Node));
            if(Node.Get("children") is { }Children){
                if(Children.Kind!=SceneJsonKind.Array){ return false; }
                foreach(var Child in Children.Items){ if(!Visit(Child,Id)){ return false; } }
            }
            return true;
        }
        Nodes=Index;
        return Document.Kind==SceneJsonKind.Object&&Document.Get("root") is { }Root&&Visit(Root,null);
    }

    private static void PreserveUnchangedNumbers(SceneJsonValue Old,SceneJsonValue Baseline,SceneJsonValue New,string OldText,List<Edit>Edits){
        if(Old.Kind!=Baseline.Kind||Baseline.Kind!=New.Kind){ return; }
        if(New.Kind==SceneJsonKind.Number){
            if(SameNumber(Baseline.Text!,New.Text!)){
                Edits.Add(new Edit(New.Start,New.End,OldText.Substring(Old.Start,Old.End-Old.Start),Edits.Count));
            }
            return;
        }
        if(New.Kind==SceneJsonKind.Object){
            if(HasDuplicateNames(Old)||HasDuplicateNames(Baseline)||HasDuplicateNames(New)||!SameIdentity(Old,Baseline,New,"id")||!SameIdentity(Old,Baseline,New,"type")){ return; }
            foreach(var Property in New.Properties){
                if(Old.Get(Property.Name) is { }OldValue&&Baseline.Get(Property.Name) is { }BaselineValue){
                    PreserveUnchangedNumbers(OldValue,BaselineValue,Property.Value,OldText,Edits);
                }
            }
            return;
        }
        if(New.Kind==SceneJsonKind.Array){
            foreach(var Name in new[]{"id","type"}){
                if(Old.Items.Concat(Baseline.Items).Concat(New.Items).Any(Item=>Item.Kind==SceneJsonKind.Object&&Item.Get(Name) is not null)){
                    if(TryIndexByIdentity(Old,Name,out var OldItems)&&TryIndexByIdentity(Baseline,Name,out var BaselineItems)&&TryIndexByIdentity(New,Name,out var NewItems)){
                        foreach(var Pair in NewItems){
                            if(OldItems.TryGetValue(Pair.Key,out var OldItem)&&BaselineItems.TryGetValue(Pair.Key,out var BaselineItem)){
                                PreserveUnchangedNumbers(OldItem,BaselineItem,Pair.Value,OldText,Edits);
                            }
                        }
                    }
                    return;
                }
            }
            if(Old.Items.Count==Baseline.Items.Count&&Baseline.Items.Count==New.Items.Count){
                for(var Index=0;Index<New.Items.Count;Index++){
                    PreserveUnchangedNumbers(Old.Items[Index],Baseline.Items[Index],New.Items[Index],OldText,Edits);
                }
            }
        }
    }
    private static bool TryIndexByIdentity(SceneJsonValue Array,string Name,out Dictionary<string,SceneJsonValue>Items){
        Items=new Dictionary<string,SceneJsonValue>(StringComparer.Ordinal);
        foreach(var Item in Array.Items){
            if(Item.Kind!=SceneJsonKind.Object||HasDuplicateNames(Item)||Item.Get(Name) is not { Kind:SceneJsonKind.String,Text:{ }Identity }||Items.ContainsKey(Identity)){ return false; }
            Items.Add(Identity,Item);
        }
        return true;
    }
    private static bool SameIdentity(SceneJsonValue Old,SceneJsonValue Baseline,SceneJsonValue New,string Name){
        var A=Old.Get(Name);var B=Baseline.Get(Name);var C=New.Get(Name);
        return A is null&&B is null&&C is null||A is not null&&B is not null&&C is not null&&SameValue(A,B)&&SameValue(B,C);
    }

    /// <summary>古い値のテキストから新しい値に変換する編集操作。</summary>
    private static void Collect(SceneJsonValue Old,SceneJsonValue New,Role Role,string OldText,string NewText,List<Edit>Edits){
        if(SameValue(Old,New,Role)){ return; }
        if(Old.Kind==SceneJsonKind.Object&&New.Kind==SceneJsonKind.Object&&CollectObject(Old,New,Role,OldText,NewText,Edits)){
            return;
        }
        if(Old.Kind==SceneJsonKind.Array&&New.Kind==SceneJsonKind.Array&&Old.Items.Count==New.Items.Count){
            for(var Index=0;Index<Old.Items.Count;Index++){
                Collect(Old.Items[Index],New.Items[Index],Role,OldText,NewText,Edits);
            }
            return;
        }
        Replace(Old,New,NewText,Edits);
    }

    /// <summary>
    /// 2つのオブジェクトをキーごとにマージする。両方にあるキーは再帰的に処理し、新オブジェクトだけにあるキーは、旧テキスト内でその前にある最も近いキーの後へ挿入する。新オブジェクトが旧キーを削除した場合、ま
    /// たはいずれかでキー名が重複している場合は false を返し、オブジェクト全体を置き換える。
    /// </summary>
    private static bool CollectObject(SceneJsonValue Old,SceneJsonValue New,Role Role,string OldText,string NewText,List<Edit>Edits){
        if(HasDuplicateNames(Old)||HasDuplicateNames(New)){ return false; }
        foreach(var Property in Old.Properties){
            if(New.Get(Property.Name) is null&&!MayLeaveOut(Role,Property.Name,Property.Value)){ return false; }
        }
        if(Old.Properties.Count==0){ return false; }
        foreach(var Property in Old.Properties){
            if(New.Get(Property.Name) is { }NewValue){
                Collect(Property.Value,NewValue,ChildRole(Role,Property.Name),OldText,NewText,Edits);
            }
        }
        var First=Old.Properties[0];
        var Separator=OldText.Substring(Old.Start+1,First.NameStart-(Old.Start+1));
        for(var Index=0;Index<New.Properties.Count;Index++){
            var Added=New.Properties[Index];
            if(Old.Get(Added.Name) is not null||MayLeaveOut(Role,Added.Name,Added.Value)){ continue; }
            var Text=NewText.Substring(Added.NameStart,Added.Value.End-Added.NameStart);
            SceneJsonValue?After=null;
            for(var Before=Index-1;Before>=0&&After is null;Before--){
                After=Old.Get(New.Properties[Before].Name);
            }
            Edits.Add(After is null
                ?new Edit(First.NameStart,First.NameStart,Text+","+Separator,Edits.Count)
                :new Edit(After.End,After.End,","+Separator+Text,Edits.Count));
        }
        return true;
    }

    private static void Replace(SceneJsonValue Old,SceneJsonValue New,string NewText,List<Edit>Edits)=>
        Edits.Add(new Edit(Old.Start,Old.End,NewText.Substring(New.Start,New.End-New.Start),Edits.Count));

    private static bool HasDuplicateNames(SceneJsonValue Object){
        var Names=new HashSet<string>(StringComparer.Ordinal);
        return Object.Properties.Any(Property=>!Names.Add(Property.Name));
    }

    private static Role ChildRole(Role Parent,string Name)=>(Parent,Name) switch {
        (Role.Document,"root")=>Role.Node,
        (Role.Node,"children")=>Role.Node,
        (Role.Node,"components")=>Role.Component,
        _=>Role.Other,
    };

    /// <summary>同じ値として読み取るキー省略状態。ノードの空の components または children、コンポーネントの空の members。</summary>
    private static bool MayLeaveOut(Role Role,string Name,SceneJsonValue Value)=>(Role,Name) switch {
        (Role.Node,"components" or "children")=>Value.Kind==SceneJsonKind.Array&&Value.Items.Count==0,
        (Role.Component,"members")=>Value.Kind==SceneJsonKind.Object&&Value.Properties.Count==0,
        _=>false,
    };

    /// <summary>同じ Scene ドキュメントかを判定する。数値は値で比較し（"1.0" と "1" は等しい）、オブジェクトはキー順を問わず比較する。空の場合に省略できるキーは、存在しない状態と等しい。</summary>
    internal static bool SameValue(SceneJsonValue Old,SceneJsonValue New)=>SameValue(Old,New,Role.Document);

    private static bool SameValue(SceneJsonValue Old,SceneJsonValue New,Role Role){
        if(Old.Kind!=New.Kind){ return false; }
        switch(Old.Kind){
            case SceneJsonKind.String:return string.Equals(Old.Text,New.Text,StringComparison.Ordinal);
            case SceneJsonKind.Number:return string.Equals(Old.Text,New.Text,StringComparison.Ordinal)||SameNumber(Old.Text!,New.Text!);
            case SceneJsonKind.Object:
                if(HasDuplicateNames(Old)||HasDuplicateNames(New)){ return SameInOrder(Old,New,Role); }
                foreach(var Property in Old.Properties){
                    if(New.Get(Property.Name) is { }NewValue){
                        if(!SameValue(Property.Value,NewValue,ChildRole(Role,Property.Name))){ return false; }
                    } else if(!MayLeaveOut(Role,Property.Name,Property.Value)){
                        return false;
                    }
                }
                return New.Properties.All(Property=>Old.Get(Property.Name) is not null||MayLeaveOut(Role,Property.Name,Property.Value));
            case SceneJsonKind.Array:
                if(Old.Items.Count!=New.Items.Count){ return false; }
                for(var Index=0;Index<Old.Items.Count;Index++){
                    if(!SameValue(Old.Items[Index],New.Items[Index],Role)){ return false; }
                }
                return true;
            default:return true;
        }
    }

    /// <summary>同じキーを2回記述したオブジェクトは、キーの順序で比較する。リーダーは最初の値を使うため、それ以外の値は区別できない。</summary>
    private static bool SameInOrder(SceneJsonValue Old,SceneJsonValue New,Role Role){
        if(Old.Properties.Count!=New.Properties.Count){ return false; }
        for(var Index=0;Index<Old.Properties.Count;Index++){
            if(!string.Equals(Old.Properties[Index].Name,New.Properties[Index].Name,StringComparison.Ordinal)
                ||!SameValue(Old.Properties[Index].Value,New.Properties[Index].Value,ChildRole(Role,Old.Properties[Index].Name))){
                return false;
            }
        }
        return true;
    }

    /// <summary>JSON 数値テキストを数値型へ丸めずに厳密比較する。符号、有効桁、指数に分解して比較するため（"1.0" と "1" はどちらも 0.1e1）、大きな整数が丸めによって同じ値になることはない。</summary>
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
