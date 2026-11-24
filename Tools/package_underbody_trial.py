"""Validate and package the private grounded-underbody Windows trial."""
import hashlib
import json
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "Reports/Underbody-20261124"
ROLLING = False
EXPECTED_CONTRACT = 13
LABEL = "grounded-clearance-trial"
ARTIFACTS = ROOT / "Artifacts/Underbody-20261124"
PLATFORM = ROOT / "Artifacts/PumpingProject-20261121/BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64"


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify():
    files = {p.name: sha(p) for p in PLATFORM.iterdir() if p.is_file()}
    build = load(EVIDENCE / "build.json")
    assert files == build["files"], "Build files changed"
    source = ROOT / "Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744"
    assert {p.name: sha(p) for p in source.glob("*.cs")} == build["source_sha256"], "Runtime source changed"
    assert all(sha(ROOT / "Assets/Editor" / name) == digest for name, digest in build["validation_source_sha256"].items()), "Fixture source changed"
    assert len(files) == 6 and load(PLATFORM / "modpack.json")["version"] == "1.0.2"
    reports = {"build.json": sha(EVIDENCE / "build.json"), "preserved-release.json": sha(EVIDENCE / "preserved-release.json")}
    counts = {"contract.json": EXPECTED_CONTRACT, "traction-forward.json": 14,
              "traction-reverse-pair.json": 12, "controls.json": 58,
              "shooting.json": 7, "collection-retention.json": 2, "corner-pile.json": 2}
    for name, count in counts.items():
        path = EVIDENCE / name
        report = load(path)
        assert report["passed"] and not report["errors"], name
        assert len(report.get("cases", report.get("checks", []))) == count, name
        assert report["runtimeDllSha256"] == files["Ninjas4744.dll"], name
        assert report["bundleSha256"] == files["rebuilt_ninjas_4744_turbo_2026_assets"], name
        reports[name] = sha(path)
    contract = load(EVIDENCE / "contract.json")
    assert all(c["allBallsPreserved"] and c["robotUnmoved"] and c["badGroundedClears"] == 0
               for c in contract["cases"])
    assert all(c["cleared"] == (c["seeded"] if c["expectedGrounded"] else 0)
               for c in contract["cases"])
    if ROLLING:
        assert all(c["collisionPairsRestored"] and c["maximumStepDistance"] < .04
                   for c in contract["cases"])
        assert all(c["movingSamples"] > 2 and c["assistedSteps"] > 0
                   for c in contract["cases"] if c["expectedGrounded"])
        assert all(c["startedBeforeCancellation"] and c["noAssistanceAfterCancellation"]
                   for c in contract["cases"] if "during roll-out" in c["name"])
        diagnostic = load(EVIDENCE / "shooting-3m-diagnostic.json")
        assert diagnostic["passed"] and len(diagnostic["cases"]) == 3
        assert diagnostic["runtimeDllSha256"] == files["Ninjas4744.dll"]
        assert diagnostic["bundleSha256"] == files["rebuilt_ninjas_4744_turbo_2026_assets"]
        assert all(c["underbodyAssistedSteps"] == c["underbodyClearedBalls"] == c["underbodyActive"] == 0 for c in diagnostic["cases"])
        reports["shooting-3m-diagnostic.json"] = sha(EVIDENCE / "shooting-3m-diagnostic.json")
        runtime = (source / "Turbo4744UnderbodyClearance.cs").read_text(encoding="utf-8")
        import re
        assert not re.search(r"\.position\s*=|MovePosition\(|Instantiate\(", runtime), "Roll-out must not move body poses or spawn replacements"
    unsupported = 0
    clears = 0
    for name in ("traction-forward.json", "traction-reverse-pair.json"):
        for case in load(EVIDENCE / name)["cases"]:
            assert case["passed"] and case["clearanceWithoutGround"] == 0
            clears += case["clearedBalls"]
            if case["name"] == "all tires clear":
                assert case["maximumGrounded"] == 0 and case["clearedBalls"] == 0
                unsupported += 1
    assert unsupported == 4 and clears > 0
    capacity = load(EVIDENCE / "collection-retention.json")
    assert all(c["collected"] == c["offered"] == c["retainedAfterDrive"] == c["released"] == 30
               and not c["escapes"] for c in capacity["cases"])
    corner = load(EVIDENCE / "corner-pile.json")
    assert all(c["escaped"] and c["feedingObserved"] and c["capturedBeforeShoot"] > 0
               and c["capturedAfterShoot"] == 0 for c in corner["cases"])
    installed = load(EVIDENCE / "local-install.json")
    native = load(EVIDENCE / "native-startup.json")
    assert installed["files"] == files and installed["native_runtime_verified"]
    assert native["runtimeModule"] == installed["runtime_module"]
    assert max(f["clearanceGroundedWheels"] for f in native["frames"]) > 0
    if ROLLING:
        assert all("activeClearances" in f and "ignoredClearancePairs" in f and "assistedClearanceSteps" in f for f in native["frames"])
    reports.update({name: sha(EVIDENCE / name) for name in ("local-install.json", "native-startup.json")})
    source = ROOT / "Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744"
    return {"passed": True, "private_trial": True, "version": "1.0.2",
            "files": files, "evidence_sha256": reports,
            "source_sha256": {p.name: sha(p) for p in sorted(source.glob("*.cs"))},
            "motion_continuity": {"maximum_step_metres": max(c["maximumStepDistance"] for c in contract["cases"]),
                                  "maximum_observed_escape_seconds": max(c["maximumEscapeSeconds"] for c in contract["cases"]),
                                  "all_changed_pairs_restored": all(c["collisionPairsRestored"] for c in contract["cases"])} if ROLLING else None,
            "ground_contract_cases": EXPECTED_CONTRACT,
            "clearance_behavior": "continuous force/torque roll-out" if ROLLING else "instant relocation", "free_body_cases": 26,
            "fully_unsupported_controls": unsupported,
            "free_body_clearances": clears,
            "shooting_launched": sum(c["launched"] for c in load(EVIDENCE / "shooting.json")["cases"]),
            "shooting_scored": sum(c["scored"] for c in load(EVIDENCE / "shooting.json")["cases"]),
            "controls_checks": 58, "collection_retention_release_balls": 60, "corner_pile_cases": 2,
            "native_module_loaded": installed["runtime_module"],
            "native_feedback": installed.get("owner_feedback", "Pending"),
            "native_scope": installed["native_scope"],
            "limits": ["No assistance when all tires are clear of field surfaces",
                       "Retains and retries a ball when no safe nearby floor destination is available"]}


def main():
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true")
    parser.add_argument("--rolling", action="store_true", help="Validate the continuous roll-out trial instead of the historical instant-clearance trial")
    args = parser.parse_args()
    global ROLLING, EXPECTED_CONTRACT, EVIDENCE, ARTIFACTS, LABEL
    if args.rolling:
        ROLLING = True
        EXPECTED_CONTRACT = 15
        EVIDENCE = ROOT / "Reports/Rollout-20261124"
        ARTIFACTS = ROOT / "Artifacts/Rollout-20261124"
        LABEL = "rolling-clearance-trial"
    certificate = verify()
    if args.check_only:
        print(json.dumps(certificate, indent=2))
        return
    certificate_path = EVIDENCE / "verification.json"
    certificate_path.write_text(json.dumps(certificate, indent=2) + "\n", encoding="utf-8", newline="\n")
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    prefix = "Ninjas4744-Turbo2026/"
    payloads = {prefix + "standalonewindows64/" + p.name: p.read_bytes()
                for p in PLATFORM.iterdir() if p.is_file()}
    payloads[prefix + "BUILD-INFO.json"] = (json.dumps({**certificate, "source_commit": commit}, indent=2) + "\n").encode()
    payloads[prefix + "BUILD-NOTES.md"] = (EVIDENCE / "README.md").read_bytes()
    payloads[prefix + "INSTALL-he.md"] = (ROOT / "Docs/Installation-Discord-he.md").read_bytes()
    for name in certificate["evidence_sha256"]:
        payloads[prefix + "Verification/" + name] = (EVIDENCE / name).read_bytes()
    output = ROOT / f"dist/Ninjas4744-Turbo2026-1.0.2-{LABEL}-Windows.zip"
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(payloads.items()):
            archive.writestr(name, data)
    with zipfile.ZipFile(output) as archive:
        assert archive.testzip() is None and set(archive.namelist()) == set(payloads)
        assert all(archive.read(name) == data for name, data in payloads.items())
    receipt = {"archive": str(output.relative_to(ROOT)), "sha256": sha(output),
               "source_commit": commit, "files": certificate["files"]}
    (ARTIFACTS / "package.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(output)
    print(receipt["sha256"])


if __name__ == "__main__":
    main()
