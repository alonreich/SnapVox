# SnapVox reliability fixes — resume here

Scope: the eight reviewed issues plus (9) preserving administrator startup during upgrades and (10) startup on battery power. The conversation specifies 10 issues, despite mentioning 12.

Existing workspace edits predate this task; preserve them. No commits, installs, uninstall runs, or changes to the host's scheduled tasks are needed for validation.

## Implementation checklist

- [x] 1. Remove cleanup patterns that delete user exports or unrelated temporary files.
- [x] 2. Fail export safely when annotation rendering fails; never substitute the original.
- [x] 3. Make highlighting preserve the visible content underneath redactions.
- [x] 4. Eliminate process-name-based forced termination.
- [x] 5. Await current pixelation rendering before export and stop on rendering failure.
- [x] 6. Propagate clipboard failures; keep editor open and show failure.
- [x] 7. Ask upgrade choice before touching running apps; require users to save/exit before continuing, without force killing.
- [x] 8. Verify settings backup/restore and retain recovery copies on any upgrade failure.
- [x] 9. Restore administrator startup task after restoring kept settings.
- [x] 10. Explicitly allow scheduled startup and continued running on battery; ordinary startup remains a user Run entry.
- [x] Add regression tests and run both OCR configurations.
- [ ] Produce and validate both packaged installers if the local toolchain permits.

## Starting evidence

The previous turn was review only. The reported unsafe code is still present. Baseline: 172 tests passed in each OCR configuration. No fixes from this request were already implemented.

## Resume notes

Start by reading this file and inspecting the current diff; do not repeat the original audit. Update this checklist after each implementation/validation batch.

## Implemented; validation in progress

All 10 implementation paths changed. Highlights are translucent overlays; pending pixelation is opaque black and export awaits its latest task. Failed render/clipboard transfer throws. Native clipboard now uses an owned message-only window. Installer never kills apps, prompts Retry/Cancel after upgrade choice, backs up verified settings in LocalAppData/SnapVoxUpgradeBackups, restores startup after settings, retains recovery on failure. Scheduled task XML explicitly permits battery operation and unlimited runtime. Ordinary startup uses the Run key. Shared installer semaphore replaces thread-affine mutex across awaits. Payload extraction failures now abort installation.

First build succeeded. 171/172 old tests passed; the old clipboard test relied on swallowed failure and is being replaced with deterministic tests. New UpgradeSafetyTests added; headless editor tests next. Do not rerun installed app/installer on this host. Final dual-branch tests and packaging remain.

Technical references: Microsoft Task Scheduler schema (DisallowStartIfOnBatteries and StopIfGoingOnBatteries); Avalonia 11 headless XUnit/Skia documentation.

## Validation checkpoint

- Windows OCR: 189/189 tests passed, TreatWarningsAsErrors=true.
- Tesseract: 189/189 tests passed, TreatWarningsAsErrors=true.
- Real headless Skia rendering tested for highlights over blackouts, render failure, pending/replaced/failed pixelation. Backup tests cover exact restore, access failures, corrupt copies, and retry after failed restoration. Clipboard tests now avoid touching the OS clipboard.
- Windows Task Scheduler accepted generated XML for both run levels through TASK_VALIDATE_ONLY (flag 1). No task was registered or changed. Actual reboot/battery hardware testing has not been performed.
- Safe isolated packaging script: obj/reliability-validation/Build.ps1. It does not call the existing dev_build.cmd, which force-kills applications. The script publishes both payloads and Native AOT wrappers to fresh directories, then copies final installers into compiled/. It logs builds to build.log. Currently running in exec session 49492 (Native payload generating native code). If the session is gone, inspect build.log, compiled file timestamps, and obj/reliability-validation publish directories before rebuilding.
