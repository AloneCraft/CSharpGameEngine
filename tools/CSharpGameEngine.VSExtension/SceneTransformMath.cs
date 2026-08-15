using System;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>VSIX内で扱う、Scene DSLのflatなローカルTRS値。</summary>
internal readonly struct SceneTransformValue {
    internal SceneTransformValue(Vector3 Position,Quaternion Rotation,Vector3 Scale){
        this.Position=Position;
        this.Rotation=SceneTransformMath.NormalizeRotation(Rotation);
        this.Scale=Scale;
    }

    internal Vector3 Position { get; }

    internal Quaternion Rotation { get; }

    internal Vector3 Scale { get; }

    internal static SceneTransformValue Identity=>new(Vector3.Zero,Quaternion.Identity,Vector3.One);
}

/// <summary>
/// Scene Designer用の純粋なTRS計算。net472のVSIXからnet10.0のRuntime.Windowsを参照できないため、
/// RuntimeのTransformMathと同じSystem.Numerics row-vector式をこの境界で固定する。
/// </summary>
internal static class SceneTransformMath {
    private const float QuaternionEpsilon=1e-12f;

    internal static Matrix4x4 ToMatrix(in SceneTransformValue Transform)=>
        Matrix4x4.CreateScale(Transform.Scale)
        *Matrix4x4.CreateFromQuaternion(NormalizeRotation(Transform.Rotation))
        *Matrix4x4.CreateTranslation(Transform.Position);

    internal static Matrix4x4 ToWorld(in SceneTransformValue Local,in Matrix4x4 ParentWorld)=>
        ToMatrix(Local)*ParentWorld;

    internal static bool TryCalculateLocal(
        in Matrix4x4 World,in Matrix4x4 ParentWorld,out SceneTransformValue Local){
        if(!Matrix4x4.Invert(ParentWorld,out Matrix4x4 InverseParent)){
            Local=default;
            return false;
        }

        return TryDecompose(World*InverseParent,out Local);
    }

    internal static bool TryDecompose(in Matrix4x4 Matrix,out SceneTransformValue Transform){
        if(!Matrix4x4.Decompose(Matrix,out Vector3 Scale,out Quaternion Rotation,out Vector3 Position)
            ||!IsFinite(Position)||!IsFinite(Scale)||!IsFinite(Rotation)){
            Transform=default;
            return false;
        }

        Transform=new SceneTransformValue(Position,Rotation,Scale);
        return true;
    }

    internal static Quaternion NormalizeRotation(Quaternion Rotation)=>
        Rotation.LengthSquared()<=QuaternionEpsilon?Quaternion.Identity:Quaternion.Normalize(Rotation);

    internal static Quaternion MatchHemisphere(Quaternion Reference,Quaternion Value)=>
        Quaternion.Dot(Reference,Value)<0f
            ?new Quaternion(-Value.X,-Value.Y,-Value.Z,-Value.W)
            :Value;

    internal static bool NearlyEqual(float Left,float Right,float Epsilon=1e-6f)=>
        Math.Abs(Left-Right)<=Epsilon;

    private static bool IsFinite(Vector3 Value)=>
        IsFinite(Value.X)&&IsFinite(Value.Y)&&IsFinite(Value.Z);

    private static bool IsFinite(Quaternion Value)=>
        IsFinite(Value.X)&&IsFinite(Value.Y)&&IsFinite(Value.Z)&&IsFinite(Value.W);

    private static bool IsFinite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);
}

/// <summary>SceneModelのLocalTransform bindingをTRS値と編集可否へ変換する。</summary>
internal static class SceneTransformBinding {
    internal static bool TryRead(SceneNode Node,out SceneTransformValue Transform){
        SceneComponent?Component=SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform);
        if(Component is null){
            Transform=default;
            return false;
        }

        Vector3 Position=new(
            ReadFloat(Component,"PositionX"),
            ReadFloat(Component,"PositionY"),
            ReadFloat(Component,"PositionZ"));
        Quaternion Rotation=new(
            ReadFloat(Component,"RotationX"),
            ReadFloat(Component,"RotationY"),
            ReadFloat(Component,"RotationZ"),
            ReadFloat(Component,"RotationW",1f));
        Vector3 Scale=new(
            ReadFloat(Component,"ScaleX",1f),
            ReadFloat(Component,"ScaleY",1f),
            ReadFloat(Component,"ScaleZ",1f));
        Transform=new SceneTransformValue(Position,Rotation,Scale);
        return true;
    }

    internal static bool CanEditMove(SceneNode Node,GizmoHandleId Handle){
        SceneComponent?Component=SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform);
        if(Component is null){
            return false;
        }

        return Handle switch {
            GizmoHandleId.AxisX=>CanEdit(Component,"PositionX"),
            GizmoHandleId.AxisY=>CanEdit(Component,"PositionY"),
            GizmoHandleId.AxisZ=>CanEdit(Component,"PositionZ"),
            GizmoHandleId.PlaneXY=>CanEdit(Component,"PositionX")&&CanEdit(Component,"PositionY"),
            GizmoHandleId.PlaneYZ=>CanEdit(Component,"PositionY")&&CanEdit(Component,"PositionZ"),
            GizmoHandleId.PlaneZX=>CanEdit(Component,"PositionZ")&&CanEdit(Component,"PositionX"),
            _=>false,
        };
    }

    internal static bool CanEditAllPosition(SceneNode Node)=>
        SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform)is{ }Component
        &&CanEdit(Component,"PositionX")&&CanEdit(Component,"PositionY")&&CanEdit(Component,"PositionZ");

    private static bool CanEdit(SceneComponent Component,string Name)=>
        Component.FindMember(Name)?.Value.IsEditable??true;

    private static float ReadFloat(SceneComponent Component,string Name,float Default=0f)=>
        Component.FindMember(Name)?.Value.ConstantValue switch {
            float Float=>Float,
            double Double=>(float)Double,
            decimal Decimal=>(float)Decimal,
            int Integer=>Integer,
            long Long=>Long,
            _=>Default,
        };
}
