using CSharpGameEngine.Core;
namespace CSharpGameEngine.Platform.Windows;

public sealed class Win32WindowOptions {
    public WindowId WindowId { get; init;}=WindowId.Primary;
    public string Title { get; init;}="CSharpGameEngine";

    public int Width { get; init;}=1280;

    public int Height { get; init;}=720;

    public bool Visible { get; init;}=true;

    /// <summary>
    /// デバイスからのキーボード、Rawマウス、ゲームパッド入力を破棄し、<see cref="Win32Window.InjectKey"/>で注入したキーだけを入力ストリームへ通す。
    /// 自動実行では、マシンを操作しているユーザーから独立して実行できるように使う。
    /// </summary>
    public bool IgnoreDeviceInput { get; init;}
}
