using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content.Models;
/// <summary>参照先が厳密に指定されたランタイム依存関係。解決にソースパスやプロバイダーオブジェクトは不要。</summary>
public sealed record ModelReference(string Uri,string Hash) {
    public AssetArtifactReference ToReference(){
        if(!AssetUri.TryParse(this.Uri,out var Asset,out var Error)||!Hash256.TryParse(this.Hash,out var Hash)||!Hash.IsValid){ throw new InvalidDataException("Invalid model dependency: "+Error); }
        return new(Asset.Key,Hash);
    }
    public static ModelReference From(AssetArtifactReference Reference)=>new(new AssetUri(Reference.Key).ToString(),Reference.ContentHash.ToString());
}
public readonly record struct ModelLocalTransform(Vector3 Translation,Quaternion Rotation,Vector3 Scale) {
    public Matrix4x4 ToMatrix()=>Matrix4x4.CreateScale(this.Scale)*Matrix4x4.CreateFromQuaternion(this.Rotation)*Matrix4x4.CreateTranslation(this.Translation);
}
public sealed record ModelNodeData(string Name,int ParentIndex,ModelLocalTransform Local,int MeshIndex,ImmutableArray<int>MaterialSlots,int SkinIndex,uint Flags);
public sealed record ModelSkinData(string Name,int SkeletonIndex,ImmutableArray<int>JointMap,ImmutableArray<Matrix4x4>InverseBindMatrices,Matrix4x4 BindShape);
public sealed record ModelRuntimeBlock(string Schema,ImmutableArray<byte>Payload);
public readonly record struct ModelOriginOffset(Int128 X,Int128 Y,Int128 Z);
public sealed record ModelData(string Name,ImmutableArray<ModelNodeData>Nodes,ImmutableArray<ModelReference>Meshes,ImmutableArray<ModelReference>Materials,ImmutableArray<ModelReference>Skeletons,ImmutableArray<ModelSkinData>Skins,ImmutableArray<ModelAvatarData>Avatars,ImmutableArray<ModelRuntimeBlock>RuntimeBlocks,ModelOriginOffset Origin);
public readonly record struct ModelSubmesh(int FirstIndex,int IndexCount,int MaterialSlot);
public readonly record struct ModelBounds(Vector3 Min,Vector3 Max);
public sealed record ModelSkinInfluences(ImmutableArray<ushort>JointIndices,ImmutableArray<float>Weights) {
    public const int InfluencesPerVertex=8;
}
public sealed record ModelCustomStream(string Semantic,int Components,ImmutableArray<float>Values);
public sealed record ModelMorphInBetween(float Weight,ImmutableArray<Vector3>Positions,ImmutableArray<Vector3>Normals,ImmutableArray<Vector3>Tangents);
public sealed record ModelMorphTarget(string Name,ImmutableArray<int>SparseVertices,ImmutableArray<Vector3>Positions,ImmutableArray<Vector3>Normals,ImmutableArray<Vector3>Tangents,float DefaultWeight,ImmutableArray<ModelMorphInBetween>InBetweens);
public sealed record ModelMeshData(string Name,ImmutableArray<Vector3>Positions,ImmutableArray<Vector3>Normals,ImmutableArray<Vector4>Tangents,ImmutableArray<ImmutableArray<Vector2>>TextureCoordinates,ImmutableArray<ImmutableArray<Vector4>>Colors,ImmutableArray<uint>Indices,ImmutableArray<ModelSubmesh>Submeshes,ImmutableArray<int>DefaultMaterials,ModelSkinInfluences?Influences,ImmutableArray<ModelMorphTarget>Morphs,ImmutableArray<ModelCustomStream>Custom,ModelBounds Bounds);
public sealed record ModelSkeletonData(string Name,ImmutableArray<string>JointNames,ImmutableArray<int>ParentIndices,ImmutableArray<ModelLocalTransform>Rest,ImmutableArray<byte>JointFlags,Matrix4x4 RootTransform);
public readonly record struct ModelHumanBone(HumanoidBone Bone,int JointIndex);
public sealed record ModelAvatarData(int SkeletonIndex,int RootNode,ImmutableArray<ModelHumanBone>Bones,ImmutableArray<Quaternion>RestToReference,float HipsHeightMeters,string MappingSource);
public enum ModelInterpolation:byte { Linear,Step,CubicSpline }
public sealed record ModelKeys<T>(ImmutableArray<float>Times,ImmutableArray<T>Values,ModelInterpolation Interpolation,ImmutableArray<T>InTangents,ImmutableArray<T>OutTangents) where T:struct;
public sealed record ModelTransformTrack(string JointName,int NodeIndex,ModelKeys<Vector3>?Translation,ModelKeys<Quaternion>?Rotation,ModelKeys<Vector3>?Scale);
public sealed record ModelMorphWeightTrack(int NodeIndex,int TargetIndex,ModelKeys<float>Weights);
public sealed record ModelCurve(string Name,ModelKeys<float>Keys);
public sealed record ModelAnimationEvent(float TimeSeconds,string Name,string Payload);
public sealed record ModelRootMotion(int NodeIndex,ModelKeys<Vector3>Translation,ModelKeys<Quaternion>Rotation);
public sealed record ModelAnimationClipData(string Name,float StartSeconds,float EndSeconds,float SourceFramesPerSecond,uint Flags,ImmutableArray<ModelTransformTrack>Tracks,ImmutableArray<ModelMorphWeightTrack>MorphWeights,ImmutableArray<ModelCurve>Curves,ImmutableArray<ModelAnimationEvent>Events,ModelRootMotion?RootMotion);
public sealed record ModelLicenseData(string Kind,bool Confirmed,string Credit,string SourceUrl,ImmutableArray<string>Restrictions);
