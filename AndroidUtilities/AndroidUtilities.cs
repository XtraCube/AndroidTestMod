using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace AndroidUtilities;

[BepInAutoPlugin("dev.xtracube.androidutilities")]
// ReSharper disable once ClassNeverInstantiated.Global
public partial class AndroidUtilities : BasePlugin
{
    private static AndroidUtilities Instance { get; set; }

    private ConfigEntry<int> TargetFrameRate { get; set; }
    private ConfigEntry<LightSourceRendererType> LightSourceRenderMode { get; set; }

    public AndroidUtilities()
    {
        Instance = this;
    }
    
    public override void Load()
    {
        TargetFrameRate = Config.Bind("General", "Target Frame Rate", 60, "The target frame rate of the game");
        LightSourceRenderMode = Config.Bind("General", "Light Source Mode", LightSourceRendererType.GPU, "The renderer type of the light source");
        Config.Save();

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), Id);
    }

    // frame rate patch
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.Awake))]
    public static class AmongUsClientPatch
    {
        public static void Postfix()
        { 
            Application.targetFrameRate = Instance.TargetFrameRate.Value;
        }
    }
    
    [HarmonyPatch(typeof(StoreManager), nameof(StoreManager.InitiateStorePurchaseStar))]
    public static class DisableStarBuyPatch
    {
        // ReSharper disable once InconsistentNaming
        public static bool Prefix(StoreManager __instance)
        {
            var purchasePopUp = StoreMenu.Instance.plsWaitModal;
            purchasePopUp.waitingText.gameObject.SetActive(false);
            purchasePopUp.titleText.text = "NOT SUPPORTED";
            purchasePopUp.infoText.text = "Platform Purchases are not supported in Starlight.\nBuy in the vanilla client instead.";
            purchasePopUp.infoText.gameObject.SetActive(true);
            purchasePopUp.controllerFocusHolder.gameObject.SetActive(true);
            purchasePopUp.closeButton.gameObject.SetActive(true);
            return false;
        }
    }

    // light source patch
    [HarmonyPatch(typeof(LightSource), nameof(LightSource.Initialize))]
    public static class LightSourcePatch
    {
        // ReSharper disable once InconsistentNaming
        public static void Prefix(LightSource __instance)
        {
            __instance.rendererType = Instance.LightSourceRenderMode.Value;
        }
    }
}