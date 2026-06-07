# Native AOT & Trimming Suppressions

This document tracks and justifies IL/AOT suppressions in SnapVox v3.0.

## Suppressed Warnings

### IL2050 (COM Interop)
- **Status:** Suppressed
- **Justification:** Direct Win32 P/Invokes for GDI/User32 are essential for performance and screen capture. Trimmer cannot verify COM/PInvoke stability but these are well-tested rooted paths.

### IL2104 (Static interface methods)
- **Status:** Suppressed
- **Justification:** Used in Avalonia internal shims. Non-critical for app logic.

### IL3000 / IL3002 (Single-file assembly location)
- **Status:** Suppressed
- **Justification:** The app uses `AppDomain.BaseDirectory` which is stable in Native AOT single-file mode.

### IL2026 / IL3050 (Reflection / Dynamic Code)
- **Status:** Partially Suppressed
- **Justification:** 
  - `IniValue.cs`: Uses reflection to map INI properties to `CoreConfiguration`. Members are rooted via `[DynamicallyAccessedMembers]`.
  - `SimpleServiceProvider`: Uses generic registration. 

## Smoke Test Strategy
To ensure these suppressions don't hide runtime crashes, the following smoke tests must be run on the **Published EXE**:
1. OCR Extraction (Tests reflection over providers).
2. Configuration Save/Load (Tests reflection over IniSections).
3. Clipboard Injection (Tests COM/Native interop).
