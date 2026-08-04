using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools-menu command that (re)applies the "GameEngine" default panel arrangement (<see cref="GameEngineLayout"/>,
/// ADR-0021) on demand. Necessary because VS's own "Window &gt; Apply Window Layout" mechanism has
/// no public SDK API to register a named entry, and because a tool window's
/// <c>[ProvideToolWindow]</c> default docking only ever applies the very first time that window is
/// created in a given hive - once VS has persisted a real layout (<c>.winprf</c>), this command is
/// the only way to force the panels back to their intended positions.
/// </summary>
internal sealed class ApplyGameEngineLayoutCommand
{
    public const int CommandId = 0x0106;
    public static readonly Guid CommandSet = new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage _package;

    private ApplyGameEngineLayoutCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        if (commandService is null)
        {
            throw new ArgumentNullException(nameof(commandService));
        }

        CommandID menuCommandId = new(CommandSet, CommandId);
        MenuCommand menuItem = new(Execute, menuCommandId);
        commandService.AddCommand(menuItem);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        object? service = await package.GetServiceAsync(typeof(IMenuCommandService));
        if (service is not OleMenuCommandService commandService)
        {
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _ = new ApplyGameEngineLayoutCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async delegate
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

            if (await _package.GetServiceAsync(typeof(SDTE)) is not DTE dte)
            {
                return;
            }

            string? repoRoot = RepoLocator.ResolveRepoRoot();
            if (repoRoot is null)
            {
                return;
            }

            await GameEngineLayout.ApplyAsync(_package, dte, repoRoot);
        });
    }
}
