#version 450

#include "../Core/Shaders/Common/Global.glsl"
#include "../Core/Shaders/Common/Gauges.glsl"

layout(set = 1, binding = 1) uniform sampler2D image1;
layout(set = 1, binding = 2) uniform sampler2D image2;

layout(location = 0) out vec4 outColor;

void main()
{
  vec2 instSz = instUv.zw - instUv.xy;
  vec2 locUv = (inUv - instUv.xy) / instSz;

  vec4 cr1 = textureLod(image1, locUv, 0.0);
  vec4 cr2 = textureLod(image2, locUv, 0.0);

  outColor = mix(cr1, cr2, locUv.x);
}