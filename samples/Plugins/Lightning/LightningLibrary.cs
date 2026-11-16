[assembly: CSharpGameEngine.Authoring.PluginLibrary]
namespace Lightning;
/// <summary>雷撃の明るさ。同名のコンポーネントがWeatherサンプルプラグインにもあるため、ジェネレーターが両者を区別して命名する。</summary>
public struct Intensity {
    public float Value;
}
/// <summary>雷撃を放つ嵐のセル。</summary>
public struct Bolt {
    public int Count;
}
/// <summary>Libraryモジュールの処理を担う。ECS型を含まない通常のコードなので、コンパイル済みの形で配布できる。</summary>
public static class Discharge {
    /// <summary>指定した強度における1回の雷撃のエネルギー（ギガジュール）。</summary>
    public static float Energy(float Intensity)=>Intensity*Intensity*5f;
}
