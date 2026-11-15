// Copyright (c) Richasy. All rights reserved.
#define D2D_INPUT_COUNT 2
#define D2D_INPUT0_SIMPLE
#define D2D_INPUT1_SIMPLE
#define D2D_REQUIRES_SCENE_POSITION
#include "d2d1effecthelpers.hlsli"
cbuffer Constants : register(b0) {
    float4 Cover;
    float4 Viewport;
    float4 Background;
};
float ramp(float t, float a, float b) { return saturate((t-a)/(b-a)); }
D2D_PS_ENTRY(Execute) {
    float2 p = D2DGetScenePosition().xy * Cover.x + Cover.yz;
    float t = saturate((p.x * .8660254038 + (Viewport.y-p.y)*.5)/Viewport.z);
    float weight = 1;
    if (t > .42) weight = lerp(.2,0,ramp(t,.42,.54));
    else if (t > .28) weight = lerp(166.0/255.0,.2,ramp(t,.28,.42));
    else if (t > .14) weight = lerp(1,166.0/255.0,ramp(t,.14,.28));
    float4 color = lerp(D2DGetInput(0),D2DGetInput(1),weight);
    float3 rgb = color.rgb + Background.rgb*(1-color.a);
    float opacity;
    float3 tint;
    if (Cover.w > .5) {
        tint=float3(249,246,235)/255.0;
        if(t>.42) opacity=lerp(.24,0,ramp(t,.42,.56));
        else if(t>.24) opacity=lerp(.56,.24,ramp(t,.24,.42));
        else opacity=lerp(.62,.56,ramp(t,0,.24));
    } else {
        tint=float3(9,22,27)/255.0;
        if(t>.64) opacity=lerp(.25,.16,ramp(t,.64,1));
        else if(t>.32) opacity=lerp(.58,.25,ramp(t,.32,.64));
        else opacity=lerp(.68,.58,ramp(t,0,.32));
    }
    return float4(lerp(rgb,tint,opacity),1);
}
