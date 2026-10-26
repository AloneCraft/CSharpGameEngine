using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
using A=Assimp;
namespace CSharpGameEngine.Import.Fbx;
public sealed record CompositeMaterial(string Name,Vector4 BaseColor,Vector3 Emissive,string?ColorImage,string?NormalImage,string?EmissionImage);
public sealed record CompositeImage(string Name,byte[]Encoded,int Width,int Height,byte[]Rgba);
public sealed record FbxCompositeData(ImmutableArray<ModelNodeData>Nodes,ImmutableArray<ModelMeshData>Meshes,
    ImmutableArray<CompositeMaterial>Materials,ModelSkeletonData?Skeleton,ImmutableArray<ModelSkinData>Skins,
    ImmutableArray<ModelAnimationClipData>Clips,ImmutableArray<CompositeImage>EmbeddedImages);
/// <summary>複合FBXアダプター。アニメーション専用ファイルを含め、返す値はすべてエンジンに属する。</summary>
public static class FbxCompositeImporter {
    public static FbxCompositeData Import(string Path,ModelImportSettings Settings){
        if(!System.IO.Path.GetExtension(Path).Equals(".fbx",StringComparison.OrdinalIgnoreCase)){ throw new ArgumentException("Expected FBX."); }
        return ConvertScene(FbxImporter.ReadCompositeScene(Path),Settings);
    }
    [ExternalNumericAdapter]
    internal static FbxCompositeData ConvertScene(A.Scene Scene,ModelImportSettings Settings){
        if(Scene.RootNode is null){ throw new InvalidDataException("MODEL_ROOT: FBX has no root node."); }
        _=ModelImportSettings.FromOptions(Settings.ToOptions());
        var SourceNodes=new List<A.Node>();var Parents=new List<int>();var Names=new List<string>();var Used=new HashSet<string>(StringComparer.Ordinal);
        void Visit(A.Node Node,int Parent){
            if(SourceNodes.Count>=65535){ throw new InvalidDataException("MODEL_LIMIT: maximum 65535 nodes."); }
            var Index=SourceNodes.Count;SourceNodes.Add(Node);Parents.Add(Parent);
            var Base=string.IsNullOrWhiteSpace(Node.Name)?"@node:"+Index:Node.Name.Normalize();var Name=Base;var N=2;while(!Used.Add(Name)){ Name=Base+"~"+N++; }Names.Add(Name);
            foreach(var Child in Node.Children){ Visit(Child,Index); }
        }
        Visit(Scene.RootNode,-1);
        int NodeIndex(string Name){
            var Found=SourceNodes.Select((Node,Index)=>(Node,Index)).Where(P=>P.Node.Name==Name).ToArray();
            if(Found.Length!=1){ throw new InvalidDataException("MODEL_TARGET: missing or ambiguous bone/animation node "+Name); }return Found[0].Index;
        }
        var Unit=1f;
        if(Scene.RootNode.Metadata is not null&&Scene.RootNode.Metadata.TryGetValue("UnitScaleFactor",out var Entry)){ Unit=System.Convert.ToSingle(Entry.Data,System.Globalization.CultureInfo.InvariantCulture)*0.01f; }
        if(!float.IsFinite(Unit)||Unit<=0f){ throw new InvalidDataException("MODEL_UNIT: invalid FBX unit scale."); }
        Vector3 Position(A.Vector3D Value)=>FbxAxisConversion.ConvertVector(new(Value.X,Value.Y,Value.Z))*Unit;
        Vector3 Direction(A.Vector3D Value)=>FbxAxisConversion.ConvertVector(new(Value.X,Value.Y,Value.Z));
        Quaternion Rotation(A.Quaternion Value)=>Quaternion.Normalize(new(-Value.X,-Value.Y,Value.Z,Value.W));
        Matrix4x4 Matrix(A.Matrix4x4 Value){
            var M=new Matrix4x4(Value.A1,Value.B1,Value.C1,Value.D1,Value.A2,Value.B2,Value.C2,Value.D2,Value.A3,Value.B3,Value.C3,Value.D3,Value.A4,Value.B4,Value.C4,Value.D4);
            var Mirror=Matrix4x4.CreateScale(1f,1f,-1f);M=Mirror*M*Mirror;M.Translation*=Unit;return M;
        }
        ModelLocalTransform Local(A.Node Node){
            if(!Matrix4x4.Decompose(Matrix(Node.Transform),out var Scale,out var Q,out var T)){ throw new InvalidDataException("MODEL_TRANSFORM: FBX contains shear that requires baking."); }
            return new(T,Quaternion.Normalize(Q),Scale);
        }
        var HasRig=Settings.Skeleton&&(Scene.Meshes.Any(M=>M.HasBones)||Scene.HasAnimations);
        var Skeleton=HasRig?new ModelSkeletonData(Names[0],[..Names],[..Parents],[..SourceNodes.Select(Local)],
            [..SourceNodes.Select(Node=>(byte)(Scene.Meshes.Any(M=>M.Bones.Any(B=>B.Name==Node.Name))?1:0))],Matrix4x4.Identity):null;
        var Meshes=new List<ModelMeshData>();var MeshNames=new HashSet<string>(StringComparer.Ordinal);var Skins=new List<ModelSkinData>();
        var SkinByMesh=new Dictionary<int,int>();
        if(Settings.Meshes){
            foreach(var Mesh in Scene.Meshes){
                var Base=string.IsNullOrWhiteSpace(Mesh.Name)?"mesh["+Meshes.Count+"]":Mesh.Name.Normalize();var Name=Base;var Suffix=2;while(!MeshNames.Add(Name)){ Name=Base+"~"+Suffix++; }
                if(Mesh.VertexCount==0||Mesh.VertexCount>4194240){ throw new InvalidDataException("MODEL_MESH: invalid vertex count."); }
                var Positions=Mesh.Vertices.Select(Position).ToImmutableArray();var Normals=Mesh.HasNormals?Mesh.Normals.Select(Direction).ToImmutableArray():Enumerable.Repeat(Vector3.UnitY,Mesh.VertexCount).ToImmutableArray();
                var Tangents=Enumerable.Range(0,Mesh.VertexCount).Select(I=>{
                    var N=Normals[I];var T=Mesh.HasTangentBasis?Direction(Mesh.Tangents[I]):Vector3.Normalize(Vector3.Cross(MathF.Abs(N.Y)<.99f?Vector3.UnitY:Vector3.UnitX,N));
                    var Sign=Mesh.HasTangentBasis&&Vector3.Dot(Vector3.Cross(N,T),Direction(Mesh.BiTangents[I]))>0f?-1f:1f;
                    return new Vector4(T,Sign);
                }).ToImmutableArray();
                var Indices=Mesh.Faces.SelectMany(F=>F.IndexCount==3?new[]{(uint)F.Indices[0],(uint)F.Indices[2],(uint)F.Indices[1]}:throw new InvalidDataException("MODEL_TRIANGLES: triangulation failed.")).ToImmutableArray();
                ModelSkinInfluences?Influences=null;
                if(HasRig&&Mesh.HasBones){
                    var VertexWeights=Enumerable.Range(0,Mesh.VertexCount).Select(_=>new List<(ushort Joint,float Weight)>()).ToArray();
                    foreach(var Bone in Mesh.Bones){ var Joint=checked((ushort)NodeIndex(Bone.Name));foreach(var Weight in Bone.VertexWeights){ VertexWeights[Weight.VertexID].Add((Joint,Weight.Weight)); } }
                    var Joints=new ushort[Mesh.VertexCount*8];var Weights=new float[Joints.Length];
                    for(var V=0;V<Mesh.VertexCount;V++){
                        var Strong=VertexWeights[V].OrderByDescending(W=>W.Weight).ThenBy(W=>W.Joint).Take(8).ToArray();var Sum=Strong.Sum(W=>W.Weight);
                        if(Sum<=0f){ Weights[V*8]=1f;continue; }
                        for(var I=0;I<Strong.Length;I++){ Joints[V*8+I]=Strong[I].Joint;Weights[V*8+I]=Strong[I].Weight/Sum; }
                    }
                    Influences=new([..Joints],[..Weights]);
                    var Bind=Enumerable.Repeat(Matrix4x4.Identity,SourceNodes.Count).ToArray();foreach(var Bone in Mesh.Bones){ Bind[NodeIndex(Bone.Name)]=Matrix(Bone.OffsetMatrix); }
                    SkinByMesh[Meshes.Count]=Skins.Count;Skins.Add(new(Name,0,[..Enumerable.Range(0,SourceNodes.Count)],[..Bind],Matrix4x4.Identity));
                }
                var Material=Settings.Materials&&Mesh.MaterialIndex>=0&&Mesh.MaterialIndex<Scene.MaterialCount?Mesh.MaterialIndex:-1;
                Meshes.Add(new(Name,Positions,Normals,Tangents,Mesh.HasTextureCoords(0)?[[..Mesh.TextureCoordinateChannels[0].Select(U=>new Vector2(U.X,1f-U.Y))]]:[],[],Indices,
                    [new(0,Indices.Length,Material<0?-1:0)],Material<0?[]:[Material],Influences,[],[],new(Positions.Aggregate(Vector3.Min),Positions.Aggregate(Vector3.Max))));
            }
        }
        var Nodes=new List<ModelNodeData>();var NewIndex=new int[SourceNodes.Count];
        for(var I=0;I<SourceNodes.Count;I++){
            var Node=SourceNodes[I];NewIndex[I]=Nodes.Count;
            var Mesh=Settings.Meshes&&Node.MeshCount>0?Node.MeshIndices[0]:-1;
            ImmutableArray<int>Slots(int M)=>M<0||!Settings.Materials||Scene.Meshes[M].MaterialIndex<0||Scene.Meshes[M].MaterialIndex>=Scene.MaterialCount?[]:[Scene.Meshes[M].MaterialIndex];
            Nodes.Add(new(Names[I],Parents[I]<0?-1:NewIndex[Parents[I]],Local(Node),Mesh,Slots(Mesh),SkinByMesh.TryGetValue(Mesh,out var Skin)?Skin:-1,0));
            if(Settings.Meshes){ foreach(var Extra in Node.MeshIndices.Skip(1)){ Nodes.Add(new(Names[I]+"/@mesh:"+Extra,NewIndex[I],new(Vector3.Zero,Quaternion.Identity,Vector3.One),Extra,Slots(Extra),SkinByMesh.TryGetValue(Extra,out var ExtraSkin)?ExtraSkin:-1,0)); } }
        }
        var Clips=new List<ModelAnimationClipData>();var ClipNames=new HashSet<string>(StringComparer.Ordinal);
        if(Settings.Animations){
            foreach(var Animation in Scene.Animations){
                var Rate=Animation.TicksPerSecond>0?Animation.TicksPerSecond:25d;
                var Base=string.IsNullOrWhiteSpace(Animation.Name)?"take["+Clips.Count+"]":Animation.Name.Normalize();var Name=Base;var Suffix=2;while(!ClipNames.Add(Name)){ Name=Base+"~"+Suffix++; }
                var Tracks=Animation.NodeAnimationChannels.Select(Channel=>{
                    var Target=NodeIndex(Channel.NodeName);
                    ModelKeys<Vector3>?VectorKeys(IEnumerable<A.VectorKey>Values,bool Translate){
                        var Keys=Values.GroupBy(K=>(float)(K.Time/Rate)).Select(G=>G.Last()).OrderBy(K=>K.Time).ToArray();
                        return Keys.Length==0?null:new([..Keys.Select(K=>(float)(K.Time/Rate))],[..Keys.Select(K=>Translate?Position(K.Value):new Vector3(K.Value.X,K.Value.Y,K.Value.Z))],ModelInterpolation.Linear,[],[]);
                    }
                    var RotationKeys=Channel.RotationKeys.GroupBy(K=>(float)(K.Time/Rate)).Select(G=>G.Last()).OrderBy(K=>K.Time).ToArray();
                    return new ModelTransformTrack(Names[Target],NewIndex[Target],VectorKeys(Channel.PositionKeys,true),RotationKeys.Length==0?null:new([..RotationKeys.Select(K=>(float)(K.Time/Rate))],[..RotationKeys.Select(K=>Rotation(K.Value))],ModelInterpolation.Linear,[],[]),VectorKeys(Channel.ScalingKeys,false));
                }).ToImmutableArray();
                var End=MathF.Max((float)(Animation.DurationInTicks/Rate),Tracks.SelectMany(T=>(T.Translation?.Times??[]).Concat(T.Rotation?.Times??[]).Concat(T.Scale?.Times??[])).DefaultIfEmpty(0f).Max());
                Clips.Add(new(Name,0f,End,(float)Rate,0,Tracks,[],[],[],null));
            }
        }
        if(Settings.Animations){
            var Takes=Clips.ToArray();
            foreach(var Range in Settings.Clips){
                var Take=Takes.SingleOrDefault(T=>T.Name==Range.Take)??throw new InvalidDataException("MODEL_CLIP_RANGE: unknown take "+Range.Take);
                if(Clips.Any(C=>C.Name==Range.Name.Normalize())){ throw new InvalidDataException("MODEL_CLIP_RANGE: duplicate clip name."); }
                Clips.Add(ModelClipSplitter.Split(Take,Range));
            }
        }
        var Materials=Settings.Materials?Scene.Materials.Select((M,I)=>{
            var Color=M.HasColorDiffuse?M.ColorDiffuse:new A.Color4D(1f,1f,1f,1f);var Emission=M.HasColorEmissive?M.ColorEmissive:new A.Color4D(0f,0f,0f,1f);
            return new CompositeMaterial(string.IsNullOrWhiteSpace(M.Name)?"material["+I+"]":M.Name.Normalize(),new(Color.R,Color.G,Color.B,Color.A),new(Emission.R,Emission.G,Emission.B),M.HasTextureDiffuse?M.TextureDiffuse.FilePath:null,M.HasTextureNormal?M.TextureNormal.FilePath:null,M.HasTextureEmissive?M.TextureEmissive.FilePath:null);
        }).ToImmutableArray():[];
        var Images=Scene.Textures.Select((T,I)=>new CompositeImage("*"+I,T.HasCompressedData?T.CompressedData:[],T.Width,T.Height,
            T.HasCompressedData?[]:T.NonCompressedData.SelectMany(P=>new[]{P.R,P.G,P.B,P.A}).ToArray())).ToImmutableArray();
        return new([..Nodes],[..Meshes],Materials,Skeleton,[..Skins],[..Clips],Images);
    }
}
