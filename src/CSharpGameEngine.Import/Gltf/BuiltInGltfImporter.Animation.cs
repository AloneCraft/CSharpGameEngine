using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
using SharpGLTF.Schema2;
namespace CSharpGameEngine.Import.Gltf;
public sealed partial class BuiltInGltfImporter {
    private static InterchangeInfluences?ReadInfluences(MeshPrimitive Primitive,int Count,IImportContext Context,string Source){
        if(Primitive.GetVertexAccessor("JOINTS_0") is null){ return null; }
        var Joints=new ushort[checked(Count*8)];
        var Weights=new float[Joints.Length];
        for(var Set=0;Primitive.GetVertexAccessor($"JOINTS_{Set}") is{}JointAccessor;Set++){
            if(Set>=2){ Context.Report(new(ImportSeverity.Warning,"IMP131",Identity,ImportPhase.Read,"Only the first eight influences per vertex are retained and normalized.",new(Source)));break; }
            if(Primitive.GetVertexAccessor($"WEIGHTS_{Set}") is not{}WeightAccessor||JointAccessor.Count!=Count||WeightAccessor.Count!=Count){ throw new InvalidDataException("IMP104: glTF joint and weight streams differ in length."); }
            var JointValues=JointAccessor.AsVector4Array();
            var WeightValues=WeightAccessor.AsVector4Array();
            for(var Vertex=0;Vertex<Count;Vertex++){
                for(var Component=0;Component<4;Component++){
                    var Joint=JointValues[Vertex][Component];
                    var Weight=WeightValues[Vertex][Component];
                    if(!float.IsFinite(Joint)||Joint<0f||Joint>ushort.MaxValue||Joint!=MathF.Truncate(Joint)||!float.IsFinite(Weight)||Weight<0f){ throw new InvalidDataException("IMP104: glTF has invalid joint indices or weights."); }
                    var Index=Vertex*8+Set*4+Component;
                    Joints[Index]=(ushort)Joint;Weights[Index]=Weight;
                }
            }
        }
        return new(8,[..Joints],[..Weights]);
    }
    private static (ImmutableArray<InterchangeSkeleton>Skeletons,ImmutableArray<InterchangeSkin>Skins)ReadSkins(ModelRoot Model,IReadOnlyDictionary<Node,int>NodeMap){
        var Skeletons=ImmutableArray.CreateBuilder<InterchangeSkeleton>();
        var Skins=ImmutableArray.CreateBuilder<InterchangeSkin>();
        foreach(var Skin in Model.LogicalSkins){
            if(Skin.Joints.Count==0){ throw new InvalidDataException("IMP104: glTF skin has no joints."); }
            static IEnumerable<Node>Ancestors(Node Node){ for(Node?Current=Node;Current is not null;Current=Current.VisualParent){ yield return Current; } }
            var Deform=Skin.Joints.ToHashSet();
            var Common=Ancestors(Skin.Joints[0]).FirstOrDefault(Candidate=>Skin.Joints.All(Joint=>Ancestors(Joint).Contains(Candidate)));
            var Parent=Common is not null&&Deform.Contains(Common)?Common.VisualParent:Common;
            var Closure=new HashSet<Node>();
            foreach(var Joint in Skin.Joints){ for(Node?Current=Joint;Current is not null&&Current!=Parent;Current=Current.VisualParent){ Closure.Add(Current); } }
            var Ordered=Closure.OrderBy(Joint=>NodeMap[Joint]).ToArray();
            var JointMap=Ordered.Select((Joint,Index)=>(Joint,Index)).ToDictionary(static Pair=>Pair.Joint,static Pair=>Pair.Index);
            var Flags=Ordered.Select(Joint=>(Deform.Contains(Joint)?InterchangeJointFlags.Deform:InterchangeJointFlags.Helper)
                |(Joint.VisualChildren.Any(Closure.Contains)?InterchangeJointFlags.None:InterchangeJointFlags.Leaf)).ToImmutableArray();
            var Name=Skin.Name??$"skin{Skins.Count}";
            Skeletons.Add(new(Name,[..Ordered.Select(Joint=>NodeMap[Joint])],Flags,Parent is null?-1:NodeMap[Parent]));
            Skins.Add(new(Name,Skeletons.Count-1,[..Skin.Joints.Select(Joint=>JointMap[Joint])],[..Skin.InverseBindMatrices],Matrix4x4.Identity,InterchangeSkinningMethod.LinearBlend));
        }
        return (Skeletons.ToImmutable(),Skins.ToImmutable());
    }
    private static ImmutableArray<InterchangeAnimationClip>ReadClips(ModelRoot Model,IReadOnlyDictionary<Node,int>NodeMap,IImportContext Context,string Source){
        var Clips=ImmutableArray.CreateBuilder<InterchangeAnimationClip>();
        foreach(var Animation in Model.LogicalAnimations){
            var Tracks=ImmutableArray.CreateBuilder<InterchangeTransformTrack>();
            var Duration=0f;
            foreach(var Node in Model.LogicalNodes){
                var Translation=Keys(Animation.FindTranslationChannel(Node)?.GetTranslationSampler());
                var Rotation=Keys(Animation.FindRotationChannel(Node)?.GetRotationSampler());
                var Scale=Keys(Animation.FindScaleChannel(Node)?.GetScaleSampler());
                if(Translation is null&&Rotation is null&&Scale is null){ continue; }
                Tracks.Add(new(NodeMap[Node],Translation,Rotation,Scale));
                Duration=MathF.Max(Duration,MathF.Max(Translation?.Times.LastOrDefault()??0f,MathF.Max(Rotation?.Times.LastOrDefault()??0f,Scale?.Times.LastOrDefault()??0f)));
            }
            InterchangeKeys<T>?Keys<T>(IAnimationSampler<T>?Sampler)where T:struct{
                if(Sampler is null){ return null; }
                List<(float Time,T Value)>Values;
                if(Sampler.InterpolationMode==AnimationInterpolationMode.CUBICSPLINE){
                    Context.Report(new(ImportSeverity.Warning,"IMP130",Identity,ImportPhase.Read,$"Animation '{Animation.Name}' cubic keys retain the existing linear-value playback contract.",new(Source)));
                    Values=[..Sampler.GetCubicKeys().Select(static Key=>(Key.Key,Key.Value.Value))];
                }else{ Values=[..Sampler.GetLinearKeys()]; }
                return new([..Values.Select(static Value=>Value.Time)],[..Values.Select(static Value=>Value.Value)],Sampler.InterpolationMode==AnimationInterpolationMode.STEP?InterchangeInterpolation.Step:InterchangeInterpolation.Linear,[],[]);
            }
            Clips.Add(new(Animation.Name??string.Empty,0f,Duration,0f,InterchangeClipFlags.None,Tracks.ToImmutable(),[],[],[],[],null));
        }
        return Clips.ToImmutable();
    }
}
