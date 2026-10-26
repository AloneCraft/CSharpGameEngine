using System.Numerics;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Runtime.Windows.Tests;

/// <summary>
/// ADR-0078/M0104：画面 UI サービスは、収集された <see cref="ScreenText"/> と <see cref="ScreenImage"/> を 1 つのレイヤーにまとめる。GPU は不要で、ファイルは関数から得るため、ここでは手作りフォントを使う。
/// </summary>
public class ScreenUiTests {
    private const string FontPath="Fonts/Test.cgefont";

    /// <summary>Ascent 0.8、descent 0.2 で 1 行が 1 em になる。'A' は 0.6 進んで描画され、' ' は 0.3 進んで何も描画せず、'?' は代替グリフ。</summary>
    private static byte[] FontBytes(){
        List<FontGlyph>Glyphs=[
            new(' ',0.3f,0f,0f,0f,0f,0f,0f,0f,0f),
            new('A',0.6f,0.1f,0f,0.6f,0.7f,0.25f,0.5f,0.5f,0.75f),
            new('?',0.5f,0.05f,0f,0.45f,0.7f,0.5f,0.5f,0.75f,0.75f),
        ];
        return new FontAtlas(8,8,0.8f,0.2f,0f,0.125f,Glyphs,new byte[8*8]).ToBytes();
    }

    private static ScreenUi MakeUi()=>new(Path=>Path==FontPath?FontBytes():null);

    private static ScreenText TextOf(string Text,int Layer=0,ScreenAnchor Anchor=ScreenAnchor.TopLeft,float OffsetX=0f,float OffsetY=0f){
        return new ScreenText { Text=Text,Font=FontPath,Anchor=Anchor,OffsetX=OffsetX,OffsetY=OffsetY,Layer=Layer };
    }

    private static (RenderScreenVertex[] Vertices,RenderScreenBatch[] Batches)Build(ScreenUi Ui,int Width=1920,int Height=1080){
        Assert.True(Ui.Build(Width,Height,out var Vertices,out var Batches));
        return (Vertices.ToArray(),Batches.ToArray());
    }

    [Theory]
    [InlineData(ScreenAnchor.TopLeft,ScreenAnchor.TopLeft,0f,0f)]
    [InlineData(ScreenAnchor.BottomRight,ScreenAnchor.BottomRight,1820f,980f)]
    [InlineData(ScreenAnchor.Center,ScreenAnchor.Center,910f,490f)]
    [InlineData(ScreenAnchor.BottomRight,ScreenAnchor.BottomCenter,1870f,980f)]
    [InlineData(ScreenAnchor.TopCenter,ScreenAnchor.TopRight,860f,0f)]
    public void Placement_PutsThePivotOnTheAnchor(ScreenAnchor Anchor,ScreenAnchor Pivot,float ExpectedX,float ExpectedY){
        var TopLeft=ScreenUiPlacement.TopLeft(Anchor,Pivot,Vector2.Zero,new Vector2(100f,100f),1920f,1080f);

        Assert.Equal(new Vector2(ExpectedX,ExpectedY),TopLeft);
    }

    [Fact]
    public void Placement_MovesByTheOffsetRightAndDown_AndRoundsToWholePixels(){
        var TopLeft=ScreenUiPlacement.TopLeft(ScreenAnchor.BottomRight,ScreenAnchor.BottomCenter,new Vector2(-148f,-48f),new Vector2(67.56f,48f),1920f,1080f);

        Assert.Equal(new Vector2(1738f,984f),TopLeft);
    }

    [Fact]
    public void NothingCollected_OrASurfaceWithoutArea_BuildsNoLayer(){
        var Ui=MakeUi();
        Assert.False(Ui.Build(1920,1080,out _,out _));

        Ui.Add(TextOf("A"));
        Assert.False(Ui.Build(0,1080,out _,out _));
        // ビルド失敗時は収集した内容を破棄する。
        Assert.False(Ui.Build(1920,1080,out _,out _));
    }

    [Fact]
    public void HiddenAndEmptyElements_AreNotDrawn(){
        var Ui=MakeUi();
        var Hidden=TextOf("A");
        Hidden.Visible=false;
        Ui.Add(Hidden);
        Ui.Add(TextOf(string.Empty));
        var HiddenImage=new ScreenImage { Width=10f,Height=10f,Visible=false };
        Ui.Add(HiddenImage);

        Assert.False(Ui.Build(1920,1080,out _,out _));
    }

    [Fact]
    public void ALayer_LastsOneBuild(){
        var Ui=MakeUi();
        Ui.Add(TextOf("A"));
        Build(Ui);

        Assert.False(Ui.Build(1920,1080,out _,out _));
    }

    [Fact]
    public void Text_DrawsTwoTrianglesPerVisibleGlyph_AsOneDistanceFieldBatch(){
        var Ui=MakeUi();
        Ui.Add(TextOf("A A"));

        var (Vertices,Batches)=Build(Ui);

        // 空白には四角形がなく、'A' 1 文字につき頂点は 6 つ。
        Assert.Equal(12,Vertices.Length);
        var Batch=Assert.Single(Batches);
        Assert.Equal(RenderScreenShading.DistanceField,Batch.Shading);
        Assert.Equal(0,Batch.FirstVertex);
        Assert.Equal(12,Batch.VertexCount);
    }

    [Fact]
    public void Text_ScalesWithTheShorterSideOfTheSurface(){
        var Ui=MakeUi();
        Ui.Add(TextOf("A"));
        var (Large,_)=Build(Ui,1920,1080);
        Ui.Add(TextOf("A"));
        var (Small,_)=Build(Ui,1280,720);

        Assert.Equal(Large.Length,Small.Length);
        for(var Index=0;Index<Large.Length;Index++){
            Assert.Equal(Large[Index].Position.X*720f/1080f,Small[Index].Position.X,0.01f);
            Assert.Equal(Large[Index].Position.Y*720f/1080f,Small[Index].Position.Y,0.01f);
        }
    }

    [Fact]
    public void ElementsOfAHigherLayer_AreDrawnLater_AndSameFontTextsShareOneBatch(){
        var Ui=MakeUi();
        var Top=TextOf("A",Layer:2);
        Top.ColorR=0f;
        Ui.Add(Top);
        Ui.Add(TextOf("A",Layer:0));
        var Backdrop=new ScreenImage { Width=100f,Height=50f,Layer=1,ColorA=0.5f };
        Ui.Add(Backdrop);

        var (Vertices,Batches)=Build(Ui);

        // レイヤー 0 のテキスト、次にレイヤー 1 の矩形、最後にレイヤー 2 のテキスト。2 つのテキストは隣接しないため、バッチは 3 つ。
        Assert.Equal(3,Batches.Length);
        Assert.Equal(new[]{ RenderScreenShading.DistanceField,RenderScreenShading.Image,RenderScreenShading.DistanceField },Batches.Select(static Batch=>Batch.Shading).ToArray());
        Assert.Equal(6,Batches[1].VertexCount);
        Assert.Equal(0.5f,Vertices[Batches[1].FirstVertex].Color.W);
        Assert.Equal(0f,Vertices[Batches[2].FirstVertex].Color.X);
        Assert.Equal(1f,Vertices[Batches[0].FirstVertex].Color.X);
    }

    [Fact]
    public void ARectangleWithoutAnImage_DrawsItsColorWhereItIsPlaced(){
        var Ui=MakeUi();
        Ui.Add(new ScreenImage { Width=200f,Height=100f,Anchor=ScreenAnchor.BottomRight,Pivot=ScreenAnchor.BottomRight,OffsetX=-20f,OffsetY=-10f,ColorR=1f,ColorG=0f,ColorB=0f,ColorA=1f });

        var (Vertices,_)=Build(Ui);

        Assert.Equal(new Vector2(1700f,970f),Vertices[0].Position);
        Assert.Equal(new Vector2(1900f,1070f),Vertices[4].Position);
        Assert.Equal(new Vector4(1f,0f,0f,1f),Vertices[0].Color);
    }

    [Fact]
    public void AFontThatIsMissingOrBroken_DrawsNothing_AndIsReportedOnce(){
        var Ui=new ScreenUi(static Path=>Path=="Fonts/Broken.cgefont"?[1,2,3]:null);
        var Missing=TextOf("A");
        Missing.Font="Fonts/Nowhere.cgefont";
        var Broken=TextOf("A");
        Broken.Font="Fonts/Broken.cgefont";

        for(var Frame=0;Frame<3;Frame++){
            Ui.Add(Missing);
            Ui.Add(Broken);
            Assert.False(Ui.Build(1920,1080,out _,out _));
        }

        Assert.Equal(2,Ui.Diagnostics.Count);
        Assert.Contains(Ui.Diagnostics,static Message=>Message.Contains("Nowhere",StringComparison.Ordinal));
        Assert.Contains(Ui.Diagnostics,static Message=>Message.Contains("Broken",StringComparison.Ordinal));
    }

    [Fact]
    public void NonFiniteOrNonPositiveValues_AreDroppedInsteadOfDrawn(){
        var Ui=MakeUi();
        var NoSize=TextOf("A");
        NoSize.Size=0f;
        var NotANumber=TextOf("A",OffsetX:float.NaN);
        var Backward=new ScreenImage { Width=-5f,Height=5f };
        Ui.Add(NoSize);
        Ui.Add(NotANumber);
        Ui.Add(Backward);

        Assert.False(Ui.Build(1920,1080,out _,out _));
    }

    [Fact]
    public void TheFingerprint_FollowsWhatIsDrawn(){
        var Ui=MakeUi();
        Ui.Add(TextOf("A"));
        var (First,_)=Build(Ui);
        Ui.Add(TextOf("A"));
        var (Again,_)=Build(Ui);
        Ui.Add(TextOf("AA"));
        var (Other,_)=Build(Ui);

        Assert.Equal(ScreenUi.Fingerprint(First),ScreenUi.Fingerprint(Again));
        Assert.NotEqual(ScreenUi.Fingerprint(First),ScreenUi.Fingerprint(Other));
    }
}
