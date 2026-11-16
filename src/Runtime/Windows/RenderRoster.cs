using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// ADR-0078/M0105：描画オブジェクトをEntityに追従させる。<see cref="LevelLoad"/>では引き続き、レベル読み込み時に存在するEntityからレンダラーを構築する。
/// その後はこれを使って、レンダラーのオブジェクトをワールド内のEntityと同期させる。<see cref="StaticMesh"/>または<see cref="PrimitiveMesh"/>を取得したEntity
///（新規生成、またはメッシュを持つ形状へ変更した場合）には描画オブジェクトを追加する。破棄された場合、またはメッシュを失った場合は描画を止める
///（Renderableラッパーが<see cref="Release"/>を呼び出す）。まだEntityに使われていないメッシュアセットは、最初にバインドされた際に読み込んでGPUへ送るため1フレームかかる。
/// 後から生成された<see cref="CSharpGameEngine.Runtime.Animation.SkeletalMesh"/>（M0107：再出現したキャラクター）には、アセットのスキニング済みメッシュと
/// マテリアルの独自コピーを作成し、レンダラーのスキニングパスへ追加する。最初の1回はGPU待ちが発生する。Entity破棄後はコピーをプールへ戻し、
/// 同じアセットを使う次のEntityで再利用するため、何度再出現させてもレンダラーが増え続けない。
/// 追従対象外の変更は、既に描画中のEntityに対する<see cref="StaticMesh.AssetPath"/>の変更。
/// </summary>
internal sealed class RenderRoster {
    private readonly Dictionary<string,StageSceneDataBuilder.AssetTemplate>StaticAssets;
    private readonly Dictionary<PrimitiveType,int>PrimitiveMeshes;
    private int PrimitiveMaterial;
    private readonly HashSet<Entity>Bound=[];
    private readonly Dictionary<int,StageMeshCreateInfo>EmissiveMeshes=[];
    private readonly List<(Vector3 Nits,float LuminanceScale)>MaterialEmission=[];
    private readonly HashSet<string>FailedAssets=new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string>DiagnosticList=[];
    private readonly Dictionary<Entity,StageSceneDataBuilder.SkeletalInstance>SkeletalOwners=[];
    private readonly Dictionary<string,List<StageSceneDataBuilder.SkeletalInstance>>FreeSkeletal=new(StringComparer.OrdinalIgnoreCase);
    private bool ReportedHeadroom;
    private RenderRoster(StageSceneDataBuilder Scene){
        this.StaticAssets=new(Scene.StaticAssets,StringComparer.OrdinalIgnoreCase);
        this.PrimitiveMeshes=new(Scene.PrimitiveMeshIndices);
        this.PrimitiveMaterial=Scene.PrimitiveMaterialIndex;
        foreach(var Material in Scene.Materials){
            this.MaterialEmission.Add(EmissionOf(Material));
        }
        for(var Index=0;Index<Scene.Objects.Length;Index++){
            this.NoteEmissiveMesh(Scene.Objects[Index].MeshIndex,Scene.Objects[Index].MaterialIndex,Scene.Meshes[Scene.Objects[Index].MeshIndex]);
        }
        foreach(var Source in Scene.SourceEntities){
            this.Bound.Add(Source);
        }
        foreach(var Instance in Scene.SkeletalInstances){
            this.SkeletalOwners[Instance.Entity]=Instance;
        }
    }
    /// <summary>読み込み直後のレベルの登録一覧。<paramref name="Scene"/>がオブジェクト化したEntityはすべてバインド済みとして扱う。</summary>
    internal static RenderRoster FromScene(StageSceneDataBuilder Scene)=>new(Scene);
    /// <summary>Entityのバインド時に発生した問題（アセットの読み込み失敗、空き領域不足など）。ログとテスト用に1行ずつ記録する。</summary>
    internal IReadOnlyList<string>Diagnostics=>this.DiagnosticList;
    /// <summary>レンダラーの余裕がなく、追加できなかったオブジェクト数。</summary>
    internal int DroppedObjects { get;private set;}
    internal bool IsBound(Entity Source)=>this.Bound.Contains(Source);
    internal static (Vector3 Nits,float LuminanceScale)EmissionOf(in StageMaterialCreateInfo Material)=>(
        Material.EmissiveNits!=Vector3.Zero?Material.EmissiveNits:Material.EmissiveFactor*100f,
        Material.EmissiveTextureLuminanceScale);
    internal static bool IsEmissive(Vector3 Nits)=>Nits.X>0f||Nits.Y>0f||Nits.Z>0f;
    /// <summary>
    /// 発光メッシュが周囲も照らすかどうか（ライトパッチ）。<see cref="MeshEmissive"/>を持つメッシュ（ADR-0078/M0109。ブレーキライトのようにゲーム実行中に
    /// 発光の強さを変更するもの）は表面だけが光る。その発光はフレームごとに変化し、大きなメッシュの一部に過ぎないためである。
    /// </summary>
    internal static bool LightsTheScene(Entity Source,CSharpGameEngine.Entities.World World)=>!World.TryGetComponent(Source,out MeshEmissive _);
    /// <summary>Entityがまだ描画対象を持つかどうか。生存しており、メッシュコンポーネントがある。</summary>
    internal static bool IsDrawnSource(Entity Source,CSharpGameEngine.Entities.World World)=>
        World.IsAlive(Source)
        &&(World.TryGetComponent(Source,out StaticMesh _)
            ||World.TryGetComponent(Source,out PrimitiveMesh _)
            ||World.TryGetComponent(Source,out CSharpGameEngine.Runtime.Animation.SkeletalMesh _)
            ||World.TryGetComponent(Source,out FragmentMesh _));
    private readonly Dictionary<(string Path,int Fragment),StageSceneDataBuilder.AssetObject[]>FragmentObjects=new();
    private readonly Dictionary<(Vector3 Outer,Vector3 Interior),(int Outer,int Interior)>FragmentMaterials=new();
    /// <summary>
    /// ADR-0078/M0113：<see cref="FragmentMesh"/>を持つEntityにオブジェクトを割り当てる。破片の外側と切断面の三角形を2つのメッシュとして扱う
    ///（アセット内の各破片につき一度作成）。色の組ごとに単色マテリアルを2つ用意する。アセットを読み込めない場合は一度だけ報告し、描画しない。
    /// </summary>
    internal void BindFragmentMesh(Entity Source,in Transform Transform,in FragmentMesh Mesh,EngineHost Host,ref State State){
        if(this.Bound.Contains(Source)||Host.Renderer is not{ }Renderer){ return; }
        if(string.IsNullOrEmpty(Mesh.AssetPath)){ return; }
        var FullPath=ContentRoot.Resolve(Mesh.AssetPath);
        if(this.FailedAssets.Contains(FullPath)){ return; }
        if(!this.FragmentObjects.TryGetValue((FullPath,Mesh.Fragment),out var Objects)){
            if(!PrefractureAssetCache.TryGet(FullPath,out var Asset,out var Error)){
                this.Fail(FullPath,Error);
                return;
            }
            if((uint)Mesh.Fragment>=(uint)Asset.Fragments.Count){
                var Key=$"{FullPath}#{Mesh.Fragment}";
                if(!this.FailedAssets.Contains(Key)){ this.Fail(Key,$"The prefracture asset '{FullPath}' has no fragment {Mesh.Fragment}."); }
                return;
            }
            if(!this.FragmentMaterials.TryGetValue((Mesh.OuterColor,Mesh.InteriorColor),out var Materials)){
                if(Renderer.FreeMaterialSlots<2){
                    this.Fail(FullPath,"The renderer has no room for the materials of a fragment.");
                    return;
                }
                StageMaterialCreateInfo Outer=new(){ BaseColorFactor=new Vector4(Mesh.OuterColor,1f),RoughnessFactor=0.8f,MetallicFactor=0f };
                StageMaterialCreateInfo Interior=new(){ BaseColorFactor=new Vector4(Mesh.InteriorColor,1f),RoughnessFactor=0.95f,MetallicFactor=0f };
                Materials=(Renderer.AddMaterial(Outer),Renderer.AddMaterial(Interior));
                this.MaterialEmission.Add(EmissionOf(Outer));
                this.MaterialEmission.Add(EmissionOf(Interior));
                this.FragmentMaterials.Add((Mesh.OuterColor,Mesh.InteriorColor),Materials);
            }
            List<StageSceneDataBuilder.AssetObject>Made=[];
            foreach(var Slot in (ReadOnlySpan<byte>)[FractureMesh.OuterSlot,FractureMesh.InteriorSlot]){
                if(FragmentMeshBuilder.Build(Asset,Mesh.Fragment,Slot) is not { }Part){ continue; }
                var MeshIndex=Renderer.AddMesh(Part);
                Made.Add(new StageSceneDataBuilder.AssetObject(MeshIndex,Slot==FractureMesh.OuterSlot?Materials.Outer:Materials.Interior,Matrix4x4.Identity));
            }
            Objects=[.. Made];
            this.FragmentObjects.Add((FullPath,Mesh.Fragment),Objects);
        }
        this.BindObjects(Source,in Transform,Objects,Renderer,Host,ref State);
    }
    private void NoteEmissiveMesh(int MeshIndex,int MaterialIndex,StageMeshCreateInfo Mesh){
        if(IsEmissive(this.MaterialEmission[MaterialIndex].Nits)){
            this.EmissiveMeshes.TryAdd(MeshIndex,Mesh);
        }
    }
    /// <summary>毎フレーム、発光オブジェクトのライトパッチを送信するラッパー。読み込み時と後からオブジェクトをバインドするたびに作成する。</summary>
    internal static EmissiveMeshLighting NewEmissiveLighting(Entity Source,StageMeshCreateInfo Mesh,(Vector3 Nits,float LuminanceScale)Emission,
        Matrix4x4 MeshLocalTransform,Matrix4x4 CameraRelativeWorld,FixedTransformResolveStatus ResolveStatus)=>new(){
        Mesh=Mesh,
        EmissiveNits=Emission.Nits,
        EmissiveTextureLuminanceScale=Emission.LuminanceScale,
        SourceEntity=Source,
        HasSourceEntity=true,
        MeshLocalTransform=MeshLocalTransform,
        LastCameraRelativeWorld=CameraRelativeWorld,
        RangeMeters=20f,
        CastsShadows=true,
        ShadowPriority=0,
        ShadowBiasMeters=0.005f,
        HasLastCameraRelativeWorld=true,
        LastTransformStatus=FixedTransformStatus.Success,
        LastWorldResolveStatus=ResolveStatus,
    };
    internal void BindStaticMesh(Entity Source,in Transform Transform,in StaticMesh Mesh,EngineHost Host,ref State State){
        if(this.Bound.Contains(Source)||Host.Renderer is not{ }Renderer){ return; }
        if(string.IsNullOrEmpty(Mesh.AssetPath)){
            this.Fail(string.Empty,$"A StaticMesh on {Source} names no asset.");
            return;
        }
        var FullPath=ContentRoot.Resolve(Mesh.AssetPath);
        if(!this.TryGetStaticAsset(FullPath,Renderer,Host,out var Asset)){ return; }
        this.BindObjects(Source,in Transform,Asset.Objects,Renderer,Host,ref State);
    }
    internal void BindPrimitiveMesh(Entity Source,in Transform Transform,in PrimitiveMesh Mesh,EngineHost Host,ref State State){
        if(this.Bound.Contains(Source)||Host.Renderer is not{ }Renderer){ return; }
        if(!this.TryGetPrimitive(Mesh.Type,Renderer,out var MeshIndex,out var MaterialIndex)){ return; }
        this.BindObjects(Source,in Transform,[new StageSceneDataBuilder.AssetObject(MeshIndex,MaterialIndex,Matrix4x4.Identity)],Renderer,Host,ref State);
    }
    /// <summary>
    /// <see cref="CSharpGameEngine.Runtime.Animation.SkeletalMesh"/>を持ち、まだ描画されていないEntityにレンダラーオブジェクトを割り当てる（M0107）。
    /// 過去に同じアセットのEntityが返却したメッシュ／マテリアルのコピーを使うか、新しいコピーを作ってレンダラーへ追加する。
    /// </summary>
    internal void BindSkeletalMesh(Entity Source,in Transform Transform,in CSharpGameEngine.Runtime.Animation.SkeletalMesh Mesh,EngineHost Host,ref State State){
        if(this.Bound.Contains(Source)||Host.Renderer is not{ }Renderer){ return; }
        if(string.IsNullOrEmpty(Mesh.AssetPath)){
            // フレームごとではなく、Entityごとに一度だけ通知する。
            var NoAsset=$"skeletal-without-asset:{Source}";
            if(!this.FailedAssets.Contains(NoAsset)){
                this.Fail(NoAsset,$"A SkeletalMesh on {Source} names no asset.");
            }
            return;
        }
        var FullPath=CSharpGameEngine.Runtime.Animation.SkeletalAssetCache.ResolvePath(Mesh.AssetPath);
        if(this.FailedAssets.Contains(FullPath)){ return; }
        var Reused=this.FreeSkeletal.TryGetValue(FullPath,out var Pool)&&Pool.Count>0;
        StageSceneDataBuilder.SkeletalInstance?Instance;
        if(Reused){
            Instance=Pool![^1];
            Pool.RemoveAt(Pool.Count-1);
        } else {
            Instance=this.AddSkeletalInstance(FullPath,Mesh.AssetPath,Renderer,Host);
            if(Instance is null){ return; }
        }
        this.BindObjects(Source,in Transform,Instance.Template.Objects,Renderer,Host,ref State);
        if(!this.Bound.Contains(Source)){
            // まだ描画場所がない、または空きがない場合は、次の試行までコピーを保留する。
            if(!this.FreeSkeletal.TryGetValue(FullPath,out Pool)){
                this.FreeSkeletal[FullPath]=Pool=[];
            }
            Pool.Add(Instance);
            return;
        }
        this.SkeletalOwners[Source]=Instance with { Entity=Source };
        Host.RegisterSkinnedMeshes(Source,Instance.SkinnedMeshIndices);
        if(Reused){
            // コピーには前の所有者の姿勢が残っているため、アニメーターが書き込むまではバインド姿勢から新しいEntityを開始する。
            var Identity=Enumerable.Repeat(Matrix4x4.Identity,CSharpGameEngine.Runtime.Animation.SkeletalAssetCache.Skeleton(Mesh.AssetPath).JointCount).ToArray();
            foreach(var MeshIndex in Instance.SkinnedMeshIndices){
                Renderer.SetSkinPalette(MeshIndex,Identity);
            }
        }
    }
    /// <summary>スケルタルアセットのメッシュとマテリアルを新しくコピーしてレンダラーへ追加する。読み込めない場合、または領域に収まらない場合は診断とともにnull。</summary>
    private StageSceneDataBuilder.SkeletalInstance?AddSkeletalInstance(string FullPath,string AssetPath,RenderServer Renderer,EngineHost Host){
        List<StageMeshCreateInfo>Meshes=[];
        List<StageMaterialCreateInfo>Materials=[];
        StageSceneDataBuilder.AssetTemplate Template;
        int[]Skinned;
        try {
            (Template,Skinned)=StageSceneDataBuilder.LoadSkeletalAsset(AssetPath,Meshes,Materials);
        } catch(Exception Error) when(Error is InvalidOperationException or InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException){
            this.Fail(FullPath,Error.Message);
            return null;
        }
        if(Renderer.FreeMaterialSlots<Materials.Count){
            this.Fail(FullPath,$"The renderer has no room for the {Materials.Count} materials of '{FullPath}'.");
            return null;
        }
        var MeshBase=Renderer.MeshCount;
        var MaterialBase=Renderer.MaterialCount;
        foreach(var Mesh in Meshes){
            Renderer.AddMesh(Mesh);
        }
        foreach(var Material in Materials){
            Renderer.AddMaterial(Material);
            this.MaterialEmission.Add(EmissionOf(Material));
        }
        Host.AppendWorldGridMaterials(MaterialBase,Materials);
        var Objects=Template.Objects.Select(Object=>new StageSceneDataBuilder.AssetObject(MeshBase+Object.MeshIndex,MaterialBase+Object.MaterialIndex,Object.LocalTransform)).ToArray();
        foreach(var Object in Template.Objects){
            this.NoteEmissiveMesh(MeshBase+Object.MeshIndex,MaterialBase+Object.MaterialIndex,Meshes[Object.MeshIndex]);
        }
        return new StageSceneDataBuilder.SkeletalInstance(default,FullPath,new StageSceneDataBuilder.AssetTemplate(Objects),[.. Skinned.Select(Index=>MeshBase+Index)]);
    }
    /// <summary><paramref name="Source"/>のオブジェクト1つの描画を停止する。後からEntityにメッシュが追加された場合は、再びバインドできる。</summary>
    internal void Release(Entity Source,int ObjectIndex,EngineHost Host){
        if(ObjectIndex>=0&&Host.Renderer is{ } Renderer){
            Renderer.RemoveObject(ObjectIndex);
        }
        Host.UnregisterRenderObject(Source,ObjectIndex);
        this.Bound.Remove(Source);
        if(this.SkeletalOwners.TryGetValue(Source,out var Owner)&&!Host.HasRenderObjects(Source)){
            // スケルタルEntityの最後のオブジェクトを破棄する。メッシュとマテリアルは次の同アセットEntity用に保持する。
            this.SkeletalOwners.Remove(Source);
            Host.UnregisterSkinnedMeshes(Source);
            if(!this.FreeSkeletal.TryGetValue(Owner.FullPath,out var Pool)){
                this.FreeSkeletal[Owner.FullPath]=Pool=[];
            }
            Pool.Add(Owner);
        }
    }
    private void BindObjects(Entity Source,in Transform Transform,ReadOnlySpan<StageSceneDataBuilder.AssetObject>Objects,RenderServer Renderer,EngineHost Host,ref State State){
        // 描画領域がないとオブジェクトはカメラ位置に描画されてしまうため、次のフレームにEntityを再試行する。
        if(RuntimeWorldTransform.TryGetCameraRelativeMatrix(Source,State.World,Host,out var CameraRelative,out var ResolveStatus)!=FixedTransformStatus.Success){ return; }
        var Added=0;
        foreach(var Object in Objects){
            var Index=Renderer.AddObject(Object.MeshIndex,Object.MaterialIndex);
            if(Index<0){
                this.DroppedObjects++;
                if(!this.ReportedHeadroom){
                    this.ReportedHeadroom=true;
                    this.DiagnosticList.Add($"The renderer has no room for more than {StageRenderer.DynamicObjectHeadroom} objects added after the level loaded; the rest are not drawn.");
                }
                continue;
            }
            Renderer.SetObjectTransform(Index,Object.LocalTransform*CameraRelative);
            Host.RegisterRenderObject(Source,Index);
            State.World.CreateDeferred(Transform,new Renderable {
                ObjectIndex=Index,
                SourceEntity=Source,
                HasSourceEntity=true,
                MeshLocalTransform=Object.LocalTransform,
                HasMeshLocalTransform=true,
                LastCameraRelativeWorld=CameraRelative,
                HasLastCameraRelativeWorld=true,
                LastTransformStatus=FixedTransformStatus.Success,
                LastWorldResolveStatus=ResolveStatus,
            });
            var Emission=this.MaterialEmission[Object.MaterialIndex];
            if(IsEmissive(Emission.Nits)&&LightsTheScene(Source,State.World)&&this.EmissiveMeshes.TryGetValue(Object.MeshIndex,out var EmissiveMesh)){
                State.World.CreateDeferred(Transform,NewEmissiveLighting(Source,EmissiveMesh,Emission,Object.LocalTransform,CameraRelative,ResolveStatus));
            }
            Added++;
        }
        if(Added>0){ this.Bound.Add(Source); }
    }
    private bool TryGetStaticAsset(string FullPath,RenderServer Renderer,EngineHost Host,out StageSceneDataBuilder.AssetTemplate Asset){
        if(this.StaticAssets.TryGetValue(FullPath,out Asset!)){ return true; }
        if(this.FailedAssets.Contains(FullPath)){ return false; }
        List<StageMeshCreateInfo>Meshes=[];
        List<StageMaterialCreateInfo>Materials=[];
        StageSceneDataBuilder.AssetTemplate Loaded;
        try {
            Loaded=StageSceneDataBuilder.LoadAsset(FullPath,Meshes,Materials);
        } catch(Exception Error) when(Error is InvalidOperationException or InvalidDataException or IOException){
            this.Fail(FullPath,Error.Message);
            return false;
        }
        if(Meshes.Any(Mesh=>Mesh.Skin is not null)){
            this.Fail(FullPath,$"The StaticMesh '{FullPath}' has a skinned mesh, which cannot be added after the level loaded.");
            return false;
        }
        if(Renderer.FreeMaterialSlots<Materials.Count){
            this.Fail(FullPath,$"The renderer has no room for the {Materials.Count} materials of '{FullPath}'.");
            return false;
        }
        var MeshBase=Renderer.MeshCount;
        var MaterialBase=Renderer.MaterialCount;
        foreach(var Mesh in Meshes){
            Renderer.AddMesh(Mesh);
        }
        foreach(var Material in Materials){
            Renderer.AddMaterial(Material);
            this.MaterialEmission.Add(EmissionOf(Material));
        }
        Host.AppendWorldGridMaterials(MaterialBase,Materials);
        Asset=new StageSceneDataBuilder.AssetTemplate([.. Loaded.Objects.Select(Object=>new StageSceneDataBuilder.AssetObject(MeshBase+Object.MeshIndex,MaterialBase+Object.MaterialIndex,Object.LocalTransform))]);
        foreach(var Object in Loaded.Objects){
            this.NoteEmissiveMesh(MeshBase+Object.MeshIndex,MaterialBase+Object.MaterialIndex,Meshes[Object.MeshIndex]);
        }
        this.StaticAssets.Add(FullPath,Asset);
        return true;
    }
    private bool TryGetPrimitive(PrimitiveType Type,RenderServer Renderer,out int MeshIndex,out int MaterialIndex){
        MaterialIndex=this.PrimitiveMaterial;
        if(this.PrimitiveMeshes.TryGetValue(Type,out MeshIndex)&&MaterialIndex>=0){ return true; }
        var Key="primitive:"+Type;
        if(this.FailedAssets.Contains(Key)){ return false; }
        if(!Enum.IsDefined(Type)){
            this.Fail(Key,$"The PrimitiveMesh type {(int)Type} is not a known primitive.");
            return false;
        }
        var (Meshes,Materials,_)=PrimitiveRoster.Build([Type],0,0);
        if(MaterialIndex<0){
            if(Renderer.FreeMaterialSlots<1){
                this.Fail(Key,"The renderer has no room for the primitive material.");
                return false;
            }
            MaterialIndex=Renderer.AddMaterial(Materials[0]);
            this.PrimitiveMaterial=MaterialIndex;
            this.MaterialEmission.Add(EmissionOf(Materials[0]));
        }
        MeshIndex=Renderer.AddMesh(Meshes[0]);
        this.PrimitiveMeshes.Add(Type,MeshIndex);
        return true;
    }
    private void Fail(string Key,string Message){
        this.FailedAssets.Add(Key);
        this.DiagnosticList.Add(Message);
    }
}
