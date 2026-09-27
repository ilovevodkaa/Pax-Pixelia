// Trade routes (Trade map mode): animated golden dashes over a thin dark casing. Same strip layout as rivers,
// plus UV.x = distance along the route in world px (drives the dash pattern: 3 px dash, 2 px gap, in world px).
shader_type canvas_item;
render_mode unshaded;

#include "res://shaders/map_common.gdshaderinc"

uniform float zoom = 3.0;
uniform bool casing = false;
uniform vec4 dash_color = vec4(0.941, 0.784, 0.376, 1.0);
uniform vec4 casing_color = vec4(0.086, 0.102, 0.122, 0.5);
uniform float speed_px = 25.0;       // screen px per second

varying vec2 wpos;
varying float across;
varying float along;
varying float half_px;

void vertex() {
	vec2 n = (COLOR.rg - 0.5) * 4.0;
	float fill = max(2.0, zoom * 0.8);
	half_px = (casing ? fill + 2.0 : fill) * 0.5;
	float ext = half_px + 1.0;
	VERTEX += n * UV.y * ext / zoom;
	wpos = VERTEX;
	across = UV.y * ext;
	along = UV.x;
	COLOR = vec4(1.0);
}

void fragment() {
	if (fog_hides(wpos)) discard;
	float cov = clamp(half_px + 0.5 - abs(across), 0.0, 1.0);
	if (!casing) {
		float m = mod(along - TIME * speed_px / zoom, 5.0);
		cov *= clamp(m * zoom, 0.0, 1.0) * clamp((3.0 - m) * zoom, 0.0, 1.0);
	}
	vec4 c = casing ? casing_color : dash_color;
	COLOR = vec4(c.rgb, c.a * cov);
}
