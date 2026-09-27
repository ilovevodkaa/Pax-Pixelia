// Rivers: one triangle-strip mesh for all rivers (built by Map/RiverMesh.cs), drawn twice: casing, then water.
// ×1–×2: antialiased strokes whose width stays in screen pixels (grows with the flow).
// ×3 and closer: pixel art on the world-pixel grid — a world pixel is water when its centre lies within the river
// (1 px for streams, 2 px for big rivers), and the casing pass becomes a 1-px shadow on the lower-right side.
// Hidden under fog clouds and over unexplored ground; dimmed like the land over stale (explored, unseen) provinces.
//   VERTEX = centre-line point (world px), UV.y = side (-1 / +1),
//   COLOR.rg = miter normal * 0.25 + 0.5, COLOR.b = width factor / 2.
shader_type canvas_item;
render_mode unshaded;

#include "res://shaders/map_common.gdshaderinc"

uniform float zoom = 3.0;
uniform bool casing = false;
uniform vec4 water_color = vec4(0.416, 0.612, 0.776, 1.0);
uniform vec4 casing_color = vec4(0.078, 0.133, 0.188, 0.55);
uniform vec4 shadow_color = vec4(0.063, 0.098, 0.141, 0.6);

const float PIXEL_ZOOM = 2.5;

varying vec2 wpos;
varying float across;    // signed distance from the centre line: screen px (strokes) or world px (pixel art)
varying float half_w;    // half width in the same unit
varying vec2 nrm;        // unit normal of the strip, world space

void vertex() {
	vec2 n = (COLOR.rg - 0.5) * 4.0;
	float wf = COLOR.b * 2.0;
	nrm = normalize(n);
	if (zoom >= PIXEL_ZOOM) {
		half_w = wf > 1.2 ? 1.0 : 0.5;
		// cover every world pixel whose centre may be inside (+ the shadow pixel for the casing pass)
		float ext = half_w + (casing ? 2.2 : 1.0);
		VERTEX += n * UV.y * ext;
		across = UV.y * ext;
	} else {
		float fill = max(1.0, zoom * 0.34) * wf;
		half_w = (casing ? fill + 1.6 : fill) * 0.5;
		float ext = half_w + 1.0;           // one extra pixel for the antialiased rim
		VERTEX += n * UV.y * ext / zoom;
		across = UV.y * ext;
	}
	wpos = VERTEX;
	COLOR = vec4(1.0);
}

void fragment() {
	if (zoom >= PIXEL_ZOOM) {
		vec2 c = floor(wpos) + 0.5;
		// the casing pass paints the river shifted one pixel right and down: a hard pixel shadow
		vec2 probe = casing ? c - vec2(1.0) : c;
		float d = across + dot(probe - wpos, nrm);
		if (abs(d) > half_w || fog_hides(c)) discard;
		vec4 col = casing ? shadow_color : water_color;
		COLOR = vec4(stale_grade(col.rgb, c), col.a);
	} else {
		if (fog_hides(wpos)) discard;
		float cov = clamp(half_w + 0.5 - abs(across), 0.0, 1.0);
		vec4 col = casing ? casing_color : water_color;
		COLOR = vec4(stale_grade(col.rgb, wpos), col.a * cov);
	}
}
