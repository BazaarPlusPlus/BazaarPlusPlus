import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from check_mod_dependency_update import CENTRAL_PACKAGES, TEST_TOOLS, validate_changes


def packages(name, version="1.0.0", extra=""):
    return (
        '<Project><ItemGroup><PackageVersion Include="'
        + name
        + '" Version="'
        + version
        + '" /></ItemGroup>'
        + extra
        + "</Project>"
    )


class ModDependencyUpdateTests(unittest.TestCase):
    def test_existing_test_tool_versions_can_change(self):
        for name in TEST_TOOLS:
            with self.subTest(name=name):
                self.assertEqual(
                    validate_changes({CENTRAL_PACKAGES: (packages(name), packages(name, "2.0.0"))}),
                    [],
                )

    def test_runtime_generators_and_unknown_dependencies_require_manual_maintenance(self):
        for name in (
            "Newtonsoft.Json",
            "MessagePackAnalyzer",
            "BepInEx.Core",
            "UnityEngine.Modules",
            "Krafs.Publicizer",
            "Microsoft.Data.Sqlite",
            "Future.Runtime.Library",
        ):
            with self.subTest(name=name):
                self.assertTrue(
                    validate_changes({CENTRAL_PACKAGES: (packages(name), packages(name, "2.0.0"))})
                )

    def test_allowed_update_cannot_hide_a_new_reference_or_msbuild_target(self):
        before = packages("xunit")
        for extra in (
            '<ItemGroup><PackageVersion Include="MessagePack" Version="9.0.0" /></ItemGroup>',
            '<Target Name="NewTarget" BeforeTargets="Build" />',
        ):
            with self.subTest(extra=extra):
                self.assertTrue(
                    validate_changes(
                        {CENTRAL_PACKAGES: (before, packages("xunit", "2.0.0", extra))}
                    )
                )

    def test_runtime_lock_churn_is_rejected_even_with_allowed_direct_update(self):
        self.assertTrue(
            validate_changes(
                {
                    CENTRAL_PACKAGES: (packages("xunit"), packages("xunit", "2.0.0")),
                    "bazaarplusplus-mod/src/BazaarPlusPlus/packages.lock.json": ("old", "new"),
                }
            )
        )

    def test_changes_to_game_references_sources_or_removed_packages_are_rejected(self):
        for path in (
            "bazaarplusplus-mod/build/GameLibraries.props",
            "bazaarplusplus-mod/src/New.cs",
            CENTRAL_PACKAGES,
        ):
            with self.subTest(path=path):
                self.assertTrue(validate_changes({path: ("old", None)}))

    def test_invalid_xml_is_rejected(self):
        self.assertTrue(validate_changes({CENTRAL_PACKAGES: (packages("xunit"), "<broken")}))

    def test_other_projects_are_not_subject_to_mod_policy(self):
        self.assertEqual(
            validate_changes({"bazaarplusplus-server/package-lock.json": ("old", "new")}), []
        )

    def test_cli_ignores_base_branch_drift_but_rejects_bot_lock_changes(self):
        script = Path(__file__).with_name("check_mod_dependency_update.py").resolve()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            def git(*args):
                return (
                    subprocess.check_output(["git", *args], cwd=root, stderr=subprocess.DEVNULL)
                    .decode()
                    .strip()
                )

            def commit():
                git("add", ".")
                git(
                    "-c",
                    "user.name=Test",
                    "-c",
                    "user.email=test@example.invalid",
                    "-c",
                    "commit.gpgsign=false",
                    "-c",
                    "core.hooksPath=/dev/null",
                    "commit",
                    "-m",
                    "fixture",
                )
                return git("rev-parse", "HEAD")

            git("init")
            central = root / CENTRAL_PACKAGES
            central.parent.mkdir(parents=True)
            central.write_text(packages("xunit"))
            lock = central.parent / "src" / "BazaarPlusPlus" / "packages.lock.json"
            lock.parent.mkdir(parents=True)
            lock.write_text("old lock")
            base = commit()
            central.write_text(packages("xunit", "2.0.0"))
            bot = commit()
            git("checkout", "--detach", base)
            lock.write_text("base branch moved independently")
            advanced_base = commit()

            def check(head):
                return subprocess.run(
                    [sys.executable, str(script), advanced_base, head],
                    cwd=root,
                    capture_output=True,
                    text=True,
                )

            self.assertEqual(check(bot).returncode, 0)
            git("checkout", "--detach", bot)
            lock.write_text("bot changed runtime dependency")
            rejected = check(commit())
            self.assertNotEqual(rejected.returncode, 0)
            self.assertIn("packages.lock.json", rejected.stderr)
            self.assertNotEqual(check("missing-revision").returncode, 0)


if __name__ == "__main__":
    unittest.main()
