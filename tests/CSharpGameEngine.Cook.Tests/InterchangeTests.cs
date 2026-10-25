using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Interchange;
using CSharpGameEngine.Cooking;
namespace CSharpGameEngine.Cook.Tests;
public sealed class InterchangeTests {
    internal static InterchangeDocument Triangle()=>new() {
        Nodes=[new("root",-1,InterchangeTransform.Identity,0,[0],-1,-1,-1,InterchangeNodeFlags.None,[],[])],
        Meshes=[new("triangle",new(3,[Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],[],[[Vector2.Zero,Vector2.UnitX,Vector2.UnitY]],[],null,[]),[0,1,2],[new(0,3,0)],[0],[],new(Vector3.Zero,Vector3.One))],
        Materials=[new() { Name="mat" }]
    };
    [Fact]
    public void Codec_IsCanonicalBoundedAndRejectsCorruption(){
        var Input=Triangle();
        var Bytes=InterchangeCodec.Write(Input);
        var RoundTrip=InterchangeCodec.Read(Bytes);
        Assert.Equal(Input.Meshes[0].Vertices.Positions.ToArray(),RoundTrip.Meshes[0].Vertices.Positions.ToArray());
        Assert.Equal(Input.Nodes[0].Local,RoundTrip.Nodes[0].Local);
        Assert.Equal(Bytes,InterchangeCodec.Write(RoundTrip));
        Assert.Throws<InvalidDataException>(()=>InterchangeCodec.Read(Bytes.AsSpan(0,Bytes.Length-1)));
        Assert.Throws<InvalidDataException>(()=>InterchangeCodec.Read(Bytes,Bytes.Length-1));
        Bytes[^1]^=1;
        Assert.Throws<InvalidDataException>(()=>InterchangeCodec.Read(Bytes));
    }
    [Fact]
    public void Normalize_ConvertsOnceAndPreservesSignedScale(){
        var Input=Triangle();
        Input=Input with { Nodes=[Input.Nodes[0] with { Local=new(new(1f,2f,3f),Quaternion.Identity,new(-1f,2f,3f),InheritMode.Normal) }] };
        var Result=ModelImportPipeline.Normalize(Input);
        Assert.DoesNotContain(Result.Diagnostics,D=>D.Severity==ImportSeverity.Error);
        Assert.Equal(InterchangeSpaceState.Engine,Result.Document!.Space.State);
        Assert.Equal(new Vector3(1f,2f,-3f),Result.Document.Nodes[0].Local.Translation);
        Assert.Equal(new Vector3(-1f,2f,3f),Result.Document.Nodes[0].Local.Scale);
        Assert.Equal(new uint[]{0,2,1},Result.Document.Meshes[0].Indices);
        var Second=ModelImportPipeline.Normalize(Result.Document);
        Assert.Equal(InterchangeCodec.Write(Result.Document),InterchangeCodec.Write(Second.Document!));
    }
    [Fact]
    public void Validate_RejectsCycleRangeNonfiniteAndWrongWeightCount(){
        var Input=Triangle();
        Assert.Contains(InterchangeValidator.Validate(Input with { Nodes=[Input.Nodes[0] with { ParentIndex=0 }] }),D=>D.Code=="IMP103");
        Assert.Contains(InterchangeValidator.Validate(Input with { Meshes=[Input.Meshes[0] with { Indices=[0,1,8] }] }),D=>D.Code=="IMP101");
        Assert.Contains(InterchangeValidator.Validate(Input with { Nodes=[Input.Nodes[0] with { Local=new(new(float.NaN,0f,0f),Quaternion.Identity,Vector3.One,InheritMode.Normal) }] }),D=>D.Code=="IMP102");
        var Vertices=Input.Meshes[0].Vertices with { Influences=new(8,[0],[1f]) };
        Assert.Contains(InterchangeValidator.Validate(Input with { Meshes=[Input.Meshes[0] with { Vertices=Vertices }] }),D=>D.Code=="IMP101");
    }
    [Fact]
    public void Normalize_HandlesUvOriginUnitsAndRejectsLicenseRestrictions(){
        var Input=Triangle() with { Space=Triangle().Space with { MetersPerUnit=0.01f,UvOrigin=UvOrigin.BottomLeft } };
        var Result=ModelImportPipeline.Normalize(Input);
        Assert.NotNull(Result.Document);
        Assert.Equal(new Vector3(0.01f,0f,0f),Result.Document.Meshes[0].Vertices.Positions[1]);
        Assert.Equal(new Vector2(0f,1f),Result.Document.Meshes[0].Vertices.TexCoords[0][0]);
        var License=Input.Provenance.License with { Restrictions=["UE-Only"] };
        var Denied=ModelImportPipeline.Normalize(Input with { Provenance=Input.Provenance with { License=License } });
        Assert.Null(Denied.Document);
        Assert.Contains(Denied.Diagnostics,D=>D.Code=="IMP701"&&D.Severity==ImportSeverity.Error);
    }
    [Fact]
    public void ModuleContext_SharesAllThreeContractAssemblies(){
        var Module=typeof(ContractProbe).Assembly.Location;
        var Context=new PluginModuleLoadContext(Module);
        try {
            var Assembly=Context.LoadFromAssemblyPath(Module);
            var Probe=Assembly.GetType(typeof(ContractProbe).FullName!)!;
            var Shared=(System.Reflection.Assembly[])Probe.GetMethod(nameof(ContractProbe.Assemblies))!.Invoke(null,null)!;
            Assert.Equal(new[]{typeof(IAssetImporter).Assembly,typeof(CSharpGameEngine.Contracts.AssetId).Assembly,typeof(CSharpGameEngine.Spatial.Abstractions.ICoordinateConvention).Assembly},Shared);
            Assert.NotSame(typeof(ContractProbe).Assembly,Assembly);
        } finally { Context.Unload(); }
    }
    [Fact]
    public void Normalize_TwoCompatibleSkinsShareOnePalette(){
        var Input=Triangle();
        var Root=new InterchangeNode("joint",-1,InterchangeTransform.Identity,-1,[],-1,-1,-1,InterchangeNodeFlags.Joint,[],[]);
        var MeshNode=Input.Nodes[0] with { Name="part",ParentIndex=0,SkinIndex=0 };
        var Influences=new InterchangeInfluences(1,[0,0,0],[1f,1f,1f]);
        Input=Input with {
            Nodes=[Root,MeshNode,MeshNode with { Name="part2",SkinIndex=1 }],
            Meshes=[Input.Meshes[0] with { Vertices=Input.Meshes[0].Vertices with { Influences=Influences } }],
            Skeletons=[new("skeleton",[0],[InterchangeJointFlags.Deform],-1)],
            Skins=[new("first",0,[0],[Matrix4x4.Identity],Matrix4x4.Identity,InterchangeSkinningMethod.LinearBlend),new("second",0,[0],[Matrix4x4.Identity],Matrix4x4.Identity,InterchangeSkinningMethod.LinearBlend)]
        };
        var Result=ModelImportPipeline.Normalize(Input);
        Assert.DoesNotContain(Result.Diagnostics,D=>D.Severity==ImportSeverity.Error);
        Assert.Single(Result.Document!.Skins);
        Assert.Equal(0,Result.Document.Nodes[1].SkinIndex);
        Assert.Equal(0,Result.Document.Nodes[2].SkinIndex);
        Assert.All(Result.Document.Meshes,Mesh=>Assert.Equal(8,Mesh.Vertices.Influences!.PerVertex));
        var BadBind=Input.Skins[1] with { InverseBindMatrices=[Matrix4x4.CreateTranslation(0.01f,0f,0f)] };
        var Denied=ModelImportPipeline.Normalize(Input with { Skins=[Input.Skins[0],BadBind] });
        Assert.Null(Denied.Document);
        Assert.Contains(Denied.Diagnostics,D=>D.Code=="IMP105");
    }
    [Fact]
    public void Codec_NormalizesNegativeZeroAndKeepsOpaqueExtensionBytes(){
        var Input=Triangle();
        var NegativeZero=BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        Input=Input with {
            Nodes=[Input.Nodes[0] with { Local=InterchangeTransform.Identity with { Translation=new(NegativeZero,0f,0f) } }],
            RawExtensions=[new("test.extension",InterchangeScope.Document,0,[123,34,118,34,58,49,125])]
        };
        var Bytes=InterchangeCodec.Write(Input);
        var Result=InterchangeCodec.Read(Bytes);
        Assert.Equal(0,BitConverter.SingleToInt32Bits(Result.Nodes[0].Local.Translation.X));
        Assert.Equal(Input.RawExtensions[0].JsonUtf8.ToArray(),Result.RawExtensions[0].JsonUtf8.ToArray());
        Assert.Throws<ArgumentException>(()=>InterchangeCodec.Write(Input with { Space=Input.Space with { MetersPerUnit=float.NaN } }));
    }
    [Fact]
    public void Normalize_PreservesNonzeroClipTimeAndUsesStructuralNodeNames(){
        var Input=Triangle();
        var Keys=new InterchangeKeys<Vector3>([1f,2f],[Vector3.UnitZ,2f*Vector3.UnitZ],InterchangeInterpolation.Step,[],[]);
        Input=Input with {
            Nodes=[Input.Nodes[0] with { Name="" }],
            Clips=[new("move",1f,2f,30f,InterchangeClipFlags.None,[new(0,Keys,null,null)],[],[],[],[],null)]
        };
        var Result=ModelImportPipeline.Normalize(Input);
        Assert.NotNull(Result.Document);
        Assert.Equal("_0",Result.Document.Nodes[0].Name);
        Assert.Equal(1f,Result.Document.Clips[0].StartSeconds);
        Assert.Equal(2f,Result.Document.Clips[0].EndSeconds);
        Assert.Equal(InterchangeInterpolation.Step,Result.Document.Clips[0].Transforms[0].Translation!.Interpolation);
        Assert.Equal(new Vector3(0f,0f,-1f),Result.Document.Clips[0].Transforms[0].Translation!.Values[0]);
        Assert.Contains(Result.Diagnostics,D=>D.Code=="IMP150");
    }
    [Fact]
    public void InterchangeTypeGraph_ContainsOnlyBclCookingAndContractsValues(){
        var Pending=new Stack<Type>();
        var Seen=new HashSet<Type>();
        Pending.Push(typeof(InterchangeDocument));
        while(Pending.TryPop(out var Type)){
            if(!Seen.Add(Type)){ continue; }
            var Assembly=Type.Assembly.GetName().Name!;
            Assert.True(Assembly.StartsWith("System",StringComparison.Ordinal)||Assembly is "CSharpGameEngine.Cooking" or "CSharpGameEngine.Contracts",Type.FullName);
            if(Type.IsGenericType){ foreach(var Argument in Type.GetGenericArguments()){ Pending.Push(Argument); } }
            if(Type.IsArray){ Pending.Push(Type.GetElementType()!); }
            if(Type.Namespace!="CSharpGameEngine.Interchange"){ continue; }
            foreach(var Property in Type.GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance)){
                Pending.Push(Property.PropertyType);
            }
        }
    }
}
public static class ContractProbe {
    public static System.Reflection.Assembly[]Assemblies()=>[typeof(IAssetImporter).Assembly,typeof(CSharpGameEngine.Contracts.AssetId).Assembly,typeof(CSharpGameEngine.Spatial.Abstractions.ICoordinateConvention).Assembly];
}
