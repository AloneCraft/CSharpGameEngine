using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// <c>IScene</c>を実装する1クラスの解析結果。<b>Source GeneratorとVSIXが共有する唯一のScene表現</b>
/// (ADR-0040)。SGはこれを<c>SceneEmitModel</c>へ落としてからArchetype登録とコード生成に使い、
/// VSIXはこれをそのままHierarchy/Scene View/Inspectorへ描画する。
///
/// <para>
/// 共有されるのは<b>解析コードのソース</b>であってインスタンスではない。SGはコンパイラーが供給する
/// <c>Compilation</c>上で、VSIXは<c>VisualStudioWorkspace</c>上で、それぞれ同じ
/// <see cref="SceneDeclarationAnalyzer"/>を走らせる。これにより「SGは受理するがエディターは壊れる」
/// というクラスのバグが構造的に発生しない。
/// </para>
/// </summary>
internal sealed class SceneModel {
    internal SceneModel(
        string?Namespace,
        string ClassName,
        string ClassFullyQualifiedName,
        INamedTypeSymbol?ClassSymbol,
        SceneNode?Root,
        bool IsStartupScene,
        int StartupOrder,
        ImmutableArray<Diagnostic>Diagnostics,
        string FilePath,
        int DeclarationPosition){
        this.Namespace=Namespace;
        this.ClassName=ClassName;
        this.ClassFullyQualifiedName=ClassFullyQualifiedName;
        this.ClassSymbol=ClassSymbol;
        this.Root=Root;
        this.IsStartupScene=IsStartupScene;
        this.StartupOrder=StartupOrder;
        this.Diagnostics=Diagnostics;
        this.FilePath=FilePath;
        this.DeclarationPosition=DeclarationPosition;
    }

    internal string?Namespace { get;}

    internal string ClassName { get;}

    /// <summary><c>global::</c>接頭辞<b>なし</b>の完全修飾名(<c>GameSystemResult.ClassFullyQualifiedName</c>と同じキー空間)。</summary>
    internal string ClassFullyQualifiedName { get;}

    internal INamedTypeSymbol?ClassSymbol { get;}

    /// <summary>解析に失敗した場合は<c>null</c>。</summary>
    internal SceneNode?Root { get;}

    /// <summary><c>[StartupScene]</c>が付いているか。SGが起動時に自動でinstantiateする合成Systemを作る。</summary>
    internal bool IsStartupScene { get;}

    internal int StartupOrder { get;}

    internal ImmutableArray<Diagnostic>Diagnostics { get;}

    internal string FilePath { get;}

    /// <summary>決定論的な整列キー(<c>SourceSpan.Start</c>)。<c>Collect()</c>の順序に依存しないため。</summary>
    internal int DeclarationPosition { get;}

    internal bool HasErrors{
        get{
            foreach(Diagnostic Diagnostic in this.Diagnostics){
                if(Diagnostic.Severity==DiagnosticSeverity.Error){
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>ノードの総数。生成コードの<c>BeginSceneInstance</c>へ渡す容量ヒントになる。</summary>
    internal int TotalNodeCount=>this.Root is { } Node?CountNodes(Node):0;

    /// <summary>編集後の選択復元に使う。存在しないパスなら<c>null</c>。</summary>
    internal SceneNode?FindNode(NodePath Path){
        if(this.Root is not { } Current){
            return null;
        }

        foreach(var ChildIndex in Path.Indices){
            if((uint)ChildIndex>=(uint)Current.Children.Length){
                return null;
            }

            Current=Current.Children[ChildIndex];
        }

        return Current;
    }

    /// <summary>Rootから深さ優先・宣言順で全ノードを列挙する(生成コードのEntity生成順と一致する)。</summary>
    internal IEnumerable<SceneNode>EnumerateNodes(){
        if(this.Root is not { } Root){
            yield break;
        }

        Stack<SceneNode>Pending=new();
        Pending.Push(Root);

        while(Pending.Count>0){
            SceneNode Current=Pending.Pop();
            yield return Current;

            for(var Index=Current.Children.Length-1;Index>=0;Index--){
                Pending.Push(Current.Children[Index]);
            }
        }
    }

    internal static SceneModel Failed(
        string?Namespace,
        string ClassName,
        string ClassFullyQualifiedName,
        INamedTypeSymbol?ClassSymbol,
        ImmutableArray<Diagnostic>Diagnostics,
        string FilePath,
        int DeclarationPosition){
        return new SceneModel(
            Namespace,
            ClassName,
            ClassFullyQualifiedName,
            ClassSymbol,
            Root:null,
            IsStartupScene:false,
            StartupOrder:0,
            Diagnostics,
            FilePath,
            DeclarationPosition);
    }

    private static int CountNodes(SceneNode Node){
        var Count=1;

        foreach(SceneNode Child in Node.Children){
            Count+=CountNodes(Child);
        }

        return Count;
    }
}
