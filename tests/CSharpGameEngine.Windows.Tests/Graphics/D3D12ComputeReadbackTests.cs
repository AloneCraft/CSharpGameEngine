using CSharpGameEngine.Graphics.D3D12;

namespace CSharpGameEngine.Windows.Tests.Graphics;

[Collection(CSharpGameEngine.Graphics.D3D12.Tests.D3D12GpuCollection.Name)]
public sealed class D3D12ComputeReadbackTests {
    [Fact]
    public void WarpComputeArtifact_MatchesIndependentCpuVector(){
        float[]Input=Enumerable.Range(0,257)
            .Select(static Index=>(Index-128)*0.25f)
            .ToArray();
        string Artifact=Path.Combine(
            AppContext.BaseDirectory,"Shaders","ComputeVector.cgeshaderartifact");

        D3D12ComputeReadbackResult Result=
            D3D12ComputeReadback.Execute(Input,Artifact,UseWarp:true);

        Assert.Contains("Microsoft Basic Render Driver",Result.AdapterDescription,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0,Result.ErrorOrWorseMessageCount);
        Assert.Equal(Input.Length,Result.Values.Length);
        for(int Index=0;Index<Input.Length;Index++){
            Assert.Equal((Input[Index]*2f)+1f,Result.Values[Index]);
        }
    }

    [Fact]
    public void ComputeBackend_RejectsWrongArtifactBeforeDeviceCreation(){
        string Artifact=Path.Combine(
            AppContext.BaseDirectory,"Shaders","Triangle.cgeshaderartifact");

        InvalidDataException Exception=Assert.Throws<InvalidDataException>(
            ()=>D3D12ComputeReadback.Execute([1f],Artifact,UseWarp:true));

        Assert.Contains("target",Exception.Message,StringComparison.OrdinalIgnoreCase);
    }
}
