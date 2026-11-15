using System.Numerics;
namespace CSharpGameEngine.Numerics;
internal interface IFixedMathScalar<T>:IAdditionOperators<T,T,T>,ISubtractionOperators<T,T,T>,IMultiplyOperators<T,T,T>,IDivisionOperators<T,T,T>,IModulusOperators<T,T,T>,IUnaryNegationOperators<T,T>,IComparisonOperators<T,T,bool> where T:struct,IFixedMathScalar<T> {
    bool IsZero{get;}
    static abstract int FractionalPrecision{get;}
    static abstract T Zero{get;}
    static abstract T One{get;}
    static abstract T MinValue{get;}
    static abstract T ParseInvariant(string Text);
    static abstract T FromRatio(long Numerator,long Denominator);
    static abstract bool TryAdd(T Left,T Right,out T Result);
    static abstract bool TrySubtract(T Left,T Right,out T Result);
    static abstract bool TryMultiply(T Left,T Right,out T Result);
    static abstract bool TryDivide(T Left,T Right,out T Result);
    static abstract bool TrySquareRoot(T Value,out T Result);
    static abstract bool TryMultiplyInteger(T Value,long Factor,out T Result);
    static abstract bool TryDivideInteger(T Value,long Divisor,out T Result);
}
