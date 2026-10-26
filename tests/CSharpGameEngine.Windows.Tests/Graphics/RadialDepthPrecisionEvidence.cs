using System.Globalization;
using System.Numerics;
using System.Text;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using Xunit.Abstractions;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// M0119のネイティブ精度の証拠（ADR-0084）。カメラに正対したクアッドを4 µm～5e18 mの範囲に描画し、アダプターがピクセルごとに書き込む放射状深度を
/// 倍精度オラクルと比較する。ビュー中央ではピクセル間の距離差が小さいため、誤差はシェーダー演算に由来し、すべてのアダプターで4 ULP以内に収まる。
/// 端では頂点をサブピクセル格子へスナップする際に、距離勾配に比例する誤差が加わる。これは記録のみで合否判定には使わない。
/// WARPはlog2を正確に評価するため、ハードウェア演算を調べるにはGPUで実行すること：
/// <c>CGE_PERFORMANCE_EVIDENCE=1</c>と<c>CGE_PERFORMANCE_ADAPTER=hardware</c>を指定。<c>.artifacts/M0119/precision</c>へ出力する。
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
    /// <summary><see cref="DepthOracle"/>を共有して求める、中央17 × 11ピクセルと幅6～10ピクセルの境界における期待深度からの最大ULP誤差。</summary>
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
