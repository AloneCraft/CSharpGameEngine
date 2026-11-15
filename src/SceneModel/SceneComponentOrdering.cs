using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.SceneAnalysis;
/// <summary>Component正規化に使うカテゴリ、明示順、完全修飾型名の比較キー。</summary>
internal readonly struct SceneComponentOrderKey(int Category,int Order,string FullyQualifiedName):IComparable<SceneComponentOrderKey> {
    internal int Category { get; }=Category;
    internal int Order { get; }=Order;
    internal string FullyQualifiedName { get; }=FullyQualifiedName??throw new ArgumentNullException(nameof(FullyQualifiedName));
    public int CompareTo(SceneComponentOrderKey Other){
        var CategoryComparison=this.Category.CompareTo(Other.Category);
        if(CategoryComparison!=0){
            return CategoryComparison;
        }
        var OrderComparison=this.Order.CompareTo(Other.Order);
        return OrderComparison!=0?OrderComparison:StringComparer.Ordinal.Compare(this.FullyQualifiedName,Other.FullyQualifiedName);
    }
}
/// <summary>Scene Componentの順序を属性付き・未注釈の双方で同じ規則へ正規化する。</summary>
internal static class SceneComponentOrdering {
    private const string OrderAttributeFullyQualifiedName="global::CSharpGameEngine.Entities.SceneComponentOrderAttribute";
    private const int MinimumCategory=0;
    private const int GameplayCategory=6;
    private const int MaximumCategory=7;
    internal static SceneComponentOrderKey GetSortKey(INamedTypeSymbol?TypeSymbol,string FullyQualifiedName){
        var Category=GameplayCategory;
        var Order=0;
        if(TypeSymbol is not null){
            foreach(var Attribute in TypeSymbol.GetAttributes()){
                if(Attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)!=OrderAttributeFullyQualifiedName){
                    continue;
                }
                if(Attribute.ConstructorArguments.Length>0&&TryReadInt(Attribute.ConstructorArguments[0],out var DeclaredCategory)&&DeclaredCategory>=MinimumCategory&&DeclaredCategory<=MaximumCategory){
                    Category=DeclaredCategory;
                }
                if(Attribute.ConstructorArguments.Length>1&&TryReadInt(Attribute.ConstructorArguments[1],out var DeclaredOrder)){
                    Order=DeclaredOrder;
                }
                break;
            }
        }
        return new SceneComponentOrderKey(Category,Order,FullyQualifiedName);
    }
    /// <summary>同型の後続宣言を除き、最初の宣言を残してから比較キー順に整列する。</summary>
    internal static ImmutableArray<SceneComponent>Normalize(ImmutableArray<SceneComponent>Components){
        HashSet<string>SeenTypes=new(StringComparer.Ordinal);
        List<SceneComponent>FirstDeclarations=[];
        foreach(var Component in Components){
            if(SeenTypes.Add(Component.TypeFullyQualifiedName)){
                FirstDeclarations.Add(Component);
            }
        }
        return FirstDeclarations.OrderBy(static Component=>Component.OrderKey.Category).ThenBy(static Component=>Component.OrderKey.Order).ThenBy(static Component=>Component.OrderKey.FullyQualifiedName,StringComparer.Ordinal).ToImmutableArray();
    }
    private static bool TryReadInt(TypedConstant Constant,out int Value){
        if(Constant.Value is int Integer){
            Value=Integer;
            return true;
        }
        try {
            if(Constant.Value is not null){
                Value=Convert.ToInt32(Constant.Value,System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
        } catch(InvalidCastException) {
        } catch(FormatException) {
        } catch(OverflowException) {
        }
        Value=0;
        return false;
    }
}
