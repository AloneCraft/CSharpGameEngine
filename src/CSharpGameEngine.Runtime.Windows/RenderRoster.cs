using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// ADR-0078/M0105: the drawn objects follow the Entities. <see cref="LevelLoad"/> still builds the renderer from the Entities that
/// exist when the level loads; from then on this keeps the renderer's objects in step with the world. An Entity that gains a
/// <see cref="StaticMesh"/> or <see cref="PrimitiveMesh"/> (spawned, or moved to a shape that has one) gets its objects added; when it
/// is destroyed or loses the mesh, its objects stop being drawn (<see cref="Release"/>, called by the renderable wrappers).
/// A mesh asset no Entity has used yet is loaded and uploaded the first time one is bound, which costs one frame.
/// A <see cref="CSharpGameEngine.Runtime.Animation.SkeletalMesh"/> spawned later (M0107: a respawned character) gets its own copy of the asset's skinned
/// meshes and materials, which join the renderer's skinning pass (the first one costs a GPU wait); when that Entity is gone its copy goes back to a
/// pool for the next Entity of the same asset, so respawning over and over does not grow the renderer. What is not followed: a changed
/// <see cref="StaticMesh.AssetPath"/> on an Entity that is already drawn.
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
    /// <summary>The roster of a level just loaded: every Entity <paramref name="Scene"/> turned into objects counts as bound.</summary>
    internal static RenderRoster FromScene(StageSceneDataBuilder Scene)=>new(Scene);
    /// <summary>What went wrong binding an Entity (an asset that did not load, no room left), one line each, for the log and the tests.</summary>
    internal IReadOnlyList<string>Diagnostics=>this.DiagnosticList;
    /// <summary>How many objects could not be added because the renderer's headroom was used up.</summary>
    internal int DroppedObjects { get;private set;}
    internal bool IsBound(Entity Source)=>this.Bound.Contains(Source);
    internal static (Vector3 Nits,float LuminanceScale)EmissionOf(in StageMaterialCreateInfo Material)=>(
        Material.EmissiveNits!=Vector3.Zero?Material.EmissiveNits:Material.EmissiveFactor*100f,
        Material.EmissiveTextureLuminanceScale);
    internal static bool IsEmissive(Vector3 Nits)=>Nits.X>0f||Nits.Y>0f||Nits.Z>0f;
    /// <summary>
    /// Whether an emissive mesh also lights its surroundings (its light patches). A mesh with a <see cref="MeshEmissive"/> (ADR-0078/M0109: lamps that the game
    /// turns up and down, as brake lights) only glows on its surface: its emission changes every frame and is a small part of a large mesh.
    /// </summary>
    internal static bool LightsTheScene(Entity Source,CSharpGameEngine.Entities.World World)=>!World.TryGetComponent(Source,out MeshEmissive _);
    /// <summary>Whether an Entity still has something to draw: it is alive and has a mesh component.</summary>
    internal static bool IsDrawnSource(Entity Source,CSharpGameEngine.Entities.World World)=>
        World.IsAlive(Source)
        &&(World.TryGetComponent(Source,out StaticMesh _)
            ||World.TryGetComponent(Source,out PrimitiveMesh _)
            ||World.TryGetComponent(Source,out CSharpGameEngine.Runtime.Animation.SkeletalMesh _)
            ||World.TryGetComponent(Source,out FragmentMesh _));
    private readonly Dictionary<(string Path,int Fragment),StageSceneDataBuilder.AssetObject[]>FragmentObjects=new();
    private readonly Dictionary<(Vector3 Outer,Vector3 Interior),(int Outer,int Interior)>FragmentMaterials=new();
    /// <summary>
    /// ADR-0078/M0113: gives an Entity with a <see cref="FragmentMesh"/> its objects: the fragment's outer and cut triangles as two meshes (made once per
    /// fragment of an asset), with a pair of plain materials per colour pair. An asset that cannot be loaded is reported once and not drawn.
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
    /// <summary>The wrapper that submits an emissive object's light patches every frame; made at load and for every object bound later.</summary>
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
    /// Gives an Entity that has a <see cref="CSharpGameEngine.Runtime.Animation.SkeletalMesh"/> and is not drawn yet its renderer objects (M0107): a copy of the asset's meshes
    /// and materials that an earlier Entity of the asset gave back, or a new copy added to the renderer.
    /// </summary>
    internal void BindSkeletalMesh(Entity Source,in Transform Transform,in CSharpGameEngine.Runtime.Animation.SkeletalMesh Mesh,EngineHost Host,ref State State){
        if(this.Bound.Contains(Source)||Host.Renderer is not{ }Renderer){ return; }
        if(string.IsNullOrEmpty(Mesh.AssetPath)){
            // Said once per Entity, not every frame.
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
            // No place to draw it yet (or no room): the copy waits for the next try.
            if(!this.FreeSkeletal.TryGetValue(FullPath,out Pool)){
                this.FreeSkeletal[FullPath]=Pool=[];
            }
            Pool.Add(Instance);
            return;
        }
        this.SkeletalOwners[Source]=Instance with { Entity=Source };
        Host.RegisterSkinnedMeshes(Source,Instance.SkinnedMeshIndices);
        if(Reused){
            // The copy still holds its last owner's pose; start the new Entity from the bind pose until its animator writes one.
            var Identity=Enumerable.Repeat(Matrix4x4.Identity,CSharpGameEngine.Runtime.Animation.SkeletalAssetCache.Skeleton(Mesh.AssetPath).JointCount).ToArray();
            foreach(var MeshIndex in Instance.SkinnedMeshIndices){
                Renderer.SetSkinPalette(MeshIndex,Identity);
            }
        }
    }
    /// <summary>Adds a new copy of a skeletal asset's meshes and materials to the renderer; null (with a diagnostic) when it cannot be loaded or does not fit.</summary>
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
    /// <summary>Stops drawing one object of <paramref name="Source"/> and lets the Entity bind again if it gets a mesh later.</summary>
    internal void Release(Entity Source,int ObjectIndex,EngineHost Host){
        if(ObjectIndex>=0&&Host.Renderer is{ } Renderer){
            Renderer.RemoveObject(ObjectIndex);
        }
        Host.UnregisterRenderObject(Source,ObjectIndex);
        this.Bound.Remove(Source);
        if(this.SkeletalOwners.TryGetValue(Source,out var Owner)&&!Host.HasRenderObjects(Source)){
            // The last object of a skeletal Entity: its meshes and materials wait for the next Entity of the asset.
            this.SkeletalOwners.Remove(Source);
            Host.UnregisterSkinnedMeshes(Source);
            if(!this.FreeSkeletal.TryGetValue(Owner.FullPath,out var Pool)){
                this.FreeSkeletal[Owner.FullPath]=Pool=[];
            }
            Pool.Add(Owner);
        }
    }
    private void BindObjects(Entity Source,in Transform Transform,ReadOnlySpan<StageSceneDataBuilder.AssetObject>Objects,RenderServer Renderer,EngineHost Host,ref State State){
        // Without a place the object would be drawn at the camera; the Entity is tried again next frame.
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
        var (Meshes,Materials,_)=RenderServer.BuildPrimitiveRoster([Type],0,0);
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
