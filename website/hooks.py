"""Copy the application artwork into the site without duplicating source assets."""
from pathlib import Path
import shutil


def on_post_build(config, **kwargs):
    root = Path(__file__).resolve().parent.parent
    destination = Path(config["site_dir"]) / "assets"
    destination.mkdir(parents=True, exist_ok=True)
    for name in ("HoloWin.png", "HoloWin.ico"):
        shutil.copyfile(root / "assets" / name, destination / name)
