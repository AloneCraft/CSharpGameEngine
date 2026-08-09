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
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            RufVector vector = new(1.5f, 2.5f, 3.5f);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            string commaCultureText = vector.ToString();

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string invariantCultureText = vector.ToString();

            // Ruf(...) uses ", " as a fixed tuple separator regardless of culture; the invariance
            // this test is about is the decimal point inside each component (never a German ",").
            Assert.Equal(invariantCultureText, commaCultureText);
            Assert.DoesNotContain("1,5", commaCultureText, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ConventionConversion_IsCultureIndependent()
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            RufVector input = new(1.25f, -2.5f, 3.75f);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            XyzVector underGermanCulture = UnityCoordinateConvention.ToXyz(input);

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            XyzVector underInvariantCulture = UnityCoordinateConvention.ToXyz(input);

            Assert.Equal(underInvariantCulture, underGermanCulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
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
