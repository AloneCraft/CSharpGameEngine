using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools-menu command that shows/creates <see cref="ViewerToolWindowPane"/> - the embedded-in-VS
/// alternative (ADR-0007) to <see cref="OpenViewerCommand"/>'s separate top-level window
/// (ADR-0006). Both commands are kept; this one doesn't replace the other.
/// </summary>
internal sealed class ShowViewerToolWindowCommand
{
    public const int CommandId = 0x0101;
    public static readonly Guid CommandSet = new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage _package;

    private ShowViewerToolWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
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

        _ = new ShowViewerToolWindowCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async delegate
        {
            ToolWindowPane window = await _package.ShowToolWindowAsync(typeof(ViewerToolWindowPane), 0, true, _package.DisposalToken);
            if (window?.Frame is null)
            {
                throw new NotSupportedException("Cannot create the C# Game Engine Viewer tool window.");
            }
        });
    }
}
