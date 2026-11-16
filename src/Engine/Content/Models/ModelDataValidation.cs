using System.Numerics;
namespace CSharpGameEngine.Content.Models;
internal static class ModelDataValidation {
    internal static void Validate(object Value){
        switch(Value){
            case ModelMeshData Mesh:
                Name(Mesh.Name);
                var Count=Mesh.Positions.Length;
                Require(Count<=4194240&&Mesh.Normals.Length==Count&&Mesh.Tangents.Length==Count&&Mesh.TextureCoordinates.All(Set=>Set.Length==Count)&&Mesh.Colors.All(Set=>Set.Length==Count),"Mesh stream lengths or limits are invalid.");
                Require(Mesh.Indices.Length%3==0&&Mesh.Indices.All(Index=>Index<Count),"Mesh indices are invalid.");
                Require(Finite(Mesh.Bounds.Min)&&Finite(Mesh.Bounds.Max)&&Mesh.Bounds.Min.X<=Mesh.Bounds.Max.X&&Mesh.Bounds.Min.Y<=Mesh.Bounds.Max.Y&&Mesh.Bounds.Min.Z<=Mesh.Bounds.Max.Z,"Mesh bounds are invalid.");
                foreach(var Submesh in Mesh.Submeshes){ Require(Submesh.FirstIndex>=0&&Submesh.IndexCount>=0&&Submesh.FirstIndex%3==0&&Submesh.IndexCount%3==0&&(long)Submesh.FirstIndex+Submesh.IndexCount<=Mesh.Indices.Length&&Submesh.MaterialSlot>= -1,"Submesh range is invalid."); }
                if(Mesh.Influences is{ }Influences){
                    Require(Influences.JointIndices.Length==(long)Count*8&&Influences.Weights.Length==Influences.JointIndices.Length,"Influence lengths are invalid.");
                    for(var Vertex=0;Vertex<Count;Vertex++){
                        var Sum=0f;
                        for(var Slot=0;Slot<8;Slot++){
                            var Index=Vertex*8+Slot;
                            Require(Influences.JointIndices[Index]<65535&&float.IsFinite(Influences.Weights[Index])&&Influences.Weights[Index]>=0f,"Invalid joint index or weight.");
                            Sum+=Influences.Weights[Index];
                        }
                        Require(MathF.Abs(Sum-1f)<=1e-4f,"Weights must sum to one.");
                    }
                }
                Require(Mesh.Morphs.Length<=256,"Morph count exceeds its limit.");
                foreach(var Morph in Mesh.Morphs){
                    Name(Morph.Name);
                    var Length=Morph.SparseVertices.Length==0?Count:Morph.SparseVertices.Length;
                    Require(Morph.SparseVertices.All(Index=>Index>=0&&Index<Count)&&Morph.SparseVertices.Distinct().Count()==Morph.SparseVertices.Length,"Morph vertices are invalid.");
                    Require(Morph.Positions.Length==Length&&(Morph.Normals.Length==0||Morph.Normals.Length==Length)&&(Morph.Tangents.Length==0||Morph.Tangents.Length==Length),"Morph stream lengths are invalid.");
                    foreach(var Between in Morph.InBetweens){ Require(Between.Positions.Length==Length&&(Between.Normals.Length==0||Between.Normals.Length==Length)&&(Between.Tangents.Length==0||Between.Tangents.Length==Length),"Morph in-between stream lengths are invalid."); }
                }
                foreach(var Custom in Mesh.Custom){ Require(Custom.Components is >=1 and <=16&&Custom.Values.Length==(long)Count*Custom.Components,"Custom stream layout is invalid."); }
                break;
            case ModelData Model:
                Name(Model.Name);
                Require(Model.Nodes.Length<=1000000&&Model.Meshes.Length<=65535&&Model.Materials.Length<=65535&&Model.Skeletons.Length<=65535,"Model object limit exceeded.");
                foreach(var Reference in Model.Meshes.Concat(Model.Materials).Concat(Model.Skeletons)){ _=Reference.ToReference(); }
                for(var Index=0;Index<Model.Nodes.Length;Index++){
                    var Node=Model.Nodes[Index];
                    Require(Node.ParentIndex>= -1&&Node.ParentIndex<Index&&OptionalIndex(Node.MeshIndex,Model.Meshes.Length)&&OptionalIndex(Node.SkinIndex,Model.Skins.Length)&&Node.MaterialSlots.All(Slot=>OptionalIndex(Slot,Model.Materials.Length)),"Model node reference or parent ordering is invalid.");
                    Pose(Node.Local);
                }
                foreach(var Skin in Model.Skins){ Require(IndexIn(Skin.SkeletonIndex,Model.Skeletons.Length)&&Skin.JointMap.Length==Skin.InverseBindMatrices.Length&&Skin.JointMap.Length<=65535&&Skin.JointMap.All(Index=>Index>=0&&Index<65535),"Model skin references are invalid."); }
                foreach(var Avatar in Model.Avatars){ Require(IndexIn(Avatar.SkeletonIndex,Model.Skeletons.Length)&&IndexIn(Avatar.RootNode,Model.Nodes.Length)&&float.IsFinite(Avatar.HipsHeightMeters)&&Avatar.HipsHeightMeters>=0f,"Avatar references or height are invalid."); }
                foreach(var Block in Model.RuntimeBlocks){ Name(Block.Schema); }
                break;
            case ModelSkeletonData Skeleton:
                Name(Skeleton.Name);
                var Joints=Skeleton.JointNames.Length;
                Require(Joints<=65535&&Skeleton.ParentIndices.Length==Joints&&Skeleton.Rest.Length==Joints&&Skeleton.JointFlags.Length==Joints&&Skeleton.JointNames.Distinct(StringComparer.Ordinal).Count()==Joints,"Skeleton lengths, limit or names are invalid.");
                foreach(var Joint in Skeleton.JointNames){ Name(Joint); }
                foreach(var Rest in Skeleton.Rest){ Pose(Rest); }
                var State=new byte[Joints];
                for(var Index=0;Index<Joints;Index++){
                    var Path=new List<int>();
                    var Joint=Index;
                    while(Joint>=0){
                        Require(Joint<Joints,"Joint parent is out of range.");
                        if(State[Joint]==2){ break; }
                        Require(State[Joint]!=1,"Skeleton contains a cycle.");
                        State[Joint]=1;Path.Add(Joint);
                        var Parent=Skeleton.ParentIndices[Joint];
                        Require(Parent>= -1,"Joint parent is invalid.");
                        Joint=Parent;
                    }
                    foreach(var Visited in Path){ State[Visited]=2; }
                }
                break;
            case ModelAnimationClipData Clip:
                Name(Clip.Name);
                Require(float.IsFinite(Clip.StartSeconds)&&float.IsFinite(Clip.EndSeconds)&&Clip.StartSeconds<=Clip.EndSeconds&&float.IsFinite(Clip.SourceFramesPerSecond)&&Clip.SourceFramesPerSecond>=0f,"Clip time range is invalid.");
                Require(Clip.Tracks.Length<=65535&&Clip.Tracks.Select(Track=>Track.JointName).Distinct(StringComparer.Ordinal).Count()==Clip.Tracks.Length,"Clip tracks exceed their limit or repeat a joint.");
                foreach(var Track in Clip.Tracks){ Name(Track.JointName);Require(Track.NodeIndex>=0,"Clip target is invalid.");Keys(Track.Translation);Keys(Track.Rotation);Keys(Track.Scale); }
                foreach(var Track in Clip.MorphWeights){ Require(Track.NodeIndex>=0&&Track.TargetIndex>=0,"Morph track target is invalid.");Keys(Track.Weights); }
                foreach(var Curve in Clip.Curves){ Name(Curve.Name);Keys(Curve.Keys); }
                foreach(var Event in Clip.Events){ Require(float.IsFinite(Event.TimeSeconds),"Clip event time is nonfinite."); }
                if(Clip.RootMotion is{ }Root){ Require(Root.NodeIndex>=0,"Root motion target is invalid.");Keys(Root.Translation);Keys(Root.Rotation); }
                break;
            case ModelLicenseData License:
                Name(License.Kind);
                Require(License.Restrictions.Distinct(StringComparer.Ordinal).Count()==License.Restrictions.Length,"Duplicate licence restriction.");
                break;
            default:throw new InvalidDataException("Unknown runtime model payload type.");
        }
    }
    private static void Keys<T>(ModelKeys<T>?Value) where T:struct{
        if(Value is null){ return; }
        Require(Enum.IsDefined(Value.Interpolation)&&Value.Times.Length>0&&Value.Times.Length==Value.Values.Length&&Value.Times.All(float.IsFinite),"Invalid animation keys.");
        for(var Index=1;Index<Value.Times.Length;Index++){ Require(Value.Times[Index]>Value.Times[Index-1],"Key times must strictly increase."); }
        if(Value.Interpolation==ModelInterpolation.CubicSpline){ Require(Value.InTangents.Length==Value.Times.Length&&Value.OutTangents.Length==Value.Times.Length,"Cubic keys require tangent arrays."); }
    }
    private static void Pose(ModelLocalTransform Pose)=>Require(Finite(Pose.Translation)&&Finite(Pose.Scale)&&float.IsFinite(Pose.Rotation.LengthSquared())&&MathF.Abs(Pose.Rotation.LengthSquared()-1f)<=1e-3f,"Invalid local transform.");
    private static bool Finite(Vector3 Value)=>float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);
    private static bool IndexIn(int Value,int Count)=>Value>=0&&Value<Count;
    private static bool OptionalIndex(int Value,int Count)=>Value== -1||IndexIn(Value,Count);
    private static void Name(string Value)=>Require(!string.IsNullOrWhiteSpace(Value)&&!Value.Any(char.IsControl)&&Value.IsNormalized(System.Text.NormalizationForm.FormC),"A canonical NFC name is required.");
    private static void Require(bool Condition,string Message){ if(!Condition){ throw new InvalidDataException(Message); } }
}
