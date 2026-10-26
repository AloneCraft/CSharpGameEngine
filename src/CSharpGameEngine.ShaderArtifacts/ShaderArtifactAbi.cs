using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
namespace CSharpGameEngine.ShaderArtifacts;
public sealed class ShaderArtifactEntryContract {
    public ShaderArtifactEntryContract(string LogicalName,string Profile,ShaderArtifactEntryStage Stage) {
        this.LogicalName=LogicalName;
        this.Profile=Profile;
        this.Stage=Stage;
    }
    public string LogicalName { get; }
    public string Profile { get; }
    public ShaderArtifactEntryStage Stage { get; }
}
public static class ShaderArtifactAbi {
    private static readonly IReadOnlyList<ShaderArtifactEntryContract>StageEntries=new[] {
        new ShaderArtifactEntryContract("VSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("PSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("VSShadowMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("FullscreenVS","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("PostProcessPS","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("PreviewPSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("PreviewCleanPSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("OverlayVSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("OverlayPSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("ScreenVSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("ScreenPSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("ObserverVSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("SurfacePrepassPSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("LumenCardPSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
    };
    private static readonly IReadOnlyList<ShaderArtifactEntryContract>ComputeEntries=new[] {
        new ShaderArtifactEntryContract("CSMain","cs_6_0",ShaderArtifactEntryStage.Compute),
    };
    public static string GetHash(ShaderArtifactTargetKind Target) {
        var Contract=Target switch {
            ShaderArtifactTargetKind.Stage=>
                "stage-v16:mrt1-physical-scene-distance,b0-frame1248-depth-encoding-inverse-viewport-virtual-soft-shadows,b1-object-conservative-raster-depth,t0-base,t1-normal,t2-metalrough,t3-emissive,t4-shadow,t5-lights,t6-tiles,t7-spot,t8-point,t9-area-meta,t10-area-projection,t11-sky-irradiance-sh,t12-gi-irradiance,t13-gi-distance,t14-ambient-occlusion,t15-exposure-state,t16-lumen-diffuse,t17-lumen-reflection,t18-virtual-shadow,t19-virtual-pages,s0-anisotropic-wrap,s1-shadow,post-b0-params,screen-t0-texture,screen-b0-params:"
                +"VSMain,PSMain,VSShadowMain,FullscreenVS,PostProcessPS,PreviewPSMain,PreviewCleanPSMain,OverlayVSMain,OverlayPSMain,ScreenVSMain,ScreenPSMain,ObserverVSMain,SurfacePrepassPSMain,LumenCardPSMain:"
                +ComputeHash(StagePresentationShaderSource.Source),
            // Compute アーティファクトは共通のエントリー契約を持つ。各ディスパッチホストが所有するルートシグネチャを、
            // HLSL の隣に記載する（Atmosphere.hlsl と StageAtmosphere など）。
            ShaderArtifactTargetKind.Compute=>"compute-v2:CSMain:host-owned-root-signature",
            _=>throw new ArgumentOutOfRangeException(nameof(Target)),
        };
        using(var Hash=SHA256.Create()) {
            return BitConverter.ToString(Hash.ComputeHash(Encoding.UTF8.GetBytes(Contract))).Replace("-","");
        }
    }
    public static IReadOnlyList<ShaderArtifactEntryContract>GetEntries(ShaderArtifactTargetKind Target) {
        return Target switch {
            ShaderArtifactTargetKind.Stage=>StageEntries,
            ShaderArtifactTargetKind.Compute=>ComputeEntries,
            _=>throw new ArgumentOutOfRangeException(nameof(Target)),
        };
    }
    private static string ComputeHash(string Value) {
        using(var Hash=SHA256.Create()) {
            return BitConverter.ToString(Hash.ComputeHash(Encoding.UTF8.GetBytes(Value))).Replace("-","");
        }
    }
}
