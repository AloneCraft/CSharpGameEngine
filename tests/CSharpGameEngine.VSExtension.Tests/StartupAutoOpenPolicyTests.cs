namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// 起動時の自動オープン方針。VS Shell 無しで契約を固定する。
/// </summary>
public sealed class StartupAutoOpenPolicyTests {
    private const string TargetSolution=@"G:\GitHub\CSharpGameEngine\UserEcsProject.slnx";

    /// <summary>Start Window から起動した場合のフォールバックが生きていること(394ff04で復元した挙動)。</summary>
    [Fact]
    public void NoSolutionAtAll_OpensTheUserEcsProject(){
        Assert.True(StartupAutoOpenPolicy.ShouldOpenUserEcsProject(
            IsSolutionAlreadyOpen:false,CurrentSolutionPath:null));
        Assert.True(StartupAutoOpenPolicy.ShouldOpenUserEcsProject(
            IsSolutionAlreadyOpen:false,CurrentSolutionPath:string.Empty));
        Assert.True(StartupAutoOpenPolicy.ShouldOpenUserEcsProject(
            IsSolutionAlreadyOpen:false,CurrentSolutionPath:"   "));
    }

    [Fact]
    public void SolutionAlreadyOpen_DoesNotOpenAgain(){
        Assert.False(StartupAutoOpenPolicy.ShouldOpenUserEcsProject(
            IsSolutionAlreadyOpen:true,CurrentSolutionPath:TargetSolution));
    }

    /// <summary>
    /// ソリューションが開きかけで<c>VSPROPID_IsSolutionOpen</c>がまだ立っていないがパスは判っている
    /// 状態。判っているなら開き直してはいけない(開き直すと二重読み込みになる)。
    ///
    /// <para>
    /// 実際の「slnxを開くと一度開いて画像が出た後にもう一度読み込み直す」という不具合の本質的な原因は
    /// これではなく<see cref="CommandLineAlreadySpecifiesASolution"/>で防ぐべきレース
    /// (コマンドラインでソリューションを渡して起動する経路では、ネイティブな読み込みが
    /// <c>GetSolutionInfo</c>へパスを反映し始めるより先にこの判定が呼ばれることがあり、その窓の
    /// 内側ではここは無力)だった。この判定自体は、真に引数なし起動されたフォールバック経路で
    /// ユーザーが2秒の待機中に別のソリューションを開いた場合の直列化としてなお意味を持つ。
    /// </para>
    /// </summary>
    [Fact]
    public void TargetSolutionStillOpening_DoesNotReopenIt(){
        Assert.False(StartupAutoOpenPolicy.ShouldOpenUserEcsProject(
            IsSolutionAlreadyOpen:false,CurrentSolutionPath:TargetSolution));
    }

    /// <summary>
    /// 別のソリューションを開いている最中に割り込んで差し替えないこと。報告された症状より危険な
    /// 潜在バグ(ユーザーのソリューションが勝手にUserEcsProjectへ置き換わる)の防止。
    /// </summary>
    [Fact]
    public void AnotherSolutionStillOpening_IsNotHijacked(){
        Assert.False(StartupAutoOpenPolicy.ShouldOpenUserEcsProject(
            IsSolutionAlreadyOpen:false,CurrentSolutionPath:@"D:\Work\SomeOtherApp.sln"));
        Assert.False(StartupAutoOpenPolicy.ShouldOpenUserEcsProject(
            IsSolutionAlreadyOpen:true,CurrentSolutionPath:@"D:\Work\SomeOtherApp.sln"));
    }

    [Fact]
    public void DoesNotActivateTheMainWindow(){
        // シェルをアクティブ化するとVSIX起動中にコードエディタからキーボードフォーカスを奪う。
        Assert.False(StartupAutoOpenPolicy.ShouldActivateMainWindow);
    }

    /// <summary>
    /// 今回の実際の不具合と修正の核心: このリポジトリのF5起動設定
    /// (<c>CSharpGameEngine.VSExtension.csproj</c>の<c>StartArguments</c>)が実際に生成する
    /// コマンドラインそのもの。この形なら、タイミングに依存するどの判定にも頼らず即座にfalseを
    /// 返さなければならない。
    /// </summary>
    [Fact]
    public void CommandLineAlreadySpecifiesASolution_TrueForTheActualF5LaunchArguments(){
        Assert.True(StartupAutoOpenPolicy.CommandLineAlreadySpecifiesASolution(
            ["devenv.exe","/rootsuffix","Exp",TargetSolution]));
    }

    /// <summary>引数なし起動(394ff04本来の対象、Start Windowに落ちるケース)ではフォールバックが必要。</summary>
    [Fact]
    public void CommandLineAlreadySpecifiesASolution_FalseWhenLaunchedWithoutArguments(){
        Assert.False(StartupAutoOpenPolicy.CommandLineAlreadySpecifiesASolution(["devenv.exe"]));
        Assert.False(StartupAutoOpenPolicy.CommandLineAlreadySpecifiesASolution(
            ["devenv.exe","/rootsuffix","Exp"]));
    }

    [Fact]
    public void CommandLineAlreadySpecifiesASolution_TrueForLegacySlnExtension(){
        Assert.True(StartupAutoOpenPolicy.CommandLineAlreadySpecifiesASolution(
            ["devenv.exe",@"D:\Work\Legacy.sln"]));
    }

    [Fact]
    public void CommandLineAlreadySpecifiesASolution_IsCaseInsensitive(){
        Assert.True(StartupAutoOpenPolicy.CommandLineAlreadySpecifiesASolution(
            ["devenv.exe",@"G:\GitHub\CSharpGameEngine\UserEcsProject.SLNX"]));
    }

    /// <summary>
    /// 単発のファイル引数(例えば.csファイルを直接開く起動)は「ソリューションを指定している」
    /// ことにはならない — この判定はソリューション/プロジェクトファイルの拡張子だけを見る。
    /// </summary>
    [Fact]
    public void CommandLineAlreadySpecifiesASolution_FalseForNonSolutionFileArguments(){
        Assert.False(StartupAutoOpenPolicy.CommandLineAlreadySpecifiesASolution(
            ["devenv.exe","/rootsuffix","Exp",@"G:\GitHub\CSharpGameEngine\UserEcsProject\Scenes\FirstPersonStageScene.cs"]));
    }
}
