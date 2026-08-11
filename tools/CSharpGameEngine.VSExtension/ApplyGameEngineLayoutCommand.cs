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
internal sealed class ApplyGameEngineLayoutCommand {
    public const int CommandId=0x0106;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;

    private ApplyGameEngineLayoutCommand(AsyncPackage package,OleMenuCommandService CommandService){
        Package=package??throw new ArgumentNullException(nameof(package));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }

        CommandID MenuCommandId=new(CommandSet,CommandId);
        MenuCommand MenuItem=new(Execute,MenuCommandId);
        CommandService.AddCommand(MenuItem);
    }

    public static async Task InitializeAsync(AsyncPackage package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var Service= await package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _=new ApplyGameEngineLayoutCommand(package,CommandService);
    }

    private void Execute(object Sender,EventArgs E){
        _=Package.JoinableTaskFactory.RunAsync(async delegate {
            await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);

            if(await Package.GetServiceAsync(typeof(SDTE))is not DTE Dte){
                return;
            }

            var RepoRoot=RepoLocator.ResolveRepoRoot();
            if(RepoRoot is null){
                return;
            }

            await GameEngineLayout.ApplyAsync(Package,Dte,RepoRoot);
        });
    }
}
