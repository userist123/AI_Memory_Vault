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
    p.add_argument('--no-render', action='store_true')
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
            'velvet': ('velour_velvet', 0.5, 0.6), 'leather': ('leather_white', 0.6, 0.8), 'wall': ('plastered_wall', 2.5, 0.12)}


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
        if key in ('parquet', 'tile') and diff is not None:
            mix = nt.nodes.new('ShaderNodeMix')
            mix.data_type = 'RGBA'
            mix.blend_type = 'MULTIPLY'
            mix.inputs['Factor'].default_value = 1.0
            nt.links.new(tex(diff).outputs['Color'], mix.inputs['A'])
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
                r, _w = wood_grain(base, 14.0)
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
                n = noise(3.0, 12.0)
                r = ramp(n.outputs['Fac'], scaled(col, 0.72), col)
                r.color_ramp.elements[0].position = 0.47
                r.color_ramp.elements[1].position = 0.5
                nt.links.new(r.outputs['Color'], inp['Base Color'])

    def get(self, d):
        key = json.dumps(d, sort_keys=True)
        if key not in self.cache:
            self.cache[key] = self.build(d)
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
            w.inputs['Distortion'].default_value = 7.0
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
            r, w = wood_grain(base, 14.0)
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
    return None, None


# ---------------------------------------------------------------- scena
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


def light_scene(scene_data, assets=''):
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
    # lumini în camere: o plafonieră pe încăpere, mai puternică seara și noaptea
    per_m2 = {'day': 12.0, 'evening': 32.0, 'night': 45.0}.get(time, 12.0)
    H = float(scene_data.get('height', 2.6))
    for r in scene_data.get('rooms', []):
        x0, z0, x1, z1 = r['rect']
        area = max(1.0, (x1 - x0) * (z1 - z0))
        al = bpy.data.lights.new(f"lumina_{r['id']}", 'AREA')
        al.shape = 'DISK'
        al.size = 0.45
        al.energy = per_m2 * area
        al.color = (1.0, 0.92, 0.82)
        ao = bpy.data.objects.new(al.name, al)
        ao.location = to_blender(((x0 + x1) / 2, H - 0.05, (z0 + z1) / 2))
        sc.collection.objects.link(ao)


def room_camera(scene_data, room, width, height, panorama=False):
    """Camera foto a încăperii, la 1,55 m, cu verticalele drepte. Locul se alege dintre colțuri și mijlocul pereților:
    raze trase prin scenă pe toată deschiderea obiectivului; câștigă poziția cu vederea cea mai liberă și adâncă
    (fără un perete lipit de obiectiv) și cu cele mai multe piese în cadru."""
    x0, z0, x1, z1 = room['rect']
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    cam = bpy.data.cameras.new(f"cam_{room['id']}")
    ob = bpy.data.objects.new(cam.name, cam)
    bpy.context.scene.collection.objects.link(ob)
    if panorama:
        cam.type = 'PANO'
        cam.panorama_type = 'EQUIRECTANGULAR'
        ob.location = to_blender((cx, 1.6, cz))
        ob.rotation_euler = (math.radians(90), 0, 0)
        return ob
    fov = math.radians(82)
    k = 0.35
    cands = [(x0 + k, z0 + k), (x1 - k, z0 + k), (x1 - k, z1 - k), (x0 + k, z1 - k),
             (cx, z0 + k), (x1 - k, cz), (cx, z1 - k), (x0 + k, cz)]
    pieces = [e['p'] for e in scene_data.get('meshes', []) if x0 < e['p'][0] < x1 and z0 < e['p'][2] < z1 and 0.15 < e['p'][1] < 1.8]
    deps = bpy.context.evaluated_depsgraph_get()
    sc = bpy.context.scene

    def look(c):  # privirea spre un punct dincolo de centrul camerei
        return (cx + (cx - c[0]) * 0.35 - c[0], cz + (cz - c[1]) * 0.35 - c[1])

    def score(c):
        lx, lz = look(c)
        base = math.atan2(lz, lx)
        eye = to_blender((c[0], 1.55, c[1]))
        total = 0.0
        for i in range(11):
            a = base - fov / 2 + fov * i / 10
            d = to_blender((math.cos(a), 0, math.sin(a))).normalized()
            hit, loc, *_ = sc.ray_cast(deps, eye, d, distance=30.0)
            dist = (loc - eye).length if hit else 30.0
            total += min(dist, 7.0) - (6.0 if dist < 0.9 else 0.0)
        n = math.hypot(lx, lz) or 1
        seen = sum(1 for p in pieces if ((p[0] - c[0]) * lx + (p[2] - c[1]) * lz) / n > 0.6
                   and abs(math.atan2((p[0] - c[0]) * lz - (p[2] - c[1]) * lx, (p[0] - c[0]) * lx + (p[2] - c[1]) * lz)) < fov / 2)
        return total + 0.6 * seen

    c = max(cands, key=score)
    lx, lz = look(c)
    ob.location = to_blender((c[0], 1.55, c[1]))
    ob.rotation_euler = to_blender((lx, 0, lz)).normalized().to_track_quat('-Z', 'Y').to_euler()
    cam.lens_unit = 'FOV'
    cam.angle = fov
    cam.shift_y = -0.08  # privire ușor în jos fără să înclinăm aparatul: verticalele rămân verticale
    cam.clip_start = 0.05
    return ob


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
    sc.view_settings.look = 'AgX - Medium High Contrast'
    sc.view_settings.exposure = -0.4
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
    light_scene(data, args.assets)
    setup_render(args)
    sc = bpy.context.scene
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
        sc.render.filepath = os.path.join(os.path.abspath(args.out), f'{safe}.png')
        bpy.ops.render.render(write_still=True)
        print(f"[casa3d] {r.get('name', r['id'])}: {sc.render.filepath}")
        if args.panorama:
            sc.camera = room_camera(data, r, args.width, args.height, panorama=True)
            sc.render.resolution_x, sc.render.resolution_y = args.width, args.width // 2   # echirectangular 2:1
            sc.render.filepath = os.path.join(os.path.abspath(args.out), f'{safe}-360.png')
            bpy.ops.render.render(write_still=True)
            print(f"[casa3d] {r.get('name', r['id'])} 360°: {sc.render.filepath}")


if __name__ == '__main__':
    main()
