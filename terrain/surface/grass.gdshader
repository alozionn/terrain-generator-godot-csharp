// Stylised grass and flower clumps. Blades carry their height (0 root .. 1 tip) in UV.y; UV.x > 1.5 marks a
// flower head. UV2 is each vertex's offset from its blade's centre line (xz), so blades can widen. Vertex colour
// (times the instance tint) varies brightness per blade; vertex alpha is the blade's own distance cut-off.
// Lit by a custom light(): soft wrap diffuse, light through the blades from behind, and the baked decor shadow, so
// grass under a tree sits in its shade without any realtime leaf shadows.
shader_type spatial;
render_mode cull_disabled;

#include "res://terrain/surface/foliage_common.gdshaderinc"

group_uniforms colour;
uniform vec3 root_color : source_color = vec3(0.22, 0.36, 0.12);
uniform vec3 tip_color : source_color = vec3(0.62, 0.82, 0.33);
uniform vec3 head_color : source_color = vec3(0.95, 0.82, 0.3);
uniform float root_ao : hint_range(0.0, 1.0) = 0.35;
uniform float backlight : hint_range(0.0, 1.0) = 0.35;
group_uniforms blade_wind;
uniform float wind_strength = 0.18; // metres of tip sway
group_uniforms shading;
uniform float up_normal : hint_range(0.0, 1.0) = 0.85; // bend normals toward the sky so clumps read as soft masses
group_uniforms blades;
// Within a clump, blades drop out one by one between these distances, down to blade_min of them; the survivors
// widen (blade_widen) so the clump keeps its weight. The far LOD mesh holds only the blades kept at blade_min.
uniform vec2 blade_thin = vec2(6.0, 25.0);
uniform float blade_min : hint_range(0.0, 1.0) = 0.3;
uniform float blade_widen : hint_range(0.0, 1.0) = 0.7;
group_uniforms;

varying float v_height;
varying float v_head;
varying vec3 v_tint;
varying float v_bake;
varying float l_bake;

void vertex() {
	v_height = UV.y;
	v_head = step(1.5, UV.x);
	v_tint = COLOR.rgb;

	vec3 origin = MODEL_MATRIX[3].xyz;
	float d = distance(origin, CAMERA_POSITION_WORLD);
	float de = foliage_distance(origin, CAMERA_POSITION_WORLD);
	float keep = foliage_keep(de, d);
	float blade_keep = mix(1.0, blade_min, smoothstep(blade_thin.x, blade_thin.y, de));
	float size = foliage_grow(keep, origin) * clamp((blade_keep - COLOR.a) * 15.0, 0.0, 1.0);
	// survivors widen, and whole clumps spread, so thinning keeps the ground covered
	VERTEX.xz += UV2 * (pow(max(blade_keep, 0.05), -blade_widen) - 1.0);
	VERTEX.xz *= foliage_spread(keep);
	VERTEX *= size;

	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 dir = normalize(wind_direction);
	float gust = sin(TIME * wind_speed + dot(world.xz, dir) * gust_scale * 6.2832) * 0.5 + 0.5;
	float flutter = sin(TIME * wind_speed * 3.1 + world.x * 1.7 + world.z * 1.1);
	float bend = wind_strength * (0.25 + gust * 0.75 + flutter * 0.15) * v_height * v_height * size;
	VERTEX += foliage_to_local(vec3(dir.x * bend, -0.35 * bend * bend, dir.y * bend), MODEL_MATRIX);

	vec3 local_up = normalize((vec4(0.0, 1.0, 0.0, 0.0) * MODEL_MATRIX).xyz);
	NORMAL = normalize(mix(NORMAL, local_up, up_normal));
	v_bake = terrain_shadow_at(origin.xz);
}

void fragment() {
	vec3 blade = mix(root_color, tip_color, smoothstep(0.0, 1.0, v_height));
	vec3 albedo = mix(blade, head_color, v_head) * v_tint;
	if (!FRONT_FACING) NORMAL = -NORMAL; // light both sides of a blade the same, no dark backs
	ALBEDO = albedo;
	AO = mix(1.0 - root_ao, 1.0, smoothstep(0.0, 0.7, v_height));
	AO_LIGHT_AFFECT = 0.4;
	ROUGHNESS = 0.85;
	SPECULAR = 0.15;
	l_bake = v_bake * mix(0.75, 1.0, v_height); // roots sit a little in their own clump's shade
}

void light() {
	float shadow = ATTENUATION * (LIGHT_IS_DIRECTIONAL ? l_bake : 1.0);
	float wrap = clamp((dot(NORMAL, LIGHT) + 0.3) / 1.3, 0.0, 1.0);
	// sun behind the blades shines through them, mostly near the tips
	float through = pow(clamp(dot(-VIEW, LIGHT), 0.0, 1.0), 3.0) * backlight * 2.0;
	DIFFUSE_LIGHT += (wrap + through) * shadow * LIGHT_COLOR / PI;
}
