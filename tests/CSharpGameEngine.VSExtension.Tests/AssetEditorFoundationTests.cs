using System.Numerics;
using System.Reflection;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>アセットエディター基盤（拡張子の対応表、パッケージ登録、開く場所の判定）の契約。</summary>
public sealed class AssetEditorFoundationTests {
    [Fact]
    public void Registry_ExtensionsAreUniqueAndCaseInsensitive(){
        var Extensions=AssetEditorRegistry.All.Select(static Descriptor=>Descriptor.Extension).ToArray();
        Assert.Equal(Extensions.Length,Extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Extensions,static Extension=>Assert.StartsWith(".",Extension));
        Assert.True(AssetEditorRegistry.TryGet(@"C:\Game\Assets\Box.GLB",out var Descriptor));
        Assert.Equal(AssetEditorKind.StaticMesh,Descriptor.Kind);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Game\Assets\readme.txt")]
    [InlineData(@"C:\Game\Assets\noextension")]
    public void Registry_RejectsUnsupportedPaths(string?Path)=>Assert.False(AssetEditorRegistry.Accepts(Path));
    [Fact]
    public void Registry_DelegatesSceneAndGraphDocumentsToTheirOwnEditors(){
        Assert.False(AssetEditorRegistry.Accepts(@"C:\Game\Scenes\Main.json"));
        Assert.False(AssetEditorRegistry.Accepts(@"C:\Game\Assets\Stage.cgeshadergraph"));
        Assert.False(AssetEditorRegistry.Accepts(@"C:\Game\Assets\Pbr.cgesubgraph"));
    }
    [Fact]
    public void Registry_MatchesTheWorkspaceClassificationForEditableKinds(){
        // 在庫の分類と専用エディターの対応表は同じ拡張子集合を指す。分類が増えて対応表が追従しないと、ダブルクリックで既定エディターに落ちる。
        var Candidates=new[]{ ".glb",".gltf",".fbx",".png",".jpg",".jpeg",".dds",".tga",".hdr",".exr",".wav",".ogg",".mp3",".flac",".cgeasset",".cgefont",".ttf",".otf",".cgenav",".cgephysics",".cgeshadergraph",".cgesubgraph" };
        foreach(var Extension in Candidates){
            var Kind=AssetWorkspaceClassification.Classify("Assets/Sample"+Extension);
            var Expected=Kind is AssetWorkspaceKind.Model or AssetWorkspaceKind.Texture or AssetWorkspaceKind.Audio or AssetWorkspaceKind.Data or AssetWorkspaceKind.Font;
            Assert.True(Expected==AssetEditorRegistry.Accepts("Assets/Sample"+Extension),Extension+" classified as "+Kind);
        }
        Assert.All(AssetEditorRegistry.All,static Descriptor=>Assert.NotNull(AssetWorkspaceClassification.Classify("Assets/Sample"+Descriptor.Extension)));
    }
    [Fact]
    public void Registry_KindsFollowTheClassification(){
        foreach(var Descriptor in AssetEditorRegistry.All){
            var Kind=AssetWorkspaceClassification.Classify("Assets/Sample"+Descriptor.Extension);
            var Expected=Kind switch {
                AssetWorkspaceKind.Model=>AssetEditorKind.StaticMesh,
                AssetWorkspaceKind.Texture=>AssetEditorKind.Texture,
                AssetWorkspaceKind.Audio=>AssetEditorKind.SoundWave,
                AssetWorkspaceKind.Font=>AssetEditorKind.Font,
                AssetWorkspaceKind.Data=>AssetEditorKind.DataAsset,
                _=>throw new InvalidOperationException(Descriptor.Extension),
            };
            Assert.Equal(Expected,Descriptor.Kind);
        }
        Assert.True(AssetEditorRegistry.TryGet("a.cgeasset",out var Data));
        Assert.True(Data.UsesTextBuffer);
        Assert.All(AssetEditorRegistry.All.Where(static Descriptor=>Descriptor.Kind!=AssetEditorKind.DataAsset),static Descriptor=>Assert.False(Descriptor.UsesTextBuffer));
    }
    [Fact]
    public void Package_RegistersEveryRegistryExtensionAboveTheDefaultEditors(){
        var Factory=typeof(AssetEditorFactory).GUID;
        var Registered=typeof(CSharpGameEngineViewerPackage).GetCustomAttributes<ProvideEditorExtensionAttribute>()
            .Where(Attribute=>Attribute.Factory==Factory)
            .ToArray();
        Assert.Equal(
            AssetEditorRegistry.All.Select(static Descriptor=>Descriptor.Extension).OrderBy(static Value=>Value,StringComparer.Ordinal),
            Registered.Select(static Attribute=>Attribute.Extension).OrderBy(static Value=>Value,StringComparer.Ordinal));
        // Visual Studio の JSON／画像エディターは 0x32 で登録する。
        Assert.All(Registered,static Attribute=>Assert.True(Attribute.Priority>0x32));
        Assert.All(Registered,static Attribute=>Assert.Equal(AssetEditorRegistry.ExtensionPriority,Attribute.Priority));
        var Views=typeof(CSharpGameEngineViewerPackage).GetCustomAttributes<ProvideEditorLogicalViewAttribute>()
            .Where(Attribute=>Attribute.FactoryType==Factory)
            .Select(static Attribute=>Attribute.LogicalView)
            .ToHashSet();
        Assert.Contains(VSConstants.LOGVIEWID_Designer,Views);
    }
    [Fact]
    public void Package_NoLongerRegistersTheReadOnlyModelPreviewFactory()=>
        Assert.DoesNotContain(
            typeof(CSharpGameEngineViewerPackage).GetCustomAttributes<ProvideEditorExtensionAttribute>(),
            static Attribute=>Attribute.Extension==".glb"&&Attribute.Factory!=typeof(AssetEditorFactory).GUID);
    [Fact]
    public void Package_RegistersTheAssetEditorOptionsPage()=>
        Assert.Contains(
            typeof(CSharpGameEngineViewerPackage).GetCustomAttributes<ProvideOptionPageAttribute>(),
            static Attribute=>Attribute.PageType==typeof(AssetEditorOptionsPage));
    [Theory]
    [InlineData(0,false,true)]
    [InlineData(0,true,false)]
    [InlineData(1,false,false)]
    [InlineData(1,true,false)]
    public void OpenLocation_FloatsOnlyTheFirstTimeAndOnlyWhenConfigured(int Setting,bool Placed,bool Expected)=>
        Assert.Equal(Expected,AssetEditorOpenLocationPolicy.ShouldFloat((AssetEditorOpenLocation)Setting,Placed));
    [Fact]
    public void OpenLocation_DefaultsToFloating()=>
        Assert.Equal(AssetEditorOpenLocation.Floating,SceneEditorSettings.DefaultAssetEditorOpenLocation);
    [Fact]
    public void OpenLocation_CentersThePreferredSizeInALargeMainWindow(){
        var Bounds=AssetEditorOpenLocationPolicy.CenteredBounds(100,50,3000,1500);
        Assert.Equal(AssetEditorOpenLocationPolicy.PreferredWidth,Bounds.Width);
        Assert.Equal(AssetEditorOpenLocationPolicy.PreferredHeight,Bounds.Height);
        Assert.Equal(100+(3000-Bounds.Width)/2,Bounds.Left);
        Assert.Equal(50+(1500-Bounds.Height)/2,Bounds.Top);
    }
    [Fact]
    public void OpenLocation_ShrinksToEightyFivePercentOfASmallMainWindow(){
        var Bounds=AssetEditorOpenLocationPolicy.CenteredBounds(0,0,1000,700);
        Assert.Equal(850,Bounds.Width);
        Assert.Equal(595,Bounds.Height);
        Assert.True(Bounds.Left>=0&&Bounds.Top>=0);
        Assert.True(Bounds.Left+Bounds.Width<=1000);
        Assert.True(Bounds.Top+Bounds.Height<=700);
    }
    [Fact]
    public void OpenLocation_NeverGoesBelowTheMinimumSize(){
        var Bounds=AssetEditorOpenLocationPolicy.CenteredBounds(0,0,400,300);
        Assert.Equal(AssetEditorOpenLocationPolicy.MinimumWidth,Bounds.Width);
        Assert.Equal(AssetEditorOpenLocationPolicy.MinimumHeight,Bounds.Height);
        Assert.True(Bounds.Left>=0&&Bounds.Top>=0);
    }
    [Theory]
    [InlineData(0x2u,false)]
    [InlineData(0x1u,false)]
    [InlineData(0x6u,false)]
    [InlineData(0x10u,true)]
    [InlineData(0x12u,true)]
    [InlineData(0x32u,true)]
    public void Placement_IsSkippedOnlyForShellRestoreOrSpecificEditorOpens(uint CreateFlags,bool Skipped)=>
        Assert.Equal(Skipped,AssetEditorFramePlacement.IsShellRestoreOrSpecificEditor(CreateFlags));
    [Fact]
    public void FrameFloater_DecidesBySetPropertyResultNotByReadback(){
        // 実機では文書フレームが浮いた後も FrameMode／IsFloating は「浮いていない」を返し続ける。読み戻しで成功判定すると毎回再試行してしまう。
        var Source=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestAssets","Source","AssetEditorFrameFloater.cs"));
        Assert.Contains("ErrorHandler.Failed(Hr)",Source,StringComparison.Ordinal);
        Assert.DoesNotContain("IsFloating(",Source,StringComparison.Ordinal);
    }
}
public sealed class AssetViewportCameraTests {
    private static PointerEvent Down(PointerButton Button,int X,int Y,bool Shift=false,bool Ctrl=false)=>new(PointerEventKind.Down,Button,X,Y,new RawInputModifiers(Shift,Ctrl,false));
    private static PointerEvent Move(int X,int Y,bool Shift=false,bool Ctrl=false)=>new(PointerEventKind.Move,PointerButton.None,X,Y,new RawInputModifiers(Shift,Ctrl,false));
    private static PointerEvent Up(PointerButton Button,int X,int Y)=>new(PointerEventKind.Up,Button,X,Y,new RawInputModifiers(false,false,false));
    private static AssetViewportCamera Framed(){
        var Camera=new AssetViewportCamera();
        Camera.Frame(new Vector3(1f,2f,3f),2f,16f/9f);
        return Camera;
    }
    [Fact]
    public void Frame_FocusesTheBoundsCenterAndBacksOffByTheRadius(){
        var Camera=Framed();
        Assert.Equal(new Vector3(1f,2f,3f),Camera.FocusPosition);
        Assert.True(Camera.FocusDistance>2f);
        Assert.Equal(2f,Camera.BoundsRadius);
        Assert.True(Vector3.Distance(Camera.Eye,Camera.FocusPosition)>=2f);
    }
    [Fact]
    public void Frame_ReplacesAnInvalidRadiusWithOne(){
        var Camera=new AssetViewportCamera();
        Camera.Frame(Vector3.Zero,0f,1f);
        Assert.Equal(1f,Camera.BoundsRadius);
        Assert.True(Camera.FocusDistance>0f);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeftOrRightDrag_OrbitsAroundTheFocusWithoutMovingIt(bool UseRight){
        var Button=UseRight?PointerButton.Right:PointerButton.Left;
        var Camera=Framed();
        var Focus=Camera.FocusPosition;
        var Distance=Camera.FocusDistance;
        var Yaw=Camera.Yaw;
        Assert.Equal(AssetViewportNavigationResult.CaptureStarted,Camera.Handle(Down(Button,100,100)));
        Assert.True(Camera.IsDragging);
        Assert.Equal(AssetViewportNavigationResult.CameraChanged,Camera.Handle(Move(140,100)));
        Assert.NotEqual(Yaw,Camera.Yaw);
        Assert.Equal(Focus,Camera.FocusPosition);
        Assert.Equal(Distance,Camera.FocusDistance);
        Assert.Equal(AssetViewportNavigationResult.CaptureEnded,Camera.Handle(Up(Button,140,100)));
        Assert.False(Camera.IsDragging);
    }
    [Fact]
    public void VerticalDrag_ChangesThePitchAndIsClampedByTheSharedSceneCamera(){
        var Camera=Framed();
        Camera.Handle(Down(PointerButton.Left,0,0));
        Camera.Handle(Move(0,100000));
        Assert.InRange(Camera.Pitch,SceneViewCamera.MinPitchRadians,SceneViewCamera.MaxPitchRadians);
        Camera.Handle(Move(0,-200000));
        Assert.InRange(Camera.Pitch,SceneViewCamera.MinPitchRadians,SceneViewCamera.MaxPitchRadians);
    }
    [Fact]
    public void InvertOrbit_ReversesTheHorizontalDirection(){
        var Normal=Framed();
        var Inverted=Framed();
        Inverted.InvertOrbitX=true;
        Normal.Handle(Down(PointerButton.Left,0,0));
        Inverted.Handle(Down(PointerButton.Left,0,0));
        Normal.Handle(Move(50,0));
        Inverted.Handle(Move(50,0));
        var Start=Framed().Yaw;
        Assert.True((Normal.Yaw-Start)*(Inverted.Yaw-Start)<0f);
    }
    [Fact]
    public void MiddleDragAndShiftLeftDrag_PanTheFocusInsteadOfOrbiting(){
        foreach(var (Button,Shift) in new[]{ (PointerButton.Middle,false),(PointerButton.Left,true) }){
            var Camera=Framed();
            var Yaw=Camera.Yaw;
            var Focus=Camera.FocusPosition;
            Camera.Handle(Down(Button,10,10,Shift));
            Assert.Equal(AssetViewportNavigationResult.CameraChanged,Camera.Handle(Move(60,40,Shift)));
            Assert.Equal(Yaw,Camera.Yaw);
            Assert.NotEqual(Focus,Camera.FocusPosition);
        }
    }
    [Fact]
    public void CtrlLeftDrag_DollysAndKeepsTheFocus(){
        var Camera=Framed();
        var Focus=Camera.FocusPosition;
        var Distance=Camera.FocusDistance;
        Camera.Handle(Down(PointerButton.Left,0,0,Ctrl:true));
        Camera.Handle(Move(0,50,Ctrl:true));
        Assert.True(Camera.FocusDistance>Distance);
        Camera.Handle(Move(0,-150,Ctrl:true));
        Assert.True(Camera.FocusDistance<Distance);
        Assert.Equal(Focus,Camera.FocusPosition);
    }
    [Fact]
    public void Wheel_DollysTowardsTheFocusAndNeverBelowTheMinimum(){
        var Camera=Framed();
        var Distance=Camera.FocusDistance;
        Camera.Dolly(1);
        Assert.True(Camera.FocusDistance<Distance);
        Camera.Dolly(-1);
        Assert.Equal(Distance,Camera.FocusDistance,3);
        Camera.Dolly(10000);
        Assert.True(Camera.FocusDistance>0f);
    }
    [Fact]
    public void ExtremeWheelAndDragAmounts_DoNotOverflowAndStayWithinTheMinimum(){
        var Camera=Framed();
        Camera.Dolly(-1000000);
        Assert.False(float.IsInfinity(Camera.FocusDistance)||float.IsNaN(Camera.FocusDistance));
        Camera.Dolly(1000000);
        Assert.True(Camera.FocusDistance>0f);
        Camera.DollyByPixels(float.MaxValue/2f);
        Assert.False(float.IsInfinity(Camera.FocusDistance)||float.IsNaN(Camera.FocusDistance));
        Camera.DollyByPixels(-float.MaxValue/2f);
        Assert.True(Camera.FocusDistance>0f);
    }
    [Fact]
    public void AnotherButtonDuringADragIsIgnoredUntilTheFirstIsReleased(){
        var Camera=Framed();
        Camera.Handle(Down(PointerButton.Left,0,0));
        Assert.Equal(AssetViewportNavigationResult.None,Camera.Handle(Down(PointerButton.Right,0,0)));
        Assert.Equal(AssetViewportNavigationResult.None,Camera.Handle(Up(PointerButton.Right,0,0)));
        Assert.True(Camera.IsDragging);
        Assert.Equal(AssetViewportNavigationResult.CaptureEnded,Camera.Handle(Up(PointerButton.Left,0,0)));
    }
    [Fact]
    public void CaptureLossEndsTheDragAndLaterMovesDoNothing(){
        var Camera=Framed();
        Camera.Handle(Down(PointerButton.Left,0,0));
        Assert.Equal(AssetViewportNavigationResult.CaptureEnded,Camera.Handle(new PointerEvent(PointerEventKind.CaptureLost,PointerButton.None,0,0,default)));
        var Yaw=Camera.Yaw;
        Assert.Equal(AssetViewportNavigationResult.None,Camera.Handle(Move(500,500)));
        Assert.Equal(Yaw,Camera.Yaw);
        Assert.Equal(AssetViewportNavigationResult.None,Camera.Handle(new PointerEvent(PointerEventKind.CaptureLost,PointerButton.None,0,0,default)));
    }
    [Fact]
    public void MoveWithoutAnyDeltaOrDragReportsNoChange(){
        var Camera=Framed();
        Assert.Equal(AssetViewportNavigationResult.None,Camera.Handle(Move(5,5)));
        Camera.Handle(Down(PointerButton.Left,5,5));
        Assert.Equal(AssetViewportNavigationResult.None,Camera.Handle(Move(5,5)));
        Assert.Equal(AssetViewportNavigationResult.None,Camera.Handle(Down(PointerButton.None,0,0)));
    }
    [Fact]
    public void ViewAndProjection_AreFiniteAndLookAtTheFocus(){
        var Camera=Framed();
        var View=Camera.View;
        var Projection=Camera.Projection(16f/9f);
        Assert.False(float.IsNaN(View.M11)||float.IsInfinity(View.M11)||float.IsNaN(Projection.M11)||float.IsInfinity(Projection.M11));
        var InView=Vector3.Transform(Camera.FocusPosition,View);
        Assert.True(InView.Z<0f);
        Assert.Equal(0f,InView.X,2);
        Assert.Equal(0f,InView.Y,2);
    }
}
public sealed class AssetMeshStatisticsTests {
    [Fact]
    public void From_ReportsCountsBoundsAndColor(){
        var Positions=new[]{ new Vector3(-1f,0f,2f),new Vector3(3f,4f,2f),new Vector3(0f,1f,-5f) };
        var Mesh=new SimpleMesh {
            Positions=Positions,
            Normals=[Vector3.UnitY,Vector3.UnitY,Vector3.UnitY],
            Indices=[0u,1u,2u,2u,1u,0u],
            BaseColorFactor=new Vector4(0.2f,0.4f,0.6f,1f),
            BoundsCenter=Vector3.Zero,
            BoundsRadius=5f,
        };
        var Statistics=AssetMeshStatistics.From(Mesh);
        Assert.Equal(3,Statistics.VertexCount);
        Assert.Equal(2,Statistics.TriangleCount);
        Assert.Equal(new Vector3(-1f,0f,-5f),Statistics.BoundsMin);
        Assert.Equal(new Vector3(3f,4f,2f),Statistics.BoundsMax);
        Assert.Equal(new Vector3(4f,4f,7f),Statistics.Size);
        Assert.Equal(new Vector4(0.2f,0.4f,0.6f,1f),Statistics.BaseColor);
    }
    [Fact]
    public void From_RejectsNull()=>Assert.Throws<ArgumentNullException>(()=>AssetMeshStatistics.From(null!));
}
