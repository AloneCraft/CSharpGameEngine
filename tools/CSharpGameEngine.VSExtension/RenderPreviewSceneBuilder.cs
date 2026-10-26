using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

internal static class RenderPreviewSceneBuilder {
    internal static RenderPreviewScene FromMesh(SimpleMesh Mesh,int Width,int Height){
        if(Mesh is null){ throw new ArgumentNullException(nameof(Mesh)); }
        Vector3 Center=Mesh.BoundsCenter;
        float Radius=Mesh.BoundsRadius>0f?Mesh.BoundsRadius:1f;
        Vector3 Eye=Center+Vector3.Normalize(new Vector3(1f,0.75f,1f))*(Radius*3f);
        Matrix4x4 View=Matrix4x4.CreateLookAt(Eye,Center,Vector3.UnitY);
        Matrix4x4 Projection=Matrix4x4.CreatePerspectiveFieldOfView(
            FloatMath.Pi/4f,Math.Max(1,Width)/(float)Math.Max(1,Height),0.05f,Radius*20f);
        return new RenderPreviewScene {
            Camera=Camera(View,Projection,Eye,NearZ:0.05f,FarZ:Radius*20f),
            Meshes=[new RenderPreviewMesh {
                Positions=Flatten(Mesh.Positions),
                Normals=Flatten(Mesh.Normals),
                Indices=[..Mesh.Indices],
            }],
            Materials=[new RenderPreviewMaterial {
                BaseColor=[
                    Mesh.BaseColorFactor.X,Mesh.BaseColorFactor.Y,
                    Mesh.BaseColorFactor.Z,Mesh.BaseColorFactor.W,
                ],
            }],
            Objects=[new RenderPreviewObject {
                ObjectId="AssetPreview",
                MeshIndex=0,
                MaterialIndex=0,
                World=RenderPreviewMatrix4x4.Identity,
            }],
        };
    }

    internal static RenderPreviewScene FromScene(
        IReadOnlyList<SceneRenderObject>Objects,
        in Matrix4x4 View,in Matrix4x4 Projection,in Vector3 CameraPosition,
        float FovYRadians=SceneCameraFraming.DefaultFovYRadians){
        if(Objects is null){ throw new ArgumentNullException(nameof(Objects)); }
        Dictionary<SimpleMesh,int>MeshIndices=[];
        List<RenderPreviewMesh>Meshes=[];
        List<RenderPreviewMaterial>Materials=[];
        RenderPreviewObject[]WireObjects=new RenderPreviewObject[Objects.Count];
        for(int ObjectIndex=0;ObjectIndex<Objects.Count;ObjectIndex++){
            SceneRenderObject Object=Objects[ObjectIndex]
                ??throw new ArgumentException(
                    $"Scene render object {ObjectIndex} is null.",nameof(Objects));
            if(!MeshIndices.TryGetValue(Object.Mesh,out int MeshIndex)){
                MeshIndex=Meshes.Count;
                MeshIndices.Add(Object.Mesh,MeshIndex);
                Meshes.Add(new RenderPreviewMesh {
                    Positions=Flatten(Object.Mesh.Positions),
                    Normals=Flatten(Object.Mesh.Normals),
                    Indices=[..Object.Mesh.Indices],
                });
                Vector4 BaseColor=Object.Mesh.BaseColorFactor;
                Materials.Add(new RenderPreviewMaterial {
                    BaseColor=[
                        BaseColor.X,BaseColor.Y,BaseColor.Z,BaseColor.W,
                    ],
                });
            }
            WireObjects[ObjectIndex]=new RenderPreviewObject {
                ObjectId=Object.NodeId.ToDisplayString(),
                MeshIndex=MeshIndex,
                MaterialIndex=MeshIndex,
                World=Matrix(Object.World),
            };
        }
        return new RenderPreviewScene {
            Camera=Camera(View,Projection,CameraPosition,FovYRadians),
            Meshes=[..Meshes],
            Materials=[..Materials],
            Objects=WireObjects,
        };
    }

    internal static RenderPreviewFrame CreateFrame(
        IReadOnlyList<SceneRenderObject>Objects,
        in Matrix4x4 View,in Matrix4x4 Projection,in Vector3 CameraPosition,
        SceneViewRenderMode RenderMode,IReadOnlyCollection<NodePath>SelectedPaths,
        IReadOnlyList<GizmoOverlayPrimitive>Overlay,
        IReadOnlyList<RenderPreviewCameraPass>CameraPreviews,
        float FovYRadians=SceneCameraFraming.DefaultFovYRadians,
        IReadOnlyList<RenderPreviewTransformOverride>?TransformOverrides=null){
        if(Objects is null){ throw new ArgumentNullException(nameof(Objects)); }
        if(SelectedPaths is null){ throw new ArgumentNullException(nameof(SelectedPaths)); }
        if(Overlay is null){ throw new ArgumentNullException(nameof(Overlay)); }
        if(CameraPreviews is null){ throw new ArgumentNullException(nameof(CameraPreviews)); }
        HashSet<NodePath>Selected=new(SelectedPaths);
        RenderPreviewObjectTransform[]WireObjects=
            new RenderPreviewObjectTransform[Objects.Count];
        for(int ObjectIndex=0;ObjectIndex<Objects.Count;ObjectIndex++){
            SceneRenderObject Object=Objects[ObjectIndex]
                ??throw new ArgumentException(
                    $"Scene render object {ObjectIndex} is null.",nameof(Objects));
            WireObjects[ObjectIndex]=new RenderPreviewObjectTransform {
                ObjectId=Object.NodeId.ToDisplayString(),
                World=Matrix(Object.World),
                SelectionWeight=Selected.Contains(Object.NodeId)?1f:0f,
            };
        }
        List<RenderPreviewOverlayVertex>Triangles=[];
        List<RenderPreviewOverlayVertex>Lines=[];
        foreach(GizmoOverlayPrimitive Primitive in Overlay){
            List<RenderPreviewOverlayVertex>Destination=
                Primitive.Kind==GizmoPrimitiveKind.Triangle?Triangles:Lines;
            foreach(GizmoOverlayVertex Vertex in Primitive.Vertices){
                Destination.Add(OverlayVertex(Vertex));
            }
        }
        return new RenderPreviewFrame {
            Camera=Camera(View,Projection,CameraPosition,FovYRadians),
            PresentationMode=RenderMode switch {
                SceneViewRenderMode.Lit=>RenderPreviewPresentationMode.Lit,
                SceneViewRenderMode.BaseColor=>RenderPreviewPresentationMode.BaseColor,
                SceneViewRenderMode.Normal=>RenderPreviewPresentationMode.Normal,
                SceneViewRenderMode.Wireframe=>RenderPreviewPresentationMode.Wireframe,
                _=>throw new ArgumentOutOfRangeException(nameof(RenderMode)),
            },
            Objects=WireObjects,
            OverlayTriangles=[..Triangles],
            OverlayLines=[..Lines],
            CameraPreviews=[..CameraPreviews],
            TransformOverrides=TransformOverrides is null?[]:[..TransformOverrides],
        };
    }

    /// <summary>
    /// M0096：runtime view はゲーム自身の world を描画するため、gizmo のドラッグ中はデザイナーの world matrix ではなく、各ドラッグ対象 root の親相対 transform（commit 時に書き込まれる値）を送信します。デザイナーの X、Y、Z は R、U、F に対応し、quaternion はエンジンの EulerRuf 規約に従います。
    /// </summary>
    internal static RenderPreviewTransformOverride[]DragOverrides(
        IReadOnlyList<SceneTransformTarget>Targets,IReadOnlyList<SceneTransformPreview>Previews){
        List<RenderPreviewTransformOverride>Overrides=[];
        for(var Index=0;Index<Targets.Count&&Index<Previews.Count;Index++){
            if(!SceneTransformMath.TryCalculateLocal(Previews[Index].World,Targets[Index].ParentWorld,out var Local)){ continue; }
            Overrides.Add(new RenderPreviewTransformOverride {
                ObjectId=Targets[Index].NodePath.ToDisplayString(),
                Position=[Local.Position.X,Local.Position.Y,Local.Position.Z],
                Rotation=[Local.Rotation.X,Local.Rotation.Y,Local.Rotation.Z,Local.Rotation.W],
                Scale=[Local.Scale.X,Local.Scale.Y,Local.Scale.Z],
            });
        }
        return [..Overrides];
    }

    internal static string ResolveHostPath(string RepoRoot)=>ResolveHostPath(
        RepoRoot,GetExtensionDirectory(),
        Environment.GetEnvironmentVariable("CGE_EDITOR_PREVIEW_HOST"));

    internal static string ResolveHostPath(
        string RepoRoot,string ExtensionDirectory,string?OverridePath)=>ResolveExisting(
            OverridePath,
            Path.Combine(ExtensionDirectory,"PreviewHost",
                "CSharpGameEngine.EditorPreview.Host.exe"),
            Path.Combine(ExtensionDirectory,"PreviewHost",
                "CSharpGameEngine.EditorPreview.Host.dll"),
            Path.Combine(RepoRoot,"tools","CSharpGameEngine.EditorPreview.Host","bin","Debug",
                "net10.0-windows10.0.19041.0","CSharpGameEngine.EditorPreview.Host.exe"),
            Path.Combine(RepoRoot,"tools","CSharpGameEngine.EditorPreview.Host","bin","Debug",
                "net10.0-windows10.0.19041.0","CSharpGameEngine.EditorPreview.Host.dll"),
            Path.Combine(RepoRoot,"tools","CSharpGameEngine.EditorPreview.Host","bin","Release",
                "net10.0-windows10.0.19041.0","CSharpGameEngine.EditorPreview.Host.exe"),
            Path.Combine(RepoRoot,"tools","CSharpGameEngine.EditorPreview.Host","bin","Release",
                "net10.0-windows10.0.19041.0","CSharpGameEngine.EditorPreview.Host.dll"));

    internal static string ResolveShaderArtifactPath(string RepoRoot)=>ResolveShaderArtifactPath(
        RepoRoot,GetExtensionDirectory(),
        Environment.GetEnvironmentVariable("CGE_EDITOR_PREVIEW_SHADER"));

    internal static string ResolveShaderArtifactPath(
        string RepoRoot,string ExtensionDirectory,string?OverridePath)=>ResolveExisting(
            OverridePath,
            Path.Combine(ExtensionDirectory,"PreviewHost","Shaders",
                "Stage.cgeshaderartifact"),
            Path.Combine(RepoRoot,"UserEcsProject","bin","Debug",
                "net10.0-windows10.0.19041.0","win-x64","Shaders","Stage.cgeshaderartifact"),
            Path.Combine(RepoRoot,"UserEcsProject","bin","Release",
                "net10.0-windows10.0.19041.0","win-x64","Shaders","Stage.cgeshaderartifact"));

    private static string GetExtensionDirectory()=>
        Path.GetDirectoryName(typeof(RenderPreviewSceneBuilder).Assembly.Location)
        ??AppDomain.CurrentDomain.BaseDirectory;

    private static string ResolveExisting(params string?[]Candidates){
        foreach(string?Candidate in Candidates){
            if(!string.IsNullOrWhiteSpace(Candidate)&&File.Exists(Candidate)){
                return Path.GetFullPath(Candidate);
            }
        }
        throw new FileNotFoundException("Required isolated preview artifact was not found.");
    }

    private static float[]Flatten(Vector3[]Values){
        float[]Result=new float[Values.Length*3];
        for(int Index=0;Index<Values.Length;Index++){
            Result[Index*3]=Values[Index].X;
            Result[Index*3+1]=Values[Index].Y;
            Result[Index*3+2]=Values[Index].Z;
        }
        return Result;
    }

    internal static RenderPreviewMatrix4x4 Matrix(in Matrix4x4 Value)=>new(){
        Values=[
            Value.M11,Value.M12,Value.M13,Value.M14,
            Value.M21,Value.M22,Value.M23,Value.M24,
            Value.M31,Value.M32,Value.M33,Value.M34,
            Value.M41,Value.M42,Value.M43,Value.M44,
        ],
    };

    /// <summary>アセットエディターのビューポートが毎フレーム送るカメラ。</summary>
    internal static RenderPreviewCamera CreateCamera(
        in Matrix4x4 View,in Matrix4x4 Projection,in Vector3 Position,float FovYRadians=SceneCameraFraming.DefaultFovYRadians)=>
        Camera(View,Projection,Position,FovYRadians);
    private static RenderPreviewCamera Camera(
        in Matrix4x4 View,in Matrix4x4 Projection,in Vector3 Position,float FovYRadians=SceneCameraFraming.DefaultFovYRadians,
        float?NearZ=null,float?FarZ=null){
        RenderPreviewCamera Camera=new(){
            ViewProjection=Matrix(View*Projection),
            Position=[Position.X,Position.Y,Position.Z],
        };
        DescribePose(Camera,View,Projection,FovYRadians,NearZ:NearZ,FarZ:FarZ);
        return Camera;
    }

    /// <summary>
    /// ADR-0070 B1：runtime view に必要な pose と projection。Scene view の matrix から読み戻します。ビューは右手系の <see cref="Matrix4x4.CreateLookAt"/> なので、列がカメラの右、上、後方の軸を表します。runtime camera は RUF 回転の行から前方（ローカル +F）と上方（+U）を取得します。
    /// </summary>
    internal static void DescribePose(RenderPreviewCamera Camera,in Matrix4x4 View,in Matrix4x4 Projection,float FovYRadians,
        float RadialTransitionMeters=1f,float?NearZ=null,float?FarZ=null){
        Vector3 Up=Vector3.Normalize(new Vector3(View.M12,View.M22,View.M32));
        Vector3 Forward=-Vector3.Normalize(new Vector3(View.M13,View.M23,View.M33));
        Vector3 Right=Vector3.Cross(Up,Forward);
        Quaternion Rotation=Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            Right.X,Right.Y,Right.Z,0f,
            Up.X,Up.Y,Up.Z,0f,
            Forward.X,Forward.Y,Forward.Z,0f,
            0f,0f,0f,1f)));
        Camera.Rotation=[Rotation.X,Rotation.Y,Rotation.Z,Rotation.W];
        Camera.FovYRadians=FovYRadians;
        // Perspective（M34=-1）：M33=f/(n-f)、M43=nf/(n-f)。Orthographic（M34=0）：M33=1/(n-f)、M43=n/(n-f)。
        // 指定された clip plane を保持します。巨大な perspective far plane は丸められた matrix から復元できません。
        Camera.NearZ=NearZ??Projection.M43/Projection.M33;
        Camera.FarZ=FarZ??(Projection.M34==0f?(Projection.M43-1f)/Projection.M33:Projection.M43/(Projection.M33+1f));
        Camera.RadialTransitionMeters=RadialTransitionMeters;
    }

    private static RenderPreviewOverlayVertex OverlayVertex(in GizmoOverlayVertex Vertex)=>new(){
        Position=[Vertex.Position.X,Vertex.Position.Y,Vertex.Position.Z],
        Color=[Vertex.Color.X,Vertex.Color.Y,Vertex.Color.Z,Vertex.Color.W],
    };
}
