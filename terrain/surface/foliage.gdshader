// Authored foliage (trees, bushes, plants, grass cards from model packs) with wind. Takes over the imported material:
// albedo texture and colour, alpha cut-out, bark normal map. Sway grows with height above the model's
// base; leaves also flutter, and can bend their normals outward from the canopy for soft, painterly shading.
// Wind uses the same direction, speed and gusts as grass.gdshader so everything moves together.
shader_type spatial;
render_mode cull_disabled;

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

group_uniforms wind;
uniform vec2 wind_direction = vec2(1.0, 0.35);
uniform float wind_speed = 1.3;
uniform float gust_scale = 0.06;
uniform float sway = 0.2; // metres at model_height
uniform float model_height = 5.0;
uniform float flutter = 0.0; // leaves: extra small, fast motion in metres
group_uniforms leaves;
uniform float leaf_normals : hint_range(0.0, 1.0) = 0.0; // bend normals outward from the canopy centre
uniform float canopy_height = 3.5;
group_uniforms distance;
uniform float fade_start = 1e6; // ground cover only: clumps thin out between these, like grass
uniform float fade_end = 2e6;
group_uniforms;

varying float v_height;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float keep = 1.0 - smoothstep(fade_start, fade_end, distance(origin, CAMERA_POSITION_WORLD));
	float cutoff = fract(sin(dot(origin.xz, vec2(12.9898, 78.233))) * 43758.5453);
	VERTEX *= clamp((keep - cutoff) * 10.0, 0.0, 1.0);

	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 dir = normalize(wind_direction);
	float h = clamp(VERTEX.y / model_height, 0.0, 1.5);
	v_height = clamp(h, 0.0, 1.0);
	float gust = sin(TIME * wind_speed + dot(origin.xz, dir) * gust_scale * 6.2832) * 0.5 + 0.5;
	float bend = sway * (0.3 + 0.7 * gust) * h * h;
	vec3 push = vec3(dir.x, 0.0, dir.y) * bend;
	push += flutter * h * vec3(sin(TIME * 7.0 + world.x * 2.3 + world.y * 1.7), sin(TIME * 6.1 + world.z * 2.1) * 0.5, cos(TIME * 7.7 + world.z * 1.9));
	float s2 = dot(MODEL_MATRIX[0].xyz, MODEL_MATRIX[0].xyz);
	VERTEX += (vec4(push, 0.0) * MODEL_MATRIX).xyz / s2;

	if (leaf_normals > 0.0)
		NORMAL = normalize(mix(NORMAL, normalize(VERTEX - vec3(0.0, canopy_height, 0.0)), leaf_normals));
}

void fragment() {
	vec4 tex = texture(albedo_texture, UV);
	if (alpha_cut > 0.0 && tex.a < alpha_cut) discard;
	vec3 base = tex.rgb * albedo_color.rgb;
	if (recolor) {
		float luma = dot(tex.rgb, vec3(0.299, 0.587, 0.114));
		base = mix(root_color, tip_color, smoothstep(0.0, 1.0, v_height)) * mix(0.8, 1.15, clamp(luma * 2.0, 0.0, 1.0));
	}
	ALBEDO = base * COLOR.rgb; // COLOR: the model's vertex colour times the instance tint
	if (leaf_normals <= 0.0 && !FRONT_FACING) NORMAL = -NORMAL;
	if (use_normal_map) NORMAL_MAP = texture(normal_texture, UV).rgb;
	ROUGHNESS = roughness;
	SPECULAR = 0.2;
	BACKLIGHT = ALBEDO * 0.25 * leaf_normals;
}
