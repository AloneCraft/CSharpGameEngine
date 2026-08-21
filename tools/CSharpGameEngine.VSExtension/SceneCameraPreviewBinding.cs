using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>カメラプレビューの生成結果。Scene ViewのRendererに依存しない純粋な値です。</summary>
internal readonly struct SceneCameraPreview {
    internal SceneCameraPreview(
        NodePath NodePath,
        string DisplayName,
        SceneProjectionMode ProjectionMode,
        float AspectRatio,
        float FovYRadians,
        float NearZ,
        float FarZ,
        float OrthographicSize,
        Matrix4x4 World,
        Matrix4x4 View,
        Matrix4x4 Projection,
        string? ErrorMessage = null) {
        this.NodePath = NodePath;
        this.DisplayName = DisplayName;
        this.ProjectionMode = ProjectionMode;
        this.AspectRatio = AspectRatio;
        this.FovYRadians = FovYRadians;
        this.NearZ = NearZ;
        this.FarZ = FarZ;
        this.OrthographicSize = OrthographicSize;
        this.World = World;
        this.View = View;
        this.Projection = Projection;
        this.ErrorMessage = ErrorMessage;
    }

    internal NodePath NodePath { get; }
    internal string DisplayName { get; }
    internal SceneProjectionMode ProjectionMode { get; }
    internal float AspectRatio { get; }
    internal float FovYRadians { get; }
    internal float NearZ { get; }
    internal float FarZ { get; }
    internal float OrthographicSize { get; }
    internal Matrix4x4 World { get; }
    internal Matrix4x4 View { get; }
    internal Matrix4x4 Projection { get; }
    internal string? ErrorMessage { get; }
    internal bool IsAvailable => this.ErrorMessage is null;

    internal SceneCameraPreview WithAspectRatio(float AspectRatio) {
        float SafeAspect = IsFinite(AspectRatio) && AspectRatio > 0f
            ? AspectRatio
            : SceneCameraPreviewBinding.DefaultAspectRatio;
        if (!this.IsAvailable) {
            return new SceneCameraPreview(
                this.NodePath, this.DisplayName, this.ProjectionMode, SafeAspect,
                this.FovYRadians, this.NearZ, this.FarZ, this.OrthographicSize,
                this.World, Matrix4x4.Identity, Matrix4x4.Identity, this.ErrorMessage);
        }

        Matrix4x4 Projection = this.ProjectionMode == SceneProjectionMode.Orthographic
            ? Matrix4x4.CreateOrthographic(
                this.OrthographicSize * 2f * SafeAspect,
                this.OrthographicSize * 2f,
                this.NearZ, this.FarZ)
            : SceneViewCamera.GetProjection(
                this.FovYRadians, SafeAspect, this.NearZ, this.FarZ);
        return new SceneCameraPreview(
            this.NodePath, this.DisplayName, this.ProjectionMode, SafeAspect,
            this.FovYRadians, this.NearZ, this.FarZ, this.OrthographicSize,
            this.World, this.View, Projection, this.ErrorMessage);
    }

    private static bool IsFinite(float Value) =>
        !float.IsNaN(Value) && !float.IsInfinity(Value);
}

/// <summary>
/// Scene DSLのCamera ComponentとTransform階層を、Scene View用のカメラ行列へ束ねます。
/// RuntimeのCamera型を参照せず、VSIXがRoslynで得たSceneModelだけを入力にします。
/// </summary>
internal static class SceneCameraPreviewBinding {
    internal const int DefaultMaximumPreviewCount = 3;
    internal const float DefaultAspectRatio = 16f / 9f;
    internal const float DefaultFovYRadians = (float)(Math.PI / 4d);
    internal const float DefaultNearZ = 0.1f;
    internal const float DefaultFarZ = 300f;
    internal const float DefaultOrthographicSize = 10f;

    /// <summary>選択順を保ったまま、選択されたCameraだけを最大数まで取り出します。</summary>
    internal static IReadOnlyList<SceneCameraPreview> Build(
        SceneModel? Model,
        IReadOnlyCollection<NodePath> SelectedPaths,
        float AspectRatio,
        int MaximumPreviewCount = DefaultMaximumPreviewCount) {
        if (Model?.Root is null || SelectedPaths.Count == 0 || MaximumPreviewCount <= 0) {
            return Array.Empty<SceneCameraPreview>();
        }

        float SafeAspect = IsFinite(AspectRatio) && AspectRatio > 0f
            ? AspectRatio
            : DefaultAspectRatio;
        Dictionary<NodePath, SceneCameraPreview> ByPath = new();
        Collect(Model.Root, Matrix4x4.Identity, SafeAspect, ByPath);

        List<SceneCameraPreview> Result = new();
        foreach (NodePath Path in SelectedPaths) {
            if (Result.Count >= MaximumPreviewCount) {
                break;
            }

            if (ByPath.TryGetValue(Path, out SceneCameraPreview Preview)) {
                Result.Add(Preview);
            }
        }

        return Result;
    }

    /// <summary>Buildの別名。呼び出し側の意図を明示したい場所で使用します。</summary>
    internal static IReadOnlyList<SceneCameraPreview> Extract(
        SceneModel? Model,
        IReadOnlyCollection<NodePath> SelectedPaths,
        float AspectRatio,
        int MaximumPreviewCount = DefaultMaximumPreviewCount) =>
        Build(Model, SelectedPaths, AspectRatio, MaximumPreviewCount);

    private static void Collect(
        SceneNode Node,
        Matrix4x4 ParentWorld,
        float AspectRatio,
        IDictionary<NodePath, SceneCameraPreview> Result) {
        Matrix4x4 World = ParentWorld;
        bool HasTransform=SceneTransformBinding.TryRead(Node, out SceneTransformValue Local);
        if (HasTransform) {
            World = SceneTransformMath.ToWorld(Local, ParentWorld);
        }

        if (SceneComponentBindings.FindComponent(Node, SceneComponentRole.Camera) is { } Camera) {
            Result[Node.Path] = HasTransform
                ?Create(Node, Camera, World, AspectRatio)
                :new SceneCameraPreview(
                    Node.Path,Node.DisplayName,ReadProjection(Camera),
                    AspectRatio,DefaultFovYRadians,DefaultNearZ,DefaultFarZ,DefaultOrthographicSize,
                    World,Matrix4x4.Identity,Matrix4x4.Identity,string.Empty);
        }

        foreach (SceneNode Child in Node.Children) {
            Collect(Child, World, AspectRatio, Result);
        }
    }

    private static SceneCameraPreview Create(
        SceneNode Node,
        SceneComponent Camera,
        Matrix4x4 World,
        float AspectRatio) {
        SceneProjectionMode ProjectionMode = ReadProjection(Camera);
        float FovY = ReadFloat(Camera, "FovYRadians", DefaultFovYRadians);
        float NearZ = ReadFloat(Camera, "NearZ", DefaultNearZ);
        float FarZ = ReadFloat(Camera, "FarZ", DefaultFarZ);
        float OrthographicSize = ReadFloat(Camera, "OrthographicSize", DefaultOrthographicSize);
        string? Error = Validate(World, ProjectionMode, AspectRatio, FovY, NearZ, FarZ, OrthographicSize);

        if (Error is not null) {
            return new SceneCameraPreview(Node.Path, Node.DisplayName, ProjectionMode,
                AspectRatio, FovY, NearZ, FarZ, OrthographicSize,
                World, Matrix4x4.Identity, Matrix4x4.Identity, Error);
        }

        TryGetCameraAxes(World,out Vector3 Eye,out _,out Vector3 Up,out Vector3 Forward);
        Matrix4x4 View = Matrix4x4.CreateLookAt(Eye, Eye + Forward, Up);
        Matrix4x4 Projection = ProjectionMode == SceneProjectionMode.Orthographic
            ? Matrix4x4.CreateOrthographic(OrthographicSize * 2f * AspectRatio,
                OrthographicSize * 2f, NearZ, FarZ)
            : SceneViewCamera.GetProjection(FovY, AspectRatio, NearZ, FarZ);

        return new SceneCameraPreview(Node.Path, Node.DisplayName, ProjectionMode,
            AspectRatio, FovY, NearZ, FarZ, OrthographicSize,
            World, View, Projection);
    }

    private static SceneProjectionMode ReadProjection(SceneComponent Camera) {
        SceneValue? Value = Camera.FindMember("Projection")?.Value;
        string? Name = Value?.ReferencedSymbol?.Name;
        return string.Equals(Name, "Orthographic", StringComparison.Ordinal)
            ? SceneProjectionMode.Orthographic
            : SceneProjectionMode.Perspective;
    }

    private static float ReadFloat(SceneComponent Component, string Name, float Default) {
        object? Value = Component.FindMember(Name)?.Value.ConstantValue;
        float Result = Value switch {
            float Float => Float,
            double Double => (float)Double,
            decimal Decimal => (float)Decimal,
            int Integer => Integer,
            long Long => Long,
            uint UnsignedInteger => UnsignedInteger,
            short Short => Short,
            byte Byte => Byte,
            _ => Default,
        };
        return IsFinite(Result) ? Result : Default;
    }

    private static string? Validate(
        Matrix4x4 World,
        SceneProjectionMode ProjectionMode,
        float AspectRatio,
        float FovY,
        float NearZ,
        float FarZ,
        float OrthographicSize) {
        if (!IsFinite(World) || !IsFinite(AspectRatio) || AspectRatio <= 0f) {
            return "Camera transform or aspect ratio is not finite.";
        }

        Vector3 Forward = Vector3.TransformNormal(Vector3.UnitZ, World);
        Vector3 Up = Vector3.TransformNormal(Vector3.UnitY, World);
        if (!IsFinite(Forward) || !IsFinite(Up) || Forward.LengthSquared() < 1e-12f || Up.LengthSquared() < 1e-12f) {
            return "Camera transform has a zero-length view axis.";
        }

        if (!IsFinite(NearZ) || !IsFinite(FarZ) || NearZ <= 0f || FarZ <= NearZ) {
            return "Camera NearZ/FarZ values are invalid.";
        }

        if (ProjectionMode == SceneProjectionMode.Perspective &&
            (!IsFinite(FovY) || FovY <= 0f || FovY >= Math.PI)) {
            return "Camera FovYRadians must be between 0 and π.";
        }

        if (ProjectionMode == SceneProjectionMode.Orthographic &&
            (!IsFinite(OrthographicSize) || OrthographicSize <= 0f)) {
            return "Camera OrthographicSize must be positive.";
        }

        return null;
    }

    /// <summary>
    /// Shared camera pose contract for the thumbnail renderer and the Camera frustum gizmo:
    /// position is the Transform translation, local +Z is forward, and local +Y is up.
    /// </summary>
    internal static bool TryGetCameraAxes(
        in Matrix4x4 World,out Vector3 Position,out Vector3 Right,out Vector3 Up,out Vector3 Forward) {
        Position=new Vector3(World.M41,World.M42,World.M43);
        Forward=Vector3.TransformNormal(Vector3.UnitZ,World);
        Up=Vector3.TransformNormal(Vector3.UnitY,World);
        if(!IsFinite(Forward)||!IsFinite(Up)||Forward.LengthSquared()<1e-12f||Up.LengthSquared()<1e-12f){
            Right=Vector3.UnitX;
            Up=Vector3.UnitY;
            Forward=Vector3.UnitZ;
            return false;
        }

        Forward=Vector3.Normalize(Forward);
        Up=Vector3.Normalize(Up);
        Right=Vector3.Cross(Up,Forward);
        if(Right.LengthSquared()<1e-12f){
            Right=Vector3.TransformNormal(Vector3.UnitX,World);
        }
        Right=Right.LengthSquared()<1e-12f?Vector3.UnitX:Vector3.Normalize(Right);
        Up=Vector3.Normalize(Vector3.Cross(Forward,Right));
        return true;
    }

    private static bool IsFinite(Matrix4x4 Value) =>
        IsFinite(Value.M11) && IsFinite(Value.M12) && IsFinite(Value.M13) && IsFinite(Value.M14) &&
        IsFinite(Value.M21) && IsFinite(Value.M22) && IsFinite(Value.M23) && IsFinite(Value.M24) &&
        IsFinite(Value.M31) && IsFinite(Value.M32) && IsFinite(Value.M33) && IsFinite(Value.M34) &&
        IsFinite(Value.M41) && IsFinite(Value.M42) && IsFinite(Value.M43) && IsFinite(Value.M44);

    private static bool IsFinite(Vector3 Value) => IsFinite(Value.X) && IsFinite(Value.Y) && IsFinite(Value.Z);

    private static bool IsFinite(float Value) => !float.IsNaN(Value) && !float.IsInfinity(Value);
}

/// <summary>画面上のカメラプレビューカードの矩形です。</summary>
internal readonly struct SceneCameraPreviewRect {
    internal SceneCameraPreviewRect(float X, float Y, float Width, float Height) {
        this.X = X;
        this.Y = Y;
        this.Width = Width;
        this.Height = Height;
    }

    internal float X { get; }
    internal float Y { get; }
    internal float Width { get; }
    internal float Height { get; }

    internal bool Contains(float PointX, float PointY) =>
        PointX >= this.X && PointX < this.X + this.Width &&
        PointY >= this.Y && PointY < this.Y + this.Height;
}

/// <summary>Cameraと描画矩形の組み合わせです。</summary>
internal readonly struct SceneCameraPreviewLayoutItem {
    internal SceneCameraPreviewLayoutItem(SceneCameraPreview Camera, SceneCameraPreviewRect Rect, int Index) {
        this.Camera = Camera;
        this.Rect = Rect;
        this.Index = Index;
    }

    internal SceneCameraPreview Camera { get; }
    internal SceneCameraPreviewRect Rect { get; }
    internal int Index { get; }
    internal bool IsPrimary => this.Index == 0;
}

/// <summary>UE風の下端横並びカメラプレビューの配置を決める純粋なポリシーです。</summary>
internal static class CameraPreviewLayoutPolicy {
    internal const float CardAspectRatio = 16f / 9f;
    internal const float HorizontalMargin = 16f;
    internal const float BottomMargin = 16f;
    internal const float Spacing = 12f;
    internal const float MaximumViewportHeightFraction = 0.42f;

    internal static IReadOnlyList<SceneCameraPreviewLayoutItem> Build(
        float ViewportWidth,
        float ViewportHeight,
        IReadOnlyList<SceneCameraPreview> Cameras) {
        if (!IsFinite(ViewportWidth) || !IsFinite(ViewportHeight) || ViewportWidth <= 0f || ViewportHeight <= 0f || Cameras.Count == 0) {
            return Array.Empty<SceneCameraPreviewLayoutItem>();
        }

        int Count = Math.Min(Cameras.Count, SceneCameraPreviewBinding.DefaultMaximumPreviewCount);
        float AvailableWidth = Math.Max(0f, ViewportWidth - (HorizontalMargin * 2f) - (Spacing * (Count - 1)));
        float CardWidth = AvailableWidth / Count;
        float CardHeight = CardWidth / CardAspectRatio;
        float MaximumHeight = ViewportHeight * MaximumViewportHeightFraction;
        if (CardHeight > MaximumHeight) {
            CardHeight = MaximumHeight;
            CardWidth = CardHeight * CardAspectRatio;
        }

        float TotalWidth = (CardWidth * Count) + (Spacing * (Count - 1));
        float StartX = Math.Max(HorizontalMargin, (ViewportWidth - TotalWidth) * 0.5f);
        float StartY = Math.Max(0f, ViewportHeight - BottomMargin - CardHeight);
        List<SceneCameraPreviewLayoutItem> Result = new(Count);
        for (int Index = 0; Index < Count; Index++) {
            float X = StartX + ((CardWidth + Spacing) * Index);
            Result.Add(new SceneCameraPreviewLayoutItem(Cameras[Index],
                new SceneCameraPreviewRect(X, StartY, CardWidth, CardHeight), Index));
        }

        return Result;
    }

    private static bool IsFinite(float Value) => !float.IsNaN(Value) && !float.IsInfinity(Value);
}

/// <summary>ワイプ上のクリックをScene Viewの通常操作より先に解決します。</summary>
internal static class CameraPreviewHitTestPolicy {
    internal static bool TryHit(
        IReadOnlyList<SceneCameraPreviewLayoutItem> Layout,
        float X,
        float Y,
        out SceneCameraPreview Camera) {
        for (int Index = Layout.Count - 1; Index >= 0; Index--) {
            SceneCameraPreviewLayoutItem Item = Layout[Index];
            if (Item.Rect.Contains(X, Y)) {
                Camera = Item.Camera;
                return true;
            }
        }

        Camera = default;
        return false;
    }

    internal static bool TryHit(
        IReadOnlyList<SceneCameraPreviewLayoutItem> Layout,
        float X,
        float Y,
        out NodePath Path) {
        if (TryHit(Layout, X, Y, out SceneCameraPreview Camera)) {
            Path = Camera.NodePath;
            return true;
        }

        Path = default;
        return false;
    }
}
