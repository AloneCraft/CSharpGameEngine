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
    private static readonly SceneObservationTarget[]SceneObservationTargets=[
        new("EngineShowcaseScene","EngineShowcaseGallery"),
        new("EngineShowcaseRigidBottomScene","Physics_Stack_0"),
        new("EngineShowcaseRigidMiddleScene","Physics_Stack_1"),
        new("EngineShowcaseRigidTopScene","Physics_Stack_2"),
        new("EngineShowcaseProjectileScene","Physics_CCD_Projectile"),
        new("FirstPersonStageScene","Lvl_FirstPerson"),
        new("MainScene","Application"),
        new("PhysicsGroundScene","Physics_Ground"),
        new("PhysicsStackBottomScene","Physics_Stack_0"),
        new("PhysicsStackMiddleScene","Physics_Stack_1"),
        new("PhysicsStackTopScene","Physics_Stack_2"),
        new("PhysicsProjectileScene","Physics_Projectile"),
        new("PhysicsPrefractureWallScene","Physics_PrefractureWall"),
        new("PhysicsCachePlaybackScene","Physics_CachePlayback"),
        new("PrimitiveShowcaseBoxScene","ShowcaseBox"),
        new("PrimitiveShowcaseSphereScene","ShowcaseSphere"),
    ];
    internal AutomationExitCode Run(AutomationOptions Options) {
        if(Options.Scenario=="scene-observation") {
            return this.RunSceneObservation(Options);
        }
        var RepoRoot=ResolveRepoRoot();
        EnsureInteractiveDesktop();
        var ExistingAutomationPids=GetAutomationHiveProcesses().ToArray();
        if(ExistingAutomationPids.Length!=0) { throw new InvalidOperationException("A CgeAutomation Visual Studio process already exists: "+string.Join(",",ExistingAutomationPids)); }
        var VisualStudio=DiscoverVisualStudio();
        var RunId=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
        var RunRoot=Path.GetFullPath(Path.Combine(RepoRoot,Options.EvidenceDirectory,RunId));
        Directory.CreateDirectory(RunRoot);
        var BuildLog=Path.Combine(RunRoot,"build.log");
        if(Options.Configuration=="Release"&&!Options.SkipBuild) {
            throw new InvalidOperationException(
                "Release observation requires --skip-build and the frozen VSIX release input.");
        }
        if(!Options.SkipBuild) {
            RunProcess("dotnet","build "+Quote(Path.Combine(RepoRoot,"tools","CSharpGameEngine.VSExtension","CSharpGameEngine.VSExtension.csproj"))+" -c "+Options.Configuration+" --nologo",RepoRoot,Options.TimeoutSeconds,BuildLog);
        } else {
            File.WriteAllText(BuildLog,"Skipped: release evidence consumes the prebuilt package without repacking."+Environment.NewLine);
        }
        var Artifacts=ResolveArtifactPaths(RepoRoot,Options.Configuration);
        var VsixPath=Artifacts.VsixPath;
        var BuildDllPath=Artifacts.BuildDllPath;
        RequireFile(VsixPath,"VSIX output");
        RequireFile(BuildDllPath,"VSIX build DLL");
        var InstallLog=Path.Combine(RunRoot,"install.log");
        var Script=Path.Combine(RepoRoot,"tools","CSharpGameEngine.VSExtension","Reinstall-CgeFixedVsix.ps1");
        var InstallArguments="-NoProfile -ExecutionPolicy Bypass -File "+Quote(Script)+" -Configuration "+Options.Configuration+" -RootSuffix CgeAutomation -VsixPath "+Quote(VsixPath)+" -SkipBuild -LogPath "+Quote(InstallLog);
        RunProcess("powershell.exe",InstallArguments,RepoRoot,Options.TimeoutSeconds,Path.Combine(RunRoot,"install-process.log"));
        var PackageDllHash=ComputeVsixDllHash(VsixPath);
        var BuildDllHash=VsixEvidenceWriter.ComputeSha256(BuildDllPath);
        var InstalledDll=FindInstalledDll("CgeAutomation");
        var InstalledDllHash=VsixEvidenceWriter.ComputeSha256(InstalledDll);
        if(!string.Equals(PackageDllHash,BuildDllHash,StringComparison.OrdinalIgnoreCase)||!string.Equals(BuildDllHash,InstalledDllHash,StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"VSIX DLL provenance mismatch. package={PackageDllHash} build={BuildDllHash} installed={InstalledDllHash}");
        }
        var Locales=SelectLocales(VisualStudio,Options.Locale).ToArray();
        if(Locales.Length==0) { throw new InvalidOperationException("No requested Visual Studio locale is available."); }
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
    private AutomationExitCode RunSceneObservation(AutomationOptions Options) {
        var RepoRoot=ResolveRepoRoot();
        EnsureInteractiveDesktop();
        var ExistingPids=GetRootSuffixProcesses(Options.RootSuffix).ToArray();
        if(ExistingPids.Length!=0) {
            throw new InvalidOperationException(
                "A "+Options.RootSuffix+" Visual Studio process already exists: "
                +string.Join(",",ExistingPids));
        }
        var VisualStudio=DiscoverVisualStudio();
        var RunId=DateTime.UtcNow.ToString(
            "yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"
            +Guid.NewGuid().ToString("N").Substring(0,8);
        var RunRoot=Path.GetFullPath(Path.Combine(
            RepoRoot,Options.EvidenceDirectory,RunId,"scene-observation"));
        Directory.CreateDirectory(RunRoot);
        var BuildLog=Path.Combine(RunRoot,"build.log");
        if(!Options.SkipBuild){
            RunProcess("dotnet","build "+Quote(Path.Combine(RepoRoot,"tools","CSharpGameEngine.VSExtension","CSharpGameEngine.VSExtension.csproj"))+" -c "+Options.Configuration+" --nologo",RepoRoot,Options.TimeoutSeconds,BuildLog);
        } else {
            File.WriteAllText(BuildLog,"Skipped: observation consumes the prebuilt package without repacking."+Environment.NewLine);
        }
        var Artifacts=ResolveArtifactPaths(RepoRoot,Options.Configuration);
        var VsixPath=Artifacts.VsixPath;
        var BuildDllPath=Artifacts.BuildDllPath;
        RequireFile(VsixPath,"VSIX output");
        RequireFile(BuildDllPath,"VSIX build DLL");
        var InstallLog=Path.Combine(RunRoot,"install.log");
        var InstallScript=Path.Combine(RepoRoot,"tools","CSharpGameEngine.VSExtension","Reinstall-CgeFixedVsix.ps1");
        var InstallArguments="-NoProfile -ExecutionPolicy Bypass -File "+Quote(InstallScript)
            +" -Configuration "+Options.Configuration+" -RootSuffix "+Options.RootSuffix
            +" -VsixPath "+Quote(VsixPath)+" -SkipBuild -LogPath "+Quote(InstallLog);
        RunProcess("powershell.exe",InstallArguments,RepoRoot,Options.TimeoutSeconds,
            Path.Combine(RunRoot,"install-process.log"));
        var PackageDllHash=ComputeVsixDllHash(VsixPath);
        var BuildDllHash=VsixEvidenceWriter.ComputeSha256(BuildDllPath);
        var InstalledDll=FindInstalledDll(Options.RootSuffix);
        var InstalledDllHash=VsixEvidenceWriter.ComputeSha256(InstalledDll);
        if(!string.Equals(
            PackageDllHash,BuildDllHash,StringComparison.OrdinalIgnoreCase)
            ||!string.Equals(
                BuildDllHash,InstalledDllHash,StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"VSIX DLL provenance mismatch. package={PackageDllHash} "
                +$"build={BuildDllHash} installed={InstalledDllHash}");
        }
        var PackageHash=VsixEvidenceWriter.ComputeSha256(VsixPath);
        var ActivityLog=Path.Combine(RunRoot,"ActivityLog.xml");
        var InitialScreenshot=Path.Combine(RunRoot,"main-scene.png");
        var SceneScreenshot=Path.Combine(RunRoot,"scene-designer.png");
        var RuntimeScreenshot=Path.Combine(RunRoot,"runtime-debug.png");
        var PhysicsScreenshot=Path.Combine(RunRoot,"physics-overlay.png");
        var SelectionScreenshot=Path.Combine(RunRoot,"selection-sync.png");
        var ResizedScreenshot=Path.Combine(RunRoot,"resized.png");
        var ShutdownScreenshot=Path.Combine(RunRoot,"shutdown-blocked.png");
        var InitialTree=Path.Combine(RunRoot,"main-scene-uia.txt");
        var SceneTree=Path.Combine(RunRoot,"scene-designer-uia.txt");
        var RuntimeTree=Path.Combine(RunRoot,"runtime-debug-uia.txt");
        var SelectionTree=Path.Combine(RunRoot,"selection-sync-uia.txt");
        var EvidencePath=Path.Combine(RunRoot,"observation.json");
        var SolutionPath=Path.Combine(RepoRoot,"UserEcsProject.slnx");
        var ScenePath=Path.Combine(
            RepoRoot,"UserEcsProject","Scenes","MainScene.json");
        RequireFile(SolutionPath,"User ECS solution");
        RequireFile(ScenePath,"Main Scene source");
        var Evidence=new VsixSceneObservationEvidence {
            RunId=RunId,
            Configuration=Options.Configuration,
            Locale=Options.Locale,
            RootSuffix=Options.RootSuffix,
            VisualStudioVersion=VisualStudio.Version,
            PackageSha256=PackageHash,
            PackageDllSha256=PackageDllHash,
            BuildDllSha256=BuildDllHash,
            InstalledDllSha256=InstalledDllHash,
            ActivityLogPath=ActivityLog,
            ScreenshotPaths=[InitialScreenshot,SceneScreenshot,RuntimeScreenshot,
                PhysicsScreenshot,SelectionScreenshot,ResizedScreenshot],
            TreePaths=[InitialTree,SceneTree,RuntimeTree,SelectionTree],
        };
        Process?VisualStudioProcess=null;
        try {
            var Arguments="/RootSuffix "+Options.RootSuffix
                +" /LCID "+Options.Locale+" /Log "+Quote(ActivityLog)
                +" "+Quote(SolutionPath)+" "+Quote(ScenePath);
            var StartInfo=new ProcessStartInfo(
                VisualStudio.ProductPath,Arguments) {
                    WorkingDirectory=RepoRoot,UseShellExecute=false
                };
            // Scene のリリース証拠は WARP による正確性ゲートである。所有するアダプターを設定する。
            // Visual Studio のプロセスに設定することで、そのプロセスが起動するプロセス外 Preview Host も
            // 同じ決定論的なアダプター契約を継承する。ユーザーの通常の Visual Studio 環境は
            // 読み取りも変更もしない。
            StartInfo.EnvironmentVariables["CGE_GRAPHICS_ADAPTER"]="warp";
            VisualStudioProcess=Process.Start(StartInfo);
            if(VisualStudioProcess is null) {
                throw new InvalidOperationException("Visual Studio did not start.");
            }
            Evidence.ProcessId=VisualStudioProcess.Id;
            var Window=WaitForTopLevelWindow(
                VisualStudioProcess,
                TimeSpan.FromSeconds(Math.Min(600,Options.TimeoutSeconds)));
            CompleteFirstLaunch(Window);
            ActivateDocumentTab(
                VisualStudioProcess.Id,"MainScene.json",
                TimeSpan.FromSeconds(Math.Min(600,Options.TimeoutSeconds)));
            DismissLineEndingDialog(VisualStudioProcess.Id);
            Window=WaitForTopLevelWindow(
                VisualStudioProcess,TimeSpan.FromSeconds(60));
            var SceneToolbarTarget=WriteAutomationTree(
                Window,InitialTree,CaptureSceneDesignerToolbar:true)
                ??throw new OracleMismatchException(
                    "The visible Scene Designer product toolbar command was not found.");
            CaptureWindow(Window.Current.NativeWindowHandle,InitialScreenshot);
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="main-scene-open",
                Passed=true,
                Detail="MainScene.json opened in the owned CgeFixed process."
            });
            var EditorSurface=FindLargestEditorSurface(Window)
                ??throw new OracleMismatchException(
                    "The MainScene JSON editor surface was not exposed by UI Automation.");
            // ドキュメントタブがすでにアクティブでも、Visual Studio は改行コードの確認を非同期に表示することがある。
            // 実際のツールバークリックを送る前に、正規化を拒否する。
            // そうしないとモーダルダイアログが所有するメインウィンドウを無効化し、クリックを黙って消費する。
            DismissLineEndingDialog(VisualStudioProcess.Id);
            var SceneCommandObservation=InvokeSceneDesignerToolbarCommand(
                SceneToolbarTarget);
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="scene-command",Passed=true,Detail=SceneCommandObservation
            });
            // WPF デザイナーを開くと Visual Studio の自動操作
            // ツリーの大部分が置き換えられる。その置き換え中にツリーをポーリングすると、
            // プロセス外 UIA プロバイダー内でブロックし、マネージドのタイムアウトでは制限できない。
            // 製品側の状態遷移が完了するのを待つ。まず画面上で観測できるウィンドウをキャプチャーし、
            // その後、落ち着いたツリーを 1 回観測する。
            Thread.Sleep(10000);
            Window=WaitForTopLevelWindow(
                VisualStudioProcess,TimeSpan.FromSeconds(60));
            CaptureWindow(Window.Current.NativeWindowHandle,SceneScreenshot);
            WriteAutomationTree(Window,SceneTree);
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="scene-designer-open",
                Passed=true,
                Detail="MainScene [Scene] opened in the Visual Studio Document Well."
            });
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="document-open",
                Passed=FindTabContaining(Window,"MainScene [Scene]") is not null,
                Detail="The Scene Designer document was present in the Visual Studio Document Well."
            });
            ExerciseAllSceneObservations(
                VisualStudioProcess.Id,Window,Evidence,RunRoot);
            Window=FindTopLevelWindow(VisualStudioProcess.Id)!;
            ExerciseSceneManualObservations(
                VisualStudioProcess.Id,Window,Evidence,RuntimeScreenshot,
                PhysicsScreenshot,SelectionScreenshot,ResizedScreenshot,
                RuntimeTree,SelectionTree,SceneScreenshot,ScenePath);
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="screenshots",
                Passed=File.Exists(InitialScreenshot)
                    &&File.Exists(SceneScreenshot)
                    &&new FileInfo(InitialScreenshot).Length>0
                    &&new FileInfo(SceneScreenshot).Length>0,
                Detail="MainScene and Scene Designer screenshots were captured."
            });
            if(Evidence.Assertions.Any(Assertion=>!Assertion.Passed)) {
                throw new OracleMismatchException(
                    "Scene observation screenshot capture failed.");
            }
        } catch(OracleMismatchException Exception) {
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="scenario",Passed=false,Detail=Exception.Message
            });
        } catch(InvalidOperationException Exception) {
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="scenario",Passed=false,
                Detail="UI Automation operation failed: "+Exception.Message
            });
        } catch(COMException Exception) {
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="scenario",Passed=false,
                Detail="UI Automation provider failed: "+Exception.Message
            });
        } finally {
            if(VisualStudioProcess is not null&&!VisualStudioProcess.HasExited) {
                RequestVisualStudioShutdown(
                    VisualStudioProcess,TimeSpan.FromSeconds(60));
                if(!VisualStudioProcess.HasExited
                    &&OwnedProcessShutdownPolicy.CanForceTerminate(
                        Evidence.ProcessId,VisualStudioProcess.Id,true)) {
                    try {
                        var BlockingWindow=FindShutdownDialog(
                            VisualStudioProcess.Id)
                            ??FindTopLevelWindow(VisualStudioProcess.Id);
                        if(BlockingWindow is not null){
                            CaptureWindow(
                                BlockingWindow.Current.NativeWindowHandle,
                                ShutdownScreenshot);
                            Evidence.ScreenshotPaths=[
                                ..Evidence.ScreenshotPaths,ShutdownScreenshot];
                        }
                    } catch(InvalidOperationException) {
                    } catch(ElementNotAvailableException) {
                    } catch(COMException) {
                    }
                    if(!VisualStudioProcess.HasExited){
                        try {
                            VisualStudioProcess.Kill();
                            Evidence.ForcedTermination=true;
                            VisualStudioProcess.WaitForExit(10000);
                        } catch(System.ComponentModel.Win32Exception)
                            when(VisualStudioProcess.HasExited){
                        }
                    }
                }
            }
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="normal-shutdown",
                Passed=!Evidence.ForcedTermination,
                Detail=!Evidence.ForcedTermination
                    ?"Owned Visual Studio process exited normally."
                    :"Owned Visual Studio process required forced termination."
            });
            var PreviewShutdownEnd=DateTime.UtcNow+TimeSpan.FromSeconds(10);
            var RemainingPreviewHosts=GetOwnedPreviewHostProcesses(Evidence.ProcessId).ToArray();
            while(RemainingPreviewHosts.Length>0&&DateTime.UtcNow<PreviewShutdownEnd){
                Thread.Sleep(250);
                RemainingPreviewHosts=GetOwnedPreviewHostProcesses(Evidence.ProcessId).ToArray();
            }
            Evidence.Assertions.Add(new VsixEvidenceAssertion {
                Id="preview-hosts-terminated",
                Passed=RemainingPreviewHosts.Length==0,
                Detail=RemainingPreviewHosts.Length==0
                    ?"Every Preview Host owned by the Visual Studio process exited with its owner."
                    :"Owned Preview Host processes remained after Visual Studio exit: "
                        +string.Join(",",RemainingPreviewHosts)
            });
            Evidence.Status=Evidence.Assertions.All(
                Assertion=>Assertion.Passed)?"Passed":"Failed";
            File.WriteAllText(
                EvidencePath,
                JsonSerializer.Serialize(
                    Evidence,new JsonSerializerOptions { WriteIndented=true })
                +Environment.NewLine);
            Console.WriteLine(
                $"scene-observation={EvidencePath} "
                +$"sha256={VsixEvidenceWriter.ComputeSha256(EvidencePath)}");
        }
        return Evidence.Status=="Passed"
            ?AutomationExitCode.Success:AutomationExitCode.OracleMismatch;
    }
    private static void ExerciseAllSceneObservations(
        int ProcessId,AutomationElement Window,VsixSceneObservationEvidence Evidence,
        string RunRoot){
        string[]CatalogNames=GetSceneCatalogNames(ProcessId,Window);
        string[]ExpectedNames=SceneObservationTargets
            .Select(Target=>Target.SceneName)
            .OrderBy(Name=>Name,StringComparer.Ordinal)
            .ToArray();
        Observe(Evidence,"all-scenes-catalogued",
            CatalogNames.SequenceEqual(ExpectedNames,StringComparer.Ordinal),
            $"Scene catalog expected {ExpectedNames.Length} entries and exposed "
                +$"{CatalogNames.Length}: {string.Join(",",CatalogNames)}");
        foreach(SceneObservationTarget Target in SceneObservationTargets){
            Window=FindTopLevelWindow(ProcessId)
                ??throw new OracleMismatchException("Visual Studio window disappeared during Scene traversal.");
            IntPtr Surface=SceneFrameCaptureSession.FindRenderSurface(
                Window.Current.NativeWindowHandle);
            if(Surface==IntPtr.Zero){
                throw new OracleMismatchException("The native Scene render surface was not found.");
            }
            string SceneRoot=Path.Combine(RunRoot,"scene-frames",Target.SceneName);
            using SceneFrameCaptureSession Capture=new(Surface,SceneRoot,Target.SceneName);
            Capture.Start();
            SelectSceneByName(ProcessId,Target.SceneName);
            WaitUntil(()=>IsScenePresented(ProcessId,Target),TimeSpan.FromSeconds(90),
                $"{Target.SceneName} did not become the presented Scene with root {Target.RootName}.");
            Thread.Sleep(1000);
            Capture.MarkReady();
            Thread.Sleep(10250);
            SceneFrameCaptureResult Result=Capture.StopAndAnalyze(10000);
            Evidence.SceneFrames.Add(new SceneFrameObservationEvidence {
                SceneName=Target.SceneName,
                RootName=Target.RootName,
                TimelinePath=Result.TimelinePath,
                Passed=Result.Analysis.Passed,
                Detail=Result.Analysis.Detail,
            });
            Evidence.ScreenshotPaths=[..Evidence.ScreenshotPaths,..Result.RepresentativePaths];
            Observe(Evidence,"scene-sequence-"+Target.SceneName,Result.Analysis.Passed,
                Result.Analysis.Detail);
            VerifySceneDisplayToggles(ProcessId,Surface,Target.SceneName,Evidence,SceneRoot);
        }
        for(int Index=SceneObservationTargets.Length-1;Index>=0;Index--){
            SceneObservationTarget Target=SceneObservationTargets[Index];
            SelectSceneByName(ProcessId,Target.SceneName);
            WaitUntil(()=>IsScenePresented(ProcessId,Target),TimeSpan.FromSeconds(90),
                $"Reverse traversal did not present {Target.SceneName}.");
        }
        DateTime RapidEnd=DateTime.UtcNow+TimeSpan.FromSeconds(60);
        int RapidIndex=0;
        SceneObservationTarget[]RapidTargets=[
            SceneObservationTargets.First(Target=>Target.SceneName=="EngineShowcaseScene"),
            SceneObservationTargets.First(Target=>Target.SceneName=="PrimitiveShowcaseBoxScene")
        ];
        while(DateTime.UtcNow<RapidEnd){
            SelectSceneByName(ProcessId,RapidTargets[RapidIndex%RapidTargets.Length].SceneName);
            RapidIndex++;
            Thread.Sleep(100);
        }
        SceneObservationTarget Final=SceneObservationTargets.First(
            Target=>Target.SceneName=="MainScene");
        SelectSceneByName(ProcessId,Final.SceneName);
        WaitUntil(()=>IsScenePresented(ProcessId,Final),TimeSpan.FromSeconds(90),
            "Rapid selection did not converge on the final MainScene request.");
        Observe(Evidence,"latest-scene-wins",true,
            $"{RapidIndex} rapid selections converged on MainScene after a 60-second stress interval.");
    }
    private static string[]GetSceneCatalogNames(int ProcessId,AutomationElement Window){
        ExpandSceneSelector(Window);
        AutomationElement[]Items=FindSceneCatalogItems(ProcessId);
        CollapseSceneSelector(Window);
        return Items.Select(Item=>Item.Current.Name)
            .Where(Name=>SceneObservationTargets.Any(
                Target=>string.Equals(Target.SceneName,Name,StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Name=>Name,StringComparer.Ordinal)
            .ToArray();
    }
    private static void SelectSceneByName(int ProcessId,string SceneName){
        AutomationElement Window=FindTopLevelWindow(ProcessId)
            ??throw new OracleMismatchException("Visual Studio window disappeared during Scene selection.");
        if(string.Equals(GetSelectedSceneName(Window),SceneName,StringComparison.Ordinal)){
            return;
        }
        ExpandSceneSelector(Window);
        AutomationElement?Item=null;
        WaitUntil(()=>{
            Item=FindSceneCatalogItems(ProcessId).FirstOrDefault(Candidate=>
                string.Equals(Candidate.Current.Name,SceneName,StringComparison.Ordinal));
            return Item is not null;
        },TimeSpan.FromSeconds(10),SceneName+" was not exposed by the Scene selector.");
        if(!Item!.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var Pattern)){
            throw new OracleMismatchException(SceneName+" exposed no SelectionItemPattern.");
        }
        ((SelectionItemPattern)Pattern).Select();
    }
    private static void ExpandSceneSelector(AutomationElement Window){
        AutomationElement Selector=RequireElement(Window,"SceneSelector");
        if(!Selector.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var Pattern)){
            throw new OracleMismatchException("SceneSelector exposed no ExpandCollapsePattern.");
        }
        ExpandCollapsePattern Expand=(ExpandCollapsePattern)Pattern;
        if(Expand.Current.ExpandCollapseState!=ExpandCollapseState.Expanded){
            Expand.Expand();
        }
        Thread.Sleep(250);
    }
    private static void CollapseSceneSelector(AutomationElement Window){
        AutomationElement Selector=RequireElement(Window,"SceneSelector");
        if(!Selector.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var Pattern)){
            throw new OracleMismatchException("SceneSelector exposed no ExpandCollapsePattern.");
        }
        ExpandCollapsePattern Expand=(ExpandCollapsePattern)Pattern;
        if(Expand.Current.ExpandCollapseState==ExpandCollapseState.Expanded){
            Expand.Collapse();
        }
        Thread.Sleep(100);
    }
    private static AutomationElement[]FindSceneCatalogItems(int ProcessId){
        Condition ConditionValue=new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty,ProcessId),
            new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem));
        AutomationElementCollection Items=AutomationElement.RootElement.FindAll(
            TreeScope.Descendants,ConditionValue);
        List<AutomationElement>Result=[];
        for(int Index=0;Index<Items.Count;Index++){
            string Name=Items[Index].Current.Name;
            if(SceneObservationTargets.Any(Target=>
                string.Equals(Target.SceneName,Name,StringComparison.Ordinal))){
                Result.Add(Items[Index]);
            }
        }
        return [..Result];
    }
    private static string GetSelectedSceneName(AutomationElement Window){
        AutomationElement Selector=RequireElement(Window,"SceneSelector");
        if(!Selector.TryGetCurrentPattern(SelectionPattern.Pattern,out var Pattern)){
            return string.Empty;
        }
        AutomationElement[]Selection=((SelectionPattern)Pattern).Current.GetSelection();
        return Selection.Length==1?Selection[0].Current.Name:string.Empty;
    }
    private static bool IsScenePresented(int ProcessId,SceneObservationTarget Target){
        AutomationElement?Window=FindTopLevelWindow(ProcessId);
        if(Window is null){ return false; }
        AutomationElement?Selector=FindById(Window,"SceneSelector");
        AutomationElement?Hierarchy=FindNamedPane(Window,["Hierarchy","ヒエラルキー"]);
        AutomationElement?Tree=Hierarchy is null?null:FindById(Hierarchy,"Tree");
        AutomationElement?Empty=FindById(Window,"EmptyStateText");
        string Presented=Selector?.Current.HelpText??string.Empty;
        return string.Equals(GetSelectedSceneName(Window),Target.SceneName,StringComparison.Ordinal)
            &&Presented.EndsWith("."+Target.SceneName,StringComparison.Ordinal)
            &&GetElementText(Tree).IndexOf(Target.RootName,StringComparison.Ordinal)>=0
            &&!IsVisible(Empty)
            &&SceneFrameCaptureSession.FindRenderSurface(Window.Current.NativeWindowHandle)!=IntPtr.Zero;
    }
    private static void VerifySceneDisplayToggles(int ProcessId,IntPtr Surface,
        string SceneName,VsixSceneObservationEvidence Evidence,string SceneRoot){
        string CameraOff=Path.Combine(SceneRoot,"camera-preview-off.png");
        string PhysicsOn=Path.Combine(SceneRoot,"physics-debug-on.png");
        SetToggleState(ProcessId,"CameraPreviewToggle",false);
        Thread.Sleep(500);
        CaptureWindow(unchecked((int)Surface.ToInt64()),CameraOff);
        SetToggleState(ProcessId,"CameraPreviewToggle",true);
        SetToggleState(ProcessId,"PhysicsDebugToggle",true);
        Thread.Sleep(500);
        CaptureWindow(unchecked((int)Surface.ToInt64()),PhysicsOn);
        SetToggleState(ProcessId,"PhysicsDebugToggle",false);
        Evidence.ScreenshotPaths=[..Evidence.ScreenshotPaths,CameraOff,PhysicsOn];
        Observe(Evidence,"scene-toggles-"+SceneName,
            HasRenderedSceneVariation(CameraOff)&&HasRenderedSceneVariation(PhysicsOn),
            "Camera Preview and Physics Debug toggles retained a rendered Scene surface.");
    }
    private static void SetToggleState(int ProcessId,string AutomationId,bool Enabled){
        AutomationElement Window=FindTopLevelWindow(ProcessId)
            ??throw new OracleMismatchException("Visual Studio window disappeared during toggle verification.");
        AutomationElement Element=RequireElement(Window,AutomationId);
        if(!Element.TryGetCurrentPattern(TogglePattern.Pattern,out var Pattern)){
            throw new OracleMismatchException(AutomationId+" exposed no TogglePattern.");
        }
        TogglePattern Toggle=(TogglePattern)Pattern;
        bool Current=Toggle.Current.ToggleState==ToggleState.On;
        if(Current!=Enabled){ Toggle.Toggle(); }
    }
    private static void ExerciseSceneManualObservations(
        int ProcessId,AutomationElement Window,VsixSceneObservationEvidence Evidence,
        string RuntimeScreenshot,string PhysicsScreenshot,
        string SelectionScreenshot,string ResizedScreenshot,
        string RuntimeTree,string SelectionTree,string SceneScreenshot,
        string ScenePath) {
        Observe(Evidence,"snapshot-rendered",
            File.Exists(SceneScreenshot)
                &&new FileInfo(SceneScreenshot).Length>0
                &&HasRenderedSceneVariation(SceneScreenshot),
            "The Scene Designer screenshot contained a non-uniform rendered Scene surface.");
        WriteAutomationTree(Window,RuntimeTree);
        CaptureWindow(Window.Current.NativeWindowHandle,RuntimeScreenshot);

        string[]PhysicsControls=["PhysicsPanelCloseButton","PhysicsBakeSelectedButton",
            "PhysicsBakeAllButton","PhysicsDebugToggle","PhysicsBoundsToggle",
            "PhysicsVelocityToggle","PhysicsSleepToggle","PhysicsCenterOfMassToggle",
            "PhysicsContactsToggle","PhysicsTreeToggle","PhysicsBondsToggle"];
        Observe(Evidence,"controls-visible",PhysicsControls.All(Id=>IsVisible(FindById(Window,Id))),
            "Every required Physics sidebar control was visible in the Scene Designer Document Well.");
        var DebugToggle=RequireElement(Window,"PhysicsDebugToggle");
        Activate(DebugToggle);
        WaitUntil(()=>IsToggleOn(FindById(FindTopLevelWindow(ProcessId)!,"PhysicsDebugToggle")),
            TimeSpan.FromSeconds(10),"Physics debug overlay did not enter the enabled state.");
        Window=FindTopLevelWindow(ProcessId)!;
        CaptureWindow(Window.Current.NativeWindowHandle,PhysicsScreenshot);
        Observe(Evidence,"overlay-visible",IsToggleOn(FindById(Window,"PhysicsDebugToggle")),
            "Physics debug overlay was enabled and captured with the rendered Scene snapshot.");
        RequireElement(Window,"PhysicsDebugToggle").SetFocus();
        SendEscape();
        Thread.Sleep(500);
        Window=FindTopLevelWindow(ProcessId)!;
        Observe(Evidence,"escape-remained-open",
            PhysicsControls.All(Id=>IsVisible(FindById(Window,Id))),
            "Escape left the Physics sidebar and all of its controls open.");

        var HierarchyPane=FindNamedPane(Window,["Hierarchy","ヒエラルキー"])
            ??throw new OracleMismatchException("The Hierarchy pane was not visible.");
        var HierarchyTree=FindById(HierarchyPane,"Tree")
            ??throw new OracleMismatchException("The Hierarchy Scene tree was not visible.");
        var InspectorPane=FindNamedPane(Window,["Inspector","インスペクター"])
            ??throw new OracleMismatchException("The Inspector pane was not visible.");
        SelectEditableHierarchyItem(
            ProcessId,HierarchyTree,SelectionScreenshot);
        WaitUntil(()=>{
            var Current=FindTopLevelWindow(ProcessId);
            var CurrentInspector=Current is null?null:FindNamedPane(
                Current,["Inspector","インスペクター"]);
            return CurrentInspector is not null
                &&!IsVisible(FindById(CurrentInspector,"EmptyMessage"))
                &&FindEditableInspectorValue(CurrentInspector) is not null;
        },TimeSpan.FromSeconds(30),"Hierarchy selection did not reach the Inspector.");
        Window=FindTopLevelWindow(ProcessId)!;
        InspectorPane=FindNamedPane(Window,["Inspector","インスペクター"])
            ??throw new OracleMismatchException("The Inspector pane disappeared after selection.");
        WriteAutomationTree(Window,SelectionTree);
        CaptureWindow(Window.Current.NativeWindowHandle,SelectionScreenshot);
        Observe(Evidence,"selection-synchronized",
            !IsVisible(FindById(InspectorPane,"EmptyMessage"))
                &&FindEditableInspectorValue(InspectorPane) is not null,
            "The Hierarchy selection populated the Inspector from the same Scene selection state.");

        var Editor=FindEditableInspectorValue(InspectorPane);
        if(Editor is null||!Editor.TryGetCurrentPattern(
                ValuePattern.Pattern,out var ValuePatternValue)) {
            throw new OracleMismatchException(
                "Selected Scene node exposed no editable Inspector text value.");
        }
        var ValuePatternInstance=(ValuePattern)ValuePatternValue;
        var Original=ValuePatternInstance.Current.Value;
        var SourceHashBefore=VsixEvidenceWriter.ComputeSha256(ScenePath);
        Editor.SetFocus();
        ValuePatternInstance.SetValue(Original+" observation-cancel");
        SendEscape();
        WaitUntil(()=>InspectorContainsEditValue(ProcessId,Original),
            TimeSpan.FromSeconds(10),"Escape did not restore the Inspector edit value.");
        Observe(Evidence,"edit-cancelled",
            string.Equals(SourceHashBefore,VsixEvidenceWriter.ComputeSha256(ScenePath),
                StringComparison.OrdinalIgnoreCase),
            "Escape restored the original Inspector value without changing authoritative C# source.");

        Window=FindTopLevelWindow(ProcessId)!;
        HierarchyPane=FindNamedPane(Window,["Hierarchy","ヒエラルキー"])
            ??throw new OracleMismatchException(
                "The Hierarchy pane disappeared before clearing selection.");
        HierarchyTree=FindById(HierarchyPane,"Tree")
            ??throw new OracleMismatchException(
                "The Hierarchy Scene tree disappeared before clearing selection.");
        var CurrentHierarchyItems=HierarchyTree.FindAll(
            TreeScope.Descendants,new PropertyCondition(
                AutomationElement.ControlTypeProperty,ControlType.TreeItem));
        if(CurrentHierarchyItems.Count==0){
            throw new OracleMismatchException(
                "The Hierarchy exposed no current item before clearing selection.");
        }
        Click(CurrentHierarchyItems[0]);
        WaitUntil(()=>{
            var Current=FindTopLevelWindow(ProcessId);
            var CurrentHierarchy=Current is null?null:FindNamedPane(
                Current,["Hierarchy","ヒエラルキー"]);
            var CurrentTree=CurrentHierarchy is null?null:FindById(
                CurrentHierarchy,"Tree");
            return CurrentTree is not null
                &&HasKeyboardFocusWithin(CurrentTree);
        },TimeSpan.FromSeconds(10),
        "The real Hierarchy click did not establish keyboard focus.");
        SendEscape();
        WaitUntil(()=>{
            var Current=FindTopLevelWindow(ProcessId);
            var CurrentInspector=Current is null?null:FindNamedPane(
                Current,["Inspector","インスペクター"]);
            return CurrentInspector is not null
                &&IsVisible(FindById(CurrentInspector,"EmptyMessage"));
        },TimeSpan.FromSeconds(10),"Escape did not clear the shared Scene selection.");
        Window=FindTopLevelWindow(ProcessId)!;
        Observe(Evidence,"selection-cleared",true,
            "Escape cleared Hierarchy selection and returned Inspector to its empty state.");
        Observe(Evidence,"windows-remained-open",
            FindTabContaining(Window,"MainScene [Scene]") is not null
                &&PhysicsControls.All(Id=>IsVisible(FindById(Window,Id))),
            "Escape left the Scene Document Well and Physics sidebar open.");
        var SolutionSearch=FindById(Window,"PART_SearchBox");
        if(SolutionSearch is null){
            throw new OracleMismatchException("Solution Explorer search box was not visible.");
        }
        SolutionSearch.SetFocus();
        SendEscape();
        Thread.Sleep(500);
        Window=FindTopLevelWindow(ProcessId)!;
        Observe(Evidence,"non-scene-unaffected",
            FindTabContaining(Window,"MainScene [Scene]") is not null
                &&IsVisible(FindById(Window,"PART_SearchBox")),
            "Escape outside Scene-owned focus did not close or mutate the Scene document.");

        var SceneSelector=RequireElement(Window,"SceneSelector");
        SceneSelector.SetFocus();
        var Before=Window.Current.BoundingRectangle;
        ResizeWindow(Window,
            Math.Max(1280,Before.Width-180),Math.Max(760,Before.Height-120));
        Thread.Sleep(1000);
        Window=FindTopLevelWindow(ProcessId)!;
        CaptureWindow(Window.Current.NativeWindowHandle,ResizedScreenshot);
        Observe(Evidence,"content-visible",
            IsVisible(FindById(Window,"SceneSelector"))
                &&IsVisible(FindById(Window,"PhysicsPanelCloseButton")),
            "Scene and Physics content remained visible at the active Windows DPI scale.");
        Observe(Evidence,"resize-stable",
            Window.Current.BoundingRectangle.Width>=1280
                &&Window.Current.BoundingRectangle.Height>=760,
            "The owned Visual Studio window resized while Scene layout retained valid bounds.");
        var Focused=AutomationElement.FocusedElement;
        Observe(Evidence,"focus-retained",
            Focused is not null&&Focused.Current.ProcessId==ProcessId,
            "Keyboard focus remained inside the owned Visual Studio process after resize.");
        // このメソッドの直後に所有するプロセスは終了する。元のサイズに戻そうとして
        // 2 回目の UIA TransformPattern.Resize を行っても製品の検証項目は増えず、
        // Visual Studio がドッキングレイアウトを再計算中だと、永久にブロックすることがある。検証済みの
        // サイズ変更後のウィンドウをそのままにし、残りのライフサイクルは通常のプロセス終了に任せる。
    }
    internal static bool HasRenderedSceneVariation(string ScreenshotPath) {
        using(var Screenshot=new Bitmap(ScreenshotPath)) {
            if(Screenshot.Width<400||Screenshot.Height<300) { return false; }
            var Histogram=new Dictionary<int,int>();
            var Left=Screenshot.Width/12;
            var Right=Screenshot.Width*5/12;
            var Top=Screenshot.Height/5;
            var Bottom=Screenshot.Height*4/5;
            var SampleCount=0;
            for(var Y=Top;Y<Bottom;Y+=Math.Max(1,(Bottom-Top)/40)) {
                for(var X=Left;X<Right;X+=Math.Max(1,(Right-Left)/40)) {
                    var ColorValue=Screenshot.GetPixel(X,Y);
                    var Quantized=(ColorValue.R/8<<16)|(ColorValue.G/8<<8)|ColorValue.B/8;
                    Histogram.TryGetValue(Quantized,out var Count);
                    Histogram[Quantized]=Count+1;
                    SampleCount++;
                }
            }
            var MinimumCoverage=Math.Max(4,SampleCount/100);
            var Significant=Histogram
                .Where(Pair=>Pair.Value>=MinimumCoverage)
                .Select(Pair=>Pair.Key)
                .ToArray();
            for(var LeftIndex=0;LeftIndex<Significant.Length;LeftIndex++) {
                for(var RightIndex=LeftIndex+1;RightIndex<Significant.Length;RightIndex++) {
                    var LeftColor=Significant[LeftIndex];
                    var RightColor=Significant[RightIndex];
                    var Distance=Math.Abs((LeftColor>>16&0xFF)-(RightColor>>16&0xFF))
                        +Math.Abs((LeftColor>>8&0xFF)-(RightColor>>8&0xFF))
                        +Math.Abs((LeftColor&0xFF)-(RightColor&0xFF));
                    if(Distance>=8) { return true; }
                }
            }
            return false;
        }
    }
    private static AutomationElement SelectEditableHierarchyItem(
        int ProcessId,AutomationElement HierarchyTree,string DiagnosticScreenshot) {
        var End=DateTime.UtcNow+TimeSpan.FromSeconds(30);
        var Observed=new List<string>();
        while(DateTime.UtcNow<End) {
            var Items=HierarchyTree.FindAll(
                TreeScope.Descendants,new PropertyCondition(
                    AutomationElement.ControlTypeProperty,ControlType.TreeItem));
            for(var Index=0;Index<Items.Count;Index++) {
                var Item=Items[Index];
                if(Item.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,out var ExpandPattern)) {
                    try {
                        ((ExpandCollapsePattern)ExpandPattern).Expand();
                    } catch(InvalidOperationException) { }
                }
                // 共有された Scene の選択状態への反映は、製品の PreviewMouseLeftButtonDown 経路が行う。
                // SelectionItemPattern が変更するのは WPF コンテナーの状態だけであり、非同期の再バインドが
                // 選択状態を反映してはならないため、この操作だけでは不十分である。
                Item.SetFocus();
                Thread.Sleep(100);
                Click(Item);
                Thread.Sleep(500);
                var Current=FindTopLevelWindow(ProcessId);
                var Inspector=Current is null?null:FindNamedPane(
                    Current,["Inspector","インスペクター"]);
                var Editor=Inspector is null?null:FindEditableInspectorValue(Inspector);
                if(Editor is not null) {
                    return Item;
                }
                if(Current is not null){
                    CaptureWindow(Current.Current.NativeWindowHandle,DiagnosticScreenshot);
                }
                var Empty=Inspector is null?null:FindById(Inspector,"EmptyMessage");
                var EditCount=Inspector?.FindAll(
                    TreeScope.Descendants,new PropertyCondition(
                        AutomationElement.ControlTypeProperty,ControlType.Edit)).Count??0;
                Observed.Add(
                    $"item='{Item.Current.Name}' emptyVisible={IsVisible(Empty)} "
                    +$"inspectorTextLength={GetElementText(Inspector).Length} "
                    +$"edits={EditCount}");
            }
            Thread.Sleep(250);
        }
        throw new OracleMismatchException(
            "Hierarchy exposed no Scene node with an editable Inspector text value. "
            +string.Join(" | ",Observed.Skip(Math.Max(0,Observed.Count-8))));
    }
    private static bool InspectorContainsEditValue(int ProcessId,string ExpectedValue) {
        var Window=FindTopLevelWindow(ProcessId);
        var Inspector=Window is null?null:FindNamedPane(
            Window,["Inspector","インスペクター"]);
        if(Inspector is null) { return false; }
        var Editors=Inspector.FindAll(
            TreeScope.Descendants,new PropertyCondition(
                AutomationElement.ControlTypeProperty,ControlType.Edit));
        for(var Index=0;Index<Editors.Count;Index++) {
            if(Editors[Index].TryGetCurrentPattern(
                    ValuePattern.Pattern,out var Pattern)
                &&string.Equals(((ValuePattern)Pattern).Current.Value,
                    ExpectedValue,StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }
    private static AutomationElement?FindEditableInspectorValue(AutomationElement Inspector){
        // WPF の ItemsControl は、x:Name に対するコントロールビュー AutomationPeer の存在を保証しない。
        // 生成されたエディターコントロールが表示され、アクセス可能な場合も同様である。親となる
        // Inspector ペインは正確に特定できるローカライズ済みの範囲であるため、意味的な API 契約ではない MemberList コンテナーを対象にせず、
        // 子孫のエディターを直接検索する。
        var Editors=Inspector.FindAll(
            TreeScope.Descendants,new PropertyCondition(
                AutomationElement.ControlTypeProperty,ControlType.Edit));
        for(var Index=0;Index<Editors.Count;Index++){
            if(Editors[Index].TryGetCurrentPattern(ValuePattern.Pattern,out _)){
                return Editors[Index];
            }
        }
        return null;
    }
    private static VsixExperimentalEvidence RunLocaleScenario(string RepoRoot,VisualStudioCandidate VisualStudio,AutomationOptions Options,string RunId,string Locale,string LocaleRoot,string VsixPath,string PackageDllHash,string BuildDllHash,string InstalledDllHash) {
        var ScratchGraph=Path.Combine(LocaleRoot,"Stage.cgeshadergraph");
        var SourceGraph=Path.Combine(RepoRoot,"assets","Shaders","Stage.cgeshadergraph");
        File.Copy(SourceGraph,ScratchGraph,true);
        foreach(var Dependency in ReadGraph(SourceGraph).Dependencies){
            var DependencyPath=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceGraph)!,Dependency.Path));
            File.Copy(DependencyPath,Path.Combine(LocaleRoot,Dependency.Path),true);
        }
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
            var NodeLeftBeforeReload=(float)Node.Current.BoundingRectangle.Left;
            ReloadLayout.X+=80;
            PublishExternalGraph(ScratchGraph,CleanReload);
            WaitUntil(()=>{
                var ReloadedNode=FindById(Root!,ReloadNodeAutomationId);
                var ReloadedLeft=ReloadedNode is null?NodeLeftBeforeReload:(float)ReloadedNode.Current.BoundingRectangle.Left;
                return ReloadedNode is not null&&Math.Abs(ReloadedLeft-NodeLeftBeforeReload)>1f;
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
                RequestVisualStudioShutdown(
                    VisualStudioProcess,TimeSpan.FromSeconds(60));
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
    private static void Observe(
        VsixSceneObservationEvidence Evidence,string Id,bool Passed,string Detail) {
        Evidence.Assertions.Add(new VsixEvidenceAssertion {
            Id=Id,Passed=Passed,Detail=Detail
        });
    }
    private static AutomationElement RequireElement(
        AutomationElement Root,string AutomationId) {
        return FindById(Root,AutomationId)
            ??throw new OracleMismatchException(AutomationId+" was not found.");
    }
    private static AutomationElement?FindNamedPane(
        AutomationElement Root,IReadOnlyList<string>Names) {
        var Panes=Root.FindAll(
            TreeScope.Descendants,new PropertyCondition(
                AutomationElement.ControlTypeProperty,ControlType.Pane));
        AutomationElement?HiddenMatch=null;
        for(var Index=0;Index<Panes.Count;Index++) {
            var Name=Panes[Index].Current.Name;
            if(Names.Any(Expected=>string.Equals(
                Name,Expected,StringComparison.OrdinalIgnoreCase))) {
                if(IsVisible(Panes[Index])) { return Panes[Index]; }
                HiddenMatch??=Panes[Index];
            }
        }
        return HiddenMatch;
    }
    private static AutomationElement?FindTabContaining(
        AutomationElement Root,string Caption) {
        var Tabs=Root.FindAll(
            TreeScope.Descendants,new PropertyCondition(
                AutomationElement.ControlTypeProperty,ControlType.TabItem));
        for(var Index=0;Index<Tabs.Count;Index++) {
            if(Tabs[Index].Current.Name.IndexOf(
                Caption,StringComparison.OrdinalIgnoreCase)>=0) {
                return Tabs[Index];
            }
        }
        return null;
    }
    private static AutomationElement?FindSolutionExplorerItem(
        AutomationElement Root,string FileName) {
        var Tree=FindById(Root,"SolutionExplorer");
        if(Tree is null) { return null; }
        var Items=Tree.FindAll(
            TreeScope.Descendants,new PropertyCondition(
                AutomationElement.ControlTypeProperty,ControlType.TreeItem));
        for(var Index=0;Index<Items.Count;Index++) {
            if(string.Equals(
                Items[Index].Current.Name,FileName,StringComparison.OrdinalIgnoreCase)) {
                return Items[Index];
            }
        }
        return null;
    }
    private static string InvokeSceneDesignerToolbarCommand(
        SceneDesignerToolbarClickTarget Target) {
        try {
            if(Target.Element.TryGetCurrentPattern(
                    InvokePattern.Pattern,out var Pattern)) {
                ((InvokePattern)Pattern).Invoke();
                Thread.Sleep(300);
                return "Invoked the fresh visible product toolbar command '"
                    +Target.Name+"' through its UI Automation InvokePattern.";
            }
        } catch(ElementNotAvailableException) {
            // キャプチャーと呼び出しの間に、最初のツリーピアは置き換えられていた。不変の
            // 画面上の座標は同じ所有ウィンドウに属するため、安全な代替手段として使える。
        } catch(InvalidOperationException) {
            // 新しいピアを呼び出せないプロバイダーの場合は、実際のポインタージェスチャーに切り替える。
        }
        SetCursorPos(Target.X,Target.Y);
        mouse_event(2,0,0,0,UIntPtr.Zero);
        Thread.Sleep(100);
        mouse_event(4,0,0,0,UIntPtr.Zero);
        Thread.Sleep(300);
        return "Invoked the visible product toolbar command '"+Target.Name
            +"' in the owned Visual Studio process.";
    }
    internal static bool IsSceneDesignerToolbarButtonName(string?Name) {
        return !string.IsNullOrWhiteSpace(Name)
            &&(Name!.IndexOf("Scene Designer",StringComparison.OrdinalIgnoreCase)>=0
                ||Name.IndexOf("シーン デザイナー",StringComparison.Ordinal)>=0);
    }
    private static bool IsVisible(AutomationElement?Element) {
        if(Element is null) { return false; }
        try {
            var Rectangle=Element.Current.BoundingRectangle;
            return !Element.Current.IsOffscreen
                &&Rectangle.Width>0&&Rectangle.Height>0;
        } catch(ElementNotAvailableException) {
            return false;
        }
    }
    private static bool HasKeyboardFocusWithin(AutomationElement Element){
        try {
            return Element.Current.HasKeyboardFocus
                ||Element.FindFirst(
                    TreeScope.Descendants,new PropertyCondition(
                        AutomationElement.HasKeyboardFocusProperty,true))
                    is not null;
        } catch(ElementNotAvailableException) {
            return false;
        } catch(COMException) {
            return false;
        }
    }
    private static bool IsToggleOn(AutomationElement?Element) {
        if(Element is null||!Element.TryGetCurrentPattern(
            TogglePattern.Pattern,out var Pattern)) {
            return false;
        }
        return ((TogglePattern)Pattern).Current.ToggleState==ToggleState.On;
    }
    private static void Select(AutomationElement Element) {
        if(Element.TryGetCurrentPattern(
            SelectionItemPattern.Pattern,out var Pattern)) {
            ((SelectionItemPattern)Pattern).Select();
            Thread.Sleep(250);
            return;
        }
        Click(Element);
    }
    private static void ResizeWindow(
        AutomationElement Window,double Width,double Height) {
        if(Window.TryGetCurrentPattern(
            WindowPattern.Pattern,out var WindowPatternValue)) {
            var WindowPatternInstance=(WindowPattern)WindowPatternValue;
            if(WindowPatternInstance.Current.WindowVisualState
                !=WindowVisualState.Normal) {
                WindowPatternInstance.SetWindowVisualState(WindowVisualState.Normal);
                Thread.Sleep(500);
            }
        }
        if(!Window.TryGetCurrentPattern(
            TransformPattern.Pattern,out var TransformPatternValue)) {
            throw new OracleMismatchException(
                "Visual Studio main window exposed no TransformPattern.");
        }
        var TransformPatternInstance=(TransformPattern)TransformPatternValue;
        if(!TransformPatternInstance.Current.CanResize) {
            throw new OracleMismatchException(
                "Visual Studio main window cannot be resized.");
        }
        TransformPatternInstance.Resize(Width,Height);
        Thread.Sleep(500);
    }
    private static void SendEscape() {
        const byte EscapeKey=0x1B;
        const uint KeyUp=0x0002;
        keybd_event(EscapeKey,0,0,UIntPtr.Zero);
        keybd_event(EscapeKey,0,KeyUp,UIntPtr.Zero);
        Thread.Sleep(250);
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
            if(Requested=="1033"||Directory.Exists(Path.Combine(VisualStudio.InstallationPath,"Common7","IDE",Requested))) { yield return Requested; }
            yield break;
        }
        if(Directory.Exists(Path.Combine(VisualStudio.InstallationPath,"Common7","IDE","1041"))) { yield return "1041"; }
        yield return "1033";
    }
    private static IEnumerable<int>GetAutomationHiveProcesses() {
        using(var Searcher=new ManagementObjectSearcher("SELECT ProcessId,CommandLine FROM Win32_Process WHERE Name='devenv.exe'"))using(var Results=Searcher.Get()) {
            foreach(ManagementObject ProcessObject in Results) {
                var CommandLine=ProcessObject["CommandLine"] as string;
                if(RootSuffixProcessPolicy.IsOwnedAutomationHive(CommandLine)) { yield return Convert.ToInt32((uint)ProcessObject["ProcessId"],CultureInfo.InvariantCulture); }
            }
        }
    }
    private static IEnumerable<int>GetOwnedPreviewHostProcesses(int OwnerProcessId){
        if(OwnerProcessId<=0){ yield break; }
        var OwnerToken="--parent-process-id "+OwnerProcessId.ToString(
            CultureInfo.InvariantCulture);
        using(var Searcher=new ManagementObjectSearcher(
            "SELECT ProcessId,Name,CommandLine FROM Win32_Process "
            +"WHERE Name='CSharpGameEngine.EditorPreview.Host.exe' OR Name='dotnet.exe'"))
        using(var Results=Searcher.Get()){
            foreach(ManagementObject ProcessObject in Results){
                if(ProcessObject["CommandLine"] is not string CommandLine
                    ||CommandLine.IndexOf(
                        "CSharpGameEngine.EditorPreview.Host",StringComparison.OrdinalIgnoreCase)<0
                    ||CommandLine.IndexOf(
                        OwnerToken,StringComparison.OrdinalIgnoreCase)<0){
                    continue;
                }
                yield return Convert.ToInt32(
                    (uint)ProcessObject["ProcessId"],CultureInfo.InvariantCulture);
            }
        }
    }
    private static IEnumerable<int>GetRootSuffixProcesses(string RootSuffix) {
        using(var Searcher=new ManagementObjectSearcher(
            "SELECT ProcessId,CommandLine FROM Win32_Process "
            +"WHERE Name='devenv.exe'"))
        using(var Results=Searcher.Get()) {
            var Token="/RootSuffix "+RootSuffix;
            foreach(ManagementObject ProcessObject in Results) {
                if(ProcessObject["CommandLine"] is string CommandLine
                    &&!string.IsNullOrWhiteSpace(CommandLine)
                    &&CommandLine.IndexOf(
                        Token,StringComparison.OrdinalIgnoreCase)>=0) {
                    yield return Convert.ToInt32(
                        (uint)ProcessObject["ProcessId"],CultureInfo.InvariantCulture);
                }
            }
        }
    }
    private static string FindInstalledDll(string RootSuffix) {
        var Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","VisualStudio");
        var Candidates=Directory.Exists(Root)?Directory.EnumerateFiles(Root,"CSharpGameEngine.VSExtension.dll",SearchOption.AllDirectories)
            .Where(PathValue=>PathValue.IndexOf(RootSuffix,StringComparison.OrdinalIgnoreCase)>=0
                &&PathValue.IndexOf("Backups",StringComparison.OrdinalIgnoreCase)<0)
            .OrderByDescending(File.GetLastWriteTimeUtc).ToArray():[];
        return Candidates.FirstOrDefault()??throw new InvalidOperationException(
            "The installed "+RootSuffix+" VSIX DLL was not found.");
    }
    private static (string VsixPath,string BuildDllPath)ResolveArtifactPaths(
        string RepoRoot,string Configuration) {
        if(Configuration!="Release") {
            var OutputRoot=Path.Combine(
                RepoRoot,"tools","CSharpGameEngine.VSExtension","bin",
                Configuration,"net472");
            return (
                Path.Combine(OutputRoot,"CSharpGameEngine.VSExtension.vsix"),
                Path.Combine(OutputRoot,"CSharpGameEngine.VSExtension.dll"));
        }
        var InputPath=Path.Combine(
            RepoRoot,"Docs","Verification","VSIX-Release-Input.json");
        RequireFile(InputPath,"frozen VSIX release input");
        using(var Document=JsonDocument.Parse(File.ReadAllBytes(InputPath))) {
            var Root=Document.RootElement;
            if(Root.GetProperty("schemaVersion").GetString()
                    !="csharp-game-engine-vsix-release-input-v1"
                ||Root.GetProperty("configuration").GetString()!="Release") {
                throw new InvalidOperationException(
                    "Frozen VSIX release input has an invalid schema or configuration.");
            }
            var VsixPath=ResolveRepositoryRelative(
                RepoRoot,Root.GetProperty("packagePath").GetString());
            var BuildDllPath=ResolveRepositoryRelative(
                RepoRoot,Root.GetProperty("buildDllPath").GetString());
            RequireFile(VsixPath,"frozen VSIX package");
            RequireFile(BuildDllPath,"frozen VSIX build DLL");
            var PackageHash=VsixEvidenceWriter.ComputeSha256(VsixPath);
            var BuildDllHash=VsixEvidenceWriter.ComputeSha256(BuildDllPath);
            if(!string.Equals(
                    PackageHash,Root.GetProperty("packageSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase)
                ||!string.Equals(
                    BuildDllHash,Root.GetProperty("buildDllSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    "Frozen VSIX release input content does not match its declared hashes.");
            }
            return (VsixPath,BuildDllPath);
        }
    }
    private static string ResolveRepositoryRelative(string RepoRoot,string? RelativePath) {
        if(string.IsNullOrWhiteSpace(RelativePath)||Path.IsPathRooted(RelativePath)) {
            throw new InvalidOperationException(
                "Frozen VSIX release input path must be repository-relative.");
        }
        var FullPath=Path.GetFullPath(Path.Combine(RepoRoot,RelativePath!));
        var Prefix=Path.GetFullPath(RepoRoot).TrimEnd(Path.DirectorySeparatorChar)
            +Path.DirectorySeparatorChar;
        if(!FullPath.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                "Frozen VSIX release input path escaped the repository.");
        }
        return FullPath;
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
                var Width=(float)Rectangle.Width;
                var Height=(float)Rectangle.Height;
                var Left=(float)Rectangle.Left;
                var Top=(float)Rectangle.Top;
                if(Width<=0||Height<=0||float.IsNaN(Width)||float.IsInfinity(Width)||float.IsNaN(Height)||float.IsInfinity(Height)||float.IsNaN(Left)||float.IsInfinity(Left)||float.IsNaN(Top)||float.IsInfinity(Top)) { continue; }
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
    private static void DismissLineEndingDialog(int ProcessId) {
        var Dialog=FindDialog(ProcessId);
        if(Dialog is null) { return; }
        var Name=Dialog.Current.Name;
        if(Name.IndexOf("Inconsistent Line Endings",StringComparison.OrdinalIgnoreCase)<0
            &&Name.IndexOf("行の終わりの不整合",StringComparison.Ordinal)<0) {
            return;
        }
        ClickDialogButton(Dialog,"7",["No","いいえ"],false);
        WaitUntil(
            ()=>FindDialog(ProcessId) is null,
            TimeSpan.FromSeconds(10),
            "The line-ending normalization dialog did not close.");
    }
    private static AutomationElement?FindShutdownDialog(int ProcessId) {
        foreach(var Window in EnumerateProcessWindows(ProcessId)) {
            try {
                var NativeHandle=new IntPtr(Window.Current.NativeWindowHandle);
                if(NativeHandle==IntPtr.Zero
                    ||GetWindow(NativeHandle,4)==IntPtr.Zero){
                    continue;
                }
                var Buttons=Window.FindAll(
                    TreeScope.Descendants,new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Button));
                if(Buttons.Count>0){ return Window; }
            } catch(ElementNotAvailableException) {
            } catch(COMException) {
            }
        }
        return null;
    }
    private static void RequestVisualStudioShutdown(
        Process ProcessValue,TimeSpan Timeout){
        try {
            if(!ProcessValue.CloseMainWindow()){
                var Window=FindTopLevelWindow(ProcessValue.Id);
                if(Window is not null&&Window.TryGetCurrentPattern(
                        WindowPattern.Pattern,out var Pattern)){
                    ((WindowPattern)Pattern).Close();
                }
            }
        } catch(InvalidOperationException) {
        } catch(ElementNotAvailableException) {
        }
        var ShutdownEnd=DateTime.UtcNow+Timeout;
        while(!ProcessValue.HasExited&&DateTime.UtcNow<ShutdownEnd){
            try {
                var ShutdownDialog=FindShutdownDialog(ProcessValue.Id);
                if(ShutdownDialog is not null){
                    ClickDialogButton(
                        ShutdownDialog,"7",
                        ["Don't Save","保存しない","いいえ","No"],false);
                }
            } catch(ElementNotAvailableException) {
            } catch(COMException) {
            } catch(InvalidOperationException) {
            }
            ProcessValue.WaitForExit(500);
        }
    }
    private static IReadOnlyList<AutomationElement>EnumerateProcessWindows(int ProcessId) {
        var Windows=new List<AutomationElement>();
        EnumWindows((Window,Parameter)=>{
            GetWindowThreadProcessId(Window,out var OwnerProcessId);
            // 呼び出し元はすべてダイアログまたはポップアップを探している。UIA に入る前に、所有者のない Visual Studio の
            // メインウィンドウは除外する。シャットダウン中は、そのプロバイダーが COM のタイムアウトまで
            // ブロックし、実際の保存ダイアログに到達できなくなる場合がある。
            if(OwnerProcessId!=(uint)ProcessId||!IsWindowVisible(Window)
                ||GetWindow(Window,4)==IntPtr.Zero){
                return true;
            }
            try {
                var Element=AutomationElement.FromHandle(Window);
                if(Element is not null&&Element.Current.ProcessId==ProcessId) { Windows.Add(Element); }
            } catch(ElementNotAvailableException) {
            } catch(COMException) {
            }
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
        if(!Element.TryGetClickablePoint(out var PointValue)) {
            var Rectangle=Element.Current.BoundingRectangle;
            if(Rectangle.Width<=0||Rectangle.Height<=0) {
                throw new OracleMismatchException(
                    "Element has no clickable point: "+Element.Current.AutomationId);
            }
            PointValue=new System.Windows.Point(
                Rectangle.Left+Math.Min(Rectangle.Width/2,300),
                Rectangle.Top+Math.Min(Rectangle.Height/2,200));
        }
        SetCursorPos((int)PointValue.X,(int)PointValue.Y);mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);Thread.Sleep(250);
    }
    private static void RightClick(AutomationElement Element) {
        if(!Element.TryGetClickablePoint(out var PointValue)) {
            var Rectangle=Element.Current.BoundingRectangle;
            if(Rectangle.Width<=0||Rectangle.Height<=0) {
                throw new OracleMismatchException(
                    "Element has no right-click point.");
            }
            PointValue=new System.Windows.Point(
                Rectangle.Left+Math.Min(Rectangle.Width/2,300),
                Rectangle.Top+Math.Min(Rectangle.Height/2,200));
        }
        SetCursorPos((int)PointValue.X,(int)PointValue.Y);
        mouse_event(8,0,0,0,UIntPtr.Zero);
        mouse_event(16,0,0,0,UIntPtr.Zero);
        Thread.Sleep(500);
    }
    private static AutomationElement?FindLargestEditorSurface(
        AutomationElement Window) {
        var All=Window.FindAll(TreeScope.Descendants,Condition.TrueCondition);
        AutomationElement?Best=null;
        var BestArea=0d;
        for(var Index=0;Index<All.Count;Index++) {
            var Type=All[Index].Current.ControlType;
            if(Type!=ControlType.Document&&Type!=ControlType.Edit) { continue; }
            var Rectangle=All[Index].Current.BoundingRectangle;
            var Area=Rectangle.Width*Rectangle.Height;
            if(Area>BestArea&&Rectangle.Width>300&&Rectangle.Height>200) {
                Best=All[Index];
                BestArea=Area;
            }
        }
        return Best;
    }
    private static AutomationElement?WaitForEnabledMenuItem(
        Func<AutomationElement?>ResolveTarget,int ProcessId,bool SelectTarget,
        IReadOnlyList<string>Names,TimeSpan Timeout,out string Observation) {
        var Observed=new HashSet<string>(StringComparer.Ordinal);
        var End=DateTime.UtcNow+Timeout;
        while(DateTime.UtcNow<End) {
            var Target=ResolveTarget();
            if(Target is null) {
                Thread.Sleep(500);
                continue;
            }
            try {
                if(SelectTarget) { Select(Target); }
                OpenContextMenu(Target);
            } catch(InvalidOperationException) {
                Thread.Sleep(250);
                continue;
            } catch(ElementNotAvailableException) {
                Thread.Sleep(250);
                continue;
            }
            var MenuEnd=DateTime.UtcNow+TimeSpan.FromSeconds(3);
            while(DateTime.UtcNow<MenuEnd) {
                foreach(var Window in EnumerateProcessWindows(ProcessId)) {
                    var Items=Window.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.MenuItem));
                    for(var Index=0;Index<Items.Count;Index++) {
                        var Name=Items[Index].Current.Name;
                        if(!string.IsNullOrWhiteSpace(Name)&&Observed.Count<80) {
                            Observed.Add(Name+" [enabled="
                                +Items[Index].Current.IsEnabled+"]");
                        }
                        if(Names.Any(Expected=>
                            Name.IndexOf(
                                Expected,StringComparison.OrdinalIgnoreCase)>=0)) {
                            Observed.Add(Name+" [enabled="
                                +Items[Index].Current.IsEnabled+"]");
                            if(Items[Index].Current.IsEnabled) {
                                Observation=string.Join(" | ",Observed);
                                return Items[Index];
                            }
                        }
                    }
                }
                Thread.Sleep(200);
            }
            SendEscape();
            Thread.Sleep(1000);
        }
        Observation=Observed.Count==0
            ?"no matching menu item observed":string.Join(" | ",Observed);
        return null;
    }
    private static void OpenContextMenu(AutomationElement Element) {
        Element.SetFocus();
        const byte ContextMenuKey=0x5D;
        const uint KeyUp=0x0002;
        keybd_event(ContextMenuKey,0,0,UIntPtr.Zero);
        keybd_event(ContextMenuKey,0,KeyUp,UIntPtr.Zero);
        Thread.Sleep(500);
    }
    private static void ActivateDocumentTabContains(
        int ProcessId,string Caption,TimeSpan Timeout) {
        WaitUntil(()=>{
            var Window=FindTopLevelWindow(ProcessId);
            if(Window is null) { return false; }
            var Tabs=Window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,ControlType.TabItem));
            for(var Index=0;Index<Tabs.Count;Index++) {
                if(Tabs[Index].Current.Name.IndexOf(
                    Caption,StringComparison.OrdinalIgnoreCase)<0) {
                    continue;
                }
                if(Tabs[Index].TryGetCurrentPattern(
                    SelectionItemPattern.Pattern,out var Pattern)) {
                    ((SelectionItemPattern)Pattern).Select();
                } else {
                    Activate(Tabs[Index]);
                }
                return true;
            }
            return false;
        },Timeout,"Scene Designer document tab was not opened.");
    }
    private static SceneDesignerToolbarClickTarget?WriteAutomationTree(
        AutomationElement Window,string PathValue,
        bool CaptureSceneDesignerToolbar=false) {
        var Lines=new List<string>();
        SceneDesignerToolbarClickTarget?SceneToolbarTarget=null;
        var All=Window.FindAll(TreeScope.Subtree,Condition.TrueCondition);
        for(var Index=0;Index<All.Count;Index++) {
            try {
                var Item=All[Index];
                var Name=Item.Current.Name;
                var Id=Item.Current.AutomationId;
                var Rectangle=Item.Current.BoundingRectangle;
                var ControlTypeValue=Item.Current.ControlType;
                var IsEnabled=Item.Current.IsEnabled;
                var IsOffscreen=Item.Current.IsOffscreen;
                if(string.IsNullOrWhiteSpace(Name)&&string.IsNullOrWhiteSpace(Id)) {
                    continue;
                }
                if(CaptureSceneDesignerToolbar
                    &&SceneToolbarTarget is null
                    &&ControlTypeValue==ControlType.Button
                    &&IsSceneDesignerToolbarButtonName(Name)
                    &&!IsOffscreen&&Rectangle.Width>0&&Rectangle.Height>0) {
                    SceneToolbarTarget=new(
                        Item,Name,
                        (int)(Rectangle.Left+Rectangle.Width/2),
                        (int)(Rectangle.Top+Rectangle.Height/2));
                }
                Lines.Add(
                    $"{Index}|{ControlTypeValue.ProgrammaticName}"
                    +$"|name={Name}|id={Id}|enabled={IsEnabled}"
                    +$"|offscreen={IsOffscreen}"
                    +$"|rect={Rectangle.Left},{Rectangle.Top},"
                    +$"{Rectangle.Width},{Rectangle.Height}");
            } catch(ElementNotAvailableException) { }
        }
        File.WriteAllLines(PathValue,Lines,Encoding.UTF8);
        return SceneToolbarTarget;
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
        if(Element.TryGetCurrentPattern(
                TogglePattern.Pattern,out var TogglePatternValue)) {
            ((TogglePattern)TogglePatternValue).Toggle();
            Thread.Sleep(250);
            return;
        }
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
    [DllImport("user32.dll")]private static extern void keybd_event(byte VirtualKey,byte ScanCode,uint Flags,UIntPtr ExtraInfo);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr Window,out NativeRect Rect);
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr Window,uint Message,IntPtr WParam,IntPtr LParam);
    private delegate bool EnumWindowsCallback(IntPtr Window,IntPtr Parameter);
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumWindowsCallback Callback,IntPtr Parameter);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr Window,out uint ProcessId);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr Window);
    [DllImport("user32.dll")]private static extern IntPtr GetWindow(IntPtr Window,uint Command);
}

internal sealed class SceneDesignerToolbarClickTarget {
    internal SceneDesignerToolbarClickTarget(
        AutomationElement Element,string Name,int X,int Y) {
        this.Element=Element;
        this.Name=Name;
        this.X=X;
        this.Y=Y;
    }
    internal AutomationElement Element { get; }
    internal string Name { get; }
    internal int X { get; }
    internal int Y { get; }
}

internal sealed class VsixSceneObservationEvidence {
    public string SchemaVersion { get; set; }=
        "cge-vsix-scene-observation-v2";
    public string RunId { get; set; }=string.Empty;
    public DateTimeOffset ObservedUtc { get; set; }=DateTimeOffset.UtcNow;
    public string Configuration { get; set; }=string.Empty;
    public string Locale { get; set; }=string.Empty;
    public string RootSuffix { get; set; }=string.Empty;
    public string Status { get; set; }=string.Empty;
    public string VisualStudioVersion { get; set; }=string.Empty;
    public int ProcessId { get; set; }
    public bool ForcedTermination { get; set; }
    public string PackageSha256 { get; set; }=string.Empty;
    public string PackageDllSha256 { get; set; }=string.Empty;
    public string BuildDllSha256 { get; set; }=string.Empty;
    public string InstalledDllSha256 { get; set; }=string.Empty;
    public string ActivityLogPath { get; set; }=string.Empty;
    public IReadOnlyList<string>ScreenshotPaths { get; set; }=[];
    public IReadOnlyList<string>TreePaths { get; set; }=[];
    public List<SceneFrameObservationEvidence>SceneFrames { get; set; }=[];
    public List<VsixEvidenceAssertion>Assertions { get; set; }=[];
}
internal readonly struct SceneObservationTarget {
    internal SceneObservationTarget(string SceneName,string RootName){
        this.SceneName=SceneName;
        this.RootName=RootName;
    }
    internal string SceneName { get; }
    internal string RootName { get; }
}
internal sealed class SceneFrameObservationEvidence {
    public string SceneName { get; set; }=string.Empty;
    public string RootName { get; set; }=string.Empty;
    public string TimelinePath { get; set; }=string.Empty;
    public bool Passed { get; set; }
    public string Detail { get; set; }=string.Empty;
}
