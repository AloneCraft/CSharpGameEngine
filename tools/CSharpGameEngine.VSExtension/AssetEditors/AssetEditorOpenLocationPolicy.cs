using System;
namespace CSharpGameEngine.VSExtension;
/// <summary>アセットエディターを最初に開く場所。UE5 の別ウィンドウに相当する <see cref="Floating"/> が既定。</summary>
internal enum AssetEditorOpenLocation { Floating, DocumentTab }
/// <summary>フローティングウィンドウの位置とサイズ（画面ピクセル）。</summary>
internal readonly struct AssetEditorFloatingBounds(int Left,int Top,int Width,int Height) {
    internal int Left { get;}=Left;
    internal int Top { get;}=Top;
    internal int Width { get;}=Width;
    internal int Height { get;}=Height;
}
/// <summary>開く場所の判定。VS に依存しないので単体テストできる。</summary>
internal static class AssetEditorOpenLocationPolicy {
    internal const int PreferredWidth=1280;
    internal const int PreferredHeight=800;
    internal const int MinimumWidth=640;
    internal const int MinimumHeight=480;
    /// <summary>
    /// フレームを浮かせるか。設定がフローティングで、まだこのフレームを配置していない場合だけ true。
    /// ユーザーがドッキングした後の再表示・再アクティブ化では浮かせ直さない。
    /// </summary>
    internal static bool ShouldFloat(AssetEditorOpenLocation Setting,bool AlreadyPlaced)=>
        Setting==AssetEditorOpenLocation.Floating&&!AlreadyPlaced;
    /// <summary>VS メインウィンドウの中央に、希望サイズ（メインウィンドウの 85% を上限）で置く。</summary>
    internal static AssetEditorFloatingBounds CenteredBounds(int MainLeft,int MainTop,int MainWidth,int MainHeight){
        var Width=Math.Max(MinimumWidth,Math.Min(PreferredWidth,MainWidth*85/100));
        var Height=Math.Max(MinimumHeight,Math.Min(PreferredHeight,MainHeight*85/100));
        var Left=MainLeft+(MainWidth-Width)/2;
        var Top=MainTop+(MainHeight-Height)/2;
        return new AssetEditorFloatingBounds(Math.Max(MainLeft,Left),Math.Max(MainTop,Top),Width,Height);
    }
}
