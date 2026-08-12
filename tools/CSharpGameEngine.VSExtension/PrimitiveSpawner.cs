using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Drives the "シェイプ" palette's enabled buttons (ADR-0021): generates the requested primitive's
/// <c>.glb</c> once via the out-of-process <c>tools/CSharpGameEngine.Primitive.Cli</c> bridge
/// (cached under <c>UserEcsProject/Assets/Primitives/</c>, so a second click of the same shape
/// skips regeneration), then hands off to <see cref="SceneNodeAdder"/> (ADR-0022) to append a node
/// referencing it to the currently-open scene. The scene JSON schema itself is untouched - a
/// spawned primitive is indistinguishable from any other <c>meshAssetPath</c>-bearing node.
/// </summary>
internal static class PrimitiveSpawner {
    private const string BridgeProjectRelativePath=@"tools\CSharpGameEngine.Primitive.Cli";
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);

    public static async Task SpawnAsync(string PrimitiveKind){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        try {
            var RepoRoot=RepoLocator.ResolveRepoRoot();
            if(RepoRoot is null){
                throw new InvalidOperationException($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
            }

            var PrimitivesDirectory=Path.Combine(RepoRoot,"UserEcsProject","Assets","Primitives");
            Directory.CreateDirectory(PrimitivesDirectory);
            var GlbPath=Path.Combine(PrimitivesDirectory, $"{PrimitiveKind}.glb");

            if(!File.Exists(GlbPath)){
                SetStatusBarText($"{PrimitiveKind} を生成しています...");
                await Task.Run(()=>ExternalBridgeProcess.RunProducingFile(BridgeProjectRelativePath,PrimitiveKind,GlbPath,RepoRoot,BridgeTimeout,UseResidentServer:true));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetStatusBarText(string.Empty);
            }

            if(!SceneNodeAdder.TryAddAssetNode(GlbPath,PrimitiveKind,out var ErrorMessage)){
                System.Windows.MessageBox.Show(
                    ErrorMessage,
                    "C# Game Engine Shape Palette",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        } catch(Exception Ex){
            System.Windows.MessageBox.Show(
                $"シェイプの配置に失敗しました: {Ex}",
                "C# Game Engine Shape Palette",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private static void SetStatusBarText(string Text){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Package.GetGlobalService(typeof(SVsStatusbar))is IVsStatusbar StatusBar){
            StatusBar.SetText(Text);
        }
    }
}
