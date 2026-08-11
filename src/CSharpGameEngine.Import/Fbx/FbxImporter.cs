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
        LogStream logStream=new((Msg,_)=>{
            var Trimmed=Msg.Trim();
            if(Trimmed.StartsWith("Error",StringComparison.OrdinalIgnoreCase)||
                Trimmed.StartsWith("Warn",StringComparison.OrdinalIgnoreCase)){
                Diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning, $"Assimp: {Trimmed}"));
            }
        });
        logStream.Attach();

        using AssimpContext Context=new();
        Scene scene;
        try {
            scene=Context.ImportFile(
                FullPath,
                PostProcessSteps.Triangulate|PostProcessSteps.GenerateSmoothNormals|PostProcessSteps.JoinIdenticalVertices);
        } catch(Exception Ex){
            // Assimp has no single documented exhaustive exception list for malformed input; a
            // bad file must never escape as an unhandled exception from this importer.
            return Failed($"Failed to parse FBX file '{FullPath}': {Ex.Message}");
        } finally {
            logStream.Detach();
        }

        if(scene is null||scene.MeshCount==0){
            return Failed("FBX file contains no meshes.",Diagnostics);
        }

        Assimp.Mesh mesh=scene.Meshes[0];
        if(mesh.VertexCount==0){
            return Failed("FBX mesh has no vertices.",Diagnostics);
        }

        // AssimpNet's own default output space is right-handed/Y-up/Forward=-Z (see
        // FbxAxisConversion's doc comment); converted here into this engine's fixed
        // UnityCoordinateConvention (left-handed) per ADR-0027 - positions/normals convert
        // per-vertex (FbxAxisConversion.ConvertVector), and triangle winding is flipped below to
        // keep front-facing orientation consistent with the mirrored positions.
        Vector3[]Positions=new Vector3[mesh.VertexCount];
        for(var i=0;i<mesh.VertexCount;i++){
            Vector3D V=mesh.Vertices[i];
            Positions[i]=FbxAxisConversion.ConvertVector(new Vector3(V.X,V.Y,V.Z));
        }

        Vector3[]Normals;
        if(mesh.HasNormals){
            Normals=new Vector3[mesh.VertexCount];
            for(var i=0;i<mesh.VertexCount;i++){
                Vector3D N=mesh.Normals[i];
                Normals[i]=FbxAxisConversion.ConvertVector(new Vector3(N.X,N.Y,N.Z));
            }
        } else {
            Diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning,
                "Mesh has no normals even after GenerateSmoothNormals; defaulting to +Y for every vertex."));
            Normals=new Vector3[mesh.VertexCount];
            Array.Fill(Normals,Vector3.UnitY);
        }

        Vector2[]Uvs;
        if(mesh.HasTextureCoords(0)){
            Uvs=new Vector2[mesh.VertexCount];
            List<Vector3D>Channel=mesh.TextureCoordinateChannels[0];
            for(var i=0;i<mesh.VertexCount;i++){
                Vector3D Uv=Channel[i];
                Uvs[i]=new Vector2(Uv.X,Uv.Y);
            }
        } else {
            Diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning,
                "Mesh has no TEXCOORD channel 0; synthesized (0,0) UV for every vertex."));
            Uvs=new Vector2[mesh.VertexCount];
        }

        List<uint>Indices=[];
        foreach(Face face in mesh.Faces){
            if(face.IndexCount!=3){
                // Triangulate should guarantee this; skip any degenerate leftovers defensively.
                continue;
            }

            Indices.Add((uint)face.Indices[0]);
            Indices.Add((uint)face.Indices[1]);
            Indices.Add((uint)face.Indices[2]);
        }

        if(Indices.Count==0){
            return Failed("FBX mesh has no triangles after triangulation.",Diagnostics);
        }

        FbxAxisConversion.FlipTriangleWinding(Indices);

        Vector4 BaseColorFactor=new(1f,1f,1f,1f);
        if(mesh.MaterialIndex>=0&&mesh.MaterialIndex<scene.MaterialCount){
            Assimp.Material material=scene.Materials[mesh.MaterialIndex];
            if(material.HasColorDiffuse){
                Color4D C=material.ColorDiffuse;
                BaseColorFactor=new Vector4(C.R,C.G,C.B,C.A);
            }
        }

        ImportedMesh importedMesh=new(){
            Positions=Positions,
            Normals=Normals,
            Uvs=Uvs,
            BaseColorTextureBytes=null,
            BaseColorFactor=BaseColorFactor,
            Indices=Indices,
            Bounds=BoundingBox.FromPoints(Positions),
        };

        return FbxImportResult.Succeeded(importedMesh,Diagnostics);
    }

    private static FbxImportResult Failed(string Message,IReadOnlyList<FbxImportDiagnostic>?Extra=null){
        List<FbxImportDiagnostic>All=Extra is null?[]:[.. Extra];
        All.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Error,Message));
        return FbxImportResult.Failed(All);
    }
}
