using Xunit;
using System.Numerics;
using CSharpGameEngine.Compiler.Hlsl;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 画面上の大きさの式は、CPU（<see cref="ScreenSizeMetric"/>）とシェーダー（<see cref="ScreenSizeMetric.Hlsl"/>）で同じ結果になる（M0129 単位 6、ADR-0100 決定 1）。
/// 透視と正射影、近い・遠い・カメラの中の球を混ぜた例を WARP で計算し、1 件ずつ比べる。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class ScreenSizeMetricGpuTests {
    private const int Stride=8;
    /// <summary>
    /// 1 件は 8 個の float: 式の 4 つの値、球の中心（カメラから見た相対の位置）、半径。出力は同じ位置に、球の画素数と、半径を大きさ・中心の z を距離にした画素数。
    /// </summary>
    private const string Probe="""
StructuredBuffer<float> Input : register(t0);
RWStructuredBuffer<float> Output : register(u0);
[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint count;
    uint stride;
    Input.GetDimensions(count, stride);
    uint first = id.x * 8;
    if (first + 7 >= count)
    {
        return;
    }
    float4 metric = float4(Input[first], Input[first + 1], Input[first + 2], Input[first + 3]);
    float3 centre = float3(Input[first + 4], Input[first + 5], Input[first + 6]);
    float radius = Input[first + 7];
    Output[first] = ScreenSizeSphereProjectedPixels(centre, radius, metric);
    Output[first + 1] = ScreenSizeProjectedPixels(radius, centre.z, metric);
    for (uint index = 2; index < 8; index++)
    {
        Output[first + index] = 0.0;
    }
}
""";
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_ShaderAndCpuAgreeOnEveryCase(){
        var Metrics=new[]{
            ScreenSizeMetric.Perspective(MathF.PI/2f,1080,0.1f),
            ScreenSizeMetric.Perspective(0.3f,720,0.01f),
            ScreenSizeMetric.Perspective(2.8f,4320,1f),
            ScreenSizeMetric.Orthographic(40f,1080),
        };
        var Random=new Random(6);
        List<(ScreenSizeMetric Metric,Vector3 Centre,float Radius)>Cases=[];
        foreach(var Metric in Metrics){
            // 決まった例: カメラの中、表面がちょうど最小の距離、遠い所、とても遠い所。
            Cases.Add((Metric,Vector3.Zero,1f));
            Cases.Add((Metric,new Vector3(0f,0f,1.1f),1f));
            Cases.Add((Metric,new Vector3(3f,-4f,120f),0.25f));
            Cases.Add((Metric,new Vector3(-2e5f,1e5f,3e6f),40f));
            for(var Index=0;Index<60;Index++){
                var Scale=MathF.Pow(10f,(float)Random.NextDouble()*6f-1f);
                Cases.Add((Metric,new Vector3((float)Random.NextDouble()-0.5f,(float)Random.NextDouble()-0.5f,(float)Random.NextDouble())*Scale,(float)Random.NextDouble()*Scale*0.1f+0.001f));
            }
        }
        var Input=new float[Cases.Count*Stride];
        for(var Index=0;Index<Cases.Count;Index++){
            var (Metric,Centre,Radius)=Cases[Index];
            var Parameters=Metric.ShaderParameters;
            new[]{ Parameters.X,Parameters.Y,Parameters.Z,Parameters.W,Centre.X,Centre.Y,Centre.Z,Radius }.CopyTo(Input,Index*Stride);
        }
        var Result=D3D12ComputeReadback.Execute(Input,Compile(),UseWarp:true);
        Assert.Equal(0,Result.ErrorOrWorseMessageCount);
        for(var Index=0;Index<Cases.Count;Index++){
            var (Metric,Centre,Radius)=Cases[Index];
            AssertClose(Metric.SphereProjectedPixels(Centre,Radius),Result.Values[Index*Stride],$"case {Index} sphere");
            AssertClose(Metric.ProjectedPixels(Radius,Centre.Z),Result.Values[Index*Stride+1],$"case {Index} size");
        }
    }
    /// <summary>
    /// 平方根と割り算は、GPU では最後の桁が 1 つずれてよい（D3D の精度の決まり）。だから完全一致ではなく、相対の差が 1e-6 以内であることを約束にする。
    /// 画素数を段の切り替えに使うときは、この差よりずっと広いヒステリシス（1.25 倍）を置く。
    /// </summary>
    private static void AssertClose(float Cpu,float Gpu,string What){
        var Tolerance=MathF.Max(MathF.Abs(Cpu),1e-30f)*1e-6f;
        Assert.True(MathF.Abs(Cpu-Gpu)<=Tolerance,$"{What}: CPU {Cpu:R} and GPU {Gpu:R} differ by more than {Tolerance:R}");
    }
    private static byte[]Compile(){
        var Directory=Path.Combine(AppContext.BaseDirectory,"ScreenSizeMetricProbe");
        System.IO.Directory.CreateDirectory(Directory);
        var File=Path.Combine(Directory,"Probe.hlsl");
        System.IO.File.WriteAllText(File,ScreenSizeMetric.Hlsl+Probe);
        var Result=HlslCompiler.Compile(new HlslCompileRequest { FilePath=File,EntryPoint="CSMain",TargetProfile="cs_6_0" });
        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        return Result.Bytecode;
    }
}
