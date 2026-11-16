using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(MockedVsCollection.Name)]
public sealed class AssetEditorNavigationTests {
    public AssetEditorNavigationTests(MockedVsFixture Fixture){ }
    [Fact]
    public void SolutionSelectionClearsOldFolderAndAssetButLockAndUnrelatedNotificationsPreserveThem(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Constructor=typeof(AssetWorkspaceController).GetConstructors(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Single();
            var Controller=(AssetWorkspaceController)Constructor.Invoke([null,null,null]);
            var Old=AssetWorkspaceSelection.Current;
            var Item=new AssetWorkspaceItem{Source=new(){ProjectPath="Game.csproj",SourcePath=@"C:\Game\Assets\Hero.glb"}};
            var Solution=Proxy<EnvDTE.Solution>((_,_)=>throw new NotSupportedException());
            var Hierarchy=Proxy<IVsHierarchy>((Call,Args)=>{ Args[2]=Solution;return VSConstants.S_OK; });
            var Notifications=0;Controller.Changed+=()=>Notifications++;
            try {
                Controller.Query.MoveTo("Game.csproj",@"C:\Game\Assets");AssetWorkspaceSelection.Select([Item]);
                Controller.OnSelectionChanged(null!,0,null!,null!,Hierarchy,VSConstants.VSITEMID_ROOT,null!,null!);
                Assert.Equal(AssetWorkspaceScope.Solution,Controller.Query.Scope);Assert.Empty(Controller.Query.ProjectPath);Assert.Empty(Controller.Query.Folder);Assert.Empty(AssetWorkspaceSelection.Current);Assert.Equal(1,Notifications);
                Controller.OnSelectionChanged(null!,0,null!,null!,Hierarchy,VSConstants.VSITEMID_ROOT,null!,null!);Assert.Equal(1,Notifications);
                Controller.Query.MoveTo("Game.csproj",@"C:\Game\Assets");Controller.Query.Locked=true;AssetWorkspaceSelection.Select([Item]);
                Controller.OnSelectionChanged(null!,0,null!,null!,Hierarchy,VSConstants.VSITEMID_ROOT,null!,null!);
                Assert.Equal(AssetWorkspaceScope.Folder,Controller.Query.Scope);Assert.Same(Item,Assert.Single(AssetWorkspaceSelection.Current));
                Controller.Query.Locked=false;
                Controller.OnSelectionChanged(null!,0,null!,null!,null!,VSConstants.VSITEMID_NIL,null!,null!);
                Controller.OnSelectionChanged(null!,0,null!,null!,null!,VSConstants.VSITEMID_ROOT,null!,null!);
                Assert.Equal(AssetWorkspaceScope.Folder,Controller.Query.Scope);Assert.Same(Item,Assert.Single(AssetWorkspaceSelection.Current));Assert.Equal(1,Notifications);
            }finally { AssetWorkspaceSelection.Select(Old); }
        });
    }
    [Fact]
    public void SolutionRootRequiresRootIdentityAndDoesNotTreatMissingHierarchiesAsSolutions(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Assert.False(AssetSelectionPlanner.IsSolutionRoot(null,VSConstants.VSITEMID_NIL,false,()=>throw new InvalidOperationException()));
            Assert.False(AssetSelectionPlanner.IsSolutionRoot(null,7,false,()=>throw new InvalidOperationException()));
            Assert.False(AssetSelectionPlanner.IsSolutionRoot(null,VSConstants.VSITEMID_ROOT,true,()=>throw new InvalidOperationException()));
            Assert.False(AssetSelectionPlanner.IsSolutionRoot(null,VSConstants.VSITEMID_ROOT,false,()=>false));
            Assert.True(AssetSelectionPlanner.IsSolutionRoot(null,VSConstants.VSITEMID_ROOT,false,()=>true));
            var Solution=Proxy<EnvDTE.Solution>((_,_)=>throw new NotSupportedException());
            var Hierarchy=Proxy<IVsHierarchy>((Call,Args)=>{ Assert.Equal("GetProperty",Call.MethodName);Args[2]=Solution;return VSConstants.S_OK; });
            Assert.True(AssetSelectionPlanner.IsSolutionRoot(Hierarchy,VSConstants.VSITEMID_ROOT,false,()=>throw new InvalidOperationException()));
            var Project=Proxy<IVsHierarchy>((Call,Args)=>{ Args[2]=null!;return VSConstants.E_FAIL; });
            Assert.False(AssetSelectionPlanner.IsSolutionRoot(Project,VSConstants.VSITEMID_ROOT,false,()=>true));
        });
    }
    [Fact]
    public void RevealClearsEveryHidingFilterAndChangesProjectAndFolder(){
        var PathValue=Path.Combine(Path.GetTempPath(),"Game","Assets","Models","Hero.glb");
        var Item=new AssetWorkspaceItem { Kind=AssetWorkspaceKind.Model,State=AssetWorkspaceState.Source,Source=new AssetWorkspaceSource { ProjectPath="Game.csproj",SourcePath=PathValue,RelativePath="Assets/Models/Hero.glb",Provider="Project" } };
        var Query=new AssetWorkspaceQuery { ProjectPath="Other.csproj",Folder="Other",WholeProject=true,Search="not-the-hero",Kind="Audio",Provider="Package",State="InvalidMetadata",Locked=true,Details=true };
        Assert.Empty(Query.Apply([Item],default));
        Query.Reveal(Item);
        Assert.Same(Item,Assert.Single(Query.Apply([Item],default)));
        Assert.Equal(Item.Source.ProjectPath,Query.ProjectPath);
        Assert.Equal(Path.GetDirectoryName(PathValue),Query.Folder);
        Assert.False(Query.WholeProject);
        Assert.True(Query.Locked);
        Assert.True(Query.Details);
    }
    [Fact]
    public void SelectingInTheSolutionIsSilentAndNeverShowsOrCreatesTheWindow(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Calls=new List<string>();
            var Hierarchy=Proxy<IVsUIHierarchy>((_,_)=>throw new NotSupportedException());
            var Tree=Proxy<IVsUIHierarchyWindow>((Call,Args)=>{ Calls.Add(((EXPANDFLAGS)Args[2]).ToString());return VSConstants.S_OK; });
            var Frame=Proxy<IVsWindowFrame>((Call,Args)=>{
                Assert.Equal("GetProperty",Call.MethodName);
                Args[1]=Tree;
                return VSConstants.S_OK;
            });
            var Documents=Proxy<IVsUIShellOpenDocument>((Call,Args)=>{ Args[1]=Hierarchy;Args[2]=7u;Args[3]=null!;Args[4]=1;return VSConstants.S_OK; });
            var Shell=Proxy<IVsUIShell>((Call,Args)=>{
                Assert.Equal("FindToolWindow",Call.MethodName);
                Assert.Equal((uint)__VSFINDTOOLWIN.FTW_fFindFirst,(uint)Args[0]);
                Args[2]=Frame;
                return VSConstants.S_OK;
            });
            var Services=new Services(new Dictionary<Type,object> { [typeof(SVsUIShellOpenDocument)]=Documents,[typeof(SVsUIShell)]=Shell });
            Assert.True(AssetEditorNavigation.SelectInSolution(Services,@"C:\Linked\Hero.glb"));
            Assert.Equal(new[]{"EXPF_ExpandParentsToShowItem","EXPF_SelectItem"},Calls);
        });
    }
    [Fact]
    public void SelectingInTheSolutionDoesNothingForAFileOutsideItOrWithoutAnOpenExplorer(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Outside=Proxy<IVsUIShellOpenDocument>((Call,Args)=>{ Args[1]=null!;Args[2]=VSConstants.VSITEMID_NIL;Args[3]=null!;Args[4]=0;return VSConstants.S_OK; });
            Assert.False(AssetEditorNavigation.SelectInSolution(new Services(new Dictionary<Type,object> { [typeof(SVsUIShellOpenDocument)]=Outside }),@"C:\External\Idle.glb"));
            var Hierarchy=Proxy<IVsUIHierarchy>((_,_)=>throw new NotSupportedException());
            var Inside=Proxy<IVsUIShellOpenDocument>((Call,Args)=>{ Args[1]=Hierarchy;Args[2]=7u;Args[3]=null!;Args[4]=1;return VSConstants.S_OK; });
            var Closed=Proxy<IVsUIShell>((Call,Args)=>{ Args[2]=null!;return VSConstants.S_OK; });
            Assert.False(AssetEditorNavigation.SelectInSolution(new Services(new Dictionary<Type,object> { [typeof(SVsUIShellOpenDocument)]=Inside,[typeof(SVsUIShell)]=Closed }),@"C:\Linked\Hero.glb"));
        });
    }
    private sealed class Services(Dictionary<Type,object> Values):IServiceProvider {
        public object?GetService(Type ServiceType)=>Values.TryGetValue(ServiceType,out var Value)?Value:null;
    }
    private static T Proxy<T>(Func<IMethodCallMessage,object[],object> Handler) where T:class=>(T)new InterfaceProxy(typeof(T),Handler).GetTransparentProxy();
    private sealed class InterfaceProxy(Type Interface,Func<IMethodCallMessage,object[],object> Handler):RealProxy(Interface) {
        public override IMessage Invoke(IMessage Message){
            var Call=(IMethodCallMessage)Message;
            var Args=Call.Args;
            try { return new ReturnMessage(Handler(Call,Args),Args,Call.ArgCount,Call.LogicalCallContext,Call); }
            catch(Exception Error){ return new ReturnMessage(Error,Call); }
        }
    }
}
