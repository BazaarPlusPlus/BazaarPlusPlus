#!/usr/bin/env python3
"""Filesystem and real codesign regressions for the developer repair path."""

import hashlib
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import tempfile
import unittest

NORMALIZER = Path(__file__).with_name("normalize-macos-bundle-root.sh")
REPAIR = Path(__file__).with_name("repair-macos-trampoline.sh")
DUPLICATE = "TheBazaar_ARM64.app"


def digest(root):
    records = ["D .\n"]
    for path in root.rglob("*"):
        relative = "./" + path.relative_to(root).as_posix()
        if path.is_dir():
            records.append(f"D {relative}\n")
        else:
            value = hashlib.sha256(path.read_bytes()).hexdigest()
            records.append(f"F {value}  {relative}\n")
    return hashlib.sha256("".join(sorted(records)).encode()).hexdigest()


class BundleRootTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="bpp-normalize-test-")
        self.addCleanup(self.temp.cleanup)
        self.game = Path(self.temp.name)
        self.app = self.game / "TheBazaar.app"
        self.backups = self.game / ".bpp-bundle-root-stash"
        for relative in ("Contents/MacOS", "Contents/Frameworks", "Contents/Resources/Data"):
            (self.app / relative).mkdir(parents=True)
        (self.app / "Contents/Info.plist").write_bytes(plistlib.dumps({
            "CFBundleExecutable": "The Bazaar",
            "CFBundleIdentifier": "com.TempoStorm.TheBazaar",
            "CFBundlePackageType": "APPL",
            "CFBundleShortVersionString": "test",
        }))
        for relative in ("Contents/MacOS/The Bazaar", "Contents/Frameworks/UnityPlayer.dylib",
                         "Contents/Frameworks/libmonobdwgc-2.0.dylib", "Contents/Resources/Data/boot.config"):
            (self.app / relative).write_bytes(b"original")

    def duplicate(self):
        staged = self.game / "staged.app"
        shutil.copytree(self.app, staged)
        staged.rename(self.app / DUPLICATE)

    def normalize(self, success=True):
        result = subprocess.run(["/bin/bash", str(NORMALIZER), str(self.game)], text=True, capture_output=True)
        self.assertEqual(result.returncode == 0, success, result.stdout + result.stderr)
        return result

    def test_repeated_repair_and_steam_update_preserve_one_backup(self):
        self.duplicate()
        original = digest(self.app / DUPLICATE)
        self.normalize()
        self.assertEqual((self.backups / "current").read_text().strip(), original)
        for _ in range(3):
            self.normalize()
            self.duplicate()
            self.normalize()
            self.assertFalse((self.app / DUPLICATE).exists())
            self.assertEqual(digest(self.backups / f"{original}.app"), original)
        (self.app / "Contents/Resources/Data/boot.config").write_bytes(b"new-game-build")
        self.duplicate()
        updated = digest(self.app / DUPLICATE)
        self.normalize()
        self.assertFalse((self.backups / f"{original}.app").exists())
        self.assertEqual(digest(self.backups / f"{updated}.app"), updated)
        self.assertEqual(len(list(self.backups.iterdir())), 2)

    def test_unknown_and_modified_backup_are_not_overwritten(self):
        (self.app / ".DS_Store").write_bytes(b"Finder metadata")
        self.duplicate()
        (self.app / "user-file").write_text("keep")
        self.normalize(success=False)
        self.assertTrue((self.app / DUPLICATE).exists())
        (self.app / "user-file").unlink()
        self.normalize()
        self.assertEqual((self.app / ".DS_Store").read_bytes(), b"Finder metadata")
        backup = next(self.backups.glob("*.app"))
        (backup / "user-file").write_text("keep")
        self.duplicate()
        self.normalize(success=False)
        self.assertEqual((backup / "user-file").read_text(), "keep")
        self.assertTrue((self.app / DUPLICATE).exists())

    def test_legacy_stash_stays_outside_application(self):
        (self.app / ".DS_Store").write_bytes(b"Finder metadata")
        self.backups.mkdir()
        shutil.copytree(self.app, self.backups / DUPLICATE)
        original = digest(self.backups / DUPLICATE)
        (self.backups / ".DS_Store").write_bytes(b"Legacy Finder metadata")
        self.normalize()
        self.normalize()
        self.duplicate()
        self.normalize()
        self.assertFalse((self.app / DUPLICATE).exists())
        self.assertFalse((self.backups / DUPLICATE).exists())
        self.assertEqual(len(list(self.backups.glob("*.app"))), 1)
        self.assertEqual(digest(self.backups / f"{original}.app"), original)
        self.assertEqual((self.app / ".DS_Store").read_bytes(), b"Finder metadata")
        self.assertEqual((self.backups / ".DS_Store").read_bytes(), b"Legacy Finder metadata")

    def test_backup_metadata_rejects_directories_links_and_unknown_files(self):
        self.backups.mkdir()
        self.duplicate()
        outside = self.game / "outside"
        outside.write_bytes(b"keep")
        for kind in ("directory", "symbolic-link", "unknown-file"):
            with self.subTest(kind=kind):
                entry = self.backups / ("user-file" if kind == "unknown-file" else ".DS_Store")
                if kind == "directory":
                    entry.mkdir()
                elif kind == "symbolic-link":
                    entry.symlink_to(outside)
                else:
                    entry.write_bytes(b"keep")
                self.normalize(success=False)
                self.assertTrue((self.app / DUPLICATE).is_dir())
                self.assertFalse((self.backups / "current").exists())
                self.assertEqual(outside.read_bytes(), b"keep")
                if kind == "directory":
                    entry.rmdir()
                else:
                    self.assertEqual(entry.read_bytes(), b"keep")
                    entry.unlink()

    def test_symbolic_link_is_rejected_without_moving_source(self):
        self.duplicate()
        (self.app / DUPLICATE / "link").symlink_to(self.game, target_is_directory=True)
        self.normalize(success=False)
        self.assertTrue((self.app / DUPLICATE).exists())
        self.assertFalse(self.backups.exists())

    def test_unreadable_file_does_not_produce_an_incomplete_backup(self):
        self.duplicate()
        resource = self.app / DUPLICATE / "Contents/Resources/unreadable"
        resource.write_bytes(b"must be included in the fingerprint")
        resource.chmod(0)
        try:
            self.normalize(success=False)
            self.assertTrue((self.app / DUPLICATE).exists())
            self.assertFalse(self.backups.exists())
        finally:
            resource.chmod(0o600)

    def test_partly_deleted_duplicate_and_missing_legacy_record_resume(self):
        self.duplicate()
        self.normalize()
        current = (self.backups / "current").read_text().strip()
        tombstone = self.backups / f".delete-{current}.app"
        tombstone.mkdir()
        (tombstone / "remaining-file").write_text("partly removed")
        self.normalize()
        self.assertFalse(tombstone.exists())
        self.assertTrue((self.backups / f"{current}.app").is_dir())
        (self.backups / "current").unlink()
        self.normalize()
        self.assertEqual((self.backups / "current").read_text().strip(), current)

    def test_real_repair_leaves_final_bundle_signed_on_every_run(self):
        source = self.game / "probe.c"
        engine = self.app / "Contents/Frameworks/UnityPlayer.dylib"
        source.write_text("int unity_probe(void) { return 0; }")
        sdk = subprocess.check_output(["xcrun", "--sdk", "macosx", "--show-sdk-path"], text=True).strip()
        subprocess.run(["clang", "-isysroot", sdk, "-arch", "arm64", "-dynamiclib", "-install_name",
                        "@rpath/UnityPlayer.dylib", str(source), "-o", str(engine)], check=True, capture_output=True)
        shutil.copyfile(engine, self.app / "Contents/Frameworks/libmonobdwgc-2.0.dylib")
        source.write_text("extern int unity_probe(void); int main(void) { return unity_probe(); }")
        subprocess.run(["clang", "-isysroot", sdk, "-arch", "arm64", str(source), str(engine),
                        "-Wl,-rpath,@executable_path/../Frameworks", "-o",
                        str(self.app / "Contents/MacOS/The Bazaar")], check=True, capture_output=True)
        (self.app / ".DS_Store").write_bytes(b"Finder metadata")
        self.backups.mkdir()
        (self.backups / ".DS_Store").write_bytes(b"Legacy Finder metadata")
        self.duplicate()
        (self.game / "libdoorstop.dylib").write_text("DOORSTOP")
        stub = self.game / "stub"
        source.write_text("int main(void) { return 0; }")
        subprocess.run(["clang", "-isysroot", sdk, "-arch", "arm64", str(source), "-o", str(stub)], check=True, capture_output=True)
        env = {**os.environ, "BPP_GAME_ROOT": str(self.game), "BPP_TRAMPOLINE_STUB": str(stub)}
        for _ in range(3):
            result = subprocess.run(["/bin/bash", str(REPAIR)], env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            verify = subprocess.run(["codesign", "--verify", "--deep", "--strict", str(self.app)], text=True, capture_output=True)
            self.assertEqual(verify.returncode, 0, verify.stdout + verify.stderr)
            self.assertFalse((self.app / DUPLICATE).exists())
            self.assertEqual((self.app / ".DS_Store").read_bytes(), b"Finder metadata")
            self.assertEqual((self.backups / ".DS_Store").read_bytes(), b"Legacy Finder metadata")


if __name__ == "__main__":
    unittest.main()
