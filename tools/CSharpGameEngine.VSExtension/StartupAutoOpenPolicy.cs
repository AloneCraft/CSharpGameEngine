namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VS Shell から独立させた起動方針です。フォーカスに関する契約をホストなしでテストします。
/// </summary>
internal static class StartupAutoOpenPolicy {
    internal const string NoSolutionContext="adfc4e64-0397-11d1-9f4e-00a0c911004f";

    internal static bool ShouldOpenUserEcsProject(bool IsSolutionAlreadyOpen)=>!IsSolutionAlreadyOpen;

    // ソリューションを開くためにシェルをアクティブ化してはなりません。VSIX起動中に
    // コードエディタからキーボードフォーカスを奪うためです。
    internal static bool ShouldActivateMainWindow=>false;
}
