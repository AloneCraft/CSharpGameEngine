using System.Globalization;
using System.Numerics;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetInspectorFactsTests {
    private static readonly CultureInfo English=new("en-US");
    [Theory]
    [InlineData(0L,"0 B")]
    [InlineData(1023L,"1,023 B")]
    [InlineData(1024L,"1.0 KB")]
    [InlineData(1536L,"1.5 KB")]
    [InlineData(1048576L,"1.0 MB")]
    [InlineData(1610612736L,"1.5 GB")]
    public void FileSizesUseTheLargestUnitThatKeepsTheNumberSmall(long Bytes,string Expected){
        Assert.Equal(Expected,AssetInspectorFacts.FormatBytes(Bytes,English));
    }
    [Fact]
    public void DurationsAreMinutesAndSeconds(){
        Assert.Equal("0:00.589",AssetInspectorFacts.FormatDuration(0.589f,English));
        Assert.Equal("2:05.500",AssetInspectorFacts.FormatDuration(125.5f,English));
        Assert.Equal("0:00.000",AssetInspectorFacts.FormatDuration(-3f,English));
    }
    [Fact]
    public void EveryKindGetsTheFileSizeTheModifiedTimeAndTheProvider(){
        var Item=new AssetWorkspaceItem { SourceLength=2048,ModifiedUtcTicks=new DateTime(2026,10,28,1,2,3,DateTimeKind.Utc).Ticks,Kind=AssetWorkspaceKind.Font };
        var Facts=AssetInspectorFacts.Common(Item,English,"Project");
        Assert.Equal(3,Facts.Count);
        Assert.Equal("2.0 KB",Facts[0].Value);
        Assert.Equal("Project",Facts[2].Value);
        Assert.All(Facts,static Fact=>Assert.NotEqual(string.Empty,Fact.Label));
        // 大きさも更新時刻もまだ分からない項目は、提供元だけ。
        Assert.Single(AssetInspectorFacts.Common(new AssetWorkspaceItem(),English,"Engine"));
    }
    [Fact]
    public void AModelShowsItsCountsSizeSkinsAndClipsWhenTheyAreKnown(){
        var Stats=new AssetMeshStatistics { VertexCount=9770,TriangleCount=11172,BoundsMin=new Vector3(-1f,0f,-2f),BoundsMax=new Vector3(3.708f,1.243f,0.285f) };
        var Probe=new GltfSkeletalInfo(1,2,["Idle","Walk"]);
        var Facts=AssetInspectorFacts.Model(Stats,Probe,English);
        Assert.Equal(new[]{ "9,770","11,172","4.708 × 1.243 × 2.285","1","2" },Facts.Select(static Fact=>Fact.Value));
        Assert.Equal(2,AssetInspectorFacts.Model(null,Probe,English).Count);
        Assert.Equal(3,AssetInspectorFacts.Model(Stats,null,English).Count);
        Assert.Empty(AssetInspectorFacts.Model(null,null,English));
    }
    [Fact]
    public void ATextureShowsItsPixelSizeOnlyWhenItIsKnown(){
        Assert.Equal("1,312 × 1,199 px",Assert.Single(AssetInspectorFacts.Texture(1312,1199,English)).Value);
        Assert.Empty(AssetInspectorFacts.Texture(0,10,English));
    }
    [Fact]
    public void AnAudioFileShowsItsFormatAndLength(){
        var Facts=AssetInspectorFacts.Audio(new AssetAudioPreview { SampleRate=48000,Channels=1,BitsPerSample=16,DurationSeconds=0.589f },English);
        Assert.Equal(new[]{ "48,000 Hz","Mono","16 bit","0:00.589" },Facts.Select(static Fact=>Fact.Value));
        Assert.Equal("Stereo",AssetInspectorFacts.Audio(new AssetAudioPreview { SampleRate=44100,Channels=2,BitsPerSample=24,DurationSeconds=1f },English)[1].Value);
    }
}
