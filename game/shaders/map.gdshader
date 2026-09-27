// World map: terrain + map-mode tint, province/coast/country borders (1 screen px at any zoom, like the
// mockup's sub-pixel edge logic), hover/selection, fog of war (cloud texture with a frayed dithered edge).
// Drawn as one big rect in world coordinates; x wraps (the world is a cylinder).
shader_type canvas_item;
render_mode blend_disabled, unshaded;

#include "res://shaders/map_common.gdshaderinc"

uniform sampler2D base_tex : filter_nearest, repeat_disable;  // RGBA8 per pixel: shaded terrain colour
uniform sampler2D ptint_tex : filter_nearest, repeat_disable; // RGBA8 per province: map-mode tint rgb + strength
uniform sampler2D pown_tex : filter_nearest, repeat_disable;  // RGBA8 per province: country-border rgb, owner + 1
uniform float zoom = 3.0;        // screen px per world px
uniform int hovered = -1;
uniform int selected = -1;
uniform vec3 void_color = vec3(0.243, 0.263, 0.290);

// fog tones: 4 cloud levels x (flat, lit top edge, shaded bottom edge)
const vec3 FOG_PAL[12] = vec3[12](
	vec3(14.0, 15.0, 18.0) / 255.0, vec3(14.0, 15.0, 18.0) / 255.0, vec3(11.0, 12.0, 14.0) / 255.0,
	vec3(20.0, 21.0, 25.0) / 255.0, vec3(29.0, 31.0, 36.0) / 255.0, vec3(15.0, 16.0, 19.0) / 255.0,
	vec3(27.0, 29.0, 34.0) / 255.0, vec3(40.0, 42.0, 49.0) / 255.0, vec3(20.0, 21.0, 25.0) / 255.0,
	vec3(36.0, 38.0, 45.0) / 255.0, vec3(56.0, 59.0, 68.0) / 255.0, vec3(27.0, 29.0, 34.0) / 255.0);

varying vec2 wpos;

void vertex() {
	wpos = VERTEX;
}

vec3 mode_color(vec3 c, int p, vec4 info) {
	float m = (c.r + c.g + c.b) / 3.0;
	c = mix(c, vec3(m), info.r);
	vec4 tint = texelFetch(ptint_tex, pcoord(p), 0);
	c = mix(c, tint.rgb, tint.a);
	return c * info.g;
}

int owner_of(vec4 own) { return int(own.a * 255.0 + 0.5) - 1; }

void fragment() {
	vec2 w = wpos;
	if (w.y < 0.0 || w.y >= world_size.y) {
		// out-of-world void: flat slate with a soft shadow along the world edge
		float d = (w.y < 0.0 ? -w.y : w.y - world_size.y) * zoom;
		COLOR = vec4(void_color * (0.72 + 0.28 * smoothstep(0.0, 18.0, d)), 1.0);
	} else {
		ivec2 t = texel_at(w);
		int p = prov_at(t);
		vec4 info = pinfo(p);
		bool explored = explored_info(info);
		vec3 base = texelFetch(base_tex, t, 0).rgb;
		int tone; float tt;
		int fog = fog_px(t, explored, tone, tt);
		if (fog == 1) {
			COLOR = vec4(FOG_PAL[tone], 1.0);
		} else if (fog == 2) {
			float m = (base.r + base.g + base.b) / 3.0;
			COLOR = vec4(mix(base, vec3(m), 0.7) * (0.5 - 0.16 * tt), 1.0);
		} else {
			vec3 c = mode_color(base, p, info);
			// Which sides of this screen pixel lie on a world-pixel edge (1 world px = zoom screen px).
			// Left/top edges always; right/bottom only when zoomed in, so land borders stay 1 px while
			// country borders and the selection get one pixel on each side (two-tone at zoom >= 2).
			float s = 1.0 / zoom;
			bool eL = floor(w.x - s) != floor(w.x);
			bool eU = floor(w.y - s) != floor(w.y);
			bool eR = zoom > 1.5 && floor(w.x + s) != floor(w.x);
			bool eD = zoom > 1.5 && floor(w.y + s) != floor(w.y);
			bool white = false;
			if (eL || eU || eR || eD) {
				bool landP = info.a > 0.5;
				vec4 ownP = texelFetch(pown_tex, pcoord(p), 0);
				int op = owner_of(ownP);
				int kind = 0;
				vec3 ncol = vec3(0.0);
				for (int k = 0; k < 4; k++) {
					bool on = k == 0 ? eL : k == 1 ? eU : k == 2 ? eR : eD;
					if (!on) continue;
					ivec2 d = k == 0 ? ivec2(-1, 0) : k == 1 ? ivec2(0, -1) : k == 2 ? ivec2(1, 0) : ivec2(0, 1);
					int q = prov_at(wrap_texel(t + d));
					if (q == p) continue;
					if (p == selected || q == selected) { white = true; break; }
					vec4 qi = pinfo(q);
					if (!explored_info(qi)) continue;
					bool landQ = qi.a > 0.5;
					vec4 ownQ = texelFetch(pown_tex, pcoord(q), 0);
					int oq = owner_of(ownQ);
					if (op != oq && (op >= 0 || oq >= 0) && landP && landQ) {
						kind = 3;
						ncol = op >= 0 ? ownP.rgb : ownQ.rgb;
					} else if (k < 2 && landP != landQ) {
						kind = max(kind, 2);
					} else if (k < 2) {
						kind = max(kind, 1);
					}
				}
				if (white) c = vec3(1.0);
				else if (kind == 3) c = ncol;
				else if (kind == 2) c *= 0.42;
				else if (kind == 1) c *= landP ? (zoom >= 1.5 ? 0.72 : 0.84) : (zoom >= 1.5 ? 0.9 : 1.0);
			}
			if (!white) {
				if (p == hovered) c += (1.0 - c) * 0.2;
				else if (p == selected) c += (1.0 - c) * 0.1;
			}
			COLOR = vec4(c, 1.0);
		}
	}
}
