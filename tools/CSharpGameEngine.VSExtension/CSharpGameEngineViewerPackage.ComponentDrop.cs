using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

public sealed partial class CSharpGameEngineViewerPackage {
    /// <summary>
    /// Hierarchy の空白部分にコンポーネントをドロップした場合、シーン操作 1 回でルートの子を作成する。ライトに必要な Transform も同じ操作で追加するため、エディター、ソースライター、Undo スタックのいずれからも、一時的に不正な Entity を観測できない。
    /// </summary>
    private async Task TryCreateRootChildForComponentAsync(
        ComponentAddRequest Request,SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        if(Snapshot.Projection is not { } Projection
            ||!Projection.TryGetId(NodePath.Root,out EditorNodeId RootId)){
            SetStatusBarText(VsixText.Get("ComponentDropRootResolveFailed"));
            return;
        }

        var Dropped=await Session.ResolveComponentTypeAsync(
            Request.ComponentTypeFullyQualifiedName,this.DisposalToken);
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        if(Dropped is not { } DroppedType){
            SetStatusBarText(VsixText.Get("ComponentDropTypeResolveFailed"));
            return;
        }

        SceneComponentInitializerPlan DroppedPlan=SceneComponentInitializerPolicy.CreatePlan(
            DroppedType.Symbol,Request.ComponentTypeFullyQualifiedName,DroppedType.Compilation);
        List<NewNodeComponent>InitialComponents=[];
        if(RequiresTransform(Request.ComponentTypeFullyQualifiedName)){
            var Transform=await Session.ResolveComponentTypeAsync(
                SceneComponentBindings.TransformTypeFullyQualifiedName,this.DisposalToken);
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            if(Transform is not { } TransformType){
                SetStatusBarText(VsixText.Get("ComponentDropTransformResolveFailed"));
                return;
            }

            SceneComponentInitializerPlan TransformPlan=SceneComponentInitializerPolicy.CreatePlan(
                TransformType.Symbol,SceneComponentBindings.TransformTypeFullyQualifiedName,
                TransformType.Compilation);
            InitialComponents.Add(new NewNodeComponent(
                SceneComponentBindings.TransformTypeFullyQualifiedName,
                CreateInitialTransformExpression(
                    TransformPlan.Expression,FacesDownInitially(Request.ComponentTypeFullyQualifiedName)),
                TransformPlan.Members,
                SceneComponentOrdering.GetSortKey(
                    TransformType.Symbol,SceneComponentBindings.TransformTypeFullyQualifiedName)));
        }

        InitialComponents.Add(new NewNodeComponent(
            Request.ComponentTypeFullyQualifiedName,DroppedPlan.Expression,DroppedPlan.Members,
            SceneComponentOrdering.GetSortKey(
                DroppedType.Symbol,Request.ComponentTypeFullyQualifiedName)));

        if(LightComponentContract.IsLight(Request.ComponentTypeFullyQualifiedName)){
            var Shadow=await Session.ResolveComponentTypeAsync(LightComponentContract.ShadowType,this.DisposalToken);
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            if(Shadow is not { }Value){SetStatusBarText(VsixText.Get("ComponentDropTypeResolveFailed"));return;}
            var ShadowPlan=SceneComponentInitializerPolicy.CreatePlan(Value.Symbol,LightComponentContract.ShadowType,Value.Compilation);
            InitialComponents.Add(new NewNodeComponent(LightComponentContract.ShadowType,ShadowPlan.Expression,ShadowPlan.Members,SceneComponentOrdering.GetSortKey(Value.Symbol,LightComponentContract.ShadowType)));
        }
        string Name=string.IsNullOrWhiteSpace(Request.NewEntityName)
            ?SimpleName(Request.ComponentTypeFullyQualifiedName)
            :Request.NewEntityName!.Trim();
        AddNodeOperation Operation=new(
            Snapshot.Key,RootId,Snapshot.DocumentVersion,Name,
            InitialComponents.ToImmutableArray());
        SceneEditCommitRequest CommitRequest=new(
            Snapshot.Key,Snapshot.DocumentVersion,VsixText.Get("UndoCreateEntityWithComponent"),
            Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,
            DirectOperations:[Operation]);
        SceneEditCommitResult Commit=await Session.CommitAsync(CommitRequest,this.DisposalToken);
        if(!Commit.Succeeded){
            SetStatusBarText(Commit.ErrorMessage??VsixText.Get("ComponentDropCreateFailed"));
        }
    }

    private static bool RequiresTransform(string FullyQualifiedName)=>
        !string.Equals(FullyQualifiedName,SceneComponentBindings.TransformTypeFullyQualifiedName,
            StringComparison.Ordinal)
        &&FullyQualifiedName.EndsWith("Light",StringComparison.Ordinal);

    private static bool FacesDownInitially(string FullyQualifiedName)=>
        !string.Equals(FullyQualifiedName,SceneComponentBindings.PointLightTypeFullyQualifiedName,
            StringComparison.Ordinal)
        &&FullyQualifiedName.EndsWith("Light",StringComparison.Ordinal);

    internal static string CreateInitialTransformExpression(string Expression,bool FacesDown){
        if(!FacesDown){
            return Expression;
        }

        // R/U/F オイラー角：R 周りの正の 4 分の 1 回転で、ローカル +F は -U の方向へ向く。
        string QuaternionEuler=Regex.Replace(Expression,
            @"(Rotation\s*=\s*new\s+[^\(]+\(\s*new\s+[^\(]+\(\s*)0(\s*,\s*0\s*,\s*0\s*\)\s*\))",
            "${1}1.5707964f${2}",RegexOptions.CultureInvariant);
        if(!string.Equals(QuaternionEuler,Expression,StringComparison.Ordinal)){
            return QuaternionEuler;
        }

        string Euler=Regex.Replace(Expression,
            @"(Rotation\s*=\s*new\s+[^\(]+\(\s*)0(\s*,\s*0\s*,\s*0\s*\))",
            "${1}1.5707964f${2}",RegexOptions.CultureInvariant);
        if(!string.Equals(Euler,Expression,StringComparison.Ordinal)){
            return Euler;
        }

        // 以前の平坦なクォータニオン型 Transform に基づいて作成されたシーンも読み込めるようにする。
        return Expression.Replace("RotationX=default","RotationX=0.70710678f")
            .Replace("RotationW=1f","RotationW=0.70710678f")
            .Replace("RotationW=FixedRotationScalar.One","RotationW=0.70710678f");
    }

    private static string SimpleName(string FullyQualifiedName){
        int GenericStart=FullyQualifiedName.IndexOf('<');
        string NonGenericName=GenericStart>=0
            ?FullyQualifiedName.Substring(0,GenericStart)
            :FullyQualifiedName;
        int LastDot=NonGenericName.LastIndexOf('.');
        return LastDot>=0?NonGenericName.Substring(LastDot+1):NonGenericName;
    }
}
