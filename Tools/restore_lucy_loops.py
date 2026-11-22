"""Restore the project's Loop Time settings after importing the licensed Lucy 1.0 package."""

from pathlib import Path
import re


PROJECT = Path(__file__).resolve().parents[1]
ANIMATIONS = PROJECT / "Assets" / "Lucy" / "Animation"
MOVEMENT_LOOP = re.compile(
    r"Lucy_(?:(?:Fight_Walk|Walk|Run|Crouch)_(?:F|FL|FR|B|BL|BR|L|R)(?:_1)?"
    r"|Sprint_1|Jump_Loop|CrouchIdle)_(?:Root|Inplace)\.FBX\.meta"
)
LOOP_TIME = re.compile(rb"(?m)^      loopTime: ([01])(?=\r?$)")


def main():
    paths = [
        path for path in (ANIMATIONS / "Movement").rglob("*.meta")
        if MOVEMENT_LOOP.fullmatch(path.name)
    ]
    paths.extend([
        ANIMATIONS / "Lucy_Idle.FBX.meta",
        ANIMATIONS / "Battle" / "Inplace" / "Lucy_Fight_Idle_Inplace.FBX.meta",
        ANIMATIONS / "Battle" / "Root" / "Lucy_Fight_Idle_Root.FBX.meta",
    ])
    if len(paths) != 83 or any(not path.is_file() for path in paths):
        raise SystemExit("Import the complete Lucy 1.0 package into Assets/Lucy first.")

    updates = {}
    for path in paths:
        original = path.read_bytes()
        matches = LOOP_TIME.findall(original)
        if len(matches) != 1:
            raise SystemExit(f"Expected one imported animation clip in {path.relative_to(PROJECT)}.")
        if matches[0] == b"0":
            updates[path] = LOOP_TIME.sub(b"      loopTime: 1", original)

    for path, content in updates.items():
        path.write_bytes(content)
    print(f"Loop Time is enabled for {len(paths)} clips ({len(updates)} updated).")


if __name__ == "__main__":
    main()
