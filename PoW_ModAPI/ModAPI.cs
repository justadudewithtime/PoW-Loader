using System.Collections.Generic;

using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

using HarmonyLib;

using UnityEngine;

namespace ModAPI
{
    public class PoWMod_Wrapper
    {
        public IPoWMod Mod;
        public BaseUnityPlugin ModAsPlugin;

        private readonly ConfigEntry<int> _Config_LoadOrderIndex;
        private readonly ConfigEntry<bool> _Config_Enabled;

        public PoWMod_Wrapper(IPoWMod mod)
        {
            Mod = mod;
            ModAsPlugin = Mod as BaseUnityPlugin;

            _Config_LoadOrderIndex = ModAsPlugin.Config.Bind<int>("Generic", "LoadOrderIndex", -1, "Load order index");
            _Config_Enabled = ModAsPlugin.Config.Bind<bool>("Generic", "Enabled", true, "Mod Enabled");
            ModAsPlugin.Config.Save();
        }

        public int GetLoadOrderIndex()
        {
            return _Config_LoadOrderIndex.Value;
        }

        public void SetLoadOrderIndex(int newValue)
        {
            _Config_LoadOrderIndex.Value = newValue;
            ModAsPlugin.Config.Save();
        }

        public bool IsEnabled()
        {
            return _Config_Enabled.Value;
        }
    }

    [BepInDependency("gravydevsupreme.xunity.resourceredirector", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin("plugins.modapi", "Mod API", "0.4.0")]
    [BepInProcess("PathOfWuxia.exe")]
    public class ModAPI : BaseUnityPlugin
    {
        private static readonly string _VERSION = "0.4.0";
        private ConfigEntry<bool> _Config_ForceSimplifiedChinese;

        public ModAPI()
        {
            _Config_ForceSimplifiedChinese = Config.Bind<bool>("Generic", "Force Simplified Chinese", true, "");
        }

        public bool GetIsForcedSimplifiedChinese()
        {
            return _Config_ForceSimplifiedChinese.Value;
        }

        public string GetVersion()
        {
            return _VERSION;
        }

        public static ModAPI GetInstance()
        {
            if (!Chainloader.PluginInfos.ContainsKey("plugins.modapi"))
            {
                Debug.LogError("[ModAPI] Tried to obtain ModAPI Instance but it was not loaded!");
                return null;
            }

            return Chainloader.PluginInfos["plugins.modapi"].Instance as ModAPI;
        }

        private Harmony _HM;
        public ResourceRedirectManager ResourceRedirector = ResourceRedirectManager.GetInstance();

        private bool _ModsLoaded = false;
        private readonly SortedList<int, PoWMod_Wrapper> _Mods = new SortedList<int, PoWMod_Wrapper>();
        private int _ModsHighestLoadOrderIndex = -1;
        private readonly List<PoWMod_Wrapper> _NewMods = new List<PoWMod_Wrapper>();

        public void AddMod(IPoWMod mod)
        {
            if (_ModsLoaded)
            {
                Debug.LogError("[ModAPI] The mod " + mod.GetName() + " was added after the mods have already been processed!");
            }
            else
            {
                Debug.Log("[ModAPI] Mod found: " + mod.GetName());
            }

            var modWrapper = new PoWMod_Wrapper(mod);

            int loadOrderIndex = modWrapper.GetLoadOrderIndex();
            if (loadOrderIndex == -1)
            {
                _NewMods.Add(modWrapper);
            }
            else
            {
                if (loadOrderIndex > _ModsHighestLoadOrderIndex)
                    _ModsHighestLoadOrderIndex = loadOrderIndex;

                _Mods.Add(loadOrderIndex, modWrapper);
            }
        }

        public void LoadMods()
        {
            Debug.Log("[ModAPI] Loading mods!");
            if (_ModsLoaded)
            {
                Debug.Log("[ModAPI] Mods already loaded...");
                return;
            }

            //Processing new mods and giving them a l
            for (int i = 0; i < _NewMods.Count; ++i)
            {
                Debug.Log("[ModAPI] Found new mod " + _NewMods[i].Mod.GetName());
                _ModsHighestLoadOrderIndex++;
                _NewMods[i].SetLoadOrderIndex(_ModsHighestLoadOrderIndex);
                _Mods.Add(_ModsHighestLoadOrderIndex, _NewMods[i]);
            }
            _NewMods.Clear();

            //Loading all mods
            foreach (var modEntry in _Mods)
            {
                var modWrapper = modEntry.Value;
                if (modWrapper.IsEnabled())
                {
                    Debug.Log("[ModAPI] Loading Mod " + modWrapper.Mod.GetName());
                    modWrapper.Mod.Load();
                }
            }

            _ModsLoaded = true;
        }

        private void Awake()
        {
            this.name = "ModAPI";

            //Hooking
            _HM = new Harmony("ModAPI");
            //Patching hook by hook, so one hook broken by a game update (e.g. DataManager.ReadData became ambiguous with ReadData<T>) does not stop all the others.
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(ModAPI).Assembly))
            {
                try
                {
                    _HM.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[ModAPI] Could not apply hook " + type.FullName + ": " + e.Message);
                }
            }

            //Initing in game console (basically just a log viewer right now)
            UI.Console.Init();
        }

        private void OnDestroy()
        {
            //Cleaning up in reverse order in which we set everything up.
            UI.Console.Unload();

            _HM.UnpatchAll("ModAPI");
        }
    }

    [HarmonyPatch(typeof(Heluo.Game), "OnBeforeSceneLoad")]
    public class Game_OnBeforeSceneLoad_Hook
    {
        public static bool Prefix()
        {
            Debug.Log("Game.OnBeforeSceneLoad called");
            ModAPI.GetInstance().LoadMods();

            //Call original
            return true;
        }
    }
}