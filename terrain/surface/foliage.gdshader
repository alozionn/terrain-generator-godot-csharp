// Authored foliage (trees, bushes, plants, grass cards from model packs) with wind. Takes over the imported material:
// albedo texture and colour, alpha cut-out, bark normal map. Sway grows with height above the model's
// base; leaves also flutter, and can bend their normals outward from the canopy for soft, painterly shading.
// Wind and distance thinning come from foliage_common.gdshaderinc, shared with grass.gdshader.
// Lit by a custom light(): leaves wrap the light softly and glow when the sun is behind them; ground cover
// (receive_bake) sits in the baked shade of trees, which cast no realtime shadows.
shader_type spatial;
render_mode cull_disabled;

#include "res://terrain/surface/foliage_common.gdshaderinc"

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform float alpha_cut = 0.0; // 0 = opaque
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal_map = false;
uniform float roughness = 0.9;
// Recolour: use the texture only for its light and dark, and colour by a root-to-tip gradient (palette-driven grass,
// bushes whose pack texture is the wrong colour).
uniform bool recolor = false;
uniform vec3 root_color : source_color = vec3(0.3, 0.45, 0.15);
uniform vec3 tip_color : source_color = vec3(0.7, 0.85, 0.4);

group_uniforms model_wind;
uniform float sway = 0.2; // metres at model_height
uniform float model_height = 5.0;
uniform float flutter = 0.0; // leaves: extra small motion in metres
uniform float sway_speed = 1.0; // big, heavy models (trees) move slower than grass in the same wind
group_uniforms leaves;
uniform float leaf_normals : hint_range(0.0, 1.0) = 0.0; // bend normals outward from the canopy centre
uniform float canopy_height = 3.5;
// Darker deep inside and under the canopy, so trees keep their volume without casting realtime shadows on themselves.
uniform float canopy_ao : hint_range(0.0, 1.0) = 0.0;
group_uniforms lighting;
uniform bool receive_bake = false; // sit in the baked decor shadow (ground cover, bushes)
uniform float wrap = 0.4;
// Leaves are lit in soft bands (lit, half, shade) mixed with the smooth gradient by this much: painterly, not mushy.
uniform float bands : hint_range(0.0, 1.0) = 0.6;
// ± brightness per clump of leaves (cards near each other share it), so clusters separate even in flat light.
uniform float clump_variation : hint_range(0.0, 0.5) = 0.14;
uniform float clump_size = 0.6; // metres, in model units
// Keeps cut-out leaves from thinning away in the smaller mip levels, so distant canopies keep a leafy silhouette
// instead of melting into a blob.
uniform float mip_alpha : hint_range(0.0, 1.0) = 0.3;
uniform float backlight = 0.25;
group_uniforms;

varying float v_height;
varying float v_bake;
varying float l_bake;
varying float l_leaf;
varying float v_clump;
varying float l_bands;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float d = distance(origin, CAMERA_POSITION_WORLD);
	float keep = foliage_keep(foliage_distance(origin, CAMERA_POSITION_WORLD), d);
	VERTEX *= foliage_grow(keep, origin) * foliage_spread(keep);

	// clump id from the rest position, before wind, so it never flickers
	v_clump = fract(sin(dot(floor(VERTEX / clump_size), vec3(12.9898, 78.233, 37.719))) * 43758.5453) * 2.0 - 1.0;
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float h = clamp(VERTEX.y / model_height, 0.0, 1.5);
	v_height = clamp(h, 0.0, 1.0);
	float t = TIME * sway_speed;
	vec3 push = foliage_wind(world, origin, h, sway, t);
	push += flutter * h * vec3(sin(t * 2.6 + world.x * 1.3 + world.y * 0.9), sin(t * 2.1 + world.z * 1.2) * 0.5, cos(t * 2.9 + world.z * 1.1));
	VERTEX += foliage_to_local(push, MODEL_MATRIX);

	if (leaf_normals > 0.0)
		NORMAL = normalize(mix(NORMAL, normalize(VERTEX - vec3(0.0, canopy_height, 0.0)), leaf_normals));
	v_bake = receive_bake ? terrain_shadow_at(origin.xz) : 1.0;
}

void fragment() {
	vec4 tex = texture(albedo_texture, UV);
	// built-in scissor rather than a manual discard, so Godot can use its alpha-scissor depth and shadow paths
	// scale alpha up in the smaller mips (Golus' trick), so leaf cut-outs keep their coverage at a distance
	float mip = max(textureQueryLod(albedo_texture, UV).y, 0.0);
	ALPHA = alpha_cut > 0.0 ? clamp(tex.a * (1.0 + mip * mip_alpha), 0.0, 1.0) : 1.0;
	ALPHA_SCISSOR_THRESHOLD = max(alpha_cut, 0.001);
	vec3 base = tex.rgb * albedo_color.rgb;
	if (recolor) {
		float luma = dot(tex.rgb, vec3(0.299, 0.587, 0.114));
		base = mix(root_color, tip_color, smoothstep(0.0, 1.0, v_height)) * mix(0.8, 1.15, clamp(luma * 2.0, 0.0, 1.0));
	}
	// leaf clumps vary a little in brightness and lean warmer when brighter, like sunlit leaves
	float vary = clump_variation * v_clump * step(0.001, leaf_normals);
	base *= 1.0 + vary;
	base.r *= 1.0 + vary * 0.4;
	ALBEDO = base * COLOR.rgb; // COLOR: the model's vertex colour times the instance tint
	// Godot flips the normal on back faces of double-sided surfaces; undo it, so a leaf card seen from behind keeps
	// its outward canopy normal instead of facing into the tree (that turned the far side of canopies black)
	if (!FRONT_FACING) NORMAL = -NORMAL;
	if (use_normal_map) NORMAL_MAP = texture(normal_texture, UV).rgb;
	ROUGHNESS = roughness;
	SPECULAR = 0.2;
	AO = mix(1.0 - canopy_ao, 1.0, smoothstep(0.35, 1.0, v_height));
	AO_LIGHT_AFFECT = 0.5;
	l_bake = v_bake;
	l_leaf = leaf_normals;
	l_bands = bands * step(0.001, leaf_normals);
}

void light() {
	float shadow = ATTENUATION * (LIGHT_IS_DIRECTIONAL ? l_bake : 1.0);
	float w = wrap * step(0.001, l_leaf);
	float diffuse = clamp((dot(NORMAL, LIGHT) + w) / (1.0 + w), 0.0, 1.0);
	float stepped = 0.25 + 0.4 * smoothstep(0.18, 0.26, diffuse) + 0.35 * smoothstep(0.58, 0.66, diffuse);
	diffuse = mix(diffuse, stepped, l_bands);
	float through = l_leaf * backlight * pow(clamp(dot(-VIEW, LIGHT), 0.0, 1.0), 3.0) * 2.0;
	DIFFUSE_LIGHT += (diffuse + through) * shadow * LIGHT_COLOR / PI;
	// a soft sheen, so bark and rocks are not completely matte
	vec3 half_v = normalize(LIGHT + VIEW);
	SPECULAR_LIGHT += pow(clamp(dot(NORMAL, half_v), 0.0, 1.0), 24.0) * (1.0 - ROUGHNESS) * 0.3 * shadow * LIGHT_COLOR / PI;
}
