using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.CSharp.RuntimeBinder;

namespace CSharpGameEngine.Vsix.Automation;

internal static class VisualStudioDteCommandBridge {
    internal static bool TryRaise(
        int ProcessId,Guid CommandSet,int CommandId,out string Observation) {
        object?Dte=null;
        object?CommandsObject=null;
        object?CommandObject=null;
        var Stage="locating the PID-qualified DTE moniker";
        try {
            if(!TryGetDte(ProcessId,out Dte,out var MonikerName)) {
                Observation="No running DTE moniker matched process "+ProcessId+".";
                return false;
            }
            Stage="reading the DTE command collection";
            dynamic DteValue=Dte!;
            CommandsObject=DteValue.Commands;
            dynamic Commands=CommandsObject;
            var CommandSetText=CommandSet.ToString("B",CultureInfo.InvariantCulture);
            Stage="resolving the registered Scene Designer command";
            CommandObject=Commands.Item(CommandSetText,CommandId);
            dynamic Command=CommandObject;
            Stage="reading Scene Designer command availability";
            if(!(bool)Command.IsAvailable) {
                Observation="DTE "+MonikerName+" found; command "
                    +CommandSetText+":"+CommandId.ToString(CultureInfo.InvariantCulture)
                    +" is registered but not available.";
                return false;
            }
            object?CustomIn=null;
            object?CustomOut=null;
            Stage="raising the Scene Designer command";
            Commands.Raise(CommandSetText,CommandId,CustomIn,CustomOut);
            Observation="Raised command "+CommandSetText+":"
                +CommandId.ToString(CultureInfo.InvariantCulture)
                +" through exact DTE moniker "+MonikerName+".";
            return true;
        } catch(COMException Exception) {
            Observation="Visual Studio DTE was busy or unavailable while "+Stage+": 0x"
                +Exception.HResult.ToString("X8",CultureInfo.InvariantCulture)+" "
                +Exception.Message;
            return false;
        } catch(RuntimeBinderException Exception) {
            Observation="Visual Studio DTE command binding failed while "+Stage+": "
                +Exception.Message;
            return false;
        } finally {
            ReleaseComObject(CommandObject);
            ReleaseComObject(CommandsObject);
            ReleaseComObject(Dte);
        }
    }
    internal static bool IsProcessMoniker(string?DisplayName,int ProcessId) {
        if(string.IsNullOrWhiteSpace(DisplayName)||ProcessId<=0) {
            return false;
        }
        return DisplayName!.IndexOf(
                "VisualStudio.DTE.",StringComparison.OrdinalIgnoreCase)>=0
            &&DisplayName.EndsWith(
                ":"+ProcessId.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }
    private static bool TryGetDte(
        int ProcessId,out object?Dte,out string MonikerName) {
        Dte=null;
        MonikerName=string.Empty;
        IRunningObjectTable?RunningObjectTable=null;
        IBindCtx?BindContext=null;
        IEnumMoniker?Enumerator=null;
        try {
            Marshal.ThrowExceptionForHR(GetRunningObjectTable(0,out RunningObjectTable));
            Marshal.ThrowExceptionForHR(CreateBindCtx(0,out BindContext));
            RunningObjectTable.EnumRunning(out Enumerator);
            var Monikers=new IMoniker[1];
            while(Enumerator.Next(1,Monikers,IntPtr.Zero)==0) {
                var Moniker=Monikers[0];
                try {
                    Moniker.GetDisplayName(BindContext,null,out var DisplayName);
                    if(!IsProcessMoniker(DisplayName,ProcessId)) { continue; }
                    RunningObjectTable.GetObject(Moniker,out Dte);
                    MonikerName=DisplayName;
                    return Dte is not null;
                } finally {
                    ReleaseComObject(Moniker);
                }
            }
            return false;
        } finally {
            ReleaseComObject(Enumerator);
            ReleaseComObject(BindContext);
            ReleaseComObject(RunningObjectTable);
        }
    }
    private static void ReleaseComObject(object?Value) {
        if(Value is not null&&Marshal.IsComObject(Value)) {
            Marshal.ReleaseComObject(Value);
        }
    }
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(
        int Reserved,out IRunningObjectTable RunningObjectTable);
    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int Reserved,out IBindCtx BindContext);
}
