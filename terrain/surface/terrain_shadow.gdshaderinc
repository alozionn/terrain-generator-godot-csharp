// Baked shadow of fixed decor (tree canopies, bushes) on the ground, written by the terrain node: R = 1 lit,
// 0 shadowed. terrain_shadow_rect maps world xz to its uv (x0, z0, 1/width, 1/depth); strength 0 switches it off.
// Shaders apply it to the sun only, in their light() function.
global uniform sampler2D terrain_shadow;
global uniform vec4 terrain_shadow_rect;
global uniform float terrain_shadow_strength;

float terrain_shadow_at(vec2 xz) {
	if (terrain_shadow_strength <= 0.0) return 1.0;
	vec2 uv = (xz - terrain_shadow_rect.xy) * terrain_shadow_rect.zw;
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return 1.0;
	return mix(1.0, textureLod(terrain_shadow, uv, 0.0).r, terrain_shadow_strength);
}
