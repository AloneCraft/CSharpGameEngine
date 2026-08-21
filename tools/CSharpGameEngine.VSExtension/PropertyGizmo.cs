using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

internal enum PropertyGizmoKind {
    CameraPerspective,
    CameraOrthographic,
    DirectionalLight,
    PointLight,
    SpotLight,
}

internal readonly struct PropertyGizmoModifierApplicability {
    internal PropertyGizmoModifierApplicability(bool Shift,bool Ctrl,bool Alt){
        this.Shift=Shift;
        this.Ctrl=Ctrl;
        this.Alt=Alt;
    }

    internal bool Shift { get; }

    internal bool Ctrl { get; }

    internal bool Alt { get; }
}

/// <summary>仕様書11章のPropertyGizmo modifier適用表。</summary>
internal static class PropertyGizmoModifierPolicy {
    internal static PropertyGizmoModifierApplicability Get(GizmoHandleId Handle)=>
        PropertyGizmo.IsPropertyHandle(Handle)
            ?new PropertyGizmoModifierApplicability(Shift:false,Ctrl:true,Alt:false)
            :new PropertyGizmoModifierApplicability(Shift:false,Ctrl:false,Alt:false);
}

internal readonly struct PropertyGizmoFrame {
    internal PropertyGizmoFrame(
        PropertyGizmoKind Kind,string ComponentTypeFullyQualifiedName,
        Vector3 Pivot,Vector3 Right,Vector3 Up,Vector3 Forward,float WorldScale,
        float FovYRadians,float NearZ,float FarZ,float OrthographicSize,
        float Range,float InnerConeAngleRadians,float OuterConeAngleRadians,
        IReadOnlyList<GizmoHandleId>EditableHandles,
        bool UsesPhysicalLightFields=false){
        this.Kind=Kind;
        this.ComponentTypeFullyQualifiedName=ComponentTypeFullyQualifiedName;
        this.Pivot=Pivot;
        this.Right=NormalizeOr(Right,Vector3.UnitX);
        this.Up=NormalizeOr(Up,Vector3.UnitY);
        this.Forward=NormalizeOr(Forward,Vector3.UnitZ);
        this.WorldScale=Math.Max(WorldScale,1e-4f);
        this.FovYRadians=FovYRadians;
        this.NearZ=NearZ;
        this.FarZ=FarZ;
        this.OrthographicSize=OrthographicSize;
        this.Range=Range;
        this.InnerConeAngleRadians=InnerConeAngleRadians;
        this.OuterConeAngleRadians=OuterConeAngleRadians;
        this.EditableHandles=EditableHandles;
        this.UsesPhysicalLightFields=UsesPhysicalLightFields;
    }

    internal PropertyGizmoKind Kind { get; }

    internal string ComponentTypeFullyQualifiedName { get; }

    internal Vector3 Pivot { get; }

    internal Vector3 Right { get; }

    internal Vector3 Up { get; }

    internal Vector3 Forward { get; }

    internal float WorldScale { get; }

    internal float FovYRadians { get; }

    internal float NearZ { get; }

    internal float FarZ { get; }

    internal float OrthographicSize { get; }

    internal float Range { get; }

    internal float InnerConeAngleRadians { get; }

    internal float OuterConeAngleRadians { get; }

    internal IReadOnlyList<GizmoHandleId>Handles=>
        this.Kind switch {
        PropertyGizmoKind.CameraPerspective=>
            [GizmoHandleId.CameraFov,GizmoHandleId.CameraNear,GizmoHandleId.CameraFar],
        PropertyGizmoKind.CameraOrthographic=>
            [GizmoHandleId.CameraOrthographicSize,GizmoHandleId.CameraNear,GizmoHandleId.CameraFar],
        PropertyGizmoKind.DirectionalLight=>this.UsesPhysicalLightFields
            ?Array.Empty<GizmoHandleId>()
            :[GizmoHandleId.DirectionalDirection],
        PropertyGizmoKind.PointLight=>[GizmoHandleId.PointRange],
        PropertyGizmoKind.SpotLight=>this.UsesPhysicalLightFields
            ?[GizmoHandleId.SpotInnerCone,GizmoHandleId.SpotOuterCone,GizmoHandleId.SpotRange]
            :[
                GizmoHandleId.SpotDirection,GizmoHandleId.SpotInnerCone,
                GizmoHandleId.SpotOuterCone,GizmoHandleId.SpotRange],
        _=>Array.Empty<GizmoHandleId>(),
    };

    internal IReadOnlyList<GizmoHandleId>EditableHandles { get; }

    internal bool UsesPhysicalLightFields { get; }

    internal bool CanEdit(GizmoHandleId Handle)=> this.EditableHandles.Contains(Handle);

    internal Vector3 GetHandlePosition(GizmoHandleId Handle){
        var Value=this;
        var NearDistance= this.GetDisplayNearDistance();
        var FarDistance= this.GetDisplayFarDistance();
        var NearCenter= this.Pivot+(this.Forward*NearDistance);
        var FarCenter= this.Pivot+(this.Forward*FarDistance);
        var FarHalfHeight= this.GetDisplayHalfHeight(FarDistance);
        return Handle switch {
            GizmoHandleId.CameraFov=>FarCenter+(this.Up*FarHalfHeight),
            GizmoHandleId.CameraNear=>NearCenter-(this.Right*(this.WorldScale*0.18f)),
            GizmoHandleId.CameraFar=>FarCenter-(this.Right*(this.WorldScale*0.18f)),
            GizmoHandleId.CameraOrthographicSize=>FarCenter+(this.Up*FarHalfHeight),
            GizmoHandleId.DirectionalDirection=> this.Pivot+(this.Forward* this.WorldScale),
            GizmoHandleId.PointRange=> this.Pivot+(this.Right*Math.Max(this.Range, this.WorldScale*0.2f)),
            GizmoHandleId.SpotDirection=> this.Pivot+(this.Forward*Math.Max(this.WorldScale*0.55f, this.Range*0.45f)),
            GizmoHandleId.SpotInnerCone=>ConeHandle(this.InnerConeAngleRadians,0.72f),
            GizmoHandleId.SpotOuterCone=>ConeHandle(this.OuterConeAngleRadians,0.9f),
            GizmoHandleId.SpotRange=> this.Pivot+(this.Forward*Math.Max(this.Range, this.WorldScale*0.2f)),
            _=> this.Pivot,
        };

        Vector3 ConeHandle(float Angle,float DistanceFraction){
            var Distance=Math.Max(Value.Range,Value.WorldScale)*DistanceFraction;
            return Value.Pivot+(Value.Forward*(float)Math.Cos(Angle)*Distance)
                +(Value.Up*(float)Math.Sin(Angle)*Distance);
        }
    }

    internal PropertyGizmoFrame ApplyPointerDelta(GizmoHandleId Handle,int DeltaX,int DeltaY,bool Snap){
        var Fov= this.FovYRadians;
        var Near= this.NearZ;
        var Far= this.FarZ;
        var Ortho= this.OrthographicSize;
        var NewRange= this.Range;
        var Inner= this.InnerConeAngleRadians;
        var Outer= this.OuterConeAngleRadians;
        var NewForward= this.Forward;
        float Vertical=-DeltaY;
        switch(Handle){
            case GizmoHandleId.CameraFov:
                Fov=Clamp(Fov+(Vertical*0.01f),Degrees(1f),Degrees(179f));
                if(Snap){ Fov=SnapValue(Fov,Degrees(5f)); }
                break;
            case GizmoHandleId.CameraNear:
                Near=Math.Max(0.001f,Near+(Vertical*Math.Max(Near,0.1f)*0.01f));
                if(Snap){ Near=SnapPositive(Near,0.1f); }
                Near=Math.Min(Near,Math.Max(0.001f,Far-0.001f));
                break;
            case GizmoHandleId.CameraFar:
                Far=Math.Max(Near+0.001f,Far+(Vertical*Math.Max(Far,1f)*0.01f));
                if(Snap){ Far=Math.Max(Near+0.001f,SnapPositive(Far,1f)); }
                break;
            case GizmoHandleId.CameraOrthographicSize:
                Ortho=Math.Max(0.01f,Ortho+(Vertical*Math.Max(Ortho,1f)*0.01f));
                if(Snap){ Ortho=SnapPositive(Ortho,1f); }
                break;
            case GizmoHandleId.PointRange:
            case GizmoHandleId.SpotRange:
                NewRange=Math.Max(0.01f,NewRange+(Vertical*Math.Max(NewRange,1f)*0.01f));
                if(Snap){ NewRange=SnapPositive(NewRange,1f); }
                break;
            case GizmoHandleId.SpotInnerCone:
                Inner=Clamp(Inner+(Vertical*0.01f),0f,Outer);
                if(Snap){ Inner=Clamp(SnapValue(Inner,Degrees(5f)),0f,Outer); }
                break;
            case GizmoHandleId.SpotOuterCone:
                Outer=Clamp(Outer+(Vertical*0.01f),Inner,Degrees(89f));
                if(Snap){ Outer=Clamp(SnapValue(Outer,Degrees(5f)),Inner,Degrees(89f)); }
                break;
            case GizmoHandleId.DirectionalDirection:
            case GizmoHandleId.SpotDirection:
                var Yaw=-DeltaX*0.01f;
                var Pitch=DeltaY*0.01f;
                if(Snap){
                    Yaw=SnapValue(Yaw,Degrees(15f));
                    Pitch=SnapValue(Pitch,Degrees(15f));
                }
                var Rotation=Quaternion.CreateFromAxisAngle(this.Up,Yaw)
                            *Quaternion.CreateFromAxisAngle(this.Right,Pitch);
                NewForward=NormalizeOr(Vector3.Transform(this.Forward,Rotation), this.Forward);
                break;
        }

        (var NewRight,var NewUp)=CreatePerpendicularAxes(NewForward, this.Up);
        return new PropertyGizmoFrame(this.Kind, this.ComponentTypeFullyQualifiedName, this.Pivot,NewRight,NewUp,NewForward, this.WorldScale,
            Fov,Near,Far,Ortho,NewRange,Inner,Outer, this.EditableHandles,this.UsesPhysicalLightFields);
    }

    internal float GetDisplayNearDistance()=>Math.Max(this.NearZ, this.WorldScale*0.15f);

    internal float GetDisplayFarDistance()=>Math.Max(this.GetDisplayNearDistance()+(this.WorldScale*0.35f),Math.Min(this.FarZ, this.WorldScale*4f));

    internal float GetDisplayHalfHeight(float Distance)=>
        this.Kind==PropertyGizmoKind.CameraOrthographic
        ?Math.Min(Math.Max(this.OrthographicSize*0.5f, this.WorldScale*0.2f), this.WorldScale*3f)
        :Math.Min((float)Math.Tan(Clamp(this.FovYRadians,Degrees(1f),Degrees(179f))*0.5f)*Distance, this.WorldScale*3f);

    private static float SnapPositive(float Value,float Step)=>Math.Max(Step,SnapValue(Value,Step));

    private static float SnapValue(float Value,float Step)=>(float)Math.Round(Value/Step)*Step;

    private static float Clamp(float Value,float Minimum,float Maximum)=>Math.Max(Minimum,Math.Min(Maximum,Value));

    private static float Degrees(float Value)=>Value*(float)Math.PI/180f;

    private static Vector3 NormalizeOr(Vector3 Value,Vector3 Fallback)=>
        Value.LengthSquared()<=1e-10f?Fallback:Vector3.Normalize(Value);

    private static (Vector3 Right,Vector3 Up)CreatePerpendicularAxes(Vector3 Forward,Vector3 PreferredUp){
        Forward=NormalizeOr(Forward,Vector3.UnitZ);
        var Reference=Math.Abs(Vector3.Dot(Forward,PreferredUp))<0.98f?PreferredUp:Vector3.UnitY;
        if(Math.Abs(Vector3.Dot(Forward,Reference))>=0.98f){ Reference=Vector3.UnitX; }
        var Right=NormalizeOr(Vector3.Cross(Reference,Forward),Vector3.UnitX);
        var Up=NormalizeOr(Vector3.Cross(Forward,Right),Vector3.UnitY);
        return (Right,Up);
    }
}

/// <summary>Scene Componentの値を、描画とsource編集に共通のPropertyGizmo frameへ束縛する。</summary>
internal static class PropertyGizmoBinding {
    internal static bool TryCreate(
        SceneNode Node,Matrix4x4 NodeWorld,float WorldScale,out PropertyGizmoFrame Frame){
        if(SceneComponentBindings.FindComponent(Node,SceneComponentRole.Camera)is{ }Camera){
            SceneCameraPreviewBinding.TryGetCameraAxes(
                NodeWorld,out var Position,out var Right,out var Up,out var Forward);
            var Orthographic=ReadProjection(Camera);
            GizmoHandleId[]Handles=Orthographic
                ?[GizmoHandleId.CameraOrthographicSize,GizmoHandleId.CameraNear,GizmoHandleId.CameraFar]
                :[GizmoHandleId.CameraFov,GizmoHandleId.CameraNear,GizmoHandleId.CameraFar];
            Frame=new PropertyGizmoFrame(
                Orthographic?PropertyGizmoKind.CameraOrthographic:PropertyGizmoKind.CameraPerspective,
                Camera.TypeFullyQualifiedName,Position,Right,Up,Forward,WorldScale,
                ReadFloat(Camera,"FovYRadians",(float)Math.PI/4f),
                ReadFloat(Camera,"NearZ",0.1f),ReadFloat(Camera,"FarZ",300f),
                ReadFloat(Camera,"OrthographicSize",10f),0f,0f,0f,
                Handles.Where(Handle=>CanEdit(Camera,Handle)).ToArray());
            return true;
        }

        if(SceneComponentBindings.FindComponent(Node,SceneComponentRole.SpotLight)is{ }Spot){
            SceneTransformMath.TryDecompose(NodeWorld,out var Transform);
            bool Physical=Spot.FindMember("RangeMeters") is not null;
            var Position=Physical?Transform.Position:ReadPosition(Spot);
            var Direction=Physical
                ?Vector3.Transform(Vector3.UnitZ,Transform.Rotation)
                :ReadDirection(Spot,"Direction",Vector3.UnitZ);
            (var Right,var Up)=CreateAxes(Direction);
            GizmoHandleId[]Handles=Physical
                ?[GizmoHandleId.SpotInnerCone,GizmoHandleId.SpotOuterCone,GizmoHandleId.SpotRange]
                :[GizmoHandleId.SpotDirection,GizmoHandleId.SpotInnerCone,
                    GizmoHandleId.SpotOuterCone,GizmoHandleId.SpotRange];
            Frame=new PropertyGizmoFrame(
                PropertyGizmoKind.SpotLight,Spot.TypeFullyQualifiedName,Position,Right,Up,Direction,WorldScale,
                0f,0f,0f,0f,ReadFloat(Spot,Physical?"RangeMeters":"Range",1f),
                Physical?ReadFloat(Spot,"InnerConeFullAngleDegrees",30f)*(float)Math.PI/360f
                    :ReadFloat(Spot,"InnerConeAngleRadians",Degrees(15f)),
                Physical?ReadFloat(Spot,"OuterConeFullAngleDegrees",45f)*(float)Math.PI/360f
                    :ReadFloat(Spot,"OuterConeAngleRadians",Degrees(30f)),
                Handles.Where(Handle=>CanEdit(Spot,Handle)).ToArray(),Physical);
            return true;
        }

        if(SceneComponentBindings.FindComponent(Node,SceneComponentRole.PointLight)is{ }Point){
            SceneTransformMath.TryDecompose(NodeWorld,out var Transform);
            bool Physical=Point.FindMember("RangeMeters") is not null;
            Frame=new PropertyGizmoFrame(
                PropertyGizmoKind.PointLight,Point.TypeFullyQualifiedName,
                Physical?Transform.Position:ReadPosition(Point),
                Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ,WorldScale,
                0f,0f,0f,0f,ReadFloat(Point,Physical?"RangeMeters":"Range",1f),0f,0f,
                CanEdit(Point,GizmoHandleId.PointRange)?[GizmoHandleId.PointRange]:Array.Empty<GizmoHandleId>(),
                Physical);
            return true;
        }

        if(SceneComponentBindings.FindComponent(Node,SceneComponentRole.DirectionalLight)is{ }Directional){
            SceneTransformMath.TryDecompose(NodeWorld,out var Transform);
            bool Physical=Directional.FindMember("IlluminanceLux") is not null;
            var Direction=Physical
                ?Vector3.Transform(Vector3.UnitZ,Transform.Rotation)
                :ReadDirection(Directional,"DirectionTowardLight",Vector3.UnitZ);
            (var Right,var Up)=CreateAxes(Direction);
            Frame=new PropertyGizmoFrame(
                PropertyGizmoKind.DirectionalLight,Directional.TypeFullyQualifiedName,Transform.Position,
                Right,Up,Direction,WorldScale,0f,0f,0f,0f,0f,0f,0f,
                !Physical&&CanEdit(Directional,GizmoHandleId.DirectionalDirection)
                    ?[GizmoHandleId.DirectionalDirection]:Array.Empty<GizmoHandleId>(),
                Physical);
            return true;
        }

        Frame=default;
        return false;
    }

    private static bool ReadProjection(SceneComponent Component)=>
        Component.FindMember("Projection")?.Value.SourceExpressionText.IndexOf(
            "Orthographic",StringComparison.Ordinal)>=0;

    private static Vector3 ReadPosition(SceneComponent Component)=>new(
        ReadFloat(Component,"PositionX"),ReadFloat(Component,"PositionY"),ReadFloat(Component,"PositionZ"));

    private static Vector3 ReadDirection(SceneComponent Component,string Prefix,Vector3 Default){
        Vector3 Value=new(
            ReadFloat(Component,Prefix+"X",Default.X),
            ReadFloat(Component,Prefix+"Y",Default.Y),
            ReadFloat(Component,Prefix+"Z",Default.Z));
        return Value.LengthSquared()<=1e-10f?Default:Vector3.Normalize(Value);
    }

    private static bool CanEdit(SceneComponent Component,GizmoHandleId Handle)=>Handle switch {
        GizmoHandleId.CameraFov=>CanEdit(Component,"FovYRadians"),
        GizmoHandleId.CameraNear=>CanEdit(Component,"NearZ"),
        GizmoHandleId.CameraFar=>CanEdit(Component,"FarZ"),
        GizmoHandleId.CameraOrthographicSize=>CanEdit(Component,"OrthographicSize"),
        GizmoHandleId.DirectionalDirection=>CanEditVector(Component,"DirectionTowardLight"),
        GizmoHandleId.PointRange=>CanEdit(Component,
            Component.FindMember("RangeMeters") is not null?"RangeMeters":"Range"),
        GizmoHandleId.SpotDirection=>CanEditVector(Component,"Direction"),
        GizmoHandleId.SpotInnerCone=>CanEdit(Component,
            Component.FindMember("InnerConeFullAngleDegrees") is not null
                ?"InnerConeFullAngleDegrees":"InnerConeAngleRadians"),
        GizmoHandleId.SpotOuterCone=>CanEdit(Component,
            Component.FindMember("OuterConeFullAngleDegrees") is not null
                ?"OuterConeFullAngleDegrees":"OuterConeAngleRadians"),
        GizmoHandleId.SpotRange=>CanEdit(Component,
            Component.FindMember("RangeMeters") is not null?"RangeMeters":"Range"),
        _=>false,
    };

    private static bool CanEditVector(SceneComponent Component,string Prefix)=>
        CanEdit(Component,Prefix+"X")&&CanEdit(Component,Prefix+"Y")&&CanEdit(Component,Prefix+"Z");

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

    private static (Vector3 Right,Vector3 Up)CreateAxes(Vector3 Forward){
        var Reference=Math.Abs(Vector3.Dot(Forward,Vector3.UnitY))<0.98f?Vector3.UnitY:Vector3.UnitX;
        var Right=Vector3.Normalize(Vector3.Cross(Reference,Forward));
        return (Right,Vector3.Normalize(Vector3.Cross(Forward,Right)));
    }

    private static float Degrees(float Value)=>Value*(float)Math.PI/180f;
}

internal static class FrustumGizmo {
    internal static IReadOnlyList<GizmoOverlayPrimitive>BuildOverlay(
        in PropertyGizmoFrame Frame,GizmoHandleId?Hovered,GizmoHandleId?Active){
        var Value=Frame;
        List<GizmoOverlayPrimitive>Primitives=[];
        var NearDistance=Value.GetDisplayNearDistance();
        var FarDistance=Value.GetDisplayFarDistance();
        var NearHalfHeight=Value.GetDisplayHalfHeight(NearDistance);
        var FarHalfHeight=Value.GetDisplayHalfHeight(FarDistance);
        const float Aspect=16f/9f;
        var NearCenter=Value.Pivot+(Value.Forward*NearDistance);
        var FarCenter=Value.Pivot+(Value.Forward*FarDistance);
        Vector4 Color=new(0.25f,0.85f,1f,0.85f);
        var Near=Rectangle(NearCenter,NearHalfHeight*Aspect,NearHalfHeight);
        var Far=Rectangle(FarCenter,FarHalfHeight*Aspect,FarHalfHeight);
        AddLoop(Near,Color);
        AddLoop(Far,Color);
        for(var Index=0;Index<4;Index++){
            AddLine(Near[Index],Far[Index],Color);
        }
        PropertyGizmo.AddHandleMarkers(Primitives,Value,Hovered,Active);
        return Primitives;

        Vector3[]Rectangle(Vector3 Center,float HalfWidth,float HalfHeight)=>[
            Center-(Value.Right*HalfWidth)-(Value.Up*HalfHeight),
            Center+(Value.Right*HalfWidth)-(Value.Up*HalfHeight),
            Center+(Value.Right*HalfWidth)+(Value.Up*HalfHeight),
            Center-(Value.Right*HalfWidth)+(Value.Up*HalfHeight),
        ];

        void AddLoop(Vector3[]Points,Vector4 LineColor){
            for(var Index=0;Index<Points.Length;Index++){
                AddLine(Points[Index],Points[(Index+1)%Points.Length],LineColor);
            }
        }

        void AddLine(Vector3 Start,Vector3 End,Vector4 LineColor)=>Primitives.Add(
            new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                new GizmoOverlayVertex(Start,LineColor),new GizmoOverlayVertex(End,LineColor)]));
    }
}

internal static class PropertyGizmo {
    private static readonly GizmoHandleId[]PropertyHandles=[
        GizmoHandleId.CameraFov,GizmoHandleId.CameraNear,GizmoHandleId.CameraFar,
        GizmoHandleId.CameraOrthographicSize,GizmoHandleId.DirectionalDirection,
        GizmoHandleId.PointRange,GizmoHandleId.SpotDirection,GizmoHandleId.SpotInnerCone,
        GizmoHandleId.SpotOuterCone,GizmoHandleId.SpotRange,
    ];

    internal static bool IsPropertyHandle(GizmoHandleId Handle)=>PropertyHandles.Contains(Handle);

    internal static IReadOnlyList<GizmoOverlayPrimitive>BuildOverlay(
        in PropertyGizmoFrame Frame,GizmoHandleId?Hovered,GizmoHandleId?Active){
        if(Frame.Kind is PropertyGizmoKind.CameraPerspective or PropertyGizmoKind.CameraOrthographic){
            return FrustumGizmo.BuildOverlay(Frame,Hovered,Active);
        }

        var Value=Frame;
        List<GizmoOverlayPrimitive>Primitives=[];
        Vector4 Color=new(1f,0.72f,0.18f,0.9f);
        if(Value.Kind==PropertyGizmoKind.PointLight){
            AddRing(Value.Pivot,Value.Right,Value.Up,Math.Max(Value.Range,0.01f),Color);
        } else if(Value.Kind==PropertyGizmoKind.DirectionalLight){
            AddLine(Value.Pivot,Value.Pivot+(Value.Forward*Value.WorldScale),Color);
        } else {
            var Range=Math.Max(Value.Range,0.01f);
            AddCone(Value.OuterConeAngleRadians,Range,Color);
            AddCone(Value.InnerConeAngleRadians,Range*0.72f,new Vector4(1f,0.9f,0.35f,0.75f));
            AddLine(Value.Pivot,Value.Pivot+(Value.Forward*Range),Color);
        }
        AddHandleMarkers(Primitives,Value,Hovered,Active);
        return Primitives;

        void AddCone(float Angle,float Distance,Vector4 ConeColor){
            const int Segments=20;
            var Radius=(float)Math.Tan(Math.Max(0f,Angle))*Distance;
            var Center=Value.Pivot+(Value.Forward*Distance);
            Vector3?First=null;
            Vector3 Previous=default;
            for(var Index=0;Index<=Segments;Index++){
                var Radians=Index*(float)(Math.PI*2d/Segments);
                var Point=Center+(Value.Right*((float)Math.Cos(Radians)*Radius))
                                +(Value.Up*((float)Math.Sin(Radians)*Radius));
                if(Index==0){ First=Point; }
                else { AddLine(Previous,Point,ConeColor); }
                if(Index%5==0){ AddLine(Value.Pivot,Point,ConeColor); }
                Previous=Point;
            }
            if(First is { }Start){ AddLine(Previous,Start,ConeColor); }
        }

        void AddRing(Vector3 Center,Vector3 AxisA,Vector3 AxisB,float Radius,Vector4 RingColor){
            const int Segments=24;
            var Previous=Center+(AxisA*Radius);
            for(var Index=1;Index<=Segments;Index++){
                var Radians=Index*(float)(Math.PI*2d/Segments);
                var Point=Center+(AxisA*((float)Math.Cos(Radians)*Radius))
                                +(AxisB*((float)Math.Sin(Radians)*Radius));
                AddLine(Previous,Point,RingColor);
                Previous=Point;
            }
        }

        void AddLine(Vector3 Start,Vector3 End,Vector4 LineColor)=>Primitives.Add(
            new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                new GizmoOverlayVertex(Start,LineColor),new GizmoOverlayVertex(End,LineColor)]));
    }

    internal static GizmoHandleId?TryHit(
        in PropertyGizmoFrame Frame,Vector3 RayOrigin,Vector3 RayDirection){
        RayDirection=Vector3.Normalize(RayDirection);
        GizmoHandleId?Best=null;
        var BestDistance=float.PositiveInfinity;
        var Radius=Math.Max(Frame.WorldScale*0.1f,0.04f);
        foreach(var Handle in Frame.Handles){
            if(!Frame.CanEdit(Handle)){ continue; }
            var Center=Frame.GetHandlePosition(Handle);
            var Along=Vector3.Dot(Center-RayOrigin,RayDirection);
            if(Along<0f){ continue; }
            var Distance=Vector3.Distance(RayOrigin+(RayDirection*Along),Center);
            if(Distance<=Radius&&Along<BestDistance){
                Best=Handle;
                BestDistance=Along;
            }
        }
        return Best;
    }

    internal static SceneEditOperation[]CreateOperations(
        in PropertyGizmoFrame Frame,GizmoHandleId Handle,SceneKey SceneKey,
        EditorNodeId NodeId,long DocumentVersion,PropertyGizmoFrame? OriginalFrame=null){
        var ComponentTypeFullyQualifiedName=Frame.ComponentTypeFullyQualifiedName;
        var Members=GetMembers(Frame,Handle);
        var OriginalMembers=OriginalFrame is { }Original
                               ?GetMembers(Original,Handle).ToDictionary(static Member=>Member.Name,static Member=>Member.Value)
                               :null;
        return Members.Select(Member=>(SceneEditOperation)new SetComponentMemberOperation(
            SceneKey,NodeId,DocumentVersion,ComponentTypeFullyQualifiedName,
            Member.Name,FormatFloat(Member.Value),
            OriginalMembers is not null&&OriginalMembers.TryGetValue(Member.Name,out var OriginalValue)
                ?FormatFloat(OriginalValue):null)).ToArray();
    }

    private static List<(string Name,float Value)>GetMembers(
        in PropertyGizmoFrame Frame,GizmoHandleId Handle)=>Handle switch {
            GizmoHandleId.CameraFov=>[("FovYRadians",Frame.FovYRadians)],
            GizmoHandleId.CameraNear=>[("NearZ",Frame.NearZ)],
            GizmoHandleId.CameraFar=>[("FarZ",Frame.FarZ)],
            GizmoHandleId.CameraOrthographicSize=>[("OrthographicSize",Frame.OrthographicSize)],
            GizmoHandleId.PointRange or GizmoHandleId.SpotRange=>[
                (Frame.UsesPhysicalLightFields?"RangeMeters":"Range",Frame.Range)],
            GizmoHandleId.SpotInnerCone=>[
                (Frame.UsesPhysicalLightFields?"InnerConeFullAngleDegrees":"InnerConeAngleRadians",
                    Frame.UsesPhysicalLightFields
                        ?Frame.InnerConeAngleRadians*360f/(float)Math.PI
                        :Frame.InnerConeAngleRadians)],
            GizmoHandleId.SpotOuterCone=>[
                (Frame.UsesPhysicalLightFields?"OuterConeFullAngleDegrees":"OuterConeAngleRadians",
                    Frame.UsesPhysicalLightFields
                        ?Frame.OuterConeAngleRadians*360f/(float)Math.PI
                        :Frame.OuterConeAngleRadians)],
            GizmoHandleId.DirectionalDirection=>Frame.UsesPhysicalLightFields?[]:DirectionMembers("DirectionTowardLight",Frame.Forward),
            GizmoHandleId.SpotDirection=>Frame.UsesPhysicalLightFields?[]:DirectionMembers("Direction",Frame.Forward),
            _=>[],
        };

    internal static void AddHandleMarkers(
        List<GizmoOverlayPrimitive>Primitives,in PropertyGizmoFrame Frame,
        GizmoHandleId?Hovered,GizmoHandleId?Active){
        foreach(var Handle in Frame.Handles){
            var Center=Frame.GetHandlePosition(Handle);
            var Size=Frame.WorldScale*0.07f;
            var Base=Frame.CanEdit(Handle)
                         ?new Vector4(1f,0.75f,0.15f,1f)
                         :new Vector4(0.35f,0.35f,0.35f,0.45f);
            var Color=Active==Handle?Vector4.One:
                      Hovered==Handle?Vector4.Lerp(Base,Vector4.One,0.6f):Base;
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                new GizmoOverlayVertex(Center-(Frame.Right*Size),Color),
                new GizmoOverlayVertex(Center+(Frame.Right*Size),Color)]));
            Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,[
                new GizmoOverlayVertex(Center-(Frame.Up*Size),Color),
                new GizmoOverlayVertex(Center+(Frame.Up*Size),Color)]));
        }
    }

    private static List<(string Name,float Value)>DirectionMembers(string Prefix,Vector3 Direction)=>[
        (Prefix+"X",Direction.X),(Prefix+"Y",Direction.Y),(Prefix+"Z",Direction.Z)];

    private static string FormatFloat(float Value)=>Value.ToString("R",CultureInfo.InvariantCulture)+"f";
}

internal sealed class PropertyGizmoDrag {
    private readonly PropertyGizmoFrame OriginalFrame;
    private readonly GizmoHandleId Handle;
    private readonly int StartX;
    private readonly int StartY;

    internal PropertyGizmoDrag(
        in PropertyGizmoFrame Frame,GizmoHandleId Handle,int StartX,int StartY){
        if(!PropertyGizmo.IsPropertyHandle(Handle)||!Frame.Handles.Contains(Handle)||!Frame.CanEdit(Handle)){
            throw new ArgumentOutOfRangeException(nameof(Handle));
        }
        this.OriginalFrame=Frame;
        this.Handle=Handle;
        this.StartX=StartX;
        this.StartY=StartY;
    }

    internal PropertyGizmoFrame StartFrame=>this.OriginalFrame;

    internal PropertyGizmoFrame Update(int X,int Y,bool Snap)=>
        this.OriginalFrame.ApplyPointerDelta(this.Handle,X-this.StartX,Y-this.StartY,Snap);
}

/// <summary>TransformGizmoとPropertyGizmoを同一overlayへ順序付きで合成する。</summary>
internal static class CompoundGizmo {
    internal static IReadOnlyList<GizmoOverlayPrimitive>Compose(
        IReadOnlyList<GizmoOverlayPrimitive>Transform,
        IReadOnlyList<GizmoOverlayPrimitive>Property){
        List<GizmoOverlayPrimitive>Combined=new(Transform.Count+Property.Count);
        Combined.AddRange(Transform);
        Combined.AddRange(Property);
        return Combined;
    }
}
