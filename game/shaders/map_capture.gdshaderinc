// Capture fill for map.gdshader (GDD «Захват провинции — анимация заливки», ART_BIBLE §13 «Расширение границы»;
// data from Map/ProvinceTransitions.cs). A province whose shown owner just changed keeps its old look until a front,
// running out from the border it shares with the new owner (from its centre when there is none), passes each pixel.
// The front is a light crest 1–2 px thick; both of its edges are 4×4 Bayer thresholds in world pixels, so the new
// colour arrives as a dithered pixel wave, never as an alpha fade. The country borders keep the old owner until the
// fill ends and are redrawn then. With cap_active == 0 nothing here is fetched.

uniform sampler2D cap_prov_tex : filter_nearest, repeat_disable; // R8 per province: transition slot + 1 (0 = none)
uniform sampler2D cap_slot_tex : filter_nearest, repeat_disable; // RGBAF, one row of 5 texels per slot (below)
uniform sampler2D cap_dist_tex : filter_nearest, repeat_disable; // R8 atlas: when the front reaches each pixel
uniform int cap_active = 0;       // running transitions
uniform float cap_time = 0.0;     // transition clock, real seconds
uniform float cap_freeze = -1.0;  // >= 0: every front held at this progress (--capturefreeze)

const float CAP_FRAY = 2.0;    // px over which the old look dithers into the crest (Bayer)
const float CAP_CREST = 1.0;   // px of solid crest
const float CAP_SETTLE = 1.0;  // px over which the crest dithers into the new colour (Bayer)
const float CAP_LIFT = 0.16;   // crest brightness (× the slot's crest strength)
const float CAP_TAIL = CAP_FRAY + CAP_CREST + CAP_SETTLE;   // the front runs this far past the farthest pixel

// slot row: 0 old tint rgb + strength | 1 old border rgb + owner byte | 2 old desat, old dim, crest strength |
//           3 bbox x0, y0 (world px), atlas x0, y0 | 4 start, duration, reach (px), px per atlas step
vec4 cap_tx(int s, int i) { return texelFetch(cap_slot_tex, ivec2(i, s), 0); }

int cap_slot(int p) {
	if (cap_active == 0) return -1;
	return int(texelFetch(cap_prov_tex, pcoord(p), 0).r * 255.0 + 0.5) - 1;
}

float cap_bayer4(ivec2 t) {
	const float B[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
	return (B[(t.y & 3) * 4 + (t.x & 3)] + 0.5) / 16.0;
}

// How far (px) the front of slot s has run past pixel t of its province: > 0 = reached.
float cap_rel(int s, ivec2 t) {
	vec4 box = cap_tx(s, 3);
	vec4 run = cap_tx(s, 4);
	int lx = t.x - int(box.x);
	if (lx < 0) lx += int(world_size.x);
	float d = texelFetch(cap_dist_tex, ivec2(int(box.z) + lx, int(box.w) + t.y - int(box.y)), 0).r * 255.0 * run.w;
	float u = cap_freeze >= 0.0 ? cap_freeze : clamp((cap_time - run.x) / run.y, 0.0, 1.0);
	u *= 1.5 - 0.5 * u;   // a touch of ease-out
	return u * (run.z + CAP_TAIL) - d;
}

// Country-border texel (pown_tex layout) of a province running slot s: the old owner until the fill ends.
vec4 cap_own(vec4 own, int s) { return s < 0 ? own : cap_tx(s, 1); }

// Colour of pixel t of a province running slot s: c is its new map-mode colour, base the terrain under it.
// Ahead of the front the old look; the old look frays into the crest (the new colour lifted) over CAP_FRAY px,
// the crest stays solid for CAP_CREST px and dithers into the plain new colour over CAP_SETTLE px.
vec3 cap_fill(vec3 c, vec3 base, int s, float rel, ivec2 t) {
	float b = cap_bayer4(t);
	if (rel < CAP_FRAY * b) {
		vec4 ot = cap_tx(s, 0), oi = cap_tx(s, 2);
		float m = (base.r + base.g + base.b) / 3.0;
		return mix(mix(base, vec3(m), oi.r), ot.rgb, ot.a) * oi.g;
	}
	if (rel < CAP_FRAY + CAP_CREST + CAP_SETTLE * b) {
		float k = CAP_LIFT * cap_tx(s, 2).b;
		return min(c * (1.0 + k) + 0.3 * k, vec3(1.0));
	}
	return c;
}
