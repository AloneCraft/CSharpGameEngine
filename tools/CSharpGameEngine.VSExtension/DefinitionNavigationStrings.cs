using System.Globalization;
using System.Resources;

namespace CSharpGameEngine.VSExtension;

/// <summary>定義移動 UI の文字列を一元管理する。将来はカルチャ別 resx を追加して翻訳する。</summary>
internal static class DefinitionNavigationStrings {
    private static readonly ResourceManager Resources=new(
        "CSharpGameEngine.VSExtension.DefinitionNavigationStrings",
        typeof(DefinitionNavigationStrings).Assembly);

    internal static string MenuHeader=>Get("MenuHeader","定義へ移動(G)");

    internal static string InputGesture=>Get("InputGesture","F12");

    private static string Get(string Name,string Fallback)=>
        Resources.GetString(Name,CultureInfo.CurrentUICulture)??Fallback;
}
