using CSharpGameEngine.Graphics.D3D12;
namespace CSharpGameEngine.Windows.Tests.Graphics;
public sealed class GraphicsDeviceOptionsResolverTests {
    [Theory]
    [InlineData("warp",true)]
    [InlineData("hardware",false)]
    [InlineData("default",false)]
    public void FromEnvironment_ResolvesAdapter(string Value,bool ExpectedWarp){
        var Old=Environment.GetEnvironmentVariable(GraphicsDeviceOptionsResolver.AdapterEnvironmentVariable);
        try {
            Environment.SetEnvironmentVariable(GraphicsDeviceOptionsResolver.AdapterEnvironmentVariable,Value);
            var Options=GraphicsDeviceOptionsResolver.FromEnvironment();
            Assert.Equal(ExpectedWarp,Options.UseWarp);
        } finally { Environment.SetEnvironmentVariable(GraphicsDeviceOptionsResolver.AdapterEnvironmentVariable,Old); }
    }
    [Fact]
    public void FromEnvironment_RejectsUnknownValues(){
        var Old=Environment.GetEnvironmentVariable(GraphicsDeviceOptionsResolver.AdapterEnvironmentVariable);
        try {
            Environment.SetEnvironmentVariable(GraphicsDeviceOptionsResolver.AdapterEnvironmentVariable,"unknown");
            Assert.Throws<InvalidOperationException>(GraphicsDeviceOptionsResolver.FromEnvironment);
        } finally { Environment.SetEnvironmentVariable(GraphicsDeviceOptionsResolver.AdapterEnvironmentVariable,Old); }
    }
}
