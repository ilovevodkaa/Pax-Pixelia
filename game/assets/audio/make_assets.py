# Builds the game's sound files from CC0 packs in <repo>/audio_src (AUDIO.md §6.3; not in git): Kenney (Interface
# Sounds, UI Audio, RPG Audio, Impact Sounds, Music Jingles from kenney.nl) in audio_src/kenney, OwlishMedia's
# «202 More Sound Effects» and «Sound Effects Pack» (opengameart.org) in audio_src/owlish/{more,big}, rubberduck's
# «100 CC0 metal and wood SFX» (opengameart.org) in audio_src/rubberduck/wm, BigSoundBank OGGs in audio_src/bigsoundbank:
# trims silence (Kenney files carry long silent tails), mixes the few composites (hammer blows, footsteps,
# bells, the claim stinger, the era ladder), levels every file to its layer's RMS (AUDIO.md §1 p.4: one
# normalisation for all sources, peaks ≤ −1 dBFS) and encodes OGG Vorbis q5 at 44.1 kHz.
# UI and world sounds are mono (AUDIO.md §6.2), stingers keep the jingles' stereo.
# Needs numpy, scipy, soundfile and ffmpeg on PATH. The front-end's first takes in assets/audio/sfx are not rebuilt.
#   python game/assets/audio/make_assets.py [<game_root> ...]   → <game_root>/assets/audio/{ui,world,stingers}/*.ogg
#   (default: this game folder) and assets/audio/sources.json — every file with the Kenney files it is made of.
import os, sys, subprocess, tempfile, json
import numpy as np, soundfile as sf
from scipy.signal import resample_poly

GAME = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
SRC = os.environ.get('PAX_KENNEY') or os.path.join(os.path.dirname(GAME), 'audio_src', 'kenney')   # PAX_KENNEY: another packs folder
PACK = {
    'IS': 'kenney_interface-sounds/Audio', 'UA': 'kenney_ui-audio/Audio', 'RPG': 'kenney_rpg-audio/Audio',
    'IMP': 'kenney_impact-sounds/Audio', 'PIZ': 'kenney_music-jingles/Audio/Pizzicato jingles',
    'HIT': 'kenney_music-jingles/Audio/Hit jingles', 'NES': 'kenney_music-jingles/Audio/8-Bit jingles',
    # other recorded CC0 sources, next to the Kenney packs in audio_src (not in git): so the game is not one author
    'OWL': '../owlish/more', 'OWB': '../owlish/big', 'RD': '../rubberduck/wm', 'BSB': '../bigsoundbank',
}
PACK_NAME = {'IS': 'Interface Sounds', 'UA': 'UI Audio', 'RPG': 'RPG Audio', 'IMP': 'Impact Sounds',
             'PIZ': 'Music Jingles', 'HIT': 'Music Jingles', 'NES': 'Music Jingles',
             'OWL': 'OwlishMedia 202 More Sound Effects', 'OWB': 'OwlishMedia Sound Effects Pack',
             'RD': 'rubberduck 100 CC0 metal and wood SFX', 'BSB': 'BigSoundBank (Joseph Sardin)'}
SR = 44100

def load(ref):
    pack, name = ref.split(':', 1)
    x, sr = sf.read(os.path.join(SRC, PACK[pack], name), always_2d=True, dtype='float64')
    if sr != SR: x = resample_poly(x, SR // 100, sr // 100, axis=0)
    return x

def trim(x, head_db=-50, tail_db=-45, tail_keep=.03, fade=.025):
    """Cut the silent lead-in and the silent tail (−45 dB below the peak), then a short fade-out."""
    m = np.abs(x).max(axis=1); pk = m.max() + 1e-12
    on = np.where(m > pk * 10 ** (head_db / 20))[0]
    tail = np.where(m > pk * 10 ** (tail_db / 20))[0]
    a = max(0, on[0] - int(.002 * SR)); b = min(len(x), tail[-1] + int(tail_keep * SR))
    y = x[a:b].copy()
    n = min(len(y), int(fade * SR))
    y[-n:] *= np.linspace(1, 0, n)[:, None]
    return y

def pitch(x, ratio):
    """Resample to shift pitch (and length) by ratio — bells only."""
    up, down = int(round(1000 / ratio)), 1000
    return resample_poly(x, up, down, axis=0)

def db(g): return 10 ** (g / 20)

def mix(parts, channels):
    """parts: [(signal, offset_s, gain_db)] → one buffer."""
    end = max(int(o * SR) + len(s) for s, o, _ in parts)
    out = np.zeros((end, channels))
    for s, o, g in parts:
        if s.shape[1] != channels: s = np.repeat(s.mean(axis=1, keepdims=True), channels, axis=1)
        i = int(o * SR); out[i:i + len(s)] += s * db(g)
    return out

def active_rms(x):
    m = np.abs(x).max(axis=1); pk = m.max() + 1e-12
    idx = np.where(m > pk * 10 ** (-40 / 20))[0]
    a = x[idx[0]:idx[-1] + 1]
    return 20 * np.log10(np.sqrt(np.mean(a ** 2)) + 1e-12)

def limit(x, ceiling_db=-1.0, release=.06, look=.003):
    """Look-ahead peak limiter: the gain dips just enough to keep every sample under the ceiling."""
    from scipy.ndimage import maximum_filter1d
    c = db(ceiling_db)
    need = np.maximum(1.0, np.abs(x).max(axis=1) / c)
    la = max(1, int(look * SR))
    need = maximum_filter1d(need, size=2 * la + 1)
    k = np.exp(-1 / (release * SR))
    g = np.empty_like(need); cur = 1.0
    for i in range(len(need)):
        cur = need[i] if need[i] > cur else cur * k + need[i] * (1 - k)
        g[i] = cur
    return x / g[:, None]

def level(x, target, max_limit_db=6.0):
    """Gain to the layer's RMS; transient-heavy files (coins, steps, bells) may be limited by up to 6 dB."""
    y, total = x, 0.0
    for _ in range(3):   # limiting lowers the RMS of single-transient files: re-measure and top up
        g = target - active_rms(y)
        if g < .3 or total >= max_limit_db * 2: break
        peak = 20 * np.log10(np.abs(y).max() + 1e-12)
        g = min(g, -1.0 - peak + max_limit_db)
        y = y * db(g); total += g
        if 20 * np.log10(np.abs(y).max() + 1e-12) > -1.0: y = limit(y)
    if active_rms(y) > target + .3: y = y * db(target - active_rms(y))
    if 20 * np.log10(np.abs(y).max() + 1e-12) > -1.0: y = limit(y)   # a loud composite that needed no gain
    return y, total

def src(ref, **kw): return trim(load(ref), **kw)

def cut(ref, max_s, fade=.04, **kw):
    """The first max_s seconds of a trimmed source with a short fade: one knock of a drumstick pair, one stroke of
    a typewriter, one tick of a clock."""
    y = src(ref, **kw)
    n = min(len(y), int(max_s * SR)); y = y[:n].copy()
    f = min(n, int(fade * SR)); y[-f:] *= np.linspace(1, 0, f)[:, None]
    return y

# ------------------------------------------------------------------ the sound list
# (output path under assets/audio, target RMS dBFS, mono?, builder, sources for the licence manifest)
OUT = []
def add(path, target, mono, build, refs):
    OUT.append((path, target, mono, build, refs))

def one(ref, **kw): return lambda: src(ref, **kw)

# UI (mono). Reference: the front-end's click.ogg ≈ −22 dB RMS, hover ≈ −31 dB (AUDIO §1 p.4).
for i, r in enumerate(['UA:switch10.ogg', 'UA:switch11.ogg'], 1):
    add(f'ui/ui_tab_{i}.ogg', -23, True, one(r), [r])
# one physical switch: «вкл» (switch23) and «выкл» (switch22) of the same pair
add('ui/ui_toggle_on_1.ogg', -23, True, one('UA:switch23.ogg'), ['UA:switch23.ogg'])
add('ui/ui_toggle_off_1.ogg', -23, True, one('UA:switch22.ogg'), ['UA:switch22.ogg'])
# time speed: one cluster of real switches, light → heavy (levels −25 … −21 dB)
for i, (r, t) in enumerate([('UA:switch28.ogg', -25), ('UA:switch26.ogg', -24), ('UA:switch19.ogg', -23),
                            ('UA:switch18.ogg', -22), ('UA:switch16.ogg', -21)], 1):
    add(f'ui/ui_speed_{i}.ogg', t, True, one(r), [r])
add('ui/ui_pause_1.ogg', -22, True, one('RPG:metalLatch.ogg'), ['RPG:metalLatch.ogg'])
add('ui/ui_book_open_1.ogg', -21, True,
    lambda: mix([(src('RPG:bookOpen.ogg'), 0, 0), (src('RPG:bookFlip3.ogg'), .05, -4)], 1), ['RPG:bookOpen.ogg', 'RPG:bookFlip3.ogg'])
add('ui/ui_book_close_1.ogg', -22, True, one('RPG:bookClose.ogg'), ['RPG:bookClose.ogg'])
for i, r in enumerate(['RPG:bookFlip2.ogg', 'RPG:bookFlip3.ogg'], 1):
    add(f'ui/ui_page_{i}.ogg', -25, True, one(r), [r])
for i, r in enumerate(['RPG:handleCoins.ogg', 'RPG:handleCoins2.ogg'], 1):
    add(f'ui/ui_coins_{i}.ogg', -24, True, one(r), [r])
for i, r in enumerate(['IS:glass_002.ogg', 'IS:glass_006.ogg'], 1):
    add(f'ui/ui_toast_{i}.ogg', -26, True, one(r), [r])
add('ui/ui_toast_important_1.ogg', -22, True, one('IS:bong_001.ogg'), ['IS:bong_001.ogg'])
for i, r in enumerate(['IS:pluck_001.ogg', 'IS:pluck_002.ogg'], 1):
    add(f'ui/ui_note_{i}.ogg', -27, True, one(r), [r])
add('ui/ui_question_1.ogg', -23, True, one('IS:question_002.ogg'), ['IS:question_002.ogg'])

# more variants of the front-end's interface sounds (sfx/ keeps the first of each, levelled to match it:
# click ≈ UI Audio click2 −22 dB, open/close ≈ maximize/minimize_008, confirm ≈ confirmation_001, error ≈ error_008)
for i, r in enumerate(['UA:click1.ogg', 'UA:click4.ogg'], 2):
    add(f'ui/ui_click_{i}.ogg', -22, True, one(r), [r])
add('ui/ui_open_2.ogg', -21.6, True, one('IS:maximize_006.ogg'), ['IS:maximize_006.ogg'])
add('ui/ui_close_2.ogg', -22.9, True, one('IS:minimize_006.ogg'), ['IS:minimize_006.ogg'])
add('ui/ui_confirm_2.ogg', -20, True, one('IS:confirmation_002.ogg'), ['IS:confirmation_002.ogg'])
add('ui/ui_error_2.ogg', -21, True, one('IS:error_004.ogg'), ['IS:error_004.ogg'])

# WORLD (mono): the table-top diorama — wooden piece, hammer, pick, footsteps (AUDIO §1 p.2, §2.4)
for i, r in enumerate(['IMP:impactWood_light_000.ogg', 'IMP:impactWood_light_002.ogg',
                       'IMP:impactWood_light_004.ogg', 'IMP:impactWood_light_001.ogg'], 1):
    add(f'world/world_piece_{i}.ogg', -20, True, one(r), [r])

def hammer(woods, metals):
    # three blows «тук-тук-ТУК»: a wooden knock with the nail's metal tick under it, the last one heavier
    parts = []
    for k, (w, m) in enumerate(zip(woods, metals)):
        t = k * .19
        parts.append((src(f'IMP:{w}'), t, [-4, -3, 0][k]))
        parts.append((src(f'IMP:{m}'), t + .002, [-14, -13, -11][k]))
    return mix(parts, 1)
H1 = (['impactWood_medium_000.ogg', 'impactWood_medium_003.ogg', 'impactWood_heavy_001.ogg'],
      ['impactMetal_light_001.ogg', 'impactMetal_light_004.ogg', 'impactMetal_light_000.ogg'])
H2 = (['impactWood_medium_001.ogg', 'impactWood_medium_004.ogg', 'impactWood_heavy_004.ogg'],
      ['impactMetal_light_004.ogg', 'impactMetal_light_001.ogg', 'impactMetal_light_002.ogg'])
for i, (w, m) in enumerate([H1, H2], 1):
    add(f'world/world_build_{i}.ogg', -20, True, (lambda w=w, m=m: hammer(w, m)), [f'IMP:{f}' for f in w + m])

def dig(a, b, ore=False):
    parts = [(src(f'IMP:{a}'), 0, 0), (src(f'IMP:{b}'), .30, -3)]
    if ore:  # the pick rings on metal, then a small glint
        parts += [(src('IMP:impactMetal_medium_003.ogg'), .56, -2), (src('IMP:impactGlass_light_002.ogg'), .60, -9)]
    return mix(parts, 1)
add('world/world_survey_1.ogg', -21, True, lambda: dig('impactMining_000.ogg', 'impactMining_003.ogg'),
    ['IMP:impactMining_000.ogg', 'IMP:impactMining_003.ogg'])
add('world/world_ore_1.ogg', -20, True, lambda: dig('impactMining_001.ogg', 'impactMining_004.ogg', True),
    ['IMP:impactMining_001.ogg', 'IMP:impactMining_004.ogg', 'IMP:impactMetal_medium_003.ogg', 'IMP:impactGlass_light_002.ogg'])

def steps(files, gains, tail=None):
    parts = [(src(f'IMP:{f}'), k * .21, g) for k, (f, g) in enumerate(zip(files, gains))]
    if tail: parts.append((src(tail[0]), tail[1], tail[2]))
    return mix(parts, 1)
# scouts set out: three steps walking away with a pack's rustle; they come back: two steps and the map's paper
add('world/world_scouts_out_1.ogg', -22, True,
    lambda: steps(['footstep_grass_000.ogg', 'footstep_grass_003.ogg', 'footstep_grass_004.ogg'], [0, -3, -7],
                  ('RPG:cloth4.ogg', 0, -2)),
    ['IMP:footstep_grass_000.ogg', 'IMP:footstep_grass_003.ogg', 'IMP:footstep_grass_004.ogg', 'RPG:cloth4.ogg'])
add('world/world_scouts_back_1.ogg', -22, True,
    lambda: steps(['footstep_grass_004.ogg', 'footstep_grass_000.ogg'], [-6, -2], ('RPG:bookFlip3.ogg', .40, 4)),
    ['IMP:footstep_grass_004.ogg', 'IMP:footstep_grass_000.ogg', 'RPG:bookFlip3.ogg'])

# construction complete (a capital project): one heavy blow with the nail's tick, then a soft rising third
add('world/world_built_1.ogg', -21, True,
    lambda: mix([(src('IMP:impactWood_heavy_000.ogg'), 0, 0), (src('IMP:impactMetal_light_002.ogg'), .002, -12),
                 (src('PIZ:jingles_PIZZI08.ogg'), .16, -3)], 1),
    ['IMP:impactWood_heavy_000.ogg', 'IMP:impactMetal_light_002.ogg', 'PIZ:jingles_PIZZI08.ogg'])

# second takes of the scouts and the geologists (the same recipe, other steps / blows)
add('world/world_scouts_out_2.ogg', -22, True,
    lambda: steps(['footstep_grass_001.ogg', 'footstep_grass_002.ogg', 'footstep_grass_003.ogg'], [0, -3, -7],
                  ('RPG:cloth3.ogg', 0, -2)),
    ['IMP:footstep_grass_001.ogg', 'IMP:footstep_grass_002.ogg', 'IMP:footstep_grass_003.ogg', 'RPG:cloth3.ogg'])
add('world/world_scouts_back_2.ogg', -22, True,
    lambda: steps(['footstep_grass_002.ogg', 'footstep_grass_001.ogg'], [-6, -2], ('RPG:bookFlip2.ogg', .40, 4)),
    ['IMP:footstep_grass_002.ogg', 'IMP:footstep_grass_001.ogg', 'RPG:bookFlip2.ogg'])
add('world/world_survey_2.ogg', -21, True, lambda: dig('impactMining_002.ogg', 'impactMining_004.ogg'),
    ['IMP:impactMining_002.ogg', 'IMP:impactMining_004.ogg'])

# STINGERS (stereo). Free stand-ins until the leitmotif / Ovani (AUDIO §2.5): pizzicato for discoveries,
# 8-bit for easter eggs, drums → bells → strings → chip as the era ladder.
def claim(cloth, stake):
    # ~0.9 s, paired with the capture fill: the banner flaps, the stake knocks in, a rising fifth closes it
    return mix([(src(cloth), 0, 0), (src(f'IMP:{stake}'), .30, -2), (src('PIZ:jingles_PIZZI16.ogg'), .40, 0)], 2)
add('stingers/stinger_claim_1.ogg', -20, False, lambda: claim('RPG:cloth2.ogg', 'impactWood_heavy_001.ogg'),
    ['RPG:cloth2.ogg', 'IMP:impactWood_heavy_001.ogg', 'PIZ:jingles_PIZZI16.ogg'])
add('stingers/stinger_claim_2.ogg', -20, False, lambda: claim('RPG:cloth4.ogg', 'impactWood_heavy_003.ogg'),
    ['RPG:cloth4.ogg', 'IMP:impactWood_heavy_003.ogg', 'PIZ:jingles_PIZZI16.ogg'])
add('stingers/stinger_eureka.ogg', -18, False, one('PIZ:jingles_PIZZI10.ogg'), ['PIZ:jingles_PIZZI10.ogg'])
add('stingers/stinger_meet.ogg', -18, False, one('PIZ:jingles_PIZZI13.ogg'), ['PIZ:jingles_PIZZI13.ogg'])
add('stingers/stinger_disaster.ogg', -17, False, one('PIZ:jingles_PIZZI07.ogg'), ['PIZ:jingles_PIZZI07.ogg'])
add('stingers/stinger_religion.ogg', -18, False,
    lambda: mix([(src('IMP:impactBell_heavy_001.ogg'), 0, 0), (src('IMP:impactBell_heavy_000.ogg'), .55, -2)], 2),
    ['IMP:impactBell_heavy_001.ogg', 'IMP:impactBell_heavy_000.ogg'])
for i, r in enumerate(['NES:jingles_NES05.ogg', 'NES:jingles_NES08.ogg', 'NES:jingles_NES16.ogg'], 1):
    add(f'stingers/stinger_egg_{i}.ogg', -19, False, one(r), [r])

def era(layer):
    parts = [(src('HIT:jingles_HIT15.ogg'), 0, 0)]
    parts += layer
    return mix(parts, 2)
def bells_up():
    b = src('IMP:impactBell_heavy_002.ogg')
    return [(pitch(b, 1.0), .30, -3), (pitch(b, 1.1225), .55, -3), (pitch(b, 1.335), .80, -1)]   # +2, +5 semitones
add('stingers/stinger_era_2.ogg', -16, False, lambda: era([]), ['HIT:jingles_HIT15.ogg'])
add('stingers/stinger_era_3.ogg', -16, False, lambda: era(bells_up()), ['HIT:jingles_HIT15.ogg', 'IMP:impactBell_heavy_002.ogg'])
add('stingers/stinger_era_4.ogg', -16, False, lambda: era([(src('PIZ:jingles_PIZZI02.ogg'), .28, 0)]),
    ['HIT:jingles_HIT15.ogg', 'PIZ:jingles_PIZZI02.ogg'])
add('stingers/stinger_era_5.ogg', -16, False, lambda: era([(src('NES:jingles_NES12.ogg'), .28, -3)]),
    ['HIT:jingles_HIT15.ogg', 'NES:jingles_NES12.ogg'])

# MORE SOURCES (recorded, CC0): extra takes of the everyday keys, so a click, a page or a coin is not always the
# same author's — OwlishMedia (coins, latches, pages), rubberduck (wood), BigSoundBank (writing, clock, typewriter)
for i, r in enumerate(['OWL:Money/Money_05.wav', 'OWL:Money/Money_15.wav', 'OWL:Money/Money_29.wav'], 3):
    add(f'ui/ui_coins_{i}.ogg', -24, True, (lambda r=r: cut(r, .45)), [r])
for i, r in enumerate(['OWB:Paper/pageturn1.wav', 'OWB:Paper/pageturn2.wav'], 3):
    add(f'ui/ui_page_{i}.ogg', -25, True, (lambda r=r: cut(r, .6)), [r])
for i, r in enumerate(['OWL:Keys, Locks, Door/Key_Lock_Door_30.wav', 'OWL:Keys, Locks, Door/Key_Lock_Door_51.wav'], 2):
    add(f'ui/ui_pause_{i}.ogg', -22, True, (lambda r=r: cut(r, .35)), [r])
for i, r in enumerate(['RD:wood_hit_01.ogg', 'RD:wood_hit_04.ogg', 'RD:wood_hit_07.ogg'], 5):
    add(f'world/world_piece_{i}.ogg', -20, True, (lambda r=r: cut(r, .25)), [r])
add('world/world_build_3.ogg', -20, True,
    lambda: mix([(cut('RD:wood_hammer_01.ogg', .2), 0, -4), (cut('RD:wood_hammer_02.ogg', .2), .19, -3),
                 (cut('RD:wood_hammer_01.ogg', .3), .38, 0)], 1), ['RD:wood_hammer_01.ogg', 'RD:wood_hammer_02.ogg'])

# ERA SKINS (AUDIO.md §2.2): the keys with a material — confirm, page, book, pause — sound of their era group;
# hover, click and the rest stay the same all game (the hand gets used to them). ui/skin/g<G>_<key>_<n>.ogg
SKIN = {
    1: {  # Костёр: hand drum, wood, stone
        'confirm': [('OWB:Impacts/djembe1.wav', .3), ('OWB:Impacts/djembe2.wav', .3)],
        'page': [('BSB:1022.ogg', .45)],
        'pause': [('BSB:0466.ogg', .22)],
        'book_open': [('BSB:1489.ogg', .3)],
        'book_close': [('RD:wood_close_01.ogg', .45)],
    },
    2: {  # Глина: crockery, papyrus
        'confirm': [('OWB:Impacts/clamour7.wav', .3), ('OWB:Impacts/clamour11.wav', .35)],
        'page': [('OWB:Paper/pageturn1.wav', .55)],
        'pause': [('RD:wood_misc_02.ogg', .3)],
        'book_open': [('OWB:Paper/pageturn2.wav', .6)],
    },
    3: {  # Перо: the quill (a pencil stands in) and parchment, an iron latch
        'confirm': [('BSB:3236.ogg', .5), ('BSB:0221.ogg', .45)],
        'page': [('BSB:0785.ogg', .5), ('OWB:Paper/pageturn2.wav', .6)],
        'pause': [('OWL:Keys, Locks, Door/Key_Lock_Door_51.wav', .35)],
    },
    4: {  # Латунь: the typewriter, the clock, sprung metal
        'confirm': [('BSB:2844.ogg', .6)],
        'page': [('BSB:2842.ogg', .18), ('BSB:2843.ogg', .2)],
        'pause': [('BSB:0007.ogg', .5)],
        'book_open': [('RD:metal_open_01.ogg', .45)],
        'book_close': [('RD:metal_close_01.ogg', .45)],
    },
    5: {  # Сигнал: glass and electronics
        'confirm': [('OWB:UI/UI_023.wav', .45), ('OWB:UI/UI_024.wav', .45)],
        'page': [('OWB:UI/UI_035.wav', .4), ('OWB:UI/UI_037.wav', .4)],
        'pause': [('OWB:UI/UI_033.wav', .45)],
        'book_open': [('OWB:UI/UI_026.wav', .5)],
        'book_close': [('OWB:UI/UI_036.wav', .45)],
    },
}
SKIN_LEVEL = {'confirm': -20, 'page': -25, 'pause': -22, 'book_open': -21, 'book_close': -22}
for g, keys in SKIN.items():
    for key, takes in keys.items():
        for i, (r, mx) in enumerate(takes, 1):
            add(f'ui/skin/g{g}_{key}_{i}.ogg', SKIN_LEVEL[key], True, (lambda r=r, mx=mx: cut(r, mx)), [r])

# ------------------------------------------------------------------ build
def encode(x, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with tempfile.NamedTemporaryFile(suffix='.wav', delete=False) as t: tmp = t.name
    sf.write(tmp, np.clip(x, -1, 1), SR, subtype='PCM_16')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', tmp, '-c:a', 'libvorbis', '-q:a', '5', '-map_metadata', '-1', path], check=True)
    os.remove(tmp)

def main(roots):
    manifest = []
    for path, target, mono, build, refs in OUT:
        x = build()
        if mono: x = x.mean(axis=1, keepdims=True)
        elif x.shape[1] == 1: x = np.repeat(x, 2, axis=1)
        x, g = level(x, target)
        for r in roots: encode(x, os.path.join(r, 'assets/audio', path))
        dur = len(x) / SR
        manifest.append((path, refs, round(dur, 2), round(active_rms(x), 1), round(20 * np.log10(np.abs(x).max()), 1)))
        print(f"{path:38s} {dur:4.2f}s rms {active_rms(x):6.1f} pk {20*np.log10(np.abs(x).max()):5.1f}  <- {', '.join(refs)}")
    for r in roots:
        with open(os.path.join(r, 'assets/audio/sources.json'), 'w', encoding='utf-8', newline=chr(10)) as f:
            json.dump([{'file': p, 'from': [f"{PACK_NAME[x.split(':', 1)[0]]}: {x.split(':', 1)[1]}" for x in refs], 'sec': d, 'rms': rm, 'peak': pk}
                       for p, refs, d, rm, pk in manifest], f, ensure_ascii=False, indent=1)
            f.write(chr(10))

if __name__ == '__main__':
    main(sys.argv[1:] or [GAME])
