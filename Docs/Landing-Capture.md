# Recording a stuck landing

This Windows 1.0.1 build adds optional local diagnostics. The landing issue is still under investigation; this is not a physics fix or a Steam release.

Install the mod normally and restart MoSim. Recording is off by default in the ZIP. On the development computer it has already been enabled.

To enable it on another Windows computer, create an empty file named `Turbo4744-capture-landings.enabled` inside:

```text
%USERPROFILE%\AppData\LocalLow\CascadeStudios\MoSim\
```

The marker belongs next to the `Mods` folder, not inside the mod. Restart MoSim, play normally and, when stuck, keep trying to drive for about two seconds. The recorder does not take over the controls. It saves up to two stall reports per robot spawn in:

```text
%USERPROFILE%\AppData\LocalLow\CascadeStudios\MoSim\Turbo4744-LandingCaptures\
```

Reports contain robot motion, input, wheel support and underside collisions. Nothing is uploaded. A recorded stall can also be ordinary contact with a wall; the surrounding frames help distinguish it from beaching.

Remove the marker file and restart MoSim to turn recording off. The robot's forces, fuel compression, tire/suspension settings, collision shapes and pumping behavior are unchanged by recording.
