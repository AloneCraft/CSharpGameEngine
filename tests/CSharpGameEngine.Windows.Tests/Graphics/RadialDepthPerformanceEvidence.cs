using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Globalization;
using System.Numerics;
using System.Text;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using Xunit.Abstractions;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// M0119の性能証拠（ADR-0084）。ネイティブのピクセルエントリーポイントが深度を計算するため、Early-ZでStageピクセルシェーダー実行前に
/// 隠れた破片を棄却できるのは、保守的なラスター深度（SV_DepthGreaterEqual）だけである。前から後ろ、後ろから前の順に描く全画面レイヤーのシーンで、
/// タイムスタンプを使ってメインパスのGPU時間を測る（Early-Zが最も効果を発揮するケース）。near（2～48 m）とfar（100～1020 m。各オブジェクトのラスター深度は
/// それぞれの距離に接する、M0100）を測定し、ネイティブサンプルを<c>.artifacts/M0119/performance</c>へ記録する。結果は記録のみで合否判定には使わない。
/// 時間はアダプターに依存する。<c>CGE_PERFORMANCE_EVIDENCE=1</c>で実行する（WARPではなくGPUを使う場合は<c>CGE_PERFORMANCE_ADAPTER=hardware</c>も指定）。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RadialDepthPerformanceEvidence(ITestOutputHelper Output) {
    private const int Width=1280;
    private const int Height=720;
    private const int Layers=24;
    private const int Warmup=5;
    private const int Samples=30;
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly LinearLightColor White=new(1f,1f,1f);
    private static readonly LightShadowSettings Shadow=new(0,new ShadowBias(0.005f));
    [PerformanceEvidenceFact]
    [Trait("Category","GPU")]
    public void MainPass_GpuTime_NativeLogarithmicDepth(){
        var Hardware=Environment.GetEnvironmentVariable("CGE_PERFORMANCE_ADAPTER")=="hardware";
        var Report=new StringBuilder();
        Report.Append("{\n  \"schema\": \"cge-native-depth-performance-evidence/2\",\n");
        string?Adapter=null;
        var Results=new List<string>();
        foreach(var (Range,FirstMeters,StepMeters,FarZ) in new[]{("near",2f,2f,1000f),("far",100f,40f,2000f)}){
            foreach(var FrontToBack in new[]{true,false}){
                var (Times,Description)=Measure(Hardware,FrontToBack,FirstMeters,StepMeters,FarZ);
                Adapter??=Description;
                Array.Sort(Times);
                var Median=Times[Times.Length/2];
                var P95=Times[(int)MathF.Ceiling(Times.Length*0.95f)-1];
                var Name=$"Logarithmic-{Range}-{(FrontToBack?"front-to-back":"back-to-front")}";
                Output.WriteLine($"{Name}: median {Median:F3} ms, p95 {P95:F3} ms");
                Results.Add(string.Create(CultureInfo.InvariantCulture,
                    $"    {{ \"case\": \"{Name}\", \"medianMilliseconds\": {Median:R}, \"p95Milliseconds\": {P95:R}, \"samples\": [{string.Join(", ",Times.Select(static Time=>Time.ToString("R",CultureInfo.InvariantCulture)))}] }}"));
            }
        }
        Report.Append(CultureInfo.InvariantCulture,$"  \"adapter\": \"{Adapter}\",\n  \"width\": {Width},\n  \"height\": {Height},\n  \"layers\": {Layers},\n  \"warmup\": {Warmup},\n  \"cases\": [\n");
        Report.Append(string.Join(",\n",Results)).Append("\n  ]\n}\n");
        var Directory=Path.Combine(RepositoryRoot(),".artifacts","M0119","performance");
        System.IO.Directory.CreateDirectory(Directory);
        var Path_=Path.Combine(Directory,$"native-depth-performance-{(Hardware?"hardware":"warp")}.json");
        File.WriteAllText(Path_,Report.ToString());
        Output.WriteLine($"Adapter: {Adapter}");
        Output.WriteLine($"Report: {Path_}");
    }
    private static (float[]Times,string Adapter)Measure(bool Hardware,bool FrontToBack,float FirstMeters,float StepMeters,float FarZ){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Radial Performance",Width=Width,Height=Height,Visible=false });
        var Plane=ProceduralMeshFactory.CreatePlane(Width:1f,Depth:1f);
        var Tangents=new Vector4[Plane.Positions.Length];
        Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
        StageMeshCreateInfo[]Meshes=[new StageMeshCreateInfo { Positions=Plane.Positions,Normals=Plane.Normals,Tangents=Tangents,Uvs=new Vector2[Plane.Positions.Length],Indices=Plane.Indices }];
        var Materials=Enumerable.Range(0,Layers).Select(static Index=>new StageMaterialCreateInfo {
            BaseColorFactor=new Vector4(0.3f+0.02f*Index,0.4f,0.6f-0.01f*Index,1f),RoughnessFactor=0.5f,MetallicFactor=0f }).ToArray();
        var Objects=Enumerable.Range(0,Layers).Select(static Index=>new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=Index }).ToArray();
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions { UseWarp=!Hardware,EnableDebugLayer=false },ShaderArtifactPath,Meshes,Materials,Objects);
        Renderer.EnableMainPassTimingForTesting();
        var View=Camera.CreateFirstPerson(Vector3.Zero);
        View.FarZ=FarZ;
        var Times=new float[Samples];
        for(var Frame=0;Frame<Warmup+Samples;Frame++){
            Renderer.SetCamera(View);
            Renderer.SetAmbientLight(new Vector3(0.05f));
            Renderer.Submit(new DirectionalLightSubmission(Vector3.Normalize(new Vector3(0.3f,-0.5f,1f)),White,new(3f),Shadow));
            for(var Light=0;Light<4;Light++){
                Renderer.Submit(new PointLightSubmission(new Vector3(-3f+2f*Light,1f,1.5f),new(1f,0.8f,0.6f),new(300f),new(40f)));
            }
            for(var Index=0;Index<Layers;Index++){
                // レイヤーkはFirst + k * Stepメートルに配置する。描画順はオブジェクトインデックスに従う。
                var Depth=FirstMeters+StepMeters*(FrontToBack?Index:Layers-1-Index);
                var Size=Depth*2f;
                Renderer.SetObjectTransform(Index,Matrix4x4.CreateScale(Size,1f,Size)*Matrix4x4.CreateRotationX(-MathF.PI/2f)*Matrix4x4.CreateTranslation(0f,0f,Depth));
            }
            Renderer.RenderFrame();
            var Milliseconds=Renderer.ReadMainPassGpuMillisecondsForTesting();
            if(Frame>=Warmup){
                Times[Frame-Warmup]=Milliseconds;
            }
        }
        return (Times,Renderer.AdapterDescription);
    }
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){
            Directory=Directory.Parent;
        }
        return Directory?.FullName??throw new InvalidOperationException("The repository root was not found.");
    }
}
