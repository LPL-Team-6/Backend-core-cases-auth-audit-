"""Draws a SPECIMEN-stamped PNG for every document in personas.json.

The images are plain mock-ups (header, the extracted field values, a watermark) so the
dashboard has something to show next to the fields. They are not replicas of any real
government form. Needs Pillow: python3 -m pip install pillow
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).parent
OUT = HERE / "specimens"


def font(size, bold=False):
    name = "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"
    for base in ("/usr/share/fonts/truetype/dejavu", "/Library/Fonts", "C:/Windows/Fonts"):
        path = Path(base) / name
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default(size)


def card(doc, applicant):
    is_id = doc["type"] == "GovernmentId"
    w, h = (1000, 630) if is_id else (850, 1100)
    img = Image.new("RGB", (w, h), "#f4f1ea" if is_id else "white")
    d = ImageDraw.Draw(img)

    band = "#1f3b63" if is_id else "#333333"
    d.rectangle([0, 0, w, 90], fill=band)
    d.text((30, 25), doc["title"].upper(), font=font(34, True), fill="white")

    y = 120
    if is_id:
        # Photo placeholder.
        d.rectangle([30, 120, 260, 420], outline="#888", width=3, fill="#dcd7cc")
        d.text((95, 255), "PHOTO", font=font(28, True), fill="#888")
        x = 300
    else:
        d.text((30, y), "Applicant: " + applicant["fullName"], font=font(22), fill="#333")
        y += 60
        x = 30

    for field in doc["fields"]:
        label = field["name"].replace("_", " ")
        d.text((x, y), label, font=font(18, True), fill="#666")
        size = 26
        while size > 14 and d.textlength(field["value"], font=font(size)) > w - x - 30:
            size -= 1
        d.text((x, y + 24), field["value"], font=font(size), fill="#111")
        y += 72

    # Watermark on its own layer so it can be rotated.
    mark = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    md = ImageDraw.Draw(mark)
    md.text((w // 2, h // 2), "SPECIMEN", font=font(140 if is_id else 130, True),
            fill=(200, 0, 0, 90), anchor="mm")
    mark = mark.rotate(25, resample=Image.BICUBIC)
    img = Image.alpha_composite(img.convert("RGBA"), mark)
    ImageDraw.Draw(img).text((30, h - 40), "SYNTHETIC TEST DATA - NOT A REAL DOCUMENT",
                             font=font(18, True), fill="#c00000")
    return img.convert("RGB")


def main():
    OUT.mkdir(exist_ok=True)
    personas = json.loads((HERE / "personas.json").read_text())["personas"]
    for p in personas:
        for doc in p["documents"]:
            card(doc, p["applicant"]).save(OUT / doc["file"])
            print("wrote", OUT / doc["file"])


if __name__ == "__main__":
    main()
