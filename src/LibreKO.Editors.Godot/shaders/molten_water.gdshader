shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D surface_tex : source_color, repeat_enable, filter_linear_mipmap;
uniform sampler2D wave_normal : hint_normal, repeat_enable, filter_linear_mipmap;
uniform vec2 surface_scroll = vec2(0.012, 0.008);
uniform vec2 wave_scroll = vec2(0.03, -0.02);
uniform float wave_tiling = 0.35;
uniform float distortion = 0.04;
uniform float brightness = 1.0;

void fragment() {
    vec2 wave = texture(wave_normal, UV * wave_tiling + TIME * wave_scroll).xy * 2.0 - 1.0;
    ALBEDO = texture(surface_tex, UV + TIME * surface_scroll + wave * distortion).rgb * brightness;
}
