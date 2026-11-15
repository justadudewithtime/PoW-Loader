using System;
using System.Collections.Generic;
using System.Linq;
using Heluo;
using Heluo.Actor;
using Heluo.Utility;
using UnityEngine;

namespace PoWDevTools
{
    //The character the animation browser, photo mode and wardrobe act on: the player, then characters within 25 m
    internal static class Targets
    {
        private static List<ActorController> _list = new List<ActorController>();
        private static int _index;

        public static int Count { get { return _list.Count; } }
        public static int Index { get { return _index; } }
        public static bool IsPlayer { get { return _index == 0 && Player != null && Current == Player; } }

        public static ActorController Player
        {
            get
            {
                try { return Game.EntityManager?.GetPlayerEntity()?.GetBaseComponent<ActorController>(); }
                catch { return null; }
            }
        }

        public static ActorController Current
        {
            get
            {
                //Characters unloaded before the selected one must not move the selection to someone else
                ActorController selected = _index >= 0 && _index < _list.Count ? _list[_index] : null;
                if (_list.RemoveAll(a => a == null) > 0 && selected != null)
                {
                    int kept = _list.IndexOf(selected);
                    if (kept >= 0) _index = kept;
                }
                if (_list.Count == 0) Refresh();
                if (_list.Count == 0) return null;
                _index = Mathf.Clamp(_index, 0, _list.Count - 1);
                return _list[_index];
            }
        }

        public static void Next(int step)
        {
            if (_list.Count == 0) return;
            _index = (_index + step + _list.Count) % _list.Count;
        }

        public static void Refresh()
        {
            ActorController current = _list.Count > 0 && _index < _list.Count ? _list[_index] : null;
            _list = new List<ActorController>();
            ActorController player = Player;
            if (player != null) _list.Add(player);
            Vector3 centre = player != null ? player.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            try
            {
                _list.AddRange(UnityEngine.Object.FindObjectsOfType<ActorController>()
                    .Where(a => a != player && a.isActiveAndEnabled && Vector3.Distance(a.transform.position, centre) < 25f)
                    .OrderBy(a => Vector3.Distance(a.transform.position, centre)));
            }
            catch { }
            _index = current != null ? Math.Max(0, _list.IndexOf(current)) : 0;
        }

        public static string Label
        {
            get
            {
                ActorController a = Current;
                if (a == null) return "(no character)";
                return (IsPlayer ? "Player: " : "") + a.gameObject.name.Replace("(Clone)", "") + "  (" + (_index + 1) + "/" + _list.Count + ")";
            }
        }

        //The < name > ↻ row both panels draw
        public static void DrawPicker()
        {
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("<", GUILayout.Width(30f))) Next(-1);
                GUILayout.Label(Label, GUILayout.Width(300f));
                if (GUILayout.Button(">", GUILayout.Width(30f))) Next(1);
                if (GUILayout.Button("↻", GUILayout.Width(30f))) Refresh();
            }
        }
    }
}
