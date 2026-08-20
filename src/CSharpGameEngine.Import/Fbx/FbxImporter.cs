using System.Numerics;
using Assimp;

namespace CSharpGameEngine.Import.Fbx;

/// <summary>
/// Imports the first mesh of an .fbx file via AssimpNet (ADR-0009). No scene graph, multi-mesh,
/// or full PBR material support - minimal vertical slice, matching
/// <c>CSharpGameEngine.Import.Gltf.GltfImporter</c>'s scope for glTF.
/// </summary>
public static class FbxImporter {
    public static FbxImportResult Import(string FilePath){
        ArgumentException.ThrowIfNullOrWhiteSpace(FilePath);

        var FullPath=Path.GetFullPath(FilePath);
        if(!File.Exists(FullPath)){
            return Failed($"FBX file not found: {FullPath}");
        }

        // Assimp's own native logging (warnings/errors from the FBX parser itself, e.g. unknown
        // elements in a particular exporter's output) is collected as diagnostics rather than
        // written straight to the console, matching GltfImporter's contract - callers decide how
        // to surface diagnostics. An "Error"-level Assimp log line does not necessarily mean the
        // import failed overall (confirmed empirically); only Import()'s own explicit checks
        // below (empty scene, no vertices, no triangles) are treated as fatal.
        List<FbxImportDiagnostic>Diagnostics=[];
        LogStream LogStream=new((Msg,_)=>{
            var Trimmed=Msg.Trim();
            if(Trimmed.StartsWith("Error",StringComparison.OrdinalIgnoreCase)||
                Trimmed.StartsWith("Warn",StringComparison.OrdinalIgnoreCase)){
                Diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning, $"Assimp: {Trimmed}"));
            }
        });
        LogStream.Attach();

        using AssimpContext Context=new();
        Scene Scene;
        try {
            Scene=Context.ImportFile(
                FullPath,
                PostProcessSteps.Triangulate|PostProcessSteps.GenerateSmoothNormals|PostProcessSteps.JoinIdenticalVertices);
        } catch(Exception Ex){
            // Assimp has no single documented exhaustive exception list for malformed input; a
            // bad file must never escape as an unhandled exception from this importer.
            return Failed($"Failed to parse FBX file '{FullPath}': {Ex.Message}");
        } finally {
            LogStream.Detach();
        }

        if(Scene is null||Scene.MeshCount==0){
            return Failed("FBX file contains no meshes.",Diagnostics);
        }

        var Mesh=Scene.Meshes[0];
        if(Mesh.VertexCount==0){
            return Failed("FBX mesh has no vertices.",Diagnostics);
        }

        // AssimpNet's own default output space is right-handed/Y-up/Forward=-Z (see
        // FbxAxisConversion's doc comment); converted here into this engine's fixed
        // UnityCoordinateConvention (left-handed) per ADR-0027 - positions/normals convert
        // per-vertex (FbxAxisConversion.ConvertVector), and triangle winding is flipped below to
        // keep front-facing orientation consistent with the mirrored positions.
        var Positions=new Vector3[Mesh.VertexCount];
        for(var I=0;I<Mesh.VertexCount;I++){
            var V=Mesh.Vertices[I];
            Positions[I]=FbxAxisConversion.ConvertVector(new Vector3(V.X,V.Y,V.Z));
        }

        Vector3[]Normals;
        if(Mesh.HasNormals){
            Normals=new Vector3[Mesh.VertexCount];
            for(var I=0;I<Mesh.VertexCount;I++){
                var N=Mesh.Normals[I];
                Normals[I]=FbxAxisConversion.ConvertVector(new Vector3(N.X,N.Y,N.Z));
            }
        } else {
            Diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning,
                "Mesh has no normals even after GenerateSmoothNormals; defaulting to +Y for every vertex."));
            Normals=new Vector3[Mesh.VertexCount];
            Array.Fill(Normals,Vector3.UnitY);
        }

        Vector2[]Uvs;
        if(Mesh.HasTextureCoords(0)){
            Uvs=new Vector2[Mesh.VertexCount];
            var Channel=Mesh.TextureCoordinateChannels[0];
            for(var I=0;I<Mesh.VertexCount;I++){
                var Uv=Channel[I];
                Uvs[I]=new Vector2(Uv.X,Uv.Y);
            }
        } else {
            Diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning,
                "Mesh has no TEXCOORD channel 0; synthesized (0,0) UV for every vertex."));
            Uvs=new Vector2[Mesh.VertexCount];
        }

        List<uint>Indices=[];
        foreach(var Face in Mesh.Faces){
            if(Face.IndexCount!=3){
                // Triangulate should guarantee this; skip any degenerate leftovers defensively.
                continue;
            }

            Indices.Add((uint)Face.Indices[0]);
            Indices.Add((uint)Face.Indices[1]);
            Indices.Add((uint)Face.Indices[2]);
        }

        if(Indices.Count==0){
            return Failed("FBX mesh has no triangles after triangulation.",Diagnostics);
        }

        FbxAxisConversion.FlipTriangleWinding(Indices);

        Vector4 BaseColorFactor=new(1f,1f,1f,1f);
        if(Mesh.MaterialIndex>=0&&Mesh.MaterialIndex<Scene.MaterialCount){
            var Material=Scene.Materials[Mesh.MaterialIndex];
            if(Material.HasColorDiffuse){
                var C=Material.ColorDiffuse;
                BaseColorFactor=new Vector4(C.R,C.G,C.B,C.A);
            }
        }

        ImportedMesh ImportedMesh=new(){
            Positions=Positions,
            Normals=Normals,
            Uvs=Uvs,
            BaseColorTextureBytes=null,
            BaseColorFactor=BaseColorFactor,
            Indices=Indices,
            Bounds=BoundingBox.FromPoints(Positions),
        };

        return FbxImportResult.Succeeded(ImportedMesh,Diagnostics);
    }

    private static FbxImportResult Failed(string Message,IReadOnlyList<FbxImportDiagnostic>?Extra=null){
        List<FbxImportDiagnostic>All=Extra is null?[]:[.. Extra];
        All.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Error,Message));
        return FbxImportResult.Failed(All);
    }
}
