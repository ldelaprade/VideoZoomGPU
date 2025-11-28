// Sampler for the video texture
sampler2D Input : register(S0);

float CropX : register(C0);
float CropY : register(C1);
float CropWidth : register(C2);
float CropHeight : register(C3);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    // Map UV to crop region
    float2 cropUV = float2(
        CropX + uv.x * CropWidth,
        CropY + uv.y * CropHeight
    );
    
    return tex2D(Input, cropUV);
}