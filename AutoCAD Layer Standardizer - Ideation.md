#### File 2: `AutoCAD Layer Standardizer - Ideation.md`
```markdown
# AutoCAD Layer Standardizer Plugin: Complete Project Ideation & Conversation Log
**Date:** July 2026
**Target System:** AutoCAD .NET API (C#) & WPF Node UI

---

## 1. Initial Inquiry & API Evaluation
### User Prompt:
> I want to code a plug-in or add-in to AutoCAD to intelligently review and combine/rename layers according to some defined standards. What avenue should I pursue in regard to interfacing with AutoCAD for that action?

### Summary of Options Considered:
1. **The Winner: .NET API (C# or VB.NET)**
   - *Pros:* Direct database access (ObjectDBX), runs "in-process" at lightning speeds, handles massive layers seamlessly, easily natively hosts WPF interfaces for complex node visualizations.
   - *Tools:* Visual Studio, Autodesk ObjectARX SDK (`AcDbMgd.dll`, `AcCoreMgd.dll`, `AcCoreMgd.dll`).
2. **The Runner-Up: Python (via pyautocad / COM)**
   - *Pros:* Fast for scripting, easy data parsing.
   - *Cons:* Runs "out-of-process", making iteration through thousands of drawing entities or complex database changes noticeably slower. Lacks robust native UI integration within AutoCAD's process space.
3. **The Traditional Route: AutoLISP / Visual LISP**
   - *Pros:* Light, zero-setup required, instant execution.
   - *Cons:* Extremely difficult to build modern visual user interfaces (restricted to old DCL dialog blocks), hard to scale for modular codebases.
4. **The Cloud Route: Autodesk Platform Services (APS / Forge)**
   - *Pros:* Perfect for server-side headless automation.
   - *Cons:* Requires a cloud ecosystem and ongoing subscription costs; less ideal for real-time interactive user interfaces within the desktop app.

---

## 2. Refining the Source of Truth (The Template DWG Approach)
### User Insight:
> It is a specific standard, but one that mostly lives as an AutoCAD DWG file. I think that the layer name and properties could be extracted and made into an excel spreadsheet. The excel conversion is a bit of a dead end because future revisions to the standard would most likely be made by architects editing the "template" dwg file (to add new "standard" layers or to modify exiting ones.

### Key Conceptual Architectural Breakout:
- **The "Side-Database" Pattern (`Database.ReadDwgFile`):** Instead of making users open or parse an unstable intermediary file like Excel, the .NET framework allows the plugin to quietly load the master DWG file directly into the computer's memory out-of-view.
- **Benefits:** Keeps the CAD manager/architect standard workflows native. If they modify a layer color, description, or linetype in the master template, the plugin instantly updates its evaluation rules during the next execution.
- **Network Synchronization:** By saving the template on a corporate shared drive (`X:\`) or a cloud-synced directory (OneDrive/SharePoint/BIM 360), the deployment updates automatically without re-compiling code.

---

## 3. Designing the Intelligent Memory Matrix & Node Graph UI
### User Direction:
> The main mechanic would be to ask the user in some way and then remember how they responded. That "memory" would then be used for the next file that the "standardizer" is run on. It feels like the initial pass would be for the tool to make automatic matches, then identify matches that match certain strings or heuristics... All of this would be a single side-by-side list. The program would write the selections to a "memory file" for suture reference... A sexy panning graph/node interface would be awesome.
> 
> As far as navigating the node graph, AutoCAD already relies on zooming and panning using very specific mouse actions: (pressed middle mouse button "pans" and scroll wheel zooms in and out) so we would use this convention as well and not be too concerned about a super-tall graph since the user should be able to navigate it with ease.

### Crucial Interface & Data Mechanics Decided:
1. **The 3-Tiered Matching Engine Pipeline:**
   - **Stage 1 (Exact/Memory Match):** Translates incoming layers based on identical names or existing rules explicitly logged in the JSON history. (Rendered as **Green** solid connection lines).
   - **Stage 2 (Heuristics/Fuzzy Match):** Leverages algorithms like Levenshtein Distance or string containment metrics to find close matches (e.g., `L-WLL` vs `L-WALL`). (Rendered as **Yellow** dashed connection lines requiring confirmation).
   - **Stage 3 (Unmatched Input):** Outlier layers with no history or clear similarities are displayed completely disconnected, prompting manual drag-and-drop routing.
2. **The High-Capacity JSON Memory File:**
   - Designed to hold thousands of mappings in a light format.
   - Built to be easily transferrable (via email or a common network file server) so team members can effortlessly import, merge, and enrich their mapping knowledge bases over time.
3. **The AutoCAD-Bound UX Architecture:**
   - Overriding standard WPF canvas event triggers to lock down standard CAD muscle memory:
     - *Scroll Wheel:* Controls dynamic zoom scaling anchored perfectly on the current coordinate location of the user mouse cursor.
     - *Middle-Mouse Button Hold & Drag:* Alters transformation translation fields to execute fluid 2D canvas panning.
   - Performance tuning via visual layout simplicity ensures massive multi-layer drawings navigate smoothly at a locked 60 FPS frame rate.
