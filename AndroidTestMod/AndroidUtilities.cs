using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace AndroidTestMod;

[BepInAutoPlugin("dev.xtracube.androidutilities")]
public partial class AndroidUtilities : BasePlugin
{
    public static ManualLogSource Logger { get; private set; }
    public static AndroidUtilities Instance { get; private set; }
    public ConfigEntry<int> TargetFrameRate { get; private set; }
    public ConfigEntry<LightSourceRendererType> LightSourceRenderMode { get; private set; }

    public AndroidUtilities()
    {
        Instance = this;
        Logger = Log;}
    
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

    // light source patch
    [HarmonyPatch(typeof(LightSource), nameof(LightSource.Initialize))]
    public static class LightSourcePatch
    {
        public static void Prefix(LightSource __instance)
        {
            __instance.rendererType = Instance.LightSourceRenderMode.Value;
        }
    }

    // here for testing purposes
    [HarmonyPatch(typeof(HatManager), nameof(HatManager.Initialize))]
    public static class HatManagerPatch
    {
        public static void Postfix(HatManager __instance)
        {
            CosmeticsUnlocker.UnlockCosmetics(__instance);
        }
    }
}