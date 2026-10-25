using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Blender;
using CSharpGameEngine.Spatial.Fbx;
using CSharpGameEngine.Spatial.Gltf;
using CSharpGameEngine.Spatial.Unity;
using CSharpGameEngine.Spatial.Unreal;
namespace CSharpGameEngine.Interchange;
public sealed record ModelImportPipelineResult(InterchangeDocument?Document,ImmutableArray<ImportDiagnostic>Diagnostics);
public static class ModelImportPipeline {
    public static ImmutableArray<ImportDiagnostic>ValidateEngine(InterchangeDocument Document){
        var Diagnostics=InterchangeValidator.Validate(Document,true).ToBuilder();
        if(!Diagnostics.Any(D=>D.Severity==ImportSeverity.Error)){
            ValidateBind(Document,(Severity,Code,Message)=>Diagnostics.Add(new(Severity,Code,"cge.pipeline@1",ImportPhase.Validate,Message,new(Document.Provenance.SourceUri))));
        }
        return [..Diagnostics.Take(256)];
    }
    public static ModelImportPipelineResult Normalize(InterchangeDocument Document){
        var Diagnostics=InterchangeValidator.Validate(Document).ToBuilder();
        void Report(ImportSeverity Severity,string Code,string Message){
            var Diagnostic=new ImportDiagnostic(Severity,Code,"cge.pipeline@1",ImportPhase.Normalize,Message,new(Document.Provenance.SourceUri));
            if(Diagnostics.Count<256){ Diagnostics.Add(Diagnostic); }
            else if(Severity==ImportSeverity.Error){
                for(var Index=Diagnostics.Count-1;Index>=0;Index--){ if(Diagnostics[Index].Severity!=ImportSeverity.Error){ Diagnostics[Index]=Diagnostic;break; } }
            }
        }
        if(!Document.Provenance.License.Confirmed||Document.Provenance.License.Kind=="Unknown"){ Report(ImportSeverity.Warning,"IMP703","Asset licence is unconfirmed; shipping policy must resolve it before release."); }
        foreach(var Restriction in Document.Provenance.License.Restrictions){
            if(Restriction=="UE-Only"){ Report(ImportSeverity.Error,"IMP701","This asset is restricted to Unreal Engine."); }
            if(Restriction=="UnityCompanion"){ Report(ImportSeverity.Error,"IMP702","This asset is restricted to Unity."); }
        }
        if(Diagnostics.Any(D=>D.Severity==ImportSeverity.Error)){ return new(null,[..Diagnostics.Take(256)]); }
        if(Document.Space.State==InterchangeSpaceState.Source){
            if(Document.Meshes.Any(M=>M.Vertices.Influences is{PerVertex:>8})){ Report(ImportSeverity.Warning,"IMP106","v1 keeps the first eight influence slots and renormalizes without reordering."); }
            if(Document.Clips.Any(Clip=>Clip.Transforms.Any(Track=>Track.Translation?.Interpolation==InterchangeInterpolation.CubicSpline||Track.Rotation?.Interpolation==InterchangeInterpolation.CubicSpline||Track.Scale?.Interpolation==InterchangeInterpolation.CubicSpline))){ Report(ImportSeverity.Warning,"IMP130","v1 samples cubic transform values as linear keys, preserving the existing reader behaviour."); }
            Document=Document.Space.Convention switch {
                SourceConvention.Unity=>Convert<UnityCoordinateConvention>(Document),
                SourceConvention.Gltf=>Convert<GltfCoordinateConvention>(Document),
                SourceConvention.Fbx=>Convert<FbxCoordinateConvention>(Document),
                SourceConvention.Unreal=>Convert<UnrealCoordinateConvention>(Document),
                SourceConvention.BlenderWorld=>Convert<BlenderWorldConvention>(Document),
                _=>throw new InvalidDataException("Unsupported source convention.")
            };
            var Names=NodeNames(Document.Nodes,Report);
            Document=Document with {
                Nodes=[..Document.Nodes.Select((Node,Index)=>Node with { Name=Names[Index] })],
                Meshes=Rename(Document.Meshes,M=>M.Name,(M,N)=>M with { Name=N },"mesh",Report),
                Materials=Rename(Document.Materials,M=>M.Name,(M,N)=>M with { Name=N },"material",Report),
                Textures=Rename(Document.Textures,M=>M.Name,(M,N)=>M with { Name=N },"texture",Report),
                Skeletons=Rename(Document.Skeletons,M=>M.Name,(M,N)=>M with { Name=N },"skeleton",Report),
                Clips=Rename(Document.Clips,M=>M.Name,(M,N)=>M with { Name=N },"clip",Report)
            };
        }
        foreach(var Diagnostic in InterchangeValidator.Validate(Document,true)){ Report(Diagnostic.Severity,Diagnostic.Code,Diagnostic.Message); }
        ValidateBind(Document,Report);
        if(Document.Skins.Length>1&&!Diagnostics.Any(D=>D.Severity==ImportSeverity.Error)){ Document=MergeSkins(Document,Report); }
        foreach(var Diagnostic in ValidateEngine(Document)){ Report(Diagnostic.Severity,Diagnostic.Code,Diagnostic.Message); }
        return new(Diagnostics.Any(D=>D.Severity==ImportSeverity.Error)?null:Document,[..Diagnostics.Take(256)]);
    }
    private static InterchangeDocument Convert<TSource>(InterchangeDocument Document) where TSource:struct,ICoordinateConvention{
        var Unit=Document.Space.MetersPerUnit;
        var FlipUv=Document.Space.UvOrigin==UvOrigin.BottomLeft;
        Vector3 Position(Vector3 Value)=>ConventionConversion<TSource,UnityCoordinateConvention>.Vector(Value)*Unit;
        Vector3 Direction(Vector3 Value)=>ConventionConversion<TSource,UnityCoordinateConvention>.Vector(Value);
        Quaternion Rotation(Quaternion Value)=>ConventionConversion<TSource,UnityCoordinateConvention>.Rotation(Value);
        Quaternion RestRotation(Quaternion Value){ var Q=Rotation(Value);return Q.W<0f?new(-Q.X,-Q.Y,-Q.Z,-Q.W):Q; }
        Matrix4x4 Matrix(Matrix4x4 Value){ var M=ConventionConversion<TSource,UnityCoordinateConvention>.Matrix(Value);M.Translation*=Unit;return M; }
        Vector4 Tangent(Vector4 Value){ var T=ConventionConversion<TSource,UnityCoordinateConvention>.Tangent(Value);return FlipUv?new(T.X,T.Y,T.Z,-T.W):T; }
        InterchangeTextureRef?Uv(InterchangeTextureRef?Value)=>Value is not{ }Ref||!FlipUv?Value:Ref with { Transform=Ref.Transform with {
            Offset=new(Ref.Transform.Offset.X-MathF.Sin(Ref.Transform.Rotation)*Ref.Transform.Scale.Y,1f-Ref.Transform.Offset.Y-MathF.Cos(Ref.Transform.Rotation)*Ref.Transform.Scale.Y),Rotation= -Ref.Transform.Rotation
        } };
        return Document with {
            Space=Document.Space with { State=InterchangeSpaceState.Engine,Convention=SourceConvention.Unity,MetersPerUnit=1f,UvOrigin=UvOrigin.TopLeft },
            Nodes=[..Document.Nodes.Select(Node=>Node with { Local=Node.Local with { Translation=Position(Node.Local.Translation),Rotation=RestRotation(Node.Local.Rotation),Scale=ConventionConversion<TSource,UnityCoordinateConvention>.Scale(Node.Local.Scale) } })],
            Meshes=[..Document.Meshes.Select(Mesh=>{
                var Vertices=Mesh.Vertices;
                var Positions=Vertices.Positions.Select(Position).ToImmutableArray();
                var Indices=Mesh.Indices.ToArray();
                if(ConventionConversion<TSource,UnityCoordinateConvention>.IsMirror){ for(var Index=0;Index<Indices.Length;Index+=3){ (Indices[Index+1],Indices[Index+2])=(Indices[Index+2],Indices[Index+1]); } }
                var Bounds=Positions.Length==0?default:new InterchangeBounds(Positions.Aggregate(Vector3.Min),Positions.Aggregate(Vector3.Max));
                return Mesh with {
                    Vertices=Vertices with {
                        Positions=Positions,Normals=[..Vertices.Normals.Select(Direction)],Tangents=[..Vertices.Tangents.Select(Tangent)],Influences=NormalizeInfluences(Vertices.Influences,Vertices.Count),
                        TexCoords=[..Vertices.TexCoords.Select(Set=>Set.Select(Value=>FlipUv?new Vector2(Value.X,1f-Value.Y):Value).ToImmutableArray())]
                    },
                    Indices=[..Indices],Bounds=Bounds,
                    MorphTargets=[..Mesh.MorphTargets.Select(Morph=>Morph with {
                        PositionDeltas=[..Morph.PositionDeltas.Select(Position)],NormalDeltas=[..Morph.NormalDeltas.Select(Direction)],TangentDeltas=[..Morph.TangentDeltas.Select(Direction)],
                        InBetweens=[..Morph.InBetweens.Select(Between=>Between with { PositionDeltas=[..Between.PositionDeltas.Select(Position)],NormalDeltas=[..Between.NormalDeltas.Select(Direction)],TangentDeltas=[..Between.TangentDeltas.Select(Direction)] })]
                    })]
                };
            })],
            Skins=[..Document.Skins.Select(Skin=>Skin with { InverseBindMatrices=[..Skin.InverseBindMatrices.Select(Matrix)],BindShape=Matrix(Skin.BindShape) })],
            Materials=[..Document.Materials.Select(M=>M with { BaseColor=Uv(M.BaseColor),Normal=Uv(M.Normal),MetallicRoughness=Uv(M.MetallicRoughness),Emissive=Uv(M.Emissive),Occlusion=Uv(M.Occlusion) })],
            Clips=[..Document.Clips.Select(Clip=>Clip with {
                Transforms=[..Clip.Transforms.Select(Track=>Track with { Translation=Map(Track.Translation,Position),Rotation=Hemisphere(Map(Track.Rotation,Rotation)),Scale=Map(Track.Scale,ConventionConversion<TSource,UnityCoordinateConvention>.Scale) })],
                RootMotion=Clip.RootMotion is not{ }Root?null:Root with { Translation=Map(Root.Translation,Position)!,Rotation=Hemisphere(Map(Root.Rotation,Rotation))! }
            })],
            Rig=Document.Rig with { Avatars=[..Document.Rig.Avatars.Select(Avatar=>Avatar with { HipsHeightMeters=Avatar.HipsHeightMeters*Unit,RestToReference=[..Avatar.RestToReference.Select(RestRotation)] })] },
            Cameras=[..Document.Cameras.Select(Camera=>Camera with { NearMeters=Camera.NearMeters*Unit,FarMeters=Camera.FarMeters*Unit,OrthographicHeightMeters=Camera.OrthographicHeightMeters*Unit })],
            Lights=[..Document.Lights.Select(Light=>Light with { RangeMeters=Light.RangeMeters*Unit })]
        };
    }
    private static InterchangeKeys<T>?Map<T>(InterchangeKeys<T>?Keys,Func<T,T>Convert) where T:struct=>Keys is null?null:Keys with {
        Values=[..Keys.Values.Select(Convert)],Interpolation=Keys.Interpolation==InterchangeInterpolation.CubicSpline?InterchangeInterpolation.Linear:Keys.Interpolation,InTangents=[],OutTangents=[]
    };
    private static InterchangeKeys<Quaternion>?Hemisphere(InterchangeKeys<Quaternion>?Keys){
        if(Keys is null){ return null; }
        var Values=Keys.Values.ToArray();
        for(var Index=0;Index<Values.Length;Index++){
            var Value=Values[Index];
            if(Index==0?Value.W<0f:Quaternion.Dot(Values[Index-1],Value)<0f){ Values[Index]=new(-Value.X,-Value.Y,-Value.Z,-Value.W); }
        }
        return Keys with { Values=[..Values] };
    }
    private static InterchangeInfluences?NormalizeInfluences(InterchangeInfluences?Source,int VertexCount){
        if(Source is null){ return null; }
        var Joints=new ushort[checked(VertexCount*8)];
        var Weights=new float[Joints.Length];
        for(var Vertex=0;Vertex<VertexCount;Vertex++){
            var Total=0f;
            for(var Slot=0;Slot<Math.Min(Source.PerVertex,8);Slot++){
                var From=Vertex*Source.PerVertex+Slot;
                Joints[Vertex*8+Slot]=Source.Joints[From];
                Weights[Vertex*8+Slot]=Source.Weights[From];
                Total+=Source.Weights[From];
            }
            if(Total<=0f){ Weights[Vertex*8]=1f; }
            else { for(var Slot=0;Slot<8;Slot++){ Weights[Vertex*8+Slot]/=Total; } }
        }
        return new(8,[..Joints],[..Weights]);
    }
    private static ImmutableArray<T>Rename<T>(ImmutableArray<T>Values,Func<T,string>GetName,Func<T,string,T>SetName,string Kind,Action<ImportSeverity,string,string>Report){
        var Names=NamesFor(Values.Select(GetName),Kind,Report);
        return [..Values.Select((Value,Index)=>SetName(Value,Names[Index]))];
    }
    private static string[]NamesFor(IEnumerable<string>Source,string Kind,Action<ImportSeverity,string,string>Report){
        var Seen=new HashSet<string>(StringComparer.Ordinal);
        return Source.Select((Name,Index)=>{
            var Base=string.IsNullOrWhiteSpace(Name)?$"{Kind}[{Index}]":Name.Normalize(NormalizationForm.FormC);
            if(string.IsNullOrWhiteSpace(Name)){ Report(ImportSeverity.Warning,"IMP150",$"Assigned structural name '{Base}'."); }
            var Result=Base;
            for(var Suffix=2;!Seen.Add(Result);Suffix++){ Result=Base+"~"+Suffix; }
            if(Result!=Base){ Report(ImportSeverity.Warning,"IMP151",$"Disambiguated '{Base}' as '{Result}'."); }
            return Result;
        }).ToArray();
    }
    private static string[]NodeNames(ImmutableArray<InterchangeNode>Nodes,Action<ImportSeverity,string,string>Report){
        var Siblings=new Dictionary<int,int>();
        var Names=new string[Nodes.Length];
        var Seen=new HashSet<string>(StringComparer.Ordinal);
        for(var Index=0;Index<Nodes.Length;Index++){
            var Node=Nodes[Index];
            var Ordinal=Siblings.GetValueOrDefault(Node.ParentIndex);
            Siblings[Node.ParentIndex]=Ordinal+1;
            var Base=string.IsNullOrWhiteSpace(Node.Name)?(Node.ParentIndex<0?"":Names[Node.ParentIndex]+"/")+"_"+Ordinal:Node.Name.Normalize(NormalizationForm.FormC);
            if(string.IsNullOrWhiteSpace(Node.Name)){ Report(ImportSeverity.Warning,"IMP150",$"Assigned structural node name '{Base}'."); }
            var Name=Base;
            for(var Suffix=2;!Seen.Add(Name);Suffix++){ Name=Base+"~"+Suffix; }
            if(Name!=Base){ Report(ImportSeverity.Warning,"IMP151",$"Disambiguated node '{Base}' as '{Name}'."); }
            Names[Index]=Name;
        }
        return Names;
    }
    private static void ValidateBind(InterchangeDocument Document,Action<ImportSeverity,string,string>Report){
        if(InterchangeValidator.Validate(Document,true).Any(D=>D.Severity==ImportSeverity.Error)){ return; }
        var World=new Matrix4x4[Document.Nodes.Length];
        for(var Index=0;Index<World.Length;Index++){
            var Node=Document.Nodes[Index];
            World[Index]=Node.Local.ToMatrix()*(Node.ParentIndex<0?Matrix4x4.Identity:World[Node.ParentIndex]);
        }
        foreach(var Skin in Document.Skins){
            if(Skin.BindShape!=Matrix4x4.Identity){ Report(ImportSeverity.Error,"IMP105",$"Skin '{Skin.Name}' requires bind-shape baking outside v1.");continue; }
            var Skeleton=Document.Skeletons[Skin.SkeletonIndex];
            for(var Joint=0;Joint<Skin.JointMap.Length;Joint++){
                var Palette=Skin.InverseBindMatrices[Joint]*World[Skeleton.JointNodes[Skin.JointMap[Joint]]];
                var Error=0f;
                for(var Row=0;Row<4;Row++){ for(var Column=0;Column<4;Column++){ Error=MathF.Max(Error,MathF.Abs(Palette[Row,Column]-(Row==Column?1f:0f))); } }
                if(Error>1e-4f){ Report(ImportSeverity.Error,"IMP105",$"Skin '{Skin.Name}' joint {Joint} rest palette differs from identity ({Error}).");break; }
            }
        }
    }
    private static InterchangeDocument MergeSkins(InterchangeDocument Document,Action<ImportSeverity,string,string>Report){
        if(Document.Provenance.License.Restrictions.Contains("ModificationProhibited")){
            Report(ImportSeverity.Error,"IMP705","The declared licence prohibits skin merging.");
            return Document;
        }
        var SkeletonIndices=Document.Skins.Select(Skin=>Skin.SkeletonIndex).Distinct().ToArray();
        var Skeletons=SkeletonIndices.Select(Index=>Document.Skeletons[Index]).ToArray();
        if(Skeletons.Select(Skeleton=>Skeleton.RootParentNode).Distinct().Count()!=1||Document.Skins.Any(Skin=>Skin.BindShape!=Matrix4x4.Identity)){
            Report(ImportSeverity.Error,"IMP160","Skins require compatible roots and identity bind-shape transforms.");
            return Document;
        }
        var Union=Skeletons.SelectMany(Skeleton=>Skeleton.JointNodes).Distinct().Order().ToArray();
        if(Union.Length>65535||Document.Rig.Avatars.Length>0){
            Report(ImportSeverity.Error,"IMP160","Skin union exceeds the joint limit or needs avatar remapping outside v1.");
            return Document;
        }
        var UnionIndex=Union.Select((Node,Index)=>(Node,Index)).ToDictionary(Pair=>Pair.Node,Pair=>Pair.Index);
        var InverseBind=new Dictionary<int,Matrix4x4>();
        var Mappings=new List<int[]>();
        foreach(var Skin in Document.Skins){
            var Skeleton=Document.Skeletons[Skin.SkeletonIndex];
            var Map=new int[Skin.JointMap.Length];
            for(var Joint=0;Joint<Map.Length;Joint++){
                var Node=Skeleton.JointNodes[Skin.JointMap[Joint]];
                Map[Joint]=UnionIndex[Node];
                var Matrix=Skin.InverseBindMatrices[Joint];
                if(InverseBind.TryGetValue(Node,out var Existing)){
                    for(var Row=0;Row<4;Row++){
                        for(var Column=0;Column<4;Column++){
                            if(MathF.Abs(Existing[Row,Column]-Matrix[Row,Column])>1e-4f){
                                Report(ImportSeverity.Error,"IMP160",$"Skins disagree on the inverse bind for node {Node}.");
                                return Document;
                            }
                        }
                    }
                }
                else { InverseBind.Add(Node,Matrix); }
            }
            Mappings.Add(Map);
        }
        var Meshes=Document.Meshes.ToBuilder();
        var Remapped=new Dictionary<(int Mesh,int Skin),int>();
        var Nodes=Document.Nodes.ToArray();
        for(var Index=0;Index<Nodes.Length;Index++){
            var Node=Nodes[Index];
            if(Node.SkinIndex<0||Node.MeshIndex<0){ continue; }
            var Key=(Node.MeshIndex,Node.SkinIndex);
            if(!Remapped.TryGetValue(Key,out var MeshIndex)){
                var Mesh=Document.Meshes[Node.MeshIndex];
                var Influences=Mesh.Vertices.Influences!;
                var Map=Mappings[Node.SkinIndex];
                var Updated=Mesh with { Name=Mesh.Name+".skin"+Node.SkinIndex,Vertices=Mesh.Vertices with { Influences=Influences with { Joints=[..Influences.Joints.Select(Joint=>checked((ushort)Map[Joint]))] } } };
                if(!Remapped.Keys.Any(Pair=>Pair.Mesh==Node.MeshIndex)){ MeshIndex=Node.MeshIndex;Meshes[MeshIndex]=Updated; }
                else { MeshIndex=Meshes.Count;Meshes.Add(Updated); }
                Remapped.Add(Key,MeshIndex);
            }
            Nodes[Index]=Node with { SkinIndex=0,MeshIndex=MeshIndex };
        }
        var JointFlags=Union.Select(Node=>Skeletons.Aggregate(InterchangeJointFlags.None,(Flags,Skeleton)=>{
            var Index=Skeleton.JointNodes.IndexOf(Node);
            return Index<0?Flags:Flags|Skeleton.JointFlags[Index];
        })).ToImmutableArray();
        return Document with {
            Nodes=[..Nodes],Meshes=Meshes.ToImmutable(),
            Skeletons=[new(Skeletons[0].Name,[..Union],JointFlags,Skeletons[0].RootParentNode)],
            Skins=[new(Document.Skins[0].Name,0,[..Enumerable.Range(0,Union.Length)],[..Union.Select(Node=>InverseBind.GetValueOrDefault(Node,Matrix4x4.Identity))],Matrix4x4.Identity,InterchangeSkinningMethod.LinearBlend)]
        };
    }
}
