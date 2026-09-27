// Rivers: one triangle-strip mesh for all rivers (built by Map/RiverMesh.cs). The strip is expanded in the vertex
// shader so the width stays in screen pixels at any zoom and grows downstream. Drawn twice: dark casing, then water.
// Hidden under fog clouds and over unexplored ground; dimmed like the land over stale (explored, unseen) provinces.
//   VERTEX = centre-line point (world px), UV.y = side (-1 / +1),
//   COLOR.rg = miter normal * 0.25 + 0.5, COLOR.b = width factor / 2.
shader_type canvas_item;
render_mode unshaded;

#include "res://shaders/map_common.gdshaderinc"

uniform float zoom = 3.0;
uniform bool casing = false;
uniform vec4 water_color = vec4(0.302, 0.498, 0.651, 1.0);
uniform vec4 casing_color = vec4(0.078, 0.133, 0.188, 0.55);

varying vec2 wpos;
varying float across;
varying float half_px;

void vertex() {
	vec2 n = (COLOR.rg - 0.5) * 4.0;
	float fill = max(1.0, zoom * 0.34) * COLOR.b * 2.0;
	half_px = (casing ? fill + 1.6 : fill) * 0.5;
	float ext = half_px + 1.0;           // one extra pixel for the antialiased rim
	VERTEX += n * UV.y * ext / zoom;
	wpos = VERTEX;
	across = UV.y * ext;
	COLOR = vec4(1.0);
}

void fragment() {
	if (fog_hides(wpos)) discard;
	float cov = clamp(half_px + 0.5 - abs(across), 0.0, 1.0);
	vec4 c = casing ? casing_color : water_color;
	COLOR = vec4(stale_grade(c.rgb, wpos), c.a * cov);
}
