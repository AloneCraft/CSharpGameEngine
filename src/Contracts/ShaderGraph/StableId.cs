using System;
using System.Globalization;
namespace CSharpGameEngine.ShaderGraph;
public readonly struct StableId : IEquatable<StableId> {
    private readonly Guid Value;
    private StableId(Guid Value) {
        this.Value = Value;
    }
    public bool IsEmpty => this == default;
    public static StableId New() {
        return new StableId(Guid.NewGuid());
    }
    public static StableId Parse(string Value) {
        if (Value is null) { throw new ArgumentNullException(nameof(Value)); }
        return new StableId(Guid.Parse(Value));
    }
    public static bool TryParse(string? Value, out StableId StableId) {
        if (Guid.TryParse(Value, out var Parsed)) {
            StableId = new StableId(Parsed);
            return true;
        }
        StableId = default;
        return false;
    }
    public override string ToString() {
        return this.Value.ToString("D", CultureInfo.InvariantCulture);
    }
    public bool Equals(StableId Other) {
        return this.Value.Equals(Other.Value);
    }
    public override bool Equals(object? Obj) {
        return Obj is StableId Other && this.Equals(Other);
    }
    public override int GetHashCode() {
        return this.Value.GetHashCode();
    }
    public static bool operator ==(StableId Left, StableId Right) {
        return Left.Equals(Right);
    }
    public static bool operator !=(StableId Left, StableId Right) {
        return !Left.Equals(Right);
    }
}
