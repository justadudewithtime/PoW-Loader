using BepInEx;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

using XUnity.ResourceRedirector;

namespace ModAPI
{
    /*
     * Actual ResourceRedirectManager that handles managing the different redirect paths.
     * We have hooked the different asset loading routines (in different files), however if something still goes through, we use XUnity.ResourceRedirector to catch them directly in the asset loading pipeline.
    */

    public class ResourceRedirectManager
    {
        //Redirect Dictionary
        private StringComparer _IgnoreCaseStringComparer = StringComparer.OrdinalIgnoreCase;

        //Path redirections are used by both the asset loading hook, but also the other AssetRedirectors
        public Dictionary<string, List<string>> PathRedirections;

        public Encoding PathEncoding = Encoding.UTF8;
        private bool _ForceOriginalNextLoad = false;

        public void ForceOriginalLoadInNextLoad()
        {
            _ForceOriginalNextLoad = true;
        }

        //The game asks for assets as "Assets/Image/..." or with backslashes, our redirects are stored as "Image/..."
        private static string NormalizePath(string path)
        {
            path = path.Replace("\\", "/");
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(7);
            }

            return path;
        }

        public bool ExistsRedirect(string path)
        {
            return PathRedirections.ContainsKey(NormalizePath(path));
        }

        public string GetRedirect(string path)
        {
            if (PathRedirections.TryGetValue(NormalizePath(path), out List<string> result) && result.Count > 0)
            {
                return result[result.Count - 1];
            }

            return string.Empty;
        }

        public List<string> GetAllRedirects(string path)
        {
            PathRedirections.TryGetValue(NormalizePath(path), out List<string> result);
            return result;
        }

        private string GetRedirectedFilePath(string path)
        {
            return Paths.GameRootPath + Path.DirectorySeparatorChar + GetRedirect(path) + Path.DirectorySeparatorChar + path;
        }

        //A variant of the images to use where a mod has one, set by a mod before the images are loaded (empty: none).
        //With "Christmas", Image/ui/uimain/Main_title_Christmas.png is used in place of Image/ui/uimain/Main_title.png.
        public string ImageVariant { get; set; } = "";

        //The variant of an image path if the mods have that file, otherwise the path itself
        private string VariantPath(string path)
        {
            if (string.IsNullOrEmpty(ImageVariant))
            {
                return path;
            }

            string extension = Path.GetExtension(path);
            string variant = path.Substring(0, path.Length - extension.Length) + "_" + ImageVariant + extension;
            return ExistsRedirect(variant) ? variant : path;
        }

        private Texture2D ReadTexture(string path)
        {
            Texture2D texture2D = new Texture2D(2, 2);
            texture2D.LoadImage(File.ReadAllBytes(GetRedirectedFilePath(VariantPath(path))));
            return texture2D;
        }

        //Asset Managment/Loading/Caching
        private Dictionary<string, object> _AssetCache;

        private T LoadCached<T>(string path, Func<string, T> load) where T : class
        {
            path = NormalizePath(path);
            //The same .png can be asked for as Texture2D and as Sprite, so the type is part of the key
            string key = typeof(T).FullName + "|" + path;
            if (_AssetCache.TryGetValue(key, out object cached))
            {
                return cached as T;
            }

            if (!ExistsRedirect(path))
            {
                return default;
            }

            T asset = load(path);
            _AssetCache[key] = asset;
            return asset;
        }

        private Texture2D LoadTexture2D(string path)
        {
            return LoadCached(path, ReadTexture);
        }

        private Sprite LoadSprite(string path)
        {
            return LoadCached(path, p =>
            {
                Texture2D texture2D = ReadTexture(p);
                return Sprite.Create(texture2D, new Rect(0f, 0f, texture2D.width, texture2D.height), new Vector2(texture2D.width / 2, texture2D.height / 2));
            });
        }

        public Sprite GetRedirectedSprite(string path)
        {
            return LoadSprite(path);
        }

        //Sprites built for replacing sprites inside prefabs. Kept apart from _AssetCache since they copy pivot, pixelsPerUnit and border of the sprite they replace.
        private Dictionary<string, Sprite> _PrefabSpriteCache;

        private Sprite LoadPrefabSprite(string path, Sprite original)
        {
            if (_PrefabSpriteCache.TryGetValue(path, out Sprite cached))
            {
                return cached;
            }

            //We need our own texture here. The texture of the original sprite is often a sprite atlas shared with other sprites (item icons etc.),
            //overwriting it with LoadImage replaced the whole atlas and made those sprites disappear until a restart.
            Texture2D texture2D = ReadTexture(path);

            Vector2 pivot = new Vector2(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
            Sprite sprite = Sprite.Create(texture2D, new Rect(0f, 0f, texture2D.width, texture2D.height), pivot, original.pixelsPerUnit, 0, SpriteMeshType.FullRect, original.border);
            sprite.name = original.name;

            _PrefabSpriteCache[path] = sprite;
            return sprite;
        }

        //Resolves the redirect path for a sprite used inside a prefab. Prefers the path the game bundles told us, falls back to our own files by name.
        private string GetPrefabImagePath(string imageFileName)
        {
            if (assetNameToPath.TryGetValue(imageFileName, out string path) && ExistsRedirect(path))
            {
                return path;
            }

            if (_RedirectFileNameToPath.TryGetValue(imageFileName, out path) && path != null)
            {
                return path;
            }

            return null;
        }

        private TextAsset LoadText(string path)
        {
            return LoadCached(path, p => new TextAsset(Encoding.UTF8.GetString(File.ReadAllBytes(GetRedirectedFilePath(p)))));
        }

        //XUnity.ResourceRedirector stuff
        public void AssetLoading(AssetLoadingContext context)
        {
            string path = NormalizePath(context.Parameters.Name);

            //Debug.LogError("AssetLoading: " + context.Parameters.Name);
            string rootRedirect = GetRedirect(path);
            if (string.IsNullOrEmpty(rootRedirect) || _ForceOriginalNextLoad) //_ForceOriginalNextLoad is resetted in AssetLoaded hook
            {
                //Debug.Log("[ResourceRedirectManager] Found no redirect for " + path);
                context.Complete(false, false, false);
                return;
            }

            if (context.Parameters.Type == typeof(Texture2D))
            {
                context.Asset = LoadTexture2D(path);
            }
            else if (context.Parameters.Type == typeof(Sprite))
            {
                context.Asset = LoadSprite(path);
            }
            else if (context.Parameters.Type == typeof(TextAsset))
            {
                context.Asset = LoadText(path);
            }
            else if (context.Parameters.Type == typeof(Shader))
            {
                Debug.LogError("Loading Shader " + context.Parameters.Name);
            }

            context.Complete(true, true, true);
        }

        public Dictionary<string, string> assetNameToPath;

        public void AssetLoaded(AssetLoadedContext context)
        {
            //Debug.LogError("AssetLoaded: " + context.Parameters.Name);
            if (!_ForceOriginalNextLoad && Path.GetExtension(context.Parameters.Name) == ".prefab")
            {
                //It is a prefab, we want to check for child GameObjects and replace them if needed.
                //Hidden children too: badges such as the life menu "NEW" are hidden in the prefab and only shown later.
                GameObject prefab = context.Asset as GameObject;
                foreach (UnityEngine.UI.Image childImage in prefab.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    //Only plain Images, subclasses manage their sprite themselves
                    if (childImage.GetType() != typeof(UnityEngine.UI.Image) || childImage.sprite == null)
                    {
                        continue;
                    }

                    string path = GetPrefabImagePath(childImage.sprite.name + ".png");
                    if (path != null)
                    {
                        childImage.sprite = LoadPrefabSprite(path, childImage.sprite);
                    }
                }
            }

            _ForceOriginalNextLoad = false;
            context.Complete(false);
        }

        public void AsyncAssetLoading(AsyncAssetLoadingContext context)
        {
            //Debug.LogError("AsyncAssetLoading: " + context.Parameters.Name);
            context.Complete(false, false, false);
        }

        public void AssetBundleLoading(AssetBundleLoadingContext context)
        {
            //Debug.LogError("AssetBundleLoading: " + context.Parameters.Path);
            context.Complete(false, false, false);
        }

        public void AssetBundleLoaded(AssetBundleLoadedContext context)
        {
            //Caching filenames to their actual path. We need this to modify prefab game objects later on.
            foreach (string assetName in context.Bundle.GetAllAssetNames())
            {
                if (assetNameToPath.ContainsKey(assetName))
                {
                    Debug.LogError("[Resource Redirector] An Asset with name " + assetName + " already exists in teh assetNameToPath list. This is a big issue since we need unique asset names!");
                }
                else
                {
                    string path = NormalizePath(assetName);
                    assetNameToPath[Path.GetFileName(path)] = path;
                }
            }
            //Debug.LogError("AssetBundleLoaded: " + context.Parameters.Path);
            context.Complete(false);
        }

        public void AsyncAssetBundleLoading(AsyncAssetBundleLoadingContext context)
        {
            //Debug.LogError("AsyncAssetBundleLoading: " + context.Parameters.Path);
            context.Complete(false, false, false);
        }

        public void ResourceLoaded(ResourceLoadedContext context)
        {
            //Debug.LogError("ResourceLoaded: " + context.Parameters.Path);
            context.Complete(false);
        }

        //Constructor
        private ResourceRedirectManager()
        {
            PathRedirections = new Dictionary<string, List<string>>(_IgnoreCaseStringComparer);
            _AssetCache = new Dictionary<string, object>(_IgnoreCaseStringComparer);
            assetNameToPath = new Dictionary<string, string>(_IgnoreCaseStringComparer);
            _PrefabSpriteCache = new Dictionary<string, Sprite>(_IgnoreCaseStringComparer);
            _RedirectFileNameToPath = new Dictionary<string, string>(_IgnoreCaseStringComparer);

            //ResourceRedirection.LogAllLoadedResources = true;

            ResourceRedirection.EnableRedirectMissingAssetBundlesToEmptyAssetBundle(0);
            ResourceRedirection.DisableRecursionPermanently();

            ResourceRedirection.RegisterAssetLoadedHook(HookBehaviour.OneCallbackPerResourceLoaded, 0, AssetLoaded);
            ResourceRedirection.RegisterAssetLoadingHook(0, AssetLoading);
            ResourceRedirection.RegisterAsyncAssetLoadingHook(0, AsyncAssetLoading);

            ResourceRedirection.RegisterAssetBundleLoadingHook(0, AssetBundleLoading);
            ResourceRedirection.RegisterAssetBundleLoadedHook(0, AssetBundleLoaded);
            ResourceRedirection.RegisterAsyncAssetBundleLoadingHook(0, AsyncAssetBundleLoading);

            ResourceRedirection.RegisterResourceLoadedHook(HookBehaviour.OneCallbackPerResourceLoaded, 0, ResourceLoaded);
        }

        //Singleton stuff
        private static ResourceRedirectManager instance = null;

        public static ResourceRedirectManager GetInstance()
        {
            if (instance == null)
            {
                instance = new ResourceRedirectManager();
            }

            return instance;
        }

        /// <summary>
        /// Adds all files including files in subfolders in a directory to the redirect list. Paths
        /// from the given directory must be equivilant to the paths inside the game files. If the
        /// same file exists more than once the LAST registered one will be remembered. If the game
        /// tries to read a file that has a redirection setup, we will redirect the load to our own file.
        /// Example: If we registered "ModResources\Translate" and "Config\chs\Battle\Buffer\a.json"
        ///          is a valid path inside the Translate folder then if the game tries to load
        /// "Config\chs\Battle\Buffer\a.json" from the game files it will be redirected to "GamePath\ModResources\Translate\Config\chs\Battle\Buffer\a.json".
        /// </summary>
        /// <param name="path">Relative path to game root folder</param>
        public void AddRessourceFolder(string path)
        {
            string absolutePath = Paths.GameRootPath + Path.DirectorySeparatorChar + path;
            if (Directory.Exists(absolutePath))
            {
                // This path is a valid directory
                UnityEngine.Debug.Log("[ResourceRedirectManager] Adding Ressource Folder " + absolutePath + " as " + path);
                _AddedFileCount = 0;
                AddDirectoryToPathRedirection(path, "");
                UnityEngine.Debug.Log("[ResourceRedirectManager] Added " + _AddedFileCount + " redirects from " + path);
            }
            else
            {
                UnityEngine.Debug.LogError("[ResourceRedirectManager] Error while adding ressource folder: " + absolutePath + " is not a valid directory!");
            }
        }

        private void AddDirectoryToPathRedirection(string rootPath, string directoryPath)
        {
            string absolutePath = Paths.GameRootPath + Path.DirectorySeparatorChar + rootPath + Path.DirectorySeparatorChar + directoryPath;
            if (Directory.Exists(absolutePath))
            {
                DirectoryInfo dInfo = new DirectoryInfo(absolutePath);
                var subdirectories = dInfo.EnumerateDirectories();
                foreach (var dir in subdirectories)
                {
                    string newDirectoryPath = directoryPath;
                    if (!string.IsNullOrEmpty(directoryPath))
                    {
                        newDirectoryPath += Path.DirectorySeparatorChar;
                    }
                    newDirectoryPath += dir.Name;
                    AddDirectoryToPathRedirection(rootPath, newDirectoryPath);
                }

                var files = dInfo.EnumerateFiles();
                foreach (var file in files)
                {
                    AddFileToPathRedirection(rootPath, directoryPath + Path.DirectorySeparatorChar + file.Name);
                }
            }
            else
            {
                UnityEngine.Debug.LogError("[ResourceRedirectManager] Internal Error while adding ressource folder: " + absolutePath + " is not a valid directory!");
            }
        }

        private void AddFileToPathRedirection(string rootPath, string path)
        {
            //Game uses / instead of the Path.DirectorySeparatorChar which is \
            path = path.Replace("\\", "/");
            rootPath = rootPath.Replace("\\", "/");

            //Not logging every file here, with thousands of files that slowed down the start and flooded the log. AddRessourceFolder logs the total.
            _AddedFileCount++;
            if (PathRedirections.TryGetValue(path, out List<string> roots))
            {
                UnityEngine.Debug.LogWarning("[ResourceRedirectManager] Mod Collision: Overwritting Resource Redirect " + path + " from " + roots[roots.Count - 1] + " to " + rootPath);
            }
            else
            {
                roots = new List<string>();
                PathRedirections[path] = roots;
            }
            roots.Add(rootPath);

            //Remembering our files by name for the prefab sprite replacement. A name used in more than one folder is ambiguous, so it is stored as null.
            string fileName = Path.GetFileName(path);
            if (_RedirectFileNameToPath.TryGetValue(fileName, out string knownPath) && !string.Equals(knownPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _RedirectFileNameToPath[fileName] = null;
            }
            else
            {
                _RedirectFileNameToPath[fileName] = path;
            }
        }

        //File name => path inside the resource folders, null if the file name is not unique
        private Dictionary<string, string> _RedirectFileNameToPath;

        private int _AddedFileCount;
    }
}