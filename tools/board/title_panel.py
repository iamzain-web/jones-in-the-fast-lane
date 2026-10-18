"""
THE TITLE SCREEN'S BLANK PANEL, PAINTED ONTO OUR BOARD.

Sierra draws the setup dialogs - the main menu, "How Many Players?", the character
and goal pickers - with SCI's INVISIBLE window (`global38`, back -1). They have no
backdrop of their own; they draw over whatever room is showing, which is the title
screen. The cream panel they appear to sit on is not drawn by the game at all: it is
PAINTED INTO `pic_11`, her title picture. (View 10 loop 0 cel 0 is the 183x112
"menu backdrop" the scripts name, and in the port's extraction it is fully
transparent - the pixels live in the pic.)

Our `pic_11` was a copy of our board, so the moment the browser head actually
started drawing our art, the panel vanished and the menu floated over the town:
PLAY GAME / RESTORE GAME / WATCH DEMO in mid-air, and black text on a busy
cityscape in every setup dialog after it.

THE GEOMETRY IS MEASURED FROM HERS, NOT GUESSED, and it agrees with the scripts:
the black frame's outer bounds in png4x are x[272..1003] y[176..623], which is
exactly 183x112 at (68,44) in the game's own 320x200 coordinates - the size the
source calls the menu backdrop. Note this is NOT the declared dialog rect
(`moveTo: 69 44`, 184x119); the drawn panel sits one pixel left of it and seven
short, and a panel placed on the declared numbers would be visibly wrong.

Run it again after the board changes; it reads board.png and overwrites pic_11.png.
"""
from PIL import Image, ImageDraw
import sys, pathlib

# 1x geometry, measured from png4x/pic_11.png and png12x/pic_11.png (they agree).
LEFT, TOP, W, H = 68, 44, 183, 112
FILL = (248, 248, 240)          # sampled from the centre of her panel
BORDER = (0, 0, 0)

ROOT = pathlib.Path(r"C:\AI-Accelerator\jones\assets")

def build(set_dir: str, factor: int) -> None:
    d = ROOT / set_dir
    board = d / "board.png"
    if not board.exists():
        print(f"{set_dir}: no board.png, skipped")
        return
    im = Image.open(board).convert("RGBA")
    w, h = im.size
    if (w, h) != (320 * factor, 200 * factor):
        print(f"{set_dir}: board is {w}x{h}, expected {320*factor}x{200*factor} - SKIPPED")
        return
    draw = ImageDraw.Draw(im)
    x0, y0 = LEFT * factor, TOP * factor
    x1, y1 = x0 + W * factor - 1, y0 + H * factor - 1
    draw.rectangle([x0, y0, x1, y1], fill=FILL, outline=BORDER, width=factor)
    out = d / "pic_11.png"
    im.save(out)
    print(f"{set_dir}: panel {W}x{H} at ({LEFT},{TOP}) -> {out.name} "
          f"[{x0}..{x1}] x [{y0}..{y1}], border {factor}px")

for s, f in (("png4x-cast0", 4), ("png12x-cast0", 12)):
    build(s, f)
