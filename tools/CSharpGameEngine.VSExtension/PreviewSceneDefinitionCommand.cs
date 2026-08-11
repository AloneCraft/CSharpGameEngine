using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Code-editor context-menu command (this repo's first - ADR-0031) that previews a
/// <c>[SceneDefinition]</c> class's real scene graph by actually executing its generated
/// <c>CreateRuntime</c> out-of-process (<see cref="ExternalScenePreviewLoader"/>) and reusing the
/// existing シーン preview pipeline (<see cref="SceneFileEditorFactory"/>) via a synthesized temp
/// <c>.scene</c> file (<see cref="ScenePreviewSceneFileAdapter"/>). Visible only when the active
/// document looks like it contains <c>[SceneDefinition]</c> (<see cref="SceneDefinitionTextDetector"/>)
/// - the first command in this repo to gate Visible/Enabled from
/// <see cref="OleMenuCommand.BeforeQueryStatus"/>.
/// </summary>
internal sealed class PreviewSceneDefinitionCommand {
    public const int CommandId=0x0108;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;
    private readonly DTE Dte;

    private PreviewSceneDefinitionCommand(AsyncPackage package,OleMenuCommandService CommandService,DTE Dte){
        Package=package??throw new ArgumentNullException(nameof(package));
        this.Dte=Dte??throw new ArgumentNullException(nameof(Dte));

        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }

        CommandID MenuCommandId=new(CommandSet,CommandId);
        OleMenuCommand command=new(Execute,MenuCommandId);
        command.BeforeQueryStatus+=UpdateVisibility;
        CommandService.AddCommand(command);
    }

    public static async Task InitializeAsync(AsyncPackage package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var menuCommandService= await package.GetServiceAsync(typeof(IMenuCommandService));
        if(menuCommandService is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        var DteService= await package.GetServiceAsync(typeof(SDTE));
        if(DteService is not DTE Dte){
            throw new InvalidOperationException($"{nameof(SDTE)} is not available from the VS shell.");
        }

        _=new PreviewSceneDefinitionCommand(package,CommandService,Dte);
    }

    private void UpdateVisibility(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        if(Sender is not OleMenuCommand menuCommand){
            return;
        }

        var Visible=false;

        try {
            if(TryGetActiveDocumentText(out var ActiveDocumentText)
                &&SceneDefinitionTextDetector.LooksLikeSceneDefinition(ActiveDocumentText)){
                Visible=true;
            }
        } catch(Exception){
            // BeforeQueryStatus runs every time the context menu opens - any COM/interop hiccup must
            // fail closed (command stays hidden) rather than surface an error dialog from a menu.
        }

        menuCommand.Visible=Visible;
        menuCommand.Enabled=Visible;
    }

    private bool TryGetActiveDocumentText(out string Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        Text="";

        Document?ActiveDocument=Dte.ActiveDocument;
        if(ActiveDocument is null||!string.Equals(Path.GetExtension(ActiveDocument.FullName),".cs",StringComparison.OrdinalIgnoreCase)){
            return false;
        }

        if(ActiveDocument.Object("TextDocument")is not TextDocument textDocument){
            return false;
        }

        Text=textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
        return true;
    }

    private void Execute(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();

        try {
            Document?ActiveDocument=Dte.ActiveDocument;
            if(ActiveDocument is null){
                ShowMessage("プレビュー対象のドキュメントが開かれていません。");
                return;
            }

            var SourcePath=ActiveDocument.FullName;
            Project?ContainingProject=ActiveDocument.ProjectItem?.ContainingProject;
            if(ContainingProject is null){
                ShowMessage("このドキュメントの所属プロジェクトを特定できませんでした。");
                return;
            }

            var RepoRoot=RepoLocator.ResolveRepoRoot();
            if(RepoRoot is null){
                ShowMessage($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
                return;
            }

            var AssemblyPath=ProjectOutputAssemblyResolver.TryResolve(ContainingProject);
            if(AssemblyPath is null){
                ShowMessage($"'{ContainingProject.Name}' のビルド出力が見つかりませんでした。先にプロジェクトをビルドしてから再実行してください。");
                return;
            }

            // Deliberately synchronous/blocking (spawns and waits on the bridge process), not routed
            // through JoinableTaskFactory - matches SceneHwndHost.LoadIntoSurface's own reasoning for
            // its bridge calls (this repo has hit real nested-JoinableTaskFactory deadlocks twice
            // before; a plain blocking call from a normal Execute handler has no such risk).
            ScenePreviewBridgeDocument document=ExternalScenePreviewLoader.Load(SourcePath,AssemblyPath,RepoRoot);

            SceneFile Scene=ScenePreviewSceneFileAdapter.ToSceneFile(document);
            var ScenePath=ScenePreviewSceneFileAdapter.ResolveTempScenePath(document.SceneClassFullName??Path.GetFileNameWithoutExtension(SourcePath));
            Scene.Save(ScenePath);

            // dte.ItemOperations.OpenFile(...) is the confirmed-reliable way to route through the
            // real IVsEditorFactory-based open path (ADR-0013) - mere selection or a raw file-open
            // API does not reliably trigger a custom editor factory.
            Dte.ItemOperations.OpenFile(ScenePath);

            // Forces an already-open preview tab for this same class to refresh, matching how
            // PrimitiveSpawner already relies on this same event after rewriting a .scene on disk.
            SceneEditorHub.RequestReload();
        } catch(Exception Ex){
            ShowMessage($"シーンプレビューの作成に失敗しました:\n{Ex.Message}");
        }
    }

    private void ShowMessage(string Message){
        VsShellUtilities.ShowMessageBox(
            Package,
            Message,
            "C# Game Engine Scene",
            OLEMSGICON.OLEMSGICON_WARNING,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
