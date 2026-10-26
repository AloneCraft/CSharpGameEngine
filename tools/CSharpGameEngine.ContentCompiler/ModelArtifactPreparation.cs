using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.ContentCompiler;
public sealed record PreparedModelArtifact(AssetKey Key,ContentArtifactKind Kind,string Name,byte[]Envelope,AssetArtifactReference Reference,Hash256 Address,ImmutableArray<AssetKey>Dependencies);
public sealed record PreparedModelSource(AssetId AssetId,string SourceUri,ImmutableArray<PreparedModelArtifact>Artifacts,ImmutableArray<ContentCompileNode>Nodes,AssetKey Root);
/// <summary>コンパイルノードを公開する前に、すべての変換とエンベロープ検証を完了する。</summary>
public static class ModelArtifactPreparation {
    public static PreparedModelSource Prepare(AssetId AssetId,InterchangeDocument Document,Hash256 SourceHash,Hash256 OptionsHash,string ToolVersion,IEnumerable<PreparedModelArtifact>?Resources=null){
        ArgumentNullException.ThrowIfNull(Document);
        var Errors=ModelImportPipeline.ValidateEngine(Document).Where(D=>D.Severity==ImportSeverity.Error).ToArray();
        if(Errors.Length!=0){ throw new InvalidDataException(string.Join(Environment.NewLine,Errors.Select(D=>D.ToString()))); }
        if(!AssetId.IsValid){ throw new ArgumentException("A valid asset identity is required.",nameof(AssetId)); }
        var Provenance=new ArtifactProvenance(ToolVersion,Document.Provenance.SourceUri,SourceHash,Document.Provenance.ImporterIdentity,OptionsHash);
        var Artifacts=new List<PreparedModelArtifact>();
        var ByKey=new Dictionary<AssetKey,PreparedModelArtifact>();
        AssetArtifactReference?LicenseReference=null;
        var License=Document.Provenance.License;
        LicenseReference=Leaf(SubAssetKind.License,string.Empty,ContentArtifactKind.License,ModelArtifactCodec.WriteLicense(new(License.Kind,License.Confirmed,License.Credit,License.SourceUrl,License.Restrictions))).Reference;
        foreach(var Resource in Resources??[]){
            var Checked=Validate(Resource.Key,Resource.Name,Resource.Envelope,Provenance);
            if(Checked!=Resource&&(!Checked.Reference.Equals(Resource.Reference)||Checked.Address!=Resource.Address||Checked.Kind!=Resource.Kind||!Checked.Dependencies.SequenceEqual(Resource.Dependencies))){ throw new InvalidDataException("IMP902: prepared resource metadata does not match its envelope."); }
            Add(Checked);
        }
        var Meshes=new List<ModelReference>();
        foreach(var Mesh in Document.Meshes){
            var V=Mesh.Vertices;
            if(V.Normals.Length!=V.Count||V.Tangents.Length!=V.Count){ throw new InvalidDataException("IMP110: runtime mesh streams must be generated before artifact preparation."); }
            var Payload=new ModelMeshData(Mesh.Name,V.Positions,V.Normals,V.Tangents,V.TexCoords,V.Colors,Mesh.Indices,[..Mesh.Submeshes.Select(S=>new ModelSubmesh(S.FirstIndex,S.IndexCount,S.MaterialSlot))],Mesh.DefaultMaterials,
                V.Influences is{ }I?new(I.Joints,I.Weights):null,[..Mesh.MorphTargets.Select(M=>new ModelMorphTarget(M.Name,M.SparseVertices,M.PositionDeltas,M.NormalDeltas,M.TangentDeltas,M.DefaultWeight,[..M.InBetweens.Select(B=>new ModelMorphInBetween(B.Weight,B.PositionDeltas,B.NormalDeltas,B.TangentDeltas))]))],
                [..V.Custom.Select(C=>new ModelCustomStream(C.Semantic,C.Components,C.Values))],new(Mesh.Bounds.Min,Mesh.Bounds.Max));
            Meshes.Add(ModelReference.From(Leaf(SubAssetKind.Mesh,Mesh.Name,ContentArtifactKind.Mesh,ModelArtifactCodec.WriteMesh(Payload)).Reference));
        }
        var Skeletons=new List<ModelReference>();
        foreach(var Skeleton in Document.Skeletons){
            var Joints=Skeleton.JointNodes;
            var JointMap=Joints.Select((Node,Index)=>(Node,Index)).ToDictionary(P=>P.Node,P=>P.Index);
            var Root=Matrix4x4.Identity;
            for(var Parent=Skeleton.RootParentNode;Parent>=0;Parent=Document.Nodes[Parent].ParentIndex){ Root*=Document.Nodes[Parent].Local.ToMatrix(); }
            var Payload=new ModelSkeletonData(Skeleton.Name,[..Joints.Select(N=>Document.Nodes[N].Name)],[..Joints.Select(N=>JointMap.TryGetValue(Document.Nodes[N].ParentIndex,out var Parent)?Parent:-1)],
                [..Joints.Select(N=>Pose(Document.Nodes[N].Local))],[..Skeleton.JointFlags.Select(F=>(byte)F)],Root);
            Skeletons.Add(ModelReference.From(Leaf(SubAssetKind.Skeleton,Skeleton.Name,ContentArtifactKind.Skeleton,ModelArtifactCodec.WriteSkeleton(Payload)).Reference));
        }
        foreach(var Clip in Document.Clips){
            var Payload=new ModelAnimationClipData(Clip.Name,Clip.StartSeconds,Clip.EndSeconds,Clip.SourceFramesPerSecond,(uint)Clip.Flags,
                [..Clip.Transforms.Select(T=>new ModelTransformTrack(Document.Nodes[T.NodeIndex].Name,T.NodeIndex,Keys(T.Translation),Keys(T.Rotation),Keys(T.Scale)))],
                [..Clip.MorphWeights.Select(T=>new ModelMorphWeightTrack(T.NodeIndex,T.TargetIndex,Keys(T.Weights)!))],
                [..Clip.Curves.Select(C=>new ModelCurve(C.Name,Keys(C.Keys)!))],[..Clip.Events.Select(E=>new ModelAnimationEvent(E.TimeSeconds,E.Name,E.Payload))],
                Clip.RootMotion is{ }R?new(R.NodeIndex,Keys(R.Translation)!,Keys(R.Rotation)!):null);
            _=Leaf(SubAssetKind.Clip,Clip.Name,ContentArtifactKind.AnimationClip,ModelArtifactCodec.WriteClip(Payload));
        }
        var Materials=new List<ModelReference>();
        foreach(var Material in Document.Materials){
            var Key=new AssetKey(AssetId,SubAssetIdentity.Derive(SubAssetKind.Material,Material.Name));
            if(!ByKey.TryGetValue(Key,out var Prepared)||Prepared.Kind!=ContentArtifactKind.Material){ throw new InvalidDataException("IMP902: material resource was not prepared: "+Material.Name); }
            Materials.Add(ModelReference.From(Prepared.Reference));
        }
        var Model=new ModelData(Path.GetFileNameWithoutExtension(Document.Provenance.SourceUri),
            [..Document.Nodes.Select(N=>new ModelNodeData(N.Name,N.ParentIndex,Pose(N.Local),N.MeshIndex,N.MaterialSlots,N.SkinIndex,(uint)N.Flags))],
            [..Meshes],[..Materials],[..Skeletons],
            [..Document.Skins.Select(S=>new ModelSkinData(S.Name,S.SkeletonIndex,S.JointMap,S.InverseBindMatrices,S.BindShape))],
            [..Document.Rig.Avatars.Select(A=>new ModelAvatarData(A.SkeletonIndex,A.RootNode,[..A.Bones.Select(B=>new ModelHumanBone(B.Bone,B.JointIndex))],A.RestToReference,A.HipsHeightMeters,A.MappingSource))],
            [..Document.RuntimeBlocks.Select(B=>new ModelRuntimeBlock(B.Schema,B.Payload))],new(Document.Space.Origin.X,Document.Space.Origin.Y,Document.Space.Origin.Z));
        var RootKey=new AssetKey(AssetId,null);
        var RootArtifact=Validate(RootKey,Model.Name,ContentArtifactCodec.Write(new(ContentArtifactKind.Model,Provenance,Artifacts.Select(A=>A.Reference),ModelArtifactCodec.WriteModel(Model))),Provenance);
        Add(RootArtifact);
        foreach(var Artifact in Artifacts){
            foreach(var Dependency in Artifact.Dependencies){
                if(!ByKey.TryGetValue(Dependency,out var Target)){ throw new InvalidDataException("IMP902: prepared dependency is missing."); }
                if(!ContentArtifactCodec.TryRead(Artifact.Envelope,out var Decoded,out _)||Decoded is null||!Decoded.Dependencies.Contains(Target.Reference)){ throw new InvalidDataException("IMP902: prepared dependency hash is inconsistent."); }
            }
        }
        var Nodes=Artifacts.Select(A=>{
            ReadOnlyMemory<byte>Snapshot=A.Envelope.ToArray();
            return new ContentCompileNode(A.Key,Provenance.SourceUri,SourceHash,ToolVersion,Provenance.ImporterIdentity,OptionsHash,A.Dependencies,(_,_)=>ValueTask.FromResult(Snapshot));
        }).ToImmutableArray();
        _=ContentCompileGraph.Create(Nodes);
        return new(AssetId,Provenance.SourceUri,[..Artifacts],Nodes,RootKey);
        void Add(PreparedModelArtifact Artifact){
            if(Artifact.Key.AssetId!=AssetId||!ByKey.TryAdd(Artifact.Key,Artifact)){ throw new InvalidDataException("IMP010: duplicate or foreign subasset identity."); }
            Artifacts.Add(Artifact);
        }
        PreparedModelArtifact Leaf(SubAssetKind SubKind,string Name,ContentArtifactKind Kind,byte[]Payload){
            var Key=new AssetKey(AssetId,SubAssetIdentity.Derive(SubKind,Name));
            var Result=Validate(Key,Name,ContentArtifactCodec.Write(new(Kind,Provenance,LicenseReference is{ }LicenseDependency?[LicenseDependency]:[],Payload)),Provenance);
            Add(Result);
            return Result;
        }
    }
    public static PreparedModelArtifact Validate(AssetKey Key,string Name,byte[]Envelope,ArtifactProvenance Provenance){
        if(!Key.IsValid||!ContentArtifactCodec.TryRead(Envelope,out var Artifact,out var Error)||Artifact is null){ throw new InvalidDataException("IMP902: invalid prepared envelope."); }
        if(Artifact.Provenance!=Provenance||!Envelope.AsSpan().SequenceEqual(ContentArtifactCodec.Write(Artifact))){ throw new InvalidDataException("IMP902: resource envelope is noncanonical or has inconsistent provenance."); }
        return new(Key,Artifact.Kind,Name,Envelope,new(Key,Artifact.ContentHash),ContentArtifactCodec.ComputeAddress(Envelope),[..Artifact.Dependencies.Select(D=>D.Key)]);
    }
    private static ModelLocalTransform Pose(InterchangeTransform T)=>new(T.Translation,T.Rotation,T.Scale);
    private static ModelKeys<T>?Keys<T>(InterchangeKeys<T>?K) where T:struct=>K is null?null:new(K.Times,K.Values,(ModelInterpolation)K.Interpolation,K.InTangents,K.OutTangents);
}
