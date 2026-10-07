# Beta 1.3.0

This release makes the mapping window follow your work in AutoCAD, and protects unapplied mappings from being lost.

**The mapping window follows the active drawing**
- Switch drawings in AutoCAD and the Source side updates within about a second; the Target (standard) side stays put.
- Add, rename or delete layers in AutoCAD and the Source side keeps up.
- Each drawing keeps its own unapplied connections, so you can switch away and back without losing them. The footer tells you when another drawing has unapplied connections.
- Only one mapping window opens per AutoCAD session; typing `LSTDR` again brings the open one to the front.

**Unapplied connections are protected**
- Closing a drawing, quitting AutoCAD, or closing the mapping window while connections are unapplied now asks: Apply, Discard or Cancel.
- Closing the mapping window while another drawing still has unapplied connections warns you too.
- Nothing is saved across an AutoCAD restart: you are asked first, and can apply or discard.

**Node mode**
- A new slider in the footer sets how many columns the layers are spread over (Auto, or 1 to 8). The longer list sets the column height and the shorter list only wraps when it is taller.
- Node columns wrap at 25 layers by default, and lines that pass under other columns are drawn darker.
- The filter boxes are back under each panel's title and scale with the nodes.

**Smaller changes**
- Trying to map layer 0 onto another layer (AutoCAD never allows this) now gives a plain message and changes nothing.
- A `.dws` file can be chosen as the standard.
- A failed Rust window launch now falls back to the older editor and reports the error once.
- The window repaints as soon as AutoCAD replies, and errors appear in a dialog.
- The mapping window now reads the layer dictionary, translation memory and settings itself. The layer dictionary file is read with any capitalisation of its keys and a leading byte-order mark, and remembered mappings match accented layer names regardless of case.
- Translation memory backups are named like the older ones and only the newest 10 are kept.

The installer includes the AutoCAD plug-in and Rust mapping editor for AutoCAD 2021–2027.
