"""Compare Cargo/uv dependency data without executing package or PR code."""

import copy
import json
import re
import sys
import tomllib
from urllib.parse import urlsplit

CRATES = "registry+https://github.com/rust-lang/crates.io-index"
PYPI = {"registry": "https://pypi.org/simple"}


def patch(old, new):
    pattern = r"(\^|~|==|=)?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    a = re.fullmatch(pattern, old) if isinstance(old, str) else None
    b = re.fullmatch(pattern, new) if isinstance(new, str) else None
    return bool(a and b and a.groups()[:3] == b.groups()[:3] and int(b[4]) > int(a[4]))


def cargo_manifest(before, after):
    for field in ("dependencies", "dev-dependencies", "build-dependencies"):
        for name, old in before.get(field, {}).items():
            new = after.get(field, {}).get(name)
            if patch(old, new):
                after[field][name] = old
            elif (
                isinstance(old, dict)
                and isinstance(new, dict)
                and patch(old.get("version"), new.get("version"))
            ):
                new["version"] = old["version"]
    for name, target in before.get("target", {}).items():
        if name in after.get("target", {}):
            cargo_manifest(target, after["target"][name])


def requirement_list(before, after):
    if (
        not isinstance(before, list)
        or not isinstance(after, list)
        or len(before) != len(after)
    ):
        return
    # Exact pins used by this project; extras, markers and package identity stay intact.
    pattern = r"([A-Za-z0-9_.-]+(?:\[[^\]]+\])?)(==\d+\.\d+\.\d+)(\s*;.*)?"
    for i, (old, new) in enumerate(zip(before, after, strict=True)):
        a = re.fullmatch(pattern, old) if isinstance(old, str) else None
        b = re.fullmatch(pattern, new) if isinstance(new, str) else None
        if a and b and a[1] == b[1] and a[3] == b[3] and patch(a[2], b[2]):
            after[i] = old


def uv_manifest(before, after):
    requirement_list(
        before.get("project", {}).get("dependencies"),
        after.get("project", {}).get("dependencies"),
    )
    requirement_list(
        before.get("build-system", {}).get("requires"),
        after.get("build-system", {}).get("requires"),
    )
    for old, new in (
        (before.get("dependency-groups", {}), after.get("dependency-groups", {})),
        (
            before.get("project", {}).get("optional-dependencies", {}),
            after.get("project", {}).get("optional-dependencies", {}),
        ),
    ):
        for name, requirements in old.items():
            requirement_list(requirements, new.get(name))


def package_index(packages):
    result = {}
    for package in packages:
        version = package["version"]
        # Multiple major/minor lanes coexist in Cargo; do not collapse them by name.
        lane = (
            ".".join(version.split(".")[:2])
            if re.fullmatch(r"\d+\.\d+\.\d+", version)
            else version
        )
        key = (package["name"], json.dumps(package.get("source"), sort_keys=True), lane)
        if key in result:
            raise ValueError("ambiguous packages in the same version lane")
        result[key] = package
    return result


def uv_edges(before, after, changes):
    """Normalize only version-qualified references and exact direct-dependency pins."""
    if isinstance(before, dict) and isinstance(after, dict):
        for field in ("version", "specifier"):
            if (
                field in before
                and field in after
                and before.get("name") == after.get("name")
            ):
                name = before.get("name")
                old = before[field].removeprefix("==")
                new = after[field].removeprefix("==")
                if changes.get((name, new)) == old and patch(
                    before[field], after[field]
                ):
                    after[field] = before[field]
        for key in before.keys() & after.keys():
            uv_edges(before[key], after[key], changes)
    elif (
        isinstance(before, list)
        and isinstance(after, list)
        and len(before) == len(after)
    ):
        for old, new in zip(before, after, strict=True):
            uv_edges(old, new, changes)


def valid_artifact(artifact):
    url = urlsplit(artifact.get("url", ""))
    return (
        url.scheme == "https"
        and url.hostname == "files.pythonhosted.org"
        and url.username is None
        and url.password is None
        and url.port in (None, 443)
        and not url.query
        and not url.fragment
        and re.fullmatch(r"sha256:[0-9a-f]{64}", artifact.get("hash", "")) is not None
    )


def patch_only(kind, before_manifest, after_manifest, before_lock, after_lock):
    manifest, lock = copy.deepcopy(after_manifest), copy.deepcopy(after_lock)
    if kind == "cargo":
        cargo_manifest(before_manifest, manifest)
        if before_lock.get("version") != 4 or lock.get("version") != 4:
            return False
    elif kind == "uv":
        uv_manifest(before_manifest, manifest)
        if (before_lock.get("version"), before_lock.get("revision")) != (1, 3):
            return False
    else:
        return False
    if manifest != before_manifest:
        return False
    before = package_index(before_lock["package"])
    after = package_index(lock["package"])
    if before.keys() != after.keys():
        return False
    changes = {}
    for key, old in before.items():
        new = after[key]
        if old["version"] == new["version"]:
            continue
        if not patch(old["version"], new["version"]):
            return False
        identity = (new["name"], new["version"])
        if identity in changes:
            return False
        changes[identity] = old["version"]
        if kind == "cargo":
            if old.get("source") != CRATES or not re.fullmatch(
                r"[0-9a-f]{64}", new.get("checksum", "")
            ):
                return False
            new["checksum"] = old["checksum"]
        else:
            if old.get("source") != PYPI:
                return False
            artifacts = new.get("wheels", []) + (
                [new["sdist"]] if "sdist" in new else []
            )
            if not artifacts or not all(valid_artifact(item) for item in artifacts):
                return False
            for field in ("sdist", "wheels"):
                new.pop(field, None)
                if field in old:
                    new[field] = old[field]
        new["version"] = old["version"]
    for key, old in before.items():
        new = after[key]
        if kind == "cargo":
            for i, edge in enumerate(new.get("dependencies", [])):
                fields = edge.split(" ", 2)
                if len(fields) >= 2 and (fields[0], fields[1]) in changes:
                    fields[1] = changes[(fields[0], fields[1])]
                    new["dependencies"][i] = " ".join(fields)
        else:
            uv_edges(old, new, changes)
    # Canonicalize order only; package membership, features and graph edges must match.
    lock["package"] = [after[key] for key in sorted(after)]
    original = {**before_lock, "package": [before[key] for key in sorted(before)]}
    return bool(changes) and original == lock


def main():
    request = json.load(sys.stdin)
    try:
        documents = [tomllib.loads(text) for text in request["documents"]]
        allowed = patch_only(request["kind"], *documents)
    except (ValueError, KeyError, TypeError, AttributeError):
        allowed = False
    print(json.dumps(allowed))


if __name__ == "__main__":
    main()
