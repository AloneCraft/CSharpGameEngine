using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools-menu command that shows/creates <see cref="HierarchyToolWindowPane"/> (ADR-0020) - a manual
/// fallback since <see cref="CSharpGameEngineViewerPackage.InitializeAsync"/> also auto-shows it once
/// at package load (so it behaves like Unity's always-docked Hierarchy by default).
/// </summary>
internal sealed class ShowHierarchyToolWindowCommand
{
    public const int CommandId = 0x0105;
    public static readonly Guid CommandSet = new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage _package;

    private ShowHierarchyToolWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
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

        _ = new ShowHierarchyToolWindowCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async delegate
        {
            ToolWindowPane window = await _package.ShowToolWindowAsync(typeof(HierarchyToolWindowPane), 0, true, _package.DisposalToken);
            if (window?.Frame is null)
            {
                throw new NotSupportedException("Cannot create the Hierarchy tool window.");
            }
        });
    }
}
