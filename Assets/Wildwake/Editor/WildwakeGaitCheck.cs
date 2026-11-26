using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Wildwake;

[InitializeOnLoad]
internal static class WildwakeGaitCheck
{
    private const string Flag = "Temp/Wildwake-GaitCheck.flag";
    private const string Output = "Temp/Wildwake-GaitCheck.txt";
    private static readonly List<string> Lines = new();
    private static readonly List<float> Forefoot = new();
    private static readonly List<float> PitchLift = new();
    private static WildwakePlayer player;
    private static Animator animator;
    private static SkinnedMeshRenderer boots;
    private static Mesh sample;
    private static Keyboard keyboard;
    private static bool[] rightSide;
    private static float startedAt;
    private static float lastSampleAt = -1f;
    private static float maxSwing;
    private static float minimum = float.PositiveInfinity;
    private static float jumpHeight;
    private static int stage;
    private static WildwakeGaitProbe probe;
    private static int traces;
    private static Transform[] referenceAnkles;

    static WildwakeGaitCheck()
    {
        if (File.Exists(Flag))
            EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (File.Exists(Output))
        {
            EditorApplication.update -= Tick;
            return;
        }
        try
        {
            if (!EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = true;
                return;
            }
            if (!player)
            {
                if (Time.time < .26f) return;
                player = UnityEngine.Object.FindAnyObjectByType<WildwakePlayer>();
                if (!player) return;
                animator = player.GetComponent<Animator>();
                probe = player.gameObject.AddComponent<WildwakeGaitProbe>();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Wildwake/Art/Character/base-model.fbx");
                referenceAnkles = new[] { "LeftFoot", "RightFoot" }.Select(name =>
                    model.GetComponentsInChildren<Transform>(true).Single(node => node.name.EndsWith(name))).ToArray();
                var enemy = UnityEngine.Object.FindAnyObjectByType<WildwakeEnemy>();
                if (enemy)
                {
                    enemy.enabled = false;
                    foreach (var collider in enemy.GetComponents<Collider>()) collider.enabled = false;
                }
                boots = player.GetComponentsInChildren<SkinnedMeshRenderer>().Single(renderer => renderer.name == "boots");
                var bones = boots.bones;
                rightSide = boots.sharedMesh.boneWeights.Select(weight => bones[weight.boneIndex0].name.Contains("Right")).ToArray();
                sample = new Mesh();
                keyboard = InputSystem.AddDevice<Keyboard>();
                Place(new Vector3(-1f, .02f, -8f));
                Keys(Key.W);
            }

            float elapsed = Time.time - startedAt;
            if (stage is 0 or 1 or 4 or 5 && elapsed > .32f && Time.time > lastSampleAt + .013f)
            {
                lastSampleAt = Time.time;
                boots.BakeMesh(sample, true);
                var world = sample.vertices.Select(point => boots.transform.TransformPoint(point)).ToArray();
                Measure(world, false);
                Measure(world, true);
            }

            switch (stage)
            {
                case 0 when elapsed >= 1.66f:
                    EndGait("Unarmed walk"); Place(new Vector3(-1f, .02f, -8f));
                    stage++; Keys(Key.W, Key.LeftShift); break;
                case 1 when elapsed >= 1.66f:
                    EndGait("Unarmed run");
                    var weapon = GameObject.Find("Pickup Weapon").transform;
                    Place(new Vector3(weapon.position.x - .75f, .02f, weapon.position.z));
                    stage++; Keys(); break;
                case 2 when elapsed >= .95f:
                    stage++; Keys(Key.F); break;
                case 3 when elapsed >= 1.24f:
                    if (!player.HasWeapon) throw new InvalidOperationException("Pickup failed.");
                    Place(new Vector3(-1f, .02f, -8f));
                    stage++; Keys(Key.W); break;
                case 4 when elapsed >= 1.66f:
                    EndGait("Armed walk"); Place(new Vector3(-1f, .02f, -8f));
                    stage++; Keys(Key.W, Key.LeftShift); break;
                case 5 when elapsed >= 1.66f:
                    EndGait("Armed run"); stage++; Keys(Key.Space); break;
                case 6:
                    jumpHeight = Mathf.Max(jumpHeight, player.Body.position.y);
                    if (elapsed > .72f) { Lines.Add($"Jump apex {jumpHeight:F3}m, landed {animator.GetBool("Grounded")}."); Finish(); }
                    break;
            }
        }
        catch (Exception error) { Lines.Add(error.ToString()); Finish(); }
    }

    private static void Measure(Vector3[] points, bool right)
    {
        var ankle = animator.GetBoneTransform(right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot);
        var toes = animator.GetBoneTransform(right ? HumanBodyBones.RightToes : HumanBodyBones.LeftToes);
        var heading = toes ? Vector3.ProjectOnPlane(toes.position - ankle.position, Vector3.up).normalized : player.Body.forward;
        var list = points.Select((point, index) => (point, index))
            .Where(item => rightSide[item.index] == right)
            .Select(item => (item.point, along: Vector3.Dot(item.point - ankle.position, heading))).ToArray();
        float low = list.Min(item => item.along);
        float high = list.Max(item => item.along);
        var heel = list.Where(item => item.along < Mathf.Lerp(low, high, .35f)).OrderBy(item => item.point.y).First().point;
        var toe = list.Where(item => item.along > Mathf.Lerp(low, high, .66f)).OrderBy(item => item.point.y).First().point;
        float h = heel.y;
        float t = toe.y;
        maxSwing = Mathf.Max(maxSwing, Mathf.Min(h, t));
        minimum = Mathf.Min(minimum, Mathf.Min(h, t));
        if (h <= .042f && h >= -.045f && t >= h - .02f)
        {
            Forefoot.Add(t);
            PitchLift.Add(t - h);
            if (stage == 0 && t > .065f && traces++ < 16)
            {
                var raw = probe.Gaps[right ? 1 : 0];
                var reference = referenceAnkles[right ? 1 : 0];
                Vector3 ankleUp = ankle.TransformDirection(reference.InverseTransformDirection(Vector3.up));
                var toeBone = ankle.GetComponentsInChildren<Transform>().FirstOrDefault(item => item.name.EndsWith("ToeBase"));
                var referenceToe = reference.GetComponentsInChildren<Transform>().FirstOrDefault(item => item.name.EndsWith("ToeBase"));
                float toeAngle = referenceToe && toeBone ? Quaternion.Angle(referenceToe.localRotation, toeBone.localRotation) : -1f;
                Lines.Add($"Walk {(right ? "R" : "L")} {animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F3}: input {raw.x:F3}/{raw.y:F3} output {h:F3}/{t:F3}; ankle tilt {Vector3.Angle(ankleUp, Vector3.up):F1}, toe deformation {toeAngle:F1}.");
            }
        }
    }

    private static void EndGait(string name)
    {
        float percentile(List<float> values)
        {
            values.Sort();
            return values.Count > 0 ? values[(int)((values.Count - 1) * .9f)] : 0f;
        }
        Lines.Add($"{name}, {Forefoot.Count} low-heel samples: toe P90 {percentile(Forefoot):F3}m, toe above heel P90 {percentile(PitchLift):F3}m; min {minimum:F3}m, max swing {maxSwing:F3}m.");
        Forefoot.Clear(); PitchLift.Clear(); maxSwing = 0f; minimum = float.PositiveInfinity;
    }

    private static void Place(Vector3 position)
    {
        var capsule = player.Body.GetComponent<CharacterController>();
        capsule.enabled = false;
        player.Body.SetPositionAndRotation(position, Quaternion.identity);
        capsule.enabled = true;
    }

    private static void Keys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        startedAt = Time.time;
    }

    private static void Finish()
    {
        File.WriteAllLines(Output, Lines);
        EditorApplication.update -= Tick;
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        if (sample) UnityEngine.Object.DestroyImmediate(sample);
        EditorApplication.isPlaying = false;
    }
}
