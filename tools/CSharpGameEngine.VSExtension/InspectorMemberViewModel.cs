using System.Collections.Generic;
using System.Linq;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 1行分のWPF ViewModel。<see cref="InspectorToolWindowControl"/>は選択変更・コミット完了のたびに
/// 行を丸ごと作り直す(編集中の行はフォーカスで再構築を止める、Hierarchyの再構築対応とは違う単純な方式で
/// 足りる — Inspectorの再構築頻度は選択変更時だけで、キー入力のたびではないため)。そのため
/// <c>INotifyPropertyChanged</c>は不要。
/// </summary>
internal sealed class InspectorMemberViewModel {
    internal InspectorMemberViewModel(SceneInspectorMember Member){
        this.Member=Member;
        this.DisplayText=Member.EditorKind==InspectorEditorKind.Text
            ?Member.ConstantValue as string??string.Empty
            :Member.SourceExpression;
        this.BooleanValue=Member.ConstantValue is bool BoolValue&&BoolValue;
        this.SelectedEnumName=ExtractTrailingIdentifier(Member.SourceExpression);
    }

    internal SceneInspectorMember Member { get; }

    // InspectorToolWindowControl.xamlからデータバインドされるため必ずpublicにする。WPFのバインディングは
    // publicプロパティしか解決できず、internalだと例外も警告もなく黙って失敗する。
    // InspectorMemberViewModelTestsでアクセシビリティを固定している。

    public string Label=>SimpleName(this.Member.ComponentTypeFullyQualifiedName)+"."+this.Member.MemberName;

    public InspectorEditorKind EditorKind=>this.Member.EditorKind;

    /// <summary>Number/Textエディタの初期表示・Escape復帰先。数値はソース式の接尾辞(3f等)を保つ。</summary>
    public string DisplayText { get; }

    // Checked/SelectionChangedではなくClick/DropDownClosedで確定させるため(下記コメント参照)、
    // これらは初期表示専用のOneWayバインディングで足りる。setterは不要。
    public bool BooleanValue { get; }

    public string?SelectedEnumName { get; }

    public IReadOnlyList<string>EnumCandidateNames=>this.Member.EnumCandidates.Select(static Candidate=>Candidate.Name).ToArray();

    private static string SimpleName(string TypeFullyQualifiedName){
        var LastDot=TypeFullyQualifiedName.LastIndexOf('.');
        return LastDot>=0?TypeFullyQualifiedName.Substring(LastDot+1):TypeFullyQualifiedName;
    }

    /// <summary>"Sample.PrimitiveType.Cube"→"Cube"。Enum候補名との照合はConstantValue(数値)ではなく
    /// 名前で行う — flags enumの値重複でも取り違えない。</summary>
    private static string ExtractTrailingIdentifier(string Expression){
        var LastDot=Expression.LastIndexOf('.');
        return LastDot>=0?Expression.Substring(LastDot+1):Expression;
    }
}
