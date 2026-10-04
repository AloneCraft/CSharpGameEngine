using CSharpGameEngine.Core;
namespace CSharpGameEngine.Platform.Windows;

public sealed class Win32WindowOptions {
    public WindowId WindowId { get; init;}=WindowId.Primary;
    public string Title { get; init;}="CSharpGameEngine";

    public int Width { get; init;}=1280;

    public int Height { get; init;}=720;

    public bool Visible { get; init;}=true;

    /// <summary>
    /// Drops keyboard, raw mouse and gamepad input from the devices, so only keys injected through
    /// <see cref="Win32Window.InjectKey"/> reach the input stream. Automation runs use it to stay
    /// independent of whoever is using the machine.
    /// </summary>
    public bool IgnoreDeviceInput { get; init;}
}
