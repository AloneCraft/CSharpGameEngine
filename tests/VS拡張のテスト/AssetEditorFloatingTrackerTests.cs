using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(MockedVsCollection.Name)]
public sealed class AssetEditorFloatingTrackerTests {
    public AssetEditorFloatingTrackerTests(MockedVsFixture Fixture){ }
    [Fact]
    public void RegisterAndUnregister_TracksAliveFrames(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            AssetEditorFloatingTracker.ResetForTesting();
            var Services=new TestServices([]);
            var Frame=Proxy<IVsWindowFrame>((Call,Args)=>{
                if(Call.MethodName=="IsVisible"){ return VSConstants.S_OK; }
                if(Call.MethodName=="GetProperty"){ return VSConstants.E_NOTIMPL; }
                return VSConstants.S_OK;
            });
            Assert.Null(AssetEditorFloatingTracker.GetActiveFloatingFrame(Services));
            AssetEditorFloatingTracker.Register(Frame);
            Assert.Same(Frame,AssetEditorFloatingTracker.GetActiveFloatingFrame(Services));
            AssetEditorFloatingTracker.Unregister(Frame);
            Assert.Null(AssetEditorFloatingTracker.GetActiveFloatingFrame(Services));
        });
    }
    [Fact]
    public void TryDockToExistingFloating_SetsMdiContainerId(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Services=new TestServices([]);
            const int TargetContainerId=42;
            var Calls=new List<string>();
            var Existing=Proxy<IVsWindowFrame>((Call,Args)=>{
                if(Call.MethodName=="GetProperty"&&(int)Args[0]==-5010){
                    Args[1]=TargetContainerId;
                    return VSConstants.S_OK;
                }
                return VSConstants.E_NOTIMPL;
            });
            var Target=Proxy<IVsWindowFrame>((Call,Args)=>{
                if(Call.MethodName=="SetProperty"&&(int)Args[0]==-5010){
                    Assert.Equal(TargetContainerId,Args[1]);
                    Calls.Add("SetProperty_Container");
                    return VSConstants.S_OK;
                }
                if(Call.MethodName=="Show"){
                    Calls.Add("Show");
                    return VSConstants.S_OK;
                }
                return VSConstants.E_NOTIMPL;
            });
            var Result=AssetEditorFrameFloater.TryDockToExistingFloating(Target,Existing,null,Services);
            Assert.True(Result.Floating);
            Assert.Equal("MDIContainerID",Result.Route);
            Assert.Equal("containerId=42",Result.Detail);
            Assert.Equal(["SetProperty_Container","Show"],Calls);
        });
    }
    [Fact]
    public void TryDockToExistingFloating_DocksViaDocumentGroupChildrenFallback(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Services=new TestServices([]);
            var ExistingGroup=new FakeDocumentGroup();
            var ExistingView=new FakeView(ExistingGroup);
            var TargetGroup=new FakeDocumentGroup();
            var TargetView=new FakeView(TargetGroup);
            TargetGroup.Children.Add(TargetView);
            var Existing=new FakeWindowFrameWithView(ExistingView);
            var Target=new FakeWindowFrameWithView(TargetView);
            var Result=AssetEditorFrameFloater.TryDockToExistingFloating(Target,Existing,null,Services);
            Assert.True(Result.Floating);
            Assert.Equal("DocumentGroup.Children",Result.Route);
            Assert.True(ExistingGroup.Children.Contains(TargetView));
            Assert.False(TargetGroup.Children.Contains(TargetView));
            Assert.Same(TargetView,ExistingGroup.SelectedElement);
            Assert.True(Target.ShowCalled);
        });
    }
    private sealed class FakeWindowFrameWithView(FakeView FrameView):IVsWindowFrame {
        public FakeView FrameView { get;}=FrameView;
        public bool ShowCalled { get;private set;}
        public int Show(){ this.ShowCalled=true;return VSConstants.S_OK; }
        public int Hide()=>VSConstants.S_OK;
        public int IsVisible()=>VSConstants.S_OK;
        public int ShowNoActivate()=>VSConstants.S_OK;
        public int CloseFrame(uint grfSaveOptions)=>VSConstants.S_OK;
        public int SetFramePos(VSSETFRAMEPOS dwFlags,ref Guid rguidRelativeTo,int x,int y,int cx,int cy)=>VSConstants.S_OK;
        public int GetFramePos(VSSETFRAMEPOS[] pdwFlags,out Guid pguidRelativeTo,out int px,out int py,out int pcx,out int pcy){ pguidRelativeTo=Guid.Empty;px=py=pcx=pcy=0;return VSConstants.S_OK; }
        public int GetProperty(int propid,out object pvar){ pvar=null!;return VSConstants.E_NOTIMPL; }
        public int SetProperty(int propid,object var)=>VSConstants.E_NOTIMPL;
        public int GetGuidProperty(int propid,out Guid pguid){ pguid=Guid.Empty;return VSConstants.E_NOTIMPL; }
        public int SetGuidProperty(int propid,ref Guid rguid)=>VSConstants.E_NOTIMPL;
        public int QueryViewInterface(ref Guid riid,out IntPtr ppv){ ppv=IntPtr.Zero;return VSConstants.E_NOTIMPL; }
        public int IsOnScreen(out int pfOnScreen){ pfOnScreen=1;return VSConstants.S_OK; }
    }
    private sealed class FakeView(FakeDocumentGroup Parent){
        public FakeDocumentGroup Parent { get;}=Parent;
    }
    private sealed class FakeDocumentGroup {
        public System.Collections.ArrayList Children { get;}=[];
        public object?SelectedElement { get;set;}
    }
    private sealed class TestServices(Dictionary<Type,object> Values):IServiceProvider {
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