using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MoreSuits
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class MoreSuitsMod : BaseUnityPlugin
    {
        private const string modGUID = "x753.More_Suits";
        private const string modName = "More Suits";
        private const string modVersion = "1.3.3";

        private readonly Harmony harmony = new Harmony(modGUID);

        private static MoreSuitsMod Instance;

        public static bool SuitsAdded = false;

        public static ConfigEntry<string> DisabledSuits;
        public static ConfigEntry<bool> LoadAllSuits;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            DisabledSuits = Config.Bind("General", "Disabled Suit List", "UglySuit751.png,UglySuit752.png,UglySuit753.png", "Comma-separated list of suits that shouldn't be loaded");
            LoadAllSuits = Config.Bind("General", "Ignore !less-suits.txt", false, "If true, ignores the !less-suits.txt file and will attempt to load every suit, except those in the disabled list. This should be true if you're not worried about having too many suits.");

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
                    if (!SuitsAdded) // we only need to add the new suits to the unlockables list once per game launch
                    {
                        for (int i = 0; i < __instance.unlockablesList.unlockables.Count; i++)
                        {
                            UnlockableItem unlockableItem = __instance.unlockablesList.unlockables[i];

                            if (unlockableItem.suitMaterial != null && unlockableItem.alreadyUnlocked) // find the default suit to use as a base
                            {
                                // Get all .png files from all folders named moresuits in the BepInEx/plugins folder
                                List<string> suitsFolderPaths = Directory.GetDirectories(Paths.PluginPath, "moresuits", SearchOption.AllDirectories).ToList<string>();
                                List<string> texturePaths = new List<string>();
                                List<string> disabledSuits = DisabledSuits.Value.ToLower().Replace(".png", "").Split(',').ToList();
                                List<string> disabledDefaultSuits = new List<string>();

                                // Check through each moresuits folder for a text file called !less-suits.txt, which signals not to load any of the original suits that come with this mod
                                if (!LoadAllSuits.Value)
                                {
                                    foreach (string suitsFolderPath in suitsFolderPaths)
                                    {
                                        if (File.Exists(Path.Combine(suitsFolderPath, "!less-suits.txt")))
                                        {
                                            string[] defaultSuits = { "glow", "kirby", "knuckles", "luigi", "mario", "minion", "skeleton", "slayer", "smile" };
                                            disabledDefaultSuits.AddRange(defaultSuits); // add every default suit in the mod to the disabled suits list
                                            break;
                                        }
                                    }
                                }

                                foreach (string suitsFolderPath in suitsFolderPaths)
                                {
                                    if (suitsFolderPath != "")
                                    {
                                        string[] pngFiles = Directory.GetFiles(suitsFolderPath, "*.png");

                                        texturePaths.AddRange(pngFiles);
                                    }
                                }

                                texturePaths.Sort();

                                // Create new suits for each .png
                                foreach (string texturePath in texturePaths)
                                {
                                    // skip each suit that is in the disabled suits list
                                    if (disabledSuits.Contains(Path.GetFileNameWithoutExtension(texturePath).ToLower())) { continue; }
                                    string originalMoreSuitsPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                                    if (disabledDefaultSuits.Contains(Path.GetFileNameWithoutExtension(texturePath).ToLower()) && texturePath.Contains(originalMoreSuitsPath)) { continue; }

                                    UnlockableItem newSuit;
                                    Material newMaterial;

                                    if (Path.GetFileNameWithoutExtension(texturePath).ToLower() == "default")
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

                                    byte[] fileData = File.ReadAllBytes(texturePath);
                                    Texture2D texture = new Texture2D(2, 2);
                                    texture.LoadImage(fileData);

                                    newMaterial.mainTexture = texture;

                                    newSuit.unlockableName = Path.GetFileNameWithoutExtension(texturePath);

                                    // Optional modification of other properties like normal maps, emission, etc
                                    // https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@14.0/manual/Lit-Shader.html
                                    try
                                    {
                                        string advancedJsonPath = Path.Combine(Path.GetDirectoryName(texturePath), "advanced", newSuit.unlockableName + ".json");
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

                                                    if (keyData == "PRICE" && int.TryParse(valueData, out int intValue)) // If the advanced json has a price, set it up so it rotates into the shop
                                                    {
                                                        try
                                                        {
                                                            AddToRotatingShop(newSuit, intValue, __instance.unlockablesList.unlockables.Count);
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            Debug.Log("Something went wrong with More Suits! Could not add a suit to the rotating shop. Error: " + ex);
                                                        }
                                                    }
                                                    else if (valueData == "KEYWORD")
                                                    {
                                                        newMaterial.EnableKeyword(keyData);
                                                    }
                                                    else if (valueData.Contains(".png"))
                                                    {
                                                        string advancedTexturePath = Path.Combine(Path.GetDirectoryName(texturePath), "advanced", valueData);
                                                        byte[] advancedTextureData = File.ReadAllBytes(advancedTexturePath);
                                                        Texture2D advancedTexture = new Texture2D(2, 2);
                                                        advancedTexture.LoadImage(advancedTextureData);

                                                        newMaterial.SetTexture(keyData, advancedTexture);
                                                    }
                                                    else if (float.TryParse(valueData, out float floatValue))
                                                    {
                                                        newMaterial.SetFloat(keyData, floatValue);
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

                                    if (newSuit.unlockableName.ToLower() != "default")
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

            [HarmonyPatch("PositionSuitsOnRack")]
            [HarmonyPrefix]
            static bool PositionSuitsOnRackPatch(ref StartOfRound __instance)
            {
                List<UnlockableSuit> suits = UnityEngine.Object.FindObjectsOfType<UnlockableSuit>().ToList<UnlockableSuit>();
                suits = suits.OrderBy(suit => suit.syncedSuitID.Value).ToList();
                int index = 0;
                foreach (UnlockableSuit suit in suits)
                {
                    AutoParentToShip component = suit.gameObject.GetComponent<AutoParentToShip>();
                    component.overrideOffset = true;
                    component.positionOffset = new Vector3(-2.45f, 2.75f, -8.41f) + __instance.rightmostSuitPosition.forward * 0.18f * (float)index;
                    component.rotationOffset = new Vector3(0f, 90f, 0f);

                    index++;
                }

                return false; // don't run the original
            }
        }

        private static TerminalNode cancelPurchase;
        private static TerminalKeyword buyKeyword;
        private static void AddToRotatingShop(UnlockableItem newSuit, int price, int unlockableID)
        {
            Terminal terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
            for (int i = 0; i < terminal.terminalNodes.allKeywords.Length; i++)
            {
                if (terminal.terminalNodes.allKeywords[i].name == "Buy")
                {
                    buyKeyword = terminal.terminalNodes.allKeywords[i];
                    break;
                }
            }

            newSuit.alreadyUnlocked = false;

            newSuit.shopSelectionNode = ScriptableObject.CreateInstance<TerminalNode>();
            newSuit.shopSelectionNode.name = newSuit.unlockableName + "SuitBuy1";
            newSuit.shopSelectionNode.creatureName = newSuit.unlockableName + " suit";
            newSuit.shopSelectionNode.displayText = "You have requested to order " + newSuit.unlockableName + " suits.\nTotal cost of item: [totalCost].\n\nPlease CONFIRM or DENY.\n\n";
            newSuit.shopSelectionNode.clearPreviousText = true;
            newSuit.shopSelectionNode.shipUnlockableID = unlockableID;
            newSuit.shopSelectionNode.itemCost = price;
            newSuit.shopSelectionNode.overrideOptions = true;

            CompatibleNoun confirm = new CompatibleNoun();
            confirm.noun = ScriptableObject.CreateInstance<TerminalKeyword>();
            confirm.noun.word = "confirm";
            confirm.noun.isVerb = true;

            confirm.result = ScriptableObject.CreateInstance<TerminalNode>();
            confirm.result.name = newSuit.unlockableName + "SuitBuyConfirm";
            confirm.result.creatureName = "";
            confirm.result.displayText = "Ordered " + newSuit.unlockableName + " suits! Your new balance is [playerCredits].\n\n";
            confirm.result.clearPreviousText = true;
            confirm.result.shipUnlockableID = unlockableID;
            confirm.result.buyUnlockable = true;
            confirm.result.itemCost = price;
            confirm.result.terminalEvent = "";

            CompatibleNoun deny = new CompatibleNoun();
            deny.noun = ScriptableObject.CreateInstance<TerminalKeyword>();
            deny.noun.word = "deny";
            deny.noun.isVerb = true;

            if (cancelPurchase == null)
            {
                cancelPurchase = ScriptableObject.CreateInstance<TerminalNode>(); // we can use the same Cancel Purchase node
            }
            deny.result = cancelPurchase;
            deny.result.name = "MoreSuitsCancelPurchase";
            deny.result.displayText = "Cancelled order.\n";

            newSuit.shopSelectionNode.terminalOptions = new CompatibleNoun[] { confirm, deny };

            TerminalKeyword suitKeyword = ScriptableObject.CreateInstance<TerminalKeyword>();
            suitKeyword.name = newSuit.unlockableName + "Suit";
            suitKeyword.word = newSuit.unlockableName.ToLower() + " suit";
            suitKeyword.defaultVerb = buyKeyword;

            CompatibleNoun suitCompatibleNoun = new CompatibleNoun();
            suitCompatibleNoun.noun = suitKeyword;
            suitCompatibleNoun.result = newSuit.shopSelectionNode;
            List<CompatibleNoun> buyKeywordList = buyKeyword.compatibleNouns.ToList<CompatibleNoun>();
            buyKeywordList.Add(suitCompatibleNoun);
            buyKeyword.compatibleNouns = buyKeywordList.ToArray();

            List<TerminalKeyword> allKeywordsList = terminal.terminalNodes.allKeywords.ToList();
            allKeywordsList.Add(suitKeyword);
            allKeywordsList.Add(confirm.noun);
            allKeywordsList.Add(deny.noun);
            terminal.terminalNodes.allKeywords = allKeywordsList.ToArray();
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