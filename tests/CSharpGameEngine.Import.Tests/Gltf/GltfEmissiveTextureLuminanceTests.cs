using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace CSharpGameEngine.Import.Gltf.Tests;

public class GltfEmissiveTextureLuminanceTests {
    [Fact]
    public void CalculateOrIdentity_RgbaSrgbPng_ReturnsMeanLinearLuminance(){
        byte[] Png=CreateRgbaPng(2,1,[
            0,0,0,255,
            255,255,255,255,
        ]);

        float Result=EmissiveTextureLuminance.CalculateOrIdentity(Png);

        Assert.Equal(0.5f,Result,precision:5);
    }


    [Fact]
    public void CalculateOrIdentity_RgbaSrgbPng_LinearizesBeforeAveraging(){
        byte[] Png=CreateRgbaPng(1,1,[128,128,128,255]);

        float Result=EmissiveTextureLuminance.CalculateOrIdentity(Png);

        // sRGB の 128 は線形空間では約 0.21586 であり、0.50196 ではない。
        Assert.Equal(0.21586f,Result,precision:5);
    }
    [Fact]
    public void CalculateOrIdentity_AbsentOrUnsupportedTexture_PreservesIdentityScale(){
        Assert.Equal(1f,EmissiveTextureLuminance.CalculateOrIdentity(null));
        Assert.Equal(1f,EmissiveTextureLuminance.CalculateOrIdentity([0xff,0xd8,0xff,0xd9]));
    }

    [Fact]
    public void ImportScene_EmissiveTexture_PreservesMeanLinearLuminanceForLighting(){
        byte[] Png=CreateRgbaPng(2,1,[
            0,0,0,255,
            255,255,255,255,
        ]);
        string FixturePath=Path.Combine(Path.GetTempPath(),$"gltf-emissive-texture-{Guid.NewGuid():N}.gltf");
        File.WriteAllText(FixturePath,CreateEmissiveTextureScene(Png));
        try {
            GltfSceneImportResult Result=GltfImporter.ImportScene(FixturePath);

            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            ImportedPbrMaterial Material=Assert.Single(Result.Scene!.Materials);
            Assert.NotNull(Material.EmissiveTextureBytes);
            Assert.Equal(0.5f,Material.EmissiveTextureLuminanceScale,precision:5);
        } finally {
            File.Delete(FixturePath);
        }
    }

    private static string CreateEmissiveTextureScene(byte[] Png){
        byte[] Geometry=CreateTriangleGeometry();
        string ImageBase64=Convert.ToBase64String(Png);
        string GeometryBase64=Convert.ToBase64String(Geometry);
        return $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ {
                "attributes": { "POSITION": 0 },
                "indices": 1,
                "material": 0
              } ] } ],
              "materials": [ {
                "pbrMetallicRoughness": { "baseColorFactor": [ 1, 1, 1, 1 ] },
                "emissiveFactor": [ 1, 1, 1 ],
                "emissiveTexture": { "index": 0 }
              } ],
              "textures": [ { "source": 0 } ],
              "images": [ { "uri": "data:image/png;base64,{{ImageBase64}}" } ],
              "buffers": [ {
                "uri": "data:application/octet-stream;base64,{{GeometryBase64}}",
                "byteLength": 42
              } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """;
    }

    private static byte[] CreateTriangleGeometry(){
        byte[] Result=new byte[42];
        float[] Positions=[0f,0f,0f,1f,0f,0f,0f,1f,0f];
        for(var Index=0;Index<Positions.Length;Index++){
            BinaryPrimitives.WriteSingleLittleEndian(Result.AsSpan(Index*4,4),Positions[Index]);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(Result.AsSpan(36,2),0);
        BinaryPrimitives.WriteUInt16LittleEndian(Result.AsSpan(38,2),1);
        BinaryPrimitives.WriteUInt16LittleEndian(Result.AsSpan(40,2),2);
        return Result;
    }

    internal static byte[] CreateRgbaPng(int Width,int Height,byte[] RgbaPixels){
        Assert.Equal(Width*Height*4,RgbaPixels.Length);
        byte[] Scanlines=new byte[(Width*4+1)*Height];
        for(var Row=0;Row<Height;Row++){
            int ScanlineOffset=Row*(Width*4+1);
            Buffer.BlockCopy(RgbaPixels,Row*Width*4,Scanlines,ScanlineOffset+1,Width*4);
        }

        byte[] Idat;
        using(var Buffer=new MemoryStream()){
            using(var Compressor=new ZLibStream(Buffer,CompressionLevel.NoCompression,leaveOpen:true)){
                Compressor.Write(Scanlines);
            }
            Idat=Buffer.ToArray();
        }

        using var Png=new MemoryStream();
        Png.Write([137,80,78,71,13,10,26,10]);
        WriteChunk(Png,"IHDR",[
            0,0,0,(byte)Width,
            0,0,0,(byte)Height,
            8,6,0,0,0,
        ]);
        WriteChunk(Png,"IDAT",Idat);
        WriteChunk(Png,"IEND",[]);
        return Png.ToArray();
    }

    private static void WriteChunk(Stream Destination,string Type,byte[] Data){
        byte[] TypeBytes=Encoding.ASCII.GetBytes(Type);
        Span<byte> Length=stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(Length,(uint)Data.Length);
        Destination.Write(Length);
        Destination.Write(TypeBytes);
        Destination.Write(Data);

        byte[] CrcInput=new byte[TypeBytes.Length+Data.Length];
        TypeBytes.CopyTo(CrcInput,0);
        Data.CopyTo(CrcInput,TypeBytes.Length);
        Span<byte> Crc=stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(Crc,ComputePngCrc32(CrcInput));
        Destination.Write(Crc);
    }

    private static uint ComputePngCrc32(ReadOnlySpan<byte> Data){
        uint Crc=uint.MaxValue;
        foreach(byte Value in Data){
            Crc^=Value;
            for(var Bit=0;Bit<8;Bit++){
                Crc=(Crc>>1)^((Crc&1)==0?0u:0xedb88320u);
            }
        }
        return ~Crc;
    }
}
