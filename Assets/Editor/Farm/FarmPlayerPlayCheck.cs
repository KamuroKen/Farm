using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Farm.EditorTools
{
    // An explicit, repeatable Play Mode smoke check; never runs during normal play.
    [InitializeOnLoad]
    public static class FarmPlayerPlayCheck
    {
        private const string Armed = "Farm.PlayerPlayCheck";
        private const string Request = "Library/FarmPlayerCheck.request";
        private static FarmPlayerController player;
        private static Rigidbody2D body;
        private static Animator animator;
        private static Keyboard keyboard;
        private static Vector2 spawn;
        private static Vector2 start;
        private static float started;
        private static float cardinalDistance;
        private static int stage = -1;
        private static readonly List<string> Checks = new List<string>();
        private static readonly List<string> Errors = new List<string>();
        private static readonly Key[][] Keys = {
            new[] { Key.S }, Array.Empty<Key>(), new[] { Key.A }, Array.Empty<Key>(),
            new[] { Key.D }, Array.Empty<Key>(), new[] { Key.W }, Array.Empty<Key>(),
            new[] { Key.S, Key.D }, new[] { Key.W }, Array.Empty<Key>()
        };
        private static readonly string[] States = {
            "RunDown", "IdleDown", "RunLeft", "IdleLeft", "RunRight", "IdleRight",
            "RunUp", "IdleUp", "RunRight", "RunUp", "IdleUp"
        };

        [Serializable] private class Report
        {
            public bool success;
            public string completedUtc;
            public string[] checks;
            public string[] errors;
        }

        static FarmPlayerPlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("Tools/Farm/Check Player in Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(Armed, true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Armed, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    Checks.Clear(); Errors.Clear();
                    player = UnityEngine.Object.FindFirstObjectByType<FarmPlayerController>();
                    if (player == null) throw new Exception("No farm player in the active scene.");
                    body = player.GetComponent<Rigidbody2D>();
                    animator = player.GetComponent<Animator>();
                    spawn = body.position;
                    keyboard = InputSystem.AddDevice<Keyboard>("FarmCheckKeyboard");
                    BeginStage(0);
                }
                catch (Exception e) { Finish(e.ToString()); }
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                keyboard = null;
                if (stage >= 0) Finish("Play check interrupted before completion.");
            }
        }

        private static void BeginStage(int next)
        {
            stage = next;
            if (stage % 2 == 0 || stage == 9)
            {
                body.position = spawn;
                body.linearVelocity = Vector2.zero;
            }
            start = body.position;
            started = Time.time;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Keys[stage]));
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && File.Exists(Request))
            {
                File.Delete(Request);
                Run();
                return;
            }
            if (stage < 0 || !EditorApplication.isPlaying || player == null) return;
            float elapsed = Time.time - started;
            if (elapsed < (stage == 9 ? 1.2f : 0.5f)) return;
            try
            {
                Assert(animator.GetCurrentAnimatorStateInfo(0).IsName(States[stage]), "Animation " + States[stage]);
                var sprite = player.GetComponent<SpriteRenderer>().sprite;
                string action = States[stage].StartsWith("Run", StringComparison.Ordinal) ? "Run" : "Idle";
                Assert(sprite != null && AssetDatabase.GetAssetPath(sprite) == "Assets/Farm/Player/Sprites/Bunny_" + action + ".png",
                    "Bunny sprites used for " + action);
                Assert(player.GetComponent<SpriteRenderer>().sortingOrder == 100, "Player order stays 100 during movement");
                var follow = UnityEngine.Object.FindFirstObjectByType<FarmCameraFollow>();
                Assert(follow != null && follow.Target == player.transform, "Camera target assigned");
                Assert(Vector2.Distance(follow.transform.position, player.transform.position + Vector3.up * 0.5f) < 0.15f,
                    "Camera follows player");
                Vector2 delta = body.position - start;
                if (stage == 0)
                {
                    Assert(delta.y < -1 && Mathf.Abs(delta.x) < 0.05f, "S moves down");
                    cardinalDistance = delta.magnitude / elapsed;
                }
                if (stage == 2) Assert(delta.x < -1 && Mathf.Abs(delta.y) < 0.05f, "A moves left");
                if (stage == 4) Assert(delta.x > 1 && Mathf.Abs(delta.y) < 0.05f, "D moves right");
                if (stage == 6) Assert(delta.y > 0.5f && Mathf.Abs(delta.x) < 0.05f, "W moves up");
                if (stage == 1 || stage == 3 || stage == 5 || stage == 7)
                    Assert(delta.magnitude < 0.2f && body.linearVelocity.sqrMagnitude < 0.001f, "Release stops movement");
                if (stage == 8)
                {
                    Assert(delta.x > 0.5f && delta.y < -0.5f, "Diagonal moves down-right");
                    Assert(Mathf.Abs(delta.magnitude / elapsed - cardinalDistance) < 0.4f, "Diagonal speed matches cardinal speed");
                }
                if (stage == 9)
                    Assert(body.position.y > spawn.y + 0.5f && body.position.y < 6.3f, "Cottage collider blocks passage");
                if (stage + 1 == Keys.Length) Finish(null);
                else BeginStage(stage + 1);
            }
            catch (Exception e) { Finish(e.ToString()); }
        }

        private static void Assert(bool condition, string label)
        {
            if (!condition) throw new Exception(label + " failed at stage " + stage);
            if (!Checks.Contains(label)) Checks.Add(label);
        }

        private static void Finish(string error)
        {
            if (error != null) Errors.Add(error);
            stage = -1;
            if (keyboard != null && keyboard.added)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.RemoveDevice(keyboard);
            }
            keyboard = null;
            SessionState.SetBool(Armed, false);
            Directory.CreateDirectory("BuildArtifacts");
            File.WriteAllText("BuildArtifacts/player-play-check.json", JsonUtility.ToJson(new Report {
                success = Errors.Count == 0, completedUtc = DateTime.UtcNow.ToString("o"),
                checks = Checks.ToArray(), errors = Errors.ToArray()
            }, true));
            EditorApplication.ExitPlaymode();
        }
    }
}
