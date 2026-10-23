using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
namespace CSharpGameEngine.FeatureStages;
/// <summary>
/// TopDownテンプレートのクリック移動の実走検証(ADR-0077、ADR-0079/M0106)。画面上部の1点を数フレームだけクリックし、その地点まで歩いて止まることを
/// キャラクターの位置の推移で判定する(レポートの <c>focusPositions</c>。カメラはキャラクターに遅れて追従するので、カメラ位置では歩行の経路にならない)。
/// 固定tickは1フレームに上限回数だけ進むので、判定は距離の範囲と停止の有無に限る。
/// 同じ入力を 2 回走らせる。(1) ナビメッシュを切って(<c>CGE_NAVIGATION_DISABLED=1</c>)従来どおりの直線で歩く(中央の台のふちから飛び降りる)、
/// (2) ナビメッシュを使って、台のスロープを下り、壁の端を回って、同じ床の目的地へ着く。
/// </summary>
public static class ClickToMoveVerification {
    public const int Frames=80;
    public const int ClickStartFrame=20;
    public const int ClickEndFrame=24;
    /// <summary>カーソルの位置(クライアント領域の割合)。画面の中央上寄りで、キャラクターの前方の地面を指す。</summary>
    public const string Cursor="0.5,0.1";
    /// <summary>クリックを離したあとも目的地へ歩き続けて、これだけは動く(m)。</summary>
    public const float MinimumWalkMeters=2f;
    /// <summary>カーソルを追い続けた場合のように行き過ぎていない上限(m)。目的地は数mの先。</summary>
    public const float MaximumWalkMeters=12f;
    /// <summary>ナビメッシュで歩くときの上限(m)。出発点から着いた位置までは直線で 10.7 m ほど。</summary>
    public const float MaximumNavigatedWalkMeters=16f;
    /// <summary>最後の10フレームの動きがこの距離(m)未満なら止まったとみなす。</summary>
    public const float MaximumFinalDriftMeters=0.1f;
    public const int SettledFrames=10;
    /// <summary>
    /// ナビメッシュで歩いたときの、着く位置(出発点からの水平の動き、R と F)と許す誤差(m)。台のスロープの先、壁の端を回った床の上。
    /// カメラがキャラクターに遅れて追従する(M0107。UEの CameraLagSpeed 3)ので、クリックを押している間にカーソルの下の地面がカメラの遅れの分だけ手前になり、
    /// 目的地は M0106 の (7.06, 8.06) より約 1 m 手前になる。M0108 では短押しの判定(0.5 秒未満)のためクリックを 1 フレーム短くし(4 フレーム押す)、押している間は直接歩く(UE の Follow)ので、着く位置は (6.79, 6.84) になる(M0107 は (6.75, 7.14))。
    /// </summary>
    public const float NavigatedGoalR=6.79f;
    public const float NavigatedGoalF=6.84f;
    public const float NavigatedGoalTolerance=0.5f;
    /// <summary>経路が回る壁の端(出発点からの R と F)と、そこへ近づくべき距離(m)。スロープの通路の壁の端で、直線ではここを通らない。</summary>
    public const float WallEndR=2.2f;
    public const float WallEndF=9.0f;
    public const float WallEndTolerance=1.2f;
    /// <summary>経路の長さの、出発点から着いた位置までの直線に対する下限の倍率。回り込んでいれば 1.25 倍は超える(実走は 1.39)。</summary>
    public const float MinimumDetourRatio=1.25f;
    /// <summary>1フレームのカメラの高さの落ち方の上限(m)。台から飛び降りると 0.6 m を超える落ち方をし、スロープを下る間は 0.4 m 以内。</summary>
    public const float MaximumFrameDropMeters=0.5f;
    /// <summary>
    /// 長押し(M0108)の実走。同じ場所を<see cref="LongPressEndFrame"/>まで押し続ける(フレームの時間は 0.1 秒に頭打ちなので約 2.5 秒。UE の <c>PressedThreshold</c> 0.5 秒を大きく超える)。
    /// 押している間はカーソルの下の地面の向きへ直接歩き続け、離しても目的地へは歩かず、目印も出ない。
    /// </summary>
    public const int LongPressFrames=90;
    public const int LongPressEndFrame=45;
    /// <summary>長押しの間に歩くべき最低の距離(m)。押している間、目的地へ着いて止まるのでなくカーソルを追い続けていること。</summary>
    public const float MinimumLongPressWalkMeters=4f;
    /// <summary>離したあとにさらに歩く距離の上限(m)。制動だけで止まる(目的地まで歩き続けない)こと。歩行速度 6 m/s、制動 10 m/s² の制動距離は約 1.8 m。</summary>
    public const float MaximumWalkAfterReleaseMeters=2.5f;
    public static string LongPressSchedule=>string.Create(CultureInfo.InvariantCulture,$"MouseLeft@{ClickStartFrame}-{LongPressEndFrame}");
    public static string InputSchedule=>string.Create(CultureInfo.InvariantCulture,$"MouseLeft@{ClickStartFrame}-{ClickEndFrame}");
    public static async Task<int>RunAsync(string RepositoryRoot,string Configuration,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Executable=ThirdPersonCharacterVerification.ExecutablePath(RepositoryRoot,Configuration,"TopDown");
        if(!File.Exists(Executable)){
            await Error.WriteLineAsync($"The TopDown template is not built: {Executable}").ConfigureAwait(false);
            return 2;
        }
        var Evidence=Path.Combine(RepositoryRoot,".artifacts","feature-evidence","rigid.character-controller","TopDown",Adapter);
        return await RunGameAsync(Executable,Evidence,Adapter,Output,Error,CancellationToken).ConfigureAwait(false);
    }
    /// <summary>Runs a built TopDown game twice (straight with the navigation mesh switched off, then along the navigation mesh) and judges both reports.</summary>
    public static async Task<int>RunGameAsync(string Executable,string Evidence,string Adapter,TextWriter Output,TextWriter Error,CancellationToken CancellationToken){
        var Straight=await RunOnceAsync(Executable,Evidence,Adapter,false,Output,Error,CancellationToken).ConfigureAwait(false);
        var Navigated=await RunOnceAsync(Executable,Path.Combine(Evidence,"navigation"),Adapter,true,Output,Error,CancellationToken).ConfigureAwait(false);
        var Held=await RunOnceAsync(Executable,Path.Combine(Evidence,"long-press"),Adapter,true,Output,Error,CancellationToken,true).ConfigureAwait(false);
        return Straight!=0?Straight:Navigated!=0?Navigated:Held;
    }
    private static async Task<int>RunOnceAsync(string Executable,string Evidence,string Adapter,bool Navigated,TextWriter Output,TextWriter Error,CancellationToken CancellationToken,bool LongPress=false){
        Directory.CreateDirectory(Evidence);
        var ReportPath=Path.Combine(Evidence,"report.json");
        File.Delete(ReportPath);
        var Start=new ProcessStartInfo(Executable) {
            WorkingDirectory=Path.GetDirectoryName(Executable)!,
            UseShellExecute=false,
            RedirectStandardError=true,
            RedirectStandardOutput=true,
        };
        Start.Environment["CGE_GRAPHICS_ADAPTER"]=Adapter;
        Start.Environment["CGE_AUTOMATION_FRAMES"]=(LongPress?LongPressFrames:Frames).ToString(CultureInfo.InvariantCulture);
        Start.Environment["CGE_AUTOMATION_INPUT"]=LongPress?LongPressSchedule:InputSchedule;
        Start.Environment["CGE_AUTOMATION_CURSOR"]=Cursor;
        Start.Environment["CGE_AUTOMATION_REPORT"]=ReportPath;
        Start.Environment["CGE_AUTOMATION_CAPTURE"]=Path.Combine(Evidence,"final.png");
        // The same input twice: a world without the navigation mesh is the straight walk of before M0106.
        if(Navigated){ Start.Environment.Remove("CGE_NAVIGATION_DISABLED"); }
        else { Start.Environment["CGE_NAVIGATION_DISABLED"]="1"; }
        var Label=LongPress?"long press":Navigated?"navigated":"straight";
        using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Could not start the TopDown template.");
        var Standard=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var Diagnostics=Process.StandardError.ReadToEndAsync(CancellationToken);
        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
        await Output.WriteAsync(await Standard.ConfigureAwait(false)).ConfigureAwait(false);
        await Error.WriteAsync(await Diagnostics.ConfigureAwait(false)).ConfigureAwait(false);
        if(Process.ExitCode!=0||!File.Exists(ReportPath)){
            await Error.WriteLineAsync($"The TopDown template ({Label}) exited with {Process.ExitCode} and report present={File.Exists(ReportPath)}.").ConfigureAwait(false);
            return 1;
        }
        using var Report=JsonDocument.Parse(await File.ReadAllBytesAsync(ReportPath,CancellationToken).ConfigureAwait(false));
        var Failures=LongPress?EvaluateLongPress(Report.RootElement,out var Summary):Navigated?EvaluateNavigated(Report.RootElement,out Summary):Evaluate(Report.RootElement,out Summary);
        await Output.WriteLineAsync((LongPress?"CGE-TOPDOWN-LONG-PRESS ":Navigated?"CGE-TOPDOWN-CLICK-NAVIGATED ":"CGE-TOPDOWN-CLICK ")+Summary).ConfigureAwait(false);
        foreach(var Failure in Failures){
            await Error.WriteLineAsync(Failure).ConfigureAwait(false);
        }
        return Failures.Count==0?0:1;
    }
    /// <summary>ナビメッシュを切った(直線の)歩行のレポートを判定し、失敗理由を返す(空なら合格)。<paramref name="Summary"/>は観測値の要約。</summary>
    public static IReadOnlyList<string>Evaluate(JsonElement Report,out string Summary)=>EvaluateCore(Report,false,out Summary);
    /// <summary>
    /// ナビメッシュで歩いた(M0106)レポートを判定する。直線の歩行と同じ判定(着いて止まる、足音、目印)に加えて、台のふちから飛び降りずにスロープを下り、
    /// 壁の端を回って、決まった床の位置に着いていること。位置は、出発点(クリックの前のフレーム)のカメラからの動きで測る。
    /// </summary>
    public static IReadOnlyList<string>EvaluateNavigated(JsonElement Report,out string Summary)=>EvaluateCore(Report,true,out Summary);
    /// <summary>
    /// 長押し(M0108)のレポートを判定する。押している間に<see cref="MinimumLongPressWalkMeters"/>以上歩き(カーソルを追って歩き続ける)、離したあとは
    /// <see cref="MaximumWalkAfterReleaseMeters"/>以内で止まり(目的地まで歩き続けない)、最後に動いていないこと。目印は出ない(短押しだけが出す)。
    /// </summary>
    public static IReadOnlyList<string>EvaluateLongPress(JsonElement Report,out string Summary){
        List<string>Failures=[];
        Summary=string.Empty;
        if(!Report.TryGetProperty("schema",out var Schema)||Schema.GetString()!="cge-automation-report/1"){
            Failures.Add("The automation report schema is not cge-automation-report/1.");
            return Failures;
        }
        var Errors=Report.GetProperty("d3d12Errors").GetInt32();
        if(Errors!=0){
            Failures.Add($"The D3D12 debug layer reported {Errors} error(s).");
        }
        var Positions=CharacterPositions(Report);
        if(Positions.Length<LongPressFrames||Positions.Skip(ClickStartFrame-1).Any(static Position=>Position is null)){
            Failures.Add($"Expected {LongPressFrames} frames with a character position from frame {ClickStartFrame-1}, found {Positions.Length}.");
            return Failures;
        }
        var Held=Planar(Positions[ClickStartFrame-1]!,Positions[LongPressEndFrame-1]!);
        var After=Planar(Positions[LongPressEndFrame-1]!,Positions[LongPressFrames-1]!);
        var Drift=Planar(Positions[LongPressFrames-SettledFrames-1]!,Positions[LongPressFrames-1]!);
        // Still walking at the end of the hold: the last 5 frames moved, so it followed the cursor rather than arrived somewhere.
        var StillWalking=Planar(Positions[LongPressEndFrame-6]!,Positions[LongPressEndFrame-1]!);
        Summary=string.Create(CultureInfo.InvariantCulture,$"walkWhileHeld={Held:F3} walkAfterRelease={After:F3} walkingAtRelease={StillWalking:F3} finalDrift={Drift:F3} d3d12Errors={Errors}");
        if(Held<MinimumLongPressWalkMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Holding the button walked the character {Held:F3} m; expected at least {MinimumLongPressWalkMeters} m toward the cursor."));
        }
        if(StillWalking<0.5f){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The character moved only {StillWalking:F3} m in the last 5 frames of the hold; it stopped following the cursor."));
        }
        if(After>MaximumWalkAfterReleaseMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The character walked {After:F3} m after the button was released; a long press must only brake (at most {MaximumWalkAfterReleaseMeters} m), not walk on to a destination."));
        }
        if(Drift>=MaximumFinalDriftMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The character still moved {Drift:F3} m in the last {SettledFrames} frames."));
        }
        if(Report.TryGetProperty("renderObjectCounts",out var CountsElement)&&CountsElement.ValueKind==JsonValueKind.Array){
            var Counts=CountsElement.EnumerateArray().Select(static Item=>Item.GetInt32()).ToArray();
            if(Counts.Length>=LongPressFrames){
                var Before=Counts[ClickStartFrame-1];
                var Peak=Counts.Skip(ClickStartFrame).Max();
                Summary+=string.Create(CultureInfo.InvariantCulture,$" renderObjects={Before}->{Peak}");
                if(Peak>Before){
                    Failures.Add($"A long press showed a marker: the renderer drew {Before} objects before the click and {Peak} at most after it; only a short press makes one.");
                }
            }
        }
        return Failures;
    }
    private static IReadOnlyList<string>EvaluateCore(JsonElement Report,bool Navigated,out string Summary){
        List<string>Failures=[];
        Summary=string.Empty;
        if(!Report.TryGetProperty("schema",out var Schema)||Schema.GetString()!="cge-automation-report/1"){
            Failures.Add("The automation report schema is not cge-automation-report/1.");
            return Failures;
        }
        var Errors=Report.GetProperty("d3d12Errors").GetInt32();
        if(Errors!=0){
            Failures.Add($"The D3D12 debug layer reported {Errors} error(s).");
        }
        var Positions=CharacterPositions(Report);
        if(Positions.Length<Frames||Positions.Skip(ClickStartFrame-1).Any(static Position=>Position is null)){
            Failures.Add($"Expected {Frames} frames with a character position from frame {ClickStartFrame-1}, found {Positions.Length}.");
            return Failures;
        }
        var Walk=Planar(Positions[ClickStartFrame-1]!,Positions[Frames-1]!);
        var Drift=Planar(Positions[Frames-SettledFrames-1]!,Positions[Frames-1]!);
        var Audio=Report.TryGetProperty("audioRequests",out var AudioElement)?AudioElement.GetInt32():-1;
        var AudioSuffix=Audio<0?string.Empty:string.Create(CultureInfo.InvariantCulture,$" audioRequests={Audio}");
        Summary=string.Create(CultureInfo.InvariantCulture,$"walk={Walk:F3} finalDrift={Drift:F3} d3d12Errors={Errors}{AudioSuffix}");
        if(Walk<MinimumWalkMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"Clicking the ground moved the camera {Walk:F3} m horizontally; expected at least {MinimumWalkMeters} m."));
        }
        var WalkLimit=Navigated?MaximumNavigatedWalkMeters:MaximumWalkMeters;
        if(Walk>WalkLimit){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The camera moved {Walk:F3} m horizontally; expected at most {WalkLimit} m (the destination is a few metres away)."));
        }
        if(Drift>=MaximumFinalDriftMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The camera still moved {Drift:F3} m in the last {SettledFrames} frames; the character did not stop at the destination."));
        }
        if(Audio==0){
            Failures.Add("Walking to the destination asked for no footstep sound.");
        }
        if(Navigated){ EvaluateRoute(Positions,Failures,ref Summary); }
        EvaluateMarker(Report,Failures,ref Summary);
        return Failures;
    }
    /// <summary>
    /// The route along the navigation mesh (ADR-0079/M0106), from the camera: it ends at the expected floor position, wraps the wall end of the ramp corridor, is
    /// longer than the straight line between its ends, and never drops like a fall from the platform.
    /// </summary>
    private static void EvaluateRoute(float[]?[]Positions,List<string>Failures,ref string Summary){
        var Origin=Positions[ClickStartFrame-1]!;
        var Path=Enumerable.Range(ClickStartFrame-1,Frames-ClickStartFrame+1).Select(Frame=>(R:Positions[Frame]![0]-Origin[0],F:Positions[Frame]![2]-Origin[2])).ToArray();
        var Final=Path[^1];
        var Length=0f;
        for(var Index=1;Index<Path.Length;Index++){ Length+=MathF.Sqrt(((Path[Index].R-Path[Index-1].R)*(Path[Index].R-Path[Index-1].R))+((Path[Index].F-Path[Index-1].F)*(Path[Index].F-Path[Index-1].F))); }
        var Straight=MathF.Sqrt((Final.R*Final.R)+(Final.F*Final.F));
        var WallEnd=Path.Min(Point=>MathF.Sqrt(((Point.R-WallEndR)*(Point.R-WallEndR))+((Point.F-WallEndF)*(Point.F-WallEndF))));
        var Drop=0f;
        for(var Frame=ClickStartFrame-1;Frame<Frames-1;Frame++){ Drop=MathF.Max(Drop,Positions[Frame]![1]-Positions[Frame+1]![1]); }
        var Goal=MathF.Sqrt(((Final.R-NavigatedGoalR)*(Final.R-NavigatedGoalR))+((Final.F-NavigatedGoalF)*(Final.F-NavigatedGoalF)));
        Summary+=string.Create(CultureInfo.InvariantCulture,$" final=({Final.R:F2},{Final.F:F2}) goalError={Goal:F2} pathLength={Length:F2} straight={Straight:F2} wallEnd={WallEnd:F2} maxFrameDrop={Drop:F2}");
        if(Goal>NavigatedGoalTolerance){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The character stopped at ({Final.R:F2}, {Final.F:F2}) from where it started; the route ends at ({NavigatedGoalR}, {NavigatedGoalF}) within {NavigatedGoalTolerance} m."));
        }
        if(WallEnd>WallEndTolerance){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The route never came within {WallEndTolerance} m of the wall end ({WallEndR}, {WallEndF}) (closest {WallEnd:F2} m); it did not go around the wall."));
        }
        if(Length<Straight*MinimumDetourRatio){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The route was {Length:F2} m long for a straight distance of {Straight:F2} m; a route around the wall is at least {MinimumDetourRatio} times longer."));
        }
        if(Drop>MaximumFrameDropMeters){
            Failures.Add(string.Create(CultureInfo.InvariantCulture,$"The camera dropped {Drop:F2} m in one frame; walking down the ramp drops at most {MaximumFrameDropMeters} m, so the character fell off the platform."));
        }
    }
    /// <summary>
    /// ADR-0078/M0105: the click puts a marker in the world (the cursor burst) and its lifetime takes it out again. The renderer's object count per frame
    /// shows it: above what it was before the click at some frame after it, and back to that count at the end. A report without the counts (an older
    /// run) is not judged on the marker.
    /// </summary>
    private static void EvaluateMarker(JsonElement Report,List<string>Failures,ref string Summary){
        if(!Report.TryGetProperty("renderObjectCounts",out var CountsElement)||CountsElement.ValueKind!=JsonValueKind.Array){
            return;
        }
        var Counts=CountsElement.EnumerateArray().Select(static Item=>Item.GetInt32()).ToArray();
        if(Counts.Length<Frames){
            Failures.Add($"Expected {Frames} render object counts, found {Counts.Length}.");
            return;
        }
        var Before=Counts[ClickStartFrame-1];
        var Peak=Counts.Skip(ClickStartFrame).Max();
        var After=Counts[^1];
        var FirstUp=Array.FindIndex(Counts,ClickStartFrame,Count=>Count>Before);
        var Gone=FirstUp<0?-1:Array.FindIndex(Counts,FirstUp,Count=>Count<=Before);
        Summary+=string.Create(CultureInfo.InvariantCulture,$" renderObjects={Before}->{Peak}->{After} markerFrames={(FirstUp<0?0:(Gone<0?Counts.Length:Gone)-FirstUp)}");
        if(Peak<=Before){
            Failures.Add($"Clicking the ground showed no marker: the renderer drew {Before} objects before the click and never more after it.");
        } else if(FirstUp>=0&&FirstUp<ClickEndFrame){
            // M0108: UE spawns the cursor effect in MoveTo, which only a short press's release calls; it does not appear when the button goes down.
            Failures.Add($"The marker appeared at frame {FirstUp}, while the button was still down (released at frame {ClickEndFrame}); it must appear when a short press is released.");
        } else if(After!=Before){
            Failures.Add($"The marker did not go away: the renderer drew {Before} objects before the click and {After} at the end.");
        }
    }
    /// <summary>
    /// Where the character was each frame: the position of the Entity the camera hangs from (<c>focusPositions</c>, M0107), because the camera lags behind the
    /// character (UE's CameraLagSpeed 3) and no longer shows its path; a report without them (an older run) falls back to the camera.
    /// </summary>
    private static float[]?[]CharacterPositions(JsonElement Report){
        var Name=Report.TryGetProperty("focusPositions",out var Focus)&&Focus.ValueKind==JsonValueKind.Array&&Focus.EnumerateArray().Skip(ClickStartFrame-1).Any(static Item=>Item.ValueKind==JsonValueKind.Array)?"focusPositions":"cameraPositions";
        return [.. Report.GetProperty(Name).EnumerateArray().Select(static Item=>Item.ValueKind==JsonValueKind.Array?(float[]?)[.. Item.EnumerateArray().Select(static Value=>Value.GetSingle())]:null)];
    }
    private static float Planar(float[]From,float[]To)=>MathF.Sqrt(((To[0]-From[0])*(To[0]-From[0]))+((To[2]-From[2])*(To[2]-From[2])));
}
