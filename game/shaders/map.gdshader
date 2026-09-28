// World map: terrain + map-mode tint, province/coast/country borders (1 screen px at any zoom, like the
// mockup's sub-pixel edge logic), hover/selection, fog of war (cloud texture with a frayed dithered edge),
// the old-map hatch of stale land. Below ×1 (the ×½ atlas) one screen px covers 2×2 world px: terrain comes from a
// box-averaged half-size texture and only coasts and country borders are drawn.
// Drawn as one big rect in world coordinates; x wraps (the world is a cylinder).
shader_type canvas_item;
render_mode blend_disabled, unshaded;

#include "res://shaders/map_common.gdshaderinc"
#include "res://shaders/map_capture.gdshaderinc"

uniform sampler2D base_tex : filter_nearest, repeat_disable;  // RGBA8 per pixel: shaded terrain colour
uniform sampler2D base_half_tex : filter_nearest, repeat_disable; // base_tex averaged over 2×2 blocks (atlas zoom)
uniform sampler2D ptint_tex : filter_nearest, repeat_disable; // RGBA8 per province: map-mode tint rgb + strength
uniform sampler2D pown_tex : filter_nearest, repeat_disable;  // RGBA8 per province: country-border rgb, owner + 1
uniform sampler2D water_tex : filter_nearest, repeat_disable; // R8 per pixel: cluster phase (0-3), shore distance (4-5), sparkle (6)
uniform float anim_t = 0.0;      // seconds of water animation (MapView: a quarter speed on pause)
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

// Living water, palette-cycle style (ART_BIBLE §5): foam flickers along the shore, the shallows shimmer in 3×3
// clusters, rare single-pixel glints in open water. Steps, never fades.
vec3 water_anim(vec3 c, ivec2 t) {
	int v = int(texelFetch(water_tex, t, 0).r * 255.0 + 0.5);
	int ph = v & 15, shore = (v >> 4) & 3;
	int step = int(anim_t * 4.0);
	if (shore == 1 && (ph + step) % 7 == 0) return mix(c, vec3(0.612, 0.765, 0.816), 0.55);
	if (shore > 0 && (ph + step / 2) % 8 == 0) return c * 1.1;
	if (shore == 0 && (v & 64) != 0 && (ph + step / 3) % 16 == 0) return mix(c, vec3(0.45, 0.6, 0.7), 0.35);
	return c;
}

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
		bool atlas = zoom < 0.75;
		vec3 base = atlas ? texelFetch(base_half_tex, t / 2, 0).rgb : texelFetch(base_tex, t, 0).rgb;
		int tone; float tt;
		int fog = fog_px(t, explored, tone, tt);
		if (fog == 1) {
			COLOR = vec4(FOG_PAL[tone], 1.0);
		} else if (fog == 2) {
			float m = (base.r + base.g + base.b) / 3.0;
			COLOR = vec4(mix(base, vec3(m), 0.7) * (0.5 - 0.16 * tt), 1.0);
		} else {
			bool hov = p == hovered;
			bool stale = fog_on && info.b > 0.25 && info.b < 0.75;
			bool landP = info.a > 0.5;
			if (!landP && !atlas) base = water_anim(base, t);
			vec3 c = mode_color(base, p, info);
			int cs = cap_slot(p);   // a capture fill running in this province (map_capture.gdshaderinc)
			if (cs >= 0) c = cap_fill(c, base, cs, cap_rel(cs, t), t);
			// hovering a stale province peeks at the old map: its dimming is lifted
			if (hov && stale) c = min(c / max(info.g, 0.2), vec3(1.0));
			// explored but unseen land: a faint old-map hatch (reads as «old information», not as darkness)
			else if (stale && landP && ((t.x + t.y) & 3) == 0) c *= 0.9;
			// Which sides of this screen pixel lie on a world-pixel edge (1 world px = zoom screen px).
			// Left/top edges always; right/bottom only when zoomed in, so land borders stay 1 px while
			// country borders and the selection get one pixel on each side (two-tone at zoom >= 2).
			// In the atlas every screen pixel starts a new 2×2 block: compare with the previous block.
			int st = atlas ? 2 : 1;
			float s = 1.0 / zoom;
			bool eL = atlas || floor(w.x - s) != floor(w.x);
			bool eU = atlas || floor(w.y - s) != floor(w.y);
			bool eR = !atlas && zoom > 1.5 && floor(w.x + s) != floor(w.x);
			bool eD = !atlas && zoom > 1.5 && floor(w.y + s) != floor(w.y);
			bool white = false;
			if (eL || eU || eR || eD) {
				vec4 ownP = cap_own(texelFetch(pown_tex, pcoord(p), 0), cs);
				int op = owner_of(ownP);
				int kind = 0;
				vec3 ncol = vec3(0.0);
				for (int k = 0; k < 4; k++) {
					bool on = k == 0 ? eL : k == 1 ? eU : k == 2 ? eR : eD;
					if (!on) continue;
					ivec2 d = k == 0 ? ivec2(-st, 0) : k == 1 ? ivec2(0, -st) : k == 2 ? ivec2(st, 0) : ivec2(0, st);
					int q = prov_at(wrap_texel(t + d));
					if (q == p) continue;
					if (p == selected || q == selected) { white = true; break; }
					vec4 qi = pinfo(q);
					if (!explored_info(qi)) continue;
					bool landQ = qi.a > 0.5;
					vec4 ownQ = cap_own(texelFetch(pown_tex, pcoord(q), 0), cap_slot(q));
					int oq = owner_of(ownQ);
					if (op != oq && (op >= 0 || oq >= 0) && landP && landQ) {
						kind = 3;
						ncol = op >= 0 ? ownP.rgb : ownQ.rgb;
					} else if (k < 2 && landP != landQ) {
						kind = max(kind, 2);
					} else if (k < 2 && !atlas) {
						kind = max(kind, 1);
					}
				}
				if (white) c = vec3(1.0);
				else if (kind == 3) c = ncol;
				else if (kind == 2) c *= 0.42;
				else if (kind == 1) c *= landP ? (zoom < 1.5 ? 0.93 : zoom < 2.5 ? 0.86 : 0.78) : (zoom >= 1.5 ? 0.9 : 1.0);
			}
			if (!white) {
				if (hov) {
					// brighten without lifting the blacks (the relief keeps its contrast), plus a light inner rim;
					// at ×1 and below the far sides of an edge belong to the neighbour's pixels, so test all four
					c = min(c * 1.2 + 0.015, vec3(1.0));
					bool all4 = zoom < 1.5;
					for (int k = 0; k < 4; k++) {
						bool on = k == 0 ? eL : k == 1 ? eU : k == 2 ? (eR || all4) : (eD || all4);
						if (!on) continue;
						ivec2 d = k == 0 ? ivec2(-st, 0) : k == 1 ? ivec2(0, -st) : k == 2 ? ivec2(st, 0) : ivec2(0, st);
						if (prov_at(wrap_texel(t + d)) != p) { c = mix(c, vec3(1.0), 0.45); break; }
					}
				}
				else if (p == selected) c = min(c * 1.08 + 0.01, vec3(1.0));
			}
			COLOR = vec4(c, 1.0);
		}
	}
}
