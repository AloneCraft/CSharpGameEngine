using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// M0119 (ADR-0084): the logarithmic frames of <see cref="GoldenFrameScene"/> pinned as SHA-256 hashes of the WARP
/// back buffer. They replace <see cref="StandardModeGoldenFrameTests"/> once the standard depth is deleted; they were
/// pinned only after <see cref="StandardModeGoldenFrameTests.LogarithmicFrames_EqualTheStandardFramesWithinOneLevel"/>
/// accepted every case. Re-pin a hash only with the dump comparison of ADR-0084 §7 (CGE_GOLDEN_DUMP and
/// CGE_GOLDEN_BASELINE) and a note in the milestone document.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class StageGoldenFrameTests {
    private static readonly Dictionary<string,string>ExpectedHashes=new(){
        ["legacy-lit"]="3E56DB8CABE1989F7D25078823AD71625E1D43C2BE36DF28B0AE3AA6911714D6",
        ["environment"]="9D47DDD9EE03EDA7D5E741BCD87F5ECFD06B13818DE2DE6F49C66135F57D15F7",
        ["selection"]="19F90F949E3EEFDD6A18652F64E9672AD62478F85C04D73929395D6D41F70CC3",
        ["base-color"]="C1321971CCBB47E60800E36084AE550109FF63243897D5E62C8C56B448FAB7A9",
        ["normal"]="A0C9623A15D73265623CDE7E30591B9B6A20ECEE459931067C3F381668B25880",
        ["wireframe"]="E115C10FB16308748CBEBCA934A89EC09080C808A8D5CA896074483A6F4D01F7",
        ["previews-and-overlay"]="5A7F4B3A867C91AA13651EEAE3F392FF0CD75C6424E1575AA96494068B3D7937",
    };
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void LogarithmicFrames_MatchTheirPinnedHashes(){
        List<string>Mismatches=[];
        foreach(var (Case,Expected) in ExpectedHashes){
            var Frame=GoldenFrameScene.Render(Case,RenderDepthMapping.Radial);
            var Note=GoldenFrameDump.Record("logarithmic",Case,Frame);
            var Hash=GoldenFrameDump.Hash(Frame);
            if(Hash!=Expected){
                Mismatches.Add($"[\"{Case}\"]=\"{Hash}\", {Note}");
            }
        }
        Assert.True(Mismatches.Count==0,string.Join(System.Environment.NewLine,Mismatches));
    }
}
