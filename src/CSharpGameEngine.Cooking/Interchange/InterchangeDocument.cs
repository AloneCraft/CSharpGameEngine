using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Interchange;
public enum InterchangeSpaceState:byte { Source,Engine }
public enum SourceConvention:byte { Unity,Gltf,Fbx,Unreal,BlenderWorld }
public enum AssetFacing:byte { Unknown,PlusForward,MinusForward,PlusRight,MinusRight }
public enum ReferencePoseKind:byte { Unknown,TPose,APose,Other }
public enum UvOrigin:byte { TopLeft,BottomLeft }
public readonly record struct OriginOffset(Int128 X,Int128 Y,Int128 Z);
public sealed record InterchangeSpace(InterchangeSpaceState State,SourceConvention Convention,float MetersPerUnit,AssetFacing Facing,ReferencePoseKind Pose,UvOrigin UvOrigin,OriginOffset Origin,string Evidence);
public sealed record InterchangeDocument {
    public int SchemaVersion{get;init;}=1;
    public InterchangeSpace Space{get;init;}=new(InterchangeSpaceState.Source,SourceConvention.Gltf,1f,AssetFacing.Unknown,ReferencePoseKind.Unknown,UvOrigin.TopLeft,default,string.Empty);
    public ImmutableArray<InterchangeNode>Nodes{get;init;}=[];
    public ImmutableArray<InterchangeMesh>Meshes{get;init;}=[];
    public ImmutableArray<InterchangeMaterial>Materials{get;init;}=[];
    public ImmutableArray<InterchangeTexture>Textures{get;init;}=[];
    public ImmutableArray<InterchangeImage>Images{get;init;}=[];
    public ImmutableArray<InterchangeSkeleton>Skeletons{get;init;}=[];
    public ImmutableArray<InterchangeSkin>Skins{get;init;}=[];
    public ImmutableArray<InterchangeAnimationClip>Clips{get;init;}=[];
    public ImmutableArray<InterchangeCamera>Cameras{get;init;}=[];
    public ImmutableArray<InterchangeLight>Lights{get;init;}=[];
    public InterchangeRig Rig{get;init;}=new([]);
    public InterchangeProvenance Provenance{get;init;}=new(string.Empty,string.Empty,string.Empty,[],new("Unknown",false,string.Empty,string.Empty,[]));
    public ImmutableArray<InterchangeBlock>Blocks{get;init;}=[];
    public ImmutableArray<InterchangeRawExtension>RawExtensions{get;init;}=[];
    public InterchangeSourceMap SourceMap{get;init;}=new([],[],[],[]);
    public ImmutableArray<InterchangeRuntimeBlock>RuntimeBlocks{get;init;}=[];
}
public enum InterchangeValueKind:byte { Bool,Int,Float,Float2,Float3,Float4,Quaternion,Text,Texture }
public readonly record struct InterchangeValue(InterchangeValueKind Kind,Vector4 Number,string?Text,InterchangeTextureRef?Texture);
public readonly record struct InterchangeProperty(string Key,InterchangeValue Value);
public sealed record InterchangeBlock(string Schema,ImmutableSortedDictionary<string,InterchangeValue>Values);
public enum InterchangeScope:byte { Document,Node,Mesh,Primitive,Material,Texture,Image,Skin,Animation,Camera,Light }
public sealed record InterchangeRawExtension(string Name,InterchangeScope Scope,int SourceIndex,ImmutableArray<byte>JsonUtf8);
public sealed record InterchangeSourceMap(ImmutableArray<int>NodeOfSourceNode,ImmutableArray<int>MeshOfSourceMesh,ImmutableArray<int>MaterialOfSourceMaterial,ImmutableArray<int>ImageOfSourceImage);
public sealed record InterchangeRuntimeBlock(string Schema,ImmutableArray<byte>Payload);
[Flags] public enum InterchangeNodeFlags:ushort { None=0,Joint=1,Hidden=2,Helper=4,Socket=8,LodGroup=16,Collision=32 }
public enum InheritMode:byte { Normal,SegmentScaleCompensate,IgnoreParentScale }
public readonly record struct InterchangeTransform(Vector3 Translation,Quaternion Rotation,Vector3 Scale,InheritMode Inherit) {
    public static InterchangeTransform Identity=>new(Vector3.Zero,Quaternion.Identity,Vector3.One,InheritMode.Normal);
    public Matrix4x4 ToMatrix()=>Matrix4x4.CreateScale(this.Scale)*Matrix4x4.CreateFromQuaternion(this.Rotation)*Matrix4x4.CreateTranslation(this.Translation);
}
public sealed record InterchangeNode(string Name,int ParentIndex,InterchangeTransform Local,int MeshIndex,ImmutableArray<int>MaterialSlots,int SkinIndex,int CameraIndex,int LightIndex,InterchangeNodeFlags Flags,ImmutableArray<InterchangeProperty>Properties,ImmutableArray<InterchangeBlock>Blocks);
public readonly record struct InterchangeSubmesh(int FirstIndex,int IndexCount,int MaterialSlot);
public readonly record struct InterchangeBounds(Vector3 Min,Vector3 Max);
public sealed record InterchangeInfluences(int PerVertex,ImmutableArray<ushort>Joints,ImmutableArray<float>Weights);
public sealed record InterchangeCustomStream(string Semantic,int Components,ImmutableArray<float>Values);
public sealed record InterchangeVertexStreams(int Count,ImmutableArray<Vector3>Positions,ImmutableArray<Vector3>Normals,ImmutableArray<Vector4>Tangents,ImmutableArray<ImmutableArray<Vector2>>TexCoords,ImmutableArray<ImmutableArray<Vector4>>Colors,InterchangeInfluences?Influences,ImmutableArray<InterchangeCustomStream>Custom);
public sealed record InterchangeMorphInBetween(float Weight,ImmutableArray<Vector3>PositionDeltas,ImmutableArray<Vector3>NormalDeltas,ImmutableArray<Vector3>TangentDeltas);
public sealed record InterchangeMorphTarget(string Name,ImmutableArray<int>SparseVertices,ImmutableArray<Vector3>PositionDeltas,ImmutableArray<Vector3>NormalDeltas,ImmutableArray<Vector3>TangentDeltas,float DefaultWeight,ImmutableArray<InterchangeMorphInBetween>InBetweens);
public sealed record InterchangeMesh(string Name,InterchangeVertexStreams Vertices,ImmutableArray<uint>Indices,ImmutableArray<InterchangeSubmesh>Submeshes,ImmutableArray<int>DefaultMaterials,ImmutableArray<InterchangeMorphTarget>MorphTargets,InterchangeBounds Bounds);
public enum InterchangeAlphaMode:byte { Opaque,Mask,Blend }
public readonly record struct InterchangeUvTransform(Vector2 Offset,Vector2 Scale,float Rotation);
public readonly record struct InterchangeTextureRef(int TextureIndex,int TexCoord,InterchangeUvTransform Transform,float Strength);
public sealed record InterchangeSourceMaterial(string ShaderName,ImmutableArray<InterchangeProperty>Parameters);
public sealed record InterchangeMaterial {
    public string Name{get;init;}=string.Empty;
    public Vector4 BaseColorFactor{get;init;}=Vector4.One;
    public float MetallicFactor{get;init;}=1f;
    public float RoughnessFactor{get;init;}=1f;
    public Vector3 EmissiveFactor{get;init;}
    public float EmissiveStrength{get;init;}=1f;
    public Vector3 EmissiveNits{get;init;}
    public float EmissiveTextureLuminanceScale{get;init;}=1f;
    public InterchangeTextureRef?BaseColor{get;init;}
    public InterchangeTextureRef?Normal{get;init;}
    public InterchangeTextureRef?MetallicRoughness{get;init;}
    public InterchangeTextureRef?Emissive{get;init;}
    public InterchangeTextureRef?Occlusion{get;init;}
    public InterchangeAlphaMode AlphaMode{get;init;}
    public float AlphaCutoff{get;init;}=0.5f;
    public bool DoubleSided{get;init;}
    public InterchangeSourceMaterial?Source{get;init;}
    public ImmutableArray<InterchangeBlock>Blocks{get;init;}=[];
}
public readonly record struct InterchangeSampler(int WrapS,int WrapT,int MinFilter,int MagFilter);
public sealed record InterchangeTexture(string Name,int ImageIndex,InterchangeSampler Sampler);
public sealed record InterchangeImage(string Name,string MimeType,ImmutableArray<byte>EncodedBytes);
[Flags] public enum InterchangeJointFlags:byte { None=0,Deform=1,Leaf=2,Helper=4,IkTarget=8,Twist=16,Synthetic=32 }
public sealed record InterchangeSkeleton(string Name,ImmutableArray<int>JointNodes,ImmutableArray<InterchangeJointFlags>JointFlags,int RootParentNode);
public enum InterchangeSkinningMethod:byte { LinearBlend,DualQuaternion }
public sealed record InterchangeSkin(string Name,int SkeletonIndex,ImmutableArray<int>JointMap,ImmutableArray<Matrix4x4>InverseBindMatrices,Matrix4x4 BindShape,InterchangeSkinningMethod Method);
public enum InterchangeInterpolation:byte { Linear,Step,CubicSpline }
public sealed record InterchangeKeys<T>(ImmutableArray<float>Times,ImmutableArray<T>Values,InterchangeInterpolation Interpolation,ImmutableArray<T>InTangents,ImmutableArray<T>OutTangents) where T:struct;
public sealed record InterchangeTransformTrack(int NodeIndex,InterchangeKeys<Vector3>?Translation,InterchangeKeys<Quaternion>?Rotation,InterchangeKeys<Vector3>?Scale);
public sealed record InterchangeMorphWeightTrack(int NodeIndex,int TargetIndex,InterchangeKeys<float>Weights);
public sealed record InterchangePropertyTrack(InterchangeScope Scope,int TargetIndex,string PropertyPath,InterchangeKeys<Vector4>Keys);
public sealed record InterchangeCurve(string Name,InterchangeKeys<float>Keys);
public sealed record InterchangeAnimationEvent(float TimeSeconds,string Name,string Payload);
public sealed record InterchangeRootMotion(int NodeIndex,InterchangeKeys<Vector3>Translation,InterchangeKeys<Quaternion>Rotation);
[Flags] public enum InterchangeClipFlags:byte { None=0,Loop=1,Additive=2,Humanoid=4 }
public sealed record InterchangeAnimationClip(string Name,float StartSeconds,float EndSeconds,float SourceFramesPerSecond,InterchangeClipFlags Flags,ImmutableArray<InterchangeTransformTrack>Transforms,ImmutableArray<InterchangeMorphWeightTrack>MorphWeights,ImmutableArray<InterchangePropertyTrack>Properties,ImmutableArray<InterchangeCurve>Curves,ImmutableArray<InterchangeAnimationEvent>Events,InterchangeRootMotion?RootMotion);
public readonly record struct InterchangeHumanBone(HumanoidBone Bone,int JointIndex);
public sealed record InterchangeAvatar(int SkeletonIndex,int RootNode,ImmutableArray<InterchangeHumanBone>Bones,ReferencePoseKind SourcePose,ImmutableArray<Quaternion>RestToReference,float HipsHeightMeters,string MappingSource,ImmutableArray<InterchangeProperty>SourceLimits);
public sealed record InterchangeRig(ImmutableArray<InterchangeAvatar>Avatars);
public sealed record InterchangeCamera(string Name,bool Orthographic,float NearMeters,float?FarMeters,float VerticalFieldOfViewTurns,float AspectRatio,float OrthographicHeightMeters);
public enum InterchangeLightKind:byte { Directional,Point,Spot }
public sealed record InterchangeLight(string Name,InterchangeLightKind Kind,Vector3 Color,float Intensity,float?RangeMeters,float InnerHalfAngleTurns,float OuterHalfAngleTurns);
public sealed record InterchangeLicense(string Kind,bool Confirmed,string Credit,string SourceUrl,ImmutableArray<string>Restrictions);
public sealed record InterchangeProvenance(string SourceUri,string ImporterIdentity,string Provider,ImmutableArray<string>ReadSet,InterchangeLicense License);
