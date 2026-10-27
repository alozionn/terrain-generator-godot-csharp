// The ground: up to eight textures splatted by per-vertex weights (CUSTOM0 = textures 0-3, CUSTOM1 = 4-7), blended by
// their height maps so e.g. sand settles in rock cracks, tinted per vertex (COLOR). Only textures with weight at a
// pixel are sampled, usually one or two.
// CUSTOM2 is the colour grass and other ground cover paint under themselves (rgb) and how much (a). It fades in with
// distance, where blades thin out, so a meadow still reads as one from far away.
// Lit by a custom light() that applies the baked decor shadow (tree canopies) to the sun only.
shader_type spatial;

#include "res://terrain/surface/terrain_shadow.gdshaderinc"

uniform sampler2DArray albedo_height : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_rough : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float uv_scale[8];
uniform float strata[8]; // rock layers per metre of height on steep ground (0 = none)
uniform float strata_shade[8]; // how much each layer lightens or darkens
uniform int texture_count = 1;
uniform float blend_depth : hint_range(0.01, 1.0) = 0.2; // smaller = crisper height-blend edges
uniform float normal_strength : hint_range(0.0, 2.0) = 1.0;
uniform float detail_bias : hint_range(0.0, 6.0) = 0.0; // mip bias: keeps a texture's colours, drops its fine detail
group_uniforms cover;
uniform vec2 cover_fade = vec2(25.0, 140.0); // metres where cover goes from cover_near to full
uniform float cover_near : hint_range(0.0, 1.0) = 0.3;
uniform float cover_far : hint_range(0.0, 1.0) = 0.85;
group_uniforms;

varying vec4 w0;
varying vec4 w1;
varying vec4 cover;
varying vec2 world_xz;
varying vec3 local_pos;
varying vec3 local_normal;
varying float l_bake;

void vertex() {
	w0 = CUSTOM0;
	w1 = CUSTOM1;
	cover = CUSTOM2;
	world_xz = VERTEX.xz;
	local_pos = VERTEX;
	local_normal = NORMAL;
}

// One ground texture: projected straight down where the ground is flat, from all three axes where it is steep.
void sample_texture(int i, vec3 tri, out vec4 alb, out vec4 nrm) {
	float s = uv_scale[i];
	if (tri.y > 0.96) {
		vec3 uv = vec3(local_pos.xz * s, float(i));
		alb = texture(albedo_height, uv, detail_bias);
		nrm = texture(normal_rough, uv, detail_bias);
		return;
	}
	vec3 a = vec3(local_pos.zy * s, float(i)), b = vec3(local_pos.xz * s, float(i)), c = vec3(local_pos.xy * s, float(i));
	alb = texture(albedo_height, a, detail_bias) * tri.x + texture(albedo_height, b, detail_bias) * tri.y + texture(albedo_height, c, detail_bias) * tri.z;
	// side projections reuse the tangent-space normal as is: not exact, but at board distance it reads as rock
	nrm = texture(normal_rough, a, detail_bias) * tri.x + texture(normal_rough, b, detail_bias) * tri.y + texture(normal_rough, c, detail_bias) * tri.z;
	if (strata[i] > 0.0) {
		// flat-shaded layers, wavy so they follow no grid: cliffs read as stacked stone instead of a stretched texture
		float warp = (alb.a - 0.5) * 0.8 + sin(local_pos.x * 0.13 + local_pos.z * 0.09) * 0.7 + sin(local_pos.z * 0.31 - local_pos.x * 0.07) * 0.3;
		float layer = floor(local_pos.y * strata[i] + warp);
		float shade = fract(sin(layer * 12.9898) * 43758.5453) * 2.0 - 1.0;
		alb.rgb *= 1.0 + shade * strata_shade[i] * smoothstep(0.95, 0.75, normalize(local_normal).y);
	}
}

void fragment() {
	float weights[8] = float[8](w0.x, w0.y, w0.z, w0.w, w1.x, w1.y, w1.z, w1.w);
	vec4 alb[8];
	vec4 nrm[8];
	float score[8];
	float best = -1.0;
	// steep ground is textured from all three axes, so cliffs keep their detail instead of stretching
	vec3 tri = pow(abs(normalize(local_normal)), vec3(4.0));
	tri /= tri.x + tri.y + tri.z;
	for (int i = 0; i < 8; i++) {
		score[i] = -1.0;
		if (i >= texture_count || weights[i] < 0.004) continue;
		sample_texture(i, tri, alb[i], nrm[i]);
		score[i] = alb[i].a + weights[i] * 2.0;
		best = max(best, score[i]);
	}
	vec3 albedo = vec3(0.0);
	vec4 normal = vec4(0.0);
	float total = 0.0;
	for (int i = 0; i < 8; i++) {
		if (score[i] < 0.0) continue;
		float c = max(score[i] - best + blend_depth, 0.0);
		albedo += alb[i].rgb * c;
		normal += nrm[i] * c;
		total += c;
	}
	total = max(total, 1e-4);
	albedo = albedo / total * COLOR.rgb;
	normal /= total;

	float dist = length(VERTEX); // view space: distance to the camera
	float c = cover.a * mix(cover_near, cover_far, smoothstep(cover_fade.x, cover_fade.y, dist));
	// keep the texture's light and dark under the cover colour, so it never turns into a flat sheet
	float luma = dot(albedo, vec3(0.299, 0.587, 0.114)) / max(dot(COLOR.rgb, vec3(0.299, 0.587, 0.114)), 0.05);
	albedo = mix(albedo, cover.rgb * mix(0.75, 1.25, clamp(luma * 2.0, 0.0, 1.0)), c);

	ALBEDO = albedo;
	NORMAL_MAP = mix(normal.rgb, vec3(0.5, 0.5, 1.0), c * 0.6);
	NORMAL_MAP_DEPTH = normal_strength;
	ROUGHNESS = normal.a;
	SPECULAR = 0.3;
	l_bake = terrain_shadow_at(world_xz);
}

void light() {
	float shadow = ATTENUATION * (LIGHT_IS_DIRECTIONAL ? l_bake : 1.0);
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	DIFFUSE_LIGHT += ndl * shadow * LIGHT_COLOR / PI;
	vec3 half_v = normalize(LIGHT + VIEW);
	float gloss = 1.0 - ROUGHNESS;
	SPECULAR_LIGHT += pow(clamp(dot(NORMAL, half_v), 0.0, 1.0), mix(4.0, 64.0, gloss)) * gloss * 0.25 * ndl * shadow * LIGHT_COLOR / PI;
}
