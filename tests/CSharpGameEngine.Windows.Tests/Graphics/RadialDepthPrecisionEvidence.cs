using System.Globalization;
using System.Numerics;
using System.Text;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using Xunit.Abstractions;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// M0119 native precision evidence (ADR-0084): the radial depth an adapter writes, per pixel, against a double-precision oracle,
/// for a quad facing the camera from 4 µm through 5e18 m. In the middle of the view the distance hardly changes
/// from pixel to pixel, so the error there is the shader arithmetic's and stays within 4 ulps on every adapter. Near the
/// edges the rasterizer's snapping of vertices to its sub-pixel grid adds error in proportion to the distance gradient;
/// it is recorded, not gated. WARP evaluates log2 exactly, so run this on the GPU to see hardware arithmetic:
/// <c>CGE_PERFORMANCE_EVIDENCE=1</c> and <c>CGE_PERFORMANCE_ADAPTER=hardware</c>. Writes <c>.artifacts/M0119/precision</c>.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RadialDepthPrecisionEvidence(ITestOutputHelper Output) {
    private const int Width=DepthOracle.Width;
    private const int Height=DepthOracle.Height;
    [PerformanceEvidenceFact]
    [Trait("Category","GPU")]
    public void RadialDepth_MatchesTheDoubleOracleInTheMiddleOfTheView(){
        var Hardware=Environment.GetEnvironmentVariable("CGE_PERFORMANCE_ADAPTER")=="hardware";
        var Cases=new List<string>();
        List<string>Errors=[];
        string?Adapter=null;
        foreach(var (Distance,Near,Far) in new[]{(4e-6f,1e-7f,5e-5f),(0.5f,0.1f,300f),(50f,0.1f,1000f),(2e4f,0.1f,3e4f),(1e15f,0.1f,2e15f),(5e18f,0.1f,1e19f)}){
            var (Centre,Edge,Description)=Measure(Hardware,Distance,Near,Far);
            Adapter??=Description;
            Output.WriteLine($"{Description} D={Distance:R} m: centre max {Centre:F1} ulp, edge max {Edge:F1} ulp");
            Cases.Add(string.Create(CultureInfo.InvariantCulture,$"    {{ \"distanceMeters\": {Distance:R}, \"nearZ\": {Near:R}, \"farZ\": {Far:R}, \"centreMaxUlps\": {Centre:R}, \"edgeMaxUlps\": {Edge:R} }}"));
            if(Centre>4d){ Errors.Add($"{Description}: depth at {Distance} m is {Centre:F1} ulps off in the middle of the view."); }
        }
        var Directory=Path.Combine(RepositoryRoot(),".artifacts","M0119","precision");
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory,$"radial-precision-{(Hardware?"hardware":"warp")}.json"),
            $"{{\n  \"schema\": \"cge-radial-precision-evidence/1\",\n  \"adapter\": \"{Adapter}\",\n  \"width\": {Width},\n  \"height\": {Height},\n  \"cases\": [\n{string.Join(",\n",Cases)}\n  ]\n}}\n",
            Encoding.UTF8);
        Assert.True(Errors.Count==0,string.Join(Environment.NewLine,Errors));
    }
    /// <summary>The largest error, in ulps of the expected depth, in the middle 17 × 11 pixels and in a 6–10 pixel border (the shared <see cref="DepthOracle"/>).</summary>
    private static (double Centre,double Edge,string Adapter)Measure(bool Hardware,float Distance,float Near,float Far){
        var Result=DepthOracle.Measure(Hardware,Distance,Near,Far);
        return (Result.Centre,Result.Edge,Result.Adapter);
    }
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){
            Directory=Directory.Parent;
        }
        return Directory?.FullName??throw new InvalidOperationException("The repository root was not found.");
    }
}
