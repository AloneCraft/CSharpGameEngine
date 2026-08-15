using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Hierarchy TreeView 用の ViewModel (ADR-0040). The first MVVM type in this VSIX; everything else
/// binds directly to model types, which was fine when the model only changed on file open. A live
/// Roslyn scene changes on every keystroke, so the tree is rebuilt constantly and needs to carry
/// expansion/selection across rebuilds - that is what this type exists for.
///
/// <para>
/// Identity is <see cref="NodePath"/>, never the name and never a text span: names are optional and
/// not unique, and spans move on every edit. Keeping expansion state keyed by path is what stops the
/// tree from collapsing itself while the user types.
/// </para>
/// </summary>
internal sealed class SceneNodeViewModel : INotifyPropertyChanged {
    private bool IsExpandedField;
    private bool IsSelectedField;

    private SceneNodeViewModel(NodePath Path,string DisplayName,string ComponentSummary,IReadOnlyList<SceneNodeViewModel>Children){
        this.Path=Path;
        this.DisplayName=DisplayName;
        this.ComponentSummary=ComponentSummary;
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

    /// <summary>Name if the scene declared one, otherwise the node path - a node is always addressable.</summary>
    public string DisplayName { get;}

    /// <summary>Simple component type names, e.g. "Transform, PrimitiveMesh". Empty for a bare node.</summary>
    public string ComponentSummary { get;}

    public IReadOnlyList<SceneNodeViewModel>Children { get;}

    public bool IsExpanded {
        get=>this.IsExpandedField;
        set=>this.Set(ref this.IsExpandedField,value);
    }

    public bool IsSelected {
        get=>this.IsSelectedField;
        set=>this.Set(ref this.IsSelectedField,value);
    }

    /// <summary>
    /// Builds the tree for a model, carrying expansion and selection over from the previous tree by
    /// <see cref="NodePath"/>.
    ///
    /// <para>
    /// The caller keeps a set of <b>collapsed</b> paths rather than expanded ones, so a node nobody
    /// has touched renders expanded: a live scene rebuilds on every keystroke, and defaulting to
    /// collapsed would hide the whole tree behind one disclosure triangle each time. Tracking the
    /// exceptions also keeps the set small - most nodes are never collapsed.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<SceneNodeViewModel>Build(SceneModel?Model,ISet<NodePath>CollapsedPaths,NodePath?SelectedPath){
        if(Model?.Root is not { } Root){
            return [];
        }

        return [BuildNode(Root,CollapsedPaths,SelectedPath)];
    }

    private static SceneNodeViewModel BuildNode(SceneNode Node,ISet<NodePath>CollapsedPaths,NodePath?SelectedPath){
        List<SceneNodeViewModel>Children=[];

        foreach(SceneNode Child in Node.Children){
            Children.Add(BuildNode(Child,CollapsedPaths,SelectedPath));
        }

        return new SceneNodeViewModel(
            Node.Path,
            Node.DisplayName,
            SummarizeComponents(Node.Components),
            Children){
            IsExpandedField=!CollapsedPaths.Contains(Node.Path),
            IsSelectedField=SelectedPath is { } Selected&&Selected.Equals(Node.Path),
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

    /// <summary>Depth-first, declaration order - the same order the generated Instantiate creates entities in.</summary>
    internal IEnumerable<SceneNodeViewModel>SelfAndDescendants(){
        yield return this;

        foreach(SceneNodeViewModel Child in this.Children){
            foreach(SceneNodeViewModel Descendant in Child.SelfAndDescendants()){
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
