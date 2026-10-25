using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// M0099 Phase 1: the standard depth mode's frames are pinned as SHA-256 hashes of the WARP back buffer before the
/// radial depth mode is added, so no later change to the shared Stage program, FrameCB or pipeline states can move
/// a standard pixel unnoticed. The scene is <see cref="GoldenFrameScene"/>. M0119 (ADR-0084) removes the standard
/// depth: <see cref="LogarithmicFrames_EqualTheStandardFramesWithinOneLevel"/> is the last comparison of the
/// logarithmic frames with these, and <see cref="StageGoldenFrameTests"/> pins the logarithmic frames themselves.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class StandardModeGoldenFrameTests {
    private static readonly Dictionary<string,string>ExpectedHashes=new(){
        ["legacy-lit"]="3F7940D2345F87022E7050E7CE31C31E88AF18E6AB0984189692CE9313905A64",
        ["environment"]="E16BAAAA34D474B4A683B2187C1988F2D57947F53BCC259482BF0775E7E62415",
        ["selection"]="2A65C34872C6E0953D0FD8303FADA29936B02A8F85C35CFE9A68033A702C7273",
        ["base-color"]="C1321971CCBB47E60800E36084AE550109FF63243897D5E62C8C56B448FAB7A9",
        ["normal"]="A0C9623A15D73265623CDE7E30591B9B6A20ECEE459931067C3F381668B25880",
        ["wireframe"]="25DB6AB16B2F38481923D1F40989AF0E24C128E201731C12764A82FD2BAB82E7",
        ["previews-and-overlay"]="A823A16303399452564DC404B26FD046F66D3361662F3E9709BA19A8D1932102",
    };
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void StandardFrames_MatchTheirPinnedHashes(){
        List<string>Mismatches=[];
        foreach(var (Case,Expected) in ExpectedHashes){
            var Hash=GoldenFrameDump.Hash(GoldenFrameScene.Render(Case,RenderDepthMapping.Standard));
            if(Hash!=Expected){
                Mismatches.Add($"[\"{Case}\"]=\"{Hash}\",");
            }
        }
        Assert.True(Mismatches.Count==0,string.Join(System.Environment.NewLine,Mismatches));
    }
    /// <summary>
    /// ADR-0084 §6: the last comparison before the standard depth is deleted. Every case drawn with logarithmic depth
    /// equals the standard frame within one 8-bit level of any channel, with at most 1 % of the pixels different at all
    /// (the wireframe may differ by more on at most 1 % of the pixels: a wireframe edge drawn from both of its
    /// triangles has an exact depth tie, which draw order settles in the standard mode and rounding in the radial one).
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void LogarithmicFrames_EqualTheStandardFramesWithinOneLevel(){
        List<string>Failures=[];
        List<object>Report=[];
        foreach(var Case in GoldenFrameScene.Cases){
            var Standard=GoldenFrameScene.Render(Case,RenderDepthMapping.Standard);
            var Logarithmic=GoldenFrameScene.Render(Case,RenderDepthMapping.Radial);
            GoldenFrameDump.Record("standard",Case,Standard);
            GoldenFrameDump.Record("logarithmic",Case,Logarithmic);
            var (Different,Largest)=GoldenFrameDump.Difference(Standard.Pixels,Logarithmic.Pixels);
            var Total=Standard.Pixels.Length/4;
            var Accepted=Different<=Total/100&&(Case=="wireframe"||Largest<=1);
            Report.Add(new { Case,Pixels=Total,Different,LargestChannelDifference=Largest,Accepted,StandardHash=GoldenFrameDump.Hash(Standard),LogarithmicHash=GoldenFrameDump.Hash(Logarithmic) });
            if(!Accepted){
                Failures.Add($"{Case}: {Different} of {Total} pixels differ, largest channel difference {Largest}");
            }
        }
        GoldenFrameDump.WriteReport("parity.json",Report);
        Assert.True(Failures.Count==0,string.Join(System.Environment.NewLine,Failures));
    }
}
