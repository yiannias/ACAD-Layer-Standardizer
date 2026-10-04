# RustUI column layout feedback

The screenshot marks two layout defects in RustUI Column mode. The Target Filter panel must not overlap target layer rows; the visible target pane should reserve horizontal space based on the filter panel's current resizable width. At the bottom, the list frame should end at a complete row boundary so it does not clip a layer and leave an unexplained strip of empty space beneath it.

The follow-up clarification identifies the lower issue as the frame cutting off a node while leaving extra whitespace. Apply the correction to RustUI only.
