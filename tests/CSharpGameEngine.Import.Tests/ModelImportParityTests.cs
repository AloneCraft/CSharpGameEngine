using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using CSharpGameEngine.Import.Gltf;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Import.Tests;
/// <summary>Migration oracle captured from the pre-M0114 source reader, not from the replacement pipeline.</summary>
public sealed class ModelImportParityTests {
    private static readonly JsonSerializerOptions JsonOptions=new() { WriteIndented=true,IncludeFields=true };
    [Fact]
    public void Templates_MatchPreMigrationGeometryMaterialsAndAnimation(){
        var Root=FindRoot();
        var Paths=Directory.EnumerateFiles(Path.Combine(Root,"templates"),"*.glb",SearchOption.AllDirectories)
            .Where(Path=>!System.IO.Path.GetRelativePath(Root,Path).Split('\\','/').Any(Segment=>Segment is "bin" or "obj" or ".git" or ".vs" or ".artifacts"))
            .OrderBy(Path=>Path.Replace('\\','/'),StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(Paths);
        var QuinnPath=Paths.First(Path=>System.IO.Path.GetFileNameWithoutExtension(Path).Contains("Quinn",StringComparison.OrdinalIgnoreCase)&&!Path.Contains("Animation",StringComparison.OrdinalIgnoreCase));
        var Quinn=GltfImporter.ImportScene(QuinnPath);
        Assert.True(Quinn.Success,QuinnPath);
        var QuinnSkin=Assert.Single(Quinn.Scene!.Skins);
        var Records=new List<object>();
        var Clips=0;
        foreach(var Path in Paths){
            var Result=GltfImporter.ImportScene(Path);
            Assert.True(Result.Success,Path+": "+string.Join(";",Result.Diagnostics.Select(D=>D.Message)));
            var Scene=Result.Scene!;
            var Skins=Scene.Skins.Select(Skin=>{
                var Skeleton=CSharpGameEngine.Runtime.Animation.Skeleton.FromImported(Skin);
                var Pose=Skeleton.Rest.ToArray();
                var Models=new Matrix4x4[Skeleton.JointCount];
                var Palette=new Matrix4x4[Skeleton.JointCount];
                Skeleton.ComputePalette(Pose,Models,Palette);
                return new { Skeleton.JointCount,RestPaletteHash=Hash(Writer=>WriteMatrices(Writer,Palette)),RestPaletteMaximumError=MaximumIdentityError(Palette) };
            }).ToArray();
            var Animation=GltfImporter.ImportAnimations(Path);
            Assert.True(Animation.Success,Path+": "+string.Join(";",Animation.Diagnostics.Select(D=>D.Message)));
            var Samples=new List<object>();
            foreach(var ImportedClip in Animation.Clips!){
                Clips++;
                foreach(var Skin in Scene.Skins.Count>0?Scene.Skins:[QuinnSkin]){
                    var Skeleton=CSharpGameEngine.Runtime.Animation.Skeleton.FromImported(Skin);
                    var Clip=AnimationClip.Bind(ImportedClip,Skeleton);
                    var Pose=new JointPose[Skeleton.JointCount];
                    var Models=new Matrix4x4[Skeleton.JointCount];
                    var Palette=new Matrix4x4[Skeleton.JointCount];
                    var Palettes=new List<string>();
                    foreach(var Time in new[]{0f,0.25f,0.5f}){
                        Clip.Sample(Skeleton,Time,false,Pose);
                        Skeleton.ComputePalette(Pose,Models,Palette);
                        Palettes.Add(Hash(Writer=>WriteMatrices(Writer,Palette)));
                    }
                    Samples.Add(new { Clip.Name,Clip.DurationSeconds,Clip.BoundTrackCount,Palettes });
                }
            }
            Records.Add(new {
                Path=System.IO.Path.GetRelativePath(Root,Path).Replace('\\','/'),
                SourceHash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path))),
                Meshes=Scene.Meshes.Select(Mesh=>new {
                    VertexCount=Mesh.Positions.Count,
                    GeometryHash=Hash(Writer=>{
                        foreach(var P in Mesh.Positions){ Write(Writer,P); }
                        foreach(var N in Mesh.Normals){ Write(Writer,N); }
                        foreach(var T in Mesh.Tangents){ Writer.Write(T.X);Writer.Write(T.Y);Writer.Write(T.Z);Writer.Write(T.W); }
                        foreach(var Uv in Mesh.Uvs){ Writer.Write(Uv.X);Writer.Write(Uv.Y); }
                        foreach(var Index in Mesh.Indices){ Writer.Write(Index); }
                        if(Mesh.Influences is{ }Influences){
                            foreach(var Joint in Influences.JointIndices){ Writer.Write(Joint); }
                            foreach(var Weight in Influences.Weights){ Writer.Write(Weight); }
                        }
                    })
                }).ToArray(),
                Materials=Scene.Materials.Select(M=>new {
                    M.BaseColorFactor,M.MetallicFactor,M.RoughnessFactor,M.EmissiveFactor,M.EmissiveStrength,M.EmissiveNits,M.EmissiveTextureLuminanceScale,M.WorldGrid,
                    BaseColor=TextureHash(M.BaseColorTextureBytes),Normal=TextureHash(M.NormalTextureBytes),Emissive=TextureHash(M.EmissiveTextureBytes),MetallicRoughness=TextureHash(M.MetallicRoughnessTextureBytes)
                }).ToArray(),
                Nodes=Scene.Nodes,Skins,Samples
            });
        }
        Assert.True(Clips>=20,$"Expected the Quinn corpus (at least 20 clips); found {Clips}.");
        var Actual=JsonSerializer.Serialize(new { Schema="cge-model-parity/1",FileCount=Paths.Length,ClipCount=Clips,Records },JsonOptions).Replace("\r\n","\n")+"\n";
        var Golden=Path.Combine(Root,"assets","TestFixtures","Import","M0114-Source-Parity.json");
        if(Environment.GetEnvironmentVariable("CGE_UPDATE_GOLDEN")=="1"){
            Directory.CreateDirectory(Path.GetDirectoryName(Golden)!);
            File.WriteAllText(Golden,Actual);
        }
        Assert.True(File.Exists(Golden),"Capture the pre-cutover oracle with CGE_UPDATE_GOLDEN=1.");
        Assert.Equal(File.ReadAllText(Golden).Replace("\r\n","\n"),Actual);
    }
    private static string?TextureHash(byte[]?Bytes)=>Bytes is null?null:Convert.ToHexStringLower(SHA256.HashData(Bytes));
    private static string Hash(Action<BinaryWriter>Write){
        using var Stream=new MemoryStream();
        using(var Writer=new BinaryWriter(Stream,System.Text.Encoding.UTF8,true)){ Write(Writer); }
        return Convert.ToHexStringLower(SHA256.HashData(Stream.ToArray()));
    }
    private static void Write(BinaryWriter Writer,Vector3 Value){ Writer.Write(Value.X);Writer.Write(Value.Y);Writer.Write(Value.Z); }
    private static void WriteMatrices(BinaryWriter Writer,Matrix4x4[]Matrices){
        foreach(var Matrix in Matrices){
            for(var Row=0;Row<4;Row++){
                for(var Column=0;Column<4;Column++){
                    Writer.Write(checked((long)MathF.Round(Matrix[Row,Column]*100000f)));
                }
            }
        }
    }
    private static float MaximumIdentityError(Matrix4x4[]Matrices){
        var Error=0f;
        foreach(var Matrix in Matrices){
            for(var Row=0;Row<4;Row++){
                for(var Column=0;Column<4;Column++){
                    Error=MathF.Max(Error,MathF.Abs(Matrix[Row,Column]-(Row==Column?1f:0f)));
                }
            }
        }
        return Error;
    }
    private static string FindRoot(){
        for(var Directory=new DirectoryInfo(AppContext.BaseDirectory);Directory is not null;Directory=Directory.Parent){
            if(File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ return Directory.FullName; }
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
