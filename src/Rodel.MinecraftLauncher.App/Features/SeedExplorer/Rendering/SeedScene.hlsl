struct ModelVertex { float3 position; float2 uv; float shade; };
StructuredBuffer<ModelVertex> Vertices : register(t0);
Texture2D<float4> Atlas : register(t1);
StructuredBuffer<uint> VertexColors : register(t2);
SamplerState PointSampler : register(s0);
cbuffer Camera : register(b0) { row_major float4x4 ViewProjection; };
struct Fragment { float4 position : SV_Position; float2 uv : TEXCOORD0; float shade : TEXCOORD1; float3 tint : COLOR0; };
Fragment VS(uint id : SV_VertexID)
{
    ModelVertex vertex = Vertices[id];
    Fragment result;
    result.position = mul(float4(vertex.position, 1), ViewProjection);
    result.uv = vertex.uv;
    result.shade = vertex.shade;
    uint color = VertexColors[id];
    result.tint = float3((color >> 16) & 255, (color >> 8) & 255, color & 255) / 255.0;
    return result;
}
float4 PS(Fragment fragment) : SV_Target
{
    float4 color = Atlas.Sample(PointSampler, fragment.uv);
    clip(color.a - 0.1);
    return float4(color.rgb * fragment.tint * fragment.shade, 1);
}
