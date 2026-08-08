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
internal sealed class PreviewSceneDefinitionCommand
{
    public const int CommandId = 0x0108;
    public static readonly Guid CommandSet = new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage _package;
    private readonly DTE _dte;

    private PreviewSceneDefinitionCommand(AsyncPackage package, OleMenuCommandService commandService, DTE dte)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _dte = dte ?? throw new ArgumentNullException(nameof(dte));

        if (commandService is null)
        {
            throw new ArgumentNullException(nameof(commandService));
        }

        CommandID menuCommandId = new(CommandSet, CommandId);
        OleMenuCommand command = new(Execute, menuCommandId);
        command.BeforeQueryStatus += UpdateVisibility;
        commandService.AddCommand(command);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        object? menuCommandService = await package.GetServiceAsync(typeof(IMenuCommandService));
        if (menuCommandService is not OleMenuCommandService commandService)
        {
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        object? dteService = await package.GetServiceAsync(typeof(SDTE));
        if (dteService is not DTE dte)
        {
            throw new InvalidOperationException($"{nameof(SDTE)} is not available from the VS shell.");
        }

        _ = new PreviewSceneDefinitionCommand(package, commandService, dte);
    }

    private void UpdateVisibility(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (sender is not OleMenuCommand menuCommand)
        {
            return;
        }

        bool visible = false;

        try
        {
            if (TryGetActiveDocumentText(out string activeDocumentText)
                && SceneDefinitionTextDetector.LooksLikeSceneDefinition(activeDocumentText))
            {
                visible = true;
            }
        }
        catch (Exception)
        {
            // BeforeQueryStatus runs every time the context menu opens - any COM/interop hiccup must
            // fail closed (command stays hidden) rather than surface an error dialog from a menu.
        }

        menuCommand.Visible = visible;
        menuCommand.Enabled = visible;
    }

    private bool TryGetActiveDocumentText(out string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        text = "";

        Document? activeDocument = _dte.ActiveDocument;
        if (activeDocument is null || !string.Equals(Path.GetExtension(activeDocument.FullName), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (activeDocument.Object("TextDocument") is not TextDocument textDocument)
        {
            return false;
        }

        text = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
        return true;
    }

    private void Execute(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            Document? activeDocument = _dte.ActiveDocument;
            if (activeDocument is null)
            {
                ShowMessage("プレビュー対象のドキュメントが開かれていません。");
                return;
            }

            string sourcePath = activeDocument.FullName;
            Project? containingProject = activeDocument.ProjectItem?.ContainingProject;
            if (containingProject is null)
            {
                ShowMessage("このドキュメントの所属プロジェクトを特定できませんでした。");
                return;
            }

            string? repoRoot = RepoLocator.ResolveRepoRoot();
            if (repoRoot is null)
            {
                ShowMessage($"リポジトリルート({RepoLocator.RepoMarkerFileName})が見つかりませんでした。");
                return;
            }

            string? assemblyPath = ProjectOutputAssemblyResolver.TryResolve(containingProject);
            if (assemblyPath is null)
            {
                ShowMessage($"'{containingProject.Name}' のビルド出力が見つかりませんでした。先にプロジェクトをビルドしてから再実行してください。");
                return;
            }

            // Deliberately synchronous/blocking (spawns and waits on the bridge process), not routed
            // through JoinableTaskFactory - matches SceneHwndHost.LoadIntoSurface's own reasoning for
            // its bridge calls (this repo has hit real nested-JoinableTaskFactory deadlocks twice
            // before; a plain blocking call from a normal Execute handler has no such risk).
            ScenePreviewBridgeDocument document = ExternalScenePreviewLoader.Load(sourcePath, assemblyPath, repoRoot);

            SceneFile scene = ScenePreviewSceneFileAdapter.ToSceneFile(document);
            string scenePath = ScenePreviewSceneFileAdapter.ResolveTempScenePath(document.SceneClassFullName ?? Path.GetFileNameWithoutExtension(sourcePath));
            scene.Save(scenePath);

            // dte.ItemOperations.OpenFile(...) is the confirmed-reliable way to route through the
            // real IVsEditorFactory-based open path (ADR-0013) - mere selection or a raw file-open
            // API does not reliably trigger a custom editor factory.
            _dte.ItemOperations.OpenFile(scenePath);

            // Forces an already-open preview tab for this same class to refresh, matching how
            // PrimitiveSpawner already relies on this same event after rewriting a .scene on disk.
            SceneEditorHub.RequestReload();
        }
        catch (Exception ex)
        {
            ShowMessage($"シーンプレビューの作成に失敗しました:\n{ex.Message}");
        }
    }

    private void ShowMessage(string message)
    {
        VsShellUtilities.ShowMessageBox(
            _package,
            message,
            "C# Game Engine Scene",
            OLEMSGICON.OLEMSGICON_WARNING,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
