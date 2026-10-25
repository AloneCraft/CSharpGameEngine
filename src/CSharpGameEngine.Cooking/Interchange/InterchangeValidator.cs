using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Cooking;
namespace CSharpGameEngine.Interchange;
public static class InterchangeValidator {
    public static ImmutableArray<ImportDiagnostic> Validate(InterchangeDocument Document,bool RequireEngine=false){
        ArgumentNullException.ThrowIfNull(Document);
        var Errors=ImmutableArray.CreateBuilder<ImportDiagnostic>();
        void Error(string Code,string Message){
            if(Errors.Count<256){ Errors.Add(new(ImportSeverity.Error,Code,"cge.pipeline@1",ImportPhase.Validate,Message,new(Document.Provenance.SourceUri))); }
        }
        if(Document.SchemaVersion!=1){ Error("IMP101","Unsupported interchange schema."); }
        if(!Enum.IsDefined(Document.Space.State)||!Enum.IsDefined(Document.Space.Convention)||!Enum.IsDefined(Document.Space.UvOrigin)){ Error("IMP101","Unknown interchange space convention or state."); }
        if(RequireEngine&&(Document.Space.State!=InterchangeSpaceState.Engine||Document.Space.MetersPerUnit!=1f||Document.Space.UvOrigin!=UvOrigin.TopLeft)){
            Error("IMP101","Artifact input must be in engine space, metres and top-left UVs.");
        }
        if(!float.IsFinite(Document.Space.MetersPerUnit)||Document.Space.MetersPerUnit<=0f){ Error("IMP102","MetersPerUnit must be finite and positive."); }
        if(Document.Nodes.Length>1000000||Document.Meshes.Length>65535||Document.Materials.Length>65535||Document.Textures.Length>65535||Document.Clips.Length>65535){ Error("IMP803","Interchange object limit exceeded."); }
        for(var Index=0;Index<Document.Nodes.Length;Index++){
            var Node=Document.Nodes[Index];
            if(Node.ParentIndex< -1||Node.ParentIndex>=Index){ Error("IMP103",$"Node {Index} parent must precede its child."); }
            if(!OptionalIndex(Node.MeshIndex,Document.Meshes.Length)||!OptionalIndex(Node.SkinIndex,Document.Skins.Length)||!OptionalIndex(Node.CameraIndex,Document.Cameras.Length)||!OptionalIndex(Node.LightIndex,Document.Lights.Length)||Node.MaterialSlots.Any(Slot=>!OptionalIndex(Slot,Document.Materials.Length))){ Error("IMP101",$"Node {Index} has an invalid reference."); }
            if(!Finite(Node.Local.Translation)||!Finite(Node.Local.Scale)||!Finite(Node.Local.Rotation)){ Error("IMP102",$"Node {Index} has nonfinite transform values."); }
            if(MathF.Abs(Node.Local.Rotation.LengthSquared()-1f)>1e-3f){ Error("IMP101",$"Node {Index} rotation must be normalized."); }
            if(RequireEngine&&Node.Local.Inherit!=InheritMode.Normal){ Error("IMP303",$"Node {Index} requires unsupported scale inheritance baking."); }
        }
        foreach(var Mesh in Document.Meshes){
            var V=Mesh.Vertices;
            if(V.Count<0||V.Count>4194240||Mesh.MorphTargets.Length>256){ Error("IMP803",$"Mesh '{Mesh.Name}' exceeds its limits."); }
            if(V.Positions.Length!=V.Count||(V.Normals.Length!=0&&V.Normals.Length!=V.Count)||(V.Tangents.Length!=0&&V.Tangents.Length!=V.Count)||V.TexCoords.Any(U=>U.Length!=V.Count)||V.Colors.Any(C=>C.Length!=V.Count)){
                Error("IMP101",$"Mesh '{Mesh.Name}' stream lengths disagree.");
            }
            if(Mesh.Indices.Length%3!=0||Mesh.Indices.Any(Index=>Index>=V.Count)){ Error("IMP101",$"Mesh '{Mesh.Name}' has invalid triangle indices."); }
            if(V.Positions.Any(P=>!Finite(P))||V.Normals.Any(N=>!Finite(N))||V.Tangents.Any(T=>!Finite(T))||V.TexCoords.Any(U=>U.Any(Value=>!Finite(Value)))||V.Colors.Any(C=>C.Any(Value=>!Finite(Value)))){
                Error("IMP102",$"Mesh '{Mesh.Name}' has nonfinite streams.");
            }
            foreach(var Stream in V.Custom){
                if(Stream.Components<1||Stream.Components>16||Stream.Values.Length!=(long)V.Count*Stream.Components){ Error("IMP101","Custom stream layout is invalid."); }
                if(Stream.Values.Any(Value=>!float.IsFinite(Value))){ Error("IMP102","Custom stream contains nonfinite values."); }
            }
            foreach(var Sub in Mesh.Submeshes){
                if(Sub.FirstIndex<0||Sub.IndexCount<0||Sub.IndexCount%3!=0||Sub.FirstIndex%3!=0||(long)Sub.FirstIndex+Sub.IndexCount>Mesh.Indices.Length||!OptionalIndex(Sub.MaterialSlot,Document.Materials.Length)){
                    Error("IMP101",$"Mesh '{Mesh.Name}' has an invalid submesh.");
                }
            }
            foreach(var Morph in Mesh.MorphTargets){
                var Count=Morph.SparseVertices.Length==0?V.Count:Morph.SparseVertices.Length;
                if(Morph.PositionDeltas.Length!=Count||(Morph.NormalDeltas.Length!=0&&Morph.NormalDeltas.Length!=Count)||(Morph.TangentDeltas.Length!=0&&Morph.TangentDeltas.Length!=Count)||Morph.SparseVertices.Any(Index=>!IndexIn(Index,V.Count))||Morph.SparseVertices.Distinct().Count()!=Morph.SparseVertices.Length){ Error("IMP101","Morph target has invalid sparse streams."); }
                if(!float.IsFinite(Morph.DefaultWeight)||Morph.PositionDeltas.Any(Value=>!Finite(Value))||Morph.NormalDeltas.Any(Value=>!Finite(Value))||Morph.TangentDeltas.Any(Value=>!Finite(Value))){ Error("IMP102","Morph target contains nonfinite values."); }
                foreach(var Between in Morph.InBetweens){
                    if(Between.PositionDeltas.Length!=Count||(Between.NormalDeltas.Length!=0&&Between.NormalDeltas.Length!=Count)||(Between.TangentDeltas.Length!=0&&Between.TangentDeltas.Length!=Count)){ Error("IMP101","Morph in-between has invalid streams."); }
                    if(!float.IsFinite(Between.Weight)||Between.PositionDeltas.Any(Value=>!Finite(Value))||Between.NormalDeltas.Any(Value=>!Finite(Value))||Between.TangentDeltas.Any(Value=>!Finite(Value))){ Error("IMP102","Morph in-between contains nonfinite values."); }
                }
            }
            if(V.Influences is{ }Influences){
                if(Influences.PerVertex<1||Influences.PerVertex>32||(RequireEngine&&Influences.PerVertex!=8)||Influences.Joints.Length!=(long)V.Count*Influences.PerVertex||Influences.Weights.Length!=Influences.Joints.Length){
                    Error("IMP101",$"Mesh '{Mesh.Name}' has invalid influence lengths.");
                }
                else {
                    for(var Vertex=0;Vertex<V.Count;Vertex++){
                        var Sum=0f;
                        for(var Slot=0;Slot<Influences.PerVertex;Slot++){
                            var Weight=Influences.Weights[Vertex*Influences.PerVertex+Slot];
                            if(!float.IsFinite(Weight)||Weight<0f){ Error("IMP102",$"Mesh '{Mesh.Name}' has an invalid weight."); }
                            Sum+=Weight;
                        }
                        if(RequireEngine&&MathF.Abs(Sum-1f)>1e-4f){ Error("IMP101",$"Mesh '{Mesh.Name}' weights must sum to one."); }
                    }
                }
            }
        }
        for(var Index=0;Index<Document.Skeletons.Length;Index++){
            var Skeleton=Document.Skeletons[Index];
            if(Skeleton.JointNodes.Length>65535){ Error("IMP803","Skeleton exceeds 65535 joints."); }
            if(Skeleton.JointNodes.Length!=Skeleton.JointFlags.Length||Skeleton.JointNodes.Any(Joint=>!IndexIn(Joint,Document.Nodes.Length))||Skeleton.JointNodes.Distinct().Count()!=Skeleton.JointNodes.Length||!OptionalIndex(Skeleton.RootParentNode,Document.Nodes.Length)){
                Error("IMP101",$"Skeleton {Index} has invalid joint references.");
                continue;
            }
            var JointSet=Skeleton.JointNodes.ToHashSet();
            foreach(var Joint in Skeleton.JointNodes){
                var Parent=Document.Nodes[Joint].ParentIndex;
                if(RequireEngine&&Parent>=0&&Parent!=Skeleton.RootParentNode&&!JointSet.Contains(Parent)){ Error("IMP160",$"Skeleton {Index} lacks an intermediate ancestor."); }
            }
            if(RequireEngine&&Skeleton.JointNodes.Select(Joint=>Document.Nodes[Joint].Name).Distinct(StringComparer.Ordinal).Count()!=Skeleton.JointNodes.Length){ Error("IMP104",$"Skeleton {Index} has duplicate joint names."); }
        }
        foreach(var Skin in Document.Skins){
            if(!IndexIn(Skin.SkeletonIndex,Document.Skeletons.Length)||Skin.JointMap.Length!=Skin.InverseBindMatrices.Length){ Error("IMP101",$"Skin '{Skin.Name}' has invalid skeleton or bind lengths.");continue; }
            if(Skin.JointMap.Any(Joint=>!IndexIn(Joint,Document.Skeletons[Skin.SkeletonIndex].JointNodes.Length))||Skin.InverseBindMatrices.Any(Matrix=>!Finite(Matrix))||!Finite(Skin.BindShape)){ Error("IMP101",$"Skin '{Skin.Name}' has invalid bind data."); }
            if(RequireEngine&&Skin.Method!=InterchangeSkinningMethod.LinearBlend){ Error("IMP304","Dual-quaternion skinning requires a later importer version."); }
        }
        foreach(var Node in Document.Nodes.Where(Node=>IndexIn(Node.SkinIndex,Document.Skins.Length)&&IndexIn(Node.MeshIndex,Document.Meshes.Length))){
            var Influences=Document.Meshes[Node.MeshIndex].Vertices.Influences;
            if(Influences is null||Influences.Joints.Any(Joint=>Joint>=Document.Skins[Node.SkinIndex].JointMap.Length)){ Error("IMP101","A skinned mesh has out-of-range joint indices."); }
        }
        foreach(var Texture in Document.Textures){ if(!IndexIn(Texture.ImageIndex,Document.Images.Length)){ Error("IMP101","Texture has an invalid image."); } }
        foreach(var Material in Document.Materials){
            if(!Finite(Material.BaseColorFactor)||!Finite(Material.EmissiveFactor)||!Finite(Material.EmissiveNits)||!float.IsFinite(Material.MetallicFactor)||!float.IsFinite(Material.RoughnessFactor)||!float.IsFinite(Material.EmissiveStrength)||!float.IsFinite(Material.EmissiveTextureLuminanceScale)||!float.IsFinite(Material.AlphaCutoff)){ Error("IMP102","Material contains a nonfinite value."); }
            foreach(var Ref in new[]{Material.BaseColor,Material.Normal,Material.MetallicRoughness,Material.Emissive,Material.Occlusion}){
                if(Ref is{ }Texture&&(!IndexIn(Texture.TextureIndex,Document.Textures.Length)||Texture.TexCoord<0||!Finite(Texture.Transform.Offset)||!Finite(Texture.Transform.Scale)||!float.IsFinite(Texture.Transform.Rotation)||!float.IsFinite(Texture.Strength))){ Error("IMP101","Material has an invalid texture reference or transform."); }
            }
        }
        foreach(var Camera in Document.Cameras){
            if(!float.IsFinite(Camera.NearMeters)||Camera.NearMeters<=0f||(Camera.FarMeters is{ }Far&&(!float.IsFinite(Far)||Far<=Camera.NearMeters))||!float.IsFinite(Camera.VerticalFieldOfViewTurns)||!float.IsFinite(Camera.AspectRatio)||!float.IsFinite(Camera.OrthographicHeightMeters)){ Error("IMP102","Camera has invalid projection values."); }
        }
        foreach(var Light in Document.Lights){
            if(!Finite(Light.Color)||!float.IsFinite(Light.Intensity)||Light.Intensity<0f||(Light.RangeMeters is{ }Range&&(!float.IsFinite(Range)||Range<=0f))||!float.IsFinite(Light.InnerHalfAngleTurns)||!float.IsFinite(Light.OuterHalfAngleTurns)){ Error("IMP102","Light has invalid values."); }
        }
        foreach(var Clip in Document.Clips){
            if(!float.IsFinite(Clip.StartSeconds)||!float.IsFinite(Clip.EndSeconds)||Clip.StartSeconds>Clip.EndSeconds||!float.IsFinite(Clip.SourceFramesPerSecond)||Clip.SourceFramesPerSecond<0f){ Error("IMP102",$"Clip '{Clip.Name}' has invalid timing."); }
            foreach(var Track in Clip.Transforms){
                if(!IndexIn(Track.NodeIndex,Document.Nodes.Length)){ Error("IMP101",$"Clip '{Clip.Name}' has invalid target."); }
                CheckKeys(Track.Translation,Finite,Error);CheckKeys(Track.Rotation,Finite,Error);CheckKeys(Track.Scale,Finite,Error);
            }
            foreach(var Track in Clip.MorphWeights){
                if(!IndexIn(Track.NodeIndex,Document.Nodes.Length)||Track.TargetIndex<0){ Error("IMP101","Morph track has an invalid target."); }
                CheckKeys(Track.Weights,float.IsFinite,Error);
            }
            foreach(var Curve in Clip.Curves){ CheckKeys(Curve.Keys,float.IsFinite,Error); }
            foreach(var Track in Clip.Properties){ CheckKeys(Track.Keys,Finite,Error); }
            foreach(var Event in Clip.Events){ if(!float.IsFinite(Event.TimeSeconds)){ Error("IMP102","Animation event time is nonfinite."); } }
            if(Clip.RootMotion is{ }Root){
                if(!IndexIn(Root.NodeIndex,Document.Nodes.Length)){ Error("IMP101","Root motion target is invalid."); }
                CheckKeys(Root.Translation,Finite,Error);CheckKeys(Root.Rotation,Finite,Error);
            }
        }
        foreach(var Avatar in Document.Rig.Avatars){
            if(!IndexIn(Avatar.SkeletonIndex,Document.Skeletons.Length)||!IndexIn(Avatar.RootNode,Document.Nodes.Length)||!float.IsFinite(Avatar.HipsHeightMeters)||Avatar.HipsHeightMeters<0f||Avatar.RestToReference.Any(Rotation=>!Finite(Rotation))){ Error("IMP101","Avatar contains invalid references or values.");continue; }
            if(Avatar.Bones.Any(Bone=>!Enum.IsDefined(Bone.Bone)||!IndexIn(Bone.JointIndex,Document.Skeletons[Avatar.SkeletonIndex].JointNodes.Length))||Avatar.Bones.Select(Bone=>Bone.Bone).Distinct().Count()!=Avatar.Bones.Length){ Error("IMP101","Avatar bone mapping is invalid."); }
        }
        if(RequireEngine){
            CheckNames(Document.Meshes.Select(M=>M.Name),Error);
            CheckNames(Document.Materials.Select(M=>M.Name),Error);
            CheckNames(Document.Textures.Select(M=>M.Name),Error);
            CheckNames(Document.Skeletons.Select(M=>M.Name),Error);
            CheckNames(Document.Clips.Select(M=>M.Name),Error);
        }
        return Errors.ToImmutable();
    }
    private static void CheckNames(IEnumerable<string>Names,Action<string,string>Error){
        var Set=new HashSet<string>(StringComparer.Ordinal);
        foreach(var Name in Names){ if(string.IsNullOrWhiteSpace(Name)||Name.Any(char.IsControl)||!Name.IsNormalized(System.Text.NormalizationForm.FormC)||!Set.Add(Name)){ Error("IMP010","Subasset names must be NFC, nonempty and unique per kind, without control characters."); } }
    }
    private static void CheckKeys<T>(InterchangeKeys<T>?Keys,Func<T,bool>IsFinite,Action<string,string>Error) where T:struct{
        if(Keys is null){ return; }
        if(!Enum.IsDefined(Keys.Interpolation)){ Error("IMP101","Unknown animation interpolation."); }
        if(Keys.Times.Length==0||Keys.Times.Length!=Keys.Values.Length||Keys.Times.Any(Time=>!float.IsFinite(Time))||Keys.Values.Any(Value=>!IsFinite(Value))){ Error("IMP102","Animation keys have invalid lengths or values.");return; }
        for(var Index=1;Index<Keys.Times.Length;Index++){ if(Keys.Times[Index]<=Keys.Times[Index-1]){ Error("IMP101","Animation times must strictly increase."); } }
        if(Keys.Interpolation==InterchangeInterpolation.CubicSpline&&(Keys.InTangents.Length!=Keys.Values.Length||Keys.OutTangents.Length!=Keys.Values.Length)){ Error("IMP101","Cubic keys require tangent arrays."); }
    }
    internal static bool IndexIn(int Index,int Count)=>Index>=0&&Index<Count;
    private static bool OptionalIndex(int Index,int Count)=>Index== -1||IndexIn(Index,Count);
    internal static bool Finite(Vector2 V)=>float.IsFinite(V.X)&&float.IsFinite(V.Y);
    internal static bool Finite(Vector3 V)=>float.IsFinite(V.X)&&float.IsFinite(V.Y)&&float.IsFinite(V.Z);
    internal static bool Finite(Vector4 V)=>float.IsFinite(V.X)&&float.IsFinite(V.Y)&&float.IsFinite(V.Z)&&float.IsFinite(V.W);
    internal static bool Finite(Quaternion Q)=>float.IsFinite(Q.X)&&float.IsFinite(Q.Y)&&float.IsFinite(Q.Z)&&float.IsFinite(Q.W);
    internal static bool Finite(Matrix4x4 M){
        for(var Row=0;Row<4;Row++){ for(var Column=0;Column<4;Column++){ if(!float.IsFinite(M[Row,Column])){ return false; } } }
        return true;
    }
}
