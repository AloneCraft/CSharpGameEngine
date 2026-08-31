using CSharpGameEngine.Core;
namespace CSharpGameEngine.Platform.Windows;

public sealed class Win32WindowOptions {
    public WindowId WindowId { get; init;}=WindowId.Primary;
    public string Title { get; init;}="CSharpGameEngine";

    public int Width { get; init;}=1280;

    public int Height { get; init;}=720;

    public bool Visible { get; init;}=true;
}
