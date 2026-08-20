using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0052: <see cref="ComponentPaletteToolWindowPane"/> を表示するツールメニューのコマンド。
/// 構成は <see cref="ShowInspectorToolWindowCommand"/> と同一。
/// </summary>
internal sealed class ShowComponentPaletteToolWindowCommand {
    public const int CommandId=0x010B;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;

    private ShowComponentPaletteToolWindowCommand(AsyncPackage Package,OleMenuCommandService CommandService){
        this.Package= Package ?? throw new ArgumentNullException(nameof(Package));
        if(CommandService is null){
            throw new ArgumentNullException(nameof(CommandService));
        }

        CommandID MenuCommandId=new(CommandSet,CommandId);
        MenuCommand MenuItem=new(this.Execute,MenuCommandId);
        CommandService.AddCommand(MenuItem);
    }

    public static async Task InitializeAsync(AsyncPackage Package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Service= await Package.GetServiceAsync(typeof(IMenuCommandService));
        if(Service is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }

        _=new ShowComponentPaletteToolWindowCommand(Package,CommandService);
    }

    private void Execute(object Sender,EventArgs E){
        _= this.Package.JoinableTaskFactory.RunAsync(async delegate {
                                                         var Window= await this.Package.ShowToolWindowAsync(typeof(ComponentPaletteToolWindowPane),0,true, this.Package.DisposalToken);
                                                         if(Window?.Frame is null){
                                                             throw new NotSupportedException("Cannot create the Component palette tool window.");
                                                         }
                                                     });
    }
}
