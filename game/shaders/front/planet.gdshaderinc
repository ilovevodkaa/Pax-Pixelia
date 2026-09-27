// The rotating pixel planet of the title screen: the real next world (NextWorld.PlanetMap, an equirectangular
// thumbnail of WorldData.BaseColor that wraps horizontally like the map) projected onto a sphere on the vp
// grid. Colours are toned down to sit in the monochrome night; the terminator, clouds and halo are dithered.
// Needs front_common.gdshaderinc.

uniform sampler2D planet_map : filter_nearest, repeat_enable;
uniform float planet_reveal = 0.0;             // 0 = «forming» pattern, 1 = the world (stepped k/8 by C#)
uniform vec2 planet_pos = vec2(0.86, 0.26);    // centre as a fraction of the vp grid
uniform float planet_size = 0.125;             // radius as a fraction of the vp grid height
uniform float planet_spin = 0.009;             // revolutions per second

const vec3 PLANET_LIGHT = vec3(-0.6, 0.45, 0.66);   // from the upper left, like the skyline
const vec3 PLANET_HALO = vec3(0.30, 0.31, 0.34);
const vec3 PLANET_RIM = vec3(0.56, 0.58, 0.63);
const vec3 PLANET_CLOUD = vec3(0.70, 0.70, 0.72);
const float PLANET_HALO_VP = 10.0;

vec3 planet_axis(vec3 n) {
	// tilt the spin axis (roll 20°, pitch 17°) so the poles and the stretched map rows stay off-centre
	const float CR = 0.9397;
	const float SR = 0.3420;
	const float CP = 0.9563;
	const float SP = 0.2924;
	n = vec3(CR * n.x - SR * n.y, SR * n.x + CR * n.y, n.z);
	return vec3(n.x, CP * n.y - SP * n.z, SP * n.y + CP * n.z);
}

vec3 spin_y(vec3 n, float a) {
	float c = cos(a);
	float s = sin(a);
	return vec3(c * n.x + s * n.z, n.y, -s * n.x + c * n.z);
}

// rgb + coverage: 1 on the disc, < 1 for the halo, 0 elsewhere
vec4 planet(vec2 px, vec2 vsize, float tq) {
	vec2 c = floor(vsize * planet_pos);
	float r = floor(vsize.y * planet_size);
	vec2 d = (px + 0.5 - c) / r;
	float dist = length(d);
	if (dist > 1.0 + PLANET_HALO_VP / r) return vec4(0.0);
	vec3 L = normalize(PLANET_LIGHT);
	if (dist > 1.0) {
		float side = clamp(dot(normalize(vec3(d.x, -d.y, 0.25)), L) * 0.5 + 0.5, 0.0, 1.0);
		float k = 1.0 - (dist - 1.0) * r / PLANET_HALO_VP;
		return vec4(PLANET_HALO, dither(k * k * (0.2 + 0.6 * side), px, 4.0) * 0.6);
	}
	vec3 n = vec3(d.x, -d.y, sqrt(max(0.0, 1.0 - dist * dist)));
	float light = dot(n, L);
	vec3 w = planet_axis(n);
	float spin = tq * planet_spin * TAU;
	vec3 ws = spin_y(w, spin);
	vec2 uv = vec2(fract(atan(ws.x, ws.z) / TAU + 0.5), 0.5 - asin(clamp(ws.y, -1.0, 1.0)) / PI);

	vec3 map = textureLod(planet_map, uv, 0.0).rgb;
	float g = dot(map, vec3(0.3, 0.59, 0.11));
	vec3 surf = mix(vec3(g), map, 0.5) * 0.78;

	// «forming»: slow grey swirls condensing on the sphere
	vec3 fw = spin_y(w, tq * 0.35);
	float f = noise3(fw * 3.0 + vec3(0.0, tq * 0.25, 0.0)) * 0.65 + noise3(fw * 7.0) * 0.35;
	vec3 form = mix(vec3(0.09, 0.095, 0.11), vec3(0.26, 0.27, 0.30), dither(f, px, 3.0));

	vec3 col = bayer4(px) < planet_reveal ? surf : form;

	// clouds drift a little faster than the ground; two dithered densities
	vec3 cw = spin_y(w, spin * 1.35 + 1.7);
	float cl = noise3(cw * 4.0) * 0.6 + noise3(cw * 9.0) * 0.4;
	// solid cloud cores with a checkered fringe (no salt-and-pepper speckle)
	float checker = mod(px.x + px.y, 2.0);
	float cloud = (step(0.68, cl) + step(0.64, cl) * step(cl, 0.68) * checker) * step(0.5, planet_reveal);
	col = mix(col, PLANET_CLOUD * mix(0.55, 1.0, clamp(light + 0.3, 0.0, 1.0)), cloud * 0.75);

	// terminator: 5 dithered light bands, the night side stays faintly visible
	float lit = dither(clamp(light * 1.2 + 0.12, 0.0, 1.0), px, 5.0);
	col *= mix(0.08, 1.0, lit);
	// thin atmosphere on the lit limb
	float rim = smoothstep(0.9, 1.0, dist) * clamp(light + 0.2, 0.0, 1.0);
	col = mix(col, PLANET_RIM, dither(rim * 0.55, px, 2.0));
	return vec4(col, 1.0);
}
