# ======================================================================
# KIT TEXTURES  -  seamless procedural textures for palette materials (numpy only; runs inside Blender)
# A palette entry may carry a 5th element: {"tex": recipe, "tile": metres, ...recipe params}.
# Styles (TEXTURE_STYLE in CONFIG): "flat" (no textures), "toon", "painted", "pbr".
# Every map tiles seamlessly: noise is built in the frequency domain (periodic by construction) and
# every stripe/plank pattern uses whole-number repeats per tile.
# Output per material: <name>_albedo.png (sRGB), <name>_normal.png (OpenGL / Unity), <name>_mask.png
# (R = metallic, G = AO-ish cavity, A = smoothness -> URP Lit "Metallic Alpha" smoothness).
# ======================================================================
import numpy as _np

TEXTURE_STYLES = {
    # detail: pattern strength, fine: small-scale detail, levels/soft: banding (soft = blend back to smooth),
    # brush: painterly stroke layer, hue: warm-highlight / cool-shadow shift, normal: normal-map strength
    "toon":    dict(detail=0.45, fine=0.0, levels=5, soft=0.5, brush=0.00, hue=0.35, normal=0.25, rough_var=0.05, wide=1.0),
    "painted": dict(detail=0.80, fine=0.25, levels=0, soft=0.0, brush=0.10, hue=0.70, normal=0.45, rough_var=0.10, wide=1.4),
    "pbr":     dict(detail=1.00, fine=1.0, levels=0, soft=0.0, brush=0.00, hue=0.15, normal=1.00, rough_var=0.25, wide=1.0),
}
WARM = _np.array([1.07, 1.02, 0.90])
COOL = _np.array([0.90, 0.95, 1.10])

def fnoise(n, seed, p=1.0, sx=1.0, sy=1.0, fmin=1.0, fmax=None):
    """Tileable fractal noise in 0..1. p = amplitude falloff (1/f^p); sx/sy > 1 stretch features
    along x (columns, u) / y (rows, v); fmin/fmax in cycles per tile."""
    rng = _np.random.default_rng(seed)
    w = rng.standard_normal((n, n))
    fy = _np.fft.fftfreq(n)[:, None] * n
    fx = _np.fft.fftfreq(n)[None, :] * n
    f = _np.sqrt((fx * sx) ** 2 + (fy * sy) ** 2)
    filt = 1.0 / _np.maximum(f, fmin) ** p
    if fmax:
        filt *= _np.exp(-(f / fmax) ** 2)
    filt[0, 0] = 0
    out = _np.real(_np.fft.ifft2(_np.fft.fft2(w) * filt))
    lo, hi = _np.percentile(out, (1, 99))
    return _np.clip((out - lo) / (hi - lo + 1e-9), 0, 1)

def _grid(n):
    v, u = _np.mgrid[0:n, 0:n] / n          # rows = v, cols = u, both 0..1 over one tile
    return u, v

# ---------------------------------------------------------------- recipes
# Each returns (tone, height, rough_offset, cavity): tone ~ -1..1 brightness variation,
# height 0..1 for the normal map, rough_offset -1..1, cavity 0..1 (1 = open, 0 = crevice).
def r_wood(n, s, st, planks=4, rings=10, seams=True):
    """Planks along u. seams=False -> continuous veneer (grain only, no joints)."""
    u, v = _grid(n)
    warp = fnoise(n, s + 1, p=1.4, sx=6) - 0.5
    grain = _np.sin(2 * _np.pi * (rings * v * planks + 2.5 * warp))
    fibre = fnoise(n, s + 2, p=0.6, sx=24, sy=0.7, fmax=n / 3) - 0.5
    rng = _np.random.default_rng(s + 3)
    pid = _np.floor(v * planks).astype(int) % planks
    ptint = rng.uniform(-1, 1, planks)[pid]
    joint = rng.uniform(0, 1, planks)[pid]                       # one end joint per plank per tile
    fv = (v * planks) % 1.0
    seam = _np.minimum(fv, 1 - fv) < 0.012
    jd = _np.abs(((u - joint) + 0.5) % 1.0 - 0.5) < 0.004
    seams = (seam | jd) if seams else _np.zeros_like(seam)
    tone = 0.30 * grain * st["detail"] + 0.35 * ptint + 0.6 * fibre * st["fine"]
    tone = _np.where(seams, -1.0, tone)
    height = 0.5 + 0.15 * grain * st["detail"] + 0.3 * fibre * st["fine"]
    height = _np.where(seams, 0.0, height)
    return tone, height, 0.4 * fibre * st["fine"], _np.where(seams, 0.3, 1.0)

def r_fabric(n, s, st, weave=96):
    u, v = _grid(n)
    w = (_np.sin(2 * _np.pi * weave * u) * _np.sin(2 * _np.pi * weave * v))
    blot = fnoise(n, s + 1, p=1.6, fmax=12) - 0.5                # crushed-velvet sheen patches
    tone = 0.55 * blot * st["detail"] + 0.12 * w * st["fine"]
    height = 0.5 + 0.25 * w * st["fine"] + 0.15 * blot
    return tone, height, 0.3 * blot, _np.ones_like(u)

def r_brushed(n, s, st):
    streak = fnoise(n, s + 1, p=0.7, sx=40, sy=0.6, fmax=n / 2) - 0.5
    tarnish = fnoise(n, s + 2, p=1.8, fmax=10)
    tarn = _np.clip((tarnish - 0.55) * 3, 0, 1)
    tone = 0.25 * streak * st["fine"] + 0.4 * (tarnish - 0.5) * st["detail"] - 0.5 * tarn * st["detail"]
    height = 0.5 + 0.2 * streak * st["fine"]
    return tone, height, 0.5 * tarn + 0.3 * streak * st["fine"], 1 - 0.3 * tarn

def r_painted_metal(n, s, st):
    base = fnoise(n, s + 1, p=1.7, fmax=16) - 0.5
    chips_n = fnoise(n, s + 2, p=1.1, fmax=n / 4)
    chips = chips_n > 0.86 - 0.06 * (1 - st["detail"])            # worn paint spots
    tone = 0.35 * base * st["detail"]
    tone = _np.where(chips & (st["detail"] > 0.5), -0.7, tone)
    height = 0.5 + 0.1 * base - _np.where(chips, 0.25, 0.0)
    return tone, height, _np.where(chips, -0.5, 0.0) + 0.2 * base, _np.where(chips, 0.6, 1.0)

def r_plaster(n, s, st):
    a = fnoise(n, s + 1, p=1.5, fmax=24) - 0.5
    b = fnoise(n, s + 2, p=0.8, fmax=n / 3) - 0.5
    tone = 0.30 * a * st["detail"] + 0.15 * b * st["fine"]
    return tone, 0.5 + 0.2 * b * st["fine"] + 0.1 * a, 0.2 * a, _np.ones_like(a)

def r_leather(n, s, st):
    c = fnoise(n, s + 1, p=0.9, fmax=n / 4)
    crease = 1 - _np.abs(c - 0.5) * 2                            # ridged noise -> creases
    blot = fnoise(n, s + 2, p=1.6, fmax=10) - 0.5
    tone = 0.4 * blot * st["detail"] - 0.35 * (crease ** 6) * st["fine"]
    return tone, 0.5 + 0.3 * (1 - crease ** 6) * st["fine"], 0.3 * blot, 1 - 0.4 * crease ** 6

def r_marble(n, s, st):
    warp = fnoise(n, s + 1, p=1.5, fmax=20)
    veins = _np.abs(_np.sin(2 * _np.pi * (3 * _grid(n)[0] + 4 * warp)))
    vein = (1 - veins) ** 8
    cloud = fnoise(n, s + 2, p=1.7, fmax=12) - 0.5
    tone = 0.3 * cloud * st["detail"] - 0.6 * vein * st["detail"]
    return tone, 0.5 + 0.05 * cloud, -0.2 * vein, _np.ones_like(cloud)

def r_stone(n, s, st):
    a = fnoise(n, s + 1, p=1.3, fmax=n / 6)
    b = fnoise(n, s + 2, p=0.7, fmax=n / 2) - 0.5
    pits = a < 0.12
    tone = 0.45 * (a - 0.5) * st["detail"] + 0.2 * b * st["fine"] - _np.where(pits, 0.3, 0)
    return tone, 0.3 + 0.5 * a + 0.2 * b * st["fine"], 0.3 * b, _np.where(pits, 0.5, 1.0)

def r_plain(n, s, st):
    a = fnoise(n, s + 1, p=1.6, fmax=12) - 0.5
    return 0.25 * a * st["detail"], 0.5 + 0.05 * a, 0.1 * a, _np.ones_like(a)


# ---------------------------------------------------------------- wallpaper (two-colour printed patterns)
def _soft(d, w):
    """Antialiased step: 1 where d < 0, 0 where d > w."""
    return _np.clip(0.5 - d / max(w, 1e-6), 0, 1)

def _cells(u, v, k, half_drop=False):
    """Local cell coords (-0.5..0.5) on a k x k lattice per tile; half_drop offsets every other column."""
    cu = u * k
    col = _np.floor(cu)
    cv = v * k + (0.5 * (col % 2) if half_drop else 0)
    return (cu % 1.0) - 0.5, (cv % 1.0) - 0.5

def wp_damask(u, v, k, px):
    x, y = _cells(u, v, k, half_drop=True)
    r = _np.sqrt(x * x + (y * 0.85) ** 2); th = _np.arctan2(y, _np.abs(x))
    petals = r - 0.30 * (0.55 + 0.45 * _np.abs(_np.cos(3 * th)))
    body = _soft(petals, px)
    hole = _soft(_np.abs(r - 0.13) - 0.025, px)                    # ring cut into the centre
    stem = _soft(_np.abs(x) - 0.012, px) * _soft(_np.abs(y) - 0.46, px) * (r > 0.3)
    corner = _soft(_np.abs(_np.abs(x) - 0.5) + _np.abs(_np.abs(y) - 0.5) - 0.07, px)   # small diamonds
    return _np.clip(body - hole + stem + corner, 0, 1)

def wp_fans(u, v, k, px):
    """Art Deco sunburst fans in overlapping rows (scales)."""
    cu, cv = u * k, v * k
    m = _np.zeros_like(u)
    for dy, off in ((0.0, 0.0), (0.5, 0.5), (1.0, 0.0)):         # lower rows drawn first, upper rows cover
        x = ((cu + off) % 1.0) - 0.5
        y = (cv % 1.0) - dy + 0.5 - 0.5
        r = _np.sqrt(x * x + y * y)
        inside = (r < 0.5) & (y > 0)
        rays = _soft(_np.abs(((_np.arctan2(y, x) / _np.pi * 11) % 1.0) - 0.5) - 0.12, 0.04)
        rings = _soft(_np.abs(((r * 6) % 1.0) - 0.5) - 0.36, 0.05)
        edge = _soft(_np.abs(r - 0.47) - 0.02, px)
        fan = _np.clip(rays * (r > 0.12) * (r < 0.42) + edge + _soft(r - 0.07, px), 0, 1)
        m = _np.where(inside, fan, m)
    return m

def wp_stripes(u, v, k, px):
    x = (u * k) % 1.0
    wide = _soft(_np.abs(x - 0.5) - 0.18, px)
    pins = _soft(_np.abs(x - 0.12) - 0.012, px) + _soft(_np.abs(x - 0.88) - 0.012, px)
    return _np.clip(wide + pins, 0, 1)

def wp_trellis(u, v, k, px):
    a = (u * k + v * k) % 1.0; b = (u * k - v * k) % 1.0
    lines = _soft(_np.minimum(_np.abs(a - 0.5), _np.abs(b - 0.5)) - 0.025, px * 0.7)
    x, y = _cells(u, v, k)
    r = _np.sqrt(x * x + y * y); th = _np.arctan2(y, x)
    quatre = _soft(r - 0.12 * (0.7 + 0.3 * _np.cos(4 * th)), px)  # quatrefoil in each diamond
    return _np.clip(lines + quatre, 0, 1)

def wp_chevron(u, v, k, px):
    z = (v * k + _np.abs(((u * k) % 1.0) - 0.5)) % 1.0
    return _soft(_np.abs(z - 0.5) - 0.10, px * 1.5)

def wp_floral(u, v, k, px, seed=7):
    rng = _np.random.default_rng(seed)
    m = _np.zeros_like(u)
    for _ in range(3):                                            # 3 jittered flowers per cell
        ox, oy = rng.uniform(0, 1, 2)
        x = ((u * k + ox) % 1.0) - 0.5; y = ((v * k + oy) % 1.0) - 0.5
        r = _np.sqrt(x * x + y * y); th = _np.arctan2(y, x) + rng.uniform(0, 6.28)
        petal = _soft(r - 0.09 * (0.55 + 0.45 * _np.abs(_np.cos(2.5 * th))), px)
        m = _np.maximum(m, petal - _soft(r - 0.022, px))
    return _np.clip(m, 0, 1)

WALLPAPERS = {"damask": wp_damask, "fans": wp_fans, "stripes": wp_stripes, "trellis": wp_trellis,
              "chevron": wp_chevron, "floral": wp_floral}

def r_wallpaper(n, s, st, pattern="damask", ink=(0.8, 0.6, 0.2), repeats=4):
    """Palette colour = paper, ink = printed colour. repeats = pattern repeats per texture tile."""
    u, v = _grid(n)
    mix = WALLPAPERS[pattern](u, v, repeats, 1.5 / n * repeats)
    fibre = fnoise(n, s + 1, p=0.5, fmax=n / 2) - 0.5
    print_var = fnoise(n, s + 2, p=1.4, fmax=20) - 0.5            # uneven ink
    age = fnoise(n, s + 3, p=1.8, fmax=5) - 0.5
    mix = _np.clip(mix * (0.9 + 0.15 * print_var * st["detail"]), 0, 1)
    tone = 0.08 * fibre * st["fine"] + 0.10 * age * st["detail"]
    height = 0.5 + 0.18 * mix * st["detail"] + 0.08 * fibre * st["fine"]   # slightly raised ink
    return tone, height, -0.15 * mix, 1 - 0.05 * mix, (mix, _np.array(ink))

RECIPES = {"wood": r_wood, "fabric": r_fabric, "velvet": r_fabric, "brushed": r_brushed,
           "brass": r_brushed, "painted_metal": r_painted_metal, "plaster": r_plaster,
           "paint": r_plaster, "leather": r_leather, "marble": r_marble, "stone": r_stone,
           "plain": r_plain, "wallpaper": r_wallpaper}

def _posterize(x, levels):
    return _np.round(x * (levels - 1)) / (levels - 1) if levels else x

def make_texture_set(entry, style, n=512, seed=1):
    """Returns dict of float arrays (n x n x 4, rows = v from bottom): albedo (sRGB), normal, mask."""
    name, color, metallic, rough = entry[:4]
    spec = entry[4] if len(entry) > 4 else {}
    st = TEXTURE_STYLES[style]
    fn = RECIPES.get(spec.get("tex", "plain"), r_plain)
    params = {k: v for k, v in spec.items() if k not in ("tex", "tile")}
    s = seed * 1000 + sum(map(ord, name))
    res = fn(n, s, st, **params)
    tone, height, rvar, cav = res[:4]
    base = _np.array(color)[None, None, :]
    if len(res) > 4:                                              # two-colour recipes (wallpaper)
        mix, ink = res[4]
        base = base * (1 - mix[..., None]) + ink[None, None, :] * mix[..., None]
    # broad light/dark variation every material gets (keeps flat areas alive at any style)
    tone = tone + 0.35 * st["wide"] * st["detail"] * (fnoise(n, s + 40, p=1.8, fmax=6) - 0.5) * 2
    if st["brush"]:                                               # painterly strokes: soft, directional
        strokes = fnoise(n, s + 50, p=0.9, sx=4, sy=1.0, fmin=2, fmax=40) - 0.5
        tone = tone + st["brush"] * strokes * 3
    t = _np.clip(tone, -1, 1)
    if st["levels"]:
        q = _posterize((t + 1) / 2, st["levels"]) * 2 - 1
        t = q * (1 - st["soft"]) + t * st["soft"]
    k = 1.0 + t * 0.42
    lin = base * k[..., None]
    if st["hue"]:                                                 # warm lights, cool shadows
        w = _np.clip(t, 0, 1)[..., None] * st["hue"]
        c = _np.clip(-t, 0, 1)[..., None] * st["hue"]
        lin = lin * (1 + (WARM - 1) * w) * (1 + (COOL - 1) * c)
    lin = _np.clip(lin, 0, 1)
    srgb = _np.where(lin <= 0.0031308, lin * 12.92, 1.055 * lin ** (1 / 2.4) - 0.055)
    a = _np.ones((n, n, 1))
    albedo = _np.concatenate([srgb, a], axis=2)
    h = height * st["normal"] * 6.0
    dx = (_np.roll(h, -1, 1) - _np.roll(h, 1, 1)) * 0.5
    dy = (_np.roll(h, -1, 0) - _np.roll(h, 1, 0)) * 0.5
    nrm = _np.dstack([-dx, -dy, _np.ones_like(h)])
    nrm /= _np.linalg.norm(nrm, axis=2, keepdims=True)
    normal = _np.concatenate([nrm * 0.5 + 0.5, a], axis=2)
    r = _np.clip(rough + rvar * st["rough_var"] * 2, 0.02, 1.0)
    mask = _np.dstack([_np.full((n, n), metallic), _np.clip(cav, 0, 1), _np.zeros((n, n)), 1 - r])
    return {"albedo": albedo, "normal": normal, "mask": mask}
