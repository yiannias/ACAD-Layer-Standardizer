# LayerMegaMind
A tool to help AutoCAD layer cleanup

This is a tool to read and map existing layers within an AutoCAD file to an established layer standard. In other words a "layer translator" but one with a running memory of previous mapping effort and enough logic to make suggestions.

For instance if "TEXT" is favored as a designator instead of "TXT" it will suggest this even if the initial layer was not encountered before. I guess this is "pattern matching."
