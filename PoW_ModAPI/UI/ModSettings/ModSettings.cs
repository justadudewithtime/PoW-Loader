using System.Reflection;

using Heluo;
using Heluo.Components;
using Heluo.Controller;
using Heluo.FSM.Main;
using Heluo.UI;

using PoW_ModAPI.UI.Hooks;

namespace PoW_ModAPI.UI.ModSettings
{
    internal class ModSettings : UIForm
    {
        public override void Show()
        {
            GameCamera mainCamera = Game.MainCamera;
            if (mainCamera != null)
            {
                mainCamera.Blur(false, 0f, null, 1.5f);
            }

            InputManager input = Game.Input;
            if (input != null)
            {
                input.Push(this);
            }

            base.Show();
        }

        public override void Hide()
        {
            base.Hide();
            InputManager input = Game.Input;
            if (input != null)
            {
                input.Pop(this);
            }

            GameCamera mainCamera = Game.MainCamera;
            if (mainCamera != null)
            {
                mainCamera.Blur(true, 0f, null, 1.5f);
            }

            //Now we need to go back to the Main menu
            MainMenu mainMenu = MainMenuWrapper.GetInstance().GameObject;
            MethodInfo openMenuMethodInfo = mainMenu.GetType().GetMethod("OpenMain", BindingFlags.NonPublic | BindingFlags.Instance);
            openMenuMethodInfo.Invoke(mainMenu, new object[] { });
        }
    }
}