# Randare fotorealistă a unei scene Casa3D în Blender (Cycles).
#
# Scena vine din aplicație: vederea 3D → „Randare foto (Blender)” descarcă un fișier *.casa3d.json cu exact
# formele vederii 3D (cutii, cutii rotunjite, cilindri, plăci, tor), poziția lor, materialul și lumina.
# Scriptul le reconstruiește cu materiale realiste (lemn, parchet, gresie, textil, piele, sticlă, ceramică,
# crom), cer fizic cu soarele din aplicație, lumini în camere și câte o cameră foto pe încăpere.
#
# Rulare (Windows, PowerShell):
#   & "C:\...\blender.exe" -b -P casa3d_render.py -- scena.casa3d.json D:\randari\casa
#       [--samples 256] [--device AUTO|OPTIX|CUDA|HIP|ONEAPI|METAL|CPU] [--width 1920] [--height 1280]
#       [--rooms all|id1,id2] [--panorama] [--export fbx,glb,obj] [--blend] [--no-render]
# Ieșire: <cameră>.png pentru fiecare încăpere, <cameră>-360.png cu --panorama, scene.blend cu --blend,
#         casa.fbx / casa.glb / casa.obj cu --export (pentru 3ds Max, SketchUp, Twinmotion, D5...).
import argparse
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

# Scena din aplicație are Y în sus (three.js); Blender are Z în sus: (x, y, z) -> (x, -z, y).
Y_UP_TO_Z_UP = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


def to_blender(v):
    return Vector((v[0], -v[2], v[1]))


def parse_args(argv):
    argv = argv[argv.index('--') + 1:] if '--' in argv else []
    p = argparse.ArgumentParser(prog='casa3d_render', description='Randare fotorealistă Casa3D în Blender')
    p.add_argument('scene', help='fișierul *.casa3d.json descărcat din aplicație')
    p.add_argument('out', help='dosarul pentru imagini și exporturi')
    p.add_argument('--samples', type=int, default=256)
    p.add_argument('--device', default='AUTO')
    p.add_argument('--width', type=int, default=1920)
    p.add_argument('--height', type=int, default=1280)
    p.add_argument('--rooms', default='all')
    p.add_argument('--panorama', action='store_true')
    p.add_argument('--export', default='')
    p.add_argument('--blend', action='store_true')
    p.add_argument('--assets', default=os.environ.get('CASA3D_ASSETS', ''),
                   help='dosar cu texturi Poly Haven (CC0): laminate_floor_02, large_grey_tiles, plastered_wall, rough_linen, velour_velvet, leather_white și cerurile *.hdr')
    p.add_argument('--models', default=os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'public', 'models'),
                   help='dosarul cu modelele decorative glTF (implicit public/models din aplicație)')
    p.add_argument('--no-render', action='store_true')
    p.add_argument('--no-portals', action='store_true', help='fără portaluri de lumină în ferestre')
    p.add_argument('--exposure', default='auto', help="'auto' (pe cameră, dintr-o trecere rapidă) sau o valoare fixă în EV")
    p.add_argument('--gamma', type=float, default=None, help='ridicarea tonurilor medii după AgX (implicit 1,1)')
    return p.parse_args(argv)


def hex_rgb(h, default=(0.8, 0.8, 0.8)):
    """Culoare #rrggbb (sRGB) -> RGBA liniar, cum o așteaptă Blender."""
    if not isinstance(h, str) or len(h) != 7 or h[0] != '#':
        return (*default, 1.0)
    try:
        s = [int(h[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    except ValueError:
        return (*default, 1.0)
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in s]
    return (lin[0], lin[1], lin[2], 1.0)


def scaled(rgba, k):
    return (min(1.0, rgba[0] * k), min(1.0, rgba[1] * k), min(1.0, rgba[2] * k), 1.0)


# ---------------------------------------------------------------- materiale
# Texturi foto (Poly Haven, CC0), folosite când există --assets: tip -> (dosar, mărimea unei repetări în metri, tărie relief)
TEXTURES = {'parquet': ('laminate_floor_02', 2.0, 1.0), 'tile': ('large_grey_tiles', 2.4, 1.0), 'fabric': ('rough_linen', 0.5, 0.8),
            'velvet': ('velour_velvet', 0.5, 0.6), 'leather': ('leather_white', 0.6, 0.8), 'wall': ('plastered_wall', 2.5, 0.12),
            'wood': ('oak_veneer_01', 1.2, 0.35)}
GREY = (1.0, 1.0, 1.0, 1.0)  # rampa neutră a fibrei (0,72…1)
# luminanța medie (liniară) a furnirului: fibra foto păstrează desenul lemnului, nuanța vine din culoarea aleasă în aplicație
WOOD_MEAN_LUM = 0.2374


# muchii rotunjite la randare (nodul Bevel, 1,5 mm) pe materialele comune ale mobilierului și pereților: Cycles prinde
# lumina pe muchie ca la o piesă reală; sticla, oglinda, emisia, parchetul și gresia rămân fără (nu au muchii vizibile)
BEVEL_KINDS = {'paint', 'wood', 'metal', 'chrome', 'ceramic', 'leather', 'fabric', 'velvet', 'rattan', 'screen'}
BEVEL_RADIUS = 0.0015


def add_bevel(m):
    nt = m.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    if bsdf is None:
        return
    bev = nt.nodes.new('ShaderNodeBevel')
    bev.inputs['Radius'].default_value = BEVEL_RADIUS
    bev.samples = 4
    nrm = bsdf.inputs['Normal']
    if nrm.is_linked:  # relieful existent (hartă de normale / bump) trece prin Bevel
        link = nrm.links[0]
        src = link.from_socket
        nt.links.remove(link)
        nt.links.new(src, bev.inputs['Normal'])
    nt.links.new(bev.outputs['Normal'], nrm)


class Materials:
    def __init__(self, assets=''):
        self.cache = {}
        self.assets = assets if assets and os.path.isdir(assets) else ''

    def image(self, folder, name, color):
        path = os.path.join(self.assets, folder, f'{name}.jpg')
        if not os.path.isfile(path):
            return None
        img = bpy.data.images.load(path, check_existing=True)
        img.colorspace_settings.name = 'sRGB' if color else 'Non-Color'
        return img

    def textured(self, nt, inp, kind, col, d):
        """Material cu texturi foto: culoarea aleasă rămâne (pereți, textile), parchetul și gresia își păstrează imaginea."""
        key = 'wall' if kind == 'paint' and float(d.get('r', 0.6)) >= 0.85 else kind
        if not self.assets or key not in TEXTURES:
            return False
        folder, size, strength = TEXTURES[key]
        nor = self.image(folder, 'nor_gl', False)
        if nor is None:
            return False
        geo = nt.nodes.new('ShaderNodeNewGeometry')
        mp = nt.nodes.new('ShaderNodeMapping')
        mp.inputs['Scale'].default_value = (1 / size, 1 / size, 1 / size)
        nt.links.new(geo.outputs['Position'], mp.inputs['Vector'])

        def tex(img):
            t = nt.nodes.new('ShaderNodeTexImage')
            t.image = img
            t.projection = 'BOX'
            t.projection_blend = 0.2
            nt.links.new(mp.outputs['Vector'], t.inputs['Vector'])
            return t
        diff, rough = self.image(folder, 'Diffuse', True), self.image(folder, 'Rough', False)
        if key == 'wood' and diff is not None:
            # furnir real: luminozitatea fibrei (raportată la media texturii) înmulțește culoarea aleasă — lemnul gri,
            # nuc sau stejar arată ca lemn, nu ca un model generat
            bw = nt.nodes.new('ShaderNodeRGBToBW')
            nt.links.new(tex(diff).outputs['Color'], bw.inputs['Color'])
            div = nt.nodes.new('ShaderNodeMath')
            div.operation = 'DIVIDE'
            div.use_clamp = False
            nt.links.new(bw.outputs['Val'], div.inputs[0])
            div.inputs[1].default_value = WOOD_MEAN_LUM
            mix = nt.nodes.new('ShaderNodeMix')
            mix.data_type = 'RGBA'
            mix.blend_type = 'MULTIPLY'
            mix.inputs['Factor'].default_value = 1.0
            mix.inputs['A'].default_value = col
            # contrast moderat al fibrei (0,55 + 0,45·v) și plafon 1,3: petele luminoase ale furnirului nu mai ies albe
            soft = nt.nodes.new('ShaderNodeMath')
            soft.operation = 'MULTIPLY_ADD'
            # pe lemn deschis (stejar alb, bej, sonoma) culoarea e deja aproape de alb: fibra doar umbrește, nu luminează
            light = sum(col[:3]) / 3 > 0.25   # liniar: stejar artisan ~0,38, nuc ~0,09
            soft.inputs[1].default_value = 0.22 if light else 0.45
            soft.inputs[2].default_value = 0.8 if light else 0.55
            nt.links.new(div.outputs['Value'], soft.inputs[0])
            cap = nt.nodes.new('ShaderNodeMath')
            cap.operation = 'MINIMUM'
            cap.inputs[1].default_value = 1.05 if light else 1.3
            nt.links.new(soft.outputs['Value'], cap.inputs[0])
            cc = nt.nodes.new('ShaderNodeCombineColor')
            for i in range(3):
                nt.links.new(cap.outputs['Value'], cc.inputs[i])
            nt.links.new(cc.outputs['Color'], mix.inputs['B'])
            nt.links.new(mix.outputs['Result'], inp['Base Color'])
            inp['Coat Weight'].default_value = 0.15
        if key in ('parquet', 'tile') and diff is not None:
            mix = nt.nodes.new('ShaderNodeMix')
            mix.data_type = 'RGBA'
            mix.blend_type = 'MULTIPLY'
            mix.inputs['Factor'].default_value = 1.0
            # fotografia laminatului e caldă (portocalie sub AgX): saturația redusă, nuanța vine din culoarea aplicației
            hs = nt.nodes.new('ShaderNodeHueSaturation')
            hs.inputs['Saturation'].default_value = 0.65 if key == 'parquet' else 1.0
            nt.links.new(tex(diff).outputs['Color'], hs.inputs['Color'])
            nt.links.new(hs.outputs['Color'], mix.inputs['A'])
            mix.inputs['B'].default_value = col  # nuanța aleasă în aplicație (alb = textura neschimbată)
            nt.links.new(mix.outputs['Result'], inp['Base Color'])
        if rough is not None:
            nt.links.new(tex(rough).outputs['Color'], inp['Roughness'])
        nm = nt.nodes.new('ShaderNodeNormalMap')
        nm.inputs['Strength'].default_value = strength
        nt.links.new(tex(nor).outputs['Color'], nm.inputs['Color'])
        nt.links.new(nm.outputs['Normal'], inp['Normal'])
        if kind in ('fabric', 'velvet'):
            inp['Sheen Weight'].default_value = 0.35 if kind == 'fabric' else 1.0
        if kind == 'leather':
            inp['Coat Weight'].default_value = 0.3
        return True

    # ---- finisaje de designer (modul de așezare, rostul, placările pe pereți), la dimensiunile reale din aplicație
    def _uv(self, nt, world_pos, rot=0.0):
        """Coordonatele pardoselii în metri, rotite cu `rot` (direcția lamelelor sau diagonala)."""
        mp = nt.nodes.new('ShaderNodeMapping')
        mp.inputs['Rotation'].default_value = (0.0, 0.0, rot)
        nt.links.new(world_pos(), mp.inputs['Vector'])
        return mp.outputs['Vector']

    def floor_pattern(self, nt, inp, d, tint, bump, world_pos, wood_grain):
        pat = d['pat']
        c = hex_rgb(pat.get('color', '#b88a5a'))
        base = (c[0] * tint[0], c[1] * tint[1], c[2] * tint[2], 1.0)
        wood = pat.get('kind') == 'parquet'
        if d.get('img') and pat.get('rect'):
            # spic / chevron: aceeași imagine ca în aplicație, întinsă pe dreptunghiul camerei (Blender: X = x, Y = -z)
            import base64
            import hashlib
            import tempfile
            raw = base64.b64decode(d['img'].split(',', 1)[1])
            path = os.path.join(tempfile.mkdtemp(prefix='c3d_'), hashlib.sha256(raw).hexdigest()[:16] + '.jpg')
            with open(path, 'wb') as fh:
                fh.write(raw)
            img = bpy.data.images.load(path)
            img.pack()  # imaginea intră în .blend; fișierul temporar nu mai e necesar
            os.remove(path)
            os.rmdir(os.path.dirname(path))
            x0, z0, x1, z1 = pat['rect']
            sep = nt.nodes.new('ShaderNodeSeparateXYZ')
            nt.links.new(world_pos(), sep.inputs['Vector'])

            def lin(sock, a, b):  # (sock + a) * b
                n1 = nt.nodes.new('ShaderNodeMath')
                n1.operation = 'ADD'
                n1.inputs[1].default_value = a
                nt.links.new(sock, n1.inputs[0])
                n2 = nt.nodes.new('ShaderNodeMath')
                n2.operation = 'MULTIPLY'
                n2.inputs[1].default_value = b
                nt.links.new(n1.outputs[0], n2.inputs[0])
                return n2.outputs[0]
            comb = nt.nodes.new('ShaderNodeCombineXYZ')
            nt.links.new(lin(sep.outputs['X'], -x0, 1 / max(1e-6, x1 - x0)), comb.inputs['X'])
            nt.links.new(lin(sep.outputs['Y'], z1, 1 / max(1e-6, z1 - z0)), comb.inputs['Y'])
            t = nt.nodes.new('ShaderNodeTexImage')
            t.image = img
            t.extension = 'EXTEND'
            t.interpolation = 'Cubic'
            nt.links.new(comb.outputs['Vector'], t.inputs['Vector'])
            mix = nt.nodes.new('ShaderNodeMix')
            mix.data_type = 'RGBA'
            mix.blend_type = 'MULTIPLY'
            mix.inputs['Factor'].default_value = 1.0
            nt.links.new(t.outputs['Color'], mix.inputs['A'])
            mix.inputs['B'].default_value = tint
            nt.links.new(mix.outputs['Result'], inp['Base Color'])
            bump(t.outputs['Color'], 0.15, 0.002)
        else:
            # drept, decalat, șah, diagonală: plăci/lamele de mărimea reală cu rost (Brick Texture). Nodul decalează doar rândurile
            # cu indexul multiplu de `offset_frequency`, deci „1/3” devine aproximativ: rânduri alternând 0 și 1/3.
            p = pat.get('pattern', 'straight')
            rot = (math.pi / 2 if pat.get('angle') == 90 else 0.0) + (math.pi / 4 if p == 'diagonal' else 0.0)
            br = nt.nodes.new('ShaderNodeTexBrick')
            br.offset = 0.5 if p == 'brick' else 0.333 if p == 'third' else 0.0
            br.offset_frequency = 2
            br.inputs['Scale'].default_value = 1.0
            br.inputs['Brick Width'].default_value = float(pat.get('pieceL', 0.6))
            br.inputs['Row Height'].default_value = float(pat.get('pieceW', 0.6))
            br.inputs['Mortar Size'].default_value = max(0.0006, float(pat.get('grout', 0.003)))
            br.inputs['Mortar Smooth'].default_value = 0.1
            br.inputs['Color1'].default_value = scaled(base, 0.86 if wood else 0.97)
            br.inputs['Color2'].default_value = scaled(base, 1.1 if wood else 1.02)
            br.inputs['Mortar'].default_value = hex_rgb(pat.get('groutColor', '#bdb8ae'))
            uv = self._uv(nt, world_pos, rot)
            nt.links.new(uv, br.inputs['Vector'])
            color = br.outputs['Color']
            if p == 'checker':  # (floor(x/L) + floor(y/W)) mod 2 alege nuanța închisă
                sep = nt.nodes.new('ShaderNodeSeparateXYZ')
                nt.links.new(uv, sep.inputs['Vector'])
                cells = []
                for axis, size in (('X', pat.get('pieceL', 0.6)), ('Y', pat.get('pieceW', 0.6))):
                    dv = nt.nodes.new('ShaderNodeMath')
                    dv.operation = 'DIVIDE'
                    dv.inputs[1].default_value = float(size)
                    nt.links.new(sep.outputs[axis], dv.inputs[0])
                    fl = nt.nodes.new('ShaderNodeMath')
                    fl.operation = 'FLOOR'
                    nt.links.new(dv.outputs[0], fl.inputs[0])
                    cells.append(fl.outputs[0])
                ad = nt.nodes.new('ShaderNodeMath')
                ad.operation = 'ADD'
                nt.links.new(cells[0], ad.inputs[0])
                nt.links.new(cells[1], ad.inputs[1])
                md = nt.nodes.new('ShaderNodeMath')
                md.operation = 'FLOORED_MODULO'
                md.inputs[1].default_value = 2.0
                nt.links.new(ad.outputs[0], md.inputs[0])
                mx = nt.nodes.new('ShaderNodeMix')
                mx.data_type = 'RGBA'
                mx.blend_type = 'MULTIPLY'
                nt.links.new(md.outputs[0], mx.inputs['Factor'])
                nt.links.new(color, mx.inputs['A'])
                mx.inputs['B'].default_value = (0.78, 0.78, 0.78, 1.0)
                color = mx.outputs['Result']
            if wood:
                # fibra modulează doar luminozitatea (rampă gri): înmulțirea cu o rampă în culoarea lemnului ridica culoarea
                # la pătrat și parchetul ieșea portocaliu
                r, _w = wood_grain(GREY, 14.0)
                mix = nt.nodes.new('ShaderNodeMix')
                mix.data_type = 'RGBA'
                mix.blend_type = 'MULTIPLY'
                mix.inputs['Factor'].default_value = 0.5
                nt.links.new(color, mix.inputs['A'])
                nt.links.new(r.outputs['Color'], mix.inputs['B'])
                color = mix.outputs['Result']
            nt.links.new(color, inp['Base Color'])
            bump(br.outputs['Fac'], 0.3, 0.002)
        inp['Roughness'].default_value = 0.42 if wood else 0.25
        inp['Coat Weight'].default_value = 0.15 if wood else 0.25

    def wall_finish(self, nt, inp, kind, d, col, bump, noise, ramp, world_pos):
        """Placări pe pereți. Coordonata de-a lungul peretelui e X + Y (pereții sunt paraleli cu axele), înălțimea e Z."""
        def wall_uv():
            sep = nt.nodes.new('ShaderNodeSeparateXYZ')
            nt.links.new(world_pos(), sep.inputs['Vector'])
            ad = nt.nodes.new('ShaderNodeMath')
            ad.operation = 'ADD'
            nt.links.new(sep.outputs['X'], ad.inputs[0])
            nt.links.new(sep.outputs['Y'], ad.inputs[1])
            comb = nt.nodes.new('ShaderNodeCombineXYZ')
            nt.links.new(ad.outputs[0], comb.inputs['X'])
            nt.links.new(sep.outputs['Z'], comb.inputs['Y'])
            return comb.outputs['Vector']

        def bricks(w, h, mortar, mortar_col, var, offset):
            br = nt.nodes.new('ShaderNodeTexBrick')
            br.offset = offset
            br.inputs['Scale'].default_value = 1.0
            br.inputs['Brick Width'].default_value = w
            br.inputs['Row Height'].default_value = h
            br.inputs['Mortar Size'].default_value = mortar
            br.inputs['Color1'].default_value = scaled(col, 1 - var)
            br.inputs['Color2'].default_value = scaled(col, 1 + var)
            br.inputs['Mortar'].default_value = hex_rgb(mortar_col)
            nt.links.new(wall_uv(), br.inputs['Vector'])
            nt.links.new(br.outputs['Color'], inp['Base Color'])
            return br
        size = d.get('size') or []
        if kind == 'brick':
            w, h = (max(size), min(size)) if len(size) == 2 else (0.24, 0.071)
            br = bricks(w, h, 0.01, '#cfc8bd', 0.18, 0.5)
            bump(br.outputs['Fac'], 0.6, 0.01)
            inp['Roughness'].default_value = 0.92
        elif kind == 'tile':
            w, h = (min(size), max(size)) if len(size) == 2 else (0.3, 0.6)
            br = bricks(w, h, 0.0025, '#e4e1db', 0.03, 0.0)
            bump(br.outputs['Fac'], 0.3, 0.002)
            inp['Roughness'].default_value = 0.2
            inp['Coat Weight'].default_value = 0.3
        elif kind == 'stone':
            br = bricks(0.38, 0.13, 0.012, '#8d8a84', 0.22, 0.4)
            n = noise(18.0, 6.0)
            bump(n.outputs['Fac'], 0.7, 0.02)
            inp['Roughness'].default_value = 0.95
        elif kind == 'plaster':
            n = noise(140.0, 8.0)
            bump(n.outputs['Fac'], 0.35, 0.004)
            inp['Roughness'].default_value = 0.9
        else:  # tapet: vinil semi-mat; modelul marmură primește vinișoare din zgomot
            inp['Roughness'].default_value = 0.55
            if d.get('marble'):
                # vinișoare subțiri: o curbă de nivel a zgomotului (|zgomot - 0,5| mic), nu pete întregi
                n = noise(1.6, 12.0)
                n.inputs['Distortion'].default_value = 2.5
                sub = nt.nodes.new('ShaderNodeMath')
                sub.operation = 'SUBTRACT'
                sub.inputs[1].default_value = 0.5
                nt.links.new(n.outputs['Fac'], sub.inputs[0])
                ab = nt.nodes.new('ShaderNodeMath')
                ab.operation = 'ABSOLUTE'
                nt.links.new(sub.outputs[0], ab.inputs[0])
                r = ramp(ab.outputs[0], scaled(col, 0.8), col)
                r.color_ramp.elements[0].position = 0.0
                r.color_ramp.elements[1].position = 0.012
                nt.links.new(r.outputs['Color'], inp['Base Color'])

    def get(self, d):
        key = json.dumps(d, sort_keys=True)
        if key not in self.cache:
            m = self.build(d)
            if d.get('k', 'paint') in BEVEL_KINDS:
                add_bevel(m)
            self.cache[key] = m
        return self.cache[key]

    def build(self, d):
        kind, col = d.get('k', 'paint'), hex_rgb(d.get('c'))
        m = bpy.data.materials.new(f"c3d_{kind}_{d.get('c', '')}")
        m.use_nodes = True
        nt = m.node_tree
        bsdf = nt.nodes['Principled BSDF']
        out = nt.nodes['Material Output']
        inp = bsdf.inputs
        inp['Base Color'].default_value = col
        inp['Roughness'].default_value = max(0.02, min(1.0, float(d.get('r', 0.6))))
        inp['Metallic'].default_value = float(d.get('mt', 0))
        world_pos = lambda: nt.nodes.new('ShaderNodeNewGeometry').outputs['Position']
        designer = bool(d.get('pat')) or kind in ('wallpaper', 'plaster', 'brick', 'stone') or (kind == 'tile' and d.get('size'))
        if not designer and self.textured(nt, inp, kind, col, d):
            return m

        def bump(height_socket, strength, distance=0.01):
            b = nt.nodes.new('ShaderNodeBump')
            b.inputs['Strength'].default_value = strength
            b.inputs['Distance'].default_value = distance
            nt.links.new(height_socket, b.inputs['Height'])
            nt.links.new(b.outputs['Normal'], inp['Normal'])

        def noise(scale, detail=4.0):
            n = nt.nodes.new('ShaderNodeTexNoise')
            n.inputs['Scale'].default_value = scale
            n.inputs['Detail'].default_value = detail
            nt.links.new(world_pos(), n.inputs['Vector'])
            return n

        def ramp(fac, c1, c2):
            r = nt.nodes.new('ShaderNodeValToRGB')
            r.color_ramp.elements[0].color = c1
            r.color_ramp.elements[1].color = c2
            nt.links.new(fac, r.inputs['Fac'])
            return r

        def wood_grain(base, scale=6.0):
            # inele de lemn: unde distorsionate de zgomot, pe coordonatele din lume (continuu peste piese)
            w = nt.nodes.new('ShaderNodeTexWave')
            w.wave_type = 'BANDS'
            w.bands_direction = 'X'
            w.inputs['Scale'].default_value = scale
            w.inputs['Distortion'].default_value = 1.6   # fibră lină (fără texturile foto); 7 dădea pete „camuflaj”
            w.inputs['Detail'].default_value = 3.0
            nt.links.new(world_pos(), w.inputs['Vector'])
            r = ramp(w.outputs['Fac'], scaled(base, 0.72), scaled(base, 1.12))
            return r, w

        if d.get('pat'):
            self.floor_pattern(nt, inp, d, col, bump, world_pos, wood_grain)
            return m
        if designer:
            self.wall_finish(nt, inp, kind, d, col, bump, noise, ramp, world_pos)
            return m
        if kind in ('wood', 'rattan'):
            r, w = wood_grain(col, 9.0 if kind == 'wood' else 30.0)
            nt.links.new(r.outputs['Color'], inp['Base Color'])
            bump(w.outputs['Fac'], 0.08 if kind == 'wood' else 0.4)
            inp['Roughness'].default_value = 0.55 if kind == 'wood' else 0.8
        elif kind == 'parquet':
            # parchet: plăci de 19 x 120 cm decalate, cu nuanțe diferite și fibra lemnului
            base = scaled(hex_rgb('#b0865a'), 1.0)
            tint = col
            base = (base[0] * tint[0], base[1] * tint[1], base[2] * tint[2], 1.0)
            br = nt.nodes.new('ShaderNodeTexBrick')
            br.offset = 0.33
            br.inputs['Scale'].default_value = 1.0
            br.inputs['Brick Width'].default_value = 1.2
            br.inputs['Row Height'].default_value = 0.19
            br.inputs['Mortar Size'].default_value = 0.0015
            br.inputs['Color1'].default_value = scaled(base, 0.82)
            br.inputs['Color2'].default_value = scaled(base, 1.08)
            br.inputs['Mortar'].default_value = scaled(base, 0.45)
            nt.links.new(world_pos(), br.inputs['Vector'])
            r, w = wood_grain(GREY, 14.0)
            mix = nt.nodes.new('ShaderNodeMix')
            mix.data_type = 'RGBA'
            mix.blend_type = 'MULTIPLY'
            mix.inputs['Factor'].default_value = 0.55
            nt.links.new(br.outputs['Color'], mix.inputs['A'])
            nt.links.new(r.outputs['Color'], mix.inputs['B'])
            nt.links.new(mix.outputs['Result'], inp['Base Color'])
            bump(br.outputs['Fac'], 0.25, 0.002)
            inp['Roughness'].default_value = 0.42
            inp['Coat Weight'].default_value = 0.15
        elif kind == 'tile':
            # gresie/faianță: plăci de 60 cm cu rost
            br = nt.nodes.new('ShaderNodeTexBrick')
            br.offset = 0.0
            br.inputs['Scale'].default_value = 1.0
            br.inputs['Brick Width'].default_value = 0.6
            br.inputs['Row Height'].default_value = 0.6
            br.inputs['Mortar Size'].default_value = 0.003
            tile = col if d.get('c', '#ffffff').lower() != '#ffffff' else hex_rgb('#d9d8d2')
            br.inputs['Color1'].default_value = tile
            br.inputs['Color2'].default_value = scaled(tile, 0.96)
            br.inputs['Mortar'].default_value = hex_rgb('#9a9890')
            nt.links.new(world_pos(), br.inputs['Vector'])
            nt.links.new(br.outputs['Color'], inp['Base Color'])
            bump(br.outputs['Fac'], 0.3, 0.003)
            inp['Roughness'].default_value = 0.22
            inp['Coat Weight'].default_value = 0.3
        elif kind in ('fabric', 'velvet'):
            n = noise(380.0, 2.0)
            bump(n.outputs['Fac'], 0.25, 0.002)
            inp['Roughness'].default_value = 0.92 if kind == 'fabric' else 0.75
            inp['Sheen Weight'].default_value = 0.35 if kind == 'fabric' else 1.0
            inp['Sheen Roughness'].default_value = 0.4
        elif kind == 'leather':
            n = noise(120.0, 6.0)
            bump(n.outputs['Fac'], 0.15, 0.002)
            inp['Roughness'].default_value = 0.45
            inp['Coat Weight'].default_value = 0.3
            inp['Coat Roughness'].default_value = 0.4
        elif kind == 'metal':
            inp['Metallic'].default_value = 1.0
            inp['Roughness'].default_value = 0.3
        elif kind == 'chrome':
            inp['Metallic'].default_value = 1.0
            inp['Roughness'].default_value = 0.08
            inp['Base Color'].default_value = scaled(col, 1.0) if d.get('c') else (0.9, 0.9, 0.9, 1)
        elif kind == 'mirror':
            inp['Metallic'].default_value = 1.0
            inp['Roughness'].default_value = 0.01
            inp['Base Color'].default_value = (0.95, 0.95, 0.95, 1)
        elif kind == 'ceramic':
            inp['Roughness'].default_value = 0.08
            inp['Coat Weight'].default_value = 1.0
        elif kind == 'screen':
            inp['Base Color'].default_value = (0.01, 0.01, 0.012, 1)
            inp['Roughness'].default_value = 0.12
            inp['Coat Weight'].default_value = 0.6
        elif kind in ('glass', 'frost'):
            # sticla lasă lumina soarelui să treacă direct (umbrele nu devin caustice zgomotoase)
            inp['Transmission Weight'].default_value = 1.0
            inp['Roughness'].default_value = 0.0 if kind == 'glass' else 0.3
            inp['IOR'].default_value = 1.45
            inp['Base Color'].default_value = scaled(col, 1.0) if kind == 'frost' else (0.96, 0.98, 0.98, 1)
            lp = nt.nodes.new('ShaderNodeLightPath')
            tr = nt.nodes.new('ShaderNodeBsdfTransparent')
            mix = nt.nodes.new('ShaderNodeMixShader')
            nt.links.new(lp.outputs['Is Shadow Ray'], mix.inputs['Fac'])
            nt.links.new(bsdf.outputs['BSDF'], mix.inputs[1])
            nt.links.new(tr.outputs['BSDF'], mix.inputs[2])
            nt.links.new(mix.outputs['Shader'], out.inputs['Surface'])
        elif kind == 'sheer':
            # perdea: țesătură translucidă (lumina trece difuz), parțial transparentă după opacitatea din aplicație
            inp['Roughness'].default_value = 0.9
            inp['Sheen Weight'].default_value = 0.5
            tl = nt.nodes.new('ShaderNodeBsdfTranslucent')
            tl.inputs['Color'].default_value = col
            tr = nt.nodes.new('ShaderNodeBsdfTransparent')
            m1 = nt.nodes.new('ShaderNodeMixShader')
            m1.inputs['Fac'].default_value = 0.45
            nt.links.new(bsdf.outputs['BSDF'], m1.inputs[1])
            nt.links.new(tl.outputs['BSDF'], m1.inputs[2])
            m2 = nt.nodes.new('ShaderNodeMixShader')
            m2.inputs['Fac'].default_value = max(0.0, min(1.0, 1.0 - float(d.get('o', 0.3)) * 1.4))
            nt.links.new(m1.outputs['Shader'], m2.inputs[1])
            nt.links.new(tr.outputs['BSDF'], m2.inputs[2])
            nt.links.new(m2.outputs['Shader'], out.inputs['Surface'])
        elif kind == 'emit':
            inp['Emission Color'].default_value = hex_rgb(d.get('e', '#fff1dc'))
            inp['Emission Strength'].default_value = 6.0 * float(d.get('ei', 1.0))
        else:  # vopsea: pereți și piese lăcuite; pereții mați primesc o textură fină de tencuială
            if float(d.get('r', 0.6)) >= 0.85:
                n = noise(60.0, 8.0)
                bump(n.outputs['Fac'], 0.06, 0.004)
        return m


# ---------------------------------------------------------------- geometrie (în sistemul local al aplicației)
def mesh_box(name, w, h, d):
    bm = bmesh.new()
    x, y, z = w / 2, h / 2, d / 2
    v = [bm.verts.new(p) for p in ((-x, -y, -z), (x, -y, -z), (x, y, -z), (-x, y, -z), (-x, -y, z), (x, -y, z), (x, y, z), (-x, y, z))]
    # ordinea fețelor = grupurile BoxGeometry din three.js: +x, -x, +y, -y, +z, -z (materialele pe fețe ale pereților)
    faces = [(1, 2, 6, 5), (0, 4, 7, 3), (3, 7, 6, 2), (0, 1, 5, 4), (4, 5, 6, 7), (0, 3, 2, 1)]
    for i, f in enumerate(faces):
        face = bm.faces.new([v[k] for k in f])
        face.material_index = i
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return me


def mesh_plane(name, w, h):
    bm = bmesh.new()
    v = [bm.verts.new(p) for p in ((-w / 2, -h / 2, 0), (w / 2, -h / 2, 0), (w / 2, h / 2, 0), (-w / 2, h / 2, 0))]
    bm.faces.new(v)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return me


def mesh_cylinder(name, rt, rb, h, seg, open_ended, t0, tl):
    seg = max(8, int(seg))
    bm = bmesh.new()
    full = abs(tl - 2 * math.pi) < 1e-3
    n = seg if full else seg + 1
    ring = lambda r, y: [bm.verts.new((r * math.sin(t0 + tl * i / seg), y, r * math.cos(t0 + tl * i / seg))) for i in range(n)]
    top, bot = ring(rt, h / 2), ring(rb, -h / 2)
    for i in range(seg):
        j = (i + 1) % n
        bm.faces.new((top[i], top[j], bot[j], bot[i]))
    if not open_ended and full:
        if rt > 1e-5:
            bm.faces.new(top)
        if rb > 1e-5:
            bm.faces.new(list(reversed(bot)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    return me


def mesh_torus(name, R, tube, rseg, tseg, arc):
    rseg, tseg = max(6, int(rseg)), max(8, int(tseg))
    bm = bmesh.new()
    full = abs(arc - 2 * math.pi) < 1e-3
    cols = tseg if full else tseg + 1
    grid = []
    for j in range(cols):
        u = arc * j / tseg
        row = []
        for i in range(rseg):
            v = 2 * math.pi * i / rseg
            row.append(bm.verts.new(((R + tube * math.cos(v)) * math.cos(u), (R + tube * math.cos(v)) * math.sin(u), tube * math.sin(v))))
        grid.append(row)
    for j in range(tseg):
        a, b = grid[j], grid[(j + 1) % cols]
        for i in range(rseg):
            k = (i + 1) % rseg
            bm.faces.new((a[i], b[i], b[k], a[k]))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    return me


def mesh_sphere(name, r, wseg, hseg):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=max(8, int(wseg)), v_segments=max(6, int(hseg)), radius=r)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    return me


def mesh_lathe(name, pts, seg, phi0, phil):
    """Corp de revoluție (cuve, vase WC, abajururi) ca LatheGeometry din three.js: profilul (x = rază, y = înălțime) rotit
    în jurul axei Y, în sistemul Y-sus al aplicației (conversia la Z-sus se face la plasare, ca la celelalte forme)."""
    me = bpy.data.meshes.new(name)
    seg = max(3, int(seg))
    closed = abs(phil - 2 * math.pi) < 1e-3
    rings = seg if closed else seg + 1
    verts, faces = [], []
    for i in range(rings):
        a = phi0 + phil * i / seg
        for x, y in pts:
            verts.append((x * math.sin(a), y, x * math.cos(a)))
    n = len(pts)
    for i in range(seg):
        i2 = (i + 1) % rings
        for j in range(n - 1):
            faces.append((i * n + j, i2 * n + j, i2 * n + j + 1, i * n + j + 1))
    me.from_pydata(verts, [], faces)
    me.update()
    for p in me.polygons:
        p.use_smooth = True
    return me


def mesh_tris(name, v, idx):
    """Formă liberă din aplicație (plăci cu decupaje): triunghiuri din vârfuri Y-sus; vârfurile comune se unesc,
    ca fețele plane să nu aibă cusături, apoi muchiile vii rămân vii (fără netezire)."""
    import bmesh
    me = bpy.data.meshes.new(name)
    verts = [(v[k], v[k + 1], v[k + 2]) for k in range(0, len(v) - 2, 3)]
    ids = idx if idx else list(range(len(verts)))
    faces = [tuple(ids[k:k + 3]) for k in range(0, len(ids) - 2, 3)]
    me.from_pydata(verts, [], faces)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.to_mesh(me)
    bm.free()
    me.update()
    return me


def build_mesh(g, name):
    t, p = g['t'], g['p']
    if t == 'box':
        return mesh_box(name, *p), None
    if t == 'rbox':
        w, h, d, r = p
        return mesh_box(name, w, h, d), max(0.002, min(r, w / 2 - 0.001, h / 2 - 0.001, d / 2 - 0.001))
    if t == 'plane':
        return mesh_plane(name, *p), None
    if t == 'cyl':
        return mesh_cylinder(name, *p), None
    if t == 'torus':
        return mesh_torus(name, *p), None
    if t == 'sphere':
        return mesh_sphere(name, *p), None
    if t == 'lathe':
        return mesh_lathe(name, g.get('pts') or [], *p), None
    if t == 'mesh':
        me = mesh_tris(name, g.get('v') or [], g.get('i'))
        if g.get('sm'):  # textile drapate: suprafață netedă
            for poly in me.polygons:
                poly.use_smooth = True
        return me, None
    return None, None


# ---------------------------------------------------------------- scena
def import_decor(scene_data, models_dir):
    """Decorul din aplicație (plante, tablouri, vaze): fiecare model glTF importat o dată și instanțiat cu matricea din three.js."""
    refs = scene_data.get('decor') or []
    if not refs or not os.path.isdir(models_dir):
        return
    conv = Y_UP_TO_Z_UP
    loaded = {}
    for ref in refs:
        name = ref.get('model', '')
        if not name.replace('_', '').isalnum():
            continue
        if name not in loaded:
            path = os.path.join(models_dir, name, f'{name}.gltf')
            if not os.path.isfile(path):
                loaded[name] = None
                continue
            before = set(bpy.data.objects)
            bpy.ops.import_scene.gltf(filepath=path)
            objs = [o for o in bpy.data.objects if o not in before]
            coll = bpy.data.collections.new(f'decor_{name}')
            for o in objs:
                for c in o.users_collection:
                    c.objects.unlink(o)
                coll.objects.link(o)
            loaded[name] = coll
        coll = loaded[name]
        if coll is None:
            continue
        e = ref['m']
        m3 = Matrix(((e[0], e[4], e[8], e[12]), (e[1], e[5], e[9], e[13]), (e[2], e[6], e[10], e[14]), (e[3], e[7], e[11], e[15])))
        inst = bpy.data.objects.new(f'decor_{name}', None)
        inst.instance_type = 'COLLECTION'
        inst.instance_collection = coll
        # glTF-ul importat e deja în Z-sus; matricea din three.js lucrează în Y-sus: o conjugăm cu schimbarea de axe
        inst.matrix_world = conv @ m3 @ conv.inverted()
        bpy.context.scene.collection.objects.link(inst)
    print(f'[casa3d] decor: {len(refs)} obiecte')


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def build(scene_data, assets=''):
    mats = Materials(assets)
    col = bpy.data.collections.new('Casa3D')
    bpy.context.scene.collection.children.link(col)
    material_list = scene_data.get('materials', [])
    for i, e in enumerate(scene_data.get('meshes', [])):
        me, bevel = build_mesh(e['g'], f'c3d_{i}')
        if me is None:
            continue
        ids = e['m'] if isinstance(e['m'], list) else [e['m']]
        for mi in ids:
            me.materials.append(mats.get(material_list[mi]) if 0 <= mi < len(material_list) else None)
        if not isinstance(e['m'], list):
            for poly in me.polygons:
                poly.material_index = 0
        ob = bpy.data.objects.new(f'c3d_{i}', me)
        if isinstance(e['m'], list):  # cutiile de perete au câte un material pe față
            ob['c3d_wall'] = 1
        q = e['q']
        local = Matrix.Translation(Vector(e['p'])) @ Quaternion((q[3], q[0], q[1], q[2])).to_matrix().to_4x4() @ Matrix.Diagonal((*e['s'], 1.0))
        ob.matrix_world = Y_UP_TO_Z_UP @ local
        if bevel:  # perne, tapițerie: muchii rotunjite ca în aplicație, netede
            mod = ob.modifiers.new('rotunjire', 'BEVEL')
            mod.width = bevel
            mod.segments = 4
            mod.limit_method = 'NONE'
            mod.harden_normals = False
            for poly in me.polygons:
                poly.use_smooth = True
        col.objects.link(ob)
    # teren în jurul casei
    ground = bpy.data.objects.new('teren', mesh_plane('teren', 200, 200))
    ground.matrix_world = Matrix.Translation((0, 0, -0.02))
    gm = bpy.data.materials.new('teren')
    gm.use_nodes = True
    gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = hex_rgb('#7d8a63')
    gm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.95
    ground.data.materials.append(gm)
    col.objects.link(ground)
    return col


def light_scene(scene_data, assets='', portals=True):
    sc = bpy.context.scene
    world = bpy.data.worlds.new('cer')
    sc.world = world
    world.use_nodes = True
    nt = world.node_tree
    sky = nt.nodes.new('ShaderNodeTexSky')
    sky.sky_type = 'NISHITA'
    sky.sun_disc = False
    sun = scene_data.get('sun', {})
    d = to_blender(sun.get('dir', [0.45, 0.75, -0.48])).normalized()
    elevation = math.asin(max(-1.0, min(1.0, d.z)))
    sky.sun_elevation = max(elevation, -0.1)
    # unghiul în jurul verticalei, cu 0 spre +Y, în sensul acelor de ceasornic (ca la cerul Nishita)
    sky.sun_rotation = math.atan2(d.x, d.y)
    time = sun.get('time', 'day')
    bg = nt.nodes['Background']
    bg.inputs['Strength'].default_value = {'day': 0.35, 'evening': 0.25, 'night': 0.02}.get(time, 0.35)
    nt.links.new(sky.outputs['Color'], bg.inputs['Color'])
    # cer fotografiat (HDRI Poly Haven) când există: ziua senin, seara apus; noaptea rămâne cerul calculat, slab
    hdri = {'day': 'kloofendal_43d_clear_puresky.hdr', 'evening': 'belfast_sunset_puresky.hdr'}.get(time)
    if assets and hdri and os.path.isfile(os.path.join(assets, hdri)):
        env = nt.nodes.new('ShaderNodeTexEnvironment')
        env.image = bpy.data.images.load(os.path.join(assets, hdri), check_existing=True)
        nt.links.new(env.outputs['Color'], bg.inputs['Color'])
        bg.inputs['Strength'].default_value = 0.8 if time == 'day' else 0.6
    # soarele, pe aceeași direcție ca în aplicație
    if elevation > 0.02:
        sl = bpy.data.lights.new('soare', 'SUN')
        sl.energy = {'day': 4.0, 'evening': 2.2}.get(time, 0.0)
        sl.angle = math.radians(0.6)
        sl.color = (1.0, 0.95, 0.88) if time == 'day' else (1.0, 0.78, 0.55)
        so = bpy.data.objects.new('soare', sl)
        so.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
        sc.collection.objects.link(so)
    # portaluri de lumină în ferestrele exterioare: Cycles eșantionează cerul prin ele (lumină de zi curată, mai puțin zgomot)
    rooms = scene_data.get('rooms', [])
    room_at = lambda x, z: next((r for r in rooms if r['rect'][0] < x < r['rect'][2] and r['rect'][1] < z < r['rect'][3]), None)
    lit = set()
    for o in scene_data.get('openings') or []:
        if o.get('kind') != 'window':
            continue
        (px, pz), (nx, nz), th = o['p'], o['n'], float(o.get('th', 0.25))
        inside = 1 if room_at(px + nx * 0.4, pz + nz * 0.4) else -1   # normala spre interior
        r = room_at(px + nx * inside * 0.4, pz + nz * inside * 0.4)
        if r:
            lit.add(r['id'])
        if not portals or not o.get('ext', True) or time == 'night':
            continue
        pl = bpy.data.lights.new(f"portal_{len(lit)}_{px:.2f}_{pz:.2f}", 'AREA')
        pl.shape = 'RECTANGLE'
        pl.size, pl.size_y = float(o['w']), float(o['h'])
        try:
            pl.cycles.is_portal = True
        except AttributeError:  # Blender fără proprietatea portal: lumina rămâne o zonă slabă
            pl.energy = 0.0
        po = bpy.data.objects.new(pl.name, pl)
        po.location = to_blender((px - nx * inside * th / 2, float(o.get('sill', 0.9)) + float(o['h']) / 2, pz - nz * inside * th / 2))
        po.rotation_euler = to_blender((nx * inside, 0, nz * inside)).normalized().to_track_quat('-Z', 'Y').to_euler()
        sc.collection.objects.link(po)
    # lumini în camere: o plafonieră pe încăpere, aprinsă seara și noaptea; ziua stinsă în camerele cu fereastră
    # (lumina vine de afară), aprinsă doar în cele fără fereastră (baia, holul), cum ar face cineva care face poze
    per_m2 = {'day': 0.0, 'evening': 32.0, 'night': 45.0}.get(time, 0.0)
    H = float(scene_data.get('height', 2.6))
    for r in rooms:
        x0, z0, x1, z1 = r['rect']
        area = max(1.0, (x1 - x0) * (z1 - z0))
        energy = per_m2 if time != 'day' else (0.0 if r['id'] in lit else 12.0)
        if energy <= 0:
            continue
        al = bpy.data.lights.new(f"lumina_{r['id']}", 'AREA')
        al.shape = 'DISK'
        al.size = 0.45
        al.energy = energy * area
        al.color = (1.0, 0.92, 0.82)
        ao = bpy.data.objects.new(al.name, al)
        ao.location = to_blender(((x0 + x1) / 2, H - 0.05, (z0 + z1) / 2))
        sc.collection.objects.link(ao)


MISSING = {}  # cameră → piesele principale care nu au încăput întregi în cadru

# regula comună a cadrelor, aceeași ca în aplicație (viewer3d-engine.js, SHOT): ochiul la 1,5 m, aparatul orizontal
# (verticale drepte) cu obiectivul deplasat în jos, deschidere orizontală de cel mult ~75°, fără un prim-plan care să
# acopere peste 15 % din imagine la sub 1 m
EYE = 1.5
HFOV = math.radians(72)
HFOV_MAX = math.radians(75)
SHIFT_Y = -0.06
NEAR = 1.0       # pereții
NEAR_OBJ = 1.5   # piese, uși, perdele, ferestre: prim-plan care domină imaginea
MAX_COVER = 0.15


def frame_dirs(lx, lz, fov, width, height, nx=12, ny=8):
    """Direcțiile (în Blender) ale unei grile de raze prin tot cadrul camerei, cu deplasarea obiectivului."""
    fwd = to_blender((lx, 0, lz)).normalized()
    up = Vector((0, 0, 1))
    right = fwd.cross(up).normalized()
    th = math.tan(fov / 2)
    tv = th * height / width
    out = []
    for j in range(ny):
        for i in range(nx):
            u = (i + 0.5) / nx * 2 - 1
            v = (j + 0.5) / ny * 2 - 1
            out.append((fwd + right * (u * th) + up * (v * tv + SHIFT_Y * 2 * th)).normalized())
    return out


FG_DIST = 2.2   # o piesă (alta decât cea principală) mai aproape de atât, în jumătatea de jos a cadrului: prim-plan


def coverage(sc, deps, c, lx, lz, fov, width, height, items=(), skip=None):
    """(acoperire, prim-plan): fracțiunea din cadru ocupată de ceva aflat la sub NEAR metri de obiectiv (canat, perete,
    draperie, piesă înaltă) și fracțiunea jumătății de jos a cadrului ocupată de o piesă de mobilier la sub FG_DIST metri
    (masa sau măsuța din fața obiectivului), fără piesa principală `skip`."""
    eye = to_blender((c[0], EYE, c[1]))
    dirs = frame_dirs(lx, lz, fov, width, height)
    hit = fg = low = 0
    for k, d in enumerate(dirs):
        lower = k // 12 < 4   # frame_dirs: rândurile de jos întâi (12 raze pe rând)
        low += lower
        ok, loc, _n, _i, obj, _m = sc.ray_cast(deps, eye, d, distance=FG_DIST)
        if not ok:
            continue
        dist = (loc - eye).length
        if dist < (NEAR if obj is not None and obj.get('c3d_wall') else NEAR_OBJ):
            hit += 1
        if lower and not (obj is not None and obj.get('c3d_wall')):
            px, pz, py = loc.x, -loc.y, loc.z
            if any(it is not skip and it['fp'][0] - m < px < it['fp'][2] + m and it['fp'][1] - m < pz < it['fp'][3] + m and py < max(it['h'], 1.0) + .05
                   for it in items for m in ((0.6 if it.get('m') == 'dining' else 0.05),)):   # scaunele mesei ies din amprenta ei
                fg += 1
    return hit / len(dirs), (fg / low if low else 0.0)


def room_camera(scene_data, room, width, height, panorama=False, focus=None, avoid=None):
    """Camera foto a încăperii (regula comună: 1,5 m, orizontală, deschidere ≤ 75°, deplasare în jos). Locul se alege dintre
    colțuri și mijlocul pereților, departe de uși (golurile vin din aplicație, cu ușile închise) și de spaleții de lângă goluri;
    câștigă poziția cu vederea liberă și adâncă și cu cele mai multe piese principale în cadru, după un test de acoperire
    cu raze: nimic aflat la sub 1 m nu are voie să acopere peste 15 % din imagine."""
    x0, z0, x1, z1 = room['rect']
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    cam = bpy.data.cameras.new(f"cam_{room['id']}")
    ob = bpy.data.objects.new(cam.name, cam)
    bpy.context.scene.collection.objects.link(ob)
    if panorama:
        cam.type = 'PANO'
        cam.panorama_type = 'EQUIRECTANGULAR'
        ob.location = to_blender((cx, EYE, cz))
        ob.rotation_euler = (math.radians(90), 0, 0)
        return ob
    fov = HFOV
    # piesele din cameră (exportate din aplicație): obiectivul nu stă niciodată într-o piesă sau lipit de ea, iar piesele
    # principale (bucătăria, patul, canapeaua, WC-ul, lavoarul, dușul) trebuie să intre întregi în cadru
    HERO = ('pat', 'canapea', 'bucatarie')
    KEY = {'bucatarie': 3.0, 'pat': 3.0, 'canapea': 3.0, 'wc': 2.5, 'lavoar': 2.5, 'dus': 2.0, 'masa': 1.5, 'dulap': 1.5, 'frigider': 1.0,
           'birou': 1.2, 'comodaTv': 1.0, 'pantofar': 1.0, 'oglinda': 0.8, 'biblioteca': 0.6, 'masuta': 0.8, 'noptiera': 0.4}
    items = [it for it in scene_data.get('items', []) if x0 - .05 <= (it['fp'][0] + it['fp'][2]) / 2 <= x1 + .05 and z0 - .05 <= (it['fp'][1] + it['fp'][3]) / 2 <= z1 + .05]
    openings = [o for o in (scene_data.get('openings') or []) if x0 - .4 <= o['p'][0] <= x1 + .4 and z0 - .4 <= o['p'][1] <= z1 + .4]
    doors = [o for o in openings if o.get('kind') == 'door']
    windows = [o for o in openings if o.get('kind') == 'window']
    piers = [e for o in openings for e in (o['a'], o['b'])]   # spaleții: capetele golurilor (muchia peretelui lângă ușă/fereastră)
    cands = []
    for k in (0.35, 0.6):
        cands += [(x0 + k, z0 + k), (x1 - k, z0 + k), (x1 - k, z1 - k), (x0 + k, z1 - k), (cx, z0 + k), (x1 - k, cz), (cx, z1 - k), (x0 + k, cz)]
    for f in (0.25, 0.75):
        cands += [(x0 + (x1 - x0) * f, z0 + 0.35), (x0 + (x1 - x0) * f, z1 - 0.35), (x0 + 0.35, z0 + (z1 - z0) * f), (x1 - 0.35, z0 + (z1 - z0) * f)]
    for k in (0.35, 0.6):   # și de-a lungul pereților, din 0,5 în 0,5 m (fotograful se mută puțin ca să scoată o masă din cadru)
        x = x0 + 0.6
        while x < x1 - 0.55:
            cands += [(x, z0 + k), (x, z1 - k)]
            x += 0.5
        z = z0 + 0.6
        while z < z1 - 0.55:
            cands += [(x0 + k, z), (x1 - k, z)]
            z += 0.5
    cands = list(dict.fromkeys((round(c[0], 3), round(c[1], 3)) for c in cands if x0 + .2 < c[0] < x1 - .2 and z0 + .2 < c[1] < z1 - .2))

    def blocked(c, margin=0.25):  # obiectivul (la 1,5 m) în interiorul sau prea aproape de o piesă care urcă până acolo
        for it in items:
            a, b, e, f = it['fp']
            if it['h'] > 0.9 and a - margin < c[0] < e + margin and b - margin < c[1] < f + margin:
                return True
            mg = 0.45 if it['h'] >= 0.5 else 0.25   # nici lipit de un WC sau de o masă; în fața unei comode TV joase se poate sta
            if a - mg < c[0] < e + mg and b - mg < c[1] < f + mg:
                return True
        return False

    near_door = lambda c: any(math.hypot(c[0] - o['p'][0], c[1] - o['p'][1]) < max(0.9, float(o['w']) * 0.5 + 0.55) for o in doors)
    near_pier = lambda c: any(math.hypot(c[0] - p[0], c[1] - p[1]) < 0.45 for p in piers)

    def seg_dist(c, a, b):
        ax, az, bx, bz = a[0], a[1], b[0], b[1]
        L2 = (bx - ax) ** 2 + (bz - az) ** 2 or 1e-9
        t = max(0.0, min(1.0, ((c[0] - ax) * (bx - ax) + (c[1] - az) * (bz - az)) / L2))
        return math.hypot(c[0] - ax - t * (bx - ax), c[1] - az - t * (bz - az))
    near_window = lambda c: any(seg_dist(c, o['a'], o['b']) < 0.5 for o in windows)   # nu lipit de geam (perdelele le respinge testul de acoperire)
    free = [c for c in cands if not blocked(c) and not near_door(c) and not near_pier(c) and not near_window(c)]
    cands = free or [c for c in cands if not blocked(c) and not near_door(c)] or [c for c in cands if not blocked(c)] or [(cx, cz)]
    if avoid is not None:   # a doua vedere: alt loc decât prima (altfel ar ieși aceeași imagine)
        cands = [c for c in cands if math.hypot(c[0] - avoid[0], c[1] - avoid[1]) > 1.2] or cands
    pieces = [e['p'] for e in scene_data.get('meshes', []) if x0 < e['p'][0] < x1 and z0 < e['p'][2] < z1 and 0.15 < e['p'][1] < 1.8]
    deps = bpy.context.evaluated_depsgraph_get()
    sc = bpy.context.scene

    # ținte ale privirii: un punct dincolo de centrul camerei sau centrul de greutate al pieselor principale
    beyond = lambda c: (cx + (cx - c[0]) * 0.35, cz + (cz - c[1]) * 0.35)
    main = [(KEY[it['g']], (it['fp'][0] + it['fp'][2]) / 2, (it['fp'][1] + it['fp'][3]) / 2) for it in items if (id(it) in focus if focus else KEY.get(it['g'], 0) >= 1)]
    wsum = sum(m[0] for m in main)

    def look(c, aim=beyond):
        t = aim(c)
        return (t[0] - c[0], t[1] - c[1])

    def in_view(c, px, pz, lx, lz, half, low=0.45):
        n = math.hypot(lx, lz) or 1
        fwd = ((px - c[0]) * lx + (pz - c[1]) * lz) / n
        side = math.atan2((px - c[0]) * lz - (pz - c[1]) * lx, (px - c[0]) * lx + (pz - c[1]) * lz)
        return fwd > low and abs(side) < half

    def bottom(fov):  # tangenta marginii de jos a cadrului (cu deplasarea în jos a obiectivului), pe orizontală
        t = math.tan(fov / 2)
        return t * height / width - SHIFT_Y * 2 * t

    # ce trebuie să se vadă din piesă: la WC și bideu vasul (~42 cm), nu cadrul sau rezervorul de deasupra lui
    top = lambda it: min(it['h'], 0.42 if it['g'] in ('wc', 'bideu') else 1.5)

    near = lambda it: 0.6 if it['m'] == 'dining' else 0.1  # scaunele mesei ies din amprenta ei

    # regulile fotografiei de interior (aceleași ca în aplicație, roomShot): piesa principală fotografiată din fața ei
    # (patul dinspre picioare/ușă, pe diagonală spre peretele tăbliei), fereastra într-o parte, nu în mijloc în contra
    # luminii, iar pendulul (cablul lui) nu taie mijlocul cadrului sau fereastra
    hero = max((it for it in items if it['g'] in HERO), key=lambda it: KEY.get(it['g'], 0) * (it['fp'][2] - it['fp'][0]) * (it['fp'][3] - it['fp'][1]), default=None)
    pendants = [(e['p'][0], e['p'][2]) for e in scene_data.get('meshes', []) if e['g'].get('t') == 'cyl' and e['g']['p'][0] <= 0.006
                and e['g']['p'][2] > 0.1 and x0 < e['p'][0] < x1 and z0 < e['p'][2] < z1]

    if hero is not None and hero['g'] == 'pat':   # dormitorul: niciodată de lângă tăblie sau din lateralul patului
        _bx, _bz = (hero['fp'][0] + hero['fp'][2]) / 2, (hero['fp'][1] + hero['fp'][3]) / 2
        _foot = lambda c: ((c[0] - _bx) * math.sin(hero['r']) + (c[1] - _bz) * math.cos(hero['r'])) / (math.hypot(c[0] - _bx, c[1] - _bz) or 1)
        cands = [c for c in cands if _foot(c) >= 0.3] or cands
    aims = [beyond] + ([lambda c: (sum(m[0] * m[1] for m in main) / wsum, sum(m[0] * m[2] for m in main) / wsum)] if main else [])
    grp = []
    if hero is not None and focus is None:   # privirea spre piesa principală, cu ținta trasă spre peretele din spatele ei (tăblia patului)
        _hd = 0.25 * min(hero['fp'][2] - hero['fp'][0], hero['fp'][3] - hero['fp'][1]) if hero['g'] != 'pat' else 0.45 * (hero['fp'][3] - hero['fp'][1] if abs(math.cos(hero['r'])) > 0.5 else hero['fp'][2] - hero['fp'][0])
        _ha = ((hero['fp'][0] + hero['fp'][2]) / 2 - math.sin(hero['r']) * _hd, (hero['fp'][1] + hero['fp'][3]) / 2 - math.cos(hero['r']) * _hd)
        aims.append(lambda c: _ha)
        # grupul piesei principale: ea și piesele din fața ei (măsuța din fața canapelei, banca de la picioarele patului), ca
        # amândouă să intre întregi în cadru, nu măsuța tăiată în colțul de jos
        _fx, _fz = math.sin(hero['r']), math.cos(hero['r'])
        _hx, _hz = (hero['fp'][0] + hero['fp'][2]) / 2, (hero['fp'][1] + hero['fp'][3]) / 2
        grp[:] = [it for it in items if it is not hero and 0.3 < ((it['fp'][0] + it['fp'][2]) / 2 - _hx) * _fx + ((it['fp'][1] + it['fp'][3]) / 2 - _hz) * _fz < 2.5
                and abs(((it['fp'][0] + it['fp'][2]) / 2 - _hx) * _fz - ((it['fp'][1] + it['fp'][3]) / 2 - _hz) * _fx) < 1.2 and it['h'] < 0.8]
        if grp:
            _gx = (_hx + sum((it['fp'][0] + it['fp'][2]) / 2 for it in grp)) / (len(grp) + 1)
            _gz = (_hz + sum((it['fp'][1] + it['fp'][3]) / 2 for it in grp)) / (len(grp) + 1)
            aims.append(lambda c: (_gx, _gz))

    def inward(o):  # normala ferestrei spre interiorul camerei
        nx, nz = o['n']
        px, pz = o['p']
        return (nx, nz) if x0 < px + nx * 0.4 < x1 and z0 < pz + nz * 0.4 < z1 else (-nx, -nz)

    def screen_u(c, lx, lz, px, pz, fov):  # (adâncimea, poziția orizontală în cadru -1..1) a unui punct
        n = math.hypot(lx, lz) or 1
        fx, fz = lx / n, lz / n
        dx, dz = px - c[0], pz - c[1]
        fwd = dx * fx + dz * fz
        if fwd <= 0.05:
            return fwd, 9.0
        return fwd, (dx * fz - dz * fx) / fwd / math.tan(fov / 2)

    def composition(c, lx, lz, fov, with_hero=True):
        n = math.hypot(lx, lz) or 1
        s = 0.0
        if hero is not None and with_hero:   # a doua vedere: piesele rămase, nu iar piesa principală
            hx, hz = (hero['fp'][0] + hero['fp'][2]) / 2, (hero['fp'][1] + hero['fp'][3]) / 2
            vx, vz = c[0] - hx, c[1] - hz
            vn = math.hypot(vx, vz) or 1
            cos_f = (vx * math.sin(hero['r']) + vz * math.cos(hero['r'])) / vn
            fwd_h, u_h = screen_u(c, lx, lz, hx, hz, fov)
            s += 5.0 * max(0.0, 1 - abs(u_h)) - (15.0 if fwd_h <= 0.3 or abs(u_h) > 0.85 else 0.0)   # piesa principală în cadru, spre mijloc
            a, b, e, f = hero['fp']   # și întreagă: niciun colț al ei tăiat de marginile cadrului
            if any(q[0] <= 0.3 or abs(q[1]) > 0.94 for q in (screen_u(c, lx, lz, px, pz, fov) for px, pz in ((a, b), (e, b), (e, f), (a, f)))):
                s -= 25.0   # cât valorează piesa principală întreagă: o vedere largă cu ea tăiată nu bate una cu ea întreagă
            for it in grp:   # și grupul ei (măsuța din fața canapelei): întreg sau deloc tăiat de marginea cadrului
                a, b, e, f = it['fp']
                s += -3.0 if any(q[0] <= 0.3 or abs(q[1]) > 0.94 for q in (screen_u(c, lx, lz, px, pz, fov) for px, pz in ((a, b), (e, b), (e, f), (a, f)))) else 3.0
            if hero['g'] == 'pat':   # dinspre picioare, pe diagonală (18°–65° față de axa patului); de la cap sau din lateral: nu
                s += 14.0 * cos_f + (6.0 if 0.42 < cos_f < 0.95 else 0.0) - 40.0 * max(0.0, 0.35 - cos_f)
            else:
                s += 7.0 * cos_f - 30.0 * max(0.0, 0.25 - cos_f)   # canapeaua, bucătăria: din față, nu din lateral
        wins_u = []
        for o in windows:
            fwd, u = screen_u(c, lx, lz, o['p'][0], o['p'][1], fov)
            if fwd <= 0.3 or abs(u) > 1.2:
                continue
            ix, iz = inward(o)
            facing = -(lx * ix + lz * iz) / n      # 1 = privirea drept în fereastră (contra luminii)
            # a doua vedere (spre piesele rămase) nu are voie să privească în ferestre: acolo pedeapsa e dublă
            s -= (16.0 if with_hero else 20.0) * max(0.0, facing - 0.35) / 0.65 * max(0.0, 1 - abs(u) / 0.75)
            hw = float(o['w']) / 2 / fwd / math.tan(fov / 2)
            wins_u.append((u - hw, u + hw))
        for px, pz in pendants:
            fwd, u = screen_u(c, lx, lz, px, pz, fov)
            if fwd <= 0.3 or abs(u) > 1.0:
                continue
            s -= 8.0 * max(0.0, 1 - abs(u) / 0.45)                                # cablul în treimea din mijloc
            if any(a - 0.03 < u < b + 0.03 for a, b in wins_u):
                s -= 5.0                                                            # cablul peste fereastră
        return s

    def score(c, fov=fov, aim=beyond):
        lx, lz = look(c, aim)
        if math.hypot(lx, lz) < 0.3:
            return -1e9
        base = math.atan2(lz, lx)
        eye = to_blender((c[0], EYE, c[1]))
        total = 0.0
        for i in range(11):
            a = base - fov / 2 + fov * i / 10
            d = to_blender((math.cos(a), 0, math.sin(a))).normalized()
            hit, loc, *_ = sc.ray_cast(deps, eye, d, distance=30.0)
            dist = (loc - eye).length if hit else 30.0
            total += min(dist, 7.0) - (6.0 if dist < 0.9 else 0.0)
        for i in range(14):  # raze în jos (20° și 32°): o masă sau un scaun chiar în fața obiectivului acoperă jumătate din cadru
            a, (ch, sh) = base - fov / 2 + fov * (i % 7) / 6, ((0.94, 0.342), (0.848, 0.53))[i // 7]
            d = to_blender((math.cos(a) * ch, -sh, math.sin(a) * ch)).normalized()
            hit, loc, *_ = sc.ray_cast(deps, eye, d, distance=30.0)
            if hit and math.hypot(loc.x - eye.x, loc.y - eye.y) < 1.6 and any(it['fp'][0] - near(it) < loc.x < it['fp'][2] + near(it) and it['fp'][1] - near(it) < -loc.y < it['fp'][3] + near(it) for it in items):
                total -= 3.0
        # o ușă (gol) în prim-plan: pierde
        for o in doors:
            if in_view(c, o['p'][0], o['p'][1], lx, lz, fov / 2, 0.2) and math.hypot(o['p'][0] - c[0], o['p'][1] - c[1]) < 2.0:
                total -= 4.0
        seen = sum(1 for p in pieces if in_view(c, p[0], p[2], lx, lz, fov / 2))
        keys = 0.0  # piesa principală întreagă în cadru (toate cele patru colțuri ale amprentei), cu o margine de 4°
        for it in items:
            a, b, e, f = it['fp']
            corners = [(a, b), (e, b), (e, f), (a, f)]
            w = KEY.get(it['g'], 0.5) if focus is None else (6.0 if id(it) in focus else 0.2)  # a doua vedere: piesele rămase pe dinafară
            low = max(0.45, (EYE - top(it)) / bottom(fov))  # partea de sus a piesei nu e tăiată de marginea de jos
            if all(in_view(c, px, pz, lx, lz, fov / 2 - math.radians(4), low) for px, pz in corners):
                keys += w
            elif any(in_view(c, px, pz, lx, lz, fov / 2) for px, pz in corners):
                keys += w * 0.3
        # câte părți de mobilier se văd, plafonat: o bibliotecă plină (zeci de cărți) nu are voie să tragă cadrul spre ea
        return total + 0.6 * min(seen, 60) + 8.0 * keys - 6.0 * (fov - HFOV) + composition(c, lx, lz, fov, focus is None)

    # o încăpere mică (baia, holul): se încearcă și 75° (limita regulii); nu mai folosim 100°, care deforma marginile
    fovs = [fov] + ([HFOV_MAX] if min(x1 - x0, z1 - z0) < 2.6 or (x1 - x0) * (z1 - z0) < 9 else [])
    ranked = sorted(((score(c, f, a), c, f, a) for c in cands for f in fovs for a in aims), key=lambda t: -t[0])
    # testul de acoperire pe cei mai buni candidați: prima poziție sub prag (scorul scade cu acoperirea); altfel cea mai liberă
    best, probed = None, []
    for v, c, f, a in ranked[:24]:
        lx, lz = look(c, a)
        cov, fg = coverage(sc, deps, c, lx, lz, f, width, height, items, hero if focus is None else None)
        fin = v - 25.0 * cov - (30.0 if focus is None else 120.0) * fg   # prim-planul (o masă, o măsuță la sub 2,2 m) costă: piesa principală se vede peste ea
        if focus is not None and not any(in_view(c, (it['fp'][0] + it['fp'][2]) / 2, (it['fp'][1] + it['fp'][3]) / 2, lx, lz, f / 2, 0.3) for it in items if id(it) in focus):
            fin -= 40.0   # a doua vedere există pentru piesele rămase: măcar mijlocul lor trebuie să fie în cadru
        cand = (cov <= MAX_COVER, fin if cov <= MAX_COVER else -cov, v, c, f, a, cov)
        probed.append((round(fin, 1), round(cov, 2), round(fg, 2), c, aims.index(a)))
        if best is None or cand[:2] > best[:2]:
            best = cand
    _, _, v, c, fov, aim, cov = best
    if os.environ.get('CASA3D_CAMDBG'):
        print('[casa3d] cadre testate (final, acoperire, prim-plan, loc, țintă):', sorted(probed, key=lambda q: -q[0])[:8])
    print(f"[casa3d] cameră {room['id']}: ({c[0]:.2f},{c[1]:.2f}) {math.degrees(fov):.0f}° acoperire {cov * 100:.0f}% ; "
          + '; '.join(f'({p[0]:.2f},{p[1]:.2f}) {math.degrees(f):.0f}° {aims.index(a)} = {s:.1f} [{composition(p, *look(p, a), f, focus is None):+.1f}]' for s, p, f, a in ranked[:int(os.environ.get('CASA3D_CAMDBG', 4))]))
    lx, lz = look(c, aim)
    low_of = lambda it: max(0.45, (EYE - top(it)) / bottom(fov))
    MISSING[ob.name] = [it for it in items if KEY.get(it['g'], 0) >= 1.5 and not all(in_view(c, px, pz, lx, lz, fov / 2 - math.radians(4), low_of(it)) for px, pz in ((it['fp'][0], it['fp'][1]), (it['fp'][2], it['fp'][1]), (it['fp'][2], it['fp'][3]), (it['fp'][0], it['fp'][3])))]
    ob.location = to_blender((c[0], EYE, c[1]))
    ob['c3d_xz'] = (c[0], c[1])
    ob.rotation_euler = to_blender((lx, 0, lz)).normalized().to_track_quat('-Z', 'Y').to_euler()
    cam.lens_unit = 'FOV'
    cam.sensor_fit = 'HORIZONTAL'
    cam.angle = fov
    cam.shift_y = SHIFT_Y  # privire ușor în jos fără să înclinăm aparatul: verticalele rămân verticale
    cam.clip_start = 0.05
    return ob


# expunerea pe cameră, calibrată pe fotografia imobiliară de interior: pereții luminoși, tonurile medii ridicate, fereastra
# fără alb ars (AgX o rotunjește moale). Ținta e mediana luminanței scenei (robustă la ferestre și la zgomot), nu media;
# plafonul vine de la cele mai luminoase 1 % (fereastra, peretele lovit de soare), ca AgX să nu ajungă în alb tăiat.
EXPOSURE_KEY = 0.30        # mediana scenei după expunere (liniar); 0,18 dădea interioare cu 2–3 EV prea întunecate
EXPOSURE_HI = 24.0         # luminanța maximă admisă la percentila 99 (AgX ajunge la alb pe la ~16 și rotunjește peste)
EXPOSURE_GAMMA = 1.1       # ridicarea tonurilor medii după AgX (albul și negrul rămân pe loc)


def exposure_from_luminance(lum, key=EXPOSURE_KEY, hi=EXPOSURE_HI):
    """EV-ul pentru o listă de luminanțe liniare (fără date de culoare)."""
    lum = sorted(lum)
    if not lum:
        return 0.0
    med = lum[len(lum) // 2]
    p99 = lum[min(len(lum) - 1, int(len(lum) * 0.99))]
    ev = math.log2(key / max(1e-4, med))
    ev = min(ev, math.log2(hi / max(1e-4, p99)))
    return max(-2.0, min(5.5, ev))


def auto_exposure(sc, width, height, key=EXPOSURE_KEY):
    """Expunerea pe cameră: o trecere rapidă (192 px, 16 eșantioane, cu denoiser) scrisă în EXR liniar, apoi
    exposure_from_luminance (mediana adusă la `key`, cu plafon pentru ferestre)."""
    import tempfile
    r = sc.render
    keep = (r.resolution_x, r.resolution_y, r.resolution_percentage, sc.cycles.samples, sc.cycles.use_denoising, r.filepath, r.image_settings.file_format, r.image_settings.color_depth)
    path = os.path.join(tempfile.mkdtemp(prefix='c3d_ae_'), 'ae.exr')
    try:
        r.resolution_x, r.resolution_y, r.resolution_percentage = 192, max(1, round(192 * height / width)), 100
        sc.cycles.samples, sc.cycles.use_denoising = 16, True
        r.image_settings.file_format, r.image_settings.color_depth = 'OPEN_EXR', '16'
        r.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path)
        px = img.pixels[:]
        bpy.data.images.remove(img)
        lum = [0.2126 * px[i] + 0.7152 * px[i + 1] + 0.0722 * px[i + 2] for i in range(0, len(px), 4)]
        ev = exposure_from_luminance(lum, key)
        srt = sorted(lum)
        print(f'[casa3d] luminanța mediană {srt[len(srt) // 2]:.4f}, p99 {srt[int(len(srt) * 0.99)]:.3f} -> {ev:+.2f} EV')
    except Exception as e:  # fără trecerea rapidă: expunerea fixă
        print(f'[casa3d] expunere automată indisponibilă: {e}')
        ev = 3.5
    finally:
        (r.resolution_x, r.resolution_y, r.resolution_percentage, sc.cycles.samples, sc.cycles.use_denoising, r.filepath, r.image_settings.file_format, r.image_settings.color_depth) = keep
        try:
            os.remove(path)
            os.rmdir(os.path.dirname(path))
        except OSError:
            pass
    return ev


def setup_render(args):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    cy = sc.cycles
    cy.samples = args.samples
    cy.use_adaptive_sampling = True
    cy.use_denoising = True
    cy.max_bounces = 10
    cy.diffuse_bounces = 5
    cy.glossy_bounces = 4
    cy.transmission_bounces = 8
    cy.sample_clamp_indirect = 8.0
    cy.caustics_reflective = False
    cy.caustics_refractive = False
    sc.render.resolution_x, sc.render.resolution_y = args.width, args.height
    sc.render.image_settings.file_format = 'PNG'
    sc.view_settings.view_transform = 'AgX'
    try:  # contrast de bază: AgX Medium High Contrast satura lemnul spre portocaliu
        sc.view_settings.look = 'AgX - Base Contrast'
    except TypeError:
        sc.view_settings.look = 'None'
    sc.view_settings.exposure = 3.5    # implicit; fiecare cameră primește expunerea ei (auto_exposure)
    sc.view_settings.gamma = EXPOSURE_GAMMA
    sc.render.use_persistent_data = True   # BVH-ul rămâne între randări (trecerea de expunere + imaginea)
    device = 'CPU'
    if args.device.upper() != 'CPU':
        prefs = bpy.context.preferences.addons['cycles'].preferences
        order = [args.device.upper()] if args.device.upper() != 'AUTO' else ['OPTIX', 'CUDA', 'HIP', 'ONEAPI', 'METAL']
        for kind in order:
            try:
                prefs.compute_device_type = kind
                prefs.get_devices()
                gpus = [d for d in prefs.devices if d.type == kind]
                if gpus:
                    for d in prefs.devices:
                        d.use = d.type == kind
                    device = kind
                    break
            except TypeError:
                continue
    cy.device = 'GPU' if device != 'CPU' else 'CPU'
    if device == 'OPTIX':
        cy.denoiser = 'OPTIX'
    print(f'[casa3d] dispozitiv: {device}, {args.samples} eșantioane, {args.width}x{args.height}')
    return device


def main():
    args = parse_args(sys.argv)
    with open(args.scene, encoding='utf-8') as f:
        data = json.load(f)
    if data.get('format') != 'casa3d-scene':
        sys.exit('[casa3d] fișierul nu e o scenă Casa3D (format lipsă)')
    os.makedirs(args.out, exist_ok=True)
    clear_scene()
    build(data, args.assets)
    import_decor(data, args.models)
    light_scene(data, args.assets, portals=not args.no_portals)
    setup_render(args)
    sc = bpy.context.scene
    if args.gamma is not None:
        sc.view_settings.gamma = args.gamma
    rooms = data.get('rooms', [])
    if args.rooms != 'all':
        wanted = set(args.rooms.split(','))
        rooms = [r for r in rooms if r['id'] in wanted]
    for kind in filter(None, args.export.split(',')):
        path = os.path.join(args.out, f'casa.{kind}')
        if kind == 'fbx':
            bpy.ops.export_scene.fbx(filepath=path, use_selection=False, apply_scale_options='FBX_SCALE_UNITS')
        elif kind in ('glb', 'gltf'):
            bpy.ops.export_scene.gltf(filepath=path, export_format='GLB' if kind == 'glb' else 'GLTF_SEPARATE')
        elif kind == 'obj':
            bpy.ops.wm.obj_export(filepath=path)
        print(f'[casa3d] export: {path}')
    if args.blend:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.abspath(args.out), 'scene.blend'))
    if args.no_render:
        return
    for r in rooms:
        safe = ''.join(ch if ch.isalnum() or ch in '-_' else '_' for ch in r['id'])
        sc.camera = room_camera(data, r, args.width, args.height)
        sc.render.resolution_x, sc.render.resolution_y = args.width, args.height
        sc.view_settings.exposure = auto_exposure(sc, args.width, args.height) if args.exposure == 'auto' else float(args.exposure)
        print(f"[casa3d] {r['id']}: expunere {sc.view_settings.exposure:+.2f} EV")
        sc.render.filepath = os.path.join(os.path.abspath(args.out), f'{safe}.png')
        bpy.ops.render.render(write_still=True)
        print(f"[casa3d] {r.get('name', r['id'])}: {sc.render.filepath}")
        missing = MISSING.get(sc.camera.name) or []
        if missing:  # o piesă principală (WC, lavoar, duș, pat...) nu încape în prima poză: încă o vedere, spre ea
            sc.camera = room_camera(data, r, args.width, args.height, focus={id(it) for it in missing}, avoid=tuple(sc.camera['c3d_xz']))
            sc.render.filepath = os.path.join(os.path.abspath(args.out), f'{safe}-2.png')
            bpy.ops.render.render(write_still=True)
            print(f"[casa3d] {r.get('name', r['id'])} (a doua vedere): {sc.render.filepath}")
        if args.panorama:
            sc.camera = room_camera(data, r, args.width, args.height, panorama=True)
            sc.render.resolution_x, sc.render.resolution_y = args.width, args.width // 2   # echirectangular 2:1
            sc.render.filepath = os.path.join(os.path.abspath(args.out), f'{safe}-360.png')
            bpy.ops.render.render(write_still=True)
            print(f"[casa3d] {r.get('name', r['id'])} 360°: {sc.render.filepath}")


if __name__ == '__main__':
    main()
