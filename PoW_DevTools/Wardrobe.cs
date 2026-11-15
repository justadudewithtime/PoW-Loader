using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Heluo;
using Heluo.Actor;
using Heluo.Data;
using Heluo.Features.Wearing;
using Heluo.Utility;
using UnityEngine;

namespace DevToolsProbe
{
    /*
     * F8: wardrobe. Swaps hair, head and body with those of any other model of the same body type, puts accessories
     * on the five attach points and a weapon in the hands, with the game's own AvatarComponent (what its developers'
     * appearance editor used). Changes are visual only. "Keep this look" puts it back on the player after loading a map,
     * and looks are kept in BepInEx\config\DevToolsProbe_Looks.txt.
     */
    internal class Wardrobe : Heluo.Controller.IControllable
    {
        public static Rect? OpenRect;
        public static bool HideOverlays;
        public bool Open;

        private enum Slot { Hair, Head, Body, HandL, HandR, HipL, HipR, Back, Weapon, TintHair, TintHead, TintBody, TintAccessories, HueHair, HueHead, HueBody, HueAccessories }
        private static readonly string[] SlotNames = { "Hair", "Head", "Body", "Left hand", "Right hand", "Left hip", "Right hip", "Back", "Weapon",
            "Hair tint", "Head tint", "Body tint", "Accessory tint", "Hair recolour", "Head recolour", "Body recolour", "Accessory recolour" };

        //Colours view
        private bool _colours;
        private ColorPart _colorPart = ColorPart.Body;
        private Color _tint = Color.white;
        private HueShift _hue = HueShift.Neutral;
        private string _colourStateFor;
        private static readonly Color[] Palette =
        {
            Color.white, new Color(1f, 0.55f, 0.55f), new Color(1f, 0.75f, 0.5f), new Color(1f, 0.95f, 0.55f), new Color(0.7f, 1f, 0.6f), new Color(0.55f, 0.95f, 0.9f),
            new Color(0.55f, 0.75f, 1f), new Color(0.75f, 0.6f, 1f), new Color(1f, 0.6f, 0.9f), new Color(0.85f, 0.85f, 0.85f), new Color(0.6f, 0.6f, 0.6f), new Color(0.35f, 0.35f, 0.4f),
            new Color(0.75f, 0.2f, 0.2f), new Color(0.2f, 0.5f, 0.25f), new Color(0.2f, 0.3f, 0.7f), new Color(0.55f, 0.4f, 0.25f), new Color(0.95f, 0.8f, 0.4f), new Color(0.45f, 0.2f, 0.5f)
        };

        private static Slot TintSlot(ColorPart p) { return (Slot)((int)Slot.TintHair + (int)p); }
        private static Slot HueSlot(ColorPart p) { return (Slot)((int)Slot.HueHair + (int)p); }
        private static ColorPart PartOfColourSlot(Slot s) { return (ColorPart)(s >= Slot.HueHair ? s - Slot.HueHair : s - Slot.TintHair); }
        //The game's own weapon slot hides weapons outside combat: weapons go straight onto the hands, hips or back instead
        private static readonly Slot[] VisibleSlots = { Slot.Hair, Slot.Head, Slot.Body, Slot.HandL, Slot.HandR, Slot.HipL, Slot.HipR, Slot.Back };
        private static readonly string[] Filters = { "Weapons", "Accessories", "All" };
        private string _filter = "Weapons";
        //Hair, head and body: from models of the same body type (fits), the same gender (may stretch) or any
        private static readonly string[] BodyFilters = { "Same body", "Same gender", "All" };
        private string _bodyFilter = "Same body";
        private string _bodyFilterShown;
        private string _filterShown;
        private Slot _slot = Slot.Hair;

        private static readonly Regex ModelKey = new Regex(@"^assets/character/([a-z]+_[a-z]+)/([^/]+)/prefab/\2\.prefab$", RegexOptions.Compiled);
        private static readonly Regex AccessoryKey = new Regex(@"^assets/character/all_accessory/prefab/(.+)\.prefab$", RegexOptions.Compiled);

        private Dictionary<string, List<string>> _modelsByBody;   //"male_m" -> models
        private Dictionary<string, string> _bodyOfModel;           //"in0101" -> "male_m"
        private List<string> _accessories;
        private string _bodyType;                                  //body type of the target, or picked by hand

        private readonly Dictionary<GameObject, AvatarComponent> _avatars = new Dictionary<GameObject, AvatarComponent>();
        private readonly Dictionary<GameObject, Dictionary<Slot, string>> _original = new Dictionary<GameObject, Dictionary<Slot, string>>();
        private readonly Dictionary<string, Dictionary<Slot, string>> _looks = new Dictionary<string, Dictionary<Slot, string>>(StringComparer.OrdinalIgnoreCase);
        private bool _keepPlayerLook = true;
        private GameObject _lastPlayerObject;
        private float _nextPlayerCheck;

        private string _search = "", _searchShown;
        private Slot _slotShown = (Slot)(-1);
        private string _bodyShown;
        private List<string> _shown = new List<string>();
        private Vector2 _scroll;
        private int _first, _count;
        private string _message = "";
        private Texture2D _background;
        private GUIStyle _rowStyle, _titleStyle, _smallStyle, _swatchStyle;

        public Wardrobe()
        {
            LoadLooks();
        }

        //-------------------------------------------------------------- per frame
        private bool _saveDue;
        private float _nextSave;

        public void Update()
        {
            //Slider drags change the look every frame: the file is written at most once a second
            if (_saveDue && Time.realtimeSinceStartup > _nextSave)
            {
                _saveDue = false;
                _nextSave = Time.realtimeSinceStartup + 1f;
                SaveLooks();
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                Open = !Open;
                if (Open) { Targets.Refresh(); _bodyType = null; }
            }
            //Typing in the search box must not walk the character or open menus
            SetInputTaken(Open && GUIUtility.keyboardControl != 0);
            if (!Open) OpenRect = null;

            //A kept look comes back on the player after a map load (the player's model is made anew)
            if (_keepPlayerLook && Time.realtimeSinceStartup > _nextPlayerCheck)
            {
                _nextPlayerCheck = Time.realtimeSinceStartup + 1f;
                ActorController player = Targets.Player;
                GameObject go = player != null ? player.gameObject : null;
                if (go != null && go != _lastPlayerObject)
                {
                    _lastPlayerObject = go;
                    if (_looks.TryGetValue(ModelName(go), out var look))
                    {
                        foreach (var item in look.OrderBy(i => i.Key).ToList()) Apply(player, item.Key, item.Value, remember: false);
                    }
                }
            }
        }

        //-------------------------------------------------------------- asset lists
        private void BuildLists()
        {
            if (_modelsByBody != null) return;
            _modelsByBody = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _bodyOfModel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _accessories = new List<string>();
            try
            {
                object sheet = Traverse.Create(Heluo.Resource.BundleManagerBySheet.Instance()).Field("bundleSheet").GetValue();
                var files = Traverse.Create(sheet).Field("FilesInfo").GetValue<Dictionary<string, string>>();
                foreach (string key in files.Keys)
                {
                    string k = key.ToLowerInvariant().Replace('\\', '/');
                    Match m = ModelKey.Match(k);
                    if (m.Success)
                    {
                        string body = m.Groups[1].Value, model = m.Groups[2].Value;
                        if (!_modelsByBody.TryGetValue(body, out var list)) _modelsByBody[body] = list = new List<string>();
                        list.Add(model);
                        if (!_bodyOfModel.ContainsKey(model)) _bodyOfModel[model] = body;
                        continue;
                    }
                    m = AccessoryKey.Match(k);
                    if (m.Success) _accessories.Add(m.Groups[1].Value);
                }
                foreach (var list in _modelsByBody.Values) list.Sort(StringComparer.OrdinalIgnoreCase);
                _accessories.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Debug.LogError("[DevToolsProbe] Could not read the model list: " + e);
            }
        }

        //The model folder name of a character: the player's from its exterior data, others from their object name
        private string ModelName(GameObject go)
        {
            if (go == null) return "";
            ActorController player = Targets.Player;
            if (player != null && go == player.gameObject)
            {
                try
                {
                    object exterior = Game.GameData.Exterior[GameConfig.Player];
                    string model = Traverse.Create(exterior).Property("Model").GetValue<string>();
                    if (!string.IsNullOrEmpty(model)) return model;
                }
                catch { }
            }
            return go.name.Replace("(Clone)", "").Trim();
        }

        private string BodyTypeOf(ActorController actor)
        {
            BuildLists();
            if (actor != null && _bodyOfModel.TryGetValue(ModelName(actor.gameObject), out string body)) return body;
            return null;
        }

        /*
         * A weapon model is the weapon as worn (sheathed, on the hip or back) with the drawn weapon as a child named
         * ha_Hand...: made to sit in the hand at the hand bone's own position and angle. Like the game does in
         * combat, that child goes onto the hand and the rest (the sheath) is dropped.
         */
        private static GameObject Grip(GameObject worn, Transform hand)
        {
            Transform grip = null;
            foreach (Transform t in worn.GetComponentsInChildren<Transform>(true))
            {
                if (t != worn.transform && t.name.StartsWith("ha_Hand", StringComparison.OrdinalIgnoreCase)) { grip = t; break; }
            }
            if (grip == null || hand == null) return null;
            grip.SetParent(hand, false);
            grip.SetAsFirstSibling();
            grip.localPosition = Vector3.zero;
            grip.localRotation = Quaternion.identity;
            grip.gameObject.name = worn.name.Replace("(Clone)", "").Trim();
            UnityEngine.Object.Destroy(worn);
            return grip.gameObject;
        }

        //Models offering hair/head/body for a body type, by the Show filter; other body types are listed after
        private IEnumerable<string> PartModels(string body)
        {
            if (body == null) return new List<string>();
            string gender = body.Split('_')[0];
            IEnumerable<string> bodies = _modelsByBody.Keys.Where(b => !b.StartsWith("all_", StringComparison.OrdinalIgnoreCase));
            if (_bodyFilter == "Same body") bodies = bodies.Where(b => string.Equals(b, body, StringComparison.OrdinalIgnoreCase));
            else if (_bodyFilter == "Same gender") bodies = bodies.Where(b => b.Split('_')[0] == gender);
            return bodies.OrderBy(b => string.Equals(b, body, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(b => b)
                .SelectMany(b => _modelsByBody[b].Select(m => b + "/" + m));
        }

        private static bool IsWeapon(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith("wpt", StringComparison.OrdinalIgnoreCase);
        }

        private static string Pretty(string body)
        {
            if (string.IsNullOrEmpty(body)) return "?";
            string[] p = body.Split('_');
            return char.ToUpper(p[0][0]) + p[0].Substring(1) + " " + (p.Length > 1 ? p[1].ToUpper() : "");
        }

        //-------------------------------------------------------------- applying
        private AvatarComponent AvatarOf(ActorController actor)
        {
            GameObject go = actor.gameObject;
            if (_avatars.TryGetValue(go, out var avatar) && avatar != null) return avatar;
            try
            {
                if (actor == Targets.Player)
                {
                    avatar = Game.EntityManager.GetPlayerEntity().GetBaseComponent<AvatarComponent>();
                }
            }
            catch { }
            if (avatar == null) avatar = new AvatarComponent(go);
            _avatars[go] = avatar;
            return avatar;
        }

        private static AttachPointType PointOf(Slot slot)
        {
            switch (slot)
            {
                case Slot.HandL: return AttachPointType.ha_Hand_L;
                case Slot.HandR: return AttachPointType.ha_Hand_R;
                case Slot.HipL: return AttachPointType.ha_Pelvis_L;
                case Slot.HipR: return AttachPointType.ha_Pelvis_R;
                default: return AttachPointType.ha_Spine1_B;
            }
        }

        private static RendererType PartOf(Slot slot)
        {
            return slot == Slot.Hair ? RendererType.hair : slot == Slot.Head ? RendererType.head : RendererType.body;
        }

        //What a slot holds before our first change, to put back with Reset
        private void RememberOriginal(ActorController actor, Slot slot)
        {
            GameObject go = actor.gameObject;
            if (!_original.TryGetValue(go, out var saved)) _original[go] = saved = new Dictionary<Slot, string>();
            if (saved.ContainsKey(slot)) return;
            string value = "";
            if (slot <= Slot.Body)
            {
                value = BodyTypeOf(actor) != null ? ModelName(go) : "";
            }
            else
            {
                Transform point = AvatarOf(actor)[PointOf(slot)];
                if (point != null && point.childCount > 0) value = point.GetChild(0).name.Replace("(Clone)", "").Trim();
            }
            saved[slot] = value;
        }

        private bool Apply(ActorController actor, Slot slot, string value, bool remember = true)
        {
            if (actor == null) return false;
            try
            {
                AvatarComponent avatar = AvatarOf(actor);
                string body = BodyTypeOf(actor) ?? _bodyType;
                if (remember) RememberOriginal(actor, slot);

                if (slot <= Slot.Body)
                {
                    //"female_m/in0102" (any body type) or a plain model of the character's own body type
                    string partBody = body, model = value;
                    int slash = value.IndexOf('/');
                    if (slash > 0) { partBody = value.Substring(0, slash); model = value.Substring(slash + 1); }
                    else if (_bodyOfModel != null && _bodyOfModel.TryGetValue(value, out string own)) partBody = own;
                    if (string.IsNullOrEmpty(partBody)) { _message = "Pick the body type first (the buttons above the list)."; return false; }
                    value = model;
                    string path = "Character/" + partBody + "/" + model + "/Prefab/" + model + ".prefab";
                    GameObject source = Game.Resource.Load<GameObject>(path);
                    if (source == null) { _message = "The game could not load " + value + "."; return false; }
                    //The game would remove the part when the model has none: tell instead
                    if (source.transform.Find(PartOf(slot).ToString()) == null) { _message = value + " has no " + SlotNames[(int)slot].ToLower() + "."; return false; }
                    avatar.ChangePart(PartOf(slot), path);
                }
                else if (slot == Slot.Weapon)
                {
                    //Old saved looks: the game's weapon slot is not used any more
                    return false;
                }
                else if (slot >= Slot.TintHair)
                {
                    Renderer[] renderers = Recolor.RenderersOf(avatar, PartOfColourSlot(slot));
                    int n;
                    if (slot >= Slot.HueHair) n = Recolor.Shift(renderers, HueShift.Parse(value));
                    else n = Recolor.Tint(renderers, ColorUtility.TryParseHtmlString("#" + value, out Color c) ? c : Color.white);
                    if (n == 0 && remember) { _message = "Nothing on this part can be recoloured that way."; return false; }
                }
                else
                {
                    if (avatar[PointOf(slot)] == null)
                    {
                        _message = "This character has no " + SlotNames[(int)slot].ToLower() + " attach point.";
                        return false;
                    }
                    bool inHand = (slot == Slot.HandL || slot == Slot.HandR) && IsWeapon(value);
                    GameObject worn = avatar.ChangeAccessory(PointOf(slot), value ?? "");
                    if (worn != null && inHand)
                    {
                        worn = Grip(worn, avatar[PointOf(slot)]) ?? worn;
                    }
                    if (worn != null)
                    {
                        //Weapons come hidden outside combat, on the character they are always shown
                        worn.SetActive(true);
                        foreach (Renderer r in worn.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                    }
                    else if (!string.IsNullOrEmpty(value))
                    {
                        _message = "The game could not load " + value + ".";
                        return false;
                    }
                }

                if (slot < Slot.Weapon) ReapplyColours(actor, slot <= Slot.Body ? (ColorPart)(int)slot : ColorPart.Accessories);

                if (remember)
                {
                    string model = ModelName(actor.gameObject);
                    if (!_looks.TryGetValue(model, out var look)) _looks[model] = look = new Dictionary<Slot, string>();
                    look[slot] = value ?? "";
                    _saveDue = true;
                    _message = SlotNames[(int)slot] + ": " + (string.IsNullOrEmpty(value) ? "removed" : value);
                }
                return true;
            }
            catch (Exception e)
            {
                _message = "Could not change " + SlotNames[(int)slot].ToLower() + ": " + e.GetType().Name;
                Debug.LogError("[DevToolsProbe] Wardrobe " + slot + " " + value + ": " + e);
                return false;
            }
        }

        private void ReapplyColours(ActorController actor, ColorPart part)
        {
            if (!_looks.TryGetValue(ModelName(actor.gameObject), out var look)) return;
            if (look.TryGetValue(HueSlot(part), out string hue)) Apply(actor, HueSlot(part), hue, remember: false);
            if (look.TryGetValue(TintSlot(part), out string tint)) Apply(actor, TintSlot(part), tint, remember: false);
        }

        private void ResetLook(ActorController actor)
        {
            if (actor == null) return;
            GameObject go = actor.gameObject;
            foreach (ColorPart part in Enum.GetValues(typeof(ColorPart)))
            {
                Apply(actor, HueSlot(part), HueShift.Neutral.ToString(), remember: false);
                Apply(actor, TintSlot(part), "FFFFFF", remember: false);
            }
            _colourStateFor = null;
            if (_original.TryGetValue(go, out var saved))
            {
                foreach (var item in saved.ToList())
                {
                    if (item.Key <= Slot.Body && item.Value.Length == 0) continue;
                    if (item.Key != Slot.Weapon) Apply(actor, item.Key, item.Value, remember: false);
                }
                _original.Remove(go);
            }
            _looks.Remove(ModelName(go));
            SaveLooks();
            _message = "Back to the original look.";
        }

        //-------------------------------------------------------------- saved looks
        private string LooksFile { get { return Path.Combine(BepInEx.Paths.ConfigPath, "DevToolsProbe_Looks.txt"); } }

        //One line per model: model<TAB>slot=value<TAB>slot=value...
        private void LoadLooks()
        {
            try
            {
                if (!File.Exists(LooksFile)) return;
                foreach (string line in File.ReadAllLines(LooksFile, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    var look = new Dictionary<Slot, string>();
                    foreach (string p in parts.Skip(1))
                    {
                        int eq = p.IndexOf('=');
                        if (eq > 0 && Enum.IsDefined(typeof(Slot), p.Substring(0, eq))) look[(Slot)Enum.Parse(typeof(Slot), p.Substring(0, eq))] = p.Substring(eq + 1);
                    }
                    _looks[parts[0]] = look;
                }
            }
            catch (Exception e) { Debug.LogError("[DevToolsProbe] Could not read the saved looks: " + e.Message); }
        }

        private void SaveLooks()
        {
            try
            {
                File.WriteAllLines(LooksFile, _looks.Where(l => l.Value.Count > 0)
                    .Select(l => l.Key + "\t" + string.Join("\t", l.Value.Select(v => v.Key + "=" + v.Value).ToArray())).ToArray(), Encoding.UTF8);
            }
            catch (Exception e) { Debug.LogError("[DevToolsProbe] Could not save the looks: " + e.Message); }
        }

        //-------------------------------------------------------------- panel
        public void OnGUI()
        {
            if (!Open || HideOverlays) return;
            Styles();
            BuildLists();
            ActorController actor = Targets.Current;
            string detected = BodyTypeOf(actor);
            string body = detected ?? _bodyType;

            Rect area = new Rect(10f, 10f, 470f, Mathf.Min(Screen.height - 20f, 860f));
            OpenRect = area;
            GUI.DrawTexture(area, _background);
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));
            bool layout = Event.current.type == EventType.Layout;

            GUILayout.Label("WARDROBE", _titleStyle);
            GUILayout.Label("F8 close · changes are visual only · parts come from models of the same body type");
            Targets.DrawPicker();

            //Body type: found from the model, else picked here
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Body: " + (detected != null ? Pretty(detected) + " (" + ModelName(actor?.gameObject) + ")" : "unknown, pick one:"), GUILayout.Width(detected != null ? 420f : 140f));
                if (detected == null)
                {
                    foreach (string b in _modelsByBody.Keys.Where(k => !k.StartsWith("all_")).OrderBy(k => k))
                    {
                        if (GUILayout.Toggle(_bodyType == b, Pretty(b), GUI.skin.button)) _bodyType = b;
                    }
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(!_colours, "Parts", GUI.skin.button)) _colours = false;
                if (GUILayout.Toggle(_colours, "Colours", GUI.skin.button)) _colours = true;
            }
            if (_colours)
            {
                DrawColours(actor);
                GUILayout.Label(_message);
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reset this character")) ResetLook(actor);
                    _keepPlayerLook = GUILayout.Toggle(_keepPlayerLook, " Keep the player's look after loading");
                }
                GUILayout.EndArea();
                return;
            }

            //Slots
            for (int row = 0; row < 3; row++)
            {
                using (new GUILayout.HorizontalScope())
                {
                    for (int i = row * 3; i < Math.Min(row * 3 + 3, VisibleSlots.Length); i++)
                    {
                        Slot slot = VisibleSlots[i];
                        if (GUILayout.Toggle(_slot == slot, SlotNames[(int)slot], GUI.skin.button)) _slot = slot;
                    }
                }
            }
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Show", GUILayout.Width(50f));
                foreach (string f in _slot > Slot.Body ? Filters : BodyFilters)
                {
                    if (_slot > Slot.Body) { if (GUILayout.Toggle(_filter == f, f, GUI.skin.button)) _filter = f; }
                    else if (GUILayout.Toggle(_bodyFilter == f, f, GUI.skin.button)) _bodyFilter = f;
                }
            }
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Search", GUILayout.Width(50f));
                _search = GUILayout.TextField(_search ?? "");
            }

            if (layout && (_searchShown != _search || _slotShown != _slot || _bodyShown != body || _filterShown != _filter || _bodyFilterShown != _bodyFilter))
            {
                _searchShown = _search; _slotShown = _slot; _bodyShown = body; _filterShown = _filter; _bodyFilterShown = _bodyFilter;
                IEnumerable<string> source;
                if (_slot <= Slot.Body) source = PartModels(body);
                else if (_filter == "Weapons") source = _accessories.Where(IsWeapon);
                else if (_filter == "Accessories") source = _accessories.Where(a => !IsWeapon(a));
                else source = _accessories;
                string s = _search.Trim();
                _shown = new List<string> { "" }.Concat(source.Where(n => s.Length == 0 || n.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                if (_slot <= Slot.Body) _shown.RemoveAt(0);
                _scroll = Vector2.zero;
            }

            const float rowHeight = 24f;
            float listHeight = area.height - 380f;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(listHeight));
            if (layout)
            {
                _first = Mathf.Max(0, (int)(_scroll.y / rowHeight) - 1);
                _count = Mathf.Max(0, Mathf.Min(_shown.Count - _first, (int)(listHeight / rowHeight) + 3));
            }
            int first = _first, count = Mathf.Min(_count, Mathf.Max(0, _shown.Count - _first));
            GUILayout.Space(first * rowHeight);
            for (int i = first; i < first + count; i++)
            {
                string name = _shown[i];
                string label;
                if (name.Length == 0) label = "<i>(none / remove)</i>";
                else if (name.Contains("/"))
                {
                    string[] bm = name.Split('/');
                    label = bm[1] + ExteriorNameOf(bm[1]) + (string.Equals(bm[0], body, StringComparison.OrdinalIgnoreCase) ? "" : "   <color=#c8b47a>[" + Pretty(bm[0]) + "]</color>");
                }
                else label = name + ExteriorNameOf(name);
                if (GUILayout.Button(label, _rowStyle, GUILayout.Height(rowHeight - 2f)) && actor != null)
                {
                    Apply(actor, _slot, name);
                }
            }
            GUILayout.Space(Mathf.Max(0, _shown.Count - first - count) * rowHeight);
            GUILayout.EndScrollView();

            GUILayout.Label(_message);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset this character")) ResetLook(actor);
                _keepPlayerLook = GUILayout.Toggle(_keepPlayerLook, " Keep the player's look after loading");
            }
            GUILayout.EndArea();
        }

        private void DrawColours(ActorController actor)
        {
            if (actor == null) return;
            //What the panel shows follows the character and part picked
            string key = actor.GetInstanceID() + "|" + _colorPart;
            if (_colourStateFor != key)
            {
                _colourStateFor = key;
                _tint = Color.white;
                _hue = HueShift.Neutral;
                if (_looks.TryGetValue(ModelName(actor.gameObject), out var look))
                {
                    if (look.TryGetValue(TintSlot(_colorPart), out string t) && ColorUtility.TryParseHtmlString("#" + t, out Color c)) _tint = c;
                    if (look.TryGetValue(HueSlot(_colorPart), out string h)) _hue = HueShift.Parse(h);
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                foreach (ColorPart part in Enum.GetValues(typeof(ColorPart)))
                {
                    if (GUILayout.Toggle(_colorPart == part, part.ToString(), GUI.skin.button)) _colorPart = part;
                }
            }
            GUILayout.Label("<size=11>" + Recolor.Describe(Recolor.RenderersOf(AvatarOf(actor), _colorPart)) + "</size>", _smallStyle);

            //Tint: palette and sliders, applied at once
            GUILayout.Label("<b>Tint</b>  (multiplies the colours, white = original)");
            Color before = _tint;
            for (int row = 0; row < 3; row++)
            {
                using (new GUILayout.HorizontalScope())
                {
                    for (int i = row * 6; i < row * 6 + 6; i++)
                    {
                        Color keep = GUI.backgroundColor;
                        GUI.backgroundColor = Palette[i];
                        if (GUILayout.Button(Palette[i] == Color.white ? "orig." : "", _swatchStyle, GUILayout.Width(66f), GUILayout.Height(24f))) _tint = Palette[i];
                        GUI.backgroundColor = keep;
                    }
                }
            }
            _tint.r = Slider("Red", _tint.r, 0f, 1f);
            _tint.g = Slider("Green", _tint.g, 0f, 1f);
            _tint.b = Slider("Blue", _tint.b, 0f, 1f);
            if (_tint != before) Apply(actor, TintSlot(_colorPart), ColorUtility.ToHtmlStringRGB(_tint));

            //Recolour: texture copy, applied with the button (it takes a moment)
            GUILayout.Space(6f);
            GUILayout.Label("<b>Recolour</b>  (turns the colours of the texture)");
            _hue.Hue = Slider("Hue", _hue.Hue, -180f, 180f, "0");
            _hue.Saturation = Slider("Saturation", _hue.Saturation, 0f, 2f);
            _hue.Brightness = Slider("Brightness", _hue.Brightness, 0.3f, 1.7f);
            _hue.KeepSkin = GUILayout.Toggle(_hue.KeepSkin, " Protect skin tones");
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply recolour")) Apply(actor, HueSlot(_colorPart), _hue.ToString());
                if (GUILayout.Button("Undo recolour"))
                {
                    _hue = HueShift.Neutral;
                    Apply(actor, HueSlot(_colorPart), _hue.ToString());
                }
                if (GUILayout.Button("Undo tint"))
                {
                    _tint = Color.white;
                    Apply(actor, TintSlot(_colorPart), "FFFFFF");
                }
            }

            //Texture mods: export the part's textures, edit them outside the game, reload
            GUILayout.Space(6f);
            GUILayout.Label("<b>Texture mods</b>  (edit the outfit's images in any paint program)");
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export textures")) _message = TextureTools.Export(Recolor.RenderersOf(AvatarOf(actor), _colorPart), ModelName(actor.gameObject));
                if (GUILayout.Button("Reload texture mods")) _message = TextureTools.Reload();
            }
        }

        private static float Slider(string label, float value, float min, float max, string format = "0.00")
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(label + " " + value.ToString(format), GUILayout.Width(120f));
                return GUILayout.HorizontalSlider(value, min, max);
            }
        }

        //"in0102" -> "  Duan Hong'er", from the game's character data, so models are easier to find
        private Dictionary<string, string> _exteriorNames;
        private string ExteriorNameOf(string model)
        {
            if (_exteriorNames == null)
            {
                _exteriorNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var e in Game.Data.Get<CharacterExterior>().Values)
                    {
                        string m = e.Model;
                        string n = ((e.SurName ?? "") + (e.Name ?? "")).Trim();
                        if (!string.IsNullOrEmpty(m) && n.Length > 0 && !_exteriorNames.ContainsKey(m)) _exteriorNames[m] = n;
                    }
                }
                catch { }
            }
            return _exteriorNames.TryGetValue(model, out string name) ? "   <color=#9a9a9a>" + Regex.Replace(name, @"\s+", " ") + "</color>" : "";
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
            catch (Exception e) { Debug.LogError("[DevToolsProbe] Input: " + e.Message); }
        }

        public void OnKeyDown(Heluo.Controller.Key key) { }
        public void OnKeyUp(Heluo.Controller.Key key) { }
        public void OnKeyHeld(Heluo.Controller.Key key) { }
        public void OnRightAxisMove(Vector2 axis) { }
        public void OnLeftAxisMove(Vector2 axis) { }
        public void OnDPadAxisMove(Vector2 axis) { }
        public void OnThirdAxisMove(float axis) { }

        private void Styles()
        {
            if (_background != null) return;
            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0.04f, 0.04f, 0.06f, 0.88f));
            _background.Apply();
            _rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, richText = true };
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _smallStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            _swatchStyle = new GUIStyle(GUI.skin.button);
            _swatchStyle.normal.background = Texture2D.whiteTexture;
            _swatchStyle.hover.background = Texture2D.whiteTexture;
            _swatchStyle.normal.textColor = Color.black;
        }
    }
}
