using System.Globalization;

namespace CSharpGameEngine.Bridge.Hosting;

/// <summary>
/// The resident bridge command line: <c>--serve &lt;pipe&gt; [--parent-process-id &lt;pid&gt;]</c>.
/// The owner is the Visual Studio process that started the bridge; the bridge ends with it.
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
