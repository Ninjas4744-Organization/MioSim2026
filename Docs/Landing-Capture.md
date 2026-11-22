# Recording a stuck landing

Windows 1.0.1 includes optional local contact diagnostics. Recording itself is passive and does not change robot physics. See the current verification report for the status of the contact correction and Steam release.

Install the mod normally and restart MoSim. Recording is off by default in the ZIP.

To enable it on another Windows computer, create an empty file named `Turbo4744-capture-landings.enabled` inside:

```text
%USERPROFILE%\AppData\LocalLow\CascadeStudios\MoSim\
```

The marker belongs next to the `Mods` folder, not inside the mod. Restart MoSim and play normally. When the robot struggles, press **F9** to save the recent contact history immediately. You can release the drive controls first. This also captures creeping or turning cases that do not meet the automatic stall threshold. The recorder does not take over the controls.

The automatic trigger still saves up to two sustained stopped-drive reports per spawn. Manual captures remain available afterward, including while disabled. Reports are saved in:

```text
%USERPROFILE%\AppData\LocalLow\CascadeStudios\MoSim\Turbo4744-LandingCaptures\
```

Reports contain robot pose/motion, input, each wheel's contact load, physical tire and probe positions, tire clearance, and underside collisions. Native reports use an explicit JSON writer because the player's Unity serializer omitted the frame list. Nothing is uploaded. A recorded stall can also be ordinary contact with a wall; the surrounding frames help distinguish it from beaching.

For local tooling, updating `Turbo4744-capture-now.txt` next to the marker requests an immediate snapshot without a key press. The recorder checks it twice per second while opted in. Remove that request file after testing.

Remove the marker file and restart MoSim to turn recording off. The robot's forces, fuel compression, tire/suspension settings, collision shapes and pumping behavior are unchanged by recording.
