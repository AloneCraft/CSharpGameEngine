using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene View の編集カメラ数学。<c>src/CSharpGameEngine.Windows/Graphics/Camera.cs</c>の
/// <c>CSharpGameEngine.Graphics.D3D12.Camera</c>(net10.0、`[SceneFactory]`属性と
/// `CSharpGameEngine.Spatial.Abstractions`/`.Unity`のRUF経路に依存。Scene DSLのComponent
/// `CSharpGameEngine.Runtime.Windows.Camera`とは別物)の閉形式を net472 側へ書き写したもの
/// (ADR-0041 決定事項1、GizmoMeshFactory と同じ複製方針)。RUF型・`[SceneFactory]`属性は
/// 一切持ち込まない。
///
/// <para>
/// 状態は持たない静的クラスにしてある(<see cref="SceneCameraFraming"/>と同型)。状態の唯一の
/// 所有者は <c>SceneViewState</c> — Yaw/Pitch/FocusPosition/FocusDistance をこちらにも複製すると
/// 「どちらが正か」という二重の真実源が生まれるため。
/// </para>
///
/// <para>
/// <c>MathF</c>・<c>Math.Clamp</c>はいずれも net472 に存在しないため <c>(float)Math.*</c> と
/// 手書きクランプを使う(<see cref="SceneCameraFraming"/>が既に実践している回避)。
/// </para>
/// </summary>
internal static class SceneViewCamera {
    // Orbit(±80°/40°)を採用する。このカメラはOrbit/Focus中心の編集カメラであり、Fly(WASDQE)中も
    // Pitchクランプ範囲は変えない — 仕様にFly専用のPitch範囲の言及は無く、Look→Fly→Lookの間で
    // Pitchの意味が不連続になるのを避けるため。CreateFirstPerson用の±85°
    // (legacy Camera.MinPitchRadiansFirstPerson)は無関係なプレイヤー一人称カメラのものであり、
    // このエディタカメラには適用しない。
    internal const float MinPitchRadians=-80f*(float)(Math.PI/180.0);
    internal const float MaxPitchRadians=40f*(float)(Math.PI/180.0);

    /// <summary>
    /// ピッチを含む完全な前方向。CameraTests.GetForward_MatchesLegacyTrigFormula_AcrossYawPitchSweep
    /// が固定する閉形式 Forward=normalize(cosPitch*sinYaw, sinPitch, cosPitch*cosYaw) と同一。
    /// </summary>
    internal static Vector3 GetForward(float Yaw,float Pitch){
        var CosPitch=(float)Math.Cos(Pitch);
        return Vector3.Normalize(new Vector3(
            CosPitch*(float)Math.Sin(Yaw),
            (float)Math.Sin(Pitch),
            CosPitch*(float)Math.Cos(Yaw)));
    }

    /// <summary>ピッチを除いたXZ平面上の前方向(Fly移動の水平基準)。</summary>
    internal static Vector3 GetFlatForward(float Yaw)=>
        Vector3.Normalize(new Vector3((float)Math.Sin(Yaw),0f,(float)Math.Cos(Yaw)));

    /// <summary>
    /// 水平右方向。CameraTests.GetFlatRight_MatchesLegacyCrossProductFormula_AcrossYawSweep が
    /// 固定する閉形式 FlatRight=normalize(Cross(UnitY,FlatForward)) と同一。
    /// </summary>
    internal static Vector3 GetFlatRight(float Yaw)=>
        Vector3.Normalize(Vector3.Cross(Vector3.UnitY,GetFlatForward(Yaw)));

    /// <summary>
    /// カメラ相対のUpベクトル(Pan専用、新規導出 — legacy Camera.cs に対応物なし)。
    /// Cross(UnitY,Forward)=(Fz,0,-Fx) はForwardのY成分(Pitch由来)に依存しないため、
    /// GetFlatRight はPitchに関わらず常に正しいカメラ右ベクトルになる。UpはこのRightと
    /// Forwardの外積で求める(Matrix4x4.CreateLookAt相当のLH基底構成と同型)。
    /// </summary>
    internal static Vector3 GetUp(float Yaw,float Pitch)=>
        Vector3.Normalize(Vector3.Cross(GetForward(Yaw,Pitch),GetFlatRight(Yaw)));

    internal static Vector3 GetEyePosition(Vector3 FocusPosition,float Yaw,float Pitch,float FocusDistance)=>
        FocusPosition-GetForward(Yaw,Pitch)*FocusDistance;

    /// <summary>
    /// ビュー行列。<c>Matrix4x4.CreateLookAt</c>(右手系)を使う — <b>ゲーム側の描画と一致させるため
    /// であり、意図的である</b>。
    ///
    /// <para>
    /// <b>ここを「左手系へ直す」誘惑への警告(2026-08に一度実際にやって差し戻した):</b>
    /// この座標系の基底は左手系(<c>GetFlatForward=(sinY,0,cosY)</c>、<c>GetFlatRight=Cross(UnitY,Forward)</c>、
    /// ADR-0025のUnity規約=LeftHanded)なのに、行列は右手系である。右手系の<c>CreateLookAt</c>は
    /// <c>xaxis=Cross(Up,normalize(Eye-Target))=-GetFlatRight</c>を作るため、
    /// <b><see cref="GetFlatRight"/>は名前に反して画面の「左」へ写る</b>
    /// (<c>SceneViewCameraTests.GetFlatRight_ProjectsToScreenLeft_BecauseTheEngineUsesRightHandedMatrices</c>
    /// がこれを固定している)。一見バグに見えるが、<b>ゲーム本体も同じ右手系を使っている</b>
    /// (<c>src/CSharpGameEngine.Windows/Graphics/Camera.cs</c>の<c>BuildViewMatrix</c>/<c>BuildProjection</c>、
    /// <c>StageRenderer</c>、<c>ModelRenderer</c>)。シーンデザイナーはゲームの見た目を再現するものなので、
    /// ここだけ左手系にするとデザイナーだけがゲームと左右反転する(実際にそうなった)。
    /// </para>
    ///
    /// <para>
    /// 帰結として、画面上で「右」に見える方向は<c>-GetFlatRight</c>である。だから
    /// Fly の A/D と <c>SceneViewState.ApplyPan</c> の横方向はどちらも符号を反転させてある
    /// (それぞれの箇所のコメント参照)。「行列を直せば反転が要らなくなる」は誤り —
    /// ゲームとの一致が壊れる。
    /// </para>
    ///
    /// <para>
    /// 「ADR-0025がUnity=左手系と宣言しているのに行列は右手系」というエンジン全体の矛盾は実在するが、
    /// ゲームの描画にも等しく効いているためシーンエディタ単独で決着できない。Phase 3のギズモは
    /// +Xハンドルが画面左を指すことになるので影響を受ける — その時点で別ADRとして扱う。
    /// </para>
    /// </summary>
    internal static Matrix4x4 GetView(Vector3 FocusPosition,float Yaw,float Pitch,float FocusDistance){
        Vector3 Eye=GetEyePosition(FocusPosition,Yaw,Pitch,FocusDistance);
        Vector3 LookAtPoint=FocusDistance>1e-4f?FocusPosition:Eye+GetForward(Yaw,Pitch);
        return Matrix4x4.CreateLookAt(Eye,LookAtPoint,Vector3.UnitY);
    }

    /// <summary>透視投影。ゲーム側(<c>Camera.BuildProjection</c>)と同じ右手系(<see cref="GetView"/>参照)。</summary>
    internal static Matrix4x4 GetProjection(float FovYRadians,float AspectRatio,float NearZ,float FarZ)=>
        Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians,AspectRatio,NearZ,FarZ);

    /// <summary>
    /// 生のマウス移動量をYaw/Pitchへ適用する。DeltaYを減算するのはlegacy Camera.ApplyMouseDeltaと
    /// 同じ規約(画面下方向のドラッグ=ピッチダウン)。Pitchのみクランプする。
    /// </summary>
    internal static(float Yaw,float Pitch)ApplyLookDelta(
        float Yaw,float Pitch,float DeltaX,float DeltaY,
        float YawSensitivity,float PitchSensitivity,bool InvertX,bool InvertY){
        float NewYaw=Yaw+DeltaX*YawSensitivity*(InvertX?-1f:1f);
        float NewPitch=Math.Min(Math.Max(
            Pitch-DeltaY*PitchSensitivity*(InvertY?-1f:1f),
            MinPitchRadians),MaxPitchRadians);
        return(NewYaw,NewPitch);
    }

    /// <summary>
    /// M0020 Phase 2 ピッキング用。スクリーン座標(物理ピクセル)をワールド空間のレイへ変換する。
    /// Near/Far点を両方unprojectし差分を方向にする標準手法 — 設計書§3の「透視は視点から、平行は
    /// 方向固定のレイ」という場合分けを、この手法は分岐なしで数学的に包含する(Orthographicでも
    /// Near/Farの差分は投影方向に一致する)。
    /// </summary>
    internal static(Vector3 Origin,Vector3 Direction)ScreenPointToRay(
        Matrix4x4 View,Matrix4x4 Projection,float ScreenX,float ScreenY,float ViewportWidth,float ViewportHeight){
        Matrix4x4.Invert(View*Projection,out Matrix4x4 InverseViewProjection);
        return ScreenPointToRay(InverseViewProjection,ScreenX,ScreenY,ViewportWidth,ViewportHeight);
    }

    /// <summary>
    /// M0037: <see cref="GizmoDragHud"/>のように1フレームで多数の画面点を世界座標へ変換する呼び出し元
    /// 向けに、逆行列を毎回計算し直さずに済むオーバーロード。数学は上のオーバーロードと同一。
    /// </summary>
    internal static(Vector3 Origin,Vector3 Direction)ScreenPointToRay(
        Matrix4x4 InverseViewProjection,float ScreenX,float ScreenY,float ViewportWidth,float ViewportHeight){
        float NdcX=(2f*ScreenX/ViewportWidth)-1f;
        float NdcY=1f-(2f*ScreenY/ViewportHeight); // スクリーンYは下向き、NDC Yは上向き。
        Vector3 NearWorld=UnprojectNdc(NdcX,NdcY,0f,InverseViewProjection);
        Vector3 FarWorld=UnprojectNdc(NdcX,NdcY,1f,InverseViewProjection);
        return(NearWorld,Vector3.Normalize(FarWorld-NearWorld));
    }

    private static Vector3 UnprojectNdc(float NdcX,float NdcY,float NdcZ,Matrix4x4 InverseViewProjection){
        Vector4 Clip=new(NdcX,NdcY,NdcZ,1f);
        Vector4 World=Vector4.Transform(Clip,InverseViewProjection);
        return new Vector3(World.X,World.Y,World.Z)/World.W;
    }

    /// <summary>
    /// ワールド方向を、現在の(Yaw,Pitch)基準のローカル座標系(FlatRight,Up,Forward)へ射影した係数。
    /// この係数はYaw/Pitchの値によらず不変(どの向きを基準に分解しても同じ値になる —
    /// スクリーンピクセルの光学的な位置関係だけを表すため、光学系がYaw/Pitchで変わらない以上、
    /// 分解結果も変わらない)。<see cref="SolveLookOrientation"/>が使う。
    /// </summary>
    internal static Vector3 DecomposeDirection(Vector3 WorldDirection,float Yaw,float Pitch){
        Vector3 Right=GetFlatRight(Yaw);
        Vector3 Up=GetUp(Yaw,Pitch);
        Vector3 Forward=GetForward(Yaw,Pitch);
        return new Vector3(
            Vector3.Dot(WorldDirection,Right),
            Vector3.Dot(WorldDirection,Up),
            Vector3.Dot(WorldDirection,Forward));
    }

    /// <summary>
    /// RMBドラッグのピクセル完全一致(M0020追加)。<paramref name="LocalAxes"/>
    /// (<see cref="DecomposeDirection"/>で得た、現在のマウス位置に対応するローカル係数)が
    /// 世界方向<paramref name="TargetWorldDirection"/>と一致するYaw/Pitchを厳密に解く。
    ///
    /// <para>
    /// 導出: <c>World = Lx*FlatRight + Ly*Up + Lz*Forward</c>を閉形式で展開すると、Y成分は
    /// <c>Ly*cosP + Lz*sinP</c>(<c>FlatRight.y</c>が常に0のためYaw非依存)になる。これでPitchを
    /// 先に解き、残るX/Z成分の2元1次系(<c>Lx</c>と<c>Lz*cosP-Ly*sinP</c>)を目標
    /// (<c>Dw.x</c>,<c>Dw.z</c>)へ回転させるYawをatan2で解く。往復誤差は浮動小数点誤差のみ
    /// (実測1e-14未満)。<b>この閉形式はEyeが固定であるLookだけが持つ性質</b>で、Eye自体が動く
    /// Orbitには当てはまらない(<see cref="TrySolveOrbitYawPitch"/>参照)。
    /// </para>
    /// </summary>
    internal static(float Yaw,float Pitch)SolveLookOrientation(
        Vector3 LocalAxes,Vector3 TargetWorldDirection,float PreviousYaw,float PreviousPitch){
        float Lx=LocalAxes.X,Ly=LocalAxes.Y,Lz=LocalAxes.Z;

        float R=(float)Math.Sqrt(Ly*Ly+Lz*Lz);
        if(R<1e-6f){
            // ローカルレイがほぼFlatRight軸(画面の端も端、実運用では起こらない)。解けないので
            // 直前の向きを維持する。
            return(PreviousYaw,PreviousPitch);
        }

        float Phi=(float)Math.Atan2(Lz,Ly);
        float Ratio=Math.Min(Math.Max(TargetWorldDirection.Y/R,-1f),1f);
        float AcosValue=(float)Math.Acos(Ratio);
        float CandidateA=Phi+AcosValue;
        float CandidateB=Phi-AcosValue;
        float Pitch=AngularDistance(CandidateA,PreviousPitch)<=AngularDistance(CandidateB,PreviousPitch)
            ?CandidateA:CandidateB;
        Pitch=Math.Min(Math.Max(Pitch,MinPitchRadians),MaxPitchRadians);

        float K=Lz*(float)Math.Cos(Pitch)-Ly*(float)Math.Sin(Pitch);
        if(Math.Abs(Lx)<1e-6f&&Math.Abs(K)<1e-6f){
            return(PreviousYaw,Pitch);
        }

        float Yaw=(float)Math.Atan2(K,Lx)-(float)Math.Atan2(TargetWorldDirection.Z,TargetWorldDirection.X);
        return(Yaw,Pitch);
    }

    private static float AngularDistance(float A,float B){
        const float TwoPi=(float)(2.0*Math.PI);
        float Delta=A-B;
        while(Delta>Math.PI){ Delta-=TwoPi; }
        while(Delta<-Math.PI){ Delta+=TwoPi; }
        return Math.Abs(Delta);
    }

    /// <summary>
    /// Alt+LMBドラッグ開始時、ヒットテストで何もヒットしなかった場合の仮想アンカー。現在の
    /// 注視平面(<paramref name="FocusPosition"/>を通り<paramref name="Forward"/>を法線とする平面)と
    /// クリックレイの交点を使う(「今フォーカスしているdepth」を仮のアンカー距離として採用する
    /// 自然な選択)。レイが平面とほぼ平行、または平面がレイの後方にある場合は
    /// <paramref name="FocusPosition"/>自体へ縮退する。
    /// </summary>
    internal static Vector3 ComputeOrbitAnchorFallbackPoint(
        Vector3 RayOrigin,Vector3 RayDirection,Vector3 FocusPosition,Vector3 Forward){
        float Denominator=Vector3.Dot(RayDirection,Forward);
        if(Math.Abs(Denominator)<1e-4f){
            return FocusPosition;
        }

        float T=Vector3.Dot(FocusPosition-RayOrigin,Forward)/Denominator;
        return T<=0f?FocusPosition:RayOrigin+RayDirection*T;
    }

    /// <summary>
    /// Alt+LMBドラッグのピクセル完全一致(M0020追加、ベストエフォート)。世界座標
    /// <paramref name="AnchorWorldPoint"/>がスクリーン座標
    /// (<paramref name="TargetScreenX"/>,<paramref name="TargetScreenY"/>)へ投影されるYaw/Pitchを、
    /// 減衰付きNewton法(Levenberg–Marquardt)で数値的に解く。
    ///
    /// <para>
    /// <b>OrbitにはLook(<see cref="SolveLookOrientation"/>)のような閉形式が存在しない。</b>
    /// EyeがFocusPosition周りの球面上を動くため、固定点のスクリーン位置はYaw/Pitchについて
    /// Eye位置と視線方向の両方を通じて非線形に依存し、しかも解の枝が分岐(fold)することが
    /// 数値実験で確認されている — クリック位置によっては、目標がわずか数pxしか動いていなくても
    /// 真の解が40°以上離れた場所に存在することがある。<b>これはバグではなく2自由度・注視点固定
    /// という束縛条件が持つ構造的な性質であり、「収束するまで反復回数を増やす」方向へ
    /// 直そうとしないこと</b>
    /// (<c>SceneViewCameraTests.TrySolveOrbitYawPitch_NeverIncreasesResidualBeyondTheSeed</c>参照、
    /// このセッションで一度、左右手系の食い違いを同様に「バグ」と誤診して改悪した前例がある)。
    /// </para>
    ///
    /// <para>
    /// Levenberg–Marquardtは残差を単調に悪化させないことが保証されるため、戻り値が<c>false</c>
    /// (収束せず)でも<paramref name="Yaw"/>/<paramref name="Pitch"/>には常に「その時点で最良」の
    /// 値が入る — 呼び出し元は戻り値を無視して無条件に採用してよい。完全一致を維持できない
    /// 区間ではその中で最も近づける位置に留まり、区間を抜ければ自動的に完全一致へ復帰する。
    /// </para>
    /// </summary>
    internal static bool TrySolveOrbitYawPitch(
        Vector3 FocusPosition,float FocusDistance,Matrix4x4 Projection,Vector3 AnchorWorldPoint,
        float TargetScreenX,float TargetScreenY,float ViewportWidth,float ViewportHeight,
        float SeedYaw,float SeedPitch,out float Yaw,out float Pitch){
        const int MaxIterations=10;
        const int MaxDampingRetries=30;
        const float ToleranceScreenPixels=0.05f;
        const float FiniteDifferenceStep=1e-4f;

        bool TryComputeResidual(float CandidateYaw,float CandidatePitch,out Vector2 Result){
            Matrix4x4 View=GetView(FocusPosition,CandidateYaw,CandidatePitch,FocusDistance);
            if(!TryProjectToScreen(
                AnchorWorldPoint,View,Projection,ViewportWidth,ViewportHeight,
                out float ScreenX,out float ScreenY)){
                Result=default;
                return false;
            }

            Result=new Vector2(ScreenX-TargetScreenX,ScreenY-TargetScreenY);
            return true;
        }

        bool TryComputeJacobian(
            float CandidateYaw,float CandidatePitch,Vector2 BaseResidual,out Matrix2x2 Result){
            if(!TryComputeResidual(CandidateYaw+FiniteDifferenceStep,CandidatePitch,out Vector2 ResidualYawPlus)
                ||!TryComputeResidual(
                    CandidateYaw,CandidatePitch+FiniteDifferenceStep,out Vector2 ResidualPitchPlus)){
                Result=default;
                return false;
            }

            Result=new Matrix2x2(
                (ResidualYawPlus.X-BaseResidual.X)/FiniteDifferenceStep,
                (ResidualPitchPlus.X-BaseResidual.X)/FiniteDifferenceStep,
                (ResidualYawPlus.Y-BaseResidual.Y)/FiniteDifferenceStep,
                (ResidualPitchPlus.Y-BaseResidual.Y)/FiniteDifferenceStep);
            return true;
        }

        float CurrentYaw=SeedYaw;
        float CurrentPitch=ClampOrbitPitch(SeedPitch);
        if(!TryComputeResidual(CurrentYaw,CurrentPitch,out Vector2 Residual)){
            Yaw=SeedYaw;
            Pitch=CurrentPitch;
            return false;
        }

        float Lambda=1e-2f;
        for(int Iteration=0;Iteration<MaxIterations;Iteration++){
            if(Residual.Length()<=ToleranceScreenPixels){
                Yaw=CurrentYaw;
                Pitch=CurrentPitch;
                return true;
            }

            if(!TryComputeJacobian(CurrentYaw,CurrentPitch,Residual,out Matrix2x2 Jacobian)){
                break; // カメラ背面等で微分すら取れない。ここまでの最良値で打ち切る。
            }

            bool Improved=false;
            for(int Retry=0;Retry<MaxDampingRetries;Retry++){
                if(!Jacobian.TrySolveDamped(Residual,Lambda,out float DeltaYaw,out float DeltaPitch)){
                    Lambda*=4f;
                    continue;
                }

                float CandidateYaw=CurrentYaw+DeltaYaw;
                float CandidatePitch=ClampOrbitPitch(CurrentPitch+DeltaPitch);
                if(TryComputeResidual(CandidateYaw,CandidatePitch,out Vector2 CandidateResidual)
                    &&CandidateResidual.Length()<Residual.Length()){
                    CurrentYaw=CandidateYaw;
                    CurrentPitch=CandidatePitch;
                    Residual=CandidateResidual;
                    Lambda=Math.Max(Lambda*0.3f,1e-7f);
                    Improved=true;
                    break;
                }

                Lambda*=4f;
            }

            if(!Improved){
                break; // 改善方向が見つからない = このアンカーはこれ以上近づけない(局所的な限界)。
            }
        }

        Yaw=CurrentYaw;
        Pitch=CurrentPitch;
        return Residual.Length()<=ToleranceScreenPixels;
    }

    private static float ClampOrbitPitch(float Pitch)=>Math.Min(Math.Max(Pitch,MinPitchRadians),MaxPitchRadians);

    /// <summary>
    /// <see cref="ScreenPointToRay"/>の逆写像。カメラ背後/至近すぎる場合はfalseを返す。M0037:
    /// <see cref="GizmoDragHud"/>がHUDの基準点(ピボット等)をピクセル位置へ変換するため公開。
    /// </summary>
    internal static bool TryProjectToScreen(
        Vector3 WorldPoint,Matrix4x4 View,Matrix4x4 Projection,
        float ViewportWidth,float ViewportHeight,out float ScreenX,out float ScreenY){
        Vector4 Clip=Vector4.Transform(new Vector4(WorldPoint,1f),View*Projection);
        if(Clip.W<=1e-5f){
            ScreenX=0f;
            ScreenY=0f;
            return false;
        }

        float NdcX=Clip.X/Clip.W;
        float NdcY=Clip.Y/Clip.W;
        ScreenX=(NdcX+1f)*0.5f*ViewportWidth;
        ScreenY=(1f-NdcY)*0.5f*ViewportHeight;
        return true;
    }

    /// <summary>Levenberg–Marquardt用の2x2ヤコビアン。減衰正規方程式をこの場だけで解く小さなヘルパー。</summary>
    private readonly struct Matrix2x2 {
        private readonly float M00,M01,M10,M11;

        internal Matrix2x2(float M00,float M01,float M10,float M11){
            this.M00=M00;
            this.M01=M01;
            this.M10=M10;
            this.M11=M11;
        }

        /// <summary>正規方程式 (JᵀJ + λ*diag(JᵀJ)) * delta = -Jᵀf を2x2で直接解く。</summary>
        internal bool TrySolveDamped(Vector2 Residual,float Lambda,out float DeltaYaw,out float DeltaPitch){
            float A00=M00*M00+M10*M10;
            float A01=M00*M01+M10*M11;
            float A11=M01*M01+M11*M11;
            A00+=Lambda*A00;
            A11+=Lambda*A11;

            float B0=-(M00*Residual.X+M10*Residual.Y);
            float B1=-(M01*Residual.X+M11*Residual.Y);

            float Det=A00*A11-A01*A01;
            if(Math.Abs(Det)<1e-12f){
                DeltaYaw=0f;
                DeltaPitch=0f;
                return false;
            }

            DeltaYaw=(B0*A11-A01*B1)/Det;
            DeltaPitch=(A00*B1-A01*B0)/Det;
            return true;
        }
    }
}
