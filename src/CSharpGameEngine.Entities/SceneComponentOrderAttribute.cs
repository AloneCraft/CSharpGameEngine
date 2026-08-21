using System;

namespace CSharpGameEngine.Entities;

/// <summary>
/// Stable functional categories used to present and normalize Scene components.
/// The numeric values are part of the authoring contract: changing their relative order would
/// rewrite component collections even when no component type changed.
/// </summary>
public enum SceneComponentCategory {
    /// <summary>Spatial pose, hierarchy, and other coordinate-space data.</summary>
    Spatial=0,

    /// <summary>Camera and view-projection data.</summary>
    Camera=1,

    /// <summary>Lighting and environment data.</summary>
    LightingEnvironment=2,

    /// <summary>Mesh, material, and other rendering data.</summary>
    Rendering=3,

    /// <summary>Physics bodies, colliders, and constraints.</summary>
    Physics=4,

    /// <summary>Audio emitters, listeners, and routing data.</summary>
    Audio=5,

    /// <summary>Game-specific authored behavior.</summary>
    Gameplay=6,

    /// <summary>Generated or transient runtime companion components.</summary>
    Runtime=7,
}

/// <summary>
/// Declares a component's deterministic Scene ordering key.
/// Components without this attribute are normalized as <see cref="SceneComponentCategory.Gameplay"/>
/// so existing game components preserve a stable, non-breaking default.
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class SceneComponentOrderAttribute:Attribute {
    /// <summary>Initializes a category and its explicit order within that category.</summary>
    public SceneComponentOrderAttribute(SceneComponentCategory Category,int Order){
        this.Category=Category;
        this.Order=Order;
    }

    /// <summary>Functional category, compared before <see cref="Order"/>.</summary>
    public SceneComponentCategory Category { get; }

    /// <summary>Stable ordering value within <see cref="Category"/>.</summary>
    public int Order { get; }
}
