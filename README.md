# WeldingBot — Shipyard Welding Robot Simulator (Unity 6, URP)

An inverted 6-axis welding robot hangs under a portal gantry and welds the joints between steel plates
(fillet and butt, at any angle). Seams are extracted automatically from plate geometry, the gantry station
for each seam is planned automatically, and welded beads are coloured (heat / position / joint type).

- Open `Assets/WeldingBot/Scenes/WeldingBot.unity` and press Play.
- Left panel: job, start/pause/reset, simulation speed (1-500x), bead colour mode, camera, summary, seam list, Korean/English.
- Camera: left drag rotate, right drag pan, wheel zoom.

Build steps, CLI commands and limits: [Docs/BuildFromScratch.md](Docs/BuildFromScratch.md) (Korean).
