using System;
using System.Collections.Generic;
using System.Linq;

namespace CSharpGameEngine.VSExtension;

/// <summary>インスペクターに表示される行を、見た順で選択・移動する。</summary>
internal sealed class InspectorSelectionController {
    private readonly List<InspectorSelectableViewModel>Items=[];
    private InspectorSelectableViewModel?Selected;

    internal InspectorSelectableViewModel?Current=>this.Selected;

    internal void SetItems(IEnumerable<InspectorComponentGroupViewModel>Groups){
        // 強制更新では新しい ViewModels が作成されます。commit 時に選択行が消えないよう、論理的な選択を保持します。
        // これは同じクリックで別の editor に移動した結果として発生することがあります。
        InspectorSelectableViewModel?Previous=this.Selected;
        this.ClearSelection();
        this.Items.Clear();
        foreach(InspectorComponentGroupViewModel Group in Groups){
            this.Items.Add(Group);
            foreach(object Row in Group.Rows){
                this.AddRow(Row);
            }
        }

        if(Previous is not null
            &&this.Items.FirstOrDefault(Candidate=>RepresentsSameRow(Previous,Candidate)) is { }Replacement){
            this.Select(Replacement);
        }
    }

    internal bool Select(object?Item){
        if(Item is not InspectorSelectableViewModel Candidate||!this.Items.Contains(Candidate)){
            return false;
        }

        if(ReferenceEquals(this.Selected,Candidate)){
            return false;
        }

        this.ClearSelection();
        this.Selected=Candidate;
        Candidate.IsSelected=true;
        return true;
    }

    internal InspectorSelectableViewModel?Move(int Delta){
        if(this.Items.Count==0){
            return null;
        }

        int Index=this.Selected is null?Delta>0?0:this.Items.Count-1:this.Items.IndexOf(this.Selected)+Delta;
        Index=Index<0?0:Index>=this.Items.Count?this.Items.Count-1:Index;
        this.Select(this.Items[Index]);
        return this.Selected;
    }

    internal IReadOnlyList<InspectorSelectableViewModel>Snapshot()=>this.Items.ToArray();

    private void ClearSelection(){
        if(this.Selected is { }Previous){
            Previous.IsSelected=false;
            this.Selected=null;
        }
    }

    private void AddRow(object Row){
        if(Row is not InspectorSelectableViewModel Selectable){
            return;
        }

        this.Items.Add(Selectable);
        if(Row is InspectorNestedRowViewModel Nested){
            foreach(object Child in Nested.Rows){
                this.AddRow(Child);
            }
        }
    }

    private static bool RepresentsSameRow(InspectorSelectableViewModel Left,InspectorSelectableViewModel Right)=>
        (Left,Right) switch {
            (InspectorComponentGroupViewModel LeftGroup,InspectorComponentGroupViewModel RightGroup)=>
                IsSameComponent(
                    LeftGroup.Group.NodeId,LeftGroup.FullTypeName,
                    RightGroup.Group.NodeId,RightGroup.FullTypeName),
            (InspectorMemberViewModel LeftMember,InspectorMemberViewModel RightMember)=>
                IsSameComponent(
                    LeftMember.Member.NodeId,LeftMember.Member.ComponentTypeFullyQualifiedName,
                    RightMember.Member.NodeId,RightMember.Member.ComponentTypeFullyQualifiedName)
                &&HasSamePath(LeftMember.Member.DefinitionMemberPath,RightMember.Member.DefinitionMemberPath),
            (InspectorNestedRowViewModel LeftNested,InspectorNestedRowViewModel RightNested)=>
                IsSameComponent(
                    LeftNested.Row.NodeId,LeftNested.Row.ComponentTypeFullyQualifiedName,
                    RightNested.Row.NodeId,RightNested.Row.ComponentTypeFullyQualifiedName)
                &&HasSamePath(LeftNested.Row.MemberPath,RightNested.Row.MemberPath),
            _=>false,
        };

    private static bool IsSameComponent(
        EditorNodeId LeftNodeId,string LeftTypeName,EditorNodeId RightNodeId,string RightTypeName)=>
        LeftNodeId.Equals(RightNodeId)
        &&string.Equals(LeftTypeName,RightTypeName,StringComparison.Ordinal);

    private static bool HasSamePath(IReadOnlyList<string>Left,IReadOnlyList<string>Right){
        if(Left.Count!=Right.Count){
            return false;
        }

        for(var Index=0;Index<Left.Count;Index++){
            if(!string.Equals(Left[Index],Right[Index],StringComparison.Ordinal)){
                return false;
            }
        }

        return true;
    }
}