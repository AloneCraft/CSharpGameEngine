using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_MESSAGE_SEVERITY;
using static TerraFX.Interop.DirectX.D3D_FEATURE_LEVEL;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI;
using static TerraFX.Interop.DirectX.DXGI_ADAPTER_FLAG;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>
/// Owns the DXGI factory/adapter, the D3D12 device, the direct command queue, and (when available)
/// the debug-layer info queue used to collect validation messages.
/// </summary>
public sealed unsafe class GraphicsDevice : IDisposable
{
    public const D3D_FEATURE_LEVEL RequiredFeatureLevel = D3D_FEATURE_LEVEL_11_0;

    private IDXGIFactory4* _factory;
    private IDXGIAdapter1* Adapter;
    private ID3D12Device* _device;
    private ID3D12CommandQueue* _commandQueue;
    private ID3D12InfoQueue* InfoQueue;
    private bool Disposed;

    private readonly List<D3D12DebugMessage> _collectedMessages = [];

    private GraphicsDevice()
    {
    }

    public bool IsWarp { get; private set; }

    public bool DebugLayerEnabled { get; private set; }

    public bool GpuBasedValidationEnabled { get; private set; }

    public string AdapterDescription { get; private set; } = string.Empty;

    public IReadOnlyList<D3D12DebugMessage> CollectedMessages => _collectedMessages;

    public int ErrorOrWorseMessageCount { get; private set; }

    public IDXGIFactory4* Factory => _factory;

    public ID3D12Device* Device => _device;

    public ID3D12CommandQueue* CommandQueue => _commandQueue;

    public static GraphicsDevice Create(GraphicsDeviceOptions Options)
    {
        ArgumentNullException.ThrowIfNull(Options);

        GraphicsDevice Result = new();
        bool Completed = false;
        try
        {
            if (Options.EnableDebugLayer)
            {
                Result.EnableDebugLayer(Options.EnableGpuBasedValidation);
            }

            uint FactoryFlags = Options.EnableDebugLayer ? DXGI_CREATE_FACTORY_DEBUG : 0u;

            IDXGIFactory4* factory;
            D3D12Diagnostics.EnsureSuccess(
                CreateDXGIFactory2(FactoryFlags, __uuidof<IDXGIFactory4>(), (void**)&factory),
                "CreateDXGIFactory2", "creating the DXGI factory");
            Result._factory = factory;

            Result.Adapter = SelectAdapter(factory, Options.UseWarp, out bool isWarp);
            Result.IsWarp = isWarp;
            Result.AdapterDescription = ReadAdapterDescription(Result.Adapter);

            ID3D12Device* device;
            D3D12Diagnostics.EnsureSuccess(
                D3D12CreateDevice((IUnknown*)Result.Adapter, RequiredFeatureLevel, __uuidof<ID3D12Device>(), (void**)&device),
                "D3D12CreateDevice", "creating the D3D12 device", Result.AdapterDescription);
            Result._device = device;

            if (Options.EnableDebugLayer)
            {
                Result.TryCreateInfoQueue();
            }

            D3D12_COMMAND_QUEUE_DESC QueueDesc = new()
            {
                Type = D3D12_COMMAND_LIST_TYPE_DIRECT,
            };

            ID3D12CommandQueue* queue;
            D3D12Diagnostics.EnsureSuccess(
                device->CreateCommandQueue(&QueueDesc, __uuidof<ID3D12CommandQueue>(), (void**)&queue),
                "ID3D12Device::CreateCommandQueue", "creating the direct command queue");
            Result._commandQueue = queue;

            Completed = true;
            return Result;
        }
        finally
        {
            if (!Completed)
            {
                Result.Dispose();
            }
        }
    }

    /// <summary>Pulls any newly-recorded debug-layer messages into <see cref="CollectedMessages"/>. Cheap no-op when the info queue is unavailable or empty.</summary>
    public void PollDebugMessages()
    {
        if (InfoQueue == null)
        {
            return;
        }

        ulong Stored = InfoQueue->GetNumStoredMessages();
        for (ulong i = 0; i < Stored; i++)
        {
            nuint MessageLength;
            HRESULT Hr = InfoQueue->GetMessage(i, null, &MessageLength);
            if (Hr.FAILED || MessageLength == 0)
            {
                continue;
            }

            byte* buffer = (byte*)NativeMemory.Alloc(MessageLength);
            try
            {
                D3D12_MESSAGE* Message = (D3D12_MESSAGE*)buffer;
                Hr = InfoQueue->GetMessage(i, Message, &MessageLength);
                if (Hr.FAILED)
                {
                    continue;
                }

                string Description = Message->pDescription != null
                    ? new string(Message->pDescription)
                    : string.Empty;

                D3D12DebugMessage Record = new(Message->Severity, Message->Category, Message->ID, Description);
                _collectedMessages.Add(Record);
                if (Record.IsErrorOrWorse)
                {
                    ErrorOrWorseMessageCount++;
                }
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }

        InfoQueue->ClearStoredMessages();
    }

    private void EnableDebugLayer(bool EnableGpuBasedValidation)
    {
        ID3D12Debug1* DebugController;
        HRESULT Hr = D3D12GetDebugInterface(__uuidof<ID3D12Debug1>(), (void**)&DebugController);
        if (Hr.FAILED)
        {
            Console.Error.WriteLine($"[CSharpGameEngine] D3D12GetDebugInterface failed (HRESULT=0x{Hr.Value:X8}); the D3D12 debug layer requires the 'Graphics Tools' optional Windows feature. Continuing without it.");
            return;
        }

        DebugController->EnableDebugLayer();
        DebugLayerEnabled = true;

        if (EnableGpuBasedValidation)
        {
            DebugController->SetEnableGPUBasedValidation(TRUE);
            GpuBasedValidationEnabled = true;
        }

        _ = DebugController->Release();
    }

    private void TryCreateInfoQueue()
    {
        ID3D12InfoQueue* InfoQueue;
        HRESULT Hr = _device->QueryInterface(__uuidof<ID3D12InfoQueue>(), (void**)&InfoQueue);
        if (Hr.SUCCEEDED)
        {
            this.InfoQueue = InfoQueue;
        }
    }

    private static IDXGIAdapter1* SelectAdapter(IDXGIFactory4* factory, bool UseWarp, out bool isWarp)
    {
        if (UseWarp)
        {
            IDXGIAdapter1* WarpAdapter;
            D3D12Diagnostics.EnsureSuccess(
                factory->EnumWarpAdapter(__uuidof<IDXGIAdapter1>(), (void**)&WarpAdapter),
                "IDXGIFactory4::EnumWarpAdapter", "selecting the WARP software adapter");
            isWarp = true;
            return WarpAdapter;
        }

        isWarp = false;

        for (uint index = 0; ; index++)
        {
            IDXGIAdapter1* Adapter;
            HRESULT Hr = factory->EnumAdapters1(index, &Adapter);
            if (Hr.Value == DXGI_ERROR_NOT_FOUND)
            {
                break;
            }

            D3D12Diagnostics.EnsureSuccess(Hr, "IDXGIFactory1::EnumAdapters1", "enumerating DXGI adapters");

            DXGI_ADAPTER_DESC1 Desc;
            _ = Adapter->GetDesc1(&Desc);

            if (((DXGI_ADAPTER_FLAG)Desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
            {
                _ = Adapter->Release();
                continue;
            }

            HRESULT SupportHr = D3D12CreateDevice((IUnknown*)Adapter, RequiredFeatureLevel, __uuidof<ID3D12Device>(), null);
            if (SupportHr.SUCCEEDED)
            {
                return Adapter;
            }

            _ = Adapter->Release();
        }

        throw new InvalidOperationException(
            $"No hardware adapter supporting Direct3D 12 (feature level {RequiredFeatureLevel}) was found. " +
            "Pass --warp to explicitly use the WARP software adapter instead.");
    }

    private static string ReadAdapterDescription(IDXGIAdapter1* Adapter)
    {
        DXGI_ADAPTER_DESC1 Desc;
        _ = Adapter->GetDesc1(&Desc);

        Span<char> NameChars = Desc.Description;
        int NullIndex = NameChars.IndexOf('\0');
        return new string(NullIndex >= 0 ? NameChars[..NullIndex] : NameChars);
    }

    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;

        if (_commandQueue != null)
        {
            _ = _commandQueue->Release();
            _commandQueue = null;
        }

        if (InfoQueue != null)
        {
            _ = InfoQueue->Release();
            InfoQueue = null;
        }

        if (_device != null)
        {
            _ = _device->Release();
            _device = null;
        }

        if (Adapter != null)
        {
            _ = Adapter->Release();
            Adapter = null;
        }

        if (_factory != null)
        {
            _ = _factory->Release();
            _factory = null;
        }
    }
}
