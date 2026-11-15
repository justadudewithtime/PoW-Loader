using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using BepInEx;

using FileHelpers;

using HarmonyLib;

using Heluo;
using Heluo.Data;
using Heluo.Resource;
using Heluo.Utility;

using ModAPI;

using Newtonsoft.Json;

using UnityEngine;

namespace PoW_ModAPI.ResourceRedirection
{
    /*
    The Datamanager class of the game reads the games own data files and generates the corresponding objects.
    What we can do is load the original, then the ones from our mods.
    We then check through the entries and check for every mod if they have it. If none have it, we load the original.
    */

    [HarmonyPatch(typeof(DataManager), "ReadData")]
    public class DataManagerRedirector_ReadData_Hook
    {
        //We are emulation the original method fully, except the part where we replace the data with our own. This includes doing stuff a bit "weirdly" so we are close to the optimized code.
        //This allows to check for changes easier than if we make the code more readable here.
        public static bool Prefix(ref DataManager __instance, string path, ref IDictionary<Type, IDictionary> ___dict, ref IResourceProvider ___resource)
        {
            path = __instance.CheckPath(path);

            bool forceSimplified = ModAPI.ModAPI.GetInstance().GetIsForcedSimplifiedChinese();
            if (forceSimplified)
            {
                path = path.Replace("/cht/", "/chs/");
            }
            ___dict = new Dictionary<Type, IDictionary>();
            Type type = typeof(Item);
            foreach (Type dataType in from t in type.Assembly.GetTypes() where t.IsSubclassOf(type) && !t.HasAttribute<Hidden>(false) select t)
            {
                Debug.Log("Loading " + dataType.Name);
                try
                {
                    IDictionary result;
                    if (!dataType.HasAttribute<JsonConfig>(false))
                    {
                        //It is a .txt file
                        Type csvDataSourceType = typeof(CsvDataSource<>).MakeGenericType(new Type[] { dataType });
                        string dataPath = path + dataType.Name + ".txt";
                        //First we load the original
                        var resourceRedirector = ResourceRedirectManager.GetInstance();
                        resourceRedirector.ForceOriginalLoadInNextLoad();
                        byte[] array;
                        try
                        {
                            array = ___resource.LoadBytes(dataPath);
                        }
                        finally
                        {
                            //Never left on for the next asset (a redirected image would load the original)
                            resourceRedirector.EndForceOriginalLoad();
                        }
                        result = ((Activator.CreateInstance(csvDataSourceType, new object[] { array }) as IDictionary));

                        List<string> redirects = resourceRedirector.GetAllRedirects(dataPath);
                        //We iterate over every redirect, load it and then replace all entries with same ID.
                        if (redirects != null)
                        {
                            for (int i = 0; i < redirects.Count; ++i)
                            {
                                string rootRedirect = redirects[i];
                                string absolutePath = Paths.GameRootPath + Path.DirectorySeparatorChar + rootRedirect + Path.DirectorySeparatorChar + path + Path.DirectorySeparatorChar + dataType.Name + ".txt";
                                byte[] data = File.ReadAllBytes(absolutePath);
                                IDictionary redirectData = Activator.CreateInstance(csvDataSourceType, new object[] { data }) as IDictionary;
                                foreach (var id in redirectData.Keys)
                                {
                                    if (result.Contains(id))
                                    {
                                        //Debug.Log("Replacing entry " + id);
                                        //Overwritting
                                        result[id] = redirectData[id];
                                    }
                                    else
                                    {
                                        Debug.LogWarning("Adding new custom entry " + id);
                                        result.Add(id, redirectData[id]);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        //It is a .json file, we do NOT allow partial overwritting of these, we just use the normal resource provider, which should be handled by the ResourceRedirectManager
                        string dataPath = "Config/" + dataType.Name + ".json";
                        byte[] array = ___resource.LoadBytes(dataPath);
                        if (array == null)
                        {
                            Debug.LogWarning("DataManager tried to load " + dataPath + " but it was not successful!");
                            continue;
                        }

                        string utf8Data = Encoding.UTF8.GetString(array);
                        Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(new Type[] { typeof(string), dataType });
                        result = (JsonConvert.DeserializeObject(utf8Data, dictionaryType) as IDictionary);
                    }

                    ___dict.Add(dataType, result);
                }
                catch (ConvertException ex)
                {
                    Debug.LogError(string.Concat(new object[]
{
                        "Error while parsing ",
                        dataType.Name,
                        "!\r\nRow : ",
                        ex.LineNumber,
                        ", Column : ",
                        ex.ColumnNumber,
                        ", FieldType = ",
                        ex.FieldType.Name,
                        ", FieldName = ",
                        ex.FieldName,
                        "\r\n",
                        ex
                    }));
                }
                catch (Exception ex2)
                {
                    Debug.LogError(string.Concat(new object[]
                    {
                        "Error while parsing ",
                        dataType.Name,
                        " !\r\n",
                        ex2
                    }));
                }
            }
            //No need to call original
            return false;
        }
    }
}