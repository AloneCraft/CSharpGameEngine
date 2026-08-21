using System.Numerics;
using SharpGLTF.Schema2;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// Imports the first mesh/primitive of a .gltf or .glb file, including its base-color texture
/// (raw encoded bytes, undecoded) and baseColorFactor. No scene graph, multi-mesh, or full PBR
/// material support - this is a minimal vertical slice, not a general glTF importer.
/// </summary>
public static class GltfImporter {
    public static GltfImportResult Import(string FilePath){
        ArgumentException.ThrowIfNullOrWhiteSpace(FilePath);

        var FullPath=Path.GetFullPath(FilePath);
        if(!File.Exists(FullPath)){
            return Failed($"glTF file not found: {FullPath}");
        }

        ModelRoot Model;
        try {
            Model=ModelRoot.Load(FullPath);
        } catch(Exception Ex){
            // SharpGLTF has no single documented exhaustive exception list for malformed input;
            // a bad file must never escape as an unhandled exception from this importer.
            return Failed($"Failed to parse glTF file '{FullPath}': {Ex.Message}");
        }

        if(Model.LogicalMeshes.Count==0){
            return Failed("glTF file contains no meshes.");
        }

        Mesh Mesh=Model.LogicalMeshes[0];
        if(Mesh.Primitives.Count==0){
            return Failed("glTF mesh contains no primitives.");
        }

        MeshPrimitive Primitive=Mesh.Primitives[0];

        Accessor?PositionAccessor=Primitive.GetVertexAccessor("POSITION");
        if(PositionAccessor is null){
            return Failed("glTF primitive has no POSITION attribute.");
        }

        Vector3[]RawPositions=[.. PositionAccessor.AsVector3Array()];

        Accessor?NormalAccessor=Primitive.GetVertexAccessor("NORMAL");
        Vector3[]?RawNormals=NormalAccessor is null?null:[.. NormalAccessor.AsVector3Array()];
        var NormalsUsable=RawNormals is not null&&RawNormals.Length==RawPositions.Length;

        Accessor?UvAccessor=Primitive.GetVertexAccessor("TEXCOORD_0");
        Vector2[]?RawUvs=UvAccessor is null?null:[.. UvAccessor.AsVector2Array()];
        var UvsUsable=RawUvs is not null&&RawUvs.Length==RawPositions.Length;

        Material?Material=Primitive.Material;
        MaterialChannel?BaseColorChannel=Material?.FindChannel("BaseColor");
        Vector4 BaseColorFactor=BaseColorChannel?.Color??new Vector4(1f,1f,1f,1f);
        var BaseColorTextureBytes=BaseColorChannel?.Texture?.PrimaryImage is Image Image
            ?Image.Content.Content.ToArray()
            :null;

        List<(int A,int B,int C)>Triangles=[.. Primitive.GetTriangleIndices()];
        if(Triangles.Count==0){
            return Failed("glTF primitive has no triangles.");
        }

        List<GltfImportDiagnostic>Diagnostics=[];
        Vector3[]Positions;
        Vector3[]Normals;
        Vector2[]Uvs;
        uint[]Indices;

        if(!UvsUsable){
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                "TEXCOORD_0 attribute missing or inconsistent; synthesized (0,0) UV for every vertex."));
        }

        if(NormalsUsable){
            Positions=RawPositions;
            Normals=RawNormals!;
            Uvs=UvsUsable?RawUvs!:new Vector2[RawPositions.Length];
            Indices=new uint[Triangles.Count*3];

            for(var I=0;I<Triangles.Count;I++){
                Indices[(I*3)+0]=(uint)Triangles[I].A;
                Indices[(I*3)+1]=(uint)Triangles[I].B;
                Indices[(I*3)+2]=(uint)Triangles[I].C;
            }
        } else {
            // A vertex shared by multiple faces can only hold one NORMAL value, so a missing/
            // inconsistent NORMAL attribute cannot be patched in-place: every triangle is expanded
            // into 3 unshared vertices carrying one computed flat face normal. UV rides along in
            // the same expansion (driven solely by the NORMAL case) rather than a second pass.
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                $"NORMAL attribute missing or inconsistent; synthesized {Triangles.Count} flat per-face normal(s)."));

            Positions=new Vector3[Triangles.Count*3];
            Normals=new Vector3[Triangles.Count*3];
            Uvs=new Vector2[Triangles.Count*3];
            Indices=new uint[Triangles.Count*3];

            for(var I=0;I<Triangles.Count;I++){
                (var A,var B,var C)=Triangles[I];
                Vector3 Pa=RawPositions[A];
                Vector3 Pb=RawPositions[B];
                Vector3 Pc=RawPositions[C];

                Vector3 FaceNormalUnnormalized=Vector3.Cross(Pb-Pa,Pc-Pa);
                Vector3 FaceNormal=FaceNormalUnnormalized.LengthSquared()>1e-12f
                    ?Vector3.Normalize(FaceNormalUnnormalized)
                    :Vector3.UnitY;

                var BaseIndex=I*3;
                Positions[BaseIndex+0]=Pa;
                Positions[BaseIndex+1]=Pb;
                Positions[BaseIndex+2]=Pc;
                Normals[BaseIndex+0]=FaceNormal;
                Normals[BaseIndex+1]=FaceNormal;
                Normals[BaseIndex+2]=FaceNormal;
                Uvs[BaseIndex+0]=UvsUsable?RawUvs![A]:Vector2.Zero;
                Uvs[BaseIndex+1]=UvsUsable?RawUvs![B]:Vector2.Zero;
                Uvs[BaseIndex+2]=UvsUsable?RawUvs![C]:Vector2.Zero;
                Indices[BaseIndex+0]=(uint)(BaseIndex+0);
                Indices[BaseIndex+1]=(uint)(BaseIndex+1);
                Indices[BaseIndex+2]=(uint)(BaseIndex+2);
            }
        }

        ImportedMesh ImportedMesh=new(){
            Positions=Positions,
            Normals=Normals,
            Uvs=Uvs,
            BaseColorTextureBytes=BaseColorTextureBytes,
            BaseColorFactor=BaseColorFactor,
            Indices=Indices,
            Bounds=BoundingBox.FromPoints(Positions),
        };

        return GltfImportResult.Succeeded(ImportedMesh,Diagnostics);
    }

    private static GltfImportResult Failed(string Message)=>
        GltfImportResult.Failed([new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Error,Message)]);

    /// <summary>
    /// Imports every mesh-bearing and light-bearing node of a glTF scene graph, flattened into
    /// <see cref="ImportedScene.Nodes"/> with fully-resolved world transforms (see
    /// <see cref="ImportedSceneNode"/>). Unlike <see cref="Import(string)"/> this walks the whole
    /// node hierarchy and every material's Normal/MetallicRoughness channels - needed to port a
    /// multi-object level rather than preview a single asset. Existing callers of
    /// <see cref="Import(string)"/> are unaffected; this is a separate, additive entry point.
    /// </summary>
    public static GltfSceneImportResult ImportScene(string FilePath,GltfImportOptions?Options=null){
        ArgumentException.ThrowIfNullOrWhiteSpace(FilePath);
        Options??=new GltfImportOptions();
        Options.Validate();

        var FullPath=Path.GetFullPath(FilePath);
        if(!File.Exists(FullPath)){
            return SceneFailed($"glTF file not found: {FullPath}");
        }

        ModelRoot Model;
        try {
            Model=ModelRoot.Load(FullPath);
        } catch(Exception Ex){
            // Mirrors Import(string): SharpGLTF has no single documented exhaustive exception list
            // for malformed input, so a bad file must never escape as an unhandled exception.
            return SceneFailed($"Failed to parse glTF file '{FullPath}': {Ex.Message}");
        }

        if(Model.DefaultScene is null){
            return SceneFailed("glTF file has no default scene.");
        }

        List<GltfImportDiagnostic>Diagnostics=[];

        var MeshIndexByGltfMesh=new Dictionary<Mesh,List<(int PrimitiveIndex,int MeshIndex)>>();
        List<ImportedMeshData>Meshes=[];
        var MaterialIndexByGltfMaterial=new Dictionary<Material,int>();
        List<ImportedPbrMaterial>Materials=[];
        List<ImportedLight>Lights=[];
        var LightIndexByGltfLight=new Dictionary<PunctualLight,int>();
        List<ImportedSceneNode>Nodes=[];

        foreach(Node Node in Flatten(Model.DefaultScene.VisualChildren)){
            Mesh?Mesh=Node.Mesh;
            PunctualLight?Light=Node.PunctualLight;

            if(Mesh is null&&Light is null){
                continue;
            }

            int?LightIndex=null;
            if(Light is not null){
                if(!LightIndexByGltfLight.TryGetValue(Light,out var ExistingLightIndex)){
                    ExistingLightIndex=Lights.Count;
                    Lights.Add(ImportLight(Light));
                    LightIndexByGltfLight[Light]=ExistingLightIndex;
                }
                LightIndex=ExistingLightIndex;
            }

            if(Mesh is null){
                Nodes.Add(new ImportedSceneNode {
                    Name=Node.Name??string.Empty,
                    WorldTransform=GltfAxisConversion.ConvertWorldTransform(Node.WorldMatrix),
                    LightIndex=LightIndex,
                });
                continue;
            }

            if(!MeshIndexByGltfMesh.TryGetValue(Mesh,out List<(int PrimitiveIndex,int MeshIndex)>?PrimitiveMeshIndices)){
                PrimitiveMeshIndices=[];
                for(var PrimitiveIndex=0;PrimitiveIndex<Mesh.Primitives.Count;PrimitiveIndex++){
                    MeshPrimitive Primitive=Mesh.Primitives[PrimitiveIndex];
                    ImportedMeshData?MeshData=ImportPrimitiveGeometry(Primitive,Diagnostics,Mesh.Name??Node.Name??"(unnamed)");
                    if(MeshData is null){
                        continue;
                    }

                    PrimitiveMeshIndices.Add((PrimitiveIndex,Meshes.Count));
                    Meshes.Add(MeshData);
                }
                MeshIndexByGltfMesh[Mesh]=PrimitiveMeshIndices;
            }

            foreach((var PrimitiveIndex,var MeshIndex)in PrimitiveMeshIndices){
                MeshPrimitive Primitive=Mesh.Primitives[PrimitiveIndex];
                Material?Material=Primitive.Material;

                int MaterialIndex;
                if(Material is null){
                    MaterialIndex=GetOrAddDefaultMaterial(Materials,MaterialIndexByGltfMaterial);
                } else if(!MaterialIndexByGltfMaterial.TryGetValue(Material,out MaterialIndex)){
                    MaterialIndex=Materials.Count;
                    Materials.Add(ImportMaterial(Material,Options));
                    MaterialIndexByGltfMaterial[Material]=MaterialIndex;
                }

                Nodes.Add(new ImportedSceneNode {
                    Name=Node.Name??string.Empty,
                    WorldTransform=GltfAxisConversion.ConvertWorldTransform(Node.WorldMatrix),
                    MeshIndex=MeshIndex,
                    MaterialIndex=MaterialIndex,
                    LightIndex=LightIndex,
                });
            }
        }

        ImportedScene Scene=new(){
            Nodes=Nodes,
            Meshes=Meshes,
            Materials=Materials,
            Lights=Lights,
        };

        return GltfSceneImportResult.Succeeded(Scene,Diagnostics);
    }

    /// <summary>Depth-first walk of the full visual node hierarchy - top-level scene children are not guaranteed to be the only nodes (a source scene may nest transform/pivot nodes), so every descendant is visited.</summary>
    private static IEnumerable<Node>Flatten(IEnumerable<Node>Roots){
        foreach(Node Node in Roots){
            yield return Node;
            foreach(Node Descendant in Flatten(Node.VisualChildren)){
                yield return Descendant;
            }
        }
    }

    private static int GetOrAddDefaultMaterial(List<ImportedPbrMaterial>Materials,Dictionary<Material,int>MaterialIndexByGltfMaterial){
        // Keyed by null via a sentinel-free path: a primitive with no Material at all is rare in
        // practice for this importer's real content, so it's not worth a Dictionary<Material?,int>
        // (Material is a reference type but SharpGLTF never hands out a boxed "null key" instance).
        for(var I=0;I<Materials.Count;I++){
            ImportedPbrMaterial Candidate=Materials[I];
            if(Candidate is { BaseColorTextureBytes:null,NormalTextureBytes:null,MetallicRoughnessTextureBytes:null }
                &&Candidate.BaseColorFactor==new Vector4(1f,1f,1f,1f)){
                return I;
            }
        }

        Materials.Add(new ImportedPbrMaterial {
            BaseColorFactor=new Vector4(1f,1f,1f,1f),
            MetallicFactor=1f,
            RoughnessFactor=1f,
        });
        return Materials.Count-1;
    }

    private static ImportedPbrMaterial ImportMaterial(Material Material,GltfImportOptions Options){
        MaterialChannel?BaseColorChannel=Material.FindChannel("BaseColor");
        MaterialChannel?NormalChannel=Material.FindChannel("Normal");
        MaterialChannel?MetallicRoughnessChannel=Material.FindChannel("MetallicRoughness");
        MaterialChannel?EmissiveChannel=Material.FindChannel("Emissive");

        Vector3 EmissiveFactor=EmissiveChannel is { Color: var EmissiveColor }
            ?new Vector3(EmissiveColor.X,EmissiveColor.Y,EmissiveColor.Z)
            :Vector3.Zero;
        var EmissiveStrength=EmissiveChannel?.GetFactor("EmissiveStrength")??1f;
        byte[]?EmissiveTextureBytes=ExtractTextureBytes(EmissiveChannel);
        float EmissiveTextureLuminanceScale=EmissiveTextureLuminance.CalculateOrIdentity(EmissiveTextureBytes);


        return new ImportedPbrMaterial {
            BaseColorFactor=BaseColorChannel?.Color??new Vector4(1f,1f,1f,1f),
            MetallicFactor=MetallicRoughnessChannel?.GetFactor("MetallicFactor")??1f,
            RoughnessFactor=MetallicRoughnessChannel?.GetFactor("RoughnessFactor")??1f,
            EmissiveFactor=EmissiveFactor,
            EmissiveStrength=EmissiveStrength,
            EmissiveNits=EmissiveFactor*EmissiveStrength*Options.EmissiveNitsPerUnit,
            BaseColorTextureBytes=ExtractTextureBytes(BaseColorChannel),
            NormalTextureBytes=ExtractTextureBytes(NormalChannel),
            MetallicRoughnessTextureBytes=ExtractTextureBytes(MetallicRoughnessChannel),
            EmissiveTextureLuminanceScale=EmissiveTextureLuminanceScale,
            EmissiveTextureBytes=EmissiveTextureBytes,
        };
    }

    private static byte[]?ExtractTextureBytes(MaterialChannel?Channel)=>
        Channel?.Texture?.PrimaryImage is Image Image?Image.Content.Content.ToArray():null;

    private static ImportedLight ImportLight(PunctualLight Light)=>new(){
        LightType=Light.LightType switch {
            PunctualLightType.Point=>ImportedLightType.Point,
            PunctualLightType.Spot=>ImportedLightType.Spot,
            _ =>ImportedLightType.Directional,
        },
        Color=Light.Color,
        Intensity=Light.Intensity,
    };

    /// <summary>Builds one drawable's geometry from a single glTF primitive, applying the same missing-NORMAL/TEXCOORD_0 fallbacks as <see cref="Import(string)"/> plus TANGENT generation (not needed by the single-mesh preview path). Returns null (with a diagnostic) only when POSITION or triangle data is entirely absent.</summary>
    private static ImportedMeshData?ImportPrimitiveGeometry(MeshPrimitive Primitive,List<GltfImportDiagnostic>Diagnostics,string Context){
        Accessor?PositionAccessor=Primitive.GetVertexAccessor("POSITION");
        if(PositionAccessor is null){
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{Context}': primitive has no POSITION attribute; skipped."));
            return null;
        }

        Vector3[]RawPositions=[.. PositionAccessor.AsVector3Array()];

        Accessor?NormalAccessor=Primitive.GetVertexAccessor("NORMAL");
        Vector3[]?RawNormals=NormalAccessor is null?null:[.. NormalAccessor.AsVector3Array()];
        var NormalsUsable=RawNormals is not null&&RawNormals.Length==RawPositions.Length;

        Accessor?UvAccessor=Primitive.GetVertexAccessor("TEXCOORD_0");
        Vector2[]?RawUvs=UvAccessor is null?null:[.. UvAccessor.AsVector2Array()];
        var UvsUsable=RawUvs is not null&&RawUvs.Length==RawPositions.Length;

        List<(int A,int B,int C)>Triangles=[.. Primitive.GetTriangleIndices()];
        if(Triangles.Count==0){
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{Context}': primitive has no triangles; skipped."));
            return null;
        }

        Vector3[]Positions;
        Vector3[]Normals;
        Vector2[]Uvs;
        uint[]Indices;

        if(!UvsUsable){
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{Context}': TEXCOORD_0 attribute missing or inconsistent; synthesized (0,0) UV for every vertex."));
        }

        if(NormalsUsable){
            Positions=RawPositions;
            Normals=RawNormals!;
            Uvs=UvsUsable?RawUvs!:new Vector2[RawPositions.Length];
            Indices=new uint[Triangles.Count*3];

            for(var I=0;I<Triangles.Count;I++){
                Indices[(I*3)+0]=(uint)Triangles[I].A;
                Indices[(I*3)+1]=(uint)Triangles[I].B;
                Indices[(I*3)+2]=(uint)Triangles[I].C;
            }
        } else {
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{Context}': NORMAL attribute missing or inconsistent; synthesized {Triangles.Count} flat per-face normal(s)."));

            Positions=new Vector3[Triangles.Count*3];
            Normals=new Vector3[Triangles.Count*3];
            Uvs=new Vector2[Triangles.Count*3];
            Indices=new uint[Triangles.Count*3];

            for(var I=0;I<Triangles.Count;I++){
                (var A,var B,var C)=Triangles[I];
                Vector3 Pa=RawPositions[A];
                Vector3 Pb=RawPositions[B];
                Vector3 Pc=RawPositions[C];

                Vector3 FaceNormalUnnormalized=Vector3.Cross(Pb-Pa,Pc-Pa);
                Vector3 FaceNormal=FaceNormalUnnormalized.LengthSquared()>1e-12f
                    ?Vector3.Normalize(FaceNormalUnnormalized)
                    :Vector3.UnitY;

                var BaseIndex=I*3;
                Positions[BaseIndex+0]=Pa;
                Positions[BaseIndex+1]=Pb;
                Positions[BaseIndex+2]=Pc;
                Normals[BaseIndex+0]=FaceNormal;
                Normals[BaseIndex+1]=FaceNormal;
                Normals[BaseIndex+2]=FaceNormal;
                Uvs[BaseIndex+0]=UvsUsable?RawUvs![A]:Vector2.Zero;
                Uvs[BaseIndex+1]=UvsUsable?RawUvs![B]:Vector2.Zero;
                Uvs[BaseIndex+2]=UvsUsable?RawUvs![C]:Vector2.Zero;
                Indices[BaseIndex+0]=(uint)(BaseIndex+0);
                Indices[BaseIndex+1]=(uint)(BaseIndex+1);
                Indices[BaseIndex+2]=(uint)(BaseIndex+2);
            }
        }

        // From here on, mesh-local geometry moves from glTF space into engine space (ADR-0027) - a
        // single-axis mirror, so it must flip triangle winding to keep front-facing orientation
        // (GltfAxisConversion.FlipTriangleWinding) and does not affect ComputeTangents' output either
        // way (swapping a triangle's last two indices leaves its tangent/bitangent formula unchanged,
        // verified algebraically in ADR-0027).
        for(var I=0;I<Positions.Length;I++){
            Positions[I]=GltfAxisConversion.ConvertVector(Positions[I]);
            Normals[I]=GltfAxisConversion.ConvertVector(Normals[I]);
        }

        GltfAxisConversion.FlipTriangleWinding(Indices);

        Accessor?TangentAccessor=Primitive.GetVertexAccessor("TANGENT");
        Vector4[]?RawTangents=TangentAccessor is null?null:[.. TangentAccessor.AsVector4Array()];
        Vector4[]Tangents;
        if(RawTangents is not null&&RawTangents.Length==Positions.Length){
            // A source-provided TANGENT accessor is glTF-space data and must be converted
            // explicitly (xyz direction plus a handedness-sign flip - see GltfAxisConversion.ConvertTangent).
            Tangents=new Vector4[RawTangents.Length];
            for(var I=0;I<RawTangents.Length;I++){
                Tangents[I]=GltfAxisConversion.ConvertTangent(RawTangents[I]);
            }
        } else {
            // Synthesized directly from the already-converted (engine-space) positions/normals/uvs/
            // indices above, so ComputeTangents derives a naturally-correct engine-space handedness
            // sign with no separate flip needed.
            Tangents=ComputeTangents(Positions,Normals,Uvs,Indices);
        }

        return new ImportedMeshData {
            Positions=Positions,
            Normals=Normals,
            Tangents=Tangents,
            Uvs=Uvs,
            Indices=Indices,
        };
    }

    private static GltfSceneImportResult SceneFailed(string Message)=>
        GltfSceneImportResult.Failed([new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Error,Message)]);

    /// <summary>
    /// Per-vertex tangent generation (Lengyel's method: "Computing Tangent Space Basis Vectors for
    /// an Arbitrary Mesh") for primitives with no TANGENT accessor. Accumulates each triangle's UV-
    /// space tangent/bitangent onto its three vertices, then Gram-Schmidt-orthogonalizes against the
    /// (possibly-shared) vertex normal and derives the handedness sign (w) from the bitangent.
    /// </summary>
    private static Vector4[]ComputeTangents(IReadOnlyList<Vector3>Positions,IReadOnlyList<Vector3>Normals,IReadOnlyList<Vector2>Uvs,IReadOnlyList<uint>Indices){
        var VertexCount=Positions.Count;
        Vector3[]Tan=new Vector3[VertexCount];
        Vector3[]Bitan=new Vector3[VertexCount];

        for(var I=0;I+2<Indices.Count;I+=3){
            var I0=Indices[I];
            var I1=Indices[I+1];
            var I2=Indices[I+2];

            Vector3 Edge1=Positions[(int)I1]-Positions[(int)I0];
            Vector3 Edge2=Positions[(int)I2]-Positions[(int)I0];
            Vector2 DeltaUv1=Uvs[(int)I1]-Uvs[(int)I0];
            Vector2 DeltaUv2=Uvs[(int)I2]-Uvs[(int)I0];

            var Denominator=(DeltaUv1.X*DeltaUv2.Y)-(DeltaUv2.X*DeltaUv1.Y);
            if(MathF.Abs(Denominator)<1e-12f){
                continue;
            }

            var F=1f/Denominator;
            Vector3 Tangent=F*((DeltaUv2.Y*Edge1)-(DeltaUv1.Y*Edge2));
            Vector3 Bitangent=F*((DeltaUv1.X*Edge2)-(DeltaUv2.X*Edge1));

            Tan[I0]+=Tangent;Tan[I1]+=Tangent;Tan[I2]+=Tangent;
            Bitan[I0]+=Bitangent;Bitan[I1]+=Bitangent;Bitan[I2]+=Bitangent;
        }

        Vector4[]Result=new Vector4[VertexCount];
        for(var I=0;I<VertexCount;I++){
            Vector3 N=Normals[I];
            Vector3 T=Tan[I];

            Vector3 Orthogonalized=T-(N*Vector3.Dot(N,T));
            Vector3 SafeTangent=Orthogonalized.LengthSquared()>1e-12f
                ?Vector3.Normalize(Orthogonalized)
                :ArbitraryTangentFor(N);

            var Handedness=Vector3.Dot(Vector3.Cross(N,SafeTangent),Bitan[I])<0f?-1f:1f;
            Result[I]=new Vector4(SafeTangent,Handedness);
        }

        return Result;
    }

    private static Vector3 ArbitraryTangentFor(Vector3 Normal){
        Vector3 Reference=MathF.Abs(Normal.Y)<0.99f?Vector3.UnitY:Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(Reference,Normal));
    }
}
