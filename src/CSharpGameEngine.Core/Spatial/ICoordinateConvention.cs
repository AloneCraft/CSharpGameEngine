namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// One project preset's mapping between semantic <see cref="RufVector"/> and physical
/// <see cref="XyzVector"/> storage, plus its declared <see cref="CoordinateHandedness"/>.
/// Implementations must be a stateless <c>readonly struct</c> so <see cref="Spatial{TConvention}"/>
/// can resolve every member as a compile-time-monomorphized static call - no runtime branching,
/// no reflection, no boxing, no Service Locator/global mutable setting (see
/// Docs/Specifications/Spatial-RUF.md "パフォーマンス要件"/"コード品質").
/// </summary>
public interface ICoordinateConvention {
    static abstract CoordinateHandedness Handedness { get;}

    static abstract XyzVector ToXyz(in RufVector Value);

    static abstract RufVector FromXyz(in XyzVector Value);

    static abstract XyzVector RightXyz { get;}

    static abstract XyzVector UpXyz { get;}

    static abstract XyzVector ForwardXyz { get;}
}
