using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.GWLP;
using static TerraFX.Interop.Windows.PM;
using static TerraFX.Interop.Windows.SW;
using static TerraFX.Interop.Windows.WM;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Interop.Windows.WS;

namespace CSharpGameEngine.Platform.Windows;

/// <summary>
/// Owns exactly one raw Win32 <c>HWND</c>: creates it, pumps its message queue, and destroys it.
/// Not thread-safe; must be created, pumped, and disposed from the same thread.
/// </summary>
public sealed unsafe class Win32Window : IDisposable
{
    private const string ClassName = "CSharpGameEngine.Win32Window";
    private static readonly char* IDC_ARROW = (char*)0x7F00;

    private static readonly object s_registrationLock = new();
    private static bool s_classRegistered;

    private GCHandle _selfHandle;
    private HWND _hwnd;
    private bool _destroyed;
    private bool _disposed;

    public event Action<int, int>? Resized;

    public Win32Window(Win32WindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        EnsureDpiAwareness();
        EnsureClassRegistered();

        _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);

        const uint style = WS_OVERLAPPEDWINDOW;

        RECT windowRect = new() { left = 0, top = 0, right = options.Width, bottom = options.Height };
        _ = AdjustWindowRect(&windowRect, style, FALSE);
        int windowWidth = windowRect.right - windowRect.left;
        int windowHeight = windowRect.bottom - windowRect.top;

        HWND hwnd;
        fixed (char* className = ClassName)
        fixed (char* title = options.Title)
        {
            hwnd = CreateWindowExW(
                0,
                className,
                title,
                style,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                windowWidth,
                windowHeight,
                HWND.NULL,
                HMENU.NULL,
                GetModuleHandleForCurrentProcess(),
                (void*)GCHandle.ToIntPtr(_selfHandle));
        }

        if (hwnd == HWND.NULL)
        {
            _selfHandle.Free();
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateWindowExW failed with Win32 error 0x{error:X8}.");
        }

        _hwnd = hwnd;
        RefreshClientSize();

        if (options.Visible)
        {
            _ = ShowWindow(hwnd, SW_SHOW);
            _ = UpdateWindow(hwnd);
        }
    }

    public nint Handle => (nint)_hwnd.Value;

    public int ClientWidth { get; private set; }

    public int ClientHeight { get; private set; }

    public bool IsClosed { get; private set; }

    public bool IsMinimized { get; private set; }

    public void SetTitle(string title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        fixed (char* pTitle = title)
        {
            _ = SetWindowTextW(_hwnd, pTitle);
        }
    }

    /// <summary>Processes all messages currently queued for this thread without blocking.</summary>
    public void PumpEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        MSG msg;
        while (PeekMessageW(&msg, HWND.NULL, 0, 0, PM_REMOVE))
        {
            _ = TranslateMessage(&msg);
            _ = DispatchMessageW(&msg);
        }
    }

    private void RefreshClientSize()
    {
        RECT clientRect;
        if (GetClientRect(_hwnd, &clientRect))
        {
            ClientWidth = clientRect.right - clientRect.left;
            ClientHeight = clientRect.bottom - clientRect.top;
        }
    }

    private LRESULT HandleMessage(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case WM_CLOSE:
                _ = DestroyWindow(hwnd);
                return 0;

            case WM_DESTROY:
                _destroyed = true;
                IsClosed = true;
                return 0;

            case WM_SIZE:
            {
                IsMinimized = (uint)wParam.Value.ToUInt64() == SIZE_MINIMIZED;

                long lp = lParam.Value.ToInt64();
                int width = (int)(lp & 0xFFFF);
                int height = (int)((lp >> 16) & 0xFFFF);

                ClientWidth = width;
                ClientHeight = height;

                if (!IsMinimized)
                {
                    Resized?.Invoke(width, height);
                }

                return 0;
            }

            case WM_NCDESTROY:
            {
                LRESULT result = DefWindowProcW(hwnd, msg, wParam, lParam);
                if (_selfHandle.IsAllocated)
                {
                    _selfHandle.Free();
                }
                return result;
            }

            default:
                return DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_destroyed && _hwnd != HWND.NULL)
        {
            _ = DestroyWindow(_hwnd);
        }

        _hwnd = HWND.NULL;
    }

    private static void EnsureDpiAwareness()
    {
        const int PerMonitorAwareV2 = -4;
        _ = SetProcessDpiAwarenessContext((DPI_AWARENESS_CONTEXT)(nint)PerMonitorAwareV2);
    }

    private static void EnsureClassRegistered()
    {
        if (s_classRegistered)
        {
            return;
        }

        lock (s_registrationLock)
        {
            if (s_classRegistered)
            {
                return;
            }

            fixed (char* className = ClassName)
            {
                WNDCLASSEXW wc = new()
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    style = 0,
                    lpfnWndProc = &WndProc,
                    hInstance = GetModuleHandleForCurrentProcess(),
                    hCursor = LoadCursorW(HINSTANCE.NULL, IDC_ARROW),
                    lpszClassName = className,
                };

                if (RegisterClassExW(&wc) == 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"RegisterClassExW failed with Win32 error 0x{error:X8}.");
                }
            }

            s_classRegistered = true;
        }
    }

    private static HINSTANCE GetModuleHandleForCurrentProcess() => (HINSTANCE)(void*)GetModuleHandleW(null);

    [UnmanagedCallersOnly]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == WM_NCCREATE)
        {
            CREATESTRUCTW* createStruct = (CREATESTRUCTW*)lParam.Value;
            nint selfHandle = (nint)createStruct->lpCreateParams;
            _ = SetWindowLongPtrW(hwnd, GWLP_USERDATA, selfHandle);
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        nint userData = GetWindowLongPtrW(hwnd, GWLP_USERDATA);
        if (userData != 0)
        {
            GCHandle handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is Win32Window window)
            {
                try
                {
                    return window.HandleMessage(hwnd, msg, wParam, lParam);
                }
                catch
                {
                    return 0;
                }
            }
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
