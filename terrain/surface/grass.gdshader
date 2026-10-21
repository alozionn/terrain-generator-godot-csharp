// Stylised grass and flower clumps. Blades carry their height (0 root .. 1 tip) in UV.y; UV.x > 1.5 marks a
// flower head. Vertex colour (times the instance tint) varies brightness per blade.
shader_type spatial;
render_mode cull_disabled;

group_uniforms colour;
uniform vec3 root_color : source_color = vec3(0.22, 0.36, 0.12);
uniform vec3 tip_color : source_color = vec3(0.62, 0.82, 0.33);
uniform vec3 head_color : source_color = vec3(0.95, 0.82, 0.3);
uniform float root_ao : hint_range(0.0, 1.0) = 0.35;
uniform float backlight : hint_range(0.0, 1.0) = 0.35;
group_uniforms wind;
uniform vec2 wind_direction = vec2(1.0, 0.35);
uniform float wind_strength = 0.18; // metres of tip sway
uniform float wind_speed = 1.3;
uniform float gust_scale = 0.06; // cycles per metre of the rolling gusts
group_uniforms shading;
uniform float up_normal : hint_range(0.0, 1.0) = 0.85; // bend normals toward the sky so clumps read as soft masses
group_uniforms distance;
// Clumps thin out with distance instead of shrinking: each clump has its own random cut-off between these, so the
// grass edge is a soft circle around the camera, and every clump that is drawn is drawn at full size.
uniform float fade_start = 25.0;
uniform float fade_end = 60.0; // none left here, before the instancer culls the cell (no hard edges)
// Within a clump, blades drop out one by one (random cut-off per blade in vertex alpha) between these distances,
// down to blade_min of them, on top of the instancer's coarser LOD switches.
uniform float blade_thin_start = 6.0;
uniform float blade_thin_end = 25.0;
uniform float blade_min : hint_range(0.0, 1.0) = 0.3;
group_uniforms;

varying float v_height;
varying float v_head;
varying vec3 v_tint;

void vertex() {
	v_height = UV.y;
	v_head = step(1.5, UV.x);
	v_tint = COLOR.rgb;

	vec3 origin = MODEL_MATRIX[3].xyz;
	float dist = distance(origin, CAMERA_POSITION_WORLD);
	float keep = 1.0 - smoothstep(fade_start, fade_end, dist);
	float blade_keep = mix(1.0, blade_min, smoothstep(blade_thin_start, blade_thin_end, dist));
	if (COLOR.a > blade_keep) keep = 0.0;
	float cutoff = fract(sin(dot(origin.xz, vec2(12.9898, 78.233))) * 43758.5453);
	// a clump grows in over a short band as the camera approaches its cut-off, then stays full size
	float shrink = clamp((keep - cutoff) * 10.0, 0.0, 1.0);
	VERTEX *= shrink;
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 dir = normalize(wind_direction);
	float gust = sin(TIME * wind_speed + dot(world.xz, dir) * gust_scale * 6.2832) * 0.5 + 0.5;
	float flutter = sin(TIME * wind_speed * 3.1 + world.x * 1.7 + world.z * 1.1);
	float bend = wind_strength * (0.25 + gust * 0.75 + flutter * 0.15) * v_height * v_height * shrink;
	vec3 push = vec3(dir.x * bend, -0.35 * bend * bend, dir.y * bend);

	// world push → local (instance transforms are rotation + uniform scale)
	float s2 = dot(MODEL_MATRIX[0].xyz, MODEL_MATRIX[0].xyz);
	VERTEX += (vec4(push, 0.0) * MODEL_MATRIX).xyz / s2;
	vec3 local_up = normalize((vec4(0.0, 1.0, 0.0, 0.0) * MODEL_MATRIX).xyz);
	NORMAL = normalize(mix(NORMAL, local_up, up_normal));
}

void fragment() {
	vec3 blade = mix(root_color, tip_color, smoothstep(0.0, 1.0, v_height));
	vec3 albedo = mix(blade, head_color, v_head) * v_tint;
	if (!FRONT_FACING) NORMAL = -NORMAL; // light both sides of a blade the same, no dark backs
	ALBEDO = albedo;
	AO = mix(1.0 - root_ao, 1.0, smoothstep(0.0, 0.7, v_height));
	AO_LIGHT_AFFECT = 0.4;
	BACKLIGHT = albedo * backlight * v_height;
	ROUGHNESS = 0.85;
	SPECULAR = 0.15;
}
