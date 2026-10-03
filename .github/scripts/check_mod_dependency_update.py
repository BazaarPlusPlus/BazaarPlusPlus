"""Keep dependency bots from changing Mod runtime inputs (ADR-0010).

The NuGet allowlist controls direct updates; this checks the resulting diff,
including transitive lockfile churn. It does not certify game compatibility.
"""

import argparse
import subprocess
import xml.etree.ElementTree as ET

TEST_TOOLS = frozenset({"Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio"})
CENTRAL_PACKAGES = "bazaarplusplus-mod/Directory.Packages.props"


def package_structure(source):
    root = ET.fromstring(source)
    for package in root.iter("PackageVersion"):
        if package.get("Include") in TEST_TOOLS and "Version" in package.attrib:
            package.set("Version", "<test-tool-version>")

    def structure(element):
        return (
            element.tag,
            sorted(element.attrib.items()),
            (element.text or "").strip(),
            (element.tail or "").strip(),
            [structure(child) for child in element],
        )

    return structure(root)


def validate_changes(changes):
    """changes maps each changed path to its (base bytes, head bytes)."""
    problems = []
    for path, (before, after) in changes.items():
        if not path.startswith("bazaarplusplus-mod/"):
            continue
        if path != CENTRAL_PACKAGES or before is None or after is None:
            problems.append(f"{path}: requires manual Mod dependency maintenance")
            continue
        try:
            if package_structure(before) != package_structure(after):
                problems.append(f"{path}: only existing test-tool versions may change")
        except ET.ParseError:
            problems.append(f"{path}: invalid MSBuild XML")
    return problems


def git(*args):
    return subprocess.check_output(["git", *args])


def read_file(revision, path):
    result = subprocess.run(["git", "show", f"{revision}:{path}"], capture_output=True, check=False)
    return result.stdout if result.returncode == 0 else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("base")
    parser.add_argument("head")
    args = parser.parse_args()
    # Resolve revisions first: a missing commit must never look like an empty diff.
    base = git("rev-parse", "--verify", f"{args.base}^{{commit}}").decode().strip()
    head = git("rev-parse", "--verify", f"{args.head}^{{commit}}").decode().strip()
    base = git("merge-base", base, head).decode().strip()
    names = (
        git("diff", "--name-only", "--no-renames", "-z", base, head, "--", "bazaarplusplus-mod/")
        .decode()
        .split("\0")
    )
    problems = validate_changes(
        {path: (read_file(base, path), read_file(head, path)) for path in names if path}
    )
    if problems:
        raise SystemExit("\n".join(problems) + "\nSee ADR-0010 and docs/development.md.")
    print("Bot Mod changes are limited to test-tool versions; game compatibility was not tested.")


if __name__ == "__main__":
    main()
