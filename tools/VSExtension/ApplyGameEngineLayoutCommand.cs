using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 要求に応じて「GameEngine」既定パネル配置 (<see cref="GameEngineLayout"/>、ADR-0021) を再適用する Extensions &gt; C# Game Engine メニューコマンド。VS の「Window &gt; Apply Window Layout」には名前付き項目を登録する公開 SDK API がないため必要となる。また、<c>[ProvideToolWindow]</c> の既定ドッキングは特定のハイブでウィンドウを初めて作成するときだけ適用される。VS が実際の配置を保存した後 (<c>.winprf</c>)、パネルを意図した位置に戻す方法はこのコマンドだけである。
/// </summary>
internal sealed class ApplyGameEngineLayoutCommand {
    public const int CommandId=0x0106;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");

    private readonly AsyncPackage Package;

    private ApplyGameEngineLayoutCommand(AsyncPackage Package,OleMenuCommandService CommandService){
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

        _=new ApplyGameEngineLayoutCommand(Package,CommandService);
    }

    private void Execute(object Sender,EventArgs E){
        _= this.Package.JoinableTaskFactory.RunAsync(async delegate {
                                                         await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);

                                                         if(await this.Package.GetServiceAsync(typeof(SDTE))is not DTE Dte){
                                                             return;
                                                         }


                                                         try { await GameEngineLayout.ApplyAsync(this.Package,Dte); }
                                                         catch(Exception Error){
                                                             ActivityLog.LogError(nameof(ApplyGameEngineLayoutCommand),Error.ToString());
                                                         }
                                                     });
    }
}
