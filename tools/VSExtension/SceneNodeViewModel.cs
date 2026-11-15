using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Hierarchy TreeView 用の ViewModel (ADR-0040)。この VSIX で最初に導入した MVVM 型である。それ以外はすべてモデル型に直接バインドしていた。モデルがファイルを開いた時にしか変更されなかった間は問題なかったが、ライブ Roslyn シーンはキー入力ごとに変化するためツリーが頻繁に再構築され、再構築をまたいで展開状態と選択状態を引き継ぐ必要がある。このためにこの型がある。
///
/// <para>
/// 識別には名前でもテキスト範囲でもなく <see cref="NodePath"/> を使う。名前は任意で一意ではなく、テキスト範囲は編集のたびに移動する。展開状態をパスで管理することで、ユーザーの入力中にツリーが勝手に折りたたまれるのを防ぐ。
/// </para>
/// </summary>
internal sealed class SceneNodeViewModel : INotifyPropertyChanged {
    private bool IsExpandedField;
    private bool IsSelectedField;
    private bool IsMultiSelectedField;
    private bool IsRenamingField;
    private string EditNameField=string.Empty;

    private SceneNodeViewModel(
        NodePath Path,string DisplayName,string ComponentSummary,ImageMoniker NodeMoniker,
        IReadOnlyList<SceneVisualIcon> ComponentIcons,IReadOnlyList<SceneNodeViewModel>Children){
        this.Path=Path;
        this.DisplayName=DisplayName;
        this.ComponentSummary=ComponentSummary;
        this.NodeMoniker=NodeMoniker;
        this.ComponentIcons=ComponentIcons;
        this.Children=Children;
    }

    public event PropertyChangedEventHandler?PropertyChanged;

    internal NodePath Path { get;}

    // DisplayName/ComponentSummary/ChildrenはHierarchyToolWindowControl.xamlからデータバインド
    // されるため必ずpublicにする。WPFのバインディングはpublicプロパティしか解決できず、internalだと
    // 例外も警告もなく黙って失敗する(＝ツリーが空白になる)。テストプロジェクトには
    // InternalsVisibleToが効いているのでinternalでも読めてしまい、この種の不具合は通常の単体
    // テストでは検出できない。SceneNodeViewModelTestsの
    // PropertiesBoundFromXamlArePublicSoWpfCanResolveThemでアクセシビリティを固定している。
    // Pathはコードからのみ使いXAMLからはバインドしないのでinternalで足りる。

    /// <summary>シーンに名前が宣言されていればその名前、なければ node path。ノードは常に特定できます。</summary>
    public string DisplayName { get;}

    /// <summary>コンポーネント型の簡易名（例："Transform, PrimitiveMesh"）。コンポーネントのないノードでは空です。</summary>
    public string ComponentSummary { get;}

    /// <summary>意味を示す node glyph：Scene root または通常のノード。CrispImage で描画します。</summary>
    public ImageMoniker NodeMoniker { get;}

    /// <summary>宣言順に並べた、宣言済みコンポーネントすべてのアイコン。アイコンを省略しません。</summary>
    public IReadOnlyList<SceneVisualIcon> ComponentIcons { get;}

    public IReadOnlyList<SceneNodeViewModel>Children { get;}

    public bool HasChildren=>this.Children.Count>0;

    public bool IsExpanded {
        get=>this.IsExpandedField;
        set=>this.Set(ref this.IsExpandedField,value);
    }

    public bool IsSelected {
        get=>this.IsSelectedField;
        set=>this.Set(ref this.IsSelectedField,value);
    }

    public bool IsMultiSelected {
        get=>this.IsMultiSelectedField;
        set=>this.Set(ref this.IsMultiSelectedField,value);
    }
    public bool IsRenaming {
        get=>this.IsRenamingField;
        set=>this.Set(ref this.IsRenamingField,value);
    }

    public string EditName {
        get=>this.EditNameField;
        set{
            if(string.Equals(this.EditNameField,value,StringComparison.Ordinal)){
                return;
            }

            this.EditNameField=value;
            this.PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(this.EditName)));
        }
    }

    internal void BeginRename(){
        this.EditName=this.DisplayName;
        this.IsRenaming=true;
    }

    internal void CancelRename()=>this.IsRenaming=false;

    /// <summary>
    /// モデルのツリーを構築し、前のツリーの展開状態と選択状態を <see cref="NodePath"/> で引き継ぐ。
    ///
    /// <para>
    /// 呼び出し元が保持するのは展開中のパスではなく<b>折りたたまれた</b>パスの集合である。操作されていないノードは展開して表示する。ライブシーンはキー入力ごとに再構築されるため、既定で折りたたむと、そのたびに開閉三角形 1 つの背後にツリー全体が隠れてしまう。例外を記録する方式なら、ほとんどのノードは折りたたまれないため、集合も小さく保てる。
    /// </para>
    /// </summary>
    internal static IReadOnlyList<SceneNodeViewModel>Build(SceneModel?Model,ISet<NodePath>CollapsedPaths,NodePath?SelectedPath){
        var Roots=BuildWithSelection(Model,CollapsedPaths,SelectedPath is { } SelectedPathValue?[SelectedPathValue]:[]);
        if(SelectedPath is { } TargetPath){
            Roots.SelectMany(static Root=>Root.SelfAndDescendants()).FirstOrDefault(Node=>Node.Path.Equals(TargetPath))?.IsSelected=true;
        }

        return Roots;
    }

    internal static IReadOnlyList<SceneNodeViewModel>BuildWithSelection(
        SceneModel?Model,ISet<NodePath>CollapsedPaths,IReadOnlyCollection<NodePath>SelectedPaths){
        if(Model?.Root is not { } Root){
            return [];
        }

        var ProjectDirectory=FindProjectDirectory(Model.FilePath);
        return [BuildNode(Root,CollapsedPaths,SelectedPaths,ProjectDirectory,IsRoot:true)];
    }

    private static SceneNodeViewModel BuildNode(
        SceneNode Node,ISet<NodePath>CollapsedPaths,IReadOnlyCollection<NodePath>SelectedPaths,
        string?ProjectDirectory,bool IsRoot){
        List<SceneNodeViewModel>Children=[];

        foreach(var Child in Node.Children){
            Children.Add(BuildNode(Child,CollapsedPaths,SelectedPaths,ProjectDirectory,IsRoot:false));
        }

        return new SceneNodeViewModel(
            Node.Path,
            Node.DisplayName,
            SummarizeComponents(Node.Components),
            IsRoot?KnownMonikers.ThreeDScene:KnownMonikers.AbstractCube,
            ResolveComponentIcons(Node.Components,ProjectDirectory),
            Children){
            IsExpandedField=!CollapsedPaths.Contains(Node.Path),
            IsSelectedField=false,
            IsMultiSelectedField=SelectedPaths.Contains(Node.Path),
        };
    }
    private static string SummarizeComponents(ImmutableArray<SceneComponent>Components){
        if(Components.Length==0){
            return string.Empty;
        }

        return string.Join(", ",Components.Select(static C=>SimpleName(C.TypeFullyQualifiedName)));
    }

    private static string SimpleName(string TypeFullyQualifiedName){
        var LastDot=TypeFullyQualifiedName.LastIndexOf('.');
        return LastDot>=0?TypeFullyQualifiedName.Substring(LastDot+1):TypeFullyQualifiedName;
    }

    private static IReadOnlyList<SceneVisualIcon> ResolveComponentIcons(
        ImmutableArray<SceneComponent> Components,string?ProjectDirectory){
        if(Components.Length==0){
            return [];
        }

        List<SceneVisualIcon>Icons=[];
        foreach(var Component in Components){
            Icons.Add(Component.TypeSymbol is { } Type
                ?SceneComponentIconResolver.Resolve(Type,ProjectDirectory)
                :SceneVisualIcon.ComponentDefault());
        }

        return Icons;
    }

    private static string? FindProjectDirectory(string FilePath){
        try {
            var DirectoryPath=System.IO.Path.GetDirectoryName(FilePath);
            while(!string.IsNullOrWhiteSpace(DirectoryPath)){
                if(Directory.EnumerateFiles(DirectoryPath,"*.csproj",SearchOption.TopDirectoryOnly).Any()){
                    return DirectoryPath;
                }

                var Parent=Directory.GetParent(DirectoryPath);
                if(Parent is null||string.Equals(Parent.FullName,DirectoryPath,StringComparison.OrdinalIgnoreCase)){
                    break;
                }

                DirectoryPath=Parent.FullName;
            }
        } catch(ArgumentException){
            // 一時的なパスまたは生成されたソースパスには、VS の既定のコンポーネントアイコンを使う。
        } catch(IOException){
            // プロジェクトの再読み込みと、実行中の Hierarchy の再構築が競合する場合がある。
        } catch(UnauthorizedAccessException){
            // アクセスできない祖先があっても UI を操作可能な状態に保つ。
        }

        return null;
    }

    /// <summary>深さ優先、宣言順。生成された Instantiate が Entity を作成する順序と同じ。</summary>
    internal IEnumerable<SceneNodeViewModel>SelfAndDescendants(){
        yield return this;

        foreach(var Child in this.Children){
            foreach(var Descendant in Child.SelfAndDescendants()){
                yield return Descendant;
            }
        }
    }

    private void Set(ref bool Field,bool Value,[CallerMemberName]string?PropertyName=null){
        if(Field==Value){
            return;
        }

        Field=Value;
        this.PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(PropertyName));
    }
}
