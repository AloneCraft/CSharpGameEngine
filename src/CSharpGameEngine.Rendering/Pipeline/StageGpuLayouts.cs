using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace CSharpGameEngine.Rendering.Pipeline;
[StructLayout(LayoutKind.Sequential)]
public struct StageInt4 {
    public int X;
    public int Y;
    public int Z;
    public int W;
}
[StructLayout(LayoutKind.Sequential)]
public struct StageUInt4 {
    public uint X;
    public uint Y;
    public uint Z;
    public uint W;
}
[StructLayout(LayoutKind.Sequential)]
public struct StageDirectionalLightData {
    public Vector4 DirectionAndShadowBias;
    public Vector4 ColorTimesIntensity;
}
[InlineArray(4)]
public struct StageFloat4Array4 {
    private Vector4 Element0;
}
[InlineArray(4)]
public struct StageMatrixArray4 {
    private Matrix4x4 Element0;
}
[InlineArray(4)]
public struct StageDirectionalLightArray4 {
    private StageDirectionalLightData Element0;
}
[StructLayout(LayoutKind.Sequential)]
public struct StageFrameConstants {
    public Matrix4x4 ViewProjection;
    public Vector4 CameraPositionWS;
    public StageDirectionalLightArray4 DirectionalLights;
    public Vector4 AmbientColor;
    public Vector4 FogParams0;
    public Vector4 FogColor;
    public Vector4 ExposureParams;
    public StageInt4 LightCounts;
    public StageUInt4 ForwardPlusGrid;
    public Matrix4x4 InverseViewProjection;
    public Vector4 ViewportSize;
    public Vector4 SkyLightParams;
    public Vector4 PhysicalFogParams;
    public Vector4 PhysicalFogColor;
    public StageMatrixArray4 CascadeViewProjection;
    public StageFloat4Array4 CascadeSpheres;
    public StageFloat4Array4 CascadeUvScaleOffsets;
    public Vector4 CascadeDepthRanges;
    public Vector4 GiGridOrigin;
    public Vector4 GiGridSpacing;
    public StageUInt4 GiGridCounts;
    public Vector4 GiAtlasSize;
    public Vector4 AmbientOcclusionParams;
    public Vector4 DepthEncoding0;
    public Vector4 DepthEncoding1;
    public Vector4 LumenParams;
}
[StructLayout(LayoutKind.Sequential)]
public struct StageObjectConstants {
    public Matrix4x4 World;
    public Vector4 BaseColorFactor;
    public Vector4 MaterialParams;
    public Vector4 EmissiveNits;
    public Vector4 WorldGridParams;
    public Vector4 WorldGridColor;
    public Vector4 WorldSubGridColor;
    public Vector4 WorldGridAnchor;
    public Vector4 ConservativeRasterDepth;
}
/// <summary>Managed layout authority for the Stage shader ABI. Offsets come from the transported structures.</summary>
public static class StageGpuLayouts {
    public static readonly uint FrameConstantsSize=checked((uint)Marshal.SizeOf<StageFrameConstants>());
    public static readonly uint ObjectConstantsSize=checked((uint)Marshal.SizeOf<StageObjectConstants>());
    public static readonly uint FrameViewProjectionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.ViewProjection));
    public static readonly uint FrameCameraPositionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CameraPositionWS));
    public static readonly uint FrameDirectionalLightsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.DirectionalLights));
    public static readonly uint FrameAmbientColorOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.AmbientColor));
    public static readonly uint FrameFogParamsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.FogParams0));
    public static readonly uint FrameFogColorOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.FogColor));
    public static readonly uint FrameExposureOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.ExposureParams));
    public static readonly uint FrameLightCountsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.LightCounts));
    public static readonly uint FrameForwardPlusGridOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.ForwardPlusGrid));
    public static readonly uint FrameInverseViewProjectionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.InverseViewProjection));
    public static readonly uint FrameViewportSizeOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.ViewportSize));
    public static readonly uint FrameSkyLightParamsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.SkyLightParams));
    public static readonly uint FramePhysicalFogParamsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.PhysicalFogParams));
    public static readonly uint FramePhysicalFogColorOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.PhysicalFogColor));
    public static readonly uint FrameCascadeViewProjectionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CascadeViewProjection));
    public static readonly uint FrameCascadeSpheresOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CascadeSpheres));
    public static readonly uint FrameCascadeUvScaleOffsetsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CascadeUvScaleOffsets));
    public static readonly uint FrameCascadeDepthRangesOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.CascadeDepthRanges));
    public static readonly uint FrameGiGridOriginOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.GiGridOrigin));
    public static readonly uint FrameGiGridSpacingOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.GiGridSpacing));
    public static readonly uint FrameGiGridCountsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.GiGridCounts));
    public static readonly uint FrameGiAtlasSizeOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.GiAtlasSize));
    public static readonly uint FrameAmbientOcclusionOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.AmbientOcclusionParams));
    public static readonly uint FrameDepthEncoding0Offset=Offset<StageFrameConstants>(nameof(StageFrameConstants.DepthEncoding0));
    public static readonly uint FrameDepthEncoding1Offset=Offset<StageFrameConstants>(nameof(StageFrameConstants.DepthEncoding1));
    public static readonly uint FrameLumenParamsOffset=Offset<StageFrameConstants>(nameof(StageFrameConstants.LumenParams));
    public static readonly uint ObjectWorldOffset=Offset<StageObjectConstants>(nameof(StageObjectConstants.World));
    public static readonly uint ObjectBaseColorFactorOffset=Offset<StageObjectConstants>(nameof(StageObjectConstants.BaseColorFactor));
    public static readonly uint ObjectMaterialParamsOffset=Offset<StageObjectConstants>(nameof(StageObjectConstants.MaterialParams));
    public static readonly uint ObjectEmissiveFactorOffset=Offset<StageObjectConstants>(nameof(StageObjectConstants.EmissiveNits));
    public static readonly uint ObjectWorldGridParamsOffset=Offset<StageObjectConstants>(nameof(StageObjectConstants.WorldGridParams));
    public static readonly uint ObjectWorldGridColorOffset=Offset<StageObjectConstants>(nameof(StageObjectConstants.WorldGridColor));
    public static readonly uint ObjectWorldSubGridColorOffset=Offset<StageObjectConstants>(nameof(StageObjectConstants.WorldSubGridColor));
    public static readonly uint ObjectWorldGridAnchorOffset=Offset<StageObjectConstants>(nameof(StageObjectConstants.WorldGridAnchor));
    public static readonly uint ObjectConservativeRasterDepthOffset=Offset<StageObjectConstants>(nameof(StageObjectConstants.ConservativeRasterDepth));
    private static uint Offset<T>(string Name) where T:struct=>checked((uint)Marshal.OffsetOf<T>(Name).ToInt64());
}
