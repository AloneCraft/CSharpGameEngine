using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Drives the "シェイプ" palette's enabled buttons (ADR-0021): generates the requested primitive's
/// <c>.glb</c> once via the out-of-process <c>tools/CSharpGameEngine.Primitive.Cli</c> bridge
/// (cached under <c>UserGameProject/Assets/Primitives/</c>, so a second click of the same shape
/// skips regeneration), then hands off to <see cref="SceneNodeAdder"/> (ADR-0022) to append a node
/// referencing it to the currently-open scene. The scene JSON schema itself is untouched - a
/// spawned primitive is indistinguishable from any other <c>meshAssetPath</c>-bearing node.
/// </summary>
internal static class PrimitiveSpawner
{
    private const string BridgeProjectRelativePath = @"tools\CSharpGameEngine.Primitive.Cli";
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(60);

    public static async Task SpawnAsync(string primitiveKind)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        try
        {
            string? repoRoot = RepoLocator.ResolveRepoRoot();
            if (repoRoot is null)
            {
                throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
            }

            string primitivesDirectory = Path.Combine(repoRoot, "UserGameProject", "Assets", "Primitives");
            Directory.CreateDirectory(primitivesDirectory);
            string glbPath = Path.Combine(primitivesDirectory, $"{primitiveKind}.glb");

            if (!File.Exists(glbPath))
            {
                SetStatusBarText($"{primitiveKind} を生成しています...");
                await Task.Run(() => ExternalBridgeProcess.RunProducingFile(BridgeProjectRelativePath, primitiveKind, glbPath, repoRoot, BridgeTimeout, useResidentServer: true));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetStatusBarText(string.Empty);
            }

            if (!SceneNodeAdder.TryAddAssetNode(glbPath, primitiveKind, out string? errorMessage))
            {
                System.Windows.MessageBox.Show(
                    errorMessage,
                    "C# Game Engine Shape Palette",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"シェイプの配置に失敗しました: {ex}",
                "C# Game Engine Shape Palette",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private static void SetStatusBarText(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
        {
            statusBar.SetText(text);
        }
    }
}
