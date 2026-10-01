using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Process=System.Diagnostics.Process;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0097: Extensions &gt; C# Game Engine &gt; Diagnostic Tools (and the same dropdown on the toolbar) call Visual
/// Studio's own debugger and profiler for a game started as an exe. They replace the removed runtime diagnostics
/// snapshot. A game started from a build already has the debugger, so attaching offers only the other games.
/// CPU and .NET allocation measurements run the standard collector (<see cref="VsDiagnosticsCollector"/>); the same
/// command stops the measurement and opens the report. GPU usage has no collector configuration, so its command
/// opens the Performance Profiler for the user to pick it.
/// </summary>
internal sealed class GameDiagnosticsCommands {
    public const int AttachDebuggerCommandId=0x0133;
    public const int MeasureCpuUsageCommandId=0x0134;
    public const int MeasureDotNetAllocationsCommandId=0x0135;
    public const int MeasureGpuUsageCommandId=0x0136;
    public static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");
    private const int CollectorTimeoutMilliseconds=60_000;
    /// <summary>
    /// The debug engine for a game: managed .NET (CoreCLR). Adding the native engine made Visual Studio stop responding
    /// for more than ten minutes when attaching to an engine process (measured in Visual Studio 18), so native debugging
    /// of D3D12 or XAudio2 stays with Visual Studio's own Attach to Process dialog.
    /// </summary>
    private static readonly string[]AttachEngineIds=["{2E36F1D4-B23C-435D-AB41-18E608940038}"];

    private sealed class Measurement(GameDiagnosticsKind Kind,GameProcessInfo Game,Guid Session,string Collector,string Output,Process?Watched) {
        internal GameDiagnosticsKind Kind { get; }=Kind;
        internal GameProcessInfo Game { get; }=Game;
        internal Guid Session { get; }=Session;
        internal string Collector { get; }=Collector;
        internal string Output { get; }=Output;
        internal Process?Watched { get; }=Watched;
    }

    private readonly AsyncPackage Package;
    private Measurement?Active;

    private GameDiagnosticsCommands(AsyncPackage Package,OleMenuCommandService CommandService){
        this.Package=Package;
        Add(CommandService,AttachDebuggerCommandId,GameDiagnosticsKind.Debugger,null);
        Add(CommandService,MeasureCpuUsageCommandId,GameDiagnosticsKind.CpuUsage,"Diagnostics_MeasureCpuUsage");
        Add(CommandService,MeasureDotNetAllocationsCommandId,GameDiagnosticsKind.DotNetAllocations,"Diagnostics_MeasureDotNetAllocations");
        Add(CommandService,MeasureGpuUsageCommandId,GameDiagnosticsKind.GpuUsage,null);

        void Add(OleMenuCommandService Service,int Id,GameDiagnosticsKind Kind,string?IdleTextKey){
            OleMenuCommand Command=new((_,_)=>this.Execute(Kind),new CommandID(CommandSet,Id));
            if(IdleTextKey is not null){
                Command.BeforeQueryStatus+=(_,_)=>{
                    ThreadHelper.ThrowIfNotOnUIThread();
                    bool Measuring=this.Active?.Kind==Kind;
                    Command.Text=VsixText.Get(Measuring?"Diagnostics_StopMeasuring":IdleTextKey);
                    // One measurement at a time; the other measuring command waits for it.
                    Command.Enabled=this.Active is null||Measuring;
                };
            }
            Service.AddCommand(Command);
        }
    }

    public static async Task InitializeAsync(AsyncPackage Package){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        if(await Package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService CommandService){
            throw new InvalidOperationException($"{nameof(IMenuCommandService)} is not available from the VS shell.");
        }
        _=new GameDiagnosticsCommands(Package,CommandService);
    }

    private void Execute(GameDiagnosticsKind Kind){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Active is { }Measuring&&Measuring.Kind==Kind){
            _=this.Package.JoinableTaskFactory.RunAsync(()=>this.StopAsync(Measuring,GameExited:false));
            return;
        }
        if(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SDTE)) is not DTE2 Dte){ return; }
        if(Kind==GameDiagnosticsKind.GpuUsage){
            this.OpenPerformanceProfiler(Dte);
            return;
        }
        if(this.ChooseGame(Dte,Kind) is not { }Game){ return; }
        if(Kind==GameDiagnosticsKind.Debugger){
            this.Attach(Dte,Game);
            return;
        }
        _=this.Package.JoinableTaskFactory.RunAsync(()=>this.StartAsync(Dte,Kind,Game));
    }

    /// <summary>The running game the user picks; null when there is none or the user cancels.</summary>
    private GameProcessInfo?ChooseGame(DTE2 Dte,GameDiagnosticsKind Kind){
        ThreadHelper.ThrowIfNotOnUIThread();
        IReadOnlyList<CgeProjectContext>Projects=CgeProjectContext.FindAll(out _);
        if(Projects.Count==0){
            this.ShowMessage(VsixText.Get("Diagnostics_NoGameProject"),OLEMSGICON.OLEMSGICON_INFO);
            return null;
        }
        string[]Names=[..Projects.Select(static Project=>Project.AssemblyName).Distinct(StringComparer.OrdinalIgnoreCase)];
        HashSet<int>Debugged=[];
        foreach(EnvDTE.Process Debuggee in Dte.Debugger.DebuggedProcesses){ Debugged.Add(Debuggee.ProcessID); }
        IReadOnlyList<GameProcessInfo>Candidates=GameProcessCandidates.Select(
            RunningProcesses(Names),Names,Debugged,Kind,out int AlreadyDebugged);
        if(Candidates.Count==0){
            this.ShowMessage(AlreadyDebugged>0
                ?VsixText.Get("Diagnostics_AlreadyDebugged")
                :VsixText.Format("Diagnostics_NoRunningGame",string.Join(", ",Names.Select(static Name=>Name+".exe"))),
                OLEMSGICON.OLEMSGICON_INFO);
            return null;
        }
        return Pick(Candidates,VsixText.Get(Kind switch {
            GameDiagnosticsKind.Debugger=>"Diagnostics_AttachDebugger",
            GameDiagnosticsKind.CpuUsage=>"Diagnostics_MeasureCpuUsage",
            _=>"Diagnostics_MeasureDotNetAllocations",
        }));
    }

    private static IEnumerable<GameProcessInfo>RunningProcesses(IEnumerable<string>Names){
        foreach(string Name in Names){
            foreach(Process Process in Process.GetProcessesByName(Name)){
                using(Process){
                    DateTime?Started=null;
                    string?Path=null;
                    try { Started=Process.StartTime; } catch(Exception Error) when(Error is InvalidOperationException or System.ComponentModel.Win32Exception){ }
                    try { Path=Process.MainModule?.FileName; } catch(Exception Error) when(Error is InvalidOperationException or System.ComponentModel.Win32Exception){ }
                    yield return new GameProcessInfo(Process.Id,Process.ProcessName,Started,Path);
                }
            }
        }
    }

    /// <summary>A small modal list of the candidates; one candidate is still shown, so the user confirms the target.</summary>
    private static GameProcessInfo?Pick(IReadOnlyList<GameProcessInfo>Candidates,string Action){
        ThreadHelper.ThrowIfNotOnUIThread();
        ListBox List=new(){ MinHeight=80,MaxHeight=240,Margin=new Thickness(0,6,0,10) };
        foreach(GameProcessInfo Candidate in Candidates){
            List.Items.Add(new ListBoxItem {
                Tag=Candidate,
                Content=VsixText.Format("Diagnostics_ProcessRow",Candidate.Name,Candidate.ProcessId,
                    Candidate.StartTime?.ToString("T")??"?",Candidate.Path??string.Empty),
            });
        }
        List.SelectedIndex=0;
        DialogWindow Dialog=new(){
            Title=VsixText.Get("Diagnostics_ChooseTitle"),Width=560,SizeToContent=SizeToContent.Height,
            ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,
        };
        Button Ok=new(){ Content=VsixText.Get("Diagnostics_Choose"),IsDefault=true,MinWidth=80,Margin=new Thickness(0,0,8,0) };
        Button Cancel=new(){ Content=VsixText.Get("Diagnostics_Cancel"),IsCancel=true,MinWidth=80 };
        Ok.Click+=(_,_)=>Dialog.DialogResult=true;
        List.MouseDoubleClick+=(_,_)=>Dialog.DialogResult=true;
        StackPanel Buttons=new(){ Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
        Buttons.Children.Add(Ok);
        Buttons.Children.Add(Cancel);
        StackPanel Body=new(){ Margin=new Thickness(12) };
        Body.Children.Add(new TextBlock { Text=VsixText.Format("Diagnostics_ChoosePrompt",Action),TextWrapping=TextWrapping.Wrap });
        Body.Children.Add(List);
        Body.Children.Add(Buttons);
        Dialog.Content=Body;
        return Dialog.ShowModal()==true&&List.SelectedItem is ListBoxItem{ Tag:GameProcessInfo Chosen }?Chosen:null;
    }

    private void Attach(DTE2 Dte,GameProcessInfo Game){
        ThreadHelper.ThrowIfNotOnUIThread();
        try {
            Debugger2 Debugger=(Debugger2)Dte.Debugger;
            Process2?Target=null;
            foreach(Process2 Candidate in Debugger.LocalProcesses){
                if(Candidate.ProcessID==Game.ProcessId){ Target=Candidate; break; }
            }
            if(Target is null){
                this.ShowMessage(VsixText.Format("Diagnostics_AttachFailed",Game.Name,VsixText.Get("Diagnostics_ProcessGone")),OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }
            // Engines are matched by id, because their names are localized, and found through the process's own
            // transport, because Transports.Item("Default") is not a valid index (both measured in Visual Studio 18).
            List<string>Engines=[];
            foreach(Engine Engine in Target.Transport.Engines){
                if(AttachEngineIds.Contains(Engine.ID,StringComparer.OrdinalIgnoreCase)){ Engines.Add(Engine.ID); }
            }
            if(Engines.Count>0){ Target.Attach2(Engines.ToArray()); }
            else { Target.Attach(); }
            this.SetStatus(VsixText.Format("Diagnostics_Attached",Game.Name,Game.ProcessId));
        } catch(Exception Error) when(Error is System.Runtime.InteropServices.COMException or InvalidCastException or ArgumentException){
            this.ShowMessage(VsixText.Format("Diagnostics_AttachFailed",Game.Name,Error.Message),OLEMSGICON.OLEMSGICON_WARNING);
        }
    }

    private async Task StartAsync(DTE2 Dte,GameDiagnosticsKind Kind,GameProcessInfo Game){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        string?Collector=VsDiagnosticsCollector.Find(Dte.FullName,File.Exists);
        if(Collector is null){
            this.ShowMessage(VsixText.Format("Diagnostics_CollectorMissing",VsDiagnosticsCollector.RelativePath),OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }
        CgeProjectContext.FindAll(out string?Root);
        string Output=VsDiagnosticsCollector.OutputPath(Root??Path.GetTempPath(),Game.Name,Kind,DateTime.Now);
        Guid Session=Guid.NewGuid();
        Measurement Pending=new(Kind,Game,Session,Collector,Output,null);
        this.Active=Pending;
        (int ExitCode,string Text)=await RunCollectorAsync(Collector,VsDiagnosticsCollector.StartArguments(Session,Game.ProcessId,Collector,Kind));
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(ExitCode!=0){
            this.Active=null;
            this.ShowMessage(VsixText.Format("Diagnostics_CollectorFailed",Text),OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }
        Process?Watched=null;
        try {
            Watched=Process.GetProcessById(Game.ProcessId);
            Watched.EnableRaisingEvents=true;
        } catch(Exception Error) when(Error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){
            Watched?.Dispose();
            Watched=null;
        }
        Measurement Running=new(Kind,Game,Session,Collector,Output,Watched);
        this.Active=Running;
        if(Watched is not null){
            Watched.Exited+=(_,_)=>_=this.Package.JoinableTaskFactory.RunAsync(()=>this.StopAsync(Running,GameExited:true));
        }
        this.SetStatus(VsixText.Format("Diagnostics_MeasuringStarted",
            VsixText.Get(Kind==GameDiagnosticsKind.CpuUsage?"Diagnostics_KindCpu":"Diagnostics_KindDotNet"),Game.Name,Game.ProcessId));
    }

    private async Task StopAsync(Measurement Measuring,bool GameExited){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(!ReferenceEquals(this.Active,Measuring)){ return; }
        this.Active=null;
        Measuring.Watched?.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(Measuring.Output)!);
        (int ExitCode,string Text)=await RunCollectorAsync(Measuring.Collector,VsDiagnosticsCollector.StopArguments(Measuring.Session,Measuring.Output));
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(ExitCode!=0||!File.Exists(Measuring.Output)){
            this.ShowMessage(VsixText.Format("Diagnostics_CollectorFailed",Text),OLEMSGICON.OLEMSGICON_WARNING);
            return;
        }
        if(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SDTE)) is DTE2 Dte){
            Dte.ItemOperations.OpenFile(Measuring.Output);
        }
        this.SetStatus(VsixText.Format(GameExited?"Diagnostics_MeasuringStoppedOnExit":"Diagnostics_MeasuringStopped",Measuring.Output));
    }

    /// <summary>Runs the collector off the UI thread and returns its exit code and output.</summary>
    private static async Task<(int ExitCode,string Text)>RunCollectorAsync(string Collector,string Arguments){
        await TaskScheduler.Default;
        ProcessStartInfo Start=new(Collector,Arguments){
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
        };
        try {
            using Process Process=Process.Start(Start)!;
            Task<string>Output=Process.StandardOutput.ReadToEndAsync();
            Task<string>Error=Process.StandardError.ReadToEndAsync();
            if(!Process.WaitForExit(CollectorTimeoutMilliseconds)){
                try { Process.Kill(); } catch(InvalidOperationException){ }
                return(-1,"VSDiagnostics.exe "+Arguments+": timed out");
            }
            StringBuilder Text=new();
            Text.Append((await Output).Trim());
            if((await Error).Trim() is { Length:>0 }Message){ Text.AppendLine().Append(Message); }
            return(Process.ExitCode,Text.ToString());
        } catch(Exception Failure) when(Failure is System.ComponentModel.Win32Exception or InvalidOperationException){
            return(-1,Failure.Message);
        }
    }

    private void OpenPerformanceProfiler(DTE2 Dte){
        ThreadHelper.ThrowIfNotOnUIThread();
        try {
            // The Performance Profiler (Alt+F2). "Debug.PerformanceProfiler" does not exist in Visual Studio 18 (measured).
            Dte.ExecuteCommand("Debug.DiagnosticsHub.Launch");
            this.ShowMessage(VsixText.Get("Diagnostics_GpuGuide"),OLEMSGICON.OLEMSGICON_INFO);
        } catch(Exception Error) when(Error is System.Runtime.InteropServices.COMException or ArgumentException){
            this.ShowMessage(VsixText.Format("Diagnostics_ProfilerUnavailable",Error.Message),OLEMSGICON.OLEMSGICON_WARNING);
        }
    }

    private void ShowMessage(string Message,OLEMSGICON Icon){
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(this.Package,Message,VsixText.Get("Diagnostics_Title"),Icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private void SetStatus(string Message){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar StatusBar){ StatusBar.SetText(Message); }
    }
}
