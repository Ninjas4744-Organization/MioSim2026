using System.Collections.Generic;
using Games.Rebuilt.GamePieceSystem;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>One-way contact approximation for the intake throat and tray.
    /// Only this robot's entry colliders change collision pairs. Fuel is
    /// never parented, repositioned, frozen, or assigned a storage velocity.</summary>
    public sealed class Turbo4744FuelKeeper : MonoBehaviour
    {
        public Turbo4744 robot;
        public BoxCollider floor,
            fixedFloor,
            inlet,
            frontPanel;

        private sealed class Fuel
        {
            public Collider[] colliders;
            public bool captured,
                intaken,
                floorCleared,
                entering,
                floorIgnored,
                gatesIgnored;
        }

        private readonly Dictionary<Rigidbody, Fuel> tracked = new Dictionary<Rigidbody, Fuel>();
        private readonly List<Rigidbody> remove = new List<Rigidbody>();
        private readonly Collider[] overlaps = new Collider[256];
        private int fuelMask;

        public bool IsCaptured(Rigidbody body) =>
            tracked.TryGetValue(body, out var fuel) && fuel.captured;

        public bool CameThroughIntake(Rigidbody body) =>
            tracked.TryGetValue(body, out var fuel) && fuel.intaken;

        private void Awake() =>
            fuelMask = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");

        private void FixedUpdate()
        {
            if (!robot || !floor || !inlet)
                return;
            bool enabled =
                MoSimCore.BaseClasses.GameManagement.BaseGameManager.Instance
                && MoSimCore.BaseClasses.GameManagement.BaseGameManager.Instance.RobotState
                    != MoSimCore.Enums.RobotState.Disabled;
            bool reverse = enabled && robot.Reversing;
            bool collecting = enabled && robot.Deployed && (robot.Collecting || robot.Feeding);
            int count = Physics.OverlapBoxNonAlloc(
                robot.transform.TransformPoint(new Vector3(0, .28f, .34f)),
                new Vector3(.43f, .36f, .64f),
                overlaps,
                robot.transform.rotation,
                fuelMask,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < count; i++)
            {
                var body = overlaps[i].attachedRigidbody;
                if (
                    !body
                    || tracked.ContainsKey(body)
                    || !body.GetComponent<RebuiltGamePieceController>()
                )
                    continue;
                tracked.Add(
                    body,
                    new Fuel { colliders = body.GetComponentsInChildren<Collider>(true) }
                );
            }
            float top =
                robot
                    .transform.InverseTransformPoint(floor.transform.TransformPoint(floor.center))
                    .y
                + floor.size.y * .5f;
            remove.Clear();
            foreach (var pair in tracked)
            {
                var body = pair.Key;
                var state = pair.Value;
                if (!body)
                {
                    remove.Add(body);
                    continue;
                }
                var local = robot.transform.InverseTransformPoint(body.position);
                if (
                    Mathf.Abs(local.x) > .8f
                    || local.z < -.4f
                    || local.z > 1.15f
                    || local.y < -.4f
                    || local.y > 1.1f
                )
                {
                    Ignore(state, false, false);
                    remove.Add(body);
                    continue;
                }
                float radius = .075f;
                foreach (var collider in state.colliders)
                    if (collider is SphereCollider sphere && !sphere.isTrigger)
                    {
                        var scale = sphere.transform.lossyScale;
                        radius =
                            sphere.radius
                            * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        break;
                    }
                // New entries must clear the floor; fuel already supported
                // inside can rest on it. Latch support before retaining it.
                if (
                    !state.floorCleared
                    && local.y - radius > top + (state.entering ? .002f : -.004f)
                    && Behind(frontPanel ? frontPanel : inlet, body.position, radius)
                )
                    state.floorCleared = true;
                if (
                    !reverse
                    && Mathf.Abs(local.x) < floor.size.x * .5f - .01f
                    && local.y - radius > top + (state.floorCleared ? -.004f : .002f)
                    && local.z > radius + .02f
                    && Behind(inlet, body.position, radius)
                    && (!frontPanel || Behind(frontPanel, body.position, radius))
                    && Mathf.Abs(
                        Vector3.Dot(
                            body.velocity - floor.attachedRigidbody.GetPointVelocity(body.position),
                            robot.transform.up
                        )
                    ) < .6f
                )
                    state.captured = true;
                var exit = frontPanel ? frontPanel : inlet;
                if (
                    PlaneDistance(exit, body.position) - radius > exit.size.z * .5f + .01f
                    || Mathf.Abs(local.x) > .44f
                )
                {
                    state.captured = false;
                    state.floorCleared = false;
                    state.entering = false;
                }
                if (!state.captured && collecting)
                {
                    state.entering = true;
                    state.intaken = true;
                }
                if (state.captured)
                    state.entering = false;
                // Clearing the floor is a one-way handoff. A bounce or another
                // ball pressing down must never reopen support underneath it.
                // Re-arm entry only after the whole ball leaves the hopper.
                Ignore(state, state.entering && !state.floorCleared, reverse || state.entering);
            }
            foreach (var body in remove)
                tracked.Remove(body);
        }

        private static float PlaneDistance(BoxCollider gate, Vector3 position) =>
            Vector3.Dot(
                position - gate.transform.TransformPoint(gate.center),
                gate.transform.forward
            );

        private static bool Behind(BoxCollider gate, Vector3 position, float radius) =>
            PlaneDistance(gate, position) + radius
            < -gate.size.z * Mathf.Abs(gate.transform.lossyScale.z) * .5f - .002f;

        private void Ignore(Fuel fuel, bool floorIgnored, bool gatesIgnored)
        {
            if (fuel.floorIgnored == floorIgnored && fuel.gatesIgnored == gatesIgnored)
                return;
            foreach (var collider in fuel.colliders)
            {
                if (!collider)
                    continue;
                if (floor && fuel.floorIgnored != floorIgnored)
                    Physics.IgnoreCollision(floor, collider, floorIgnored);
                if (fixedFloor && fuel.floorIgnored != floorIgnored)
                    Physics.IgnoreCollision(fixedFloor, collider, floorIgnored);
                if (inlet && fuel.gatesIgnored != gatesIgnored)
                    Physics.IgnoreCollision(inlet, collider, gatesIgnored);
                if (frontPanel && fuel.gatesIgnored != gatesIgnored)
                    Physics.IgnoreCollision(frontPanel, collider, gatesIgnored);
            }
            fuel.floorIgnored = floorIgnored;
            fuel.gatesIgnored = gatesIgnored;
        }

        private void OnDisable()
        {
            foreach (var fuel in tracked.Values)
                Ignore(fuel, false, false);
            tracked.Clear();
        }
    }
}
