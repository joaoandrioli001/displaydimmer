#!/usr/bin/env python3
"""Gera o icone do Display Dimmer (Assets/app.ico) sem dependencias externas.

Desenha um quadrado arredondado com gradiente azul, um monitor branco e um
pequeno brilho ciano. Usa supersampling 3x3 para suavizar as bordas.
"""
import math
import os
import struct

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "DisplayDimmer", "Assets", "app.ico")
SIZES = [16, 24, 32, 48, 64, 128, 256]

BLUE_TOP = (0x5A, 0x96, 0xFA)
BLUE_BOTTOM = (0x27, 0x5F, 0xD4)
WHITE = (0xFF, 0xFF, 0xFF)
CYAN = (0x7D, 0xD3, 0xFC)


def rounded_rect_sd(x, y, cx, cy, hw, hh, r):
    qx = abs(x - cx) - (hw - r)
    qy = abs(y - cy) - (hh - r)
    ax = max(qx, 0.0)
    ay = max(qy, 0.0)
    return math.hypot(ax, ay) + min(max(qx, qy), 0.0) - r


def segment_sd(px, py, ax, ay, bx, by, thickness):
    vx, vy = bx - ax, by - ay
    wx, wy = px - ax, py - ay
    length2 = vx * vx + vy * vy
    t = 0.0 if length2 == 0 else max(0.0, min(1.0, (wx * vx + wy * vy) / length2))
    dx, dy = wx - vx * t, wy - vy * t
    return math.hypot(dx, dy) - thickness


def star_alpha(x, y, cx, cy, size):
    dx, dy = abs(x - cx), abs(y - cy)
    # estrela de quatro pontas: losango fino + halo suave
    d = dx + dy * 2.4
    d2 = dy + dx * 2.4
    d = min(d, d2)
    return max(0.0, min(1.0, (size - d) / (size * 0.35)))


def sample(x, y):
    """Retorna (r, g, b, a) em 0..255 para a coordenada normalizada 0..1."""
    # fundo arredondado
    sd = rounded_rect_sd(x, y, 0.5, 0.5, 0.5, 0.5, 0.235)
    if sd > 0:
        return (0, 0, 0, 0)

    t = max(0.0, min(1.0, (x * 0.35 + y * 0.65)))
    r = BLUE_TOP[0] + (BLUE_BOTTOM[0] - BLUE_TOP[0]) * t
    g = BLUE_TOP[1] + (BLUE_BOTTOM[1] - BLUE_TOP[1]) * t
    b = BLUE_TOP[2] + (BLUE_BOTTOM[2] - BLUE_TOP[2]) * t
    color = [r, g, b]

    # moldura do monitor
    screen = rounded_rect_sd(x, y, 0.5, 0.435, 0.285, 0.205, 0.055)
    stroke = abs(screen) - 0.038
    if stroke < 0:
        color = [c + (WHITE[i] - c) * min(1.0, -stroke / 0.012) for i, c in enumerate(color)]

    # base do monitor
    stand = segment_sd(x, y, 0.5, 0.645, 0.5, 0.735, 0.028)
    if stand < 0:
        color = [c + (WHITE[i] - c) * min(1.0, -stand / 0.012) for i, c in enumerate(color)]
    base = segment_sd(x, y, 0.5, 0.765, 0.5, 0.765, 0.0)
    base_box = rounded_rect_sd(x, y, 0.5, 0.762, 0.145, 0.026, 0.026)
    if base < 0 or base_box < 0:
        color = [c + (WHITE[i] - c) * min(1.0, max(-base, -base_box) / 0.012) for i, c in enumerate(color)]

    # brilho ciano no canto superior direito da tela
    a = star_alpha(x, y, 0.735, 0.315, 0.115)
    if a > 0:
        color = [c + (CYAN[i] - c) * a for i, c in enumerate(color)]

    return (int(color[0] + 0.5), int(color[1] + 0.5), int(color[2] + 0.5), 255)


def render(size):
    ss = 3
    pixels = []
    for py in range(size):
        row = []
        for px in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(ss):
                for sx in range(ss):
                    x = (px + (sx + 0.5) / ss) / size
                    y = (py + (sy + 0.5) / ss) / size
                    r, g, b, a = sample(x, y)
                    acc[0] += r * a
                    acc[1] += g * a
                    acc[2] += b * a
                    acc[3] += a
            n = ss * ss
            alpha = acc[3] / n
            if alpha <= 0.5:
                row.append((0, 0, 0, 0))
            else:
                row.append((
                    int(acc[0] / acc[3] + 0.5),
                    int(acc[1] / acc[3] + 0.5),
                    int(acc[2] / acc[3] + 0.5),
                    int(alpha + 0.5),
                ))
        pixels.append(row)
    return pixels


def encode_entry(pixels, size):
    # BITMAPINFOHEADER + BGRA bottom-up + mascara AND zerada
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    body = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            r, g, b, a = pixels[y][x]
            body += bytes((b, g, r, a))
    mask_row = ((size + 31) // 32) * 4
    mask = bytes(mask_row * size)
    return header + bytes(body) + mask


def main():
    entries = []
    for size in SIZES:
        entries.append((size, encode_entry(render(size), size)))

    count = len(entries)
    offset = 6 + 16 * count
    out = bytearray(struct.pack("<HHH", 0, 1, count))
    for size, data in entries:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in entries:
        out += data

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as fh:
        fh.write(out)
    print("gerado", os.path.abspath(OUT), len(out), "bytes")


if __name__ == "__main__":
    main()
