
```markdown
# AutoCAD Layer Standardizer Plugin: Architectural Brief & Implementation Plan
**Author:** Gemini Framework Design
**Target Framework:** AutoCAD .NET API (C#)
**Presentation System:** Windows Presentation Foundation (WPF) with Custom Canvas Navigation

---

## Part 1: Architectural Brief

### 1.1 Model-View-ViewModel (MVVM) Separation
The application relies on an isolated MVVM design pattern to ensure database stability within AutoCAD's execution threads while driving a highly dynamic, vector-rendered user interface.

* **Model (Data Access):**
  - Manages database objects via standard transactions.
  - Implements the silent background database (`Database.ReadDwgFile`) for retrieving the destination target blueprint.
  - Serializes and deserializes the structural JSON translation tracking document.
* **ViewModel (Presentation Logic):**
  - Manages collection synchronization (`ObservableCollection`).
  - Converts active drawing state variations into graph components (Nodes and Connectors/Edges).
  - Implements matching scores to categorize layer status groupings.
* **View (User Interface Canvas):**
  - High-performance WPF User Control hosted inside a native AutoCAD `PaletteSet`.
  - Captures low-level mouse inputs to feed transformation matrices.

### 1.2 Data Schema Specification (`standards_memory.json`)
The translation history structure requires strict, flat 1-to-1 key/value pairings for rapid map processing times.

```json
{
  "SchemaVersion": "1.0",
  "LastModified": "2026-07-08T12:00:00Z",
  "UserIdentity": "CAD_Manager_01",
  "Mappings": {
    "L-WAL-OLD": "L-WALL",
    "ANNO_TXT": "A-ANNO-TEXT",
    "E-LIGHT-BAD": "E-LITE",
    "EXISTING-PROP-LINE": "V-PROP-LINE"
  }
}

```

### 1.3 Key API Classes & Dependencies

* `Autodesk.AutoCAD.DatabaseServices.Database` — To process drawing databases.
* `Autodesk.AutoCAD.DatabaseServices.LayerTableRecord` — To alter layer configurations.
* `Autodesk.AutoCAD.DatabaseServices.Transaction` — Controls atomic transactional boundaries.
* `System.Text.Json` — For lightweight rule matrix file serialization.
* `System.Windows.Media.MatrixTransform` — Computes canvas coordinate scaling and translation updates dynamically.

---

## Part 2: Detailed Implementation Plan

### Sprint 1: Headless Data & Database Access Core

* **Objective:** Establish silent database operations and local JSON translation data processing.
* **Key Tasks:**
1. Initialize a C# Class Library project targeting your organization's designated AutoCAD framework iteration.
2. Embed references to core Autodesk binaries (`acdbmgd.dll`, `acmgd.dll`, `accoremgd.dll`).
3. Code a robust database manager class capable of executing memory-bound reads of an architect's standard template file via `ReadDwgFile()`.
4. Build a dedicated local utility file class handling the file reads, updates, and validation merges of the user's `standards_memory.json` matrix.



### Sprint 2: 3-Tier Matching Engine Development

* **Objective:** Engineer the algorithms translating unstandardized active layers into target layers.
* **Key Tasks:**
1. **Stage 1 Implementation:** Look up incoming names against the loaded JSON translation dictionary for immediate identification.
2. **Stage 2 Implementation:** Build a text comparison framework (e.g., Levenshtein Distance formula scoring) that identifies and suggests close spelling variants.
3. **Stage 3 Implementation:** Group all remaining unrecognized drawing layers into an unmapped tracking array.
4. Create a test command routing matching evaluations straight to the internal AutoCAD text log terminal to verify indexing speed and accurate categorizations.



### Sprint 3: WPF Presentation & CAD Muscle-Memory Controls

* **Objective:** Build the visual layout container and map canvas controls to standard AutoCAD gestures.
* **Key Tasks:**
1. Build a streamlined split-screen WPF view. Left panel: status filter navigation buttons. Right panel: a vector-rendering `Canvas`.
2. Implement a `MatrixTransform` wrapper tracking your viewport layout parameters.
3. Connect the canvas `MouseWheel` window trigger to adjust matrix magnification increments anchored directly over the absolute mouse coordinate vector.
4. Intercept `MouseDown` and `MouseMove` hooks to handle **Middle-Mouse Button clicks**, dynamically modifying layout positioning values for smooth 2D panning.



### Sprint 4: Node Connectivity & Final Transaction Execution

* **Objective:** Connect nodes visually via drag-and-drop actions and apply batch structural modifications to drawings.
* **Key Tasks:**
1. Map layer data groups to visible workspace nodes. Implement clickable connecting line paths using Bezier curve calculations.
2. Code colour indicators mapping rule sources (Solid Green for direct matches, Dashed Yellow for fuzzy logic selections).
3. Wire an execution confirmation element that gathers the layout link associations, pipes new user mappings back into the JSON file, and launches the active file cleaner transaction.
4. Make the active file cleaner loop through drawing elements (`Entity`), convert asset assignments over to the verified target layer name, and call a clean drawing database database `Purge` on the abandoned source layer items.



### Sprint 5: Team Distribution & File Sync Tuning

* **Objective:** Prepare the system for deployment across large teams.
* **Key Tasks:**
1. Add simple UI import/export controls allowing users to share, combine, and update their mapping databases.
2. Embed robust safety systems managing challenging file properties (frozen elements, locked layers, complex XRefs).
3. Compile your code down into a distributed `.dll` binary format or wrap your solution into an automated Autodesk `.bundle` installation framework for seamless multi-user setup.



```

***
