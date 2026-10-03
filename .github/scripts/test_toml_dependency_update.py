import copy
import tomllib
import unittest
from pathlib import Path

from check_toml_dependency_update import CRATES, PYPI, patch_only


def cargo_fixture():
    manifest = {
        "dependencies": {"library": {"version": "1.2.3", "features": ["serde"]}}
    }
    lock = {
        "version": 4,
        "package": [
            {
                "name": "app",
                "version": "5.6.0",
                "dependencies": ["library 1.2.3", "library 2.0.0", "specta"],
            },
            {
                "name": "library",
                "version": "1.2.3",
                "source": CRATES,
                "checksum": "a" * 64,
            },
            {
                "name": "library",
                "version": "2.0.0",
                "source": CRATES,
                "checksum": "b" * 64,
            },
            {
                "name": "specta",
                "version": "2.0.0-rc.25",
                "source": CRATES,
                "checksum": "c" * 64,
            },
        ],
    }
    after_manifest, after_lock = copy.deepcopy(manifest), copy.deepcopy(lock)
    after_manifest["dependencies"]["library"]["version"] = "1.2.4"
    after_lock["package"][1].update(version="1.2.4", checksum="d" * 64)
    after_lock["package"][0]["dependencies"][0] = "library 1.2.4"
    return [manifest, after_manifest, lock, after_lock]


def artifact(version):
    return {
        "url": f"https://files.pythonhosted.org/packages/library-{version}.whl",
        "hash": "sha256:" + "a" * 64,
        "size": 100,
    }


def uv_fixture():
    manifest = {
        "project": {"dependencies": ['library[extra]==1.2.3; python_version >= "3.14"']}
    }
    lock = {
        "version": 1,
        "revision": 3,
        "requires-python": "==3.14.*",
        "package": [
            {
                "name": "app",
                "version": "1.0.0",
                "source": {"editable": "."},
                "dependencies": [
                    {"name": "library", "version": "1.2.3", "source": PYPI}
                ],
                "metadata": {
                    "requires-dist": [
                        {
                            "name": "library",
                            "specifier": "==1.2.3",
                            "extras": ["extra"],
                            "marker": "python_version >= '3.14'",
                        }
                    ]
                },
            },
            {
                "name": "library",
                "version": "1.2.3",
                "source": PYPI,
                "wheels": [artifact("1.2.3")],
            },
        ],
    }
    after_manifest, after_lock = copy.deepcopy(manifest), copy.deepcopy(lock)
    after_manifest["project"]["dependencies"][0] = manifest["project"]["dependencies"][
        0
    ].replace("1.2.3", "1.2.4")
    after_lock["package"][0]["dependencies"][0]["version"] = "1.2.4"
    after_lock["package"][0]["metadata"]["requires-dist"][0]["specifier"] = "==1.2.4"
    after_lock["package"][1].update(version="1.2.4", wheels=[artifact("1.2.4")])
    return [manifest, after_manifest, lock, after_lock]


class CargoPolicyTests(unittest.TestCase):
    def test_patch_with_duplicate_names_qualified_edges_and_unchanged_prerelease(self):
        data = cargo_fixture()
        original = copy.deepcopy(data)
        self.assertTrue(patch_only("cargo", *data))
        self.assertEqual(data, original)

    def test_sensitive_metadata_and_graph_changes_are_rejected(self):
        mutations = [
            lambda d: d[1]["dependencies"]["library"].update(features=["other"]),
            lambda d: d[1].update(package={"build": "new.rs"}),
            lambda d: d[3]["package"][1].update(
                source="git+https://example.com/library"
            ),
            lambda d: d[3]["package"][1].update(checksum="invalid"),
            lambda d: d[3]["package"][0]["dependencies"].pop(),
            lambda d: d[3]["package"].append({"name": "new", "version": "1.0.0"}),
            lambda d: d[3]["package"][3].update(version="2.0.0-rc.26"),
            lambda d: d[3]["package"][1].update(version="1.3.0"),
            lambda d: d[3]["package"][1].update(version="1.2.2"),
            lambda d: d[3].update(version=5),
        ]
        for mutate in mutations:
            with self.subTest(mutation=mutate):
                data = cargo_fixture()
                mutate(data)
                self.assertFalse(patch_only("cargo", *data))

    def test_real_installer_lock_supports_patch_without_collapsing_duplicate_names(
        self,
    ):
        root = (
            Path(__file__).resolve().parents[2] / "bazaarplusplus-installer/src-tauri"
        )
        manifest = tomllib.loads((root / "Cargo.toml").read_text())
        lock = tomllib.loads((root / "Cargo.lock").read_text())
        after = copy.deepcopy(lock)
        target = next(
            p
            for p in after["package"]
            if p["name"] == "base64" and p["version"].startswith("0.22.")
        )
        old = target["version"]
        parts = old.split(".")
        target["version"] = ".".join([*parts[:2], str(int(parts[2]) + 1)])
        target["checksum"] = "f" * 64
        for package in after["package"]:
            if "dependencies" in package:
                package["dependencies"] = [
                    e.replace(f"base64 {old}", f"base64 {target['version']}")
                    for e in package["dependencies"]
                ]
        self.assertTrue(patch_only("cargo", manifest, manifest, lock, after))


class UvPolicyTests(unittest.TestCase):
    def test_patch_normalizes_pins_artifacts_and_metadata_but_preserves_markers(self):
        data = uv_fixture()
        original = copy.deepcopy(data)
        self.assertTrue(patch_only("uv", *data))
        self.assertEqual(data, original)

    def test_graph_markers_artifact_sources_and_prereleases_require_review(self):
        mutations = [
            lambda d: d[1]["project"].update(dependencies=["library==1.2.4"]),
            lambda d: d[3]["package"][0]["metadata"]["requires-dist"][0].update(
                marker="python_version < '3.14'"
            ),
            lambda d: d[3]["package"][0]["metadata"]["requires-dist"][0].update(
                specifier="==1.2.5"
            ),
            lambda d: d[3]["package"][1]["wheels"][0].update(
                url="https://files.pythonhosted.org.evil.test/library.whl"
            ),
            lambda d: d[3]["package"][1]["wheels"][0].update(hash="sha256:bad"),
            lambda d: d[3]["package"][1].update(
                source={"registry": "https://example.com"}
            ),
            lambda d: d[3]["package"][1].update(version="1.2.4rc1"),
            lambda d: d[3].update(revision=4),
        ]
        for mutate in mutations:
            with self.subTest(mutation=mutate):
                data = uv_fixture()
                mutate(data)
                self.assertFalse(patch_only("uv", *data))

    def test_prerelease_suffix_in_manifest_is_not_treated_as_a_marker(self):
        data = uv_fixture()
        data[0]["project"]["dependencies"] = ["library==1.2.3rc1"]
        data[1]["project"]["dependencies"] = ["library==1.2.4rc1"]
        self.assertFalse(patch_only("uv", *data))


if __name__ == "__main__":
    unittest.main()
