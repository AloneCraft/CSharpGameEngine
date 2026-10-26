using System.Globalization;

namespace CSharpGameEngine.Bridge.Hosting;

/// <summary>
/// 常駐ブリッジのコマンドライン：<c>--serve &lt;pipe&gt; [--parent-process-id &lt;pid&gt;]</c>。所有者はブリッジを起動した Visual Studio プロセスであり、ブリッジはその終了時に終了する。
/// </summary>
public static class BridgeServeCommand {
    public static bool TryParse(string[]Arguments,out string PipeName,out int?OwnerProcessId){
        ArgumentNullException.ThrowIfNull(Arguments);
        PipeName=string.Empty;
        OwnerProcessId=null;
        if(Arguments is ["--serve",var Pipe]){
            PipeName=Pipe;
            return true;
        }
        if(Arguments is ["--serve",var OwnedPipe,"--parent-process-id",var Owner]
            &&int.TryParse(Owner,NumberStyles.None,CultureInfo.InvariantCulture,out var Id)&&Id>0){
            PipeName=OwnedPipe;
            OwnerProcessId=Id;
            return true;
        }
        return false;
    }
}
