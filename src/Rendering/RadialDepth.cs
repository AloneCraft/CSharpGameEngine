using System.Numerics;

namespace CSharpGameEngine.Rendering;

/// <summary>
/// Omni のメートル単位の Smooth 放射曲線。<c>G(r) = log2(1 + r / T)</c>。視点付近では線形（傾き <c>1 / (T ln 2)</c>）、遷移距離
/// <c>T</c> を超えると対数になる。そのため32ビット深度でも、ミリメートルから Q64.64 の範囲まで相対解像度をほぼ一定に保てる。Stage シェーダーの <c>RadialG</c> も同
/// じ式を評価する。この型はその CPU 定義であり、テスト時の正解基準でもある。
/// </summary>
public readonly record struct RadialDepthCurve {
    public const float DefaultTransitionMeters=1f;
    private RadialDepthCurve(float TransitionMeters){
        this.TransitionMeters=TransitionMeters;
    }
    /// <summary>曲線が線形から対数へ切り替わる距離。</summary>
    public float TransitionMeters { get; }
    public static RadialDepthCurve Default=>new(DefaultTransitionMeters);
    public static bool IsValidTransition(float TransitionMeters)=>float.IsFinite(TransitionMeters)&&TransitionMeters>0f;
    public static bool TryCreate(float TransitionMeters,out RadialDepthCurve Curve){
        Curve=IsValidTransition(TransitionMeters)?new RadialDepthCurve(TransitionMeters):default;
        return Curve.TransitionMeters>0f;
    }
    /// <summary>メートル単位の非負距離についての <c>log2(1 + r / T)</c>。</summary>
    public float G(float DistanceMeters)=>Log2OnePlus(DistanceMeters/this.TransitionMeters);
    /// <summary>観測者ビューで使う、圧縮後の距離（メートル）。<c>T · G(r)</c>。</summary>
    public float Map(float DistanceMeters)=>this.TransitionMeters*this.G(DistanceMeters);
    /// <summary>
    /// float 演算のみを使って、<c>x ≥ 0</c> の <c>log2(1 + x)</c> を求める。ハードウェアの <c>log2</c> が近似値を返すシェーダー（M0100）でも同じ式を
    /// 計算できるようにする。<c>ln(1 + x) = 2 atanh(s)</c> を使う。<c>√2 − 1</c> 未満では <c>s = x / (2 + x)</c>、それ以上では
    /// <c>1 + x = m · 2^e</c>（<c>m</c> は <c>(√½, √2]</c>）として <c>s = (m − 1) / (m + 1)</c> とする。
    /// <c>|s| ≤ 0.1716</c> なので、奇数次の級数を <c>s⁹</c> まで計算すれば float 精度では正確。
    /// </summary>
    public static float Log2OnePlus(float X){
        if(!float.IsFinite(X)){
            return X;
        }
        var Exponent=0f;
        float S;
        if(X<0.41421356f){
            S=X/(2f+X);
        } else {
            var Bits=BitConverter.SingleToUInt32Bits(1f+X);
            Exponent=(int)(Bits>>23)-127;
            var M=BitConverter.UInt32BitsToSingle((Bits&0x007FFFFFu)|0x3F800000u);
            if(M>1.41421356f){
                M*=0.5f;
                Exponent+=1f;
            }
            S=(M-1f)/(M+1f);
        }
        var S2=S*S;
        var Series=S*(2f+S2*(0.6666667f+S2*(0.4f+S2*(0.2857143f+S2*0.2222222f))));
        return Exponent+Series*1.4426950f;
    }
}

/// <summary>放射ビューがビュープロジェクション行列として渡された場合（エディターのカメラ、M0100）、行列以外に必要な情報。垂直視野角、クリップ範囲、遷移距離を保持する。描画範囲の決定には引き続き行列を使う。</summary>
public readonly record struct RadialViewParameters(float FovYRadians,float NearZ,float FarZ,float TransitionMeters=RadialDepthCurve.DefaultTransitionMeters);

/// <summary>
/// 保守的な放射ラスタ深度 <c>max(Limit − Slope / w, 0)</c>（w はビュー深度）。放射ビューの符号化深度に接する双曲線として距離全体でその下側に位置する（ADR-0076）
/// 。放射ピクセルシェーダーが SV_DepthGreaterEqual を宣言できるため、ハードウェアは隠れたフラグメントを早期に除外できる。
/// </summary>
public readonly record struct RadialConservativeDepth(float Limit,float Slope){
    /// <summary><paramref name="ViewDepth"/> におけるラスタ深度。ラスタライザーと同様、ゼロで下限処理する。</summary>
    public float At(float ViewDepth)=>MathF.Max(this.Limit-this.Slope/ViewDepth,0f);
}

/// <summary>カメラから放射ビューが導出するすべての情報（M0099）。深度正規化係数、フラグメントごとに保持する遠平面、逆行列を使う描画パス用の有限ビュープロジェクション行列、観測者ビュー用の圧縮後ワールド半径を含む。</summary>
public readonly record struct RadialDepthProjection {
    /// <summary>1未満で最大の float。ジオメトリがクリア済み深度に達しないため、空の合成描画を維持できる。</summary>
    public const float MaximumDepth=0.99999994f;
    /// <summary>補助遠平面と近平面の比。float で行列の可逆性を保つ。</summary>
    public const float AuxiliaryDepthRatio=65536f;
    /// <summary>安定した逆投影レイの生成に使う補助近平面の下限。</summary>
    public const float AuxiliaryNearFloorMeters=0.01f;
    private RadialDepthProjection(RadialDepthCurve Curve,float NearZ,float FarZ,float FrustumCornerDistance){
        this.Curve=Curve;
        this.NearZ=NearZ;
        this.FarZ=FarZ;
        this.FrustumCornerDistance=FrustumCornerDistance;
        this.InverseMaximumG=1f/Curve.G(FrustumCornerDistance);
        this.AuxiliaryNearZ=MathF.Max(NearZ,MathF.Min(AuxiliaryNearFloorMeters,FarZ*0.5f));
        this.AuxiliaryFarZ=MathF.Min(FarZ,this.AuxiliaryNearZ*AuxiliaryDepthRatio);
    }
    public RadialDepthCurve Curve { get; }
    public float NearZ { get; }
    /// <summary>カメラの遠平面。float ではラスタ遠平面が無限大になるため、フラグメントごとに適用する。</summary>
    public float FarZ { get; }
    /// <summary>放射ビューのフラグメントを破棄する物理ビュー深度。<see cref="FarZ"/> に1/1024を加えた値とし、丸め誤差によって投影行列自身の遠平面が保持したフラグメントまで除去されないようにする。</summary>
    public float FarDiscardDepth=>this.FarZ*(1f+1f/1024f);
    /// <summary>視点から遠平面の角までの距離。表示可能範囲内で最も遠い点を表す。</summary>
    public float FrustumCornerDistance { get; }
    /// <summary><c>1 / G(FrustumCornerDistance)</c>。深度は <c>G(r)</c> にこの値を掛ける。</summary>
    public float InverseMaximumG { get; }
    /// <summary>空の方向、太陽カスケード、AO の前処理に渡すビュープロジェクションの近平面。</summary>
    public float AuxiliaryNearZ { get; }
    /// <summary>そのビュープロジェクションの遠平面。<c>min(FarZ, AuxiliaryNearZ · 65536)</c>。</summary>
    public float AuxiliaryFarZ { get; }
    /// <summary>
    /// <paramref name="TangentViewDepth"/> において符号化深度に接する、保守的なラスタ深度（ビュー範囲に収め、NaN の場合は近平面を使う）。<c>a − b/w</c>
    /// の双曲線を <c>G(w)/G(rMax)</c> に w0 で接するようにすると、w > 0 のすべての範囲でその下側に留まる。これは <c>w²/(T + w)</c> が単調増加し、2つの式の差
    /// が w0 で唯一の最小値0を持つためである。丸め誤差をカバーするため1/4096の余裕を加える。隠れたフラグメントは接点の近くでのみ早期に除外するため、レンダラーは各オブジェクトのビュー深度に応じた
    /// 接点を設定する（M0100）。
    /// </summary>
    public RadialConservativeDepth ConservativeDepthAt(float TangentViewDepth){
        var Tangent=float.IsNaN(TangentViewDepth)?this.NearZ:Math.Clamp(TangentViewDepth,this.NearZ,this.FrustumCornerDistance);
        var Slope=1f/(MathF.Log(2f)*(this.Curve.TransitionMeters+Tangent));
        return new RadialConservativeDepth(
            (this.Curve.G(Tangent)+Tangent*Slope)*this.InverseMaximumG*(1f-1f/4096f),
            Tangent*Tangent*Slope*this.InverseMaximumG);
    }
    /// <summary>透視投影のビュープロジェクション行列1つとして渡されたビューに対する、<see cref="CreateAuxiliaryProjection"/>。</summary>
    public Matrix4x4 CreateAuxiliaryViewProjection(in Matrix4x4 PerspectiveViewProjection){
        // Matrix4x4.CreatePerspectiveFieldOfView と同じ式を使い、範囲に変更がなければ渡された行列を再構築する。
        var Range=this.AuxiliaryFarZ/(this.AuxiliaryNearZ-this.AuxiliaryFarZ);
        return WithDepthColumn(PerspectiveViewProjection,Range,Range*this.AuxiliaryNearZ);
    }
    /// <summary>ビュープロジェクションが透視投影かどうか。W（ビュー深度）が位置に依存するかで判定する。</summary>
    public static bool IsPerspective(in Matrix4x4 ViewProjection)=>
        ViewProjection.M14!=0f||ViewProjection.M24!=0f||ViewProjection.M34!=0f;
    /// <summary>
    /// 透視投影のビュープロジェクションの深度列を <c>M33 · z + M43</c>（z はビュー空間の z）で置き換える。透視投影の W は <c>−z</c> なので、ビューの z 列は第4列の
    /// 符号を反転したものになり、逆行列を求める必要はない。この積は <c>View · Projection</c> が構成するものなので、これらの値を持つ投影行列を作った場合と同じ結果になる（エディターの
    /// 行列、M0100）。
    /// </summary>
    private static Matrix4x4 WithDepthColumn(in Matrix4x4 ViewProjection,float M33,float M43){
        var Result=ViewProjection;
        Result.M13=-M33*ViewProjection.M14;
        Result.M23=-M33*ViewProjection.M24;
        Result.M33=-M33*ViewProjection.M34;
        Result.M43=-M33*ViewProjection.M44+M43;
        return Result;
    }
    /// <summary>観測者が見る圧縮後のワールドの半径。<c>T · G(FrustumCornerDistance)</c>。</summary>
    public float ObservedWorldRadius=>this.Curve.Map(this.FrustumCornerDistance);
    public static bool TryCreate(RadialDepthCurve Curve,float NearZ,float FarZ,float FovYRadians,float AspectRatio,out RadialDepthProjection Projection){
        Projection=default;
        if(Curve.TransitionMeters<=0f||!float.IsFinite(NearZ)||NearZ<=0f||!float.IsFinite(FarZ)||FarZ<=NearZ
            ||!float.IsFinite(FovYRadians)||FovYRadians<=0f||FovYRadians>=MathF.PI||!float.IsFinite(AspectRatio)||AspectRatio<=0f){
            return false;
        }
        var TanHalfFov=MathF.Tan(FovYRadians*0.5f);
        var Corner=FarZ*MathF.Sqrt(1f+TanHalfFov*TanHalfFov*(1f+AspectRatio*AspectRatio));
        if(!float.IsFinite(Corner)||!float.IsFinite(1f/Curve.G(Corner))){
            return false;
        }
        Projection=new RadialDepthProjection(Curve,NearZ,FarZ,Corner);
        return true;
    }
    /// <summary>視点から物理距離にあるフラグメントに書き込む深度。</summary>
    public float EncodeDepth(float DistanceMeters)=>MathF.Min(this.Curve.G(DistanceMeters)*this.InverseMaximumG,MaximumDepth);
    /// <summary>カメラの視野角と補助近平面・遠平面を使う有限の透視投影。</summary>
    public Matrix4x4 CreateAuxiliaryProjection(float FovYRadians,float AspectRatio)=>
        Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians,AspectRatio,this.AuxiliaryNearZ,this.AuxiliaryFarZ);
}
