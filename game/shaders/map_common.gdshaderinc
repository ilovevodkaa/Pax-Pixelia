// Shared world-map lookups for map.gdshader, river.gdshader and route.gdshader.
// Textures are uploaded by Map/MapTextures.cs; all of them use nearest filtering and texelFetch.

uniform sampler2D prov_tex : filter_nearest, repeat_disable;  // RG8 per pixel: province id (low byte, high byte)
uniform sampler2D pinfo_tex : filter_nearest, repeat_disable; // RGBA8 per province: desaturate, dim, fog (0 / .5 / 1), land
uniform sampler2D fogd_tex : filter_nearest, repeat_disable;  // R8 per pixel: chamfer distance to the explored frontier (3 = 1 px), 255 = far
uniform sampler2D cloud_tex : filter_nearest, repeat_disable; // RG8 per 2x2 block: cloud noise rank, cloud tone index
uniform vec2 world_size = vec2(2560.0, 1440.0);
uniform int pdata_w = 256;
uniform bool fog_on = true;

const float FOG_BAND = 8.0;   // width of the frayed cloud edge, px
const float FOG_AMP = 7.0;    // noise push of that edge, px: enough to break the straight province edges it follows
const float FOG_FAR = 33.0;   // (FOG_BAND / 2 + FOG_AMP) px in chamfer units: beyond it a pixel is fully clear or fully cloud
                              // (= FogField.Far)

ivec2 texel_at(vec2 w) {
	float x = mod(floor(w.x), world_size.x);
	float y = clamp(floor(w.y), 0.0, world_size.y - 1.0);
	return ivec2(int(x), int(y));
}

ivec2 wrap_texel(ivec2 t) {
	int W = int(world_size.x);
	if (t.x < 0) t.x += W; else if (t.x >= W) t.x -= W;
	t.y = clamp(t.y, 0, int(world_size.y) - 1);
	return t;
}

int prov_at(ivec2 t) {
	vec2 v = texelFetch(prov_tex, t, 0).rg;
	return int(v.r * 255.0 + 0.5) + int(v.g * 255.0 + 0.5) * 256;
}

ivec2 pcoord(int p) { return ivec2(p % pdata_w, p / pdata_w); }
vec4 pinfo(int p) { return texelFetch(pinfo_tex, pcoord(p), 0); }
bool explored_info(vec4 info) { return !fog_on || info.b > 0.25; }

float bayer8(ivec2 t) {
	const float B[64] = float[64](
		0.0, 32.0, 8.0, 40.0, 2.0, 34.0, 10.0, 42.0,   48.0, 16.0, 56.0, 24.0, 50.0, 18.0, 58.0, 26.0,
		12.0, 44.0, 4.0, 36.0, 14.0, 46.0, 6.0, 38.0,  60.0, 28.0, 52.0, 20.0, 62.0, 30.0, 54.0, 22.0,
		3.0, 35.0, 11.0, 43.0, 1.0, 33.0, 9.0, 41.0,   51.0, 19.0, 59.0, 27.0, 49.0, 17.0, 57.0, 25.0,
		15.0, 47.0, 7.0, 39.0, 13.0, 45.0, 5.0, 37.0,  63.0, 31.0, 55.0, 23.0, 61.0, 29.0, 53.0, 21.0);
	return (B[(t.y & 7) * 8 + (t.x & 7)] + 0.5) / 64.0;
}

// Value noise on a 16 px lattice, periodic in x with the world (16 divides the world width).
float lattice(ivec2 c) {
	int cw = int(world_size.x) / 16;
	c.x = (c.x % cw + cw) % cw;
	uint h = uint(c.x) * 374761393u + uint(c.y) * 668265263u;
	h = (h ^ (h >> 13u)) * 1274126177u;
	return float((h ^ (h >> 16u)) & 1023u) / 1023.0;
}

float fine_noise(ivec2 t) {
	vec2 q = (vec2(t) + 0.5) / 16.0;
	ivec2 c = ivec2(floor(q));
	vec2 f = fract(q);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(lattice(c), lattice(c + ivec2(1, 0)), f.x), mix(lattice(c + ivec2(0, 1)), lattice(c + ivec2(1, 1)), f.x), f.y);
}

// Fog look of a pixel. Returns 0 = clear, 1 = cloud (tone = palette index), 2 = unexplored ground glimpsed
// through the frayed edge (tt = fogginess). Signed distance to the frontier pushed by two octaves of noise (the broad
// cloud rank + a 16 px lattice, so the edge meanders instead of tracing province borders), Bayer-dithered.
int fog_px(ivec2 t, bool explored, out int tone, out float tt) {
	tone = 0;
	tt = 0.0;
	if (!fog_on) return 0;
	float dv = texelFetch(fogd_tex, t, 0).r * 255.0;
	vec2 cl = texelFetch(cloud_tex, t / 2, 0).rg * 255.0;
	int ft = int(cl.g + 0.5);
	if (dv >= FOG_FAR) { tone = ft; return explored ? 0 : 1; }
	float push = 0.55 * (cl.r / 127.5 - 1.0) + 0.45 * (fine_noise(t) * 2.0 - 1.0);
	tt = ((explored ? -0.5 - dv / 3.0 : 0.5 + dv / 3.0) + FOG_AMP * push + FOG_BAND * 0.5) / FOG_BAND;
	if (tt >= 1.0) { tone = ft; return 1; }
	if (tt > bayer8(t)) { tone = ft - ft % 3 + 1; return 1; }
	return explored ? 0 : 2;
}

// True where overlays (rivers, routes) must not be drawn: clouds and unexplored ground.
bool fog_hides(vec2 w) {
	if (!fog_on) return false;
	ivec2 t = texel_at(w);
	vec4 info = pinfo(prov_at(t));
	int tone; float tt;
	return fog_px(t, explored_info(info), tone, tt) != 0;
}

// Stale grade (explored but not seen now): faded like the land under it (MapTextures.StaleDesat / StaleDim).
// Map-mode grading of overlays is done with their colour uniforms instead, so rivers stay blue over unowned land.
vec3 stale_grade(vec3 c, vec2 w) {
	if (!fog_on) return c;
	vec4 info = pinfo(prov_at(texel_at(w)));
	if (info.b < 0.25 || info.b > 0.75) return c;
	bool land = info.a > 0.5;
	float m = (c.r + c.g + c.b) / 3.0;
	return mix(c, vec3(m), land ? 0.45 : 0.3) * (land ? 0.78 : 0.85);
}
