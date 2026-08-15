using System;
using System.Collections.Generic;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VS Shell から独立させた起動方針です。フォーカスに関する契約をホストなしでテストします。
/// </summary>
internal static class StartupAutoOpenPolicy {
    internal const string NoSolutionContext="adfc4e64-0397-11d1-9f4e-00a0c911004f";

    /// <summary>
    /// devenv自身のコマンドライン(<see cref="Environment.GetCommandLineArgs"/>、[0]は実行ファイル
    /// 自身のパス)に既に<c>.sln</c>/<c>.slnx</c>引数が含まれているか。
    ///
    /// <para>
    /// <b>この関数がなぜ必要か</b>: このリポジトリのF5起動設定
    /// (<c>CSharpGameEngine.VSExtension.csproj</c>の<c>StartArguments</c>)は
    /// <c>/rootsuffix Exp "UserEcsProject.slnx"</c>のように<b>常にソリューションパスを渡す</b>。
    /// この場合、VS自身のネイティブな起動シーケンスが最初からそのソリューションを開くと確定している。
    /// ところが<c>TryAutoOpenUserEcsProjectAsync</c>はパッケージの<c>ProvideAutoLoad(ShellInitialized)</c>
    /// によりこのネイティブな読み込みが完了するかなり前に動き出すため、「今ソリューションは開いて
    /// いるか」をポーリングする方式(<see cref="ShouldOpenUserEcsProject"/>)は、確認するタイミング
    /// 自体がネイティブ読み込みの完了より早ければ同じ結果(未オープン)を返し続けてしまう
    /// (実機で、UIスレッド取得直後に再確認する対策を入れても直らなかった — 窓を狭めただけで
    /// レース自体は残っていたため)。
    /// </para>
    ///
    /// <para>
    /// コマンドライン自体はプロセス起動時に確定しており、<b>いつ読んでも同じ結果になる</b>ため、
    /// この判定にはタイミングの問題が原理的に存在しない。ここが真なら
    /// <see cref="ShouldOpenUserEcsProject"/>を呼ぶまでもなく即座に何もしないのが正しい。
    /// </para>
    ///
    /// <para>
    /// このフォールバック本来の対象(<c>394ff04</c>)は「引数なしで<c>devenv.exe /rootsuffix Exp</c>が
    /// 起動されStart Windowに落ちた場合の復旧」であり、その場合はこの関数がfalseを返すので
    /// 既存のポーリング経路がそのまま働く。
    /// </para>
    /// </summary>
    internal static bool CommandLineAlreadySpecifiesASolution(IReadOnlyList<string>Args){
        for(int Index=1;Index<Args.Count;Index++){
            string Arg=Args[Index];
            if(Arg.EndsWith(".sln",StringComparison.OrdinalIgnoreCase)
                ||Arg.EndsWith(".slnx",StringComparison.OrdinalIgnoreCase)){
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Start Window から起動された場合のフォールバックとして<c>UserEcsProject.slnx</c>を自動で開くか。
    ///
    /// <para>
    /// <paramref name="CurrentSolutionPath"/>を見るのが重要で、これが無いと<b>開いたばかりの
    /// ソリューションを閉じて開き直す</b>。パッケージは<c>ProvideAutoLoad(ShellInitialized)</c>で
    /// コマンドラインのソリューションが開き終わる前に必ずロードされるため、判定した時点では
    /// 「未オープン」でも、UIスレッドを取れた時点では開き終わっていることがある(コールドスタートでは
    /// これが通常)。実機で「slnxを開くと一度開いて画像が出た後にもう一度読み込み直す」として観測された
    /// (F5は`/rootsuffix Exp "UserEcsProject.slnx"`で起動するのでこの経路を必ず通る)。
    /// </para>
    ///
    /// <para>
    /// 「対象と同じパスか」ではなく<b>「何かパスが入っていれば開かない」</b>にしている。同一性判定より
    /// 強く、かつ別のソリューションを開いている最中に割り込んでユーザーのソリューションを勝手に
    /// <c>UserEcsProject.slnx</c>へ差し替えてしまう事故(報告された症状より危険)も同時に防げるため。
    /// </para>
    /// </summary>
    internal static bool ShouldOpenUserEcsProject(bool IsSolutionAlreadyOpen,string?CurrentSolutionPath){
        if(IsSolutionAlreadyOpen){
            return false;
        }

        // 開きかけでもDTEはパスを返しうる。何か入っているなら手を出さない。
        return string.IsNullOrWhiteSpace(CurrentSolutionPath);
    }

    // ソリューションを開くためにシェルをアクティブ化してはなりません。VSIX起動中に
    // コードエディタからキーボードフォーカスを奪うためです。
    internal static bool ShouldActivateMainWindow=>false;
}
