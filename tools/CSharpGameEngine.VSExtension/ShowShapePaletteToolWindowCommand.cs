using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools-menu command that shows/creates <see cref="ShapePaletteToolWindowPane"/> (ADR-0021) - a
/// manual fallback since the package also shows it as part of the first-run-only default layout
/// (<see cref="GameEngineLayout"/>).
/// </summary>
internal sealed class ShowShapePaletteToolWindowCommand {
    public const int CommandId=0x0107;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;

    private ShowShapePaletteToolWindowCommand(AsyncPackage Package,OleMenuCommandService CommandService){
        this.Package= Package ?? throw new ArgumentNullException(nameof(Package));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }

        CommandID MenuCommandId=new(CommandSet,CommandId);
        MenuCommand MenuItem=new(Execute,MenuCommandId);
        CommandService.AddCommand(MenuItem);
    }

    public static async Task InitializeAsync(AsyncPackage Package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Service= await Package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _=new ShowShapePaletteToolWindowCommand(Package,CommandService);
    }

    private void Execute(object Sender,EventArgs E){
        _=Package.JoinableTaskFactory.RunAsync(async delegate {
            ToolWindowPane Window= await Package.ShowToolWindowAsync(typeof(ShapePaletteToolWindowPane),0,true,Package.DisposalToken);
            if(Window?.Frame is null){
                throw new NotSupportedException("Cannot create the Shape Palette tool window.");
            }
        });
    }
}
