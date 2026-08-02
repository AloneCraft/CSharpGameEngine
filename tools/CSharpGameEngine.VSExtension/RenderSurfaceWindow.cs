using System;
using System.Runtime.InteropServices;
using static CSharpGameEngine.VSExtension.NativeMethods;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// A single lightweight native <c>WS_CHILD</c> window used purely as the D3D12 swap-chain target
/// - the "slot" that <see cref="ViewerHwndHost"/> returns from <c>BuildWindowCore</c>. Forwards
/// <c>WM_SIZE</c> so the caller can resize the swap chain to match (ADR-0008).
/// </summary>
internal sealed class RenderSurfaceWindow : IDisposable
{
    private const string ClassName = "CSharpGameEngine.VSExtension.RenderSurface";

    private static readonly object s_registrationLock = new();
    private static readonly WndProcDelegate s_wndProcDelegate = WndProc;
    private static bool s_classRegistered;

    private static readonly System.Collections.Generic.Dictionary<IntPtr, RenderSurfaceWindow> s_instances = [];

    private IntPtr _hwnd;
    private bool _disposed;

    public event Action<int, int>? Resized;

    public RenderSurfaceWindow(IntPtr parentHwnd, int width, int height)
    {
        EnsureClassRegistered();

        _hwnd = CreateWindowEx(
            WS_EX_NOREDIRECTIONBITMAP,
            ClassName,
            string.Empty,
            WS_CHILD | WS_VISIBLE,
            0,
            0,
            width,
            height,
            parentHwnd,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateWindowEx failed with Win32 error 0x{error:X8}.");
        }

        lock (s_instances)
        {
            s_instances[_hwnd] = this;
        }
    }

    public IntPtr Handle => _hwnd;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (s_instances)
        {
            s_instances.Remove(_hwnd);
        }

        if (_hwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
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

            WNDCLASSEX wc = new()
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = s_wndProcDelegate,
                hInstance = GetModuleHandle(null),
                hCursor = IntPtr.Zero,
                lpszClassName = ClassName,
            };

            if (RegisterClassEx(ref wc) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"RegisterClassEx failed with Win32 error 0x{error:X8}.");
            }

            s_classRegistered = true;
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_SIZE)
        {
            RenderSurfaceWindow? instance;
            lock (s_instances)
            {
                s_instances.TryGetValue(hWnd, out instance);
            }

            if (instance is not null)
            {
                long lp = lParam.ToInt64();
                int width = (int)(lp & 0xFFFF);
                int height = (int)((lp >> 16) & 0xFFFF);
                instance.Resized?.Invoke(width, height);
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
