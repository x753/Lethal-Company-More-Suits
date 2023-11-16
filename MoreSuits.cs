using BepInEx;
using HarmonyLib;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace MoreSuits
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class MoreSuitsMod : BaseUnityPlugin
    {
        private const string modGUID = "x753.More_Suits";
        private const string modName = "More Suits";
        private const string modVersion = "1.0.0";

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
        }
    }
}