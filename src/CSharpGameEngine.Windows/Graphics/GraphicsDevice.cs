using CSharpGameEngine.Rendering.Backend;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_FEATURE_LEVEL;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_MESSAGE_SEVERITY;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI;
using static TerraFX.Interop.DirectX.DXGI_ADAPTER_FLAG;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>
/// Owns the DXGI factory/adapter, the D3D12 device, the direct command queue, and (when available)
/// the debug-layer info queue used to collect validation messages.
/// </summary>
public sealed unsafe class GraphicsDevice:IDisposable {
    public const D3D_FEATURE_LEVEL RequiredFeatureLevel=D3D_FEATURE_LEVEL_11_0;

    private IDXGIFactory4*FactoryHandle;
    private IDXGIAdapter1*Adapter;
    private ID3D12Device*DeviceHandle;
    private ID3D12CommandQueue*CommandQueueHandle;
    private ID3D12InfoQueue*InfoQueue;
    private bool Disposed;

    private readonly List<D3D12DebugMessage>CollectedMessagesValue=[];
    private readonly List<RenderDeviceMessage>RenderMessagesValue=[];

    private GraphicsDevice(){
    }

    public bool IsWarp { get;private set;}

    public bool DebugLayerEnabled { get;private set;}

    public bool GpuBasedValidationEnabled { get;private set;}

    public string AdapterDescription { get;private set;}=string.Empty;

    public IReadOnlyList<D3D12DebugMessage>CollectedMessages=> this.CollectedMessagesValue;
    public IReadOnlyList<RenderDeviceMessage>RenderMessages=>this.RenderMessagesValue;

    public int ErrorOrWorseMessageCount { get;private set;}

    public IDXGIFactory4*Factory=> this.FactoryHandle;

    public ID3D12Device*Device=> this.DeviceHandle;

    public ID3D12CommandQueue*CommandQueue=> this.CommandQueueHandle;

    /// <summary>For executable fault-injection tests only; invokes the real D3D12 removal path.</summary>
    internal void RemoveDeviceForTesting(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,19043)){
            throw new PlatformNotSupportedException(
                "D3D12 device-removal fault injection requires Windows build 19043 or later.");
        }
        ID3D12Device5*Device5;
        D3D12Diagnostics.EnsureSuccess(
            this.DeviceHandle->QueryInterface(
                __uuidof<ID3D12Device5>(),(void**)&Device5),
            "ID3D12Device::QueryInterface",
            "acquiring ID3D12Device5 for device-removal fault injection");
        try {
            Device5->RemoveDevice();
        } finally {
            _=Device5->Release();
        }
    }

    public static GraphicsDevice Create(GraphicsDeviceOptions Options){
        ArgumentNullException.ThrowIfNull(Options);

        GraphicsDevice Result=new();
        var Completed=false;
        try {
            if(Options.EnableDebugLayer){
                Result.EnableDebugLayer(Options.EnableGpuBasedValidation);
            }

            var FactoryFlags=Options.EnableDebugLayer?DXGI_CREATE_FACTORY_DEBUG:0u;

            IDXGIFactory4*Factory;
            D3D12Diagnostics.EnsureSuccess(
                CreateDXGIFactory2(FactoryFlags,__uuidof<IDXGIFactory4>(),(void**)&Factory),
                "CreateDXGIFactory2","creating the DXGI factory");
            Result.FactoryHandle=Factory;

            Result.Adapter=SelectAdapter(Factory,Options.UseWarp,out var IsWarp);
            Result.IsWarp=IsWarp;
            Result.AdapterDescription=ReadAdapterDescription(Result.Adapter);

            ID3D12Device*Device;
            D3D12Diagnostics.EnsureSuccess(
                D3D12CreateDevice((IUnknown*)Result.Adapter,RequiredFeatureLevel,__uuidof<ID3D12Device>(),(void**)&Device),
                "D3D12CreateDevice","creating the D3D12 device",Result.AdapterDescription);
            Result.DeviceHandle=Device;

            if(Options.EnableDebugLayer){
                Result.TryCreateInfoQueue();
            }

            D3D12_COMMAND_QUEUE_DESC QueueDesc=new(){
                Type=D3D12_COMMAND_LIST_TYPE_DIRECT,
            };

            ID3D12CommandQueue*Queue;
            D3D12Diagnostics.EnsureSuccess(
                Device->CreateCommandQueue(&QueueDesc,__uuidof<ID3D12CommandQueue>(),(void**)&Queue),
                "ID3D12Device::CreateCommandQueue","creating the direct command queue");
            Result.CommandQueueHandle=Queue;

            Completed=true;
            return Result;
        } finally {
            if(!Completed){
                Result.Dispose();
            }
        }
    }

    /// <summary>Pulls any newly-recorded debug-layer messages into <see cref="CollectedMessages"/>. Cheap no-op when the info queue is unavailable or empty.</summary>
    public void PollDebugMessages(){
        if(this.InfoQueue==null){
            return;
        }

        var Stored= this.InfoQueue->GetNumStoredMessages();
        for(ulong I=0;I<Stored;I++){
            nuint MessageLength;
            HRESULT Hr= this.InfoQueue->GetMessage(I,null,&MessageLength);
            if(Hr.FAILED||MessageLength==0){
                continue;
            }

            var Buffer=(byte*)NativeMemory.Alloc(MessageLength);
            try {
                D3D12_MESSAGE*Message=(D3D12_MESSAGE*)Buffer;
                Hr= this.InfoQueue->GetMessage(I,Message,&MessageLength);
                if(Hr.FAILED){
                    continue;
                }

                var Description=Message->pDescription!=null
                    ?new string(Message->pDescription)
                    :string.Empty;

                D3D12DebugMessage Record=new(Message->Severity,Message->Category,Message->ID,Description);
                this.CollectedMessagesValue.Add(Record);
                this.RenderMessagesValue.Add(Record.ToRenderDeviceMessage());
                if(Record.IsErrorOrWorse){
                    this.ErrorOrWorseMessageCount++;
                }
            } finally {
                NativeMemory.Free(Buffer);
            }
        }

        this.InfoQueue->ClearStoredMessages();
    }

    private void EnableDebugLayer(bool EnableGpuBasedValidation){
        ID3D12Debug1*DebugController;
        HRESULT Hr=D3D12GetDebugInterface(__uuidof<ID3D12Debug1>(),(void**)&DebugController);
        if(Hr.FAILED){
            Console.Error.WriteLine($"[CSharpGameEngine] D3D12GetDebugInterface failed (HRESULT=0x{Hr.Value:X8}); the D3D12 debug layer requires the 'Graphics Tools' optional Windows feature. Continuing without it.");
            return;
        }

        DebugController->EnableDebugLayer();
        this.DebugLayerEnabled=true;

        if(EnableGpuBasedValidation){
            DebugController->SetEnableGPUBasedValidation(TRUE);
            this.GpuBasedValidationEnabled=true;
        }

        _=DebugController->Release();
    }

    private void TryCreateInfoQueue(){
        ID3D12InfoQueue*InfoQueue;
        HRESULT Hr= this.Device->QueryInterface(__uuidof<ID3D12InfoQueue>(),(void**)&InfoQueue);
        if(Hr.SUCCEEDED){
            this.InfoQueue=InfoQueue;
        }
    }

    private static IDXGIAdapter1*SelectAdapter(IDXGIFactory4*Factory,bool UseWarp,out bool IsWarp){
        if(UseWarp){
            IDXGIAdapter1*WarpAdapter;
            D3D12Diagnostics.EnsureSuccess(
                Factory->EnumWarpAdapter(__uuidof<IDXGIAdapter1>(),(void**)&WarpAdapter),
                "IDXGIFactory4::EnumWarpAdapter","selecting the WARP software adapter");
            IsWarp=true;
            return WarpAdapter;
        }

        IsWarp=false;

        for(uint Index=0;;Index++){
            IDXGIAdapter1*Adapter;
            HRESULT Hr=Factory->EnumAdapters1(Index,&Adapter);
            if(Hr.Value==DXGI_ERROR_NOT_FOUND){
                break;
            }

            D3D12Diagnostics.EnsureSuccess(Hr,"IDXGIFactory1::EnumAdapters1","enumerating DXGI adapters");

            DXGI_ADAPTER_DESC1 Desc;
            _=Adapter->GetDesc1(&Desc);

            if(((DXGI_ADAPTER_FLAG)Desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE)!=0){
                _=Adapter->Release();
                continue;
            }

            HRESULT SupportHr=D3D12CreateDevice((IUnknown*)Adapter,RequiredFeatureLevel,__uuidof<ID3D12Device>(),null);
            if(SupportHr.SUCCEEDED){
                return Adapter;
            }

            _=Adapter->Release();
        }

        // A game reads the adapter from the environment only; --warp is the viewer's and the triangle sample's option.
        throw new InvalidOperationException(
            $"No hardware adapter supporting Direct3D 12 (feature level {RequiredFeatureLevel}) was found. " +
            $"Set {GraphicsDeviceOptionsResolver.AdapterEnvironmentVariable}=warp (or pass --warp to the viewer) to use the WARP software adapter instead.");
    }

    private static string ReadAdapterDescription(IDXGIAdapter1*Adapter){
        DXGI_ADAPTER_DESC1 Desc;
        _=Adapter->GetDesc1(&Desc);

        Span<char>NameChars=Desc.Description;
        var NullIndex=NameChars.IndexOf('\0');
        return new string(NullIndex>=0?NameChars[..NullIndex]:NameChars);
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;

        if(this.CommandQueueHandle!=null){
            _= this.CommandQueueHandle->Release();
            this.CommandQueueHandle=null;
        }

        if(this.InfoQueue!=null){
            _= this.InfoQueue->Release();
            this.InfoQueue=null;
        }

        if(this.DeviceHandle!=null){
            _= this.DeviceHandle->Release();
            this.DeviceHandle=null;
        }

        if(this.Adapter!=null){
            _= this.Adapter->Release();
            this.Adapter=null;
        }

        if(this.FactoryHandle!=null){
            _= this.FactoryHandle->Release();
            this.FactoryHandle=null;
        }
    }
}
