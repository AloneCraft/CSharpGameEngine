using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using CSharpGameEngine.ShaderGraph;
namespace CSharpGameEngine.Vsix.Automation;
internal sealed class VsixExperimentalScenario {
    private const string EditorRootAutomationId="Cge.ShaderGraph.Root";
    private const string NodeAutomationIdPrefix="Cge.ShaderGraph.Node.";
    internal AutomationExitCode Run(AutomationOptions Options) {
        var RepoRoot=ResolveRepoRoot();
        EnsureInteractiveDesktop();
        var ExistingAutomationPids=GetAutomationHiveProcesses().ToArray();
        if(ExistingAutomationPids.Length!=0) { throw new InvalidOperationException("A CgeAutomation Visual Studio process already exists: "+string.Join(",",ExistingAutomationPids)); }
        var VisualStudio=DiscoverVisualStudio();
        var RunId=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
        var RunRoot=Path.GetFullPath(Path.Combine(RepoRoot,Options.EvidenceDirectory,RunId));
        Directory.CreateDirectory(RunRoot);
        var BuildLog=Path.Combine(RunRoot,"build.log");
        RunProcess("dotnet","build "+Quote(Path.Combine(RepoRoot,"tools","CSharpGameEngine.VSExtension","CSharpGameEngine.VSExtension.csproj"))+" -c "+Options.Configuration+" --nologo",RepoRoot,Options.TimeoutSeconds,BuildLog);
        var OutputRoot=Path.Combine(RepoRoot,"tools","CSharpGameEngine.VSExtension","bin",Options.Configuration,"net472");
        var VsixPath=Path.Combine(OutputRoot,"CSharpGameEngine.VSExtension.vsix");
        var BuildDllPath=Path.Combine(OutputRoot,"CSharpGameEngine.VSExtension.dll");
        RequireFile(VsixPath,"VSIX output");
        RequireFile(BuildDllPath,"VSIX build DLL");
        var InstallLog=Path.Combine(RunRoot,"install.log");
        var Script=Path.Combine(RepoRoot,"tools","CSharpGameEngine.VSExtension","Reinstall-CgeFixedVsix.ps1");
        var InstallArguments="-NoProfile -ExecutionPolicy Bypass -File "+Quote(Script)+" -Configuration "+Options.Configuration+" -RootSuffix CgeAutomation -VsixPath "+Quote(VsixPath)+" -SkipBuild -LogPath "+Quote(InstallLog);
        RunProcess("powershell.exe",InstallArguments,RepoRoot,Options.TimeoutSeconds,Path.Combine(RunRoot,"install-process.log"));
        var PackageDllHash=ComputeVsixDllHash(VsixPath);
        var BuildDllHash=VsixEvidenceWriter.ComputeSha256(BuildDllPath);
        var InstalledDll=FindInstalledDll();
        var InstalledDllHash=VsixEvidenceWriter.ComputeSha256(InstalledDll);
        if(!string.Equals(PackageDllHash,BuildDllHash,StringComparison.OrdinalIgnoreCase)||!string.Equals(BuildDllHash,InstalledDllHash,StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"VSIX DLL provenance mismatch. package={PackageDllHash} build={BuildDllHash} installed={InstalledDllHash}");
        }
        var Locales=SelectLocales(VisualStudio,Options.Locale).ToArray();
        if(Locales.Length==0) { throw new InvalidOperationException("The required 1041 Visual Studio locale is not installed."); }
        var OverallPassed=true;
        foreach(var Locale in Locales) {
            var LocaleRoot=Path.Combine(RunRoot,Locale);
            Directory.CreateDirectory(LocaleRoot);
            var Evidence=RunLocaleScenario(RepoRoot,VisualStudio,Options,RunId,Locale,LocaleRoot,VsixPath,PackageDllHash,BuildDllHash,InstalledDllHash);
            var EvidencePath=Path.Combine(LocaleRoot,"evidence.json");
            var EvidenceHash=VsixEvidenceWriter.Write(EvidencePath,Evidence);
            Console.WriteLine($"evidence={EvidencePath} sha256={EvidenceHash}");
            OverallPassed&=Evidence.Status=="Passed";
        }
        return OverallPassed?AutomationExitCode.Success:AutomationExitCode.OracleMismatch;
    }
    private static VsixExperimentalEvidence RunLocaleScenario(string RepoRoot,VisualStudioCandidate VisualStudio,AutomationOptions Options,string RunId,string Locale,string LocaleRoot,string VsixPath,string PackageDllHash,string BuildDllHash,string InstalledDllHash) {
        var ScratchGraph=Path.Combine(LocaleRoot,"Triangle.cgeshadergraph");
        File.Copy(Path.Combine(RepoRoot,"assets","Shaders","Triangle.cgeshadergraph"),ScratchGraph,true);
        var ActivityLog=Path.Combine(LocaleRoot,"ActivityLog.xml");
        var ScreenshotPath=Path.Combine(LocaleRoot,"shader-graph.png");
        var Evidence=new VsixExperimentalEvidence {
            RunId=RunId,Configuration=Options.Configuration,Locale=Locale,RootSuffix=Options.RootSuffix,
            VisualStudioVersion=VisualStudio.Version,PackageSha256=VsixEvidenceWriter.ComputeSha256(VsixPath),
            PackageDllSha256=PackageDllHash,BuildDllSha256=BuildDllHash,InstalledDllSha256=InstalledDllHash,ActivityLogPath=ActivityLog,ScreenshotPath=ScreenshotPath
        };
        Process?VisualStudioProcess=null;
        try {
            var Arguments="/RootSuffix CgeAutomation /LCID "+Locale+" /Log "+Quote(ActivityLog)+" "+Quote(ScratchGraph);
            VisualStudioProcess=Process.Start(new ProcessStartInfo(VisualStudio.ProductPath,Arguments) { WorkingDirectory=RepoRoot,UseShellExecute=false });
            if(VisualStudioProcess is null) { throw new InvalidOperationException("Visual Studio did not start."); }
            Evidence.ProcessId=VisualStudioProcess.Id;
            var Window=WaitForTopLevelWindow(VisualStudioProcess,TimeSpan.FromSeconds(Math.Min(600,Options.TimeoutSeconds)));
            CompleteFirstLaunch(Window);
            ActivateDocumentTab(VisualStudioProcess.Id,Path.GetFileName(ScratchGraph),TimeSpan.FromSeconds(Math.Min(600,Options.TimeoutSeconds)));
            Window=WaitForTopLevelWindow(VisualStudioProcess,TimeSpan.FromSeconds(60));
            Assert(Evidence,"native-window",Window.Current.NativeWindowHandle!=0,"PID-owned top-level NativeWindowHandle="+Window.Current.NativeWindowHandle);
            var Root=WaitForAutomationId(VisualStudioProcess.Id,EditorRootAutomationId,TimeSpan.FromSeconds(Math.Min(600,Options.TimeoutSeconds)));
            Assert(Evidence,"editor-factory",Root is not null,"Shader Graph editor root was exposed by the Editor Factory.");
            var Original=ReadGraph(ScratchGraph);
            var OriginalHash=ShaderGraphCanonicalization.ComputeSemanticHash(Original);
            var Node=WaitForClickableNode(Root!,TimeSpan.FromSeconds(30));
            Assert(Evidence,"node-selection",Node is not null,"A stable node AutomationId was found.");
            Drag(Node!,24,16);
            Thread.Sleep(500);
            Assert(Evidence,"layout-dirty",FindById(Root!,"Cge.ShaderGraph.Save")?.Current.IsEnabled==true,"Node layout edit enabled Save.");
            Invoke(Root!,"Cge.ShaderGraph.Undo");
            Invoke(Root!,"Cge.ShaderGraph.Redo");
            Assert(Evidence,"undo-redo",true,"Native editor Undo and Redo commands completed.");
            Invoke(Root!,"Cge.ShaderGraph.Save");
            WaitUntil(()=>FindById(Root!,"Cge.ShaderGraph.Save")?.Current.IsEnabled==false,TimeSpan.FromSeconds(30),"Save did not clear the dirty state.");
            var Saved=ReadGraph(ScratchGraph);
            Assert(Evidence,"canonical-save",File.ReadAllText(ScratchGraph)==ShaderGraphCanonicalization.ToCanonicalJson(Saved),"Save emitted canonical JSON.");
            Assert(Evidence,"semantic-hash",ShaderGraphCanonicalization.ComputeSemanticHash(Saved)==OriginalHash,"Layout-only save preserved the semantic hash.");
            var CleanReload=ReadGraph(ScratchGraph);
            var ReloadNodeAutomationId=Node!.Current.AutomationId;
            var ReloadNodeId=ReloadNodeAutomationId.Substring(NodeAutomationIdPrefix.Length);
            var ReloadLayout=CleanReload.Layout?.Nodes.FirstOrDefault(Layout=>string.Equals(Layout.NodeId.ToString(),ReloadNodeId,StringComparison.OrdinalIgnoreCase))??throw new OracleMismatchException("Selected node has no editor layout.");
            var NodeLeftBeforeReload=Node.Current.BoundingRectangle.Left;
            ReloadLayout.X+=80;
            PublishExternalGraph(ScratchGraph,CleanReload);
            WaitUntil(()=>{
                var ReloadedNode=FindById(Root!,ReloadNodeAutomationId);
                return ReloadedNode is not null&&Math.Abs(ReloadedNode.Current.BoundingRectangle.Left-NodeLeftBeforeReload)>1;
            },TimeSpan.FromSeconds(30),"Clean external layout change was not observed on the selected node.");
            Assert(Evidence,"clean-reload",true,"Clean external change was observed without a prompt.");
            Node=WaitForClickableNode(Root!,TimeSpan.FromSeconds(30));
            Drag(Node??throw new OracleMismatchException("Node disappeared after clean reload."),24,16);
            WaitUntil(()=>FindById(Root!,"Cge.ShaderGraph.Save")?.Current.IsEnabled==true,TimeSpan.FromSeconds(30),"Second node layout edit did not mark the document dirty.");
            Assert(Evidence,"dirty-before-external",true,"Second layout edit enabled Save before the external change.");
            Thread.Sleep(1200);
            var DirtyExternal=ReadGraph(ScratchGraph);
            if(DirtyExternal.Layout?.Nodes.Count>0) { DirtyExternal.Layout.Nodes[0].Y+=9; }
            PublishExternalGraph(ScratchGraph,DirtyExternal);
            var Dialog=WaitForDialog(VisualStudioProcess.Id,TimeSpan.FromSeconds(30));
            Assert(Evidence,"dirty-reload-dialog",Dialog is not null,"Dirty external change displayed Reload/Keep choice.");
            Assert(Evidence,"reload-option",FindButtonByAutomationId(Dialog!,"6") is not null,"Dirty external-change dialog exposed Reload.");
            Assert(Evidence,"keep-option",FindButtonByAutomationId(Dialog!,"7") is not null,"Dirty external-change dialog exposed Keep.");
            ClickKeep(Dialog!);
            WaitUntil(()=>FindDialog(VisualStudioProcess.Id) is null,TimeSpan.FromSeconds(10),"Keep did not close the external-change dialog.");
            Assert(Evidence,"dirty-keep",FindById(Root!,"Cge.ShaderGraph.Save")?.Current.IsEnabled==true,"Keep preserved the dirty editor state.");
            Invoke(Root!,"Cge.ShaderGraph.Save");
            WaitUntil(()=>FindById(Root!,"Cge.ShaderGraph.Save")?.Current.IsEnabled==false,TimeSpan.FromSeconds(30),"Save after Keep did not clear the dirty state.");
            var SavedAfterKeep=ReadGraph(ScratchGraph);
            Thread.Sleep(1200);
            var Invalid=ReadGraph(ScratchGraph);
            Invalid.Subgraphs[0].Nodes[0].Type="Unknown.Automation.Node";
            PublishExternalGraph(ScratchGraph,Invalid);
            WaitUntil(()=>GetDiagnosticItemCount(FindById(Root!,"Cge.ShaderGraph.Diagnostics"))>0,TimeSpan.FromSeconds(30),"Invalid Graph diagnostics were not displayed.");
            Assert(Evidence,"diagnostics-invalid",true,"Invalid Graph produced a mapped diagnostics entry.");
            Assert(Evidence,"invalid-clean-reload",FindById(Root!,"Cge.ShaderGraph.Save")?.Current.IsEnabled==false,"Invalid Graph reloaded through the clean path.");
            Thread.Sleep(1200);
            PublishExternalGraph(ScratchGraph,SavedAfterKeep);
            var RepairDialog=WaitForDialog(VisualStudioProcess.Id,TimeSpan.FromSeconds(30));
            if(RepairDialog is not null) { ClickReload(RepairDialog); }
            WaitUntil(()=>GetDiagnosticItemCount(FindById(Root!,"Cge.ShaderGraph.Diagnostics"))==0,TimeSpan.FromSeconds(30),"Diagnostics did not clear after repair.");
            Assert(Evidence,"diagnostics-repair",true,"Repair removed the invalid-Graph diagnostic.");
            CaptureWindow(Window.Current.NativeWindowHandle,ScreenshotPath);
            Assert(Evidence,"screenshot",File.Exists(ScreenshotPath)&&new FileInfo(ScreenshotPath).Length>0,"Screenshot captured from PID-owned NativeWindowHandle.");
            WaitUntil(()=>File.Exists(ActivityLog),TimeSpan.FromSeconds(30),"ActivityLog was not created.");
            var Activity=ReadSharedText(ActivityLog);
            var Culture=Locale=="1041"?"ja-JP":"en-US";
            Assert(Evidence,"activity-log",Activity.IndexOf("CSharpGameEngine.VSExtension",StringComparison.OrdinalIgnoreCase)>=0&&Activity.IndexOf(Culture,StringComparison.OrdinalIgnoreCase)>=0&&Activity.IndexOf(BuildDllHash,StringComparison.OrdinalIgnoreCase)>=0,"ActivityLog records the extension, current DLL hash, and UI culture.");
        } catch(OracleMismatchException Exception) {
            Evidence.Assertions.Add(new VsixEvidenceAssertion { Id="scenario",Passed=false,Detail=Exception.Message });
        } finally {
            if(VisualStudioProcess is not null&&!VisualStudioProcess.HasExited) {
                try {
                    var Window=FindTopLevelWindow(VisualStudioProcess.Id);
                    if(Window is not null&&Window.TryGetCurrentPattern(WindowPattern.Pattern,out var Pattern)) { ((WindowPattern)Pattern).Close(); }
                    else { VisualStudioProcess.CloseMainWindow(); }
                } catch(InvalidOperationException) { }
                var ShutdownEnd=DateTime.UtcNow+TimeSpan.FromSeconds(60);
                while(!VisualStudioProcess.HasExited&&DateTime.UtcNow<ShutdownEnd) {
                    var ShutdownDialog=FindShutdownDialog(VisualStudioProcess.Id);
                    if(ShutdownDialog is not null) { ClickDialogButton(ShutdownDialog,"7",["Don't Save","保存しない","いいえ","No"],false); }
                    VisualStudioProcess.WaitForExit(500);
                }
                if(!VisualStudioProcess.HasExited&&OwnedProcessShutdownPolicy.CanForceTerminate(Evidence.ProcessId,VisualStudioProcess.Id,true)) {
                    VisualStudioProcess.Kill();Evidence.ForcedTermination=true;VisualStudioProcess.WaitForExit(10000);
                }
            }
            var NormalShutdown=!Evidence.ForcedTermination;
            Evidence.Assertions.Add(new VsixEvidenceAssertion { Id="normal-shutdown",Passed=NormalShutdown,Detail=NormalShutdown?"Owned Visual Studio process exited normally.":"Owned Visual Studio process required forced termination." });
        }
        Evidence.Status=Evidence.Assertions.All(Assertion=>Assertion.Passed)?"Passed":"Failed";
        return Evidence;
    }
    private static ShaderGraphDocument ReadGraph(string PathValue)=>ShaderGraphCanonicalization.FromJson(File.ReadAllText(PathValue))??throw new OracleMismatchException("Scratch Graph could not be parsed.");
    private static string ReadSharedText(string PathValue) {
        using(var Stream=new FileStream(PathValue,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var Reader=new StreamReader(Stream,Encoding.UTF8,true)) { return Reader.ReadToEnd(); }
    }
    private static void PublishExternalGraph(string PathValue,ShaderGraphDocument Graph) {
        var TemporaryPath=PathValue+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(TemporaryPath,ShaderGraphCanonicalization.ToCanonicalJson(Graph)+Environment.NewLine);
        try {
            File.Delete(PathValue);
            File.Move(TemporaryPath,PathValue);
        }
        finally { if(File.Exists(TemporaryPath)) { File.Delete(TemporaryPath); } }
    }
    private static void Assert(VsixExperimentalEvidence Evidence,string Id,bool Passed,string Detail) {
        Evidence.Assertions.Add(new VsixEvidenceAssertion { Id=Id,Passed=Passed,Detail=Detail });
        if(!Passed) { throw new OracleMismatchException(Id+": "+Detail); }
    }
    private static VisualStudioCandidate DiscoverVisualStudio() {
        var Vswhere=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft Visual Studio","Installer","vswhere.exe");
        RequireFile(Vswhere,"vswhere");
        var Result=RunProcessCapture(Vswhere,"-products * -requires Microsoft.VisualStudio.Component.CoreEditor -format json -utf8",Environment.CurrentDirectory,60);
        using(var Document=JsonDocument.Parse(Result)) {
            var Candidates=new List<VisualStudioCandidate>();
            foreach(var Instance in Document.RootElement.EnumerateArray()) {
                var InstallationPath=Instance.GetProperty("installationPath").GetString()??string.Empty;
                var Version=Instance.TryGetProperty("installationVersion",out var VersionNode)?VersionNode.GetString()??string.Empty:string.Empty;
                var ProductPath=Path.Combine(InstallationPath,"Common7","IDE","devenv.exe");
                Candidates.Add(new VisualStudioCandidate(InstallationPath,ProductPath,Version));
            }
            return VisualStudioCandidateSelector.Select(Candidates,File.Exists)??throw new InvalidOperationException("vswhere returned no Visual Studio installation with devenv.exe.");
        }
    }
    private static IEnumerable<string>SelectLocales(VisualStudioCandidate VisualStudio,string Requested) {
        if(Requested!="auto") {
            if(Directory.Exists(Path.Combine(VisualStudio.InstallationPath,"Common7","IDE",Requested))) { yield return Requested; }
            yield break;
        }
        if(Directory.Exists(Path.Combine(VisualStudio.InstallationPath,"Common7","IDE","1041"))) { yield return "1041"; }
        if(Directory.Exists(Path.Combine(VisualStudio.InstallationPath,"Common7","IDE","1033"))) { yield return "1033"; }
    }
    private static IEnumerable<int>GetAutomationHiveProcesses() {
        using(var Searcher=new ManagementObjectSearcher("SELECT ProcessId,CommandLine FROM Win32_Process WHERE Name='devenv.exe'"))using(var Results=Searcher.Get()) {
            foreach(ManagementObject ProcessObject in Results) {
                var CommandLine=ProcessObject["CommandLine"] as string;
                if(RootSuffixProcessPolicy.IsOwnedAutomationHive(CommandLine)) { yield return Convert.ToInt32((uint)ProcessObject["ProcessId"],CultureInfo.InvariantCulture); }
            }
        }
    }
    private static string FindInstalledDll() {
        var Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","VisualStudio");
        var Candidates=Directory.Exists(Root)?Directory.EnumerateFiles(Root,"CSharpGameEngine.VSExtension.dll",SearchOption.AllDirectories)
            .Where(PathValue=>PathValue.IndexOf("CgeAutomation",StringComparison.OrdinalIgnoreCase)>=0).OrderByDescending(File.GetLastWriteTimeUtc).ToArray():[];
        return Candidates.FirstOrDefault()??throw new InvalidOperationException("The installed CgeAutomation VSIX DLL was not found.");
    }
    private static string ComputeVsixDllHash(string VsixPath) {
        using(var Archive=ZipFile.OpenRead(VsixPath)) {
            var Entry=Archive.Entries.FirstOrDefault(Item=>Item.FullName.EndsWith("CSharpGameEngine.VSExtension.dll",StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException("VSIX package DLL is missing.");
            using(var Stream=Entry.Open())using(var Temporary=new MemoryStream()) {
                Stream.CopyTo(Temporary);Temporary.Position=0;
                using(var Sha=System.Security.Cryptography.SHA256.Create()) { return string.Concat(Sha.ComputeHash(Temporary).Select(Byte=>Byte.ToString("x2",CultureInfo.InvariantCulture))); }
            }
        }
    }
    private static AutomationElement WaitForTopLevelWindow(Process ProcessValue,TimeSpan Timeout) {
        AutomationElement?Window=null;
        WaitUntil(()=>{ Window=FindTopLevelWindow(ProcessValue.Id);return Window is not null; },Timeout,"Visual Studio top-level window was not found for the launched PID.");
        return Window!;
    }
    private static AutomationElement?FindTopLevelWindow(int ProcessId) {
        var Condition=new PropertyCondition(AutomationElement.ProcessIdProperty,ProcessId);
        var Element=AutomationElement.RootElement.FindFirst(TreeScope.Children,Condition);
        if(Element is not null) { return Element; }
        AutomationElement?NativeElement=null;
        EnumWindows((Window,Parameter)=>{
            GetWindowThreadProcessId(Window,out var OwnerProcessId);
            if(OwnerProcessId!=(uint)ProcessId||!IsWindowVisible(Window)||GetWindow(Window,4)!=IntPtr.Zero) { return true; }
            try {
                var Candidate=AutomationElement.FromHandle(Window);
                if(Candidate is not null&&Candidate.Current.ProcessId==ProcessId) { NativeElement=Candidate;return false; }
            } catch(ElementNotAvailableException) { }
            return true;
        },IntPtr.Zero);
        return NativeElement;
    }
    private static AutomationElement?WaitForAutomationId(int ProcessId,string AutomationId,TimeSpan Timeout) {
        AutomationElement?Element=null;
        WaitUntil(()=>{ var Window=FindTopLevelWindow(ProcessId);Element=Window is null?null:FindById(Window,AutomationId);return Element is not null; },Timeout,AutomationId+" was not found.");
        return Element;
    }
    private static void CompleteFirstLaunch(AutomationElement Window) {
        var Finish=FindById(Window,"FinishButton");
        if(Finish is null) { return; }
        if(!Finish.TryGetCurrentPattern(InvokePattern.Pattern,out var Pattern)) { throw new InvalidOperationException("Visual Studio first-launch FinishButton has no InvokePattern."); }
        ((InvokePattern)Pattern).Invoke();
        Thread.Sleep(3000);
    }
    private static void ActivateDocumentTab(int ProcessId,string FileName,TimeSpan Timeout) {
        WaitUntil(()=>{
            var Window=FindTopLevelWindow(ProcessId);
            if(Window is null) { return false; }
            if(FindById(Window,EditorRootAutomationId) is not null) { return true; }
            var Conditions=new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem),new PropertyCondition(AutomationElement.NameProperty,FileName));
            var Tab=Window.FindFirst(TreeScope.Descendants,Conditions);
            if(Tab is null||!Tab.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var Pattern)) { return false; }
            ((SelectionItemPattern)Pattern).Select();
            Thread.Sleep(500);
            return true;
        },Timeout,"The scratch Shader Graph document tab was not opened.");
    }
    private static AutomationElement?FindById(AutomationElement Root,string AutomationId)=>Root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,AutomationId));
    private static AutomationElement?FindByAutomationIdPrefix(AutomationElement Root,string Prefix) {
        var All=Root.FindAll(TreeScope.Descendants,Condition.TrueCondition);
        for(var Index=0;Index<All.Count;Index++) { if(((string)All[Index].GetCurrentPropertyValue(AutomationElement.AutomationIdProperty)).StartsWith(Prefix,StringComparison.Ordinal)) { return All[Index]; } }
        return null;
    }
    private static AutomationElement?WaitForClickableNode(AutomationElement Root,TimeSpan Timeout) {
        AutomationElement?Node=null;
        WaitUntil(()=>{
            var All=Root.FindAll(TreeScope.Descendants,Condition.TrueCondition);
            for(var Index=0;Index<All.Count;Index++) {
                var AutomationId=(string)All[Index].GetCurrentPropertyValue(AutomationElement.AutomationIdProperty);
                if(!AutomationId.StartsWith(NodeAutomationIdPrefix,StringComparison.Ordinal)) { continue; }
                var Rectangle=All[Index].Current.BoundingRectangle;
                if(Rectangle.Width<=0||Rectangle.Height<=0||double.IsNaN(Rectangle.Left)||double.IsNaN(Rectangle.Top)) { continue; }
                if(All[Index].TryGetCurrentPattern(TransformPattern.Pattern,out _)||All[Index].TryGetClickablePoint(out _)) { Node=All[Index];return true; }
            }
            return false;
        },Timeout,"A movable Shader Graph node was not found.");
        return Node;
    }
    private static AutomationElement?WaitForDialog(int ProcessId,TimeSpan Timeout) {
        var End=DateTime.UtcNow+Timeout;
        while(DateTime.UtcNow<End) {
            var Dialog=FindDialog(ProcessId);
            if(Dialog is not null) { return Dialog; }
            Thread.Sleep(200);
        }
        return null;
    }
    private static AutomationElement?FindDialog(int ProcessId) {
        foreach(var Window in EnumerateProcessWindows(ProcessId)) {
            var NativeHandle=new IntPtr(Window.Current.NativeWindowHandle);
            if(NativeHandle!=IntPtr.Zero&&GetWindow(NativeHandle,4)!=IntPtr.Zero) {
                    var Buttons=Window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
                    var ButtonIds=new HashSet<string>(Enumerable.Range(0,Buttons.Count).Select(Index=>Buttons[Index].Current.AutomationId),StringComparer.Ordinal);
                    if(ButtonIds.Contains("6")&&ButtonIds.Contains("7")) { return Window; }
                }
        }
        return null;
    }
    private static AutomationElement?FindShutdownDialog(int ProcessId) {
        foreach(var Window in EnumerateProcessWindows(ProcessId)) {
            var NativeHandle=new IntPtr(Window.Current.NativeWindowHandle);
            if(NativeHandle==IntPtr.Zero||GetWindow(NativeHandle,4)==IntPtr.Zero) { continue; }
            var Buttons=Window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
            if(Buttons.Count>0) { return Window; }
        }
        return null;
    }
    private static IReadOnlyList<AutomationElement>EnumerateProcessWindows(int ProcessId) {
        var Windows=new List<AutomationElement>();
        EnumWindows((Window,Parameter)=>{
            GetWindowThreadProcessId(Window,out var OwnerProcessId);
            if(OwnerProcessId!=(uint)ProcessId||!IsWindowVisible(Window)) { return true; }
            try {
                var Element=AutomationElement.FromHandle(Window);
                if(Element is not null&&Element.Current.ProcessId==ProcessId) { Windows.Add(Element); }
            } catch(ElementNotAvailableException) { }
            return true;
        },IntPtr.Zero);
        return Windows;
    }
    private static void Invoke(AutomationElement Root,string AutomationId) {
        var Element=FindById(Root,AutomationId)??throw new OracleMismatchException(AutomationId+" was not found.");
        if(!Element.TryGetCurrentPattern(InvokePattern.Pattern,out var Pattern)) { throw new OracleMismatchException(AutomationId+" has no InvokePattern."); }
        ((InvokePattern)Pattern).Invoke();Thread.Sleep(300);
    }
    private static void Click(AutomationElement Element) {
        if(!Element.TryGetClickablePoint(out var PointValue)) { throw new OracleMismatchException("Element has no clickable point: "+Element.Current.AutomationId); }
        SetCursorPos((int)PointValue.X,(int)PointValue.Y);mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);Thread.Sleep(250);
    }
    private static void Drag(AutomationElement Element,int DeltaX,int DeltaY) {
        if(Element.TryGetCurrentPattern(TransformPattern.Pattern,out var TransformPatternValue)) {
            var Rectangle=Element.Current.BoundingRectangle;
            try {
                ((TransformPattern)TransformPatternValue).Move(Rectangle.Left+DeltaX,Rectangle.Top+DeltaY);
                Thread.Sleep(400);
                return;
            } catch(InvalidOperationException) { }
        }
        if(!Element.TryGetClickablePoint(out var PointValue)) { throw new OracleMismatchException("Node has no clickable point."); }
        SetCursorPos((int)PointValue.X,(int)PointValue.Y);mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(100);SetCursorPos((int)PointValue.X+DeltaX,(int)PointValue.Y+DeltaY);Thread.Sleep(150);mouse_event(4,0,0,0,UIntPtr.Zero);Thread.Sleep(400);
    }
    private static void ClickKeep(AutomationElement Dialog)=>ClickDialogButton(Dialog,"7",["Keep","維持","いいえ","No"],false);
    private static void ClickReload(AutomationElement Dialog)=>ClickDialogButton(Dialog,"6",["Reload","再読み込み","はい","Yes"],true);
    private static void ClickDialogButton(AutomationElement Dialog,string PreferredAutomationId,IReadOnlyList<string>Names,bool FirstFallback) {
        var Buttons=Dialog.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
        for(var Index=0;Index<Buttons.Count;Index++) {
            if(string.Equals(Buttons[Index].Current.AutomationId,PreferredAutomationId,StringComparison.Ordinal)) { Activate(Buttons[Index]);return; }
        }
        for(var Index=0;Index<Buttons.Count;Index++) {
            if(Names.Any(Name=>Buttons[Index].Current.Name.IndexOf(Name,StringComparison.OrdinalIgnoreCase)>=0)) { Activate(Buttons[Index]);return; }
        }
        if(Buttons.Count==0) { throw new OracleMismatchException("Reload/Keep dialog has no buttons."); }
        Activate(Buttons[FirstFallback?0:Buttons.Count-1]);
    }
    private static AutomationElement?FindButtonByAutomationId(AutomationElement Dialog,string AutomationId) {
        var Buttons=Dialog.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
        for(var Index=0;Index<Buttons.Count;Index++) { if(string.Equals(Buttons[Index].Current.AutomationId,AutomationId,StringComparison.Ordinal)) { return Buttons[Index]; } }
        return null;
    }
    private static void Activate(AutomationElement Element) {
        if(Element.TryGetCurrentPattern(InvokePattern.Pattern,out var Pattern)) { ((InvokePattern)Pattern).Invoke();Thread.Sleep(250);return; }
        Click(Element);
    }
    private static string GetElementText(AutomationElement?Element) {
        if(Element is null) { return string.Empty; }
        var Values=new List<string>();var OwnName=Element.Current.Name;
        if(!string.IsNullOrWhiteSpace(OwnName)) { Values.Add(OwnName); }
        var Descendants=Element.FindAll(TreeScope.Descendants,Condition.TrueCondition);
        for(var Index=0;Index<Descendants.Count;Index++) { var Name=Descendants[Index].Current.Name;if(!string.IsNullOrWhiteSpace(Name)) { Values.Add(Name); } }
        return string.Join("|",Values);
    }
    private static int GetDiagnosticItemCount(AutomationElement?Element)=>Element?.FindAll(TreeScope.Children,Condition.TrueCondition).Count??0;
    private static void CaptureWindow(int NativeHandle,string PathValue) {
        var Handle=new IntPtr(NativeHandle);
        if(!GetWindowRect(Handle,out var Rect)) { throw new OracleMismatchException("GetWindowRect failed."); }
        var Width=Math.Max(1,Rect.Right-Rect.Left);var Height=Math.Max(1,Rect.Bottom-Rect.Top);
        using(var BitmapValue=new Bitmap(Width,Height))using(var GraphicsValue=Graphics.FromImage(BitmapValue)) {
            GraphicsValue.CopyFromScreen(Rect.Left,Rect.Top,0,0,new System.Drawing.Size(Width,Height));BitmapValue.Save(PathValue,System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    private static void WaitUntil(Func<bool>Predicate,TimeSpan Timeout,string Error) {
        var End=DateTime.UtcNow+Timeout;
        while(DateTime.UtcNow<End) { try { if(Predicate()) { return; } } catch(ElementNotAvailableException) { } catch(COMException) { } Thread.Sleep(200); }
        throw new OracleMismatchException(Error);
    }
    private static void RunProcess(string FileName,string Arguments,string WorkingDirectory,int TimeoutSeconds,string LogPath) {
        var Output=RunProcessCapture(FileName,Arguments,WorkingDirectory,TimeoutSeconds);
        File.WriteAllText(LogPath,Output);
    }
    private static string RunProcessCapture(string FileName,string Arguments,string WorkingDirectory,int TimeoutSeconds) {
        var StartInfo=new ProcessStartInfo(FileName,Arguments) { WorkingDirectory=WorkingDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
        using(var Child=Process.Start(StartInfo)??throw new InvalidOperationException("Failed to start "+FileName)) {
            var Output=new StringBuilder();Child.OutputDataReceived+=(Sender,Event)=>{ if(Event.Data is not null) { Output.AppendLine(Event.Data); } };Child.ErrorDataReceived+=(Sender,Event)=>{ if(Event.Data is not null) { Output.AppendLine(Event.Data); } };
            Child.BeginOutputReadLine();Child.BeginErrorReadLine();
            if(!Child.WaitForExit(checked(TimeoutSeconds*1000))) { Child.Kill();throw new TimeoutException(FileName+" exceeded its timeout."); }
            Child.WaitForExit();
            if(Child.ExitCode!=0) { throw new InvalidOperationException(FileName+" failed with exit code "+Child.ExitCode+Environment.NewLine+Output); }
            return Output.ToString();
        }
    }
    private static string ResolveRepoRoot() {
        var Current=new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while(Current is not null) { if(File.Exists(Path.Combine(Current.FullName,"CSharpGameEngine.slnx"))) { return Current.FullName; }Current=Current.Parent; }
        Current=new DirectoryInfo(Environment.CurrentDirectory);
        while(Current is not null) { if(File.Exists(Path.Combine(Current.FullName,"CSharpGameEngine.slnx"))) { return Current.FullName; }Current=Current.Parent; }
        throw new InvalidOperationException("Repository root could not be resolved.");
    }
    private static void EnsureInteractiveDesktop() {
        if(!Environment.UserInteractive) { throw new InvalidOperationException("An interactive Windows desktop is required."); }
        var Desktop=OpenInputDesktop(0,false,0x0100);
        if(Desktop==IntPtr.Zero) { throw new InvalidOperationException("The input desktop is unavailable or locked."); }
        CloseDesktop(Desktop);
    }
    private static void RequireFile(string PathValue,string Description) { if(!File.Exists(PathValue)) { throw new FileNotFoundException(Description+" is missing.",PathValue); } }
    private static string Quote(string Value)=>string.Concat((char)34,Value,(char)34);
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect { internal int Left;internal int Top;internal int Right;internal int Bottom; }
    [DllImport("user32.dll")]private static extern IntPtr OpenInputDesktop(uint Flags,bool Inherit,uint DesiredAccess);
    [DllImport("user32.dll")]private static extern bool CloseDesktop(IntPtr Desktop);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int X,int Y);
    [DllImport("user32.dll")]private static extern void mouse_event(uint Flags,uint Dx,uint Dy,uint Data,UIntPtr ExtraInfo);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr Window,out NativeRect Rect);
    private delegate bool EnumWindowsCallback(IntPtr Window,IntPtr Parameter);
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumWindowsCallback Callback,IntPtr Parameter);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr Window,out uint ProcessId);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr Window);
    [DllImport("user32.dll")]private static extern IntPtr GetWindow(IntPtr Window,uint Command);
}
