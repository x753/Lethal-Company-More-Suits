using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MoreSuits
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class MoreSuitsMod : BaseUnityPlugin
    {
        private const string modGUID = "x753.More_Suits";
        internal const string modName = "More Suits";
        internal const string modVersion = "1.4.1";

        private const int SUITS_PER_RACK = 13;

        private readonly Harmony harmony = new Harmony(modGUID);

        private static MoreSuitsMod Instance;

        public static bool SuitsAdded = false;

        public static string DisabledSuits;
        public static bool LoadAllSuits;
        public static bool MakeSuitsFitOnRack;
        public static int MaxSuits;
        public static int CurrentPage;

        public static List<Material> customMaterials = new List<Material>();

        private static bool usePageButtons;
        private static GameObject PageButton;
        private static GameObject PageText;

        private static TMP_FontAsset StolenFont;
        private static Sprite StolenHandIcon;
        private static int InteractLayer;
        private static GameObject CurrentPageTextObject;
        private static TMP_Text CurrentPageText;
        private static List<UnlockableSuit> CachedSuits = new List<UnlockableSuit>();
        private static List<UnlockableSuit[]> SuitsPages = new List<UnlockableSuit[]>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            DisabledSuits = Config.Bind("General", "Disabled Suit List", "UglySuit751.png,UglySuit752.png,UglySuit753.png", "Comma-separated list of suits that shouldn't be loaded").Value;
            LoadAllSuits = Config.Bind("General", "Ignore !less-suits.txt", false, "If true, ignores the !less-suits.txt file and will attempt to load every suit, except those in the disabled list. This should be true if you're not worried about having too many suits.").Value;
            MakeSuitsFitOnRack = Config.Bind("General", "Make Suits Fit on Rack", true, "If true, squishes the suits together so more can fit on the rack.").Value;
            MaxSuits = Config.Bind("General", "Max Suits", 100, "The maximum number of suits to load. If you have more, some will be ignored.").Value;
            
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MoreSuits.moresuits");
            if (s != null)
            {
                AssetBundle assetBundle = AssetBundle.LoadFromStream(s);
                if (assetBundle != null)
                {
                    Object[] objects = assetBundle.LoadAllAssets<Object>();
                    PageButton = objects.First(x => x.name == "PageButton") as GameObject;
                    PageText = objects.First(x => x.name == "PageText") as GameObject;
                    usePageButtons = true;
                }
                else
                    Logger.LogWarning("Failed to load PageButton Asset! Page buttons will not appear.");
            }
            else
                Logger.LogWarning("Failed to load Embedded AssetBundle! Page buttons will not appear.");
            
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            harmony.PatchAll();
            Logger.LogInfo($"Plugin {modName} is loaded!");
        }

        private void Update()
        {
            if(!usePageButtons) return;
            bool needsRefresh = false;
            CachedSuits.ForEach(x =>
            {
                if(x != null && x.gameObject != null) return;
                needsRefresh = true;
            });
            if(needsRefresh) HardReset(StartOfRound.Instance);
            if(CurrentPageTextObject == null || CurrentPageText == null) return;
            CurrentPageText.text = $"Page {CurrentPage + 1} of {SuitsPages.Count}";
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if(!usePageButtons || scene.name != "SampleSceneRelay") return;
            GameObject[] rootGameObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            // Steal Font
            GameObject sys = rootGameObjects.First(x => x.name == "Systems");
            StolenFont = sys.transform.Find("UI/Canvas/EndgameStats/Text/HeaderText").GetComponent<TextMeshProUGUI>()
                .font;
            // Steal Icon
            GameObject env = rootGameObjects.First(x => x.name == "Environment");
            Transform ts = env.transform.Find("HangarShip/Terminal/TerminalTrigger/TerminalScript");
            StolenHandIcon = ts.GetComponent<InteractTrigger>().hoverIcon;
            InteractLayer = ts.gameObject.layer;
            Transform rightRack = GetFarRightRack();
            // Clone Button for Right
            GameObject rightPageButton = Instantiate(PageButton, rightRack, true);
            rightPageButton.name = "NextPageButton";
            // Clone Button for Left
            GameObject leftPageButton = Instantiate(PageButton, rightRack, true);
            leftPageButton.name = "PreviousPageButton";
            // Create Interactable and Register
            CreateAndRegisterInteract(rightPageButton, "Next", () =>
            {
                CurrentPage += 1;
                RenderPage();
            });
            CreateAndRegisterInteract(leftPageButton, "Previous", () =>
            {
                CurrentPage -= 1;
                RenderPage();
            });
            // Position
            rightPageButton.transform.localPosition = new Vector3(0, -0.5f, -0.6f);
            leftPageButton.transform.localPosition = new Vector3(0, -0.5f, 2.6f);
            // Text
            TMP_Text leftPageText = leftPageButton.transform.GetChild(0).GetComponent<TMP_Text>();
            TMP_Text rightPageText = leftPageButton.transform.GetChild(0).GetComponent<TMP_Text>();
            leftPageText.text = "<";
            leftPageText.font = StolenFont;
            rightPageText.font = StolenFont;
            // Label
            GameObject pageText = Instantiate(PageText, rightRack, true);
            pageText.name = "PageText";
            pageText.transform.localPosition = new Vector3(0, 0.4f, 1);
            CurrentPageTextObject = pageText;
            CurrentPageText = pageText.GetComponent<TMP_Text>();
            CurrentPageText.font = StolenFont;
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
                        int originalUnlockablesCount = __instance.unlockablesList.unlockables.Count;
                        UnlockableItem originalSuit = new UnlockableItem();

                        int addedSuitCount = 0;
                        for (int i = 0; i < __instance.unlockablesList.unlockables.Count; i++)
                        {
                            UnlockableItem unlockableItem = __instance.unlockablesList.unlockables[i];

                            if (unlockableItem.suitMaterial != null && unlockableItem.alreadyUnlocked) // find the default suit to use as a base
                            {
                                originalSuit = unlockableItem;

                                // Get all .png files from all folders named moresuits in the BepInEx/plugins folder
                                List<string> suitsFolderPaths = Directory.GetDirectories(Paths.PluginPath, "moresuits", SearchOption.AllDirectories).ToList<string>();
                                List<string> texturePaths = new List<string>();
                                List<string> assetPaths = new List<string>();
                                List<string> disabledSuits = DisabledSuits.ToLower().Replace(".png", "").Split(',').ToList();
                                List<string> disabledDefaultSuits = new List<string>();

                                // Check through each moresuits folder for a text file called !less-suits.txt, which signals not to load any of the original suits that come with this mod
                                if (!LoadAllSuits)
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
                                        string[] bundleFiles = Directory.GetFiles(suitsFolderPath, "*.matbundle");

                                        texturePaths.AddRange(pngFiles);
                                        assetPaths.AddRange(bundleFiles);
                                    }
                                }

                                assetPaths.Sort();
                                texturePaths.Sort();

                                try
                                {
                                    foreach (string assetPath in assetPaths)
                                    {
                                        AssetBundle assetBundle = AssetBundle.LoadFromFile(assetPath);
                                        UnityEngine.Object[] assets = assetBundle.LoadAllAssets();

                                        foreach (UnityEngine.Object asset in assets)
                                        {
                                            if (asset is Material)
                                            {
                                                Material material = (Material)asset;
                                                customMaterials.Add(material);
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.Log("Something went wrong with More Suits! Could not load materials from asset bundle(s). Error: " + ex);
                                }

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
                                        newSuit = originalSuit;
                                        newMaterial = newSuit.suitMaterial;
                                    }
                                    else
                                    {
                                        // Serialize and deserialize to create a deep copy of the original suit item
                                        newSuit = JsonUtility.FromJson<UnlockableItem>(JsonUtility.ToJson(originalSuit));

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

                                                    if (valueData.Contains(".png"))
                                                    {
                                                        string advancedTexturePath = Path.Combine(Path.GetDirectoryName(texturePath), "advanced", valueData);
                                                        byte[] advancedTextureData = File.ReadAllBytes(advancedTexturePath);
                                                        Texture2D advancedTexture = new Texture2D(2, 2);
                                                        advancedTexture.LoadImage(advancedTextureData);

                                                        newMaterial.SetTexture(keyData, advancedTexture);
                                                    }
                                                    else if (keyData == "PRICE" && int.TryParse(valueData, out int intValue)) // If the advanced json has a price, set it up so it rotates into the shop
                                                    {
                                                        try
                                                        {
                                                            newSuit = AddToRotatingShop(newSuit, intValue, __instance.unlockablesList.unlockables.Count);
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
                                                    else if (valueData == "DISABLEKEYWORD")
                                                    {
                                                        newMaterial.DisableKeyword(keyData);
                                                    }
                                                    else if (valueData == "SHADERPASS")
                                                    {
                                                        newMaterial.SetShaderPassEnabled(keyData, true);
                                                    }
                                                    else if (valueData == "DISABLESHADERPASS")
                                                    {
                                                        newMaterial.SetShaderPassEnabled(keyData, false);
                                                    }
                                                    else if (keyData == "SHADER")
                                                    {
                                                        Shader newShader = Shader.Find(valueData);
                                                        newMaterial.shader = newShader;
                                                    }
                                                    else if (keyData == "MATERIAL")
                                                    {
                                                        foreach (Material material in customMaterials)
                                                        {
                                                            if (material.name == valueData)
                                                            {
                                                                newMaterial = Instantiate(material);
                                                                newMaterial.mainTexture = texture;
                                                                break;
                                                            }
                                                        }
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
                                        if (addedSuitCount == MaxSuits)
                                        {
                                            Debug.Log("Attempted to add a suit, but you've already reached the max number of suits! Modify the config if you want more.");
                                        }
                                        else
                                        {
                                            __instance.unlockablesList.unlockables.Add(newSuit);
                                            addedSuitCount++;
                                        }
                                    }
                                }

                                SuitsAdded = true;
                                break;
                            }
                        }

                        UnlockableItem dummySuit = JsonUtility.FromJson<UnlockableItem>(JsonUtility.ToJson(originalSuit));
                        dummySuit.alreadyUnlocked = false;
                        dummySuit.hasBeenMoved = false;
                        dummySuit.placedPosition = Vector3.zero;
                        dummySuit.placedRotation = Vector3.zero;
                        dummySuit.unlockableType = 753; // this unlockable type is not used
                        while (__instance.unlockablesList.unlockables.Count < originalUnlockablesCount + MaxSuits)
                        {
                            __instance.unlockablesList.unlockables.Add(dummySuit);
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
            internal static bool PositionSuitsOnRackPatch(ref StartOfRound __instance)
            {
                Transform rightMost;
                if (__instance != null)
                    rightMost = __instance.rightmostSuitPosition;
                else
                    rightMost = GetFarRightRack();
                RefreshSuits();
                int index = 0;
                int offset = 0;
                foreach (UnlockableSuit suit in CachedSuits)
                {
                    AutoParentToShip component = suit.gameObject.GetComponent<AutoParentToShip>();
                    component.overrideOffset = true;

                    if (usePageButtons && offset > SUITS_PER_RACK - 1)
                        offset = 0;

                    if(usePageButtons)
                    {
                        component.positionOffset =
                            new Vector3(-2.45f, 2.75f, -8.41f) + rightMost.forward * 0.18f * offset;
                        component.rotationOffset = new Vector3(0f, 90f, 0f);
                    }
                    else
                    {
                        float offsetModifier = 0.18f;
                        if (MakeSuitsFitOnRack && CachedSuits.Count > 13)
                        {
                            offsetModifier = offsetModifier / (Math.Min(CachedSuits.Count, 20) / 12f); // squish the suits together to make them all fit
                        }

                        component.positionOffset = new Vector3(-2.45f, 2.75f, -8.41f) + __instance.rightmostSuitPosition.forward * offsetModifier * (float)index;
                        component.rotationOffset = new Vector3(0f, 90f, 0f);
                    }

                    if (usePageButtons && index > SUITS_PER_RACK - 1)
                        suit.gameObject.SetActive(false);
                    
                    index++;
                    offset++;
                }
                
                FillPages();

                return false; // don't run the original
            }
        }

        private static TerminalNode cancelPurchase;
        private static TerminalKeyword buyKeyword;
        private static UnlockableItem AddToRotatingShop(UnlockableItem newSuit, int price, int unlockableID)
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
            newSuit.hasBeenMoved = false;
            newSuit.placedPosition = Vector3.zero;
            newSuit.placedRotation = Vector3.zero;

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

            return newSuit;
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

        private static void RefreshSuits()
        {
            CachedSuits = Combine(CachedSuits, FindObjectsOfType<UnlockableSuit>().ToList());
            // Remove any Deleted Suits
            CachedSuits = CachedSuits.Where(x => x != null && x.gameObject != null).ToList();
            CachedSuits = CachedSuits.OrderBy(suit => suit.syncedSuitID.Value).ToList();
        }

        private static List<T> Combine<T>(List<T> a, List<T> b)
        {
            List<T> newList = new List<T>(a);
            foreach (T t in b)
            {
                if(!newList.Contains(t))
                    newList.Add(t);
            }
            return newList;
        }

        private static void FillPages()
        {
            if(!usePageButtons) return;
            SuitsPages.Clear();
            List<UnlockableSuit> currentPage = new List<UnlockableSuit>();
            int pageLimiter = 0;
            foreach (UnlockableSuit unlockableSuit in CachedSuits)
            {
                if (pageLimiter > SUITS_PER_RACK - 1)
                {
                    SuitsPages.Add(currentPage.ToArray());
                    currentPage.Clear();
                    pageLimiter = 0;
                }
                currentPage.Add(unlockableSuit);
                pageLimiter++;
            }
            if(currentPage.Count > 0)
                SuitsPages.Add(currentPage.ToArray());
            RenderPage();
        }
        
        private static Transform GetFarRightRack()
        {
            GameObject env = SceneManager.GetActiveScene().GetRootGameObjects().First(x => x.name == "Environment");
            return env.transform.Find("HangarShip/RightmostSuitPlacement");
        }

        private static void CreateAndRegisterInteract(GameObject gameObject, string dir, Action onInteract)
        {
            gameObject.GetComponent<BoxCollider>().tag = "InteractTrigger";
            gameObject.tag = "InteractTrigger";
            gameObject.layer = InteractLayer;
            InteractTrigger interactTrigger = gameObject.AddComponent<InteractTrigger>();
            if(interactTrigger.onInteract == null)
                interactTrigger.onInteract = new InteractEvent();
            interactTrigger.onInteract.AddListener(playerController =>
            {
                if(playerController.NetworkManager.LocalClientId != playerController.playerClientId) return;
                onInteract.Invoke();
            });
            interactTrigger.hoverTip = $"{dir} Page";
            interactTrigger.hoverIcon = StolenHandIcon;
            interactTrigger.twoHandedItemAllowed = true;
            interactTrigger.interactCooldown = false;
        }

        private static void RenderPage()
        {
            if (CurrentPage < 0)
                CurrentPage = SuitsPages.Count - 1;
            if (CurrentPage > SuitsPages.Count - 1)
                CurrentPage = 0;
            SuitsPages.ForEach(x =>
            {
                foreach (UnlockableSuit unlockableSuit in x)
                    unlockableSuit.gameObject.SetActive(false);
            });
            foreach (UnlockableSuit unlockableSuit in SuitsPages[CurrentPage])
                unlockableSuit.gameObject.SetActive(true);
        }

        private static void HardReset(StartOfRound instance)
        {
            if (instance == null)
            {
                // Probably hit Quit
                CachedSuits.Clear();
                SuitsPages.Clear();
                CurrentPage = 0;
                return;
            }
            SuitsPages.Clear();
            RefreshSuits();
            StartOfRoundPatch.PositionSuitsOnRackPatch(ref instance);
            CurrentPage = 0;
            RenderPage();
        }
    }
}