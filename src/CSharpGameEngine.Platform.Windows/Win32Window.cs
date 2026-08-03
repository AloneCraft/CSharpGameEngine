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

    private readonly bool[] _keyDown = new bool[256];
    private bool _mouseCaptured;
    private POINT _capturedCenterScreenPoint;

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

    /// <summary>True while the given virtual-key code (see <c>VK_*</c>) is held down. Cleared automatically on focus loss.</summary>
    public bool IsKeyDown(int virtualKeyCode)
    {
        return virtualKeyCode is >= 0 and < 256 && _keyDown[virtualKeyCode];
    }

    public bool IsMouseCaptured => _mouseCaptured;

    /// <summary>
    /// Hides the cursor and clips it to this window (mouse-look style capture), or releases it.
    /// Implemented by re-centering the cursor every <see cref="ConsumeMouseDelta"/> call rather
    /// than raw input (<c>WM_INPUT</c>) - simpler and precise enough for an orbit camera, at the
    /// cost of being bounded by the window's client area.
    /// </summary>
    public void SetMouseCaptured(bool captured)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (captured == _mouseCaptured)
        {
            return;
        }

        _mouseCaptured = captured;

        if (captured)
        {
            _ = ShowCursor(FALSE);
            ClipCursorToClientArea();
            RecenterCursor();
        }
        else
        {
            _ = ShowCursor(TRUE);
            _ = ClipCursor(null);
        }
    }

    /// <summary>
    /// Returns the cursor movement since the last call (0,0 when not captured), then re-centers
    /// the cursor so the next call measures only new movement. Call at most once per frame.
    /// </summary>
    public (float DeltaX, float DeltaY) ConsumeMouseDelta()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_mouseCaptured)
        {
            return (0f, 0f);
        }

        POINT current;
        if (!GetCursorPos(&current))
        {
            return (0f, 0f);
        }

        float deltaX = current.x - _capturedCenterScreenPoint.x;
        float deltaY = current.y - _capturedCenterScreenPoint.y;

        RecenterCursor();

        return (deltaX, deltaY);
    }

    private void RecenterCursor()
    {
        RECT clientRect;
        if (!GetClientRect(_hwnd, &clientRect))
        {
            return;
        }

        POINT center = new() { x = (clientRect.left + clientRect.right) / 2, y = (clientRect.top + clientRect.bottom) / 2 };
        _ = ClientToScreen(_hwnd, &center);
        _ = SetCursorPos(center.x, center.y);
        _capturedCenterScreenPoint = center;
    }

    private void ClipCursorToClientArea()
    {
        RECT clientRect;
        if (!GetClientRect(_hwnd, &clientRect))
        {
            return;
        }

        POINT topLeft = new() { x = clientRect.left, y = clientRect.top };
        POINT bottomRight = new() { x = clientRect.right, y = clientRect.bottom };
        _ = ClientToScreen(_hwnd, &topLeft);
        _ = ClientToScreen(_hwnd, &bottomRight);

        RECT screenRect = new() { left = topLeft.x, top = topLeft.y, right = bottomRight.x, bottom = bottomRight.y };
        _ = ClipCursor(&screenRect);
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

            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
            {
                int virtualKeyCode = (int)wParam.Value.ToUInt64();
                if (virtualKeyCode is >= 0 and < 256)
                {
                    _keyDown[virtualKeyCode] = true;
                }
                // SYSKEY must still reach DefWindowProcW (Alt+F4, the system menu, etc.); plain
                // WM_KEYDOWN is fully consumed since this is a game window with no default text
                // input/accelerator behavior to preserve.
                return msg == WM_SYSKEYDOWN ? DefWindowProcW(hwnd, msg, wParam, lParam) : 0;
            }

            case WM_KEYUP:
            case WM_SYSKEYUP:
            {
                int virtualKeyCode = (int)wParam.Value.ToUInt64();
                if (virtualKeyCode is >= 0 and < 256)
                {
                    _keyDown[virtualKeyCode] = false;
                }
                return msg == WM_SYSKEYUP ? DefWindowProcW(hwnd, msg, wParam, lParam) : 0;
            }

            case WM_KILLFOCUS:
                // Keys can't be reliably tracked once another window owns input focus - clear all
                // state so a key held down during an Alt-Tab doesn't appear "stuck" on return.
                Array.Clear(_keyDown);
                if (_mouseCaptured)
                {
                    _ = ShowCursor(TRUE);
                    _ = ClipCursor(null);
                }
                return DefWindowProcW(hwnd, msg, wParam, lParam);

            case WM_SETFOCUS:
                if (_mouseCaptured)
                {
                    _ = ShowCursor(FALSE);
                    ClipCursorToClientArea();
                    RecenterCursor();
                }
                return DefWindowProcW(hwnd, msg, wParam, lParam);

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

        if (_mouseCaptured)
        {
            _ = ShowCursor(TRUE);
            _ = ClipCursor(null);
            _mouseCaptured = false;
        }

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
