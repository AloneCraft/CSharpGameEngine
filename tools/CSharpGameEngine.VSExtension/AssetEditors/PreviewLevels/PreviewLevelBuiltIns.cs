using System;
using System.Collections.Generic;
using System.Linq;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// コードが提供する標準のプレビューレベル（M0127）。ディスクには書かず、削除・上書き・名前変更はできない。カスタマイズするときは複製して編集する。
/// 識別子は予約済みで、プロジェクトのファイル名には使えない。
/// </summary>
internal static class PreviewLevelBuiltIns {
    internal const string DefaultId="default";
    internal const string StudioId="studio";
    internal const string OutdoorId="outdoor";
    internal const string NeutralId="neutral";
    internal static IReadOnlyList<string>ReservedIds=>[DefaultId,StudioId,OutdoorId,NeutralId];
    internal static bool IsReserved(string?Id)=>!string.IsNullOrEmpty(Id)&&ReservedIds.Contains(Id!,StringComparer.OrdinalIgnoreCase);
    internal static IReadOnlyList<PreviewLevelTemplate>All()=>[Default(),Studio(),Outdoor(),Neutral()];
    private static PreviewLevelTemplate Create(string Id,string NameKey,string DescriptionKey,Action<PreviewLevelTemplate>Configure){
        var Template=new PreviewLevelTemplate { Id=Id,BuiltIn=true,DisplayName=VsixText.Get(NameKey),Description=VsixText.Get(DescriptionKey) };
        Configure(Template);
        return Template;
    }
    /// <summary>モデル・マテリアルの確認向けの標準。ニュートラルな背景、キー・フィル・リムの 3 灯、影が落ちる床。</summary>
    internal static PreviewLevelTemplate Default()=>Create(DefaultId,"PreviewLevel_Default","PreviewLevel_DefaultDescription",static Template=>{
        Template.Background=new(){ Mode=PreviewBackgroundMode.Solid,Color=[0.17f,0.18f,0.2f] };
        Template.Ambient=new(){ Color=[0.9f,0.95f,1f],Intensity=0.14f };
        Template.Lights=[
            new(){ Kind=PreviewLightKind.Directional,Name="Key",Color=[1f,0.95f,0.88f],Intensity=3.2f,YawDegrees=190f,PitchDegrees=-40f,CastsShadows=true },
            new(){ Kind=PreviewLightKind.Directional,Name="Fill",Color=[0.85f,0.92f,1f],Intensity=0.9f,YawDegrees=295f,PitchDegrees=-15f },
            new(){ Kind=PreviewLightKind.Directional,Name="Rim",Color=[1f,1f,1f],Intensity=2f,YawDegrees=25f,PitchDegrees=-25f },
        ];
        Template.Floor=new(){ Visible=true,Color=[0.42f,0.42f,0.44f],Grid=true,GridColor=[0.2f,0.2f,0.22f],SubGridColor=[0.3f,0.3f,0.33f] };
    });
    /// <summary>
    /// 床・壁・天井のある部屋（サムネイルもこの環境で描く）。壁と天井も影を落とすので、平行ライトではなく室内の点ライトで照らす
    /// （平行ライトは天井で全部遮られる）。主灯は影を落とし、補助灯は影なし。
    /// </summary>
    internal static PreviewLevelTemplate Studio()=>Create(StudioId,"PreviewLevel_Studio","PreviewLevel_StudioDescription",static Template=>{
        Template.Background=new(){ Mode=PreviewBackgroundMode.Solid,Color=[0.17f,0.18f,0.2f] };
        Template.Ambient=new(){ Color=[0.9f,0.95f,1f],Intensity=0.3f };
        Template.Lights=[
            new(){ Kind=PreviewLightKind.Point,Name="Key",Color=[1f,0.95f,0.88f],Intensity=900f,PositionRadii=[-2.2f,4.2f,2.6f],RangeRadii=20f,CastsShadows=true },
            new(){ Kind=PreviewLightKind.Point,Name="Fill",Color=[0.85f,0.92f,1f],Intensity=220f,PositionRadii=[3.2f,2.4f,-2.4f],RangeRadii=20f },
        ];
        Template.Floor=new(){ Visible=true,SizeRadii=8f,Color=[0.42f,0.42f,0.44f],Grid=true,GridColor=[0.2f,0.2f,0.22f],SubGridColor=[0.3f,0.3f,0.33f] };
        Template.Room=new(){ Visible=true,HalfSizeRadii=8f,HeightRadii=7f };
    });
    /// <summary>屋外の照明確認向け。物理ベースの空とスカイライト、太陽に相当する方向ライト、地面と影（ThirdPerson テンプレートのレベルと同じ露出）。</summary>
    internal static PreviewLevelTemplate Outdoor()=>Create(OutdoorId,"PreviewLevel_Outdoor","PreviewLevel_OutdoorDescription",static Template=>{
        Template.Ambient=new(){ Color=[0f,0f,0f],Intensity=0f };
        Template.Lights=[new(){ Kind=PreviewLightKind.Directional,Name="Sun",Color=[1f,0.98f,0.94f],Intensity=3f,YawDegrees=200f,PitchDegrees=-40f,CastsShadows=true }];
        Template.Environment=new(){ Atmosphere=true,SkyLightIntensity=1f,PhysicalExposure=true,ExposureEv100=0f,ExposureCompensation=1f,FilmicToneMapper=true,BloomIntensity=0.675f,AmbientOcclusion=true };
        // 空は地平線の下に何も描かないので、床を遠方まで広げて地平線に接するようにする（遠方クリップ面も床に合わせて伸びる）。
        Template.Floor=new(){ Visible=true,SizeRadii=1000f,Color=[0.26f,0.23f,0.18f] };
    });
    /// <summary>
    /// ライティングの影響を最小にして形状を確認する環境。均一なアンビエントと弱い 1 灯だけで、影も床もない。
    /// 描画モードの Unlit（ベースカラー表示）とは別物で、照明は通常どおり計算される。
    /// </summary>
    internal static PreviewLevelTemplate Neutral()=>Create(NeutralId,"PreviewLevel_Neutral","PreviewLevel_NeutralDescription",static Template=>{
        Template.Background=new(){ Mode=PreviewBackgroundMode.Solid,Color=[0.5f,0.5f,0.5f] };
        Template.Ambient=new(){ Color=[1f,1f,1f],Intensity=0.65f };
        Template.Lights=[new(){ Kind=PreviewLightKind.Directional,Name="Soft",Intensity=0.7f,YawDegrees=190f,PitchDegrees=-40f }];
    });
}
