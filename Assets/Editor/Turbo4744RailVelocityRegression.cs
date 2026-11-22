using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ninjas4744.MoSim;
using UnityEngine;
using Object = UnityEngine.Object;

/// A rotating rigid assembly must report only the imposed relative slide speed.
public static class Turbo4744RailVelocityRegression
{
    [Serializable]
    private sealed class Sample
    {
        public float heading,
            pitchRate,
            expected,
            measured,
            legacy;
    }

    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public float maximumError,
            maximumLegacyError;
        public List<Sample> cases = new List<Sample>();
    }

    public static void Run()
    {
        var root = new GameObject("Rail velocity invariant");
        var slide = new GameObject("Offset centre of mass slide");
        try
        {
            var chassis = root.AddComponent<Rigidbody>();
            var moving = slide.AddComponent<Rigidbody>();
            chassis.useGravity = moving.useGravity = false;
            chassis.centerOfMass = new Vector3(.02f, .14f, -.03f);
            moving.centerOfMass = new Vector3(.04f, .3f, .08f);
            var method = typeof(Turbo4744).GetMethod(
                "RailSpeed",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            if (method == null)
                throw new InvalidOperationException("Production rail measurement missing.");
            var report = new Report();
            foreach (float heading in new[] { 0f, 90f, 180f, -90f })
            foreach (float pitchRate in new[] { -2f, 0f, 2f })
            foreach (float speed in new[] { -.12f, 0f, .12f })
            {
                var rotation = Quaternion.Euler(15, heading, 0);
                chassis.position = new Vector3(2, 3, 4);
                chassis.rotation = moving.rotation = rotation;
                moving.position = chassis.position + rotation * new Vector3(0, .2f, .15f);
                Physics.SyncTransforms();
                chassis.velocity = new Vector3(1.1f, .4f, -.6f);
                chassis.angularVelocity = moving.angularVelocity =
                    rotation * Vector3.right * pitchRate;
                var axis = rotation * Vector3.forward;
                moving.velocity = chassis.GetPointVelocity(moving.worldCenterOfMass) + axis * speed;
                float measured = (float)method.Invoke(null, new object[] { moving, chassis });
                float legacy = Vector3.Dot(
                    moving.velocity - chassis.GetPointVelocity(moving.position),
                    axis
                );
                report.cases.Add(
                    new Sample
                    {
                        heading = heading,
                        pitchRate = pitchRate,
                        expected = speed,
                        measured = measured,
                        legacy = legacy,
                    }
                );
                report.maximumError = Mathf.Max(report.maximumError, Mathf.Abs(measured - speed));
                report.maximumLegacyError = Mathf.Max(
                    report.maximumLegacyError,
                    Mathf.Abs(legacy - speed)
                );
            }
            report.passed =
                report.cases.Count == 36
                && report.maximumError < .00001f
                && report.maximumLegacyError > .5f;
            string path = Path.GetFullPath(
                Application.dataPath + "/../BuildArtifacts/rail-velocity-invariant.json"
            );
            File.WriteAllText(path, JsonUtility.ToJson(report, true) + "\n");
            if (!report.passed)
                throw new InvalidOperationException(
                    "Rail velocity invariant failed: " + JsonUtility.ToJson(report)
                );
            Debug.Log(
                "TURBO_RAIL_VELOCITY_PASSED "
                    + report.cases.Count
                    + " cases; error "
                    + report.maximumError
                    + "; old error "
                    + report.maximumLegacyError
            );
        }
        finally
        {
            Object.DestroyImmediate(slide);
            Object.DestroyImmediate(root);
        }
    }
}
