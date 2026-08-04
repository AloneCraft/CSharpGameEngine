namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Authoring/Build-time validation of an <see cref="ICoordinateConvention"/>'s declared physical
/// basis - not called on any hot path (see Docs/Specifications/Spatial-RUF.md "エラー処理").
/// </summary>
public static class CoordinateBasisValidator
{
    private const float LengthTolerance = 1e-4f;
    private const float OrthogonalityTolerance = 1e-4f;

    /// <summary>
    /// Throws <see cref="SpatialConfigurationException"/> if <typeparamref name="TConvention"/>'s
    /// Right/Up/Forward axes are not unit length, not mutually orthogonal, non-finite, or
    /// duplicate the same physical axis.
    /// </summary>
    public static void Validate<TConvention>()
        where TConvention : struct, ICoordinateConvention
    {
        XyzVector right = TConvention.RightXyz;
        XyzVector up = TConvention.UpXyz;
        XyzVector forward = TConvention.ForwardXyz;
        string conventionName = typeof(TConvention).Name;

        ValidateFinite(right, conventionName, nameof(ICoordinateConvention.RightXyz));
        ValidateFinite(up, conventionName, nameof(ICoordinateConvention.UpXyz));
        ValidateFinite(forward, conventionName, nameof(ICoordinateConvention.ForwardXyz));

        ValidateUnitLength(right, conventionName, nameof(ICoordinateConvention.RightXyz));
        ValidateUnitLength(up, conventionName, nameof(ICoordinateConvention.UpXyz));
        ValidateUnitLength(forward, conventionName, nameof(ICoordinateConvention.ForwardXyz));

        ValidateOrthogonal(right, up, conventionName, "Right", "Up");
        ValidateOrthogonal(up, forward, conventionName, "Up", "Forward");
        ValidateOrthogonal(forward, right, conventionName, "Forward", "Right");

        if (right == up || up == forward || forward == right)
        {
            throw new SpatialConfigurationException($"{conventionName}: Right/Up/Forward must not duplicate the same physical axis (Right={right}, Up={up}, Forward={forward}).");
        }
    }

    private static void ValidateFinite(XyzVector axis, string conventionName, string axisName)
    {
        if (!float.IsFinite(axis.X) || !float.IsFinite(axis.Y) || !float.IsFinite(axis.Z))
        {
            throw new SpatialConfigurationException($"{conventionName}.{axisName} must be finite, but was {axis}.");
        }
    }

    private static void ValidateUnitLength(XyzVector axis, string conventionName, string axisName)
    {
        if (MathF.Abs(axis.Length - 1f) > LengthTolerance)
        {
            throw new SpatialConfigurationException($"{conventionName}.{axisName} must be unit length, but was {axis} (length {axis.Length}).");
        }
    }

    private static void ValidateOrthogonal(XyzVector a, XyzVector b, string conventionName, string aName, string bName)
    {
        float dot = XyzVector.Dot(a, b);
        if (MathF.Abs(dot) > OrthogonalityTolerance)
        {
            throw new SpatialConfigurationException($"{conventionName}: {aName} and {bName} must be orthogonal, but their dot product was {dot}.");
        }
    }
}
