using System.Text;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Backend;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
[Collection(D3D12GpuCollection.Name)]
public sealed unsafe class RenderBackendDiagnosticsTests {
    [Theory]
    [InlineData(unchecked((int)0x887A0005),RenderRecoveryReason.DeviceRemoved)]
    [InlineData(unchecked((int)0x887A0006),RenderRecoveryReason.DeviceRemoved)]
    [InlineData(unchecked((int)0x887A0007),RenderRecoveryReason.DeviceReset)]
    [InlineData(unchecked((int)0x80004005),null)]
    [InlineData(unchecked((int)0x8007000E),null)]
    public void NativeFailuresClassifyRecoveryBeforeCrossingTheServerBoundary(int Code,RenderRecoveryReason?Expected){
        var Failure=Assert.Throws<D3D12Exception>(()=>D3D12Diagnostics.EnsureSuccess(new HRESULT(Code),"NativeCall","testing classification","subject"));
        var Portable=Assert.IsAssignableFrom<RenderBackendFailureException>(Failure);
        Assert.Equal(Expected,Portable.RecoveryReason);
        Assert.Equal(Code,Portable.NativeCode);
        Assert.Equal(Code,Failure.HResult);
        Assert.Equal("NativeCall",Portable.Operation);
        Assert.Equal(Portable.Operation,Failure.Api);
        Assert.Equal($"NativeCall failed while testing classification. HRESULT=0x{Code:X8} ({Code}) [subject]",Failure.Message);
    }
    [Fact]
    public void BackendArtifactPayloadKeepsTheCanonicalArtifactBytes(){
        var Path=System.IO.Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
        Assert.True(ShaderArtifactRuntime.TryLoad(Path,CSharpGameEngine.ShaderArtifacts.ShaderArtifactTargetKind.Stage,out var Package,out var Error),Error);
        var Native=Assert.IsType<ShaderArtifactPackage>(Package);
        IStageShaderArtifact Portable=Native;
        Assert.Equal(File.ReadAllBytes(Path),Portable.Serialized.ToArray());
        Assert.Same(Portable,Native);
    }
    [WindowsOnlyFact]
    public void InfoQueueMessagesKeepSeverityIdentityAndCountsWithoutDuplicates(){
        using var Device=GraphicsDevice.Create(new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true });
        Assert.True(Device.DebugLayerEnabled);
        ID3D12InfoQueue*Queue;
        Assert.Equal(0,Device.Device->QueryInterface(__uuidof<ID3D12InfoQueue>(),(void**)&Queue).Value);
        var StorageFilterPushed=false;
        var RetrievalFilterPushed=false;
        try {
            // The test must admit all five injected severities instead of inheriting device filters.
            var Storage=Queue->PushEmptyStorageFilter();
            Assert.False(Storage.FAILED);
            StorageFilterPushed=true;
            var Retrieval=Queue->PushEmptyRetrievalFilter();
            Assert.False(Retrieval.FAILED);
            RetrievalFilterPushed=true;
            Device.PollDebugMessages();
            var InitialErrors=Device.ErrorOrWorseMessageCount;
            (D3D12_MESSAGE_SEVERITY Native,RenderDeviceMessageSeverity Portable)[]Severities=[
                (D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_CORRUPTION,RenderDeviceMessageSeverity.Corruption),
                (D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_ERROR,RenderDeviceMessageSeverity.Error),
                (D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_WARNING,RenderDeviceMessageSeverity.Warning),
                (D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_INFO,RenderDeviceMessageSeverity.Information),
                (D3D12_MESSAGE_SEVERITY.D3D12_MESSAGE_SEVERITY_MESSAGE,RenderDeviceMessageSeverity.Message),
            ];
            foreach(var Severity in Severities){
                var Marker=$"M0120 portable diagnostics {Severity.Portable}";
                fixed(byte*Text=Encoding.UTF8.GetBytes(Marker+'\0')){
                    var Result=Queue->AddApplicationMessage(Severity.Native,(sbyte*)Text);
                    Assert.False(Result.FAILED,$"{Severity.Native}: HRESULT=0x{Result.Value:X8}, stored={Queue->GetNumStoredMessages()}");
                }
                Device.PollDebugMessages();
                var Native=Assert.Single(Device.CollectedMessages,Message=>Message.Description.Contains(Marker,StringComparison.Ordinal));
                var Portable=Assert.Single(Device.RenderMessages,Message=>Message.Description.Contains(Marker,StringComparison.Ordinal));
                Assert.Equal(Severity.Portable,Portable.Severity);
                Assert.Equal(Native.IsErrorOrWorse,Portable.IsErrorOrWorse);
                Assert.Equal(Native.Category.ToString(),Portable.Category);
                Assert.Equal((int)Native.Id,Portable.Code);
                Assert.Equal(Native.Id.ToString(),Portable.Identifier);
                Assert.Equal(Native.Description,Portable.Description);
            }
            Assert.Equal(InitialErrors+2,Device.ErrorOrWorseMessageCount);
            Assert.Equal(Device.CollectedMessages.Count,Device.RenderMessages.Count);
            var Count=Device.RenderMessages.Count;
            Device.PollDebugMessages();
            Assert.Equal(Count,Device.RenderMessages.Count);
            Assert.Equal(0ul,Queue->GetNumStoredMessages());
        } finally {
            if(RetrievalFilterPushed){ Queue->PopRetrievalFilter(); }
            if(StorageFilterPushed){ Queue->PopStorageFilter(); }
            _=Queue->Release();
        }
    }
}
