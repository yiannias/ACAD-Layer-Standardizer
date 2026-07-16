# AutoACD Layer Standardizer
A tool to help AutoCAD layer cleanup

### 🌐 [Visit the project website](https://yiannias.github.io/ACAD-Layer-Standardizer/) for screenshots, a full walkthrough, and the download link.

This is a tool to read and map existing layers within an AutoCAD file to an established layer standard. In other words a "layer translator" but one with a running memory of previous mapping effort and enough logic to make suggestions.

For instance if "TEXT" is favored as a designator instead of "TXT" it will suggest this even if the initial layer was not encountered before. I guess this is "pattern matching."

## Supported AutoCAD versions

**AutoCAD 2021 and newer** (including verticals such as Civil 3D, Map 3D, Plant 3D). One installer covers all supported releases — it ships a build for each AutoCAD .NET compatibility era and AutoCAD automatically loads the right one:

| AutoCAD release | Runtime |
|---|---|
| 2021–2024 | .NET Framework 4.8 |
| 2025–2026 | .NET 8 |
| 2027 | .NET 10 |

**AutoCAD 2020 and older are not supported.**
