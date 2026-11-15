#ifdef SKIN_PREVIEW_D2D
#define D2D_INPUT_COUNT 1
#define D2D_INPUT0_COMPLEX
#define D2D_REQUIRES_SCENE_POSITION
#include "d2d1effecthelpers.hlsli"
#endif
cbuffer Scene : register(b0)
{
    row_major float4x4 inverseViewProjection;
    float4 eye;
    float4 viewport;
    float4 background;
    float4 options; // size, grid, visible parts mask, outer visible
    float4 boxes[28]; // min, max per body surface
    float4 faces[336]; // origin, right, down, atlas rect per face
    float4 atlasView; // physical cell, origin xy, active surface
    float4 viewFlags; // atlas mode, overlay present, animated joints, four-sample antialiasing
    float4 overlayBounds; // physical output rectangle
    row_major float4x4 partInverse[7]; // rigid skin part transforms
#ifdef SKIN_PREVIEW_D2D
    float4 previewBounds[14];
    uint4 previewPixels[3636]; // mapped Classic128 texels, packed RGBA8 without unused atlas regions
#endif
};
#ifndef SKIN_PREVIEW_D2D
Texture2D<float4> atlas : register(t0);
Texture2D<float4> resolvedScene : register(t1);
#endif
Texture2D<uint> brushMask : register(t2);
float4 SkinColor(int2 texel, int offset)
{
#ifdef SKIN_PREVIEW_D2D
    float4 rect = faces[offset + 3];
    uint2 local = (uint2)(texel - (int2)rect.xy);
    uint index = (uint)faces[offset].w + local.y * (uint)rect.z + local.x;
    uint color = previewPixels[index / 4][index % 4];
    return float4(color >> 24, (color >> 16) & 255, (color >> 8) & 255, color & 255) / 255.0;
#else
    return atlas.Load(int3(texel,0));
#endif
}
struct Vertex { float4 position : SV_Position; };
Vertex VS(uint id : SV_VertexID)
{
    Vertex result;
    result.position = float4(id == 2 ? 3 : -1, id == 1 ? 3 : -1, 0, 1);
    return result;
}
float SegmentDistance(float2 p, float2 a, float2 b)
{
    float2 d = b - a;
    return length(p - a - saturate(dot(p-a,d) / max(dot(d,d), 1e-8)) * d);
}
float4 Outline(float4 color, uint mask, float2 texel, float4 rect, float2 pixelSize, float2 screen)
{
    if ((mask & 4096) != 0) { color.rgb = lerp(color.rgb, float3(.4,.75,.6), .3); color.a = max(color.a, .3); }
    float distance = 100, selectionDistance = 100;
    float2 scale = 1 / max(pixelSize, .0001);
    float2 p = texel * scale;
    // Neighbor cells allow coverage on both sides of an edge, including transparent texels.
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 cell = floor(texel) + float2(x,y);
        if (any(cell < 0) || any(cell >= rect.zw)) continue;
        uint edges = brushMask.Load(int3(rect.xy + cell, 0));
        if ((edges & 255) != (mask & 255)) continue;
        float2 a = cell * scale, b = (cell + 1) * scale;
        if ((edges & 256) != 0) distance = min(distance, SegmentDistance(p,a,float2(a.x,b.y)));
        if ((edges & 512) != 0) distance = min(distance, SegmentDistance(p,float2(b.x,a.y),b));
        if ((edges & 1024) != 0) distance = min(distance, SegmentDistance(p,a,float2(b.x,a.y)));
        if ((edges & 2048) != 0) distance = min(distance, SegmentDistance(p,float2(a.x,b.y),b));
        if ((edges & 16384) != 0) selectionDistance = min(selectionDistance, SegmentDistance(p,a,float2(a.x,b.y)));
        if ((edges & 32768) != 0) selectionDistance = min(selectionDistance, SegmentDistance(p,float2(b.x,a.y),b));
        if ((edges & 65536) != 0) selectionDistance = min(selectionDistance, SegmentDistance(p,a,float2(b.x,a.y)));
        if ((edges & 131072) != 0) selectionDistance = min(selectionDistance, SegmentDistance(p,float2(a.x,b.y),b));
    }
    float selectionCoverage = 1 - smoothstep(.6, 1.5, selectionDistance);
    float dash = fmod(floor((screen.x + screen.y) / 5), 2);
    float selectionAlpha = selectionCoverage + color.a * (1 - selectionCoverage);
    color = float4((dash * selectionCoverage + color.rgb * color.a * (1 - selectionCoverage)) / max(selectionAlpha, .00001), selectionAlpha);
    float coverage = 1 - smoothstep(1.2, 2.2, distance);
    float white = 1 - smoothstep(.3, 1.1, distance);
    float alpha = coverage + color.a * (1 - coverage);
    return float4((white * coverage + color.rgb * color.a * (1 - coverage)) / max(alpha, .00001), alpha);
}
float4 Flat(float2 position)
{
    float2 texel = (position - atlasView.yz) / atlasView.x;
    if (any(texel < 0) || any(texel >= float2(options.x, viewport.z))) return background;
    uint mask = brushMask.Load(int3(floor(texel), 0));
    uint id = mask & 255;
    if (id == 0) return float4(background.rgb * .75, 1);
    uint box = (id - 1u) / 6u;
    float checker = (((uint)floor(texel.x / 4) + (uint)floor(texel.y / 4)) & 1u) == 0u ? .18 : .26;
    float4 color = SkinColor((int2)floor(texel), 0);
    bool active = (box & 1u) == (uint)atlasView.w && (((uint)options.z & (1u << (box / 2u))) != 0);
    if (active && viewFlags.y > 0) color = Outline(color, mask, texel, float4(0,0,options.x,viewport.z), 1 / atlasView.xx, position);
    float3 result = color.rgb * color.a + checker * (1 - color.a);
    if (!active) result = lerp(background.rgb, result, .22);
    if (options.y > 0 && atlasView.x >= 4 && min(frac(texel.x), frac(texel.y)) < 1 / atlasView.x) result *= .65;
    return float4(result, 1);
}
float4 Scene(float2 screen)
{
    float2 ndc = float2(screen.x / viewport.x * 2 - 1, 1 - screen.y / viewport.y * 2);
    precise float4 farPoint = mul(float4(ndc, 1, 1), inverseViewProjection);
    precise float3 direction = normalize(farPoint.xyz / farPoint.w - eye.xyz);
    float4 farX = mul(float4(ndc + float2(2 / viewport.x, 0), 1, 1), inverseViewProjection);
    float4 farY = mul(float4(ndc - float2(0, 2 / viewport.y), 1, 1), inverseViewProjection);
    float3 directionX = normalize(farX.xyz / farX.w - eye.xyz);
    float3 directionY = normalize(farY.xyz / farY.w - eye.xyz);
    float depths[14]; float4 colors[14];
    int count = 0;
    [loop] for (int box = 0; box < 14; box++)
    {
        float4 low = boxes[box * 2], high = boxes[box * 2 + 1];
        if (any(high.xyz <= low.xyz)) continue;
        if ((((uint)options.z & (1u << (uint)low.w)) == 0) || (high.w > 0 && options.w == 0)) continue;
#ifdef SKIN_PREVIEW_D2D
        [branch] if (any(screen < previewBounds[box].xy) || any(screen > previewBounds[box].zw)) continue;
#endif
        float3 rayOrigin = eye.xyz, rayDirection = direction;
        if (viewFlags.z > 0)
        {
            rayOrigin = mul(float4(eye.xyz, 1), partInverse[(uint)low.w]).xyz;
            rayDirection = mul(float4(direction, 0), partInverse[(uint)low.w]).xyz;
        }
        float3 reciprocal = 1 / (rayDirection + (abs(rayDirection) < 1e-8) * 1e-8);
        float3 a = (low.xyz - rayOrigin) * reciprocal, b = (high.xyz - rayOrigin) * reciprocal;
        float3 nearAxis = min(a, b), farAxis = max(a, b);
        float nearDepth = max(max(nearAxis.x, nearAxis.y), nearAxis.z);
        float farDepth = min(min(farAxis.x, farAxis.y), farAxis.z);
        if (nearDepth < 0 || farDepth < nearDepth) continue;
        float3 position = rayOrigin + nearDepth * rayDirection;
        // Stable face order matches SkinFace: front, back, right, left, top, bottom.
        int face = nearAxis.z >= nearAxis.x && nearAxis.z >= nearAxis.y ? (rayDirection.z < 0 ? 0 : 1)
            : nearAxis.x >= nearAxis.y ? (rayDirection.x > 0 ? 2 : 3) : (rayDirection.y < 0 ? 4 : 5);
        int offset = (box * 6 + face) * 4;
        float3 local = position - faces[offset].xyz;
        float3 rightAxis = faces[offset + 1].xyz, downAxis = faces[offset + 2].xyz;
        float2 uv = float2(dot(local, rightAxis) / dot(rightAxis, rightAxis), dot(local, downAxis) / dot(downAxis, downAxis));
        float4 rect = faces[offset + 3];
        float2 texel = clamp(uv * rect.zw, 0, rect.zw - 0.0001);
        float4 color = SkinColor((int2)(rect.xy + floor(texel)), offset);

        float3 normal = normalize(cross(downAxis, rightAxis));
        if (viewFlags.z > 0) normal = mul(normal, transpose((float3x3)partInverse[(uint)low.w]));
        float shade = saturate(0.78 + 0.22 * dot(normal, normalize(float3(-0.35,0.55,0.76))));
        color.rgb *= shade;
        if (options.y > 0 && min(frac(texel.x), frac(texel.y)) < 0.035) color.rgb *= 0.7;
#ifndef SKIN_PREVIEW_D2D
        uint mask = brushMask.Load(int3(rect.xy + floor(texel), 0));
        uint guide = (mask >> 18) & 15;
        bool brush = viewFlags.y > 0 && all(screen >= overlayBounds.xy) && all(screen <= overlayBounds.zw);
        if (guide != 0 || brush)
        {
            // Analytic neighboring rays are valid even where different pixels enter different box branches.
            float plane = dot(faces[offset].xyz - eye.xyz, normal);
            float denominatorX = dot(directionX, normal), denominatorY = dot(directionY, normal);
            denominatorX = abs(denominatorX) < 1e-6 ? (denominatorX < 0 ? -1e-6 : 1e-6) : denominatorX;
            denominatorY = abs(denominatorY) < 1e-6 ? (denominatorY < 0 ? -1e-6 : 1e-6) : denominatorY;
            float3 deltaX = eye.xyz + directionX * (plane / denominatorX) - position;
            float3 deltaY = eye.xyz + directionY * (plane / denominatorY) - position;
            float2 derivativeX = float2(dot(deltaX,rightAxis) / dot(rightAxis,rightAxis), dot(deltaX,downAxis) / dot(downAxis,downAxis)) * rect.zw;
            float2 derivativeY = float2(dot(deltaY,rightAxis) / dot(rightAxis,rightAxis), dot(deltaY,downAxis) / dot(downAxis,downAxis)) * rect.zw;
            float2 pixelSize = clamp(sqrt(derivativeX * derivativeX + derivativeY * derivativeY), .0001, 128);
            if (guide != 0)
            {
                float2 fromStart = frac(texel) / pixelSize, fromEnd = (1 - frac(texel)) / pixelSize;
                float edge = 100;
                if ((guide & 1) != 0) edge = min(edge, fromStart.x);
                if ((guide & 2) != 0) edge = min(edge, fromEnd.x);
                if ((guide & 4) != 0) edge = min(edge, fromStart.y);
                if ((guide & 8) != 0) edge = min(edge, fromEnd.y);
                float ink = dot(background.rgb, float3(.2126,.7152,.0722)) > .5 ? .15 : .9;
                color = float4(ink.xxx, .32 * (1 - smoothstep(.5, 1.6, edge)));
            }
            if (brush) color = Outline(color, mask, texel, rect, pixelSize, screen);
        }
#endif
        if (color.a == 0) continue;
        int slot = count;
        [loop] for (int move = 0; move < 14; move++)
        {
            if (slot == 0) break;
            if (depths[slot - 1] <= nearDepth) break;
            depths[slot] = depths[slot - 1]; colors[slot] = colors[slot - 1]; slot--;
        }
        depths[slot] = nearDepth; colors[slot] = color; count++;
    }
    float4 result = 0;
    [loop] for (int hit = 0; hit < count; hit++)
    {
        result.rgb += (1 - result.a) * colors[hit].a * colors[hit].rgb;
        result.a += (1 - result.a) * colors[hit].a;
        if (result.a >= 0.99999) break;
    }
    return float4(result.rgb + (1 - result.a) * background.rgb, 1);
}
float4 AntialiasedScene(float2 screen)
{
    // Keep the same four coverage rays, but reuse one Scene invocation and its indexed scratch arrays.
    // Expanding four calls duplicates those arrays and the complete ray/alpha/outline program in DXBC.
    static const float2 offsets[4] = {
        float2(-.375,-.125), float2(.125,-.375), float2(.375,.125), float2(-.125,.375)
    };
    float4 result = 0;
    [loop] for (int sample = 0; sample < 4; sample++)
        result += Scene(screen + offsets[sample]);
    return result * .25;
}
float4 PS(Vertex input) : SV_Target
{
    if (viewFlags.x > 0) return Flat(input.position.xy);
    if (viewFlags.w == 0) return Scene(input.position.xy);
    // Coverage comes from independent rays, so silhouettes are antialiased even though the rasterized
    // primitive is a full-screen triangle. Atlas texels themselves continue to use point sampling.
    return AntialiasedScene(input.position.xy);
}
#ifdef SKIN_PREVIEW_D2D
D2D_PS_ENTRY(Preview)
{
    float2 screen = D2DGetScenePosition().xy / atlasView.xy * viewport.xy;
    float4 result = background;
    [branch] if (all(screen >= overlayBounds.xy) && all(screen <= overlayBounds.zw))
    {
        result = AntialiasedScene(screen);
    }
    return result;
}
#else
float4 Resolve(Vertex input) : SV_Target
{
    if (viewFlags.w > 0)
    {
        // WARP retains a bounded scene. Smoothly reconstruct its AA coverage instead of enlarging steps.
        float2 source = input.position.xy * viewport.xy / float2(eye.w,viewport.w) - .5;
        int2 cell = (int2)floor(source), limit = (int2)viewport.xy - 1;
        float2 f = frac(source);
        float4 a = resolvedScene.Load(int3(clamp(cell,0,limit),0));
        float4 b = resolvedScene.Load(int3(clamp(cell+int2(1,0),0,limit),0));
        float4 c = resolvedScene.Load(int3(clamp(cell+int2(0,1),0,limit),0));
        float4 d = resolvedScene.Load(int3(clamp(cell+1,0,limit),0));
        return lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y);
    }
    float2 texel = floor(input.position.xy * viewport.xy / float2(eye.w, viewport.w));
    return resolvedScene.Load(int3(clamp(texel, 0, viewport.xy - 1), 0));
}

#endif
