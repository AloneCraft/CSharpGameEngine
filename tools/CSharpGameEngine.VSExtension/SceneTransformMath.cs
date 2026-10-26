using System;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>Designer 側の TRS。編集時には RUF オイラー角のラジアンで回転を保持し、ギズモにはクォータニオンとして公開する。</summary>
internal readonly struct SceneTransformValue {
    internal SceneTransformValue(Vector3 Position,Quaternion Rotation,Vector3 Scale){
        this.Position=Position;
        this.Rotation=SceneTransformMath.NormalizeRotation(Rotation);
        this.EulerRotation=SceneTransformMath.ToEulerRuf(this.Rotation);
        this.Scale=Scale;
    }

    internal SceneTransformValue(Vector3 Position,Vector3 EulerRotation,Vector3 Scale){
        this.Position=Position;
        this.EulerRotation=EulerRotation;
        this.Rotation=SceneTransformMath.CreateRotation(EulerRotation);
        this.Scale=Scale;
    }

    internal Vector3 Position { get; }
    internal Quaternion Rotation { get; }
    internal Vector3 EulerRotation { get; }
    internal Vector3 Scale { get; }
    internal static SceneTransformValue Identity=>new(Vector3.Zero,Vector3.Zero,Vector3.One);
}

/// <summary>シーンデザイナーとそのギズモで共有する、行ベクトル型 TRS ヘルパー。</summary>
internal static class SceneTransformMath {
    private const float QuaternionEpsilon=1e-12f;

    internal static Matrix4x4 ToMatrix(in SceneTransformValue Transform)=>
        Matrix4x4.CreateScale(Transform.Scale)
        *Matrix4x4.CreateFromQuaternion(Transform.Rotation)
        *Matrix4x4.CreateTranslation(Transform.Position);

    internal static Matrix4x4 ToWorld(in SceneTransformValue Local,in Matrix4x4 ParentWorld)=>
        ToMatrix(Local)*ParentWorld;

    internal static bool TryCalculateLocal(
        in Matrix4x4 World,in Matrix4x4 ParentWorld,out SceneTransformValue Local){
        if(!Matrix4x4.Invert(ParentWorld,out var InverseParent)){
            Local=default;
            return false;
        }
        return TryDecompose(World*InverseParent,out Local);
    }

    internal static bool TryDecompose(in Matrix4x4 Matrix,out SceneTransformValue Transform){
        if(!Matrix4x4.Decompose(Matrix,out var Scale,out var Rotation,out var Position)
            ||!IsFinite(Position)||!IsFinite(Scale)||!IsFinite(Rotation)){
            Transform=default;
            return false;
        }
        Transform=new SceneTransformValue(Position,Rotation,Scale);
        return true;
    }

    /// <summary>public な RUF ラジアンから、Yaw(U)-Pitch(R)-Roll(F) の向きを作成する。</summary>
    internal static Quaternion CreateRotation(Vector3 EulerRuf)=>
        Quaternion.CreateFromYawPitchRoll(EulerRuf.Y,EulerRuf.X,EulerRuf.Z);

    /// <summary>ギズモのクォータニオンを、Yaw(U)-Pitch(R)-Roll(F) 規約の public な RUF ラジアンへ変換する。</summary>
    internal static Vector3 ToEulerRuf(Quaternion Rotation){
        Rotation=NormalizeRotation(Rotation);
        float SinR=2f*(Rotation.W*Rotation.X-Rotation.Y*Rotation.Z);
        SinR=SinR>1f?1f:SinR<-1f?-1f:SinR;
        float R=FloatMath.Asin(SinR);
        float U=FloatMath.Atan2(2f*(Rotation.W*Rotation.Y+Rotation.Z*Rotation.X),
            1f-2f*(Rotation.X*Rotation.X+Rotation.Y*Rotation.Y));
        float F=FloatMath.Atan2(2f*(Rotation.W*Rotation.Z+Rotation.X*Rotation.Y),
            1f-2f*(Rotation.X*Rotation.X+Rotation.Z*Rotation.Z));
        return new Vector3(R,U,F);
    }

    internal static Quaternion NormalizeRotation(Quaternion Rotation)=>
        Rotation.LengthSquared()<=QuaternionEpsilon?Quaternion.Identity:Quaternion.Normalize(Rotation);

    internal static Quaternion MatchHemisphere(Quaternion Reference,Quaternion Value)=>
        Quaternion.Dot(Reference,Value)<0f
            ?new Quaternion(-Value.X,-Value.Y,-Value.Z,-Value.W)
            :Value;

    internal static bool NearlyEqual(float Left,float Right,float Epsilon=1e-6f)=>
        Math.Abs(Left-Right)<=Epsilon;

    private static bool IsFinite(Vector3 Value)=>IsFinite(Value.X)&&IsFinite(Value.Y)&&IsFinite(Value.Z);
    private static bool IsFinite(Quaternion Value)=>IsFinite(Value.X)&&IsFinite(Value.Y)&&IsFinite(Value.Z)&&IsFinite(Value.W);
    private static bool IsFinite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);
}

/// <summary>入れ子になった public な Transform.Position/Rotation/Scale ベクトルを読み取り、検証する。</summary>
internal static class SceneTransformBinding {
    internal static bool TryRead(SceneNode Node,out SceneTransformValue Transform){
        var Component=SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform);
        if(Component is null){ Transform=default; return false; }
        if(IsLegacyFlat(Component)){
            Transform=new SceneTransformValue(
                new Vector3(ReadLegacyFloat(Component,"PositionX"),ReadLegacyFloat(Component,"PositionY"),ReadLegacyFloat(Component,"PositionZ")),
                new Quaternion(ReadLegacyFloat(Component,"RotationX"),ReadLegacyFloat(Component,"RotationY"),ReadLegacyFloat(Component,"RotationZ"),ReadLegacyFloat(Component,"RotationW",1f)),
                new Vector3(ReadLegacyFloat(Component,"ScaleX",1f),ReadLegacyFloat(Component,"ScaleY",1f),ReadLegacyFloat(Component,"ScaleZ",1f)));
        } else {
            Transform=new SceneTransformValue(
                ReadVector(Component,"Position",Vector3.Zero),
                ReadVector(Component,"Rotation",Vector3.Zero),
                ReadVector(Component,"Scale",Vector3.One));
        }
        return true;
    }

    internal static bool CanEditMove(SceneNode Node,GizmoHandleId Handle){
        var Component=SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform);
        if(Component is null){ return false; }
        return Handle switch {
            GizmoHandleId.AxisX=>CanEditVector(Component,"Position",0),
            GizmoHandleId.AxisY=>CanEditVector(Component,"Position",1),
            GizmoHandleId.AxisZ=>CanEditVector(Component,"Position",2),
            GizmoHandleId.PlaneXY=>CanEditVector(Component,"Position",0)&&CanEditVector(Component,"Position",1),
            GizmoHandleId.PlaneYZ=>CanEditVector(Component,"Position",1)&&CanEditVector(Component,"Position",2),
            GizmoHandleId.PlaneZX=>CanEditVector(Component,"Position",2)&&CanEditVector(Component,"Position",0),
            _=>false,
        };
    }

    internal static bool CanEditAllPosition(SceneNode Node)=>
        SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform)is { }Component
        &&CanEditVector(Component,"Position",0)&&CanEditVector(Component,"Position",1)&&CanEditVector(Component,"Position",2);

    internal static bool CanEditTransform(SceneNode Node,SceneActiveTool Tool,GizmoHandleId Handle)=>
        Handle==GizmoHandleId.SurfaceSnapPlane
            ?CanEditRotation(Node)
            :Tool switch {
                SceneActiveTool.Move=>CanEditMove(Node,Handle),
                SceneActiveTool.Rotate=>CanEditRotation(Node),
                SceneActiveTool.Scale=>CanEditScale(Node,Handle),
                _=>false,
            };

    private static bool CanEditRotation(SceneNode Node){
        var Component=SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform);
        if(Component is null||!CanEditAllPosition(Node)){ return false; }
        if(IsLegacyFlat(Component)){
            return CanEditLegacy(Component,"RotationX")&&CanEditLegacy(Component,"RotationY")
                &&CanEditLegacy(Component,"RotationZ")&&CanEditLegacy(Component,"RotationW");
        }
        return CanEditVector(Component,"Rotation",0)&&CanEditVector(Component,"Rotation",1)&&CanEditVector(Component,"Rotation",2);
    }

    private static bool CanEditScale(SceneNode Node,GizmoHandleId Handle){
        var Component=SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform);
        if(Component is null||!CanEditAllPosition(Node)){
            return false;
        }
        if(IsLegacyFlat(Component)&&(!CanEditLegacy(Component,"RotationX")||!CanEditLegacy(Component,"RotationY")
            ||!CanEditLegacy(Component,"RotationZ")||!CanEditLegacy(Component,"RotationW"))){
            return false;
        }
        if(!IsLegacyFlat(Component)&&(!CanEditVector(Component,"Rotation",0)||!CanEditVector(Component,"Rotation",1)||!CanEditVector(Component,"Rotation",2))){
            return false;
        }
        if(IsLegacyFlat(Component)){
            return(Handle is GizmoHandleId.ScaleX or GizmoHandleId.ScaleY or GizmoHandleId.ScaleZ or GizmoHandleId.ScaleUniform)
                &&CanEditLegacy(Component,"ScaleX")&&CanEditLegacy(Component,"ScaleY")&&CanEditLegacy(Component,"ScaleZ");
        }
        return(Handle is GizmoHandleId.ScaleX or GizmoHandleId.ScaleY or GizmoHandleId.ScaleZ or GizmoHandleId.ScaleUniform)
            &&CanEditVector(Component,"Scale",0)&&CanEditVector(Component,"Scale",1)&&CanEditVector(Component,"Scale",2);
    }

    internal static Vector3 ReadVector(SceneComponent Component,string Name,Vector3 Default){
        if(Name=="Rotation"&&IsCanonicalTransform(Component)){
            return ReadCanonicalRotation(Component,Default);
        }
        if(IsLegacyFlat(Component)){
            return new Vector3(
                ReadLegacyFloat(Component,Name+"X",Default.X),
                ReadLegacyFloat(Component,Name+"Y",Default.Y),
                ReadLegacyFloat(Component,Name+"Z",Default.Z));
        }
        return new Vector3(
            ReadVectorComponent(Component,Name,0,Default.X),
            ReadVectorComponent(Component,Name,1,Default.Y),
            ReadVectorComponent(Component,Name,2,Default.Z));
    }

    internal static float ReadVectorComponent(SceneComponent Component,string Name,int Index,float Default){
        SceneValue?Value=Component.FindMember(Name)?.Value;
        if(Value is null){ return Default; }
        if(Value.Members.Length>Index){ return ReadFloat(Value.Members[Index].Value.ConstantValue,Default); }
        return Value.Arguments.Length>Index?ReadFloat(Value.Arguments[Index].ConstantValue,Default):Default;
    }

    internal static bool CanEditVector(SceneComponent Component,string Name,int Index){
        if(Name=="Rotation"&&IsCanonicalTransform(Component)){
            return CanEditCanonicalRotation(Component,Index);
        }
        if(IsLegacyFlat(Component)){
            string LegacyName=Name+(Index==0?"X":Index==1?"Y":"Z");
            return Component.FindMember(LegacyName)?.Value.IsEditable??true;
        }
        SceneValue?Value=Component.FindMember(Name)?.Value;
        if(Value is null){ return true; }
        if(Value.Kind!=SceneValueKind.ObjectCreation){ return false; }
        if(Value.Members.Length>Index){ return Value.Members[Index].Value.IsEditable; }
        return Value.Arguments.Length>Index&&Value.Arguments[Index].IsEditable;
    }

    internal static bool IsCanonicalTransform(SceneComponent Component)=>
        string.Equals(Component.TypeFullyQualifiedName,SceneComponentBindings.TransformTypeFullyQualifiedName,StringComparison.Ordinal);

    private static Vector3 ReadCanonicalRotation(SceneComponent Component,Vector3 Default){
        SceneValue?Value=Component.FindMember("Rotation")?.Value;
        if(Value is null||Value.Kind==SceneValueKind.Default){ return Default; }
        if(Value.Kind!=SceneValueKind.ObjectCreation){ return Default; }
        if(Value.Arguments.Length==1&&Value.Arguments[0] is { Kind:SceneValueKind.ObjectCreation }Euler){
            return ReadValueVector(Euler,Default);
        }
        if(Value.Arguments.Length>=4){
            var Rotation=new Quaternion(
                ReadFloat(Value.Arguments[0].ConstantValue,0f),
                ReadFloat(Value.Arguments[1].ConstantValue,0f),
                ReadFloat(Value.Arguments[2].ConstantValue,0f),
                ReadFloat(Value.Arguments[3].ConstantValue,1f));
            return SceneTransformMath.ToEulerRuf(Rotation);
        }
        return Default;
    }

    private static bool CanEditCanonicalRotation(SceneComponent Component,int Index){
        if(Index<0||Index>2){ return false; }
        SceneValue?Value=Component.FindMember("Rotation")?.Value;
        if(Value is null||Value.Kind==SceneValueKind.Default){ return true; }
        if(Value.Kind!=SceneValueKind.ObjectCreation){ return false; }
        if(Value.Arguments.Length==1&&Value.Arguments[0] is { Kind:SceneValueKind.ObjectCreation }Euler){
            return Euler.Arguments.Length>Index&&Euler.Arguments[Index].IsEditable;
        }
        return Value.Arguments.Length>=4
            &&Value.Arguments[0].IsEditable&&Value.Arguments[1].IsEditable
            &&Value.Arguments[2].IsEditable&&Value.Arguments[3].IsEditable;
    }

    private static Vector3 ReadValueVector(SceneValue Value,Vector3 Default)=>new(
        Value.Arguments.Length>0?ReadFloat(Value.Arguments[0].ConstantValue,Default.X):Default.X,
        Value.Arguments.Length>1?ReadFloat(Value.Arguments[1].ConstantValue,Default.Y):Default.Y,
        Value.Arguments.Length>2?ReadFloat(Value.Arguments[2].ConstantValue,Default.Z):Default.Z);

    // 既存の手書きシーンは一度表示でき、編集時に public な RUF ベクトルへ書き換えられる。
    private static bool IsLegacyFlat(SceneComponent Component)=>
        Component.FindMember("Position") is null
        &&(Component.FindMember("PositionX") is not null||Component.FindMember("PositionY") is not null
            ||Component.FindMember("PositionZ") is not null||Component.FindMember("RotationX") is not null
            ||Component.FindMember("RotationY") is not null||Component.FindMember("RotationZ") is not null
            ||Component.FindMember("RotationW") is not null||Component.FindMember("ScaleX") is not null
            ||Component.FindMember("ScaleY") is not null||Component.FindMember("ScaleZ") is not null);

    private static bool CanEditLegacy(SceneComponent Component,string Name)=>Component.FindMember(Name)?.Value.IsEditable??true;

    private static float ReadLegacyFloat(SceneComponent Component,string Name,float Default=0f)=>
        ReadFloat(Component.FindMember(Name)?.Value.ConstantValue,Default);
    private static float ReadFloat(object? Value,float Default)=>Value switch {
        float Float=>Float,
        int Integer=>Integer, long Long=>Long, _=>Default,
    };
}
