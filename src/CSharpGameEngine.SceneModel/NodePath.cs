using System.Collections.Immutable;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// Rootからの子インデックス列。<b>Scene DSLにおけるノードの唯一の同一性</b>(ADR-0040)。
///
/// <para>
/// <c>Name</c>は一意性を保証せず(省略可能・日本語や空白も入る)、<see cref="SceneSourceLocation.Span"/>は
/// 1文字の編集で全部ずれる。値の編集ではこのパスだけが不変なので、Inspectorの選択復元・Hierarchyの展開状態・
/// Scene Viewのピッキング結果はすべてこの型をキーにする。構造編集(ノード追加/削除)ではパスが変わるが、
/// その変更を起こしたのはエディター自身なので新しいパスを計算して復元できる。
/// </para>
/// </summary>
internal readonly struct NodePath : IEquatable<NodePath> {
    private readonly ImmutableArray<int>Values;

    internal NodePath(ImmutableArray<int>Values){
        this.Values=Values;
    }

    /// <summary><c>default(NodePath)</c>でも安全に空配列を返す。</summary>
    internal ImmutableArray<int>Indices=>this.Values.IsDefault?ImmutableArray<int>.Empty:this.Values;

    internal static NodePath Root=>new(ImmutableArray<int>.Empty);

    internal int Depth=>this.Indices.Length;

    internal bool IsRoot=>this.Indices.Length==0;

    internal NodePath Append(int ChildIndex)=>new(this.Indices.Add(ChildIndex));

    internal NodePath?Parent{
        get{
            ImmutableArray<int>Current=this.Indices;
            return Current.Length==0?null:new NodePath(Current.RemoveAt(Current.Length-1));
        }
    }

    /// <summary><paramref name="Other"/>がこのパスの祖先(自分自身を含まない)かどうか。</summary>
    internal bool IsDescendantOf(NodePath Other){
        ImmutableArray<int>Self=this.Indices;
        ImmutableArray<int>Ancestor=Other.Indices;

        if(Ancestor.Length>=Self.Length){
            return false;
        }

        for(var Index=0;Index<Ancestor.Length;Index++){
            if(Self[Index]!=Ancestor[Index]){
                return false;
            }
        }

        return true;
    }

    public bool Equals(NodePath Other){
        ImmutableArray<int>Self=this.Indices;
        ImmutableArray<int>Target=Other.Indices;

        if(Self.Length!=Target.Length){
            return false;
        }

        for(var Index=0;Index<Self.Length;Index++){
            if(Self[Index]!=Target[Index]){
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object?Obj)=>Obj is NodePath Other&&this.Equals(Other);

    public override int GetHashCode(){
        unchecked {
            var Hash=17;

            foreach(var Value in this.Indices){
                Hash=(Hash*31)+Value;
            }

            return Hash;
        }
    }

    /// <summary>生成コードのコメント・診断メッセージ・UI表示に使う人間可読形("0/2/1"、Rootは"(root)")。</summary>
    internal string ToDisplayString()=>this.Indices.Length==0?"(root)":string.Join("/",this.Indices);

    public override string ToString()=>this.ToDisplayString();

    /// <summary><see cref="ToDisplayString"/>の逆変換。UIの状態永続化用。</summary>
    internal static bool TryParse(string?Text,out NodePath Path){
        Path=Root;

        if(Text is null){
            return false;
        }

        if(Text.Length==0||Text=="(root)"){
            return true;
        }

        ImmutableArray<int>.Builder Builder=ImmutableArray.CreateBuilder<int>();

        foreach(var Part in Text.Split('/')){
            if(!int.TryParse(Part,out var Value)||Value<0){
                return false;
            }

            Builder.Add(Value);
        }

        Path=new NodePath(Builder.ToImmutable());
        return true;
    }
}
