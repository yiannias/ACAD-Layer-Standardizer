# Rust UI opens without drawing layer snapshot

The user-provided AutoCAD capture shows the Rust mapping window with empty Source and Target columns, “No standard selected,” a status of “Connecting to AutoCAD…,” and a footer count of “0 source layers.” This confirms the UI launched but did not receive its drawing snapshot. The required behavior is to populate source layer names from the active AutoCAD drawing and load available standard layers through the existing bridge/configuration.
