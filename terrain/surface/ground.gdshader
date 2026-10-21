// The ground: up to eight textures splatted by per-vertex weights (CUSTOM0 = textures 0-3, CUSTOM1 = 4-7), blended by
// their height maps so e.g. sand settles in rock cracks, tinted per vertex (COLOR). Only textures with weight at a
// pixel are sampled, usually one or two.
shader_type spatial;

uniform sampler2DArray albedo_height : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_rough : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float uv_scale[8];
uniform int texture_count = 1;
uniform float blend_depth : hint_range(0.01, 1.0) = 0.2; // smaller = crisper height-blend edges
uniform float normal_strength : hint_range(0.0, 2.0) = 1.0;

varying vec4 w0;
varying vec4 w1;
varying vec2 world_xz;

void vertex() {
	w0 = CUSTOM0;
	w1 = CUSTOM1;
	world_xz = VERTEX.xz;
}

void fragment() {
	float weights[8] = float[8](w0.x, w0.y, w0.z, w0.w, w1.x, w1.y, w1.z, w1.w);
	vec4 alb[8];
	vec4 nrm[8];
	float score[8];
	float best = -1.0;
	for (int i = 0; i < 8; i++) {
		score[i] = -1.0;
		if (i >= texture_count || weights[i] < 0.004) continue;
		vec3 uv = vec3(world_xz * uv_scale[i], float(i));
		alb[i] = texture(albedo_height, uv);
		nrm[i] = texture(normal_rough, uv);
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
	ALBEDO = albedo / total * COLOR.rgb;
	normal /= total;
	NORMAL_MAP = normal.rgb;
	NORMAL_MAP_DEPTH = normal_strength;
	ROUGHNESS = normal.a;
	SPECULAR = 0.3;
}
