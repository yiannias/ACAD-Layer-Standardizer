# Alpha 4 release notes (draft)

Draft for the `gh release create alpha-4` notes — keep the supported-versions
section verbatim; it is the release's headline feature.

---

Fourth installable release of ACAD Layer Standardizer.

## Supported AutoCAD versions

**This release supports AutoCAD 2021 and newer only.** AutoCAD 2020 and older
are **not supported** and the plugin will not load on them.

One installer covers every supported release. It ships a separate build for
each AutoCAD .NET compatibility era, and AutoCAD's Autoloader picks the right
one automatically:

| AutoCAD release | Runtime |
|---|---|
| 2021–2024 | .NET Framework 4.8 |
| 2025–2026 | .NET 8 |
| 2027 | .NET 10 |

Verticals built on these releases (Civil 3D, Map 3D, MEP, Electrical,
Plant 3D, etc.) are included.

## What's new since Alpha 3

- **Multi-version installer**: one installer/bundle now targets AutoCAD
  2021–2027 (previously 2026/2027 only), with per-era payloads selected
  automatically at load time
- Build system reworked: reference assemblies now come from Autodesk's
  official AutoCAD.NET NuGet packages, so building no longer requires an
  installed AutoCAD
- Unit tests now run on all three target runtimes (.NET Framework 4.8,
  .NET 8, .NET 10)

Private repo, private release — for internal testing.

**Testing status**: verified on AutoCAD <fill in the versions actually
tested before publishing>. Other supported releases are expected to work via
Autodesk's binary-compatibility guarantee but have not been individually
exercised.
