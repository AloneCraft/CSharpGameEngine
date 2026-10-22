namespace CSharpGameEngine.Input;
/// <summary>
/// 入力を構造体で宣言する(ADR-0077)。メンバーは<see cref="Stick"/>・<see cref="Look"/>・<see cref="Button"/>で、
/// 割り当ては<see cref="KeysAttribute"/>・<see cref="GamepadAttribute"/>・<see cref="MouseAttribute"/>で書く。
/// Source Generatorが入力マップとActionのIDを生成し、毎フレーム値を埋める。この構造体を<c>in</c>で受けたSystemは、
/// 入力のあとのFrameInputフェーズで動く(<c>[SystemPhase]</c>・<c>[RunsAfter]</c>は要らない)。構造体は<c>partial</c>にする。
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class InputActionsAttribute:Attribute {
}
/// <summary>キーボードのキー。値はWindowsの仮想キーコード(UEのキー名への変換はプラットフォーム層が行う)。</summary>
public enum Key {
    Backspace=0x08,Tab=0x09,Enter=0x0D,Shift=0x10,Control=0x11,Alt=0x12,Escape=0x1B,Space=0x20,
    Left=0x25,Up=0x26,Right=0x27,Down=0x28,
    Digit0=0x30,Digit1=0x31,Digit2=0x32,Digit3=0x33,Digit4=0x34,Digit5=0x35,Digit6=0x36,Digit7=0x37,Digit8=0x38,Digit9=0x39,
    A=0x41,B=0x42,C=0x43,D=0x44,E=0x45,F=0x46,G=0x47,H=0x48,I=0x49,J=0x4A,K=0x4B,L=0x4C,M=0x4D,
    N=0x4E,O=0x4F,P=0x50,Q=0x51,R=0x52,S=0x53,T=0x54,U=0x55,V=0x56,W=0x57,X=0x58,Y=0x59,Z=0x5A,
    F1=0x70,F2=0x71,F3=0x72,F4=0x73,F5=0x74,F6=0x75,F7=0x76,F8=0x77,F9=0x78,F10=0x79,F11=0x7A,F12=0x7B,
}
/// <summary>ゲームパッドの入力。スティックは2軸、ボタンは南(A/×)・東(B/○)・西(X/□)・北(Y/△)。</summary>
public enum Pad {
    LeftStick,RightStick,South,East,West,North,
}
public enum MouseButton {
    Left,Right,Middle,
}
public enum MouseAxis {
    /// <summary>1フレームのマウス移動量(上へ動かすとYが正)。</summary>
    Delta,
}
/// <summary>
/// キーを割り当てる。<see cref="Button"/>には1つ以上のキー(どれかが押されていれば押下)、
/// <see cref="Stick"/>には上・下・左・右の4キー(Upが前進)を書く。同じメンバーに複数書ける。
/// </summary>
[AttributeUsage(AttributeTargets.Field,AllowMultiple=true,Inherited=false)]
public sealed class KeysAttribute:Attribute {
    public KeysAttribute(params Key[] Keys){
        this.Keys=Keys;
    }
    public KeysAttribute(Key Up,Key Down,Key Left,Key Right){
        this.Keys=[Up,Down,Left,Right];
    }
    public Key[] Keys { get;}
}
/// <summary>ゲームパッドを割り当てる。<see cref="Stick"/>と<see cref="Look"/>にはスティック、<see cref="Button"/>にはボタンを書く。</summary>
[AttributeUsage(AttributeTargets.Field,AllowMultiple=true,Inherited=false)]
public sealed class GamepadAttribute(Pad Control):Attribute {
    public Pad Control { get;}=Control;
}
/// <summary>マウスを割り当てる。<see cref="Look"/>に<see cref="MouseAxis.Delta"/>、<see cref="Button"/>に<see cref="MouseButton"/>を書く。</summary>
[AttributeUsage(AttributeTargets.Field,AllowMultiple=true,Inherited=false)]
public sealed class MouseAttribute:Attribute {
    public MouseAttribute(MouseAxis Axis){
        this.Axis=Axis;
    }
    public MouseAttribute(MouseButton Button){
        this.Button=Button;
    }
    public MouseAxis? Axis { get;}
    public MouseButton? Button { get;}
}
/// <summary>画面上のポインター(マウスのカーソル)を受ける印。<see cref="Pointer"/>型のメンバーに付ける(ADR-0077)。</summary>
[AttributeUsage(AttributeTargets.Field,AllowMultiple=false,Inherited=false)]
public sealed class PointerAttribute:Attribute {
}
/// <summary>2軸の状態値。Xは右、Yは前(スティックを倒した向き)。長さは1を超えうる(キーの斜めなど)ので、使う側が丸める。</summary>
public readonly struct Stick(float X,float Y) {
    public readonly float X=X;
    public readonly float Y=Y;
}
/// <summary>
/// このフレームに視点を回す量(turns)。Xは右へ、Yは上へ。マウスは移動量、スティックは倒した量にフレーム時間を掛けた値で、
/// 単位の違いはここで吸収されるので、使う側はそのまま視点に足せばよい。
/// </summary>
public readonly struct Look(float X,float Y) {
    /// <summary>マウス1カウントあたりの回転量(turns)。UEの既定値(感度0.07、入力スケール2.5)から推定した0.175度。</summary>
    public const float MouseTurnsPerCount=0.175f/360f;
    /// <summary>スティックを全部倒したときの回転速度(turns/秒)。UEの毎フレーム2.5度(60Hz)の時間基準で150度/秒。</summary>
    public const float StickTurnsPerSecond=150f/360f;
    public readonly float X=X;
    public readonly float Y=Y;
}
/// <summary>ボタンの状態。<see cref="Pressed"/>は押したフレームだけ、<see cref="Released"/>は離したフレームだけtrue。</summary>
public readonly struct Button(bool Held,bool Pressed,bool Released) {
    public readonly bool Held=Held;
    public readonly bool Pressed=Pressed;
    public readonly bool Released=Released;
}
/// <summary>
/// 画面上のポインターの位置。<see cref="X"/>と<see cref="Y"/>はクライアント領域の左上を0、右下を1とした割合。
/// <see cref="Inside"/>はカーソルが領域の中にあるとき true。3Dの位置(ピック)は<c>Picking</c>サービスが求める。
/// </summary>
public readonly struct Pointer(float X,float Y,bool Inside) {
    public readonly float X=X;
    public readonly float Y=Y;
    public readonly bool Inside=Inside;
}
