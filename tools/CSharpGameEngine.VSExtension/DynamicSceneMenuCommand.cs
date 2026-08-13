using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VSCT の DynamicItemStart に対応するコマンド。Visual Studio は連番 ID を問い合わせるため、
/// 一致した ID を <see cref="OleMenuCommand.MatchedCommandId"/> へ必ず保存する。
/// Scene 件数の次の ID で一致を止め、後続の空項目が共有コマンド状態を上書きしないようにする。
/// </summary>
internal sealed class DynamicSceneMenuCommand : OleMenuCommand {
    private readonly int FirstCommandId;
    private readonly Func<int>SceneCountProvider;

    internal DynamicSceneMenuCommand(
        EventHandler InvokeHandler,Guid CommandSet,int FirstCommandId,Func<int> SceneCountProvider)
        :base(InvokeHandler,new CommandID(CommandSet,FirstCommandId)){
        this.FirstCommandId=FirstCommandId;
        this.SceneCountProvider=SceneCountProvider??throw new ArgumentNullException(nameof(SceneCountProvider));
    }

    internal int ItemIndex=>DynamicSceneMenuItemMatcher.GetItemIndex(this.FirstCommandId,this.MatchedCommandId);

    public override bool DynamicItemMatch(int CommandId){
        bool Matched=DynamicSceneMenuItemMatcher.TryMatch(
            this.FirstCommandId,
            CommandId,
            this.SceneCountProvider(),
            out int MatchedCommandId);
        this.MatchedCommandId=MatchedCommandId;
        return Matched;
    }
}