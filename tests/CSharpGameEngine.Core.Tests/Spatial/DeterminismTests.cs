using System.Globalization;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Unity;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>Docs/RUF-Semantic-Coordinate-Specification-Prompt.md "Determinism Tests".</summary>
public class DeterminismTests
{
    [Fact]
    public void ToString_IsCultureInvariant()
    {
        CultureInfo Original = CultureInfo.CurrentCulture;

        try
        {
            RufVector Vector = new(1.5f, 2.5f, 3.5f);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            string CommaCultureText = Vector.ToString();

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string InvariantCultureText = Vector.ToString();

            // Ruf(...) uses ", " as a fixed tuple separator regardless of culture; the invariance
            // this test is about is the decimal point inside each component (never a German ",").
            Assert.Equal(InvariantCultureText, CommaCultureText);
            Assert.DoesNotContain("1,5", CommaCultureText, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = Original;
        }
    }

    [Fact]
    public void ConventionConversion_IsCultureIndependent()
    {
        CultureInfo Original = CultureInfo.CurrentCulture;

        try
        {
            RufVector Input = new(1.25f, -2.5f, 3.75f);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            XyzVector UnderGermanCulture = UnityCoordinateConvention.ToXyz(Input);

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            XyzVector UnderInvariantCulture = UnityCoordinateConvention.ToXyz(Input);

            Assert.Equal(UnderInvariantCulture, UnderGermanCulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = Original;
        }
    }

    [Fact]
    public void RepeatedEvaluation_OfConventionBasis_IsIdentical()
    {
        Assert.Equal(UnityCoordinateConvention.RightXyz, UnityCoordinateConvention.RightXyz);
        Assert.Equal(UnityCoordinateConvention.UpXyz, UnityCoordinateConvention.UpXyz);
        Assert.Equal(UnityCoordinateConvention.ForwardXyz, UnityCoordinateConvention.ForwardXyz);
    }
}
