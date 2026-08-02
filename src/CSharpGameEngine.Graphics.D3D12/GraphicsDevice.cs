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
    private IDXGIAdapter1* _adapter;
    private ID3D12Device* _device;
    private ID3D12CommandQueue* _commandQueue;
    private ID3D12InfoQueue* _infoQueue;
    private bool _disposed;

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

    public static GraphicsDevice Create(GraphicsDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        GraphicsDevice result = new();
        bool completed = false;
        try
        {
            if (options.EnableDebugLayer)
            {
                result.EnableDebugLayer(options.EnableGpuBasedValidation);
            }

            uint factoryFlags = options.EnableDebugLayer ? DXGI_CREATE_FACTORY_DEBUG : 0u;

            IDXGIFactory4* factory;
            D3D12Diagnostics.EnsureSuccess(
                CreateDXGIFactory2(factoryFlags, __uuidof<IDXGIFactory4>(), (void**)&factory),
                "CreateDXGIFactory2", "creating the DXGI factory");
            result._factory = factory;

            result._adapter = SelectAdapter(factory, options.UseWarp, out bool isWarp);
            result.IsWarp = isWarp;
            result.AdapterDescription = ReadAdapterDescription(result._adapter);

            ID3D12Device* device;
            D3D12Diagnostics.EnsureSuccess(
                D3D12CreateDevice((IUnknown*)result._adapter, RequiredFeatureLevel, __uuidof<ID3D12Device>(), (void**)&device),
                "D3D12CreateDevice", "creating the D3D12 device", result.AdapterDescription);
            result._device = device;

            if (options.EnableDebugLayer)
            {
                result.TryCreateInfoQueue();
            }

            D3D12_COMMAND_QUEUE_DESC queueDesc = new()
            {
                Type = D3D12_COMMAND_LIST_TYPE_DIRECT,
            };

            ID3D12CommandQueue* queue;
            D3D12Diagnostics.EnsureSuccess(
                device->CreateCommandQueue(&queueDesc, __uuidof<ID3D12CommandQueue>(), (void**)&queue),
                "ID3D12Device::CreateCommandQueue", "creating the direct command queue");
            result._commandQueue = queue;

            completed = true;
            return result;
        }
        finally
        {
            if (!completed)
            {
                result.Dispose();
            }
        }
    }

    /// <summary>Pulls any newly-recorded debug-layer messages into <see cref="CollectedMessages"/>. Cheap no-op when the info queue is unavailable or empty.</summary>
    public void PollDebugMessages()
    {
        if (_infoQueue == null)
        {
            return;
        }

        ulong stored = _infoQueue->GetNumStoredMessages();
        for (ulong i = 0; i < stored; i++)
        {
            nuint messageLength;
            HRESULT hr = _infoQueue->GetMessage(i, null, &messageLength);
            if (hr.FAILED || messageLength == 0)
            {
                continue;
            }

            byte* buffer = (byte*)NativeMemory.Alloc(messageLength);
            try
            {
                D3D12_MESSAGE* message = (D3D12_MESSAGE*)buffer;
                hr = _infoQueue->GetMessage(i, message, &messageLength);
                if (hr.FAILED)
                {
                    continue;
                }

                string description = message->pDescription != null
                    ? new string(message->pDescription)
                    : string.Empty;

                D3D12DebugMessage record = new(message->Severity, message->Category, message->ID, description);
                _collectedMessages.Add(record);
                if (record.IsErrorOrWorse)
                {
                    ErrorOrWorseMessageCount++;
                }
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }

        _infoQueue->ClearStoredMessages();
    }

    private void EnableDebugLayer(bool enableGpuBasedValidation)
    {
        ID3D12Debug1* debugController;
        HRESULT hr = D3D12GetDebugInterface(__uuidof<ID3D12Debug1>(), (void**)&debugController);
        if (hr.FAILED)
        {
            Console.Error.WriteLine($"[CSharpGameEngine] D3D12GetDebugInterface failed (HRESULT=0x{hr.Value:X8}); the D3D12 debug layer requires the 'Graphics Tools' optional Windows feature. Continuing without it.");
            return;
        }

        debugController->EnableDebugLayer();
        DebugLayerEnabled = true;

        if (enableGpuBasedValidation)
        {
            debugController->SetEnableGPUBasedValidation(TRUE);
            GpuBasedValidationEnabled = true;
        }

        _ = debugController->Release();
    }

    private void TryCreateInfoQueue()
    {
        ID3D12InfoQueue* infoQueue;
        HRESULT hr = _device->QueryInterface(__uuidof<ID3D12InfoQueue>(), (void**)&infoQueue);
        if (hr.SUCCEEDED)
        {
            _infoQueue = infoQueue;
        }
    }

    private static IDXGIAdapter1* SelectAdapter(IDXGIFactory4* factory, bool useWarp, out bool isWarp)
    {
        if (useWarp)
        {
            IDXGIAdapter1* warpAdapter;
            D3D12Diagnostics.EnsureSuccess(
                factory->EnumWarpAdapter(__uuidof<IDXGIAdapter1>(), (void**)&warpAdapter),
                "IDXGIFactory4::EnumWarpAdapter", "selecting the WARP software adapter");
            isWarp = true;
            return warpAdapter;
        }

        isWarp = false;

        for (uint index = 0; ; index++)
        {
            IDXGIAdapter1* adapter;
            HRESULT hr = factory->EnumAdapters1(index, &adapter);
            if (hr.Value == DXGI_ERROR_NOT_FOUND)
            {
                break;
            }

            D3D12Diagnostics.EnsureSuccess(hr, "IDXGIFactory1::EnumAdapters1", "enumerating DXGI adapters");

            DXGI_ADAPTER_DESC1 desc;
            _ = adapter->GetDesc1(&desc);

            if (((DXGI_ADAPTER_FLAG)desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
            {
                _ = adapter->Release();
                continue;
            }

            HRESULT supportHr = D3D12CreateDevice((IUnknown*)adapter, RequiredFeatureLevel, __uuidof<ID3D12Device>(), null);
            if (supportHr.SUCCEEDED)
            {
                return adapter;
            }

            _ = adapter->Release();
        }

        throw new InvalidOperationException(
            $"No hardware adapter supporting Direct3D 12 (feature level {RequiredFeatureLevel}) was found. " +
            "Pass --warp to explicitly use the WARP software adapter instead.");
    }

    private static string ReadAdapterDescription(IDXGIAdapter1* adapter)
    {
        DXGI_ADAPTER_DESC1 desc;
        _ = adapter->GetDesc1(&desc);

        Span<char> nameChars = desc.Description;
        int nullIndex = nameChars.IndexOf('\0');
        return new string(nullIndex >= 0 ? nameChars[..nullIndex] : nameChars);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_commandQueue != null)
        {
            _ = _commandQueue->Release();
            _commandQueue = null;
        }

        if (_infoQueue != null)
        {
            _ = _infoQueue->Release();
            _infoQueue = null;
        }

        if (_device != null)
        {
            _ = _device->Release();
            _device = null;
        }

        if (_adapter != null)
        {
            _ = _adapter->Release();
            _adapter = null;
        }

        if (_factory != null)
        {
            _ = _factory->Release();
            _factory = null;
        }
    }
}
