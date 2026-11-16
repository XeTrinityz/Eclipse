"""Builds Eclipse.dll in Release and packs the release zip, the DLL and mod.toml.

python dev/pack.py          makes Eclipse.zip in the mod folder
python dev/pack.py try      also puts the DLL in the mod folder, so the next game start runs it
python dev/pack.py source   takes the DLL out again, so the next game start compiles scripts/
"""

import re
import shutil
import subprocess
import sys
import tempfile
import tomllib
import zipfile
from pathlib import Path

MOD = Path(__file__).resolve().parent.parent
BUILT = ["Eclipse.dll", "Eclipse.pdb"]
KEPT = ["mod.toml"]
# Only the DLL ships. The .pdb is for the try mode.
SHIPPED = ["Eclipse.dll"]


def check_manifest():
    # Without the assembly line BmSDK only looks for scripts/, so a DLL-only install loads nothing.
    toml = tomllib.loads((MOD / "mod.toml").read_text(encoding="utf-8"))
    if toml.get("assembly") != "Eclipse.dll":
        sys.exit('mod.toml needs assembly = "Eclipse.dll" or the release won\'t load.')
    app = (MOD / "scripts" / "Core" / "App.cs").read_text(encoding="utf-8")
    found = re.search(r'Version\s*=\s*"([^"]+)"', app)
    if not found or found.group(1) != toml.get("version"):
        sys.exit("The version in mod.toml and App.Version don't match.")


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "zip"
    if mode not in ("zip", "try", "source"):
        sys.exit(__doc__)

    if mode == "source":
        for name in BUILT:
            (MOD / name).unlink(missing_ok=True)
        print("The DLL is out. The game compiles scripts/ on its next start.")
        return

    check_manifest()

    with tempfile.TemporaryDirectory() as out:
        out = Path(out)
        build = subprocess.run(
            ["dotnet", "build", str(MOD / "dev" / "Eclipse.csproj"), "-c", "Release",
             "-o", str(out), "-nologo", "-v", "q"],
        )
        if build.returncode != 0:
            sys.exit("The build failed, so nothing was packed.")

        zip_path = MOD / "Eclipse.zip"
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as pack:
            for name in KEPT:
                pack.write(MOD / name, f"Eclipse/{name}")
            for name in SHIPPED:
                pack.write(out / name, f"Eclipse/{name}")
        print(f"Packed {zip_path.name}.")

        if mode == "try":
            for name in BUILT:
                shutil.copy2(out / name, MOD / name)
            print("The DLL is in the mod folder. The game runs it from its next start.")


if __name__ == "__main__":
    main()
