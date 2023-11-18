using BepInEx;
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MoreSuits
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class MoreSuitsMod : BaseUnityPlugin
    {
        private const string modGUID = "x753.More_Suits";
        private const string modName = "More Suits";
        private const string modVersion = "1.1.0";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static MoreSuitsMod Instance;

        public static string SuitsFolder;
        public static bool SuitsAdded = false;

        private void Awake()
        {
            SuitsFolder = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"suits");

            if (Instance == null)
            {
                Instance = this;
            }

            harmony.PatchAll();
            Logger.LogInfo($"Plugin {modName} is loaded!");
        }

        [HarmonyPatch(typeof(StartOfRound))]
        internal class StartOfRoundPatch
        {
            [HarmonyPatch("Start")]
            [HarmonyPrefix]
            static void StartPatch(ref StartOfRound __instance)
            {
                try
                {
                    if (!SuitsAdded)
                    {
                        for (int i = 0; i < __instance.unlockablesList.unlockables.Count; i++)
                        {
                            UnlockableItem unlockableItem = __instance.unlockablesList.unlockables[i];
                            if (unlockableItem.suitMaterial != null && unlockableItem.alreadyUnlocked)
                            {
                                // Get all .png files in the same folder as the mod
                                string[] pngFiles = Directory.GetFiles(SuitsFolder, "*.png");

                                // Create new suits for each .png
                                foreach (string texturePath in pngFiles)
                                {
                                    UnlockableItem newSuit;
                                    Material newMaterial;

                                    if (Path.GetFileNameWithoutExtension(texturePath) == "default")
                                    {
                                        newSuit = unlockableItem;
                                        newMaterial = newSuit.suitMaterial;
                                    }
                                    else
                                    {
                                        // Serialize and deserialize to create a deep copy of the original suit item
                                        string json = JsonUtility.ToJson(unlockableItem);
                                        newSuit = JsonUtility.FromJson<UnlockableItem>(json);

                                        newMaterial = Instantiate(newSuit.suitMaterial);
                                    }

                                    string filePath = Path.Combine(SuitsFolder, texturePath);
                                    byte[] fileData = File.ReadAllBytes(filePath);
                                    Texture2D texture = new Texture2D(2, 2);
                                    texture.LoadImage(fileData);

                                    newMaterial.mainTexture = texture;

                                    // Optional modification of other properties like normal maps, emission, etc
                                    // https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@14.0/manual/Lit-Shader.html
                                    try
                                    {
                                        string advancedJsonPath = Path.Combine(SuitsFolder, "advanced", Path.GetFileNameWithoutExtension(texturePath) + ".json");
                                        if (File.Exists(advancedJsonPath))
                                        {
                                            string[] lines = File.ReadAllLines(advancedJsonPath);

                                            foreach (string line in lines)
                                            {
                                                string[] keyValue = line.Trim().Split(':');
                                                if (keyValue.Length == 2)
                                                {
                                                    string keyData = keyValue[0].Trim('"', ' ', ',');
                                                    string valueData = keyValue[1].Trim('"', ' ', ',');

                                                    if (float.TryParse(valueData, out float floatValue))
                                                    {
                                                        newMaterial.SetFloat(keyData, floatValue);
                                                    }
                                                    else if (valueData == "KEYWORD")
                                                    {
                                                        newMaterial.EnableKeyword(keyData);
                                                    }
                                                    else if (valueData.Contains(".png"))
                                                    {
                                                        string advancedTexturePath = Path.Combine(SuitsFolder, "advanced", valueData);
                                                        byte[] advancedTextureData = File.ReadAllBytes(advancedTexturePath);
                                                        Texture2D advancedTexture = new Texture2D(2, 2);
                                                        advancedTexture.LoadImage(advancedTextureData);

                                                        newMaterial.SetTexture(keyData, advancedTexture);
                                                    }
                                                    else if (TryParseVector4(valueData, out Vector4 vectorValue))
                                                    {
                                                        newMaterial.SetVector(keyData, vectorValue);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.Log("Something went wrong with More Suits! Error: " + ex);
                                    }

                                    newSuit.suitMaterial = newMaterial;
                                    newSuit.unlockableName = Path.GetFileNameWithoutExtension(filePath);

                                    if (Path.GetFileNameWithoutExtension(texturePath) != "default")
                                    {
                                        __instance.unlockablesList.unlockables.Add(newSuit);
                                    }
                                }

                                SuitsAdded = true;
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log("Something went wrong with More Suits! Error: " + ex);
                }
            }
        }

        public static bool TryParseVector4(string input, out Vector4 vector)
        {
            vector = Vector4.zero;

            string[] components = input.Split(',');

            if (components.Length == 4)
            {
                if (float.TryParse(components[0], out float x) &&
                    float.TryParse(components[1], out float y) &&
                    float.TryParse(components[2], out float z) &&
                    float.TryParse(components[3], out float w))
                {
                    vector = new Vector4(x, y, z, w);
                    return true;
                }
            }

            return false;
        }
    }
}