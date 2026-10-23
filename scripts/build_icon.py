"""Draws package/thunderstore/icon.png (256x256, the size Thunderstore asks for)."""
from pathlib import Path

from PIL import Image, ImageDraw

S = 256
img = Image.new("RGBA", (S, S), (38, 44, 52, 255))
d = ImageDraw.Draw(img)

# map grid
for i in range(0, S, 32):
    d.line([(i, 0), (i, S)], fill=(52, 60, 70, 255), width=2)
    d.line([(0, i), (S, i)], fill=(52, 60, 70, 255), width=2)

# pin: circle head plus a point at the bottom
cx, cy, r = 128, 104, 64
d.polygon([(cx - 46, cy + 44), (cx + 46, cy + 44), (cx, 228)], fill=(214, 140, 52, 255))
d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(214, 140, 52, 255))
# ore chunk inside the head
d.polygon([(cx - 30, cy + 8), (cx - 12, cy - 28), (cx + 22, cy - 22), (cx + 32, cy + 12), (cx + 4, cy + 30)],
          fill=(150, 92, 60, 255), outline=(60, 40, 30, 255), width=4)
d.line([(cx - 12, cy - 28), (cx + 4, cy + 30)], fill=(60, 40, 30, 255), width=3)

out = Path(__file__).resolve().parent.parent / "package" / "thunderstore" / "icon.png"
img.save(out)
print(out)
