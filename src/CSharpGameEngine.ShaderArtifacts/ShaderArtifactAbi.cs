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
    };
    private static readonly IReadOnlyList<ShaderArtifactEntryContract>ModelEntries=new[] {
        new ShaderArtifactEntryContract("VSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("PSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
    };
    private static readonly IReadOnlyList<ShaderArtifactEntryContract>TriangleEntries=new[] {
        new ShaderArtifactEntryContract("VSMain","vs_6_0",ShaderArtifactEntryStage.Vertex),
        new ShaderArtifactEntryContract("PSMain","ps_6_0",ShaderArtifactEntryStage.Pixel),
    };
    public static string GetHash(ShaderArtifactTargetKind Target) {
        var Contract=Target switch {
            ShaderArtifactTargetKind.Stage=>"stage-v1:b0-frame,b1-object,t0-base,t1-normal,t2-metalrough,t3-emissive,t4-shadow,t5-lights,t6-tiles,t7-spot,t8-point,t9-area-meta,t10-area-projection,s0-linear,s1-shadow:VSMain,PSMain,VSShadowMain,FullscreenVS,PostProcessPS",
            ShaderArtifactTargetKind.Model=>"model-v1:b0-transform,t0-base,s0-base:VSMain,PSMain",
            ShaderArtifactTargetKind.Triangle=>"triangle-v1:no-resources:VSMain,PSMain",
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
            _=>throw new ArgumentOutOfRangeException(nameof(Target)),
        };
    }
}