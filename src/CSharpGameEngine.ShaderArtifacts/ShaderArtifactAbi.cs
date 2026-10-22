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
        new ShaderArtifactEntryContract("RadialPSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("PreviewRadialPSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("PreviewCleanRadialPSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
        new ShaderArtifactEntryContract("RadialObserverVSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("RadialVSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
    };
    private static readonly IReadOnlyList<ShaderArtifactEntryContract>ModelEntries=new[] {
        new ShaderArtifactEntryContract("VSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("PSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
    };
    private static readonly IReadOnlyList<ShaderArtifactEntryContract>TriangleEntries=new[] {
        new ShaderArtifactEntryContract("VSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("PSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
    };
    private static readonly IReadOnlyList<ShaderArtifactEntryContract>ComputeEntries=new[] {
        new ShaderArtifactEntryContract("CSMain","cs_6_0",ShaderArtifactEntryStage.Compute),
    };
    public static string GetHash(ShaderArtifactTargetKind Target) {
        var Contract=Target switch {
            ShaderArtifactTargetKind.Stage=>
                "stage-v11:b0-frame-depth-mapping,b1-object-radial-raster-depth,t0-base,t1-normal,t2-metalrough,t3-emissive,t4-shadow,t5-lights,t6-tiles,t7-spot,t8-point,t9-area-meta,t10-area-projection,t11-sky-irradiance-sh,t12-gi-irradiance,t13-gi-distance,t14-ambient-occlusion,t15-exposure-state,s0-linear,s1-shadow,post-b0-params,screen-t0-texture,screen-b0-params:"
                +"VSMain,PSMain,VSShadowMain,FullscreenVS,PostProcessPS,PreviewPSMain,PreviewCleanPSMain,OverlayVSMain,OverlayPSMain,ScreenVSMain,ScreenPSMain,RadialPSMain,PreviewRadialPSMain,PreviewCleanRadialPSMain,RadialObserverVSMain,RadialVSMain:"
                +ComputeHash(StagePresentationShaderSource.Source),
            ShaderArtifactTargetKind.Model=>"model-v1:b0-transform,t0-base,s0-base:VSMain,PSMain",
            ShaderArtifactTargetKind.Triangle=>"triangle-v1:no-resources:VSMain,PSMain",
            // Compute artifacts share one entry contract; every host that dispatches one owns and documents
            // its root signature next to the HLSL (for example Atmosphere.hlsl and StageAtmosphere).
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
            ShaderArtifactTargetKind.Model=>ModelEntries,
            ShaderArtifactTargetKind.Triangle=>TriangleEntries,
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
