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
    /// Creates a root child in one scene operation for a component dropped on hierarchy whitespace.
    /// Lights receive their required Transform in the same operation, so no transient invalid entity
    /// is observable by the editor, source writer, or undo stack.
    /// </summary>
    private async Task TryCreateRootChildForComponentAsync(
        ComponentAddRequest Request,SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        if(Snapshot.Projection is not { } Projection
            ||!Projection.TryGetId(NodePath.Root,out EditorNodeId RootId)){
            SetStatusBarText("Root entity could not be resolved for the component drop.");
            return;
        }

        var Dropped=await Session.ResolveComponentTypeAsync(
            Request.ComponentTypeFullyQualifiedName,this.DisposalToken);
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        if(Dropped is not { } DroppedType){
            SetStatusBarText("The dropped component type could not be resolved.");
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
                SetStatusBarText("A Transform is required for this light, but its type could not be resolved.");
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

        string Name=string.IsNullOrWhiteSpace(Request.NewEntityName)
            ?SimpleName(Request.ComponentTypeFullyQualifiedName)
            :Request.NewEntityName!.Trim();
        AddNodeOperation Operation=new(
            Snapshot.Key,RootId,Snapshot.DocumentVersion,Name,
            InitialComponents.ToImmutableArray());
        SceneEditCommitRequest CommitRequest=new(
            Snapshot.Key,Snapshot.DocumentVersion,"Create entity with component",
            Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,
            DirectOperations:[Operation]);
        if(!Session.Apply(CommitRequest)){
            SetStatusBarText("The new entity and component could not be added.");
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

        // R/U/F Euler angles: a positive quarter turn around R maps local +F down to -U.
        string Euler=Regex.Replace(Expression,
            @"(Rotation\s*=\s*new\s+[^\(]+\(\s*)0(\s*,\s*0\s*,\s*0\s*\))",
            "${1}1.5707964f${2}",RegexOptions.CultureInvariant);
        if(!string.Equals(Euler,Expression,StringComparison.Ordinal)){
            return Euler;
        }

        // Keep generated scenes authored against the prior flat-quaternion Transform readable.
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
