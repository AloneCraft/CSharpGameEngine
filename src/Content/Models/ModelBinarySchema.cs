using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Serialization.Metadata;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content.Models;
internal enum ModelBinaryKind:byte { Object,Array,Single,Int32,UInt32,UInt16,Byte,Boolean,Int128,Text }
internal sealed record ModelBinaryShape(ModelBinaryKind Kind,bool HasPresence,Type?ElementType,JsonPropertyInfo[]Members);
/// <summary>モデルv1の閉じた型一覧。項目の記述とアクセサーはJSONソース生成器が構築し、実行時の型探索は行わない。</summary>
internal static class ModelBinarySchema {
    private static readonly IReadOnlyDictionary<Type,ModelBinaryShape>Shapes=Create();
    internal static ModelBinaryShape Get(Type Type)=>Shapes.TryGetValue(Type,out var Shape)?Shape:throw new InvalidDataException($"Unsupported model schema type: {Type}.");
    private static IReadOnlyDictionary<Type,ModelBinaryShape>Create(){
        var Result=new Dictionary<Type,ModelBinaryShape>();
        Scalar<float>(ModelBinaryKind.Single);Scalar<int>(ModelBinaryKind.Int32);Scalar<uint>(ModelBinaryKind.UInt32);
        Scalar<ushort>(ModelBinaryKind.UInt16);Scalar<byte>(ModelBinaryKind.Byte);Scalar<bool>(ModelBinaryKind.Boolean);Scalar<Int128>(ModelBinaryKind.Int128);
        Scalar<ModelInterpolation>(ModelBinaryKind.Byte);Scalar<HumanoidBone>(ModelBinaryKind.Byte);
        Result.Add(typeof(string),new(ModelBinaryKind.Text,true,null,[]));
        Object<Vector2>(false,"X","Y");Object<Vector3>(false,"X","Y","Z");Object<Vector4>(false,"X","Y","Z","W");Object<Quaternion>(false,"X","Y","Z","W");
        Object<Matrix4x4>(false,"M11","M12","M13","M14","M21","M22","M23","M24","M31","M32","M33","M34","M41","M42","M43","M44");
        Object<ModelReference>(true);Object<ModelLocalTransform>(false);Object<ModelNodeData>(true);Object<ModelSkinData>(true);Object<ModelRuntimeBlock>(true);Object<ModelOriginOffset>(false);Object<ModelData>(true);
        Object<ModelSubmesh>(false);Object<ModelBounds>(false);Object<ModelSkinInfluences>(true);Object<ModelCustomStream>(true);Object<ModelMorphInBetween>(true);Object<ModelMorphTarget>(true);Object<ModelMeshData>(true);
        Object<ModelSkeletonData>(true);Object<ModelHumanBone>(false);Object<ModelAvatarData>(true);
        Object<ModelKeys<float>>(true);Object<ModelKeys<Vector3>>(true);Object<ModelKeys<Quaternion>>(true);
        Object<ModelTransformTrack>(true);Object<ModelMorphWeightTrack>(true);Object<ModelCurve>(true);Object<ModelAnimationEvent>(true);Object<ModelRootMotion>(true);Object<ModelAnimationClipData>(true);Object<ModelLicenseData>(true);
        Array<int>();Array<uint>();Array<ushort>();Array<byte>();Array<float>();Array<string>();
        Array<Vector2>();Array<Vector3>();Array<Vector4>();Array<Quaternion>();Array<Matrix4x4>();
        Array<ImmutableArray<Vector2>>();Array<ImmutableArray<Vector4>>();
        Array<ModelReference>();Array<ModelLocalTransform>();Array<ModelNodeData>();Array<ModelSkinData>();Array<ModelRuntimeBlock>();Array<ModelAvatarData>();
        Array<ModelSubmesh>();Array<ModelCustomStream>();Array<ModelMorphInBetween>();Array<ModelMorphTarget>();Array<ModelHumanBone>();
        Array<ModelTransformTrack>();Array<ModelMorphWeightTrack>();Array<ModelCurve>();Array<ModelAnimationEvent>();
        foreach(var Shape in Result.Values){
            if(Shape.ElementType is{ }Element&&!Result.ContainsKey(Element)){ throw new InvalidDataException($"Unregistered model element: {Element}."); }
            foreach(var Member in Shape.Members){ if(!Result.ContainsKey(Member.PropertyType)){ throw new InvalidDataException($"Unregistered model member: {Member.Name} ({Member.PropertyType})."); } }
        }
        return Result;
        void Scalar<T>(ModelBinaryKind Kind) where T:struct=>Result.Add(typeof(T),new(Kind,false,null,[]));
        void Array<T>()=>Result.Add(typeof(ImmutableArray<T>),new(ModelBinaryKind.Array,false,typeof(T),[]));
        void Object<T>(bool HasPresence,params string[]Names){
            var TypeInfo=ModelArtifactJsonContext.Default.GetTypeInfo(typeof(T))??throw new InvalidDataException($"Missing generated model contract: {typeof(T)}.");
            var Members=TypeInfo.Properties.Where(Member=>Member.Get is not null&&Member.Set is not null&&(Names.Length==0||Names.Contains(Member.Name,StringComparer.Ordinal))).OrderBy(Member=>Member.Name,StringComparer.Ordinal).ToArray();
            if(Members.Length==0||Names.Length!=0&&Members.Length!=Names.Length){ throw new InvalidDataException($"Incomplete generated model contract: {typeof(T)}."); }
            Result.Add(typeof(T),new(ModelBinaryKind.Object,HasPresence,null,Members));
        }
    }
}
