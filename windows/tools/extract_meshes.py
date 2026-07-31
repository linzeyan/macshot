"""Extract the 18 mesh gradient definitions from macshot's BeautifyRenderer.swift.

Emits the C# body of BeautifyMeshes.Catalogue so the numbers are transcribed by a
machine rather than by hand: a wrong digit in 162 control points and 162 colours would
still render something plausible and no test would catch it.
"""
import re
import sys

SRC = "/Users/ricky/git/macshot/macshot/Services/BeautifyRenderer.swift"

text = open(SRC).read()
start = text.index("// Mesh gradients")
end = text.index("// Linear gradients")
block = text[start:end]

# Each style is `meshStyle(points: [...], colors: [...], fallbackStops: [...])`.
styles = re.findall(
    r"meshStyle\(\s*points:\s*\[(.*?)\]\s*,\s*colors:\s*\[(.*?)\]\s*,\s*fallbackStops:",
    block,
    re.S,
)

names = [
    name
    for name in re.findall(r"^\s*//\s*([A-Z][A-Za-z ]+?) — ", block, re.M)
    if name != "Mesh gradients"
]

if len(styles) != 18:
    sys.exit(f"expected 18 mesh styles, found {len(styles)}")
if len(names) != 18:
    sys.exit(f"expected 18 names, found {len(names)}: {names}")

num = r"(-?\d+(?:\.\d+)?)"

out = []
for (points_src, colors_src), name in zip(styles, names):
    points = re.findall(rf"SIMD2\(\s*{num}\s*,\s*{num}\s*\)", points_src)
    colors = re.findall(rf"c\(\s*{num}\s*,\s*{num}\s*,\s*{num}\s*\)", colors_src)
    if len(points) != 9:
        sys.exit(f"{name}: expected 9 points, found {len(points)}")
    if len(colors) != 9:
        sys.exit(f"{name}: expected 9 colours, found {len(colors)}")

    # The sampler assumes the grid's border points sit on the border of the unit
    # square, which is what makes the mesh cover the whole background.
    for index, (x, y) in enumerate(points):
        row, column = divmod(index, 3)
        x, y = float(x), float(y)
        if row == 0 and y != 0 or row == 2 and y != 1:
            sys.exit(f"{name}: point {index} is off the top/bottom edge")
        if column == 0 and x != 0 or column == 2 and x != 1:
            sys.exit(f"{name}: point {index} is off the left/right edge")

    coords = ", ".join(f"{float(x):g}, {float(y):g}" for x, y in points)
    rgb = ", ".join(
        "Rgb(0x{:02X}, 0x{:02X}, 0x{:02X})".format(
            round(float(r) * 255), round(float(g) * 255), round(float(b) * 255)
        )
        for r, g, b in colors
    )
    out.append(f'        // {name}\n        new(\n            [{coords}],\n            [{rgb}]),')

print("\n".join(out))
