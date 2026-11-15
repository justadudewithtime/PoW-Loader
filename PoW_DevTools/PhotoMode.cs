using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Heluo;
using Heluo.Actor;
using Heluo.Controller;
using Heluo.Utility;
using UnityEngine;

namespace PoWDevTools
{
    /*
     * F6: animation browser. Every clip of the game (from its asset index) can be played on the player or a character
     *     nearby, the same way cutscenes play them (ActorController.PlayCustom).
     * F7: photo mode. Free camera, game UI hidden, the player can't walk off (photo mode takes the input like a menu),
     *     time can be paused to freeze a pose, the character can be turned, screenshots at 1x/2x/4x.
     */
    internal class PhotoMode : IControllable
    {
        private const string AnimationFolder = "assets/character/all_action/animation/";

        //-------------------------------------------------------------- state
        public bool BrowserOpen;
        public bool Active;
        private bool _panelHidden;
        private bool _paused;
        private float _savedTimeScale = 1f;

        private Camera _camera;
        private Behaviour[] _disabledCameraParts = new Behaviour[0];
        private Vector3 _savedCameraPosition;
        private Quaternion _savedCameraRotation;
        private float _savedFov;
        private float _yaw, _pitch;
        private float _moveSpeed = 4f;

        //Canvases and their click handling switched off while photo mode is on
        private readonly List<Behaviour> _hiddenUi = new List<Behaviour>();
        private float _nextUiHide;
        private readonly Dictionary<Transform, Quaternion> _savedRotations = new Dictionary<Transform, Quaternion>();

        private List<string> _clips;
        private bool _clipsErrorLogged;
        private List<string> _shown = new List<string>();
        private string _search = "";
        private string _searchShown;
        private string _category = "All";
        private string _categoryShown;
        private Vector2 _scroll;
        private int _first, _count;
        private string _selected;
        private bool _loop = true;
        private float _animSpeed = 1f;
        private float _turn;


        private int _shotSize = 1;
        private int _hideForFrames;
        private string _toast;
        private float _toastUntil;

        private Texture2D _background;
        private GUIStyle _rowStyle, _boxStyle, _titleStyle;

        //-------------------------------------------------------------- keys
        public void Update()
        {
            bool typing = GUIUtility.keyboardControl != 0;
            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (Active) Exit(); else Enter();
            }
            else if (Input.GetKeyDown(KeyCode.F6) && !Active)
            {
                BrowserOpen = !BrowserOpen;
                if (BrowserOpen) RefreshTargets(); else RestoreSpeeds();
            }

            //While typing in the browser's search box, the letters must not walk the character or open menus
            SetInputTaken(Active || (BrowserOpen && typing));
            if (!Active) return;
            if (Time.unscaledTime >= _nextUiHide) HideUi();

            if (!typing)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { Exit(); return; }
                if (Input.GetKeyDown(KeyCode.H)) _panelHidden = !_panelHidden;
                if (Input.GetKeyDown(KeyCode.Space)) SetPaused(!_paused);
                if (Input.GetKeyDown(KeyCode.P)) Screenshot();
            }
            MoveCamera(typing);
        }

        //-------------------------------------------------------------- photo mode on/off
        private void Enter()
        {
            if (Game.UI == null || Game.MainCamera == null)
            {
                Toast("Photo mode needs a loaded save.");
                return;
            }
            _camera = Game.MainCamera.Camera != null ? Game.MainCamera.Camera : Camera.main;
            if (_camera == null)
            {
                Toast("No camera found.");
                return;
            }

            Active = true;
            BrowserOpen = true;
            _panelHidden = false;

            //The game's camera scripts move the camera every frame: they rest while photo mode drives it
            _savedCameraPosition = _camera.transform.position;
            _savedCameraRotation = _camera.transform.rotation;
            _savedFov = _camera.fieldOfView;
            var parts = new List<Behaviour>();
            foreach (Behaviour b in new Behaviour[] { Game.MainCamera, Game.MainCamera.Controller })
            {
                if (b != null && b.enabled) { b.enabled = false; parts.Add(b); }
            }
            _disabledCameraParts = parts.ToArray();
            Vector3 euler = _camera.transform.rotation.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;

            //The game's UI goes away (menus, names over heads)
            _hiddenUi.Clear();
            HideUi();

            //Photo mode takes the input like a menu does, the player can't walk or open menus
            SetInputTaken(true);

            RefreshTargets();
            Toast("Photo mode: WASD move, Q/E down/up, right mouse look, wheel zoom, Space pause, P photo, H panel, Esc exit");
        }

        private void Exit()
        {
            if (!Active) return;
            Active = false;
            SetPaused(false);
            SetInputTaken(false);

            foreach (Behaviour ui in _hiddenUi) if (ui != null) ui.enabled = true;
            _hiddenUi.Clear();

            if (_camera != null)
            {
                _camera.transform.position = _savedCameraPosition;
                _camera.transform.rotation = _savedCameraRotation;
                _camera.fieldOfView = _savedFov;
            }
            foreach (Behaviour b in _disabledCameraParts) if (b != null) b.enabled = true;
            _disabledCameraParts = new Behaviour[0];

            //Characters turned for a photo face their old way again, so scripted scenes are not thrown off
            foreach (var r in _savedRotations) if (r.Key != null) r.Key.rotation = r.Value;
            _savedRotations.Clear();
            _turn = 0f;
            RestoreSpeeds();
            BrowserOpen = false;
        }

        /*
         * The UI is hidden by switching its canvases off, not its objects: a cutscene's talk box, fades and the scripts
         * waiting on them keep running (a deactivated object stops its coroutines and animations, and the cutscene
         * chain then waited forever). A box opened while photo mode is on is hidden too, at the next check.
         */
        private void HideUi()
        {
            _nextUiHide = Time.unscaledTime + 0.25f;
            foreach (GameObject root in new[] { Game.UI?.Canvas, Game.UI?.Canvas3D != null ? Game.UI.Canvas3D.gameObject : null })
            {
                if (root == null) continue;
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true)) Hide(canvas);
                foreach (UnityEngine.UI.GraphicRaycaster raycaster in root.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true)) Hide(raycaster);
            }
        }

        private void Hide(Behaviour ui)
        {
            if (ui != null && ui.enabled) { ui.enabled = false; _hiddenUi.Add(ui); }
        }

        private bool _inputTaken;

        private void SetInputTaken(bool take)
        {
            if (take == _inputTaken || Game.Input == null) return;
            try
            {
                if (take) Game.Input.Push(this); else Game.Input.Pop(this);
                _inputTaken = take;
            }
            catch (Exception e)
            {
                Debug.LogError("[DevTools] Input: " + e.Message);
            }
        }

        private void SetPaused(bool paused)
        {
            if (paused == _paused) return;
            _paused = paused;
            if (paused)
            {
                _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = _savedTimeScale;
            }
        }

        //-------------------------------------------------------------- free camera
        private void MoveCamera(bool typing)
        {
            if (_camera == null) return;
            float dt = Time.unscaledDeltaTime;

            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxisRaw("Mouse X") * 3f;
                _pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * 3f, -89f, 89f);
            }
            _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            if (!typing)
            {
                Vector3 move = Vector3.zero;
                if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
                if (Input.GetKey(KeyCode.S)) move += Vector3.back;
                if (Input.GetKey(KeyCode.A)) move += Vector3.left;
                if (Input.GetKey(KeyCode.D)) move += Vector3.right;
                if (Input.GetKey(KeyCode.E)) move += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) move += Vector3.down;
                float speed = _moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? 4f : 1f) * (Input.GetKey(KeyCode.LeftControl) ? 0.25f : 1f);
                _camera.transform.position += _camera.transform.TransformDirection(move) * speed * dt;
            }

            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f && !MouseOverPanel())
            {
                _camera.fieldOfView = Mathf.Clamp(_camera.fieldOfView - wheel * 2f, 5f, 100f);
            }
        }

        //-------------------------------------------------------------- characters (shared with the wardrobe)
        private ActorController Target { get { return Targets.Current; } }

        private void RefreshTargets() { Targets.Refresh(); }

        private void Play(string clipName)
        {
            ActorController actor = Target;
            if (actor == null) { Toast("No character to play it on."); return; }
            try
            {
                AnimationClip clip = actor.GetAnimationClip(clipName);
                if (clip == null) { Toast("The game could not load " + clipName); return; }
                actor.StopCustom();
                if (_loop)
                {
                    clip.wrapMode = WrapMode.Loop;
                    actor.PlayCustom(clip, 0f, null);
                }
                else
                {
                    clip.wrapMode = WrapMode.Once;
                    actor.PlayCustom(clip, clip.length, () => { });
                }
                SetSpeed(actor.Animator, _animSpeed);
                Toast(clipName + " (" + clip.length.ToString("0.0") + " s)");
            }
            catch (Exception e)
            {
                Toast("Could not play " + clipName + ": " + e.GetType().Name);
                Debug.LogError("[DevTools] Playing " + clipName + ": " + e);
            }
        }

        //Animators whose speed the Speed slider changed, with the speed they had: given back on Stop, on leaving photo
        //mode and on closing the browser, also for characters no longer selected
        private readonly Dictionary<Animator, float> _savedSpeeds = new Dictionary<Animator, float>();

        private void SetSpeed(Animator animator, float speed)
        {
            if (animator == null) return;
            if (!_savedSpeeds.ContainsKey(animator)) _savedSpeeds[animator] = animator.speed;
            animator.speed = speed;
        }

        private void RestoreSpeed(Animator animator)
        {
            if (animator == null || !_savedSpeeds.TryGetValue(animator, out float speed)) return;
            animator.speed = speed;
            _savedSpeeds.Remove(animator);
        }

        private void RestoreSpeeds()
        {
            foreach (var saved in _savedSpeeds) if (saved.Key != null) saved.Key.speed = saved.Value;
            _savedSpeeds.Clear();
        }

        private void Stop()
        {
            ActorController actor = Target;
            if (actor == null) return;
            try
            {
                actor.StopCustom();
                RestoreSpeed(actor.Animator);
            }
            catch { }
        }

        private void Turn(float degrees)
        {
            ActorController actor = Target;
            if (actor == null) return;
            Transform t = actor.transform;
            if (!_savedRotations.ContainsKey(t)) _savedRotations[t] = t.rotation;
            t.rotation = _savedRotations[t] * Quaternion.Euler(0f, degrees, 0f);
        }

        //-------------------------------------------------------------- clip list
        private List<string> Clips
        {
            get
            {
                if (_clips != null) return _clips;
                //Kept only once the game's file list was read, until then every draw tries again
                try
                {
                    object sheet = Traverse.Create(Heluo.Resource.BundleManagerBySheet.Instance()).Field("bundleSheet").GetValue();
                    var files = Traverse.Create(sheet).Field("FilesInfo").GetValue<Dictionary<string, string>>();
                    if (files != null)
                    {
                        _clips = files.Keys
                            .Where(k => k.StartsWith(AnimationFolder, StringComparison.OrdinalIgnoreCase) && k.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                            .Select(k => Path.GetFileNameWithoutExtension(k))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                }
                catch (Exception e)
                {
                    //Written once, not on every draw that tries again
                    if (!_clipsErrorLogged) Debug.LogError("[DevTools] Could not read the animation list: " + e);
                    _clipsErrorLogged = true;
                }
                return _clips ?? new List<string>();
            }
        }

        private static readonly string[] Categories = { "All", "m_", "f_", "Characters", "Other" };

        private static bool InCategory(string clip, string category)
        {
            switch (category)
            {
                case "m_": return clip.StartsWith("m_", StringComparison.OrdinalIgnoreCase);
                case "f_": return clip.StartsWith("f_", StringComparison.OrdinalIgnoreCase);
                case "Characters": return clip.Length > 2 && char.IsLetter(clip[0]) && char.IsLetter(clip[1]) && char.IsDigit(clip[2]);
                case "Other": return !InCategory(clip, "m_") && !InCategory(clip, "f_") && !InCategory(clip, "Characters");
                default: return true;
            }
        }

        //-------------------------------------------------------------- screenshot
        private void Screenshot()
        {
            string folder = Path.Combine(BepInEx.Paths.GameRootPath, "PhotoMode");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "PathOfWuxia_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (_shotSize > 1 ? "_" + _shotSize + "x" : "") + ".png");
            //The panel stays off for a couple of frames so it is not in the picture
            _hideForFrames = 3;
            ScreenCapture.CaptureScreenshot(file, _shotSize);
            Toast("Saved " + Path.GetFileName(file) + " in the PhotoMode folder");
        }

        private void Toast(string text)
        {
            _toast = text;
            _toastUntil = Time.realtimeSinceStartup + 5f;
        }

        //-------------------------------------------------------------- panel
        private Rect PanelRect { get { return new Rect(Screen.width - 470f, 10f, 460f, Mathf.Min(Screen.height - 20f, 860f)); } }

        private bool MouseOverPanel()
        {
            Vector2 m = Input.mousePosition;
            Vector2 gui = new Vector2(m.x, Screen.height - m.y);
            if (Wardrobe.OpenRect.HasValue && Wardrobe.OpenRect.Value.Contains(gui)) return true;
            if (!BrowserOpen || _panelHidden) return false;
            return PanelRect.Contains(gui);
        }

        public void OnGUI()
        {
            //Other overlays (the wardrobe) stay out of photos and away while the panel is hidden
            Wardrobe.HideOverlays = _hideForFrames > 0 || (Active && _panelHidden);
            if (_hideForFrames > 0)
            {
                if (Event.current.type == EventType.Repaint) _hideForFrames--;
                return;
            }
            Styles();

            if (_toast != null && Time.realtimeSinceStartup < _toastUntil)
            {
                GUI.Box(new Rect(Screen.width / 2f - 450f, Screen.height - 70f, 900f, 40f), _toast, _boxStyle);
            }
            if (!BrowserOpen || (Active && _panelHidden)) return;

            Rect area = PanelRect;
            GUI.DrawTexture(area, _background);
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));
            bool layout = Event.current.type == EventType.Layout;

            GUILayout.Label(Active ? "PHOTO MODE" : "ANIMATION BROWSER", _titleStyle);
            GUILayout.Label(Active ? "WASD move · Q/E down/up · right mouse look · Shift fast · wheel zoom\nSpace pause · P photo · H hide panel · Esc/F7 exit"
                                   : "F6 close · F7 photo mode · F8 wardrobe");

            //Target
            Targets.DrawPicker();

            //Search and categories
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Search", GUILayout.Width(50f));
                _search = GUILayout.TextField(_search ?? "");
            }
            using (new GUILayout.HorizontalScope())
            {
                foreach (string c in Categories)
                {
                    if (GUILayout.Toggle(_category == c, c, GUI.skin.button)) _category = c;
                }
            }
            if (layout && (_searchShown != _search || _categoryShown != _category))
            {
                _searchShown = _search;
                _categoryShown = _category;
                string s = _search.Trim();
                _shown = Clips.Where(c => InCategory(c, _category) && (s.Length == 0 || c.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                _scroll = Vector2.zero;
            }
            GUILayout.Label(_shown.Count + " of " + Clips.Count + " animations");

            //Clip list, only the rows in view are drawn
            const float row = 24f;
            float listHeight = area.height - (Active ? 470f : 300f);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(listHeight));
            if (layout)
            {
                _first = Mathf.Max(0, (int)(_scroll.y / row) - 1);
                _count = Mathf.Max(0, Mathf.Min(_shown.Count - _first, (int)(listHeight / row) + 3));
            }
            int first = _first, count = Mathf.Min(_count, Mathf.Max(0, _shown.Count - _first));
            GUILayout.Space(first * row);
            for (int i = first; i < first + count; i++)
            {
                string clip = _shown[i];
                string label = clip == _selected ? "<b><color=#ffd27a>" + clip + "</color></b>" : clip;
                if (GUILayout.Button(label, _rowStyle, GUILayout.Height(row - 2f)))
                {
                    _selected = clip;
                    Play(clip);
                }
            }
            GUILayout.Space(Mathf.Max(0, _shown.Count - first - count) * row);
            GUILayout.EndScrollView();

            //Playback
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Play again", GUILayout.Width(100f)) && _selected != null) Play(_selected);
                if (GUILayout.Button("Stop", GUILayout.Width(70f))) Stop();
                _loop = GUILayout.Toggle(_loop, " Loop");
            }
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Speed " + _animSpeed.ToString("0.00") + "x", GUILayout.Width(100f));
                float speed = GUILayout.HorizontalSlider(_animSpeed, 0f, 2f);
                if (Math.Abs(speed - _animSpeed) > 0.001f)
                {
                    _animSpeed = speed;
                    if (Target != null) SetSpeed(Target.Animator, _animSpeed);
                }
            }

            if (Active)
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Turn " + Mathf.RoundToInt(_turn) + "°", GUILayout.Width(100f));
                    float turn = GUILayout.HorizontalSlider(_turn, -180f, 180f);
                    if (Math.Abs(turn - _turn) > 0.01f) { _turn = turn; Turn(_turn); }
                }
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Zoom (FOV) " + Mathf.RoundToInt(_camera != null ? _camera.fieldOfView : 0f), GUILayout.Width(100f));
                    if (_camera != null) _camera.fieldOfView = GUILayout.HorizontalSlider(_camera.fieldOfView, 5f, 100f);
                }
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Move speed " + _moveSpeed.ToString("0.0"), GUILayout.Width(100f));
                    _moveSpeed = GUILayout.HorizontalSlider(_moveSpeed, 0.5f, 20f);
                }
                using (new GUILayout.HorizontalScope())
                {
                    bool paused = GUILayout.Toggle(_paused, " Pause time (freeze pose)");
                    if (paused != _paused) SetPaused(paused);
                }
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Photo size", GUILayout.Width(100f));
                    foreach (int size in new[] { 1, 2, 4 })
                    {
                        if (GUILayout.Toggle(_shotSize == size, size + "x", GUI.skin.button, GUILayout.Width(50f))) _shotSize = size;
                    }
                    if (GUILayout.Button("Take photo (P)")) Screenshot();
                }
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reset camera")) ResetCamera();
                    if (GUILayout.Button("Reset turn")) { _turn = 0f; Turn(0f); }
                    if (GUILayout.Button("Exit photo mode")) Exit();
                }
            }
            GUILayout.EndArea();
        }

        private void ResetCamera()
        {
            if (_camera == null) return;
            _camera.transform.position = _savedCameraPosition;
            _camera.transform.rotation = _savedCameraRotation;
            _camera.fieldOfView = _savedFov;
            Vector3 euler = _savedCameraRotation.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        private void Styles()
        {
            if (_background != null) return;
            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0.04f, 0.04f, 0.06f, 0.88f));
            _background.Apply();
            _rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, richText = true };
            _boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        }

        //-------------------------------------------------------------- IControllable: photo mode swallows the game's input
        public void OnKeyDown(Key key) { }
        public void OnKeyUp(Key key) { }
        public void OnKeyHeld(Key key) { }
        public void OnRightAxisMove(Vector2 axis) { }
        public void OnLeftAxisMove(Vector2 axis) { }
        public void OnDPadAxisMove(Vector2 axis) { }
        public void OnThirdAxisMove(float axis) { }
    }
}
