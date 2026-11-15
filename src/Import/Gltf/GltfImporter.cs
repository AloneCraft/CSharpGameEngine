using CSharpGameEngine.Lighting;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpGLTF.Schema2;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// .gltfまたは.glbファイルから最初のメッシュ／プリミティブをインポートする。ベースカラーのテクスチャ（エンコード済みバイト列のまま。デコードしない）と
/// baseColorFactorも含む。シーングラフ、複数メッシュ、完全なPBRマテリアルには対応しない。汎用glTFインポーターではなく、最小限の垂直スライスである。
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
            // SharpGLTFには、不正入力に対する網羅的な例外一覧が文書化されていない。
            // 不正ファイルによる未処理例外が、このインポーターから漏れてはならない。
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

        List<(int A,int B,int C)>Triangles=[.. Primitive.GetTriangleIndices()];
        if(Triangles.Count==0){
            return Failed("glTF primitive has no triangles.");
        }

        List<GltfImportDiagnostic>Diagnostics=[];
        var BaseColorTextureBytes=ExtractTextureBytes(BaseColorChannel,Diagnostics,"glTF primitive");
        Vector3[]Positions;
        Vector3[]Normals;
        Vector2[]Uvs;
        uint[]Indices;

        if(!UvsUsable){
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                "TEXCOORD_0 attribute missing or inconsistent; synthesized (0,0) UV for every vertex."));
        }
        ApplyUvTransform(RawUvs,UvsUsable,ResolveUvTransform(Material,Diagnostics,"glTF primitive"));

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
            // 1つの頂点を複数の面で共有すると、保持できる法線（NORMAL）は1つだけ。そのため、NORMAL属性がない、または
            // 一貫しない場合に、その場で修正することはできない。各三角形を共有しない3頂点へ展開し、
            // 面ごとに計算したフラット法線を持たせる。UVも同じ展開に合わせて引き継ぎ、
            // NORMALのケースを根拠とするため、別の処理は行わない。
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
    /// glTFシーングラフ内のメッシュを持つノードとライトを持つノードをすべてインポートし、
    /// 完全に解決したワールド変換とともに<see cref="ImportedScene.Nodes"/>へ平坦化する
    ///（<see cref="ImportedSceneNode"/>を参照）。<see cref="Import(string)"/>と異なり、ノード階層全体と、
    /// すべてのマテリアルのNormal／MetallicRoughnessチャンネルをたどる。単一アセットのプレビューではなく、複数オブジェクトのレベルを移行するために必要。
    /// 既存の<see cref="Import(string)"/>利用側には影響しない、独立した追加エントリーポイントである。
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
            // Import(string)と同様、SharpGLTFには不正入力に対する網羅的な例外一覧が文書化されていない。
            // そのため、不正ファイルによる未処理例外が漏れてはならない。
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
        List<ImportedSkin>Skins=[];
        var SkinIndexByGltfSkin=new Dictionary<Skin,int>();

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

            int?SkinIndex=null;
            if(Node.Skin is{ }GltfSkin){
                if(!SkinIndexByGltfSkin.TryGetValue(GltfSkin,out var ExistingSkinIndex)){
                    ExistingSkinIndex=Skins.Count;
                    Skins.Add(ImportSkin(GltfSkin,Diagnostics));
                    SkinIndexByGltfSkin[GltfSkin]=ExistingSkinIndex;
                }
                SkinIndex=ExistingSkinIndex;
            }

            foreach((var PrimitiveIndex,var MeshIndex)in PrimitiveMeshIndices){
                MeshPrimitive Primitive=Mesh.Primitives[PrimitiveIndex];
                Material?Material=Primitive.Material;

                int MaterialIndex;
                if(Material is null){
                    MaterialIndex=GetOrAddDefaultMaterial(Materials,MaterialIndexByGltfMaterial);
                } else if(!MaterialIndexByGltfMaterial.TryGetValue(Material,out MaterialIndex)){
                    MaterialIndex=Materials.Count;
                    Materials.Add(ImportMaterial(Material,Options,Diagnostics));
                    MaterialIndexByGltfMaterial[Material]=MaterialIndex;
                }

                Nodes.Add(new ImportedSceneNode {
                    Name=Node.Name??string.Empty,
                    WorldTransform=GltfAxisConversion.ConvertWorldTransform(Node.WorldMatrix),
                    MeshIndex=MeshIndex,
                    MaterialIndex=MaterialIndex,
                    LightIndex=LightIndex,
                    SkinIndex=SkinIndex,
                });
            }
        }

        ImportedScene Scene=new(){
            Nodes=Nodes,
            Meshes=Meshes,
            Materials=Materials,
            Lights=Lights,
            Skins=Skins,
        };

        return GltfSceneImportResult.Succeeded(Scene,Diagnostics);
    }

    /// <summary>描画ノードの階層全体を深さ優先でたどる。ソースシーンにはTransform／ピボット用の入れ子ノードがありうるため、トップレベルの子だけとは限らない。子孫をすべて走査する。</summary>
    private static IEnumerable<Node>Flatten(IEnumerable<Node>Roots){
        foreach(Node Node in Roots){
            yield return Node;
            foreach(Node Descendant in Flatten(Node.VisualChildren)){
                yield return Descendant;
            }
        }
    }

    private static int GetOrAddDefaultMaterial(List<ImportedPbrMaterial>Materials,Dictionary<Material,int>MaterialIndexByGltfMaterial){
        // ここでは特別な番兵値を使わずにnullをキーとして扱う。実際に読み込むコンテンツでMaterialのない
        // プリミティブはまれなので、Dictionary<Material?,int>を使う価値はない。
        // Materialは参照型だが、SharpGLTFがボックス化された「nullキー」インスタンスを渡すこともない。
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

    private static ImportedPbrMaterial ImportMaterial(Material Material,GltfImportOptions Options,List<GltfImportDiagnostic>Diagnostics){
        MaterialChannel?BaseColorChannel=Material.FindChannel("BaseColor");
        MaterialChannel?NormalChannel=Material.FindChannel("Normal");
        MaterialChannel?MetallicRoughnessChannel=Material.FindChannel("MetallicRoughness");
        MaterialChannel?EmissiveChannel=Material.FindChannel("Emissive");
        var Context=$"material '{Material.Name??"(unnamed)"}'";

        Vector3 EmissiveFactor=EmissiveChannel is { Color: var EmissiveColor }
            ?new Vector3(EmissiveColor.X,EmissiveColor.Y,EmissiveColor.Z)
            :Vector3.Zero;
        var EmissiveStrength=EmissiveChannel?.GetFactor("EmissiveStrength")??1f;
        byte[]?EmissiveTextureBytes=ExtractTextureBytes(EmissiveChannel,Diagnostics,Context);
        float EmissiveTextureLuminanceScale=EmissiveTextureLuminance.CalculateOrIdentity(EmissiveTextureBytes);

        return new ImportedPbrMaterial {
            BaseColorFactor=BaseColorChannel?.Color??new Vector4(1f,1f,1f,1f),
            MetallicFactor=MetallicRoughnessChannel?.GetFactor("MetallicFactor")??1f,
            RoughnessFactor=MetallicRoughnessChannel?.GetFactor("RoughnessFactor")??1f,
            EmissiveFactor=EmissiveFactor,
            EmissiveStrength=EmissiveStrength,
            EmissiveNits=EmissiveFactor*EmissiveStrength*Options.EmissiveNitsPerUnit,
            BaseColorTextureBytes=ExtractTextureBytes(BaseColorChannel,Diagnostics,Context),
            NormalTextureBytes=ExtractTextureBytes(NormalChannel,Diagnostics,Context),
            MetallicRoughnessTextureBytes=ExtractTextureBytes(MetallicRoughnessChannel,Diagnostics,Context),
            EmissiveTextureLuminanceScale=EmissiveTextureLuminanceScale,
            EmissiveTextureBytes=EmissiveTextureBytes,
            WorldGrid=ReadWorldGrid(Material,Diagnostics,Context),
        };
    }

    /// <summary>
    /// <c>extras.CGE_world_grid</c>を読み取る。不正なグリッドは報告したうえで除外し、マテリアルは任意のパターンを描画せず、
    /// 単色のベースサーフェス色へフォールバックする。
    /// </summary>
    private static ImportedWorldGrid?ReadWorldGrid(Material Material,List<GltfImportDiagnostic>Diagnostics,string Context){
        if(Material.Extras is not JsonObject Extras||!Extras.TryGetPropertyValue(ImportedWorldGrid.ExtrasKey,out var Node)){
            return null;
        }
        try {
            if(Node is not JsonObject Grid){
                throw new FormatException("it must be an object");
            }
            var GridSizeMeters=Grid["gridSizeMeters"]?.GetValue<float>()??throw new FormatException("gridSizeMeters is missing");
            var SubGridDivisions=Grid["subGridDivisions"]?.GetValue<int>()??throw new FormatException("subGridDivisions is missing");
            var WireHalfWidth=Grid["wireHalfWidth"]?.GetValue<float>()??throw new FormatException("wireHalfWidth is missing");
            var GridColor=ReadColor(Grid,"gridColor");
            var SubGridColor=ReadColor(Grid,"subGridColor");
            if(!float.IsFinite(GridSizeMeters)||GridSizeMeters<=0f){
                throw new FormatException("gridSizeMeters must be a positive number");
            }
            if(SubGridDivisions<1){
                throw new FormatException("subGridDivisions must be at least 1");
            }
            if(!float.IsFinite(WireHalfWidth)||WireHalfWidth<=0f||WireHalfWidth>=0.5f){
                throw new FormatException("wireHalfWidth must be greater than 0 and less than 0.5");
            }
            return new ImportedWorldGrid(GridSizeMeters,SubGridDivisions,WireHalfWidth,GridColor,SubGridColor);
        } catch(Exception Ex) when(Ex is FormatException or InvalidOperationException or JsonException){
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                $"'{Context}': {ImportedWorldGrid.ExtrasKey} is invalid ({Ex.Message}); the material is imported without a world grid."));
            return null;
        }
    }

    private static Vector3 ReadColor(JsonObject Grid,string Name){
        if(Grid[Name] is not JsonArray Values||Values.Count!=3){
            throw new FormatException($"{Name} must be three numbers");
        }
        var Color=new Vector3(Values[0]!.GetValue<float>(),Values[1]!.GetValue<float>(),Values[2]!.GetValue<float>());
        if(!float.IsFinite(Color.X)||!float.IsFinite(Color.Y)||!float.IsFinite(Color.Z)||Color.X<0f||Color.Y<0f||Color.Z<0f){
            throw new FormatException($"{Name} must be finite and not negative");
        }
        return Color;
    }

    /// <summary>
    /// エンジンは頂点ごとにUVセットを1つだけ持つため、インポート時にKHR_texture_transformをTEXCOORD_0へ焼き込む。
    /// ベースカラーのテクスチャがある場合はその変換を採用し、ない場合は最初に見つかったテクスチャチャンネルの変換を採用する。
    /// 変換が異なるチャンネルは報告する。異なるマッピングでサンプリングされるためである。
    /// </summary>
    private static GltfUvTransform?ResolveUvTransform(Material?Material,List<GltfImportDiagnostic>Diagnostics,string Context){
        if(Material is null){
            return null;
        }
        MaterialChannel[]Textured=[.. new[]{"BaseColor","Normal","MetallicRoughness","Emissive","Occlusion"}
            .Select(Material.FindChannel).Where(static Channel=>Channel?.Texture is not null&&Channel.Value.TextureCoordinate==0).Select(static Channel=>Channel!.Value)];
        if(Textured.Length==0){
            return null;
        }
        GltfUvTransform?Transform=ToUvTransform(Textured[0]);
        foreach(MaterialChannel Channel in Textured.Skip(1)){
            if(ToUvTransform(Channel)!=Transform){
                Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                    $"'{Context}': the {Channel.Key} texture's KHR_texture_transform differs from the {Textured[0].Key} texture's; it is sampled with the {Textured[0].Key} mapping because meshes carry one UV set."));
            }
        }
        return Transform;
    }

    private static GltfUvTransform?ToUvTransform(MaterialChannel Channel)=>
        Channel.TextureTransform is TextureTransform Transform
            ?new GltfUvTransform(Transform.Offset,Transform.Scale,Transform.Rotation)
            :null;

    private static void ApplyUvTransform(Vector2[]?Uvs,bool UvsUsable,GltfUvTransform?Transform){
        if(!UvsUsable||Uvs is null||Transform is not GltfUvTransform Value){
            return;
        }
        for(var I=0;I<Uvs.Length;I++){
            Uvs[I]=Value.Apply(Uvs[I]);
        }
    }

    /// <summary>
    /// チャンネルのテクスチャのエンコード済みバイト列。エンジンは各テクスチャを頂点の単一UVセットでサンプリングするため、
    /// 別のセットに割り当てられたテクスチャ（<c>texCoord</c>が0より大きいもの）は報告して除外する。
    /// 誤った座標でサンプリングされるのを防ぐためである。
    /// </summary>
    private static byte[]?ExtractTextureBytes(MaterialChannel?Channel,List<GltfImportDiagnostic>Diagnostics,string Context){
        if(Channel?.Texture?.PrimaryImage is not Image Image){
            return null;
        }
        if(Channel.Value.TextureCoordinate!=0){
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,
                $"'{Context}': the {Channel.Value.Key} texture uses TEXCOORD_{Channel.Value.TextureCoordinate}, but meshes carry one UV set (TEXCOORD_0); the texture is ignored."));
            return null;
        }
        return Image.Content.Content.ToArray();
    }

    private static ImportedLight ImportLight(PunctualLight Light){
        var Color=new LinearLightColor(Light.Color.X,Light.Color.Y,Light.Color.Z);
        LightRange? Range=Light.Range is float Distance&&float.IsFinite(Distance)?new LightRange(Distance):null;
        return Light.LightType switch {
            PunctualLightType.Directional=>new ImportedDirectionalLight(Color,new Illuminance(Light.Intensity)),
            PunctualLightType.Point=>new ImportedPointLight(Color,new LuminousIntensity(Light.Intensity),Range),
            PunctualLightType.Spot=>new ImportedSpotLight(Color,new LuminousIntensity(Light.Intensity),Range,new ImportedSpotCone(Light.InnerConeAngle,Light.OuterConeAngle)),
            _=>throw new NotSupportedException("Unsupported punctual light kind."),
        };
    }

    /// <summary>
    /// 単一のglTFプリミティブから描画可能なジオメトリを構築する。<see cref="Import(string)"/>と同じNORMAL／TEXCOORD_0欠落時の補完に加え、TANGENTも生成す
    /// る（単一メッシュのプレビュー経路では不要）。POSITIONまたは三角形データが完全にない場合に限り、診断とともにnullを返す。
    /// </summary>
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
        var RawInfluences=ReadInfluences(Primitive,RawPositions.Length,Diagnostics,Context);

        Vector3[]Positions;
        Vector3[]Normals;
        Vector2[]Uvs;
        uint[]Indices;
        int[]SourceVertices;

        if(!UvsUsable){
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning, $"'{Context}': TEXCOORD_0 attribute missing or inconsistent; synthesized (0,0) UV for every vertex."));
        }
        GltfUvTransform?UvTransform=ResolveUvTransform(Primitive.Material,Diagnostics,Context);
        ApplyUvTransform(RawUvs,UvsUsable,UvTransform);

        if(NormalsUsable){
            Positions=RawPositions;
            Normals=RawNormals!;
            Uvs=UvsUsable?RawUvs!:new Vector2[RawPositions.Length];
            Indices=new uint[Triangles.Count*3];
            SourceVertices=[.. Enumerable.Range(0,RawPositions.Length)];

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
            SourceVertices=new int[Triangles.Count*3];

            for(var I=0;I<Triangles.Count;I++){
                (var A,var B,var C)=Triangles[I];
                SourceVertices[(I*3)+0]=A;
                SourceVertices[(I*3)+1]=B;
                SourceVertices[(I*3)+2]=C;
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

        // ここからメッシュローカルのジオメトリをglTF空間からエンジン空間へ移す（ADR-0027）。単一軸の
        // ミラーとなるため、表面の向きを保つには三角形の巻き順を反転する必要がある。
        //（GltfAxisConversion.FlipTriangleWinding）。これはComputeTangentsの出力にも影響しない。
        //（各三角形の最後の2つのインデックスを入れ替えても、接線／従接線の式は変わらない。
        // ADR-0027で代数的に検証済み）。
        for(var I=0;I<Positions.Length;I++){
            Positions[I]=GltfAxisConversion.ConvertVector(Positions[I]);
            Normals[I]=GltfAxisConversion.ConvertVector(Normals[I]);
        }

        GltfAxisConversion.FlipTriangleWinding(Indices);

        Accessor?TangentAccessor=Primitive.GetVertexAccessor("TANGENT");
        Vector4[]?RawTangents=TangentAccessor is null?null:[.. TangentAccessor.AsVector4Array()];
        Vector4[]Tangents;
        if(RawTangents is not null&&RawTangents.Length==Positions.Length&&UvTransform?.PreservesTangentFrame!=false){
            // ソースで指定されたTANGENTアクセサーはglTF空間のデータなので、明示的に変換する。
            //（xyz方向と、接線基底の向きを示す符号を反転。GltfAxisConversion.ConvertTangentを参照）。
            // これは変換前の+u軸に沿う値なので、UV変換で回転またはミラーが行われた場合は下記で再導出する。
            Tangents=new Vector4[RawTangents.Length];
            for(var I=0;I<RawTangents.Length;I++){
                Tangents[I]=GltfAxisConversion.ConvertTangent(RawTangents[I]);
            }
        } else {
            // 上記で既に変換済みのエンジン空間の位置／法線／UV／インデックスから
            // 直接生成するため、ComputeTangentsがエンジン空間で自然に正しい接線基底の
            // 向きの符号を導出でき、別途反転する必要はない。
            Tangents=ComputeTangents(Positions,Normals,Uvs,Indices);
        }

        return new ImportedMeshData {
            Positions=Positions,
            Normals=Normals,
            Tangents=Tangents,
            Uvs=Uvs,
            Indices=Indices,
            Influences=RawInfluences is null?null:RemapInfluences(RawInfluences,SourceVertices),
        };
    }

    /// <summary>
    /// JOINTS_n／WEIGHTS_nの組を読み取り、ソース頂点ごとに<see cref="ImportedSkinInfluences.InfluencesPerVertex"/>
    /// スロットへ格納してウェイトを正規化する。剛体プリミティブの場合はnullを返す。
    /// </summary>
    private static ImportedSkinInfluences?ReadInfluences(MeshPrimitive Primitive,int VertexCount,List<GltfImportDiagnostic>Diagnostics,string Context){
        const int Slots=ImportedSkinInfluences.InfluencesPerVertex;
        var Joints=new ushort[VertexCount*Slots];
        var Weights=new float[VertexCount*Slots];
        var Sets=0;
        for(var Set=0;Primitive.GetVertexAccessor($"JOINTS_{Set}") is{ }JointAccessor;Set++){
            if(Primitive.GetVertexAccessor($"WEIGHTS_{Set}") is not{ }WeightAccessor||JointAccessor.Count!=VertexCount||WeightAccessor.Count!=VertexCount){
                Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,$"'{Context}': JOINTS_{Set} has no matching WEIGHTS_{Set}; the set is ignored."));
                break;
            }
            if(Set*4>=Slots){
                Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,$"'{Context}': JOINTS_{Set} exceeds {Slots} influences per vertex; the remaining weights are renormalized."));
                break;
            }
            var JointValues=JointAccessor.AsVector4Array();
            var WeightValues=WeightAccessor.AsVector4Array();
            for(var Vertex=0;Vertex<VertexCount;Vertex++){
                var J=JointValues[Vertex];
                var W=WeightValues[Vertex];
                var Base=(Vertex*Slots)+(Set*4);
                Joints[Base+0]=(ushort)J.X;
                Joints[Base+1]=(ushort)J.Y;
                Joints[Base+2]=(ushort)J.Z;
                Joints[Base+3]=(ushort)J.W;
                Weights[Base+0]=W.X;
                Weights[Base+1]=W.Y;
                Weights[Base+2]=W.Z;
                Weights[Base+3]=W.W;
            }
            Sets++;
        }
        if(Sets==0){
            return null;
        }
        for(var Vertex=0;Vertex<VertexCount;Vertex++){
            var Sum=0f;
            for(var Slot=0;Slot<Slots;Slot++){
                Sum+=Weights[(Vertex*Slots)+Slot];
            }
            if(Sum<=0f){
                Weights[Vertex*Slots]=1f;
                continue;
            }
            for(var Slot=0;Slot<Slots;Slot++){
                Weights[(Vertex*Slots)+Slot]/=Sum;
            }
        }
        return new ImportedSkinInfluences { JointIndices=Joints,Weights=Weights };
    }

    private static ImportedSkinInfluences RemapInfluences(ImportedSkinInfluences Source,int[]SourceVertices){
        const int Slots=ImportedSkinInfluences.InfluencesPerVertex;
        var Joints=new ushort[SourceVertices.Length*Slots];
        var Weights=new float[SourceVertices.Length*Slots];
        for(var Vertex=0;Vertex<SourceVertices.Length;Vertex++){
            for(var Slot=0;Slot<Slots;Slot++){
                Joints[(Vertex*Slots)+Slot]=Source.JointIndices[(SourceVertices[Vertex]*Slots)+Slot];
                Weights[(Vertex*Slots)+Slot]=Source.Weights[(SourceVertices[Vertex]*Slots)+Slot];
            }
        }
        return new ImportedSkinInfluences { JointIndices=Joints,Weights=Weights };
    }

    private static ImportedSkin ImportSkin(Skin Skin,List<GltfImportDiagnostic>Diagnostics){
        var Count=Skin.JointsCount;
        var Joints=Skin.Joints;
        var InverseBind=Skin.InverseBindMatrices;
        var IndexByNode=new Dictionary<Node,int>();
        for(var Index=0;Index<Count;Index++){
            IndexByNode[Joints[Index]]=Index;
        }
        var Names=new string[Count];
        var Parents=new int[Count];
        var Translations=new Vector3[Count];
        var Rotations=new Quaternion[Count];
        var Scales=new Vector3[Count];
        var Matrices=new Matrix4x4[Count];
        Node?RootParent=null;
        var RootParentSet=false;
        for(var Index=0;Index<Count;Index++){
            var Joint=Joints[Index];
            Names[Index]=Joint.Name??$"joint{Index}";
            Parents[Index]=Joint.VisualParent is{ }Parent&&IndexByNode.TryGetValue(Parent,out var ParentIndex)?ParentIndex:-1;
            if(Parents[Index]<0){
                if(RootParentSet&&!ReferenceEquals(RootParent,Joint.VisualParent)){
                    Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,$"Skin '{Skin.Name}': root joints have different parents; the first root's parent places the skeleton."));
                }
                if(!RootParentSet){
                    RootParent=Joint.VisualParent;
                    RootParentSet=true;
                }
            }
            if(!Matrix4x4.Decompose(Joint.LocalMatrix,out var Scale,out var Rotation,out var Translation)){
                Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,$"Skin '{Skin.Name}': joint '{Names[Index]}' has a non-decomposable rest transform; identity is used."));
                (Scale,Rotation,Translation)=(Vector3.One,Quaternion.Identity,Vector3.Zero);
            }
            Translations[Index]=GltfAxisConversion.ConvertVector(Translation);
            Rotations[Index]=GltfAxisConversion.ConvertRotation(Rotation);
            Scales[Index]=Scale;
            Matrices[Index]=GltfAxisConversion.ConvertMatrix(Index<InverseBind.Count?InverseBind[Index]:Matrix4x4.Identity);
        }
        return new ImportedSkin {
            JointNames=Names,
            ParentIndices=Parents,
            RestTranslations=Translations,
            RestRotations=Rotations,
            RestScales=Scales,
            InverseBindMatrices=Matrices,
            SkeletonTransform=RootParent is null?Matrix4x4.Identity:GltfAxisConversion.ConvertMatrix(RootParent.WorldMatrix),
        };
    }

    /// <summary>
    /// glTFファイル内のすべてのアニメーションを、エンジン空間のジョイントトラックとしてインポートする（ADR-0066）。
    /// Translation、rotation、scale各チャンネルに対応する。LINEARとSTEPは意味を維持し、CUBICSPLINEは警告を出してキー値のみを使う。
    /// </summary>
    public static GltfAnimationImportResult ImportAnimations(string FilePath){
        ArgumentException.ThrowIfNullOrWhiteSpace(FilePath);
        var FullPath=Path.GetFullPath(FilePath);
        List<GltfImportDiagnostic>Diagnostics=[];
        if(!File.Exists(FullPath)){
            return GltfAnimationImportResult.Failed([new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Error,$"glTF file not found: {FullPath}")]);
        }
        ModelRoot Model;
        try {
            Model=ModelRoot.Load(FullPath);
        } catch(Exception Ex){
            return GltfAnimationImportResult.Failed([new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Error,$"Failed to parse glTF file '{FullPath}': {Ex.Message}")]);
        }
        List<ImportedAnimationClip>Clips=[];
        foreach(var Animation in Model.LogicalAnimations){
            List<ImportedJointTrack>Tracks=[];
            var Duration=0f;
            foreach(var Node in Animation.Channels.Select(static Channel=>Channel.TargetNode).Where(static Node=>Node is not null).Distinct()){
                var Translation=ToKeys(Animation.FindTranslationChannel(Node)?.GetTranslationSampler(),GltfAxisConversion.ConvertVector,Diagnostics,Animation.Name);
                var Rotation=ToKeys(Animation.FindRotationChannel(Node)?.GetRotationSampler(),GltfAxisConversion.ConvertRotation,Diagnostics,Animation.Name);
                var Scale=ToKeys(Animation.FindScaleChannel(Node)?.GetScaleSampler(),static Value=>Value,Diagnostics,Animation.Name);
                if(Translation is null&&Rotation is null&&Scale is null){
                    continue;
                }
                Duration=MathF.Max(Duration,MathF.Max(LastTime(Translation),MathF.Max(LastTime(Rotation),LastTime(Scale))));
                Tracks.Add(new ImportedJointTrack { JointName=Node.Name??string.Empty,Translation=Translation,Rotation=Rotation,Scale=Scale });
            }
            Clips.Add(new ImportedAnimationClip { Name=Animation.Name??$"animation{Clips.Count}",DurationSeconds=Duration,Tracks=Tracks });
        }
        return GltfAnimationImportResult.Succeeded(Clips,Diagnostics);
    }

    private static float LastTime<T>(ImportedKeys<T>?Keys) where T:struct=>Keys is{ Times.Count:>0 }?Keys.Times[^1]:0f;

    private static ImportedKeys<T>?ToKeys<T>(IAnimationSampler<T>?Sampler,Func<T,T>Convert,List<GltfImportDiagnostic>Diagnostics,string?Clip) where T:struct{
        if(Sampler is null){
            return null;
        }
        List<(float Time,T Value)>Keys;
        if(Sampler.InterpolationMode==AnimationInterpolationMode.CUBICSPLINE){
            Diagnostics.Add(new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Warning,$"Animation '{Clip}': cubic-spline keys are imported as linear key values."));
            Keys=[.. Sampler.GetCubicKeys().Select(static Key=>(Key.Key,Key.Value.Value))];
        } else {
            Keys=[.. Sampler.GetLinearKeys()];
        }
        return new ImportedKeys<T> {
            Times=[.. Keys.Select(static Key=>Key.Time)],
            Values=[.. Keys.Select(Key=>Convert(Key.Value))],
            Step=Sampler.InterpolationMode==AnimationInterpolationMode.STEP,
        };
    }

    private static GltfSceneImportResult SceneFailed(string Message)=>
        GltfSceneImportResult.Failed([new GltfImportDiagnostic(GltfImportDiagnosticSeverity.Error,Message)]);

    /// <summary>
    /// TANGENTアクセサーがないプリミティブ向けに、頂点ごとの接線を生成する（Lengyelの手法：
    /// 「Computing Tangent Space Basis Vectors for an Arbitrary Mesh」）。各三角形のUV空間接線／従接線を3頂点へ累積し、
    /// 頂点法線（共有の場合もある）に対してグラム・シュミット直交化した後、従接線から基底の向きの符号（w）を求める。
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
