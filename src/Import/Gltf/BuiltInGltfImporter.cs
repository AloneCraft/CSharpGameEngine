using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
using CSharpGameEngine.Import.Models;
using SharpGLTF.Schema2;
namespace CSharpGameEngine.Import.Gltf;
public sealed partial class BuiltInGltfImporter:IAssetImporter {
    internal const string Identity="cge.gltf";
    public ImporterDescriptor Descriptor=>new(Identity,1,"glTF 2.0",[".gltf",".glb"],100,"CSharpGameEngine",ImporterIsolation.PerImport,ImporterDeterminism.Deterministic,
        ImportCapabilities.Mesh|ImportCapabilities.Scene|ImportCapabilities.Materials|ImportCapabilities.Skin|ImportCapabilities.Animation,[],[],[]);
    public ImportSniffResult Sniff(ImportSniffRequest Request)=>Path.GetExtension(Request.SourcePath).ToLowerInvariant() is ".gltf" or ".glb"?ImportSniffResult.Definite:ImportSniffResult.Rejected;
    public ValueTask<ImportResult>ImportAsync(ImportRequest Request,IImportContext Context,CancellationToken CancellationToken){
        var Directory=Path.GetDirectoryName(Request.SourceUri)??string.Empty;
        var Reader=ReadContext.Create(Resource=>{
            CancellationToken.ThrowIfCancellationRequested();
            var Relative=Path.GetRelativePath(Request.ProjectDirectory,Path.GetFullPath(Path.Combine(Request.ProjectDirectory,Directory,Uri.UnescapeDataString(Resource)))).Replace('\\','/');
            using var Input=Context.OpenRead(Relative);
            using var Bytes=new MemoryStream();
            var Buffer=new byte[65536];
            int Count;
            while((Count=Input.Read(Buffer))!=0){
                CancellationToken.ThrowIfCancellationRequested();
                if(Bytes.Length+Count>256L*1024*1024){ throw new InvalidDataException("IMP101: a glTF resource exceeds 256 MiB."); }
                Bytes.Write(Buffer,0,Count);
            }
            return new ArraySegment<byte>(Bytes.ToArray());
        });
        var Model=Reader.ReadSchema2(Path.GetFileName(Request.SourceUri));
        var Materials=ReadMaterials(Model,Context,Request.SourceUri);
        var Meshes=ImmutableArray.CreateBuilder<InterchangeMesh>();
        var MeshIndices=new Dictionary<MeshPrimitive,int>();
        foreach(var Mesh in Model.LogicalMeshes){
            for(var PrimitiveIndex=0;PrimitiveIndex<Mesh.Primitives.Count;PrimitiveIndex++){
                var Primitive=Mesh.Primitives[PrimitiveIndex];
                CancellationToken.ThrowIfCancellationRequested();
                var Indices=Primitive.GetTriangleIndices().SelectMany(static Triangle=>new[]{(uint)Triangle.A,(uint)Triangle.B,(uint)Triangle.C}).ToImmutableArray();
                if(Indices.IsEmpty){ Context.Report(new(ImportSeverity.Warning,"IMP143",Identity,ImportPhase.Read,"A primitive without triangles is excluded from rendering.",new(Request.SourceUri)));continue; }
                MeshIndices.Add(Primitive,Meshes.Count);
                var Positions=Primitive.GetVertexAccessor("POSITION")?.AsVector3Array().ToImmutableArray()??throw new InvalidDataException("IMP101: a glTF primitive has no positions.");
                if(Positions.IsEmpty){ throw new InvalidDataException("IMP101: a glTF primitive has no positions."); }
                var Normals=Primitive.GetVertexAccessor("NORMAL")?.AsVector3Array().ToImmutableArray()??[];
                var Tangents=Primitive.GetVertexAccessor("TANGENT")?.AsVector4Array().ToImmutableArray()??[];
                var Uv=Primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array().ToImmutableArray()??[];
                if(UvTransformOf(Primitive.Material) is{}Transform){
                    Uv=[..Uv.Select(Transform.Apply)];
                    if(!Transform.PreservesTangentFrame){ Tangents=[]; }
                }
                var Colors=Primitive.GetVertexAccessor("COLOR_0")?.AsColorArray().ToImmutableArray()??[];
                var Vertices=new InterchangeVertexStreams(Positions.Length,Positions,Normals,Tangents,Uv.IsEmpty?[]:[Uv],Colors.IsEmpty?[]:[Colors],ReadInfluences(Primitive,Positions.Length,Context,Request.SourceUri),[]);
                var Material=Primitive.Material?.LogicalIndex??Model.LogicalMaterials.Count;
                Meshes.Add(new($"{Mesh.Name??"mesh"}/{PrimitiveIndex}",Vertices,Indices,[new(0,Indices.Length,0)],[Material],[],
                    new(Positions.Aggregate(Vector3.Min),Positions.Aggregate(Vector3.Max))));
            }
        }
        var Nodes=ImmutableArray.CreateBuilder<InterchangeNode>();
        var NodeMap=new Dictionary<Node,int>();
        foreach(var Root in (Model.DefaultScene??throw new InvalidDataException("IMP101: glTF has no default scene.")).VisualChildren){ AddNode(Root,-1,true); }
        foreach(var Node in Model.LogicalNodes){ EnsureNode(Node); }
        void EnsureNode(Node Source){
            if(NodeMap.ContainsKey(Source)){ return; }
            if(Source.VisualParent is{}Parent){ EnsureNode(Parent); }
            AddNode(Source,Source.VisualParent is{}Value?NodeMap[Value]:-1,false);
        }
        void AddNode(Node Source,int Parent,bool Render){
            if(NodeMap.ContainsKey(Source)){ return; }
            if(!Matrix4x4.Decompose(Source.LocalMatrix,out var Scale,out var Rotation,out var Translation)){ throw new InvalidDataException("IMP101: a glTF node transform cannot be represented as translation, rotation and scale."); }
            var Index=Nodes.Count;
            NodeMap.Add(Source,Index);
            IReadOnlyList<MeshPrimitive>?Primitives=Render?Source.Mesh?.Primitives.Where(MeshIndices.ContainsKey).ToArray():null;
            var Single=Primitives?.Count==1?Primitives[0]:null;
            Nodes.Add(new(Source.Name??string.Empty,Parent,new(Translation,Rotation,Scale,InheritMode.Normal),Single is null?-1:MeshIndices[Single],Single is null?[]:[Single.Material?.LogicalIndex??Model.LogicalMaterials.Count],Single is null?-1:Source.Skin?.LogicalIndex??-1,-1,-1,InterchangeNodeFlags.None,[],[]));
            if(Primitives is{Count:>1}){
                for(var PrimitiveIndex=0;PrimitiveIndex<Primitives.Count;PrimitiveIndex++){
                    var Primitive=Primitives[PrimitiveIndex];
                    Nodes.Add(new($"primitive{PrimitiveIndex}",Index,InterchangeTransform.Identity,MeshIndices[Primitive],[Primitive.Material?.LogicalIndex??Model.LogicalMaterials.Count],Source.Skin?.LogicalIndex??-1,-1,-1,InterchangeNodeFlags.None,[],[]));
                }
            }
            if(Render){ foreach(var Child in Source.VisualChildren){ AddNode(Child,Index,true); } }
        }
        var (Skeletons,Skins)=ReadSkins(Model,NodeMap);
        return ValueTask.FromResult(new ImportResult(new InterchangeDocument {
            Space=new(InterchangeSpaceState.Source,SourceConvention.Gltf,1f,AssetFacing.Unknown,ReferencePoseKind.Unknown,UvOrigin.TopLeft,default,"glTF 2.0 source coordinates"),
            Nodes=Nodes.ToImmutable(),Meshes=Meshes.ToImmutable(),Materials=Materials,
            Skeletons=Skeletons,Skins=Skins,Clips=ReadClips(Model,NodeMap,Context,Request.SourceUri),
            Images=[..Model.LogicalImages.Select(static Image=>new InterchangeImage(Image.Name??string.Empty,Image.Content.MimeType,[..Image.Content.Content.Span]))],
            Textures=[..Model.LogicalTextures.Select(static Texture=>new InterchangeTexture(Texture.Name??string.Empty,Texture.PrimaryImage.LogicalIndex,new(10497,10497,9987,9729)))]
        }));
    }
    /// <summary>The channels the runtime material samples (occlusion comes from the metallic-roughness texture).</summary>
    private static readonly string[]MappedChannels=["BaseColor","Normal","MetallicRoughness","Emissive"];
    private static GltfUvTransform?TransformOf(MaterialChannel Channel)=>Channel.TextureTransform is{}Value?new(Value.Offset,Value.Scale,Value.Rotation):null;
    /// <summary>
    /// The KHR_texture_transform baked into UV0: the first mapped channel's. The other channels keep the rest of their own mapping as
    /// <see cref="RelativeTransform"/>, which resource preparation bakes into their pixels.
    /// </summary>
    private static GltfUvTransform?UvTransformOf(Material?Material){
        if(Material is null){ return null; }
        foreach(var Name in MappedChannels){
            if(Material.FindChannel(Name) is{Texture:not null,TextureCoordinate:0}Channel){ return TransformOf(Channel); }
        }
        return null;
    }
    /// <summary>
    /// The mapping R with Own(uv) = R(Chosen(uv)), in the same form as a KHR_texture_transform: R(v) = Offset + Rot(Scale * v).
    /// It exists when the chosen mapping has no rotation: R = Own with its scale divided by the chosen scale, and its offset moved by the chosen offset.
    /// A normal map keeps its tangent frame only without rotation or mirroring. Null when R does not exist; identity when the two are the same.
    /// </summary>
    internal static GltfUvTransform?RelativeTransform(GltfUvTransform?Chosen,GltfUvTransform?Own,bool IsNormal){
        var C=Chosen??new(Vector2.Zero,Vector2.One,0f);
        var T=Own??new(Vector2.Zero,Vector2.One,0f);
        if(C==T){ return new(Vector2.Zero,Vector2.One,0f); }
        if(C.Rotation!=0f||C.Scale.X==0f||C.Scale.Y==0f){ return null; }
        var Scale=T.Scale/C.Scale;
        if(IsNormal&&(T.Rotation!=0f||Scale.X<=0f||Scale.Y<=0f)){ return null; }
        var Moved=Scale*C.Offset;
        var (Sin,Cos)=MathF.SinCos(T.Rotation);
        var Offset=T.Offset-new Vector2(Cos*Moved.X+Sin*Moved.Y,-Sin*Moved.X+Cos*Moved.Y);
        return new(Offset,Scale,T.Rotation);
    }
    private static ImmutableArray<InterchangeMaterial>ReadMaterials(ModelRoot Model,IImportContext Context,string Source){
        var Result=ImmutableArray.CreateBuilder<InterchangeMaterial>();
        foreach(var Material in Model.LogicalMaterials){
            var Base=Material.FindChannel("BaseColor");
            var Metallic=Material.FindChannel("MetallicRoughness");
            var Emissive=Material.FindChannel("Emissive");
            var Color=Emissive?.Color??Vector4.Zero;
            var Factor=new Vector3(Color.X,Color.Y,Color.Z);
            var Strength=Emissive?.GetFactor("EmissiveStrength")??1f;
            var Grid=ModelWorldGridBlock.FromGltf(Material,Context,Source);
            var Chosen=UvTransformOf(Material);
            var Unmapped=new List<string>();
            InterchangeTextureRef?Texture(MaterialChannel?Channel){
                if(Channel?.Texture is not{}Value){ return null; }
                if(Channel.Value.TextureCoordinate!=0){
                    Context.Report(new(ImportSeverity.Warning,"IMP142",Identity,ImportPhase.Read,$"Material '{Material.Name}' channel '{Channel.Value.Key}' uses an unsupported UV set and is excluded, preserving the existing renderer contract.",new(Source)));
                    return null;
                }
                var Relative=RelativeTransform(Chosen,TransformOf(Channel.Value),Channel.Value.Key=="Normal");
                if(Relative is null){ Unmapped.Add(Channel.Value.Key); }
                var Mapping=Relative??new(Vector2.Zero,Vector2.One,0f);
                return new(Value.LogicalIndex,0,new(Mapping.Offset,Mapping.Scale,Mapping.Rotation),1f);
            }
            Result.Add(new(){ Name=Material.Name??string.Empty,BaseColorFactor=Base?.Color??Vector4.One,
                MetallicFactor=Metallic?.GetFactor("MetallicFactor")??1f,RoughnessFactor=Metallic?.GetFactor("RoughnessFactor")??1f,
                EmissiveFactor=Factor,EmissiveStrength=Strength,EmissiveNits=Factor*Strength*100f,
                EmissiveTextureLuminanceScale=EmissiveTextureLuminance.CalculateOrIdentity(Emissive?.Texture?.PrimaryImage?.Content.Content.ToArray()),
                BaseColor=Texture(Base),Normal=Texture(Material.FindChannel("Normal")),MetallicRoughness=Texture(Metallic),Emissive=Texture(Emissive),Blocks=Grid is null?[]:[Grid] });
            if(Unmapped.Count>0){
                Context.Report(new(ImportSeverity.Warning,"IMP141",Identity,ImportPhase.Read,$"Material '{Material.Name}' maps {string.Join(", ",Unmapped)} with a texture transform that cannot be baked relative to UV0; those textures use UV0's mapping.",new(Source)));
            }
        }
        if(Model.LogicalMeshes.SelectMany(static Mesh=>Mesh.Primitives).Any(static Primitive=>Primitive.Material is null)){ Result.Add(new(){Name="default"}); }
        return Result.ToImmutable();
    }
}
