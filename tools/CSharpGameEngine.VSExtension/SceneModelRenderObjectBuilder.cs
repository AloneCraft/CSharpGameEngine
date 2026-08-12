using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Converts a live-Roslyn <see cref="SceneModel"/> into the <see cref="SceneRenderObject"/> list
/// <see cref="SceneRenderer"/> needs (ADR-0040 Phase 9) - the SceneModel-driven counterpart to what
/// <see cref="SceneHwndHost"/> used to build from a <c>.scene</c> JSON file.
///
/// <para>
/// Recognizes exactly two component shapes by fully-qualified name: <c>UserEcsProject.Transform</c>
/// (project-specific, not an engine-standard type - hardcoded on purpose, generalizing this to any
/// game project's own Transform type is future work) and the engine-standard
/// <c>CSharpGameEngine.Runtime.Windows.PrimitiveMesh</c> (ADR-0040 Phase 6). A node with neither
/// falls back to <see cref="GizmoMeshFactory"/>'s placeholder cube, exactly like the old
/// <c>.scene</c> pipeline did for mesh-less nodes.
/// </para>
///
/// <para>
/// No hierarchical transform composition: each node's Transform values are used as world space
/// directly, matching the runtime's own current behavior (<c>UserEcsProject</c> has no parent/child
/// transform composition either - see <c>RuntimeSystem.cs</c>).
/// </para>
/// </summary>
internal static class SceneModelRenderObjectBuilder {
    private const string TransformTypeFullyQualifiedName="global::UserEcsProject.Transform";
    private const string PrimitiveMeshTypeFullyQualifiedName="global::CSharpGameEngine.Runtime.Windows.PrimitiveMesh";
    private const string PrimitiveBridgeProjectRelativePath=@"tools\CSharpGameEngine.Primitive.Cli";
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);

    /// <summary>
    /// <paramref name="ResolvePrimitiveMesh"/> is injected (rather than always calling the real
    /// out-of-process bridge here) so this method's node-walking/value-extraction logic can be unit
    /// tested without spawning a process - production passes <see cref="ResolveViaBridge"/>.
    /// </summary>
    internal static IReadOnlyList<SceneRenderObject>Build(SceneModel Model,Func<string,SimpleMesh?>ResolvePrimitiveMesh){
        List<SceneRenderObject>Result=[];

        if(Model.Root is null){
            return Result;
        }

        foreach(SceneNode Node in Model.EnumerateNodes()){
            Matrix4x4 World=ExtractWorldTransform(Node);
            SimpleMesh Mesh=ExtractPrimitiveMeshEnumMemberName(Node)is { } EnumMemberName
                ?ResolvePrimitiveMesh(EnumMemberName)??GizmoMeshFactory.Create()
                :GizmoMeshFactory.Create();

            Result.Add(new SceneRenderObject { Mesh=Mesh,World=World,NodeId=Node.Path });
        }

        return Result;
    }

    /// <summary>
    /// Production mesh resolver: maps a <c>PrimitiveType</c> enum member name to the string
    /// <c>tools/CSharpGameEngine.Primitive.Cli</c> expects (they differ for <c>Plane</c> -&gt;
    /// <c>"Rectangle"</c>, matching the Shape Palette's existing naming), generates/reuses the same
    /// cached <c>.glb</c> under <c>UserGameProject/Assets/Primitives/</c> that
    /// <see cref="PrimitiveSpawner"/> already uses, then loads it via <see cref="MeshLoader"/> - the
    /// exact same out-of-process bridge + cache this VSIX already uses for mesh assets, so no new
    /// I/O pattern is introduced (ADR-0019).
    /// </summary>
    internal static SimpleMesh?ResolveViaBridge(string PrimitiveTypeEnumMemberName,string RepoRoot){
        var BridgeKind=MapToBridgeKind(PrimitiveTypeEnumMemberName);
        if(BridgeKind is null){
            return null;
        }

        var PrimitivesDirectory=Path.Combine(RepoRoot,"UserGameProject","Assets","Primitives");
        Directory.CreateDirectory(PrimitivesDirectory);
        var GlbPath=Path.Combine(PrimitivesDirectory, $"{BridgeKind}.glb");

        if(!File.Exists(GlbPath)){
            ExternalBridgeProcess.RunProducingFile(PrimitiveBridgeProjectRelativePath,BridgeKind,GlbPath,RepoRoot,BridgeTimeout,UseResidentServer:true);
        }

        return MeshLoader.Load(GlbPath,RepoRoot);
    }

    private static string?MapToBridgeKind(string PrimitiveTypeEnumMemberName)=>PrimitiveTypeEnumMemberName switch {
        "Plane"=>"Rectangle",
        "Box"=>"Box",
        "Sphere"=>"Sphere",
        "Cylinder"=>"Cylinder",
        "Cone"=>"Cone",
        "Torus"=>"Torus",
        "Disk"=>"Disk",
        "Capsule"=>"Capsule",
        _=>null,
    };

    private static Matrix4x4 ExtractWorldTransform(SceneNode Node){
        SceneComponent?Transform=Node.FindComponent(TransformTypeFullyQualifiedName);
        if(Transform is null){
            return Matrix4x4.Identity;
        }

        Vector3 Position=new(
            ReadFloat(Transform,"PositionX"),
            ReadFloat(Transform,"PositionY"),
            ReadFloat(Transform,"PositionZ"));

        Quaternion Rotation=new(
            ReadFloat(Transform,"RotationX"),
            ReadFloat(Transform,"RotationY"),
            ReadFloat(Transform,"RotationZ"),
            ReadFloat(Transform,"RotationW"));

        // An unset Transform.Rotation* leaves every field at its struct default (0), which is a
        // degenerate (zero-length) quaternion - not a valid rotation. Fall back to Identity rather
        // than feeding CreateFromQuaternion a NaN-producing input.
        if(Rotation==default){
            Rotation=Quaternion.Identity;
        }

        Vector3 Scale=new(
            ReadFloat(Transform,"ScaleX",Default:1f),
            ReadFloat(Transform,"ScaleY",Default:1f),
            ReadFloat(Transform,"ScaleZ",Default:1f));

        return Matrix4x4.CreateScale(Scale)
            *Matrix4x4.CreateFromQuaternion(Rotation)
            *Matrix4x4.CreateTranslation(Position);
    }

    private static float ReadFloat(SceneComponent Component,string MemberName,float Default=0f){
        object?Value=Component.FindMember(MemberName)?.Value.ConstantValue;
        return Value switch {
            float F=>F,
            double D=>(float)D,
            int I=>I,
            _=>Default,
        };
    }

    private static string?ExtractPrimitiveMeshEnumMemberName(SceneNode Node){
        SceneComponent?PrimitiveMesh=Node.FindComponent(PrimitiveMeshTypeFullyQualifiedName);
        return PrimitiveMesh?.FindMember("Type")?.Value.ReferencedSymbol?.Name;
    }
}
