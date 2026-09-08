using CSharpGameEngine.SceneAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace CSharpGameEngine.VSExtension;

internal abstract class InspectorSelectableViewModel:INotifyPropertyChanged {
    private bool Selected;

    public bool IsSelected {
        get=>this.Selected;
        set {
            if(this.Selected==value){
                return;
            }

            this.Selected=value;
            this.PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(this.IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
/// <summary>
/// 1行分のWPF ViewModel。<see cref="InspectorToolWindowControl"/>は選択変更・コミット完了のたびに
/// 行を丸ごと作り直す(編集中の行はフォーカスで再構築を止める、Hierarchyの再構築対応とは違う単純な方式で
/// 足りる — Inspectorの再構築頻度は選択変更時だけで、キー入力のたびではないため)。そのため
/// <c>INotifyPropertyChanged</c>は不要。
/// </summary>
internal sealed class InspectorMemberViewModel:InspectorSelectableViewModel {
    internal InspectorMemberViewModel(SceneInspectorMember Member){
        this.Member=Member;
        this.DisplayText=Member.EditorKind switch {
            InspectorEditorKind.PhysicalValue=>Member.ConstantValue as string??Member.SourceExpression,
            InspectorEditorKind.Text=>Member.ConstantValue as string??string.Empty,
            InspectorEditorKind.Number=>SceneInspectorValueFormat.RemoveFloatSuffix(Member.SourceExpression),
            _=>Member.SourceExpression,
        };
        this.BooleanValue=Member.ConstantValue is bool BoolValue&&BoolValue;
        this.SelectedChoiceName=SceneInspectorCommitPlanner.GetCurrentChoiceName(Member.SourceExpression);
    }

    internal SceneInspectorMember Member { get; }

    // InspectorToolWindowControl.xamlからデータバインドされるため必ずpublicにする。WPFのバインディングは
    // publicプロパティしか解決できず、internalだと例外も警告もなく黙って失敗する。
    // InspectorMemberViewModelTestsでアクセシビリティを固定している。

    public string Label=>SceneInspectorTypeName.Simplify(this.Member.ComponentTypeFullyQualifiedName)+"."+this.Member.MemberName;

    /// <summary>グルーピング表示での行ラベル。Component名はExpanderのヘッダ側が出すので重ねない
    /// (<see cref="Label"/>は引き続きツールチップで完全な位置を示すために使う)。</summary>
    public string MemberName=>this.Member.PhysicalContract is { }Physical?this.Member.MemberName+" ("+Physical.Unit+"; "+string.Join(", ",Physical.Parameters)+")":this.Member.MemberName;

    public InspectorEditorKind EditorKind=>this.Member.EditorKind;

    /// <summary>Number/Textエディタの初期表示・Escape復帰先。数値はソース式の接尾辞(3f等)を保つ。</summary>
    public string DisplayText { get; }

    // Checked/SelectionChangedではなくClick/DropDownClosedで確定させるため(下記コメント参照)、
    // これらは初期表示専用のOneWayバインディングで足りる。setterは不要。
    public bool BooleanValue { get; }

    public string?SelectedChoiceName { get; }

    public IReadOnlyList<string>ChoiceCandidateNames=>this.Member.Candidates.Select(static Candidate=>Candidate.Name).ToArray();

}

/// <summary>
/// M0052: Component 1件=Expander 1つ分のWPF ViewModel。
///
/// <para>
/// <see cref="IsExpanded"/>は<c>SceneNodeViewModel.IsExpanded</c>と同じくTwoWayバインドし、
/// 再構築の直前に生きた<c>ItemsSource</c>から読み戻す。<b>Expanderの<c>Expanded</c>/
/// <c>Collapsed</c>イベントは購読しない</b> — これらは初期のデータバインド代入自体でも発火するため、
/// 状態更新の起点にすると「確定→再構築→再バインド→再確定」のループを招く
/// (<c>InspectorToolWindowControl.xaml</c>のCheckBox/ComboBoxに同じ注意書きがある)。
/// </para>
/// </summary>
internal sealed class InspectorComponentGroupViewModel:InspectorSelectableViewModel {
    /// <summary>
    /// <paramref name="Rows"/>は呼び出し側(<see cref="InspectorToolWindowControl"/>)が
    /// <see cref="SceneInspectorComponentGroup.Rows"/>から再帰的に構築済みのもの
    /// (<see cref="InspectorMemberViewModel"/>と<see cref="InspectorNestedRowViewModel"/>の混在)を渡す。
    /// ここで自前に組み立てないのは、ネストした行1つ1つの<c>IsExpanded</c>初期値が
    /// <c>CollapsedKeys</c>/<c>PendingRevealKey</c>という<see cref="InspectorToolWindowControl"/>側の
    /// 状態を要るため。
    /// </summary>
    internal InspectorComponentGroupViewModel(
        SceneInspectorComponentGroup Group,IReadOnlyList<object>Rows,bool IsExpanded){
        this.Group=Group;
        this.Rows=Rows;
        this.IsExpanded=IsExpanded;
    }

    internal SceneInspectorComponentGroup Group { get; }

    internal string ExpansionKey=>
        InspectorExpansionKey.Compute(this.Group.NodeId,this.Group.ComponentTypeFullyQualifiedName);

    // InspectorToolWindowControl.xamlからデータバインドされるため必ずpublicにする。WPFのバインディングは
    // publicプロパティしか解決できず、internalだと例外も警告もなく黙って失敗する。
    // InspectorComponentGroupViewModelTestsでアクセシビリティを固定している。

    public string Header=>this.Group.DisplayName;

    /// <summary>ヘッダのツールチップ。generic instantiationや同名型を完全修飾名で判別できるようにする。</summary>
    public string FullTypeName=>this.Group.ComponentTypeFullyQualifiedName;

    /// <summary><see cref="InspectorMemberViewModel"/>と<see cref="InspectorNestedRowViewModel"/>の混在。
    /// XAML側は<c>DataType</c>ベースの暗黙<c>DataTemplate</c>で実行時型ごとに描き分ける。</summary>
    public IReadOnlyList<object>Rows { get; }

    public bool IsExpanded { get; set; }

    /// <summary>
    /// Componentを追加した直後はmemberが空で、Roslynの再解析が着地するまで値を設定できない。
    /// その空白を「解析待ち」として明示する(空のまま何も出さないと追加が失敗したように見える)。
    /// </summary>
    public Visibility LightConversionVisibility=>LightComponentContract.IsLight(this.Group.ComponentTypeFullyQualifiedName)?Visibility.Visible:Visibility.Collapsed;
    public Visibility PendingMessageVisibility=>
        this.Rows.Count==0?Visibility.Visible:Visibility.Collapsed;
}

/// <summary>
/// M0052フォローアップ: <c>new Vector3(1,2,3)</c>のようなネストした値を持つ行のWPF ViewModel。
/// <see cref="InspectorComponentGroupViewModel"/>と対になる構造(Expander + 行一覧)を、
/// もう1段(以上)再帰的に重ねるために使う。
///
/// <para>
/// <see cref="Rows"/>の葉は<see cref="SceneInspectorNestedRow"/>のドキュメントコメントにある理由で
/// 常に読み取り専用として構築済み — このViewModelは表示するだけで、編集可否の判断はここには無い。
/// </para>
/// </summary>
internal sealed class InspectorNestedRowViewModel:InspectorSelectableViewModel {
    internal InspectorNestedRowViewModel(SceneInspectorNestedRow Row,IReadOnlyList<object>Rows,bool IsExpanded){
        this.Row=Row;
        this.Rows=Rows;
        this.IsExpanded=IsExpanded;
    }

    internal SceneInspectorNestedRow Row { get; }

    internal string ExpansionKey=>
        InspectorExpansionKey.Compute(this.Row.NodeId,this.Row.ComponentTypeFullyQualifiedName,this.Row.MemberPath);

    // InspectorToolWindowControl.xamlからデータバインドされるため必ずpublicにする(上記と同じ理由)。
    // InspectorMemberViewModelTestsでアクセシビリティを固定している。

    public string Header=>this.Row.DisplayName;

    public string TypeDisplayName=>this.Row.TypeDisplayName;

    public IReadOnlyList<object>Rows { get; }

    public bool IsExpanded { get; set; }
}
