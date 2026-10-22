// Shared by grass.gdshader and foliage.gdshader: wind, distance thinning, and the baked decor shadow lookup.

#include "res://terrain/surface/terrain_shadow.gdshaderinc"

group_uniforms wind;
uniform vec2 wind_direction = vec2(1.0, 0.35);
uniform float wind_speed = 1.3;
uniform float gust_scale = 0.06; // cycles per metre of the rolling gusts
group_uniforms distance;
// Instances thin out with distance instead of shrinking: each has its own random cut-off, so the edge is a soft
// circle around the camera. Between thin.x and thin.y the kept fraction falls to thin_min; survivors grow by
// compensate so the ground stays as covered. Everything is gone between fade.x and fade.y (true distance).
uniform vec2 thin = vec2(1e6, 2e6);
uniform float thin_min : hint_range(0.0, 1.0) = 0.0;
uniform vec2 fade = vec2(1e6, 2e6);
uniform float compensate : hint_range(0.0, 1.0) = 0.0;
// Looking straight down, thinning starts later: the camera sees between instances there, while at a grazing angle
// they overlap and fewer are needed. Distance is scaled by this when looking down, by 1 at the horizon.
uniform float top_down : hint_range(0.2, 1.0) = 0.6;
group_uniforms;

float foliage_hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

// Distance for thinning, shortened when looking down at the instance.
float foliage_distance(vec3 origin, vec3 camera) {
	vec3 to = origin - camera;
	float d = length(to);
	float horizontal = length(to.xz) / max(d, 1e-3);
	return d * mix(top_down, 1.0, horizontal);
}

// Fraction of instances kept at this distance (before the per-instance cut-off).
float foliage_keep(float effective, float true_distance) {
	return mix(1.0, thin_min, smoothstep(thin.x, thin.y, effective)) * (1.0 - smoothstep(fade.x, fade.y, true_distance));
}

// 0..1 size of one instance given the kept fraction: grows in over a short band as the camera comes closer.
// Cut-offs stop at 0.9 so that with nothing thinned every instance is full size.
float foliage_grow(float keep, vec3 origin) {
	return clamp((keep - 0.9 * foliage_hash(origin.xz)) * 10.0, 0.0, 1.0);
}

// How much a survivor spreads out so the kept ones cover what the dropped ones did.
float foliage_spread(float keep) {
	return pow(max(keep, 0.05), -0.5 * compensate);
}

// World-space wind push of a point at height factor h (0 root .. 1 top), in metres of sway.
vec3 foliage_wind(vec3 world, vec3 origin, float h, float sway, float time) {
	vec2 dir = normalize(wind_direction);
	float gust = sin(time * wind_speed + dot(origin.xz, dir) * gust_scale * 6.2832) * 0.5 + 0.5;
	float bend = sway * (0.3 + 0.7 * gust) * h * h;
	return vec3(dir.x, 0.0, dir.y) * bend;
}

// World push → local offset (instance transforms are rotation + uniform scale).
vec3 foliage_to_local(vec3 push, mat4 model) {
	float s2 = dot(model[0].xyz, model[0].xyz);
	return (vec4(push, 0.0) * model).xyz / s2;
}

