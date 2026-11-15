using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Extensions &gt; C# Game Engine &gt; Options... は Scene Editor のオプションページ（<see cref="SceneEditorOptionsPage"/>：移動速度と視点回転の反転設定）を直接開きます。見つけにくい Tools &gt; Options &gt; C# Game Engine の下に置いたままにしません。
/// </summary>
internal sealed class OpenSceneEditorOptionsCommand {
    public const int CommandId=0x0132;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly Package Package;

    private OpenSceneEditorOptionsCommand(Package Package,OleMenuCommandService CommandService){
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

        _=new OpenSceneEditorOptionsCommand(Package,CommandService);
    }

    private void Execute(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Package.ShowOptionPage(typeof(SceneEditorOptionsPage));
    }
}
