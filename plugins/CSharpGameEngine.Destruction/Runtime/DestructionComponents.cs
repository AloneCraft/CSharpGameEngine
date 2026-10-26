using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Destruction;
/// <summary>Which fragments of a <see cref="Destructible"/> stay where they are, whatever breaks (UE's anchor field).</summary>
public enum DestructibleAnchor:byte {
    /// <summary>None: once the object comes apart, every piece falls.</summary>
    None,
    /// <summary>The fragments on the object's lowest face (within 1 cm): a wall or a pillar keeps its foot. Only for an object without a <c>RigidBody</c>.</summary>
    Bottom,
}
/// <summary>
/// An object that breaks (ADR-0078/M0113, UE's Geometry Collection). Put it on a scene node next to its <c>Collider</c> (a box, the source volume) and,
/// for an object that falls and tumbles, a <c>RigidBody</c>. The build cooks the node: it cuts the box (or <see cref="SourceMesh"/>, a closed convex mesh) into
/// <see cref="FragmentCount"/> Voronoi fragments with <see cref="Seed"/>, groups them into <see cref="ClusterCount"/> clusters (and those, level by level,
/// into <see cref="ClusterLevels"/> levels) and bonds every two touching fragments with a strength of the shared area times <see cref="BondStrength"/>,
/// into <c>Assets/Destruction/&lt;FractureAsset&gt;.cgefracture</c>. At run time the object is drawn as its fragments (<see cref="OuterColor"/> outside,
/// <see cref="InteriorColor"/> on the cuts), and the impulses of what hits it strain its bonds: a bond whose strain (with the internal strain earlier hits
/// left, fading at <see cref="StrainDecay"/>) reaches its strength breaks, the parts that come apart become bodies of their own (anchored ones stay still),
/// each piece is removed <see cref="FragmentLifetime"/> seconds after it came loose, and at most <see cref="FragmentBudget"/> loose pieces live at once (the
/// oldest go first). The node's scale must be one: the size is the collider's.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Spatial,22)]
public struct Destructible() {
    /// <summary>The name of the cooked asset (letters, digits, <c>-_.</c>): <c>Assets/Destruction/&lt;name&gt;.cgefracture</c>. Nodes that share it must share the source and the cut.</summary>
    public string FractureAsset=string.Empty;
    /// <summary>How many Voronoi fragments, 2 to 256.</summary>
    public int FragmentCount=DestructionDefaults.FragmentCount;
    /// <summary>Seeds the fragment sites: the same seed gives the same cut, byte for byte.</summary>
    public int Seed=0;
    /// <summary>How many top-level clusters the fragments are grouped into; 0 picks the nearest whole number to √<see cref="FragmentCount"/>.</summary>
    public int ClusterCount=0;
    /// <summary>
    /// Levels of clusters, 1 to 4 (M0124; UE's Geometry Collection levels). Each level splits every cluster of the level above into the nearest whole number to
    /// √(its fragments) clusters. With 2, the object first breaks into big chunks that each break into smaller ones before they break into fragments.
    /// </summary>
    public int ClusterLevels=DestructionDefaults.ClusterLevels;
    /// <summary>A closed convex <c>.glb</c> to cut instead of the collider's box, under the game's folder; empty uses the box.</summary>
    public string SourceMesh=string.Empty;
    /// <summary>kg/m³ of the fragments (wood about 500, concrete about 2400).</summary>
    public LogicalScalar Density=LogicalScalar.FromInt64(DestructionDefaults.Density);
    /// <summary>The strain a square metre of bond takes before it breaks, in N·s/m² (a strain is an impulse: a 100 kg character at 6 m/s brings 600 N·s).</summary>
    public LogicalScalar BondStrength=LogicalScalar.FromInt64(DestructionDefaults.BondStrength);
    /// <summary>
    /// How much stronger a bond inside one cluster is than a bond between clusters, once per level whose cluster holds both its fragments, so a hit frees
    /// whole top-level clusters first, then their sub-clusters, then fragments.
    /// </summary>
    public LogicalScalar ClusterStrengthScale=LogicalScalar.FromInt64(2);
    /// <summary>
    /// How fast the internal strain of earlier hits fades, per second (M0124; UE's Chaos internal strain). Each tick a bond keeps max(0, 1 − decay × Δt) of it,
    /// so repeated hits close together break what one does not. 0 never forgets; 60 or more (at 60 ticks/s) forgets at once, as M0113 did. The steady load of
    /// a body resting on the object does not add to it.
    /// </summary>
    public LogicalScalar StrainDecay=LogicalScalar.One/LogicalScalar.FromInt64(2);
    /// <summary>The share (0 to 1) of an impulse that reaches the bonds one fragment further from the hit, ring by ring.</summary>
    public LogicalScalar DamagePropagation=LogicalScalar.One/LogicalScalar.FromInt64(2);
    public DestructibleAnchor Anchor=DestructibleAnchor.Bottom;
    /// <summary>At most this many loose (moving) pieces live at once; the oldest are removed first. 0 or less: no limit.</summary>
    public int FragmentBudget=64;
    /// <summary>Seconds a loose piece lives after it came free; 0 or less keeps it.</summary>
    public LogicalScalar FragmentLifetime=LogicalScalar.FromInt64(10);
    /// <summary>Linear RGB of the object's outside (as <c>ScreenText</c>'s colour, one field per channel).</summary>
    public float OuterColorR=0.6f;
    public float OuterColorG=0.6f;
    public float OuterColorB=0.6f;
    /// <summary>Linear RGB of the cut faces (UE's interior material).</summary>
    public float InteriorColorR=0.4f;
    public float InteriorColorG=0.38f;
    public float InteriorColorB=0.35f;
    internal readonly Vector3 OuterColor=>new(this.OuterColorR,this.OuterColorG,this.OuterColorB);
    internal readonly Vector3 InteriorColor=>new(this.InteriorColorR,this.InteriorColorG,this.InteriorColorB);
}
/// <summary>The run-time state of a <see cref="Destructible"/>, attached with it. Value fields only, for the game and the tests to read.</summary>
public struct DestructibleState:IRuntimeComponent<Destructible> {
    /// <summary>Loading was tried (it is tried once).</summary>
    public bool Attempted;
    /// <summary>The cooked asset is loaded and the fragments are drawn.</summary>
    public bool Loaded;
    /// <summary>A bond has broken (the authored body is gone and the pieces are bodies of their own).</summary>
    public bool Broken;
    public int Fragments;
    public int BrokenBonds;
    /// <summary>The live pieces (anchored and loose).</summary>
    public int Pieces;
    /// <summary>The live loose (moving) pieces.</summary>
    public int LoosePieces;
    /// <summary>Why the object cannot break (the asset is missing or wrong, the scale is not one); null when it can.</summary>
    public string?Problem;
}
/// <summary>A body made from pieces of a broken <see cref="Destructible"/>: the destructible's Entity and the piece's number in its <c>FractureState</c>.</summary>
public struct DestructiblePiece {
    public Entity Root;
    public int Piece;
}
/// <summary>
/// One drawn fragment of a <see cref="Destructible"/>: it follows <see cref="Body"/> (the destructible until it breaks, then its piece's body) at
/// <see cref="Offset"/> in that body's frame.
/// </summary>
public struct DestructibleFragment {
    public Entity Root;
    public int Fragment;
    public Entity Body;
    public LogicalVector3 Offset;
}
/// <summary>
/// Lets a character break what it runs into (ADR-0078/M0113; UE's character pushes physics bodies with its mass). Put it on a node with a
/// <c>CharacterMotion</c>. When the character moves into a destructible (or a piece of one) faster than <see cref="MinimumSpeed"/>, the hit point takes the
/// impulse <see cref="Mass"/> × the approach speed, and a loose piece is pushed by it.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,40)]
public struct DestructionImpact() {
    /// <summary>kg (UE's CharacterMovement Mass is 100).</summary>
    public LogicalScalar Mass=LogicalScalar.FromInt64(100);
    /// <summary>m/s; slower approaches (leaning on a wall) neither strain nor push.</summary>
    public LogicalScalar MinimumSpeed=LogicalScalar.One;
}
/// <summary>
/// A strain field (ADR-0078/M0113, UE's External Strain field): the bonds within <see cref="Radius"/> of the node take <see cref="Strain"/> (N·s), less with
/// distance down to nothing at the radius. It acts at the first fixed tick it exists, or every tick when <see cref="Repeat"/>. With a
/// <see cref="ForceField"/> and a <c>Lifetime</c> it makes an explosion.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,41)]
public struct StrainField() {
    public LogicalScalar Radius=LogicalScalar.FromInt64(2);
    public LogicalScalar Strain=LogicalScalar.FromInt64(5000);
    public bool Repeat=false;
}
/// <summary>Whether a <see cref="StrainField"/> has acted.</summary>
public struct StrainFieldState:IRuntimeComponent<StrainField> {
    public bool Fired;
}
/// <summary>
/// A force field (ADR-0078/M0113, UE's radial vector field): every moving body within <see cref="Radius"/> of the node is pushed away from it once, by
/// <see cref="Speed"/> (m/s) at the centre, less with distance down to nothing at the radius. Bodies that come loose while the field lives are pushed too,
/// one tick after they appear.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,42)]
public struct ForceField() {
    public LogicalScalar Radius=LogicalScalar.FromInt64(3);
    public LogicalScalar Speed=LogicalScalar.FromInt64(6);
}
/// <summary>Attached with a <see cref="ForceField"/>; the bodies it has pushed are kept by the <see cref="Destruction"/> service.</summary>
public struct ForceFieldState:IRuntimeComponent<ForceField> {
    public int Pushed;
}
