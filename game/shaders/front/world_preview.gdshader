shader_type canvas_item;
// «Новая игра» world preview (MAIN_MENU.md §2.3): the decimated terrain wraps around X by a whole number of pixels,
// dims with a Bayer pattern while the next world is generating and reveals itself through the same pattern.
// dim and reveal only ever change in steps (the screen code sets them), never smoothly.

uniform sampler2D map : filter_nearest, repeat_enable;
uniform float offset_px = 0.0;   // integer pixels
uniform float map_w = 640.0;
uniform float dim = 0.0;         // 0 … 0.65
uniform float reveal = 1.0;      // 0 … 1
uniform vec4 ink : source_color = vec4(0.043, 0.043, 0.047, 1.0);

float bayer4(vec2 p) {
	int x = int(mod(p.x, 4.0));
	int y = int(mod(p.y, 4.0));
	int m[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
	return (float(m[x + y * 4]) + 0.5) / 16.0;
}

void fragment() {
	vec4 c = texture(map, UV + vec2(offset_px / map_w, 0.0));
	float b = bayer4(floor(UV / TEXTURE_PIXEL_SIZE));
	c.rgb *= 1.0 - dim * (0.75 + 0.5 * b);
	COLOR = b < reveal ? vec4(c.rgb, 1.0) : ink;
}
