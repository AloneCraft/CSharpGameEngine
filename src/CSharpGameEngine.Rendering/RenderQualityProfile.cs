namespace CSharpGameEngine.Rendering;

public enum RenderAntiAliasingMode:byte {
    None,
    Fxaa,
}

public enum RenderExposureMode:byte {
    CameraEv100,
    FixedEv100,
}

public readonly struct RenderExposurePolicy {
    private RenderExposurePolicy(RenderExposureMode Mode,float FixedEv100){
        this.Mode=Mode;
        this.FixedEv100=FixedEv100;
    }
    public RenderExposureMode Mode{get;}
    public float FixedEv100{get;}
    public static RenderExposurePolicy Camera{get;}=new(RenderExposureMode.CameraEv100,0f);
    public static RenderExposurePolicy Fixed(float Ev100){
        if(!float.IsFinite(Ev100)||Ev100 is <-32f or >32f){
            throw new ArgumentOutOfRangeException(nameof(Ev100),"Fixed exposure must be finite and within 32 stops of neutral.");
        }
        return new(RenderExposureMode.FixedEv100,Ev100);
    }
    public float Resolve(float CameraEv100){
        float Ev=this.Mode switch {
            RenderExposureMode.CameraEv100 when float.IsFinite(CameraEv100)
                &&CameraEv100 is >=-32f and <=32f=>CameraEv100,
            RenderExposureMode.CameraEv100=>throw new ArgumentOutOfRangeException(nameof(CameraEv100)),
            RenderExposureMode.FixedEv100=>this.FixedEv100,
            _=>throw new InvalidOperationException("The exposure policy is uninitialized."),
        };
        return MathF.Pow(2f,-Ev);
    }
    internal bool IsValid=>this.Mode==RenderExposureMode.CameraEv100
        ||(this.Mode==RenderExposureMode.FixedEv100
            &&float.IsFinite(this.FixedEv100)&&this.FixedEv100 is >=-32f and <=32f);
}

public readonly struct RenderShadowQuality {
    private RenderShadowQuality(int CascadeCount,uint Resolution){
        this.CascadeCount=CascadeCount;
        this.Resolution=Resolution;
    }
    public int CascadeCount{get;}
    public uint Resolution{get;}
    public static RenderShadowQuality Create(int CascadeCount,uint Resolution){
        if(CascadeCount is not (1 or 2 or 4)){
            throw new ArgumentOutOfRangeException(nameof(CascadeCount),"Shadow cascades must be one, two, or four.");
        }
        if(Resolution is <256 or >4096||(Resolution&(Resolution-1))!=0){
            throw new ArgumentOutOfRangeException(nameof(Resolution),"Shadow resolution must be a power of two from 256 through 4096.");
        }
        return new(CascadeCount,Resolution);
    }
    internal bool IsValid=>this.CascadeCount is 1 or 2 or 4
        &&this.Resolution is >=256 and <=4096
        &&(this.Resolution&(this.Resolution-1))==0;
}

/// <summary>Immutable render policy; absence of shadow data represents disabled shadows.</summary>
public sealed class RenderQualityProfile {
    private RenderQualityProfile(
        string Name,RenderShadowQuality?Shadows,bool ImageBasedLighting,
        bool Bloom,bool AcesToneMapping,RenderAntiAliasingMode AntiAliasing,
        RenderExposurePolicy Exposure){
        this.Name=Name;
        this.Shadows=Shadows;
        this.ImageBasedLighting=ImageBasedLighting;
        this.Bloom=Bloom;
        this.AcesToneMapping=AcesToneMapping;
        this.AntiAliasing=AntiAliasing;
        this.Exposure=Exposure;
    }

    public string Name{get;}
    public RenderShadowQuality?Shadows{get;}
    public bool ImageBasedLighting{get;}
    public bool Bloom{get;}
    public bool AcesToneMapping{get;}
    public RenderAntiAliasingMode AntiAliasing{get;}
    public RenderExposurePolicy Exposure{get;}

    public static RenderQualityProfile Compatibility{get;}=Create(
        "Compatibility",RenderShadowQuality.Create(1,1024),
        ImageBasedLighting:false,Bloom:true,AcesToneMapping:true,
        RenderAntiAliasingMode.None,RenderExposurePolicy.Camera);
    public static RenderQualityProfile Balanced{get;}=Create(
        "Balanced",RenderShadowQuality.Create(2,2048),
        ImageBasedLighting:true,Bloom:true,AcesToneMapping:true,
        RenderAntiAliasingMode.Fxaa,RenderExposurePolicy.Camera);
    public static RenderQualityProfile Cinematic{get;}=Create(
        "Cinematic",RenderShadowQuality.Create(4,4096),
        ImageBasedLighting:true,Bloom:true,AcesToneMapping:true,
        RenderAntiAliasingMode.Fxaa,RenderExposurePolicy.Camera);

    public static RenderQualityProfile Create(
        string Name,RenderShadowQuality?Shadows,
        bool ImageBasedLighting,bool Bloom,bool AcesToneMapping,
        RenderAntiAliasingMode AntiAliasing,RenderExposurePolicy Exposure){
        if(string.IsNullOrWhiteSpace(Name)){ throw new ArgumentException("A quality profile requires a name.",nameof(Name)); }
        if(Shadows is { }Shadow&&!Shadow.IsValid){ throw new ArgumentException("Shadow quality is invalid.",nameof(Shadows)); }
        if(AntiAliasing is not (RenderAntiAliasingMode.None or RenderAntiAliasingMode.Fxaa)){
            throw new ArgumentOutOfRangeException(nameof(AntiAliasing));
        }
        if(!Exposure.IsValid){ throw new ArgumentException("Exposure policy is invalid.",nameof(Exposure)); }
        return new(Name,Shadows,ImageBasedLighting,Bloom,AcesToneMapping,AntiAliasing,Exposure);
    }
}

public sealed class RenderQualityPlan {
    private RenderQualityPlan(RenderQualityProfile Profile,RenderGraph Graph){
        this.Profile=Profile;
        this.Graph=Graph;
    }
    public RenderQualityProfile Profile{get;}
    public RenderGraph Graph{get;}

    public static RenderQualityPlan Build(
        uint Width,uint Height,RenderQualityProfile Profile){
        if(Width==0){ throw new ArgumentOutOfRangeException(nameof(Width)); }
        if(Height==0){ throw new ArgumentOutOfRangeException(nameof(Height)); }
        ArgumentNullException.ThrowIfNull(Profile);
        RenderGraphBuilder Builder=new();
        var BackBuffer=Builder.Import<BackBufferResource>(
            "BackBuffer",RenderResourceDescriptor.Texture(Width,Height,"rgba8"));
        var Depth=Builder.CreateTransient<DepthResource>(
            "Depth",RenderResourceDescriptor.Texture(Width,Height,"d32"));
        var Hdr=Builder.CreateTransient<HdrResource>(
            "HdrColor",RenderResourceDescriptor.Texture(Width,Height,"rgba16f"));
        Builder.AddPass("DepthPrepass",Pass=>Pass.Write(Depth));

        RenderResourceHandle<ShadowResource>?ShadowMap=null;
        if(Profile.Shadows is { }Shadows){
            uint AtlasWidth=checked(Shadows.Resolution*(uint)Shadows.CascadeCount);
            ShadowMap=Builder.CreateTransient<ShadowResource>(
                "ShadowCascades",
                RenderResourceDescriptor.Texture(AtlasWidth,Shadows.Resolution,"d32"));
            RenderResourceHandle<ShadowResource>Captured=ShadowMap.Value;
            Builder.AddPass("ShadowCascades",Pass=>Pass.Write(Captured));
        }

        RenderResourceHandle<EnvironmentResource>?EnvironmentMap=null;
        if(Profile.ImageBasedLighting){
            EnvironmentMap=Builder.Import<EnvironmentResource>(
                "Environment",RenderResourceDescriptor.Texture(512,512,"rgba16f"));
        }
        Builder.AddPass("Opaque",Pass=>{
            Pass.Read(Depth);
            if(ShadowMap is { }Shadow){ Pass.Read(Shadow); }
            if(EnvironmentMap is { }Environment){ Pass.Read(Environment); }
            Pass.Write(Hdr);
        });

        RenderResourceHandle<BloomResource>?Bloom=null;
        if(Profile.Bloom){
            Bloom=Builder.CreateTransient<BloomResource>(
                "Bloom",RenderResourceDescriptor.Texture(
                    Math.Max(1u,Width/2),Math.Max(1u,Height/2),"rgba16f"));
            RenderResourceHandle<BloomResource>Captured=Bloom.Value;
            Builder.AddPass("Bloom",Pass=>{
                Pass.Read(Hdr);
                Pass.Write(Captured);
            });
        }

        var Ldr=Builder.CreateTransient<LdrResource>(
            "LdrColor",RenderResourceDescriptor.Texture(Width,Height,"rgba8"));
        Builder.AddPass(Profile.AcesToneMapping?"AcesToneMap":"LinearComposite",Pass=>{
            Pass.Read(Hdr);
            if(Bloom is { }BloomTexture){ Pass.Read(BloomTexture); }
            Pass.Write(Ldr);
        });

        if(Profile.AntiAliasing==RenderAntiAliasingMode.Fxaa){
            var Antialiased=Builder.CreateTransient<AntialiasedResource>(
                "Antialiased",RenderResourceDescriptor.Texture(Width,Height,"rgba8"));
            Builder.AddPass("Fxaa",Pass=>{
                Pass.Read(Ldr);
                Pass.Write(Antialiased);
            });
            Builder.AddPass("Present",Pass=>{
                Pass.Read(Antialiased);
                Pass.Write(BackBuffer);
            });
        } else {
            Builder.AddPass("Present",Pass=>{
                Pass.Read(Ldr);
                Pass.Write(BackBuffer);
            });
        }
        return new(Profile,Builder.Compile());
    }

    private sealed class BackBufferResource;
    private sealed class DepthResource;
    private sealed class HdrResource;
    private sealed class ShadowResource;
    private sealed class EnvironmentResource;
    private sealed class BloomResource;
    private sealed class LdrResource;
    private sealed class AntialiasedResource;
}
