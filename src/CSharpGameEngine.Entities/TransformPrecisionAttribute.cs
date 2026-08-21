using System;

namespace CSharpGameEngine.Entities;

/// <summary>
/// Declares the fixed-point layout used by authored spatial data in this compilation.
/// The declaration deliberately lives on the assembly: a single Scene project must have one
/// unambiguous precision contract, and the incremental generator can then produce storage whose
/// limb count is fixed at compile time.
/// </summary>
/// <remarks>
/// The numeric constants passed to this attribute are source-of-truth data. They are not copied
/// into Visual Studio user settings, so changing them is a normal source change that causes an
/// incremental rebuild and regeneration.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly,AllowMultiple=false,Inherited=false)]
public sealed class TransformPrecisionAttribute:Attribute {
    /// <summary>Initializes one compilation-wide fixed-point layout declaration.</summary>
    public TransformPrecisionAttribute(int TotalBits,int FractionalBits){
        this.TotalBits=TotalBits;
        this.FractionalBits=FractionalBits;
    }

    /// <summary>Total signed storage width in bits.</summary>
    public int TotalBits { get; }

    /// <summary>Number of low-order fractional bits.</summary>
    public int FractionalBits { get; }
}
