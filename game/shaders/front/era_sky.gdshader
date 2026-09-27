shader_type canvas_item;
// «Горизонт эпох» — the title backdrop (MAIN_MENU.md §3.1), a port of Mr. President's background.gdshader.
// Everything is drawn on a virtual pixel grid (ps screen px per vp): a Bayer-dithered night sky with
// twinkling stars, the rotating planet of the next world, a far ridge and 11 monochrome era silhouettes from
// the skyline atlas (encoding: docs/design/concepts/src/menu_skyline.py) with lit windows, smoke, beacons
// and a rocket launching every 90 s. Time comes from FrontClock via `t` (never TIME) so screenshots are
// deterministic and «меньше анимации» is one uniform (motion = 0 → a single static frame).

#include "res://shaders/front/front_common.gdshaderinc"
#include "res://shaders/front/planet.gdshaderinc"

uniform vec2 screen_size = vec2(1600.0, 900.0);
uniform float ps = 4.0;
uniform float t = 0.0;
uniform float motion = 1.0;
uniform float rise = 0.0;                 // intro: the horizon starts this many vp lower
uniform float ground_vp = 3.0;            // height of the flat ground strip (the corner captions sit on it)
uniform int lit_upto = 10;                // eras above this have dark windows (meta progress later)
uniform int solo_era = -1;                // >= 0: only this era, centred (chapter card)
uniform float show_planet = 1.0;
uniform sampler2D skyline : filter_nearest;
uniform vec4 emitters[8];                 // atlas x, fg of the source, kind (2 fire, 3-5 chimney, 6 steam), era
uniform int emitter_count = 0;
uniform vec4 rocket_box = vec4(326.0, 334.0, 7.0, 70.0);   // atlas x0, x1, lowest and highest fg of the body

const float ERA_X[11] = float[11](18.0, 50.0, 84.0, 118.0, 152.0, 186.0, 220.0, 256.0, 292.0, 330.0, 372.0);
const vec3 SKY_TOP = vec3(0.035, 0.035, 0.039);
const vec3 SKY_LOW = vec3(0.129, 0.129, 0.145);
const vec3 STAR = vec3(0.55, 0.55, 0.57);
const vec3 STAR_BIG = vec3(0.78, 0.78, 0.80);
const vec3 RIDGE = vec3(0.071, 0.071, 0.078);
const vec3 PLAN_FAR = vec3(0.094, 0.094, 0.106);
const vec3 PLAN_NEAR = vec3(0.051, 0.051, 0.055);
const vec3 PLAN_LIT = vec3(0.114, 0.114, 0.125);
const vec3 GROUND = vec3(0.043, 0.043, 0.047);
const vec3 GROUND_EDGE = vec3(0.071, 0.071, 0.078);
const vec3 WINDOW = vec3(0.42, 0.42, 0.44);
const vec3 SMOKE_A = vec3(0.227, 0.227, 0.251);
const vec3 SMOKE_B = vec3(0.173, 0.173, 0.192);
const vec3 FIRE_A = vec3(0.847, 0.847, 0.855);
const vec3 FIRE_B = vec3(0.639, 0.639, 0.659);
const vec3 BEACON = vec3(0.941, 0.941, 0.949);
const vec3 BEACON_OFF = vec3(0.23, 0.23, 0.25);
const vec3 CLOCK = vec3(0.62, 0.62, 0.64);
const float ROCKET_CYCLE = 90.0;
const float ROCKET_ACCEL = 22.0;          // vp/s²

// plan, window, era, emitter id of an atlas cell (x from the atlas left, fg from the bottom row)
ivec4 cell(float ax, float fg) {
	if (ax < 0.0 || ax >= 400.0 || fg < 0.0 || fg >= 72.0) return ivec4(0);
	vec4 s = texelFetch(skyline, ivec2(int(ax), 71 - int(fg)), 0);
	int g = int(round(s.g * 255.0));
	return ivec4(int(round(s.r * 255.0 / 64.0)), (g % 20) >= 5 ? 1 : 0, g / 20, int(round(s.b * 255.0 / 16.0)));
}

vec3 plan_color(int plan) {
	return plan == 1 ? PLAN_FAR : plan == 3 ? PLAN_LIT : PLAN_NEAR;
}

vec3 flame(vec2 px, float tq) {
	return mod(px.x + px.y + floor(tq * 12.0), 2.0) < 1.0 ? BEACON : FIRE_B;
}

void fragment() {
	vec2 vsize = floor(screen_size / ps);
	vec2 px = floor(UV * screen_size / ps);
	float fg = vsize.y - 1.0 - px.y;
	float T = motion > 0.5 ? t : 12.0;
	float tq = floor(T * 12.0) / 12.0;

	// ---- sky: dithered gradient, drifting haze, stars ----
	float sky_t = clamp(px.y / (vsize.y * 0.85), 0.0, 1.0);
	float haze = noise(vec2(px.x * 0.02 + tq * 0.05, px.y * 0.06)) * 0.25;
	vec3 col = mix(SKY_TOP, SKY_LOW, dither(pow(sky_t, 1.6) + haze * sky_t, px, 6.0));
	float sh = hash(px);
	float tw = step(0.5, fract(tq * (0.2 + sh) + sh * 7.0));
	col = mix(col, STAR, step(0.9965, sh) * (0.4 + 0.6 * tw) * (1.0 - sky_t));
	// a few bright cross-shaped stars, at most one per 28×28 cell
	vec2 bc = floor(px / 28.0);
	vec2 bp = bc * 28.0 + 3.0 + floor(vec2(hash(bc + 3.1), hash(bc + 7.7)) * 22.0);
	if (hash(bc + 11.3) > 0.72 && bp.y < vsize.y * 0.5) {
		vec2 q = abs(px - bp);
		float on = step(0.5, fract(tq * 0.25 + hash(bc) * 3.0));
		if (q.x + q.y < 0.5) col = STAR_BIG;
		else if (q.x + q.y < 1.5) col = mix(col, STAR, 0.35 + 0.65 * on);
	}

	// ---- shooting star: once per 35 s window, 0.35 s long ----
	float seg = floor(T / 35.0);
	float st = T - seg * 35.0 - (5.0 + hash(vec2(seg, 3.1)) * 22.0);
	if (st >= 0.0 && st < 0.35) {
		vec2 s0 = vec2(vsize.x * (0.25 + 0.55 * hash(vec2(seg, 1.7))), 6.0 + vsize.y * 0.22 * hash(vec2(seg, 2.3)));
		vec2 head = s0 + vec2(-2.0, 1.0) * floor(st / 0.35 * 22.0);
		vec2 rel = px - head;
		if (rel.x >= 0.0 && rel.x <= 11.0 && rel.y == -floor(rel.x / 2.0)) {
			bool tail = rel.x > 1.0;
			if (!tail || mod(px.x + px.y, 2.0) < 1.0) col = tail ? STAR : STAR_BIG;
		}
	}

	// ---- the planet of the next world ----
	if (show_planet > 0.5 && solo_era < 0) {
		vec4 p = planet(px, vsize, tq);
		col = mix(col, p.rgb, p.a);
	}

	float fgs = fg + rise - (ground_vp - 3.0);   // the atlas has its own 3 vp of ground
	float x0 = floor(vsize.x * 0.5) - 200.0;
	float ax = solo_era >= 0 ? px.x - floor(vsize.x * 0.5) + ERA_X[solo_era] : px.x - x0;

	// ---- smoke and steam puffs above the emitters ----
	for (int i = 0; i < 8; i++) {
		if (i >= emitter_count) break;
		vec4 e = emitters[i];
		if ((solo_era >= 0 && int(e.w) != solo_era) || int(e.w) > lit_upto) continue;
		float big = e.z > 5.5 ? 1.7 : e.z < 2.5 ? 0.6 : 1.0;
		for (int k = 0; k < 3; k++) {
			float a = fract(tq / (3.0 * big) + float(k) / 3.0 + float(i) * 0.29);
			vec2 cpos = vec2(e.x + floor(a * 6.0 * big), e.y + floor(a * 14.0 * big));
			float rad = (1.0 + floor(a * 3.0)) * big;
			vec2 dd = vec2(ax, fgs) - cpos;
			if (dot(dd, dd) <= rad * rad + 0.5 && bayer4(px) < 1.0 - a)
				col = e.z > 5.5 ? mix(SMOKE_A * 1.25, SMOKE_B, a) : mix(SMOKE_A, SMOKE_B, a);
		}
	}

	// ---- far ridge (also fills any width beyond the 400 vp atlas) ----
	float ridge = 6.0 + floor(noise(vec2(px.x * 0.03, 1.7)) * 10.0 + noise(vec2(px.x * 0.12, 5.3)) * 3.0);
	if (fgs < ridge && solo_era < 0) col = RIDGE;

	// ---- era silhouettes ----
	float cyc = mod(T, ROCKET_CYCLE);
	float sfg = fgs - (ax >= 350.0 ? step(0.5, fract(tq / 2.4)) : 0.0);   // the disc city hovers
	ivec4 c = cell(ax, sfg);
	bool visible = solo_era < 0 || c.z == solo_era;
	if (visible && c.x > 0) {
		bool shown = true;
		if (c.w == 9) shown = cyc < 85.0 && (cyc >= 0.4 || bayer4(px) < cyc / 0.4);
		if (shown) {
			col = plan_color(c.x);
			bool lit_era = c.z <= lit_upto;
			if (c.y == 1 && lit_era) {
				float ph = hash(vec2(float(c.z), 5.0)) * 5.0;
				float on = step(0.55, hash(vec2(floor(ax / 2.0), floor(sfg / 3.0)) + floor(tq * 0.15 + ph)));
				col = mix(col, WINDOW, on);
			}
			if (c.w == 1 && lit_era) col = mod(ax + sfg + floor(T / 0.25), 2.0) < 1.0 ? FIRE_A : FIRE_B;
			if (c.w == 7) col = fract(T) < 0.08 ? BEACON : BEACON_OFF;
			if (c.w == 10 && lit_era) col = CLOCK;
		}
	} else if (visible && c.w == 8 && cyc >= 84.0 && cyc < 86.0 && bayer4(px + floor(tq * 12.0)) < 0.6) {
		col = flame(px, tq);   // ignition: the trench flickers
	}

	// ---- the rocket in flight ----
	if (cyc >= 85.0 && ax >= rocket_box.x - 1.0 && ax <= rocket_box.y + 1.0 && (solo_era < 0 || solo_era == 9)) {
		float tau = floor((cyc - 85.0) * 12.0) / 12.0;
		float dy = floor(0.5 * ROCKET_ACCEL * tau * tau);
		// exhaust trail: 1 vp wide, melts away over 4 s after the rocket passed
		float cx = floor((rocket_box.x + rocket_box.y) * 0.5);
		float h = sfg - rocket_box.z;
		if (abs(ax - cx) < 0.5 && h >= 0.0 && h < dy) {
			float age = (cyc - 85.0) - sqrt(2.0 * h / ROCKET_ACCEL);
			if (bayer4(px) < 1.0 - age / 4.0) col = SMOKE_A * 1.4;
		}
		ivec4 r = cell(ax, sfg - dy);
		if (r.w == 9 && r.x > 0) col = plan_color(r.x);
		else if (dy > 0.0) {
			ivec4 f1 = cell(ax, sfg - dy + 3.0);
			if ((r.w == 8 || f1.w == 8) && bayer4(px + floor(tq * 24.0)) < 0.75) col = flame(px, tq);
		}
	}

	// ---- ground, grain, vignette ----
	if (fg < ground_vp) col = fg > ground_vp - 1.5 ? GROUND_EDGE : GROUND;
	col += (hash(px + 17.0) - 0.5) * 0.012;
	vec2 centered = UV - 0.5;
	col *= 1.0 - dither(dot(centered, centered) * 1.1, px, 5.0) * 0.6;
	COLOR = vec4(col, 1.0);
}
