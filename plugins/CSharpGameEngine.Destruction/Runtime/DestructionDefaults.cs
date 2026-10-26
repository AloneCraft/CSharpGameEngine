namespace CSharpGameEngine.Destruction;
/// <summary>
/// 調理済みの破砕アセットの保存場所、使用できる名前、分割時の既定値（ADR-0078/M0113）。プラグインの Runtime と Cook モジュールの両方でこのファイルをコンパイルするため、シーン
/// 側で省略された <c>Destructible</c> のメンバー値が調理側と一致する。
/// </summary>
public static class DestructionDefaults {
    public const string Folder="Assets/Destruction";
    public const string Extension=".cgefracture";
    public const string DestructibleType="CSharpGameEngine.Destruction.Destructible";
    public const int FragmentCount=16;
    public const int Seed=0;
    public const int ClusterCount=0;
    /// <summary>クラスター階層の段数（M0124）。1 は破片を一段のクラスターにまとめる設定で、M0113 と同じ。</summary>
    public const int ClusterLevels=1;
    /// <summary>破片の密度（kg/m³）。</summary>
    public const int Density=500;
    /// <summary>結合面積あたりの N·s。</summary>
    public const int BondStrength=2000;
    /// <summary>事前生成したアセットのコンテンツパス。</summary>
    public static string AssetPath(string Name)=>Folder+"/"+Name+Extension;
    /// <summary>名前はファイル名として、英字と数字、および <c>-_.</c> からなる1～64文字とする。先頭をドットにはできない。</summary>
    public static bool IsValidName(string?Name)=>Name is { Length:>0 and <=64 }&&Name.All(static Character=>char.IsAsciiLetterOrDigit(Character)||Character is '-' or '_' or '.')&&Name[0]!='.';
}
