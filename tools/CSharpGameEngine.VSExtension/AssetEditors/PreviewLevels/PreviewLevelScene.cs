using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
/// <summary>プレビュー対象の境界（ライト・床・カメラの大きさを決める基準）。</summary>
internal readonly struct PreviewBounds(Vector3 Min,Vector3 Max,Vector3 Center,float Radius) {
    internal Vector3 Min { get;}=Min;
    internal Vector3 Max { get;}=Max;
    internal Vector3 Center { get;}=Center;
    internal float Radius { get;}=Radius>0f?Radius:1f;
    internal static PreviewBounds From(SimpleMesh Mesh,AssetMeshStatistics Statistics)=>new(Statistics.BoundsMin,Statistics.BoundsMax,Mesh.BoundsCenter,Mesh.BoundsRadius);
    internal static bool TryFrom(RenderPreviewAssetBounds?Source,out PreviewBounds Bounds){
        Bounds=default;
        if(Source?.Min is not{ Length:3 }Min||Source.Max is not{ Length:3 }Max||Source.VertexCount<0||Source.TriangleCount<0){ return false; }
        for(var Index=0;Index<3;Index++){
            if(float.IsNaN(Min[Index])||float.IsInfinity(Min[Index])||float.IsNaN(Max[Index])||float.IsInfinity(Max[Index])||Min[Index]>Max[Index]){ return false; }
        }
        var Minimum=new Vector3(Min[0],Min[1],Min[2]);
        var Maximum=new Vector3(Max[0],Max[1],Max[2]);
        var Center=Minimum*0.5f+Maximum*0.5f;
        var Radius=(Maximum*0.5f-Minimum*0.5f).Length();
        if(float.IsNaN(Radius)||float.IsInfinity(Radius)){ return false; }
        Bounds=new PreviewBounds(Minimum,Maximum,Center,Radius);
        return true;
    }
}
/// <summary>
/// テンプレートをプレビューホストのワイヤ DTO（<see cref="RenderPreviewLighting"/>）へ変換する（M0127）。単位は型付きライトと同じ。
/// 点・スポットの位置と範囲は境界半径の倍数、光束は半径の 2 乗に比例させ、影のバイアスも半径に合わせる。
/// </summary>
internal static class PreviewLevelWire {
    private const float DegreesToRadians=FloatMath.Pi/180f;
    /// <summary>1 m の物体で調整した方向ライトの影バイアス（m）。</summary>
    private const float ReferenceShadowBiasMeters=0.0015f;
    /// <summary>光が進む向き（ワールド）。ピッチが負で下向き。<see cref="SceneViewCamera.GetForward"/> と同じ式。</summary>
    internal static Vector3 Forward(float YawDegrees,float PitchDegrees){
        var Yaw=YawDegrees*DegreesToRadians;
        var Pitch=PitchDegrees*DegreesToRadians;
        return Vector3.Normalize(new Vector3(FloatMath.Cos(Pitch)*FloatMath.Sin(Yaw),FloatMath.Sin(Pitch),FloatMath.Cos(Pitch)*FloatMath.Cos(Yaw)));
    }
    /// <summary>グリッドの間隔を自動で選ぶ（境界半径が 1〜10 マス程度になる 10 のべき乗）。</summary>
    internal static float AutoGridSizeMeters(float Radius){
        var Size=1f;
        var Safe=Radius>0f?Radius:1f;
        while(Size*10f<=Safe&&Size<1.0e6f){ Size*=10f; }
        while(Size>Safe&&Size>1.0e-6f){ Size/=10f; }
        return Size;
    }
    internal static RenderPreviewLighting CreateLighting(PreviewLevelTemplate Template,PreviewBounds Bounds,int Revision){
        var Radius=Bounds.Radius;
        var Bias=ReferenceShadowBiasMeters*Radius;
        var Ambient=Template.Ambient;
        var Lighting=new RenderPreviewLighting {
            EnvironmentRevision=Revision,
            Ambient=[Ambient.Color[0]*Ambient.Intensity,Ambient.Color[1]*Ambient.Intensity,Ambient.Color[2]*Ambient.Intensity],
            Background=Template.Background.Mode==PreviewBackgroundMode.Solid?(float[])Template.Background.Color.Clone():null,
        };
        if(!Template.Environment.IsLegacy){
            var Source=Template.Environment;
            Lighting.Environment=new RenderPreviewEnvironment {
                Atmosphere=Source.Atmosphere,SkyLightIntensity=Source.SkyLightIntensity,PhysicalExposure=Source.PhysicalExposure,ExposureEv100=Source.ExposureEv100,
                ExposureCompensation=Source.ExposureCompensation,FilmicToneMapper=Source.FilmicToneMapper,BloomIntensity=Source.BloomIntensity,AmbientOcclusion=Source.AmbientOcclusion,
            };
        }
        var Directionals=new System.Collections.Generic.List<RenderPreviewDirectionalLight>();
        var Points=new System.Collections.Generic.List<RenderPreviewPointLight>();
        var Spots=new System.Collections.Generic.List<RenderPreviewSpotLight>();
        foreach(var Light in Template.Lights){
            var Color=(float[])Light.Color.Clone();
            var Position=Light.Kind==PreviewLightKind.Directional?Vector3.Zero
                :new Vector3(Bounds.Center.X+Light.PositionRadii[0]*Radius,Bounds.Center.Y+Light.PositionRadii[1]*Radius,Bounds.Center.Z+Light.PositionRadii[2]*Radius);
            var Forward=PreviewLevelWire.Forward(Light.YawDegrees,Light.PitchDegrees);
            switch(Light.Kind){
                case PreviewLightKind.Directional:
                    Directionals.Add(new RenderPreviewDirectionalLight { Forward=[Forward.X,Forward.Y,Forward.Z],Color=Color,IlluminanceLux=Light.Intensity,CastsShadows=Light.CastsShadows,ShadowBiasMeters=Bias });
                    break;
                case PreviewLightKind.Point:
                    Points.Add(new RenderPreviewPointLight { Position=[Position.X,Position.Y,Position.Z],Color=Color,FluxLumens=Light.Intensity*Radius*Radius,RangeMeters=Light.RangeRadii*Radius,CastsShadows=Light.CastsShadows,ShadowBiasMeters=Bias });
                    break;
                case PreviewLightKind.Spot:
                    Spots.Add(new RenderPreviewSpotLight { Position=[Position.X,Position.Y,Position.Z],Forward=[Forward.X,Forward.Y,Forward.Z],Color=Color,FluxLumens=Light.Intensity*Radius*Radius,RangeMeters=Light.RangeRadii*Radius,InnerConeDegrees=Light.InnerConeDegrees,OuterConeDegrees=Light.OuterConeDegrees,CastsShadows=Light.CastsShadows,ShadowBiasMeters=Bias });
                    break;
            }
        }
        Lighting.Directionals=[..Directionals];
        Lighting.Points=[..Points];
        Lighting.Spots=[..Spots];
        return Lighting;
    }
}
/// <summary>プレビューシーン（対象 + 任意の床）と、それを再構築する必要があるかを判定する署名。</summary>
internal sealed class PreviewSceneComposition(RenderPreviewScene Scene,string FloorSignature,RenderPreviewObjectTransform[]Objects) {
    internal RenderPreviewScene Scene { get;}=Scene;
    /// <summary>床がなければ空。値が変わったときだけ <c>ReplaceScene</c> が必要（ライトや背景だけの切り替えでは変わらない）。</summary>
    internal string FloorSignature { get;}=FloorSignature;
    internal bool HasFloor=>this.FloorSignature.Length>0;
    /// <summary>毎フレーム送るオブジェクト変換（シーンのオブジェクトと同じ順序・同じ ID）。</summary>
    internal RenderPreviewObjectTransform[]Objects { get;}=Objects;
}
internal static class PreviewSceneComposer {
    internal const string AssetObjectId="AssetPreview";
    internal const string FloorObjectId="PreviewFloor";
    /// <summary>床を対象の底面から離す距離（境界半径の割合）。同一平面での深度の競合を避ける。</summary>
    private const float FloorGapRadii=0.002f;
    /// <param name="Skeletal">スケルタル glTF をホストが読み込む場合（ADR-0090）。静的なメッシュのオブジェクトは入れず、ホストが足すスキン付きのオブジェクトに任せる。<paramref name="Mesh"/> は境界の計算だけに使う。</param>
    internal static PreviewSceneComposition Compose(SimpleMesh?Mesh,PreviewBounds Bounds,PreviewLevelTemplate Template,int Width,int Height,RenderPreviewAssetSource?Skeletal=null,string?StaticAssetPath=null){
        var HostAsset=Skeletal is not null||!string.IsNullOrEmpty(StaticAssetPath);
        var Scene=HostAsset?new RenderPreviewScene { Camera=new RenderPreviewCamera(),Asset=Skeletal,StaticAssetPath=StaticAssetPath??string.Empty }
            :RenderPreviewSceneBuilder.FromMesh(Mesh??throw new ArgumentNullException(nameof(Mesh)),Width,Height);
        RenderPreviewObjectTransform[]Objects=HostAsset?[]:[new(){ ObjectId=AssetObjectId,World=RenderPreviewMatrix4x4.Identity,SelectionWeight=0f }];
        var Floor=Template.Floor;
        if(!Floor.Visible){ return new PreviewSceneComposition(Scene,string.Empty,Objects); }
        var Half=Floor.SizeRadii*Bounds.Radius;
        var FloorY=Bounds.Min.Y-FloorGapRadii*Bounds.Radius;
        var MeshIndex=Scene.Meshes.Length;
        var MaterialIndex=Scene.Materials.Length;
        var GridSize=Floor.GridSizeMeters>0f?Floor.GridSizeMeters:PreviewLevelWire.AutoGridSizeMeters(Bounds.Radius);
        Scene.Meshes=[..Scene.Meshes,FloorMesh(Bounds.Center.X,Bounds.Center.Z,Half)];
        Scene.Materials=[..Scene.Materials,new RenderPreviewMaterial {
            BaseColor=[Floor.Color[0],Floor.Color[1],Floor.Color[2],1f],Roughness=Floor.Roughness,Metallic=0f,
            WorldGrid=Floor.Grid?new RenderPreviewWorldGrid { GridSizeMeters=GridSize,SubGridDivisions=10,WireHalfWidth=0.02f,GridColor=(float[])Floor.GridColor.Clone(),SubGridColor=(float[])Floor.SubGridColor.Clone() }:null,
        }];
        var World=RenderPreviewSceneBuilder.Matrix(Matrix4x4.CreateTranslation(0f,FloorY,0f));
        Scene.Objects=[..Scene.Objects,new RenderPreviewObject { ObjectId=FloorObjectId,MeshIndex=MeshIndex,MaterialIndex=MaterialIndex,World=World }];
        Objects=[..Objects,new RenderPreviewObjectTransform { ObjectId=FloorObjectId,World=RenderPreviewSceneBuilder.Matrix(Matrix4x4.CreateTranslation(0f,FloorY,0f)),SelectionWeight=0f }];
        return new PreviewSceneComposition(Scene,Signature(Floor,GridSize,Half,FloorY,Bounds.Center),Objects);
    }
    /// <summary>上向きの 1 枚の四角形。エンジンの平面メッシュ（<c>ProceduralMeshFactory.CreatePlane</c>）と同じ頂点順・インデックスで、上から見たとき表になる。</summary>
    internal static RenderPreviewMesh FloorMesh(float CenterX,float CenterZ,float Half)=>new(){
        Positions=[CenterX-Half,0f,CenterZ-Half,CenterX+Half,0f,CenterZ-Half,CenterX+Half,0f,CenterZ+Half,CenterX-Half,0f,CenterZ+Half],
        Normals=[0f,1f,0f,0f,1f,0f,0f,1f,0f,0f,1f,0f],
        Indices=[0u,1u,2u,0u,2u,3u],
    };
    private static string Signature(PreviewFloor Floor,float GridSize,float Half,float FloorY,Vector3 Center){
        var Builder=new StringBuilder();
        void Add(float Value)=>Builder.Append(Value.ToString("R",CultureInfo.InvariantCulture)).Append('|');
        Add(Half);Add(FloorY);Add(Center.X);Add(Center.Z);Add(Floor.Roughness);Add(GridSize);
        foreach(var Value in Floor.Color){ Add(Value); }
        Builder.Append(Floor.Grid?'G':'-');
        if(Floor.Grid){
            foreach(var Value in Floor.GridColor){ Add(Value); }
            foreach(var Value in Floor.SubGridColor){ Add(Value); }
        }
        return Builder.ToString();
    }
}
