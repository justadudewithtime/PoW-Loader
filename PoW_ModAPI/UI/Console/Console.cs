using UnityEngine;

namespace ModAPI.UI
{
    class Console
    {
        private static GameObject _Console;
        public static void Init()
        {
            _Console = new GameObject();
            _Console.AddComponent<ConsoleScript>();
            GameObject.DontDestroyOnLoad(_Console);
        }

        public static void Unload()
        {
            GameObject.Destroy(_Console);
        }
    }
}
