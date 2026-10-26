using System.Numerics;
using Assimp;

namespace CSharpGameEngine.Import.Fbx;

/// <summary>
/// AssimpNet経由で.fbxファイルの最初のメッシュをインポートする（ADR-0009）。シーングラフ、複数メッシュ、完全なPBRマテリアルには対応しない。
/// glTFに対する<c>CSharpGameEngine.Import.Gltf.GltfImporter</c>と同範囲の最小限の垂直スライスである。
/// </summary>
public static class FbxImporter {
    /// <summary>
    /// Assimpのログはプロセス全体で共有される。別のインポート中にログストリームを接続／切断するとプロセスがクラッシュし
    ///（アクセス違反。ログなしの並行インポートでは発生しなかった）、接続中の各ストリームに別インポートのメッセージも届く。
    /// 同時にログを使うインポートは1件だけとする。
    /// </summary>
    private static readonly Lock NativeImportGate=new();

    internal static Assimp.Scene ReadCompositeScene(string Source){
        var Info=new FileInfo(Source);if(Info.Length is <1 or >128*1024*1024){ throw new InvalidDataException("MODEL_SIZE: FBX source exceeds 128 MiB."); }
        lock(NativeImportGate){
            using var Context=new AssimpContext();using var Stream=File.OpenRead(Source);
            return Context.ImportFileFromStream(Stream,PostProcessSteps.Triangulate|PostProcessSteps.GenerateSmoothNormals|PostProcessSteps.CalculateTangentSpace,"fbx")
                ??throw new InvalidDataException("MODEL_PARSE: FBX reader returned no scene.");
        }
    }
    public static FbxImportResult Import(string FilePath){
        ArgumentException.ThrowIfNullOrWhiteSpace(FilePath);

        var FullPath=Path.GetFullPath(FilePath);
        if(!File.Exists(FullPath)){
            return Failed($"FBX file not found: {FullPath}");
        }

        // Assimp自身のネイティブログ（FBXパーサーが出す警告／エラー。特定エクスポーターの出力に未知の
        // 要素がある場合など）は、コンソールへ直接出さず診断情報として収集する。
        // 呼び出し側が表示方法を決める、GltfImporterと同じ契約である。
        // Assimpの「Error」レベルのログがあっても、必ずしもインポート全体が失敗したとは限らない
        //（実測で確認済み）。下記のImport()内にある明示的な検査だけを
        // 致命的エラーとして扱う（シーンが空、頂点がない、三角形がない）。
        List<FbxImportDiagnostic>Diagnostics=[];
        Scene Scene;
        lock(NativeImportGate){
            LogStream LogStream=new((Msg,_)=>{
                var Trimmed=Msg.Trim();
                if(Trimmed.StartsWith("Error",StringComparison.OrdinalIgnoreCase)||
                    Trimmed.StartsWith("Warn",StringComparison.OrdinalIgnoreCase)){
                    Diagnostics.Add(new FbxImportDiagnostic(FbxImportDiagnosticSeverity.Warning, $"Assimp: {Trimmed}"));
                }
            });
            LogStream.Attach();

            using AssimpContext Context=new();
            try {
                // AssimpNetはファイル名をANSI文字列としてネイティブインポーターへ渡すため、システムのコード
                // ページにないパス（英語版Windows上の日本語フォルダーなど）は「?」になり、開けなかった。
                // インポーターはFBXファイルだけを読み、外部テクスチャは扱わないため、ファイル名ではなくバイト列を渡す。
                using var Stream=File.OpenRead(FullPath);
                Scene=Context.ImportFileFromStream(
                    Stream,
                    PostProcessSteps.Triangulate|PostProcessSteps.GenerateSmoothNormals|PostProcessSteps.JoinIdenticalVertices,
                    Path.GetExtension(FullPath).TrimStart('.'));
            } catch(Exception Ex){
                // Assimpには不正入力に対する網羅的な例外一覧が文書化されていない。
                // 不正ファイルによる未処理例外が、このインポーターから漏れてはならない。
                return Failed($"Failed to parse FBX file '{FullPath}': {Ex.Message}");
            } finally {
                LogStream.Detach();
            }
        }

        if(Scene is null||Scene.MeshCount==0){
            return Failed("FBX file contains no meshes.",Diagnostics);
        }

        var Mesh=Scene.Meshes[0];
        if(Mesh.VertexCount==0){
            return Failed("FBX mesh has no vertices.",Diagnostics);
        }

        // AssimpNetの既定出力空間は右手系／Y-up／Forward=-Zである（下記を参照）。
        //（FbxAxisConversionの型説明を参照）。ここでADR-0027に従い、エンジン固定の
        // ADR-0027に従ってUnityCoordinateConvention（左手系）へ変換する。位置／法線は頂点ごとに変換し
        //（FbxAxisConversion.ConvertVector）、下記で三角形の巻き順を反転して
        // ミラーされた位置と表面の向きを一致させる。
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
                // Triangulateが保証するはずだが、万一残った退化要素は防御的に除外する。
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
