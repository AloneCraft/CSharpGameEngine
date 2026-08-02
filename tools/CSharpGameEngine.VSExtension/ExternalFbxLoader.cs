using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Loads an .fbx file for the VSIX tool window WITHOUT loading AssimpNet's native
/// <c>assimp.dll</c> into <c>devenv.exe</c> (ADR-0010). FBX parsing runs in a short-lived
/// out-of-process helper (<c>CSharpGameEngine.Import.Fbx.Cli</c>, net10.0, wraps
/// <c>CSharpGameEngine.Import.Fbx</c>/AssimpNet) that writes the extracted mesh to a temp JSON
/// file; this class launches that helper, waits for it to exit, and deserializes the result. A
/// crash or hang in the native FBX parser while parsing an untrusted .fbx file is contained to the
/// helper process and surfaces here as a plain exception (caught by
/// <see cref="ViewerHwndHost.BuildWindowCore"/>) instead of taking down Visual Studio itself.
/// </summary>
internal static class ExternalFbxLoader
{
    private const string BridgeProjectRelativePath = @"tools\CSharpGameEngine.Import.Fbx.Cli";
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(60);

    public static SimpleMesh Load(string fbxPath, string repoRoot)
    {
        string tempJsonPath = Path.Combine(Path.GetTempPath(), $"csge-fbx-bridge-{Guid.NewGuid():N}.json");
        try
        {
            RunBridgeProcess(fbxPath, tempJsonPath, repoRoot);

            if (!File.Exists(tempJsonPath))
            {
                throw new InvalidOperationException("FBXインポートブリッジが出力ファイルを生成しませんでした。");
            }

            string json = File.ReadAllText(tempJsonPath);
            BridgeDocument? document = JsonSerializer.Deserialize<BridgeDocument>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (document is null || !document.Success)
            {
                throw new InvalidOperationException("FBXファイルのインポートに失敗しました(詳細はブリッジプロセスの出力を確認してください)。");
            }

            return ToSimpleMesh(document);
        }
        finally
        {
            try { File.Delete(tempJsonPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void RunBridgeProcess(string fbxPath, string outputJsonPath, string repoRoot)
    {
        ProcessStartInfo startInfo = new(
            "dotnet",
            $"run --project \"{BridgeProjectRelativePath}\" -- \"{fbxPath}\" \"{outputJsonPath}\"")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using Process process = new() { StartInfo = startInfo };

        // Read both streams via the event-based API (BeginOutputReadLine/BeginErrorReadLine)
        // before WaitForExit: reading them synchronously and sequentially here would deadlock if
        // the child fills the OS pipe buffer for the stream being read second while blocked
        // writing to the (unread) first stream.
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool exited = process.WaitForExit((int)BridgeTimeout.TotalMilliseconds);
        if (!exited)
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw new TimeoutException($"FBXインポートブリッジが{BridgeTimeout.TotalSeconds}秒以内に終了しませんでした。");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"FBXインポートブリッジがエラー終了しました(exit code {process.ExitCode})。\n{stdout}\n{stderr}");
        }
    }

    private static SimpleMesh ToSimpleMesh(BridgeDocument document)
    {
        Vector3[] positions = ToVector3Array(document.Positions);
        Vector3[] normals = ToVector3Array(document.Normals);
        uint[] indices = document.Indices;
        Vector4 baseColorFactor = document.BaseColorFactor.Length == 4
            ? new Vector4(document.BaseColorFactor[0], document.BaseColorFactor[1], document.BaseColorFactor[2], document.BaseColorFactor[3])
            : new Vector4(1f, 1f, 1f, 1f);

        if (positions.Length == 0)
        {
            throw new InvalidOperationException("FBXメッシュに頂点がありません。");
        }

        (Vector3 center, float radius) = SimpleMesh.ComputeBoundingSphere(positions);

        return new SimpleMesh
        {
            Positions = positions,
            Normals = normals,
            Indices = indices,
            BaseColorFactor = baseColorFactor,
            BoundsCenter = center,
            BoundsRadius = radius,
        };
    }

    private static Vector3[] ToVector3Array(float[] flat)
    {
        Vector3[] result = new Vector3[flat.Length / 3];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new Vector3(flat[(i * 3) + 0], flat[(i * 3) + 1], flat[(i * 3) + 2]);
        }

        return result;
    }

    private sealed class BridgeDocument
    {
        public bool Success { get; set; }

        public float[] Positions { get; set; } = [];

        public float[] Normals { get; set; } = [];

        public uint[] Indices { get; set; } = [];

        public float[] BaseColorFactor { get; set; } = [];
    }
}
