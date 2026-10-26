"""ADR-0083: shared world-space PCSS for cascaded and virtual directional shadows."""
import re

HELPERS = r'''
Texture2D VirtualShadowAtlas : register(t18);
StructuredBuffer<uint> VirtualShadowPages : register(t19);
bool ReadDirectionalSoftDepth(int mode,int level,int2 texel,out float depth) {
    int resolution=mode==0?512:4096;
    depth=1.0;
    if(any(texel<0)||any(texel>=resolution)) return false;
    if(mode==0) {
        float4 tile=CascadeUvScaleOffsets[level];
        int2 atlas=(int2)(((float2(texel)+0.5)/512.0*tile.xy+tile.zw)*4096.0);
        depth=ShadowAtlas.Load(int3(atlas,0)).r;
    } else {
        uint2 page=(uint2)texel/128;
        uint physical=VirtualShadowPages[(uint)VirtualShadowParams.y+level*1024+page.y*32+page.x];
        if(physical==0) return false;
        physical-=1;
        int2 atlas=int2(physical%32,physical/32)*128+texel%128;
        depth=VirtualShadowAtlas.Load(int3(atlas,0)).r;
    }
    return true;
}
float2 DirectionalSoftDisk(int index,int count) {
    float angle=(float)index*2.39996323;
    return float2(cos(angle),sin(angle))*sqrt(((float)index+0.5)/(float)count);
}
bool DirectionalSoftBilinear(int mode,int level,float2 sampleUv,float2 centerUv,float receiverDepth,float2 gradient,float bias,out float visibility) {
    float resolution=mode==0?512.0:4096.0;
    float2 pixel=sampleUv*resolution-0.5;
    int2 baseTexel=(int2)floor(pixel);
    float2 fraction=frac(pixel);
    visibility=0.0;
    for(int y=0;y<2;y++) {
        for(int x=0;x<2;x++) {
            int2 texel=baseTexel+int2(x,y);
            float depth;
            if(!ReadDirectionalSoftDepth(mode,level,texel,depth)) return false;
            float receiver=receiverDepth+dot(gradient,(float2(texel)+0.5)/resolution-centerUv);
            float weight=(x==0?1.0-fraction.x:fraction.x)*(y==0?1.0-fraction.y:fraction.y);
            visibility+=weight*(receiver-bias<=depth?1.0:0.0);
        }
    }
    return true;
}
bool FilterDirectionalSoft(int mode,int level,float2 uv,float receiverDepth,float2 gradient,float worldSpan,float depthRange,float biasMeters,out float visibility) {
    float bias=max(biasMeters,0.001)/depthRange;
    visibility=1.0;
    if(DirectionalSoftShadowParams.x==0.0) return DirectionalSoftBilinear(mode,level,uv,uv,receiverDepth,gradient,bias,visibility);
    float gapSum=0.0;
    float blockers=0.0;
    float resolution=mode==0?512.0:4096.0;
    float searchRadius=DirectionalSoftShadowParams.y/worldSpan;
    for(int i=0;i<33;i++) {
        float2 offset=i==0?float2(0.0,0.0):DirectionalSoftDisk(i-1,32)*searchRadius;
        int2 texel=(int2)floor((uv+offset)*resolution);
        float depth;
        if(!ReadDirectionalSoftDepth(mode,level,texel,depth)) return false;
        float receiver=receiverDepth+dot(gradient,(float2(texel)+0.5)/resolution-uv);
        if(depth<1.0&&receiver-depth>bias) {
            gapSum+=(receiver-depth)*depthRange;
            blockers+=1.0;
        }
    }
    if(blockers==0.0) return DirectionalSoftBilinear(mode,level,uv,uv,receiverDepth,gradient,bias,visibility);
    float radius=min((gapSum/blockers)*DirectionalSoftShadowParams.x,DirectionalSoftShadowParams.y)/worldSpan;
    visibility=0.0;
    for(int i=0;i<64;i++) {
        float sampleVisibility;
        if(!DirectionalSoftBilinear(mode,level,uv+DirectionalSoftDisk(i,64)*radius,uv,receiverDepth,gradient,bias,sampleVisibility)) return false;
        visibility+=sampleVisibility;
    }
    visibility/=64.0;
    return true;
}
float ComputeMatchedDirectionalShadow(float3 worldPosition,float3 normal,float biasMeters,int mode,out bool complete) {
    complete=true;
    float3 dx=ddx(worldPosition);
    float3 dy=ddy(worldPosition);
    float3 biased=worldPosition+normal*max(biasMeters,0.001);
    for(int level=0;level<4;level++) {
        float worldSpan=mode==0?2.0*CascadeSpheres[level].w:64.0*exp2((float)level);
        if(mode==0&&dot(worldPosition-CascadeSpheres[level].xyz,worldPosition-CascadeSpheres[level].xyz)>=CascadeSpheres[level].w*CascadeSpheres[level].w*0.96) continue;
        float4 p=mode==0?mul(float4(biased,1.0),CascadeViewProjection[level]):mul(float4(biased,1.0),VirtualShadowProjection[level]);
        float2 uv=float2(p.x*0.5+0.5,0.5-p.y*0.5);
        float guard=(DirectionalSoftShadowParams.x==0.0?0.0:DirectionalSoftShadowParams.y)/worldSpan+1.0/(mode==0?512.0:4096.0);
        if(any(uv<guard)||any(uv>1.0-guard)||p.z<0.0||p.z>1.0) continue;
        float3 pdx=mode==0?mul(float4(dx,0.0),CascadeViewProjection[level]).xyz:mul(float4(dx,0.0),VirtualShadowProjection[level]).xyz;
        float3 pdy=mode==0?mul(float4(dy,0.0),CascadeViewProjection[level]).xyz:mul(float4(dy,0.0),VirtualShadowProjection[level]).xyz;
        float2 duvdx=float2(pdx.x*0.5,-pdx.y*0.5);
        float2 duvdy=float2(pdy.x*0.5,-pdy.y*0.5);
        float determinant=duvdx.x*duvdy.y-duvdx.y*duvdy.x;
        float2 gradient=abs(determinant)>1e-16?float2(pdx.z*duvdy.y-pdy.z*duvdx.y,pdy.z*duvdx.x-pdx.z*duvdy.x)/determinant:float2(0.0,0.0);
        float visibility;
        float depthRange=mode==0?CascadeDepthRanges[level]:VirtualShadowParams.w;
        complete=FilterDirectionalSoft(mode,level,uv,p.z,gradient,worldSpan,depthRange,biasMeters,visibility);
        return visibility;
    }
    complete=mode==0;
    return 1.0;
}
'''

def edit(source):
    source=source.replace('    float4 VirtualShadowParams;', '    float4 VirtualShadowParams;\n    float4 DirectionalSoftShadowParams;')
    source=re.sub(r'Texture2D VirtualShadowAtlas : register\(t18\);\s*StructuredBuffer<uint> VirtualShadowPages : register\(t19\);','',source)
    position=source.index('float ComputeCascadedShadow(')
    source=source[:position]+HELPERS+'\n'+source[position:]
    for name,branch in [
        ('ComputeCascadedShadow', '''
    if(DirectionalSoftShadowParams.z>0.0) {
        bool complete;
        return ComputeMatchedDirectionalShadow(worldPosition,normal,biasMeters,0,complete);
    }
'''),
        ('ComputeVirtualDirectionalShadow', '''
    if(DirectionalSoftShadowParams.z>0.0) {
        bool complete;
        if(VirtualShadowParams.x==0.0||distance(worldPosition,CameraPositionWS.xyz)>200.0) return ComputeMatchedDirectionalShadow(worldPosition,normal,biasMeters,0,complete);
        float visibility=ComputeMatchedDirectionalShadow(worldPosition,normal,biasMeters,1,complete);
        if(complete) return visibility;
        return ComputeMatchedDirectionalShadow(worldPosition,normal,biasMeters,0,complete);
    }
''')]:
        position=source.index('{',source.index('float '+name+'('))+1
        source=source[:position]+branch+source[position:]
    return source
