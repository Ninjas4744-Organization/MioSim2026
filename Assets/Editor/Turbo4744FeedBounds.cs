using UnityEngine;

/// <summary>Additional storage regions introduced by the CAD indexer and bottom-entry shooter.
/// Ground fuel is below these volumes; normal escape checks and count thresholds still apply.</summary>
internal static class Turbo4744FeedBounds
{
    public static bool Contains(Vector3 p) =>
        // Lower roller axes and their landing are below the former rising ramp.
        (Mathf.Abs(p.x) < .31f && p.y > .17f && p.y < .52f && p.z > -.05f && p.z < .34f)
        // Fuel may wait inside the narrow accelerator behind the hopper while stopped.
        || (
            Mathf.Abs(p.x - .0131f) < .105f
            && p.y > .14f
            && p.y < .58f
            && p.z > -.20f
            && p.z <= -.05f
        );
}
