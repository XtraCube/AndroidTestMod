using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace AuthFix;

[BepInAutoPlugin("dev.xtracube.authfix")]
// ReSharper disable once ClassNeverInstantiated.Global
public partial class AuthPlugin : BasePlugin
{
    private static AuthPlugin Instance { get; set; } = null!;

    private ConfigEntry<bool> KeyboardMode { get; set; }

    [LibraryImport("libstarlight.so", EntryPoint = "get_string", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial nint get_string(string key);

    [LibraryImport("libstarlight.so", EntryPoint = "get_lobby", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial nint get_lobby();

    [LibraryImport("libstarlight.so", EntryPoint = "quit_app")]
    private static unsafe partial void quit_app();

    [LibraryImport("libstarlight.so", EntryPoint = "open_url", StringMarshalling = StringMarshalling.Utf8)]
    // ReSharper disable once UnusedMethodReturnValue.Local
    private static unsafe partial nint open_url(string url);

    private static bool _ranLobbyJoin;
    private static bool _useKeyboard;

    internal static bool UseKeyboardMode => Instance?.KeyboardMode?.Value ?? _useKeyboard;

    private static void ForceKeyboardMode()
    {
        var oldType = ActiveInputManager.currentControlType;
        ActiveInputManager.currentControlType = ActiveInputManager.InputType.Keyboard;

        if (HudManager.InstanceExists)
        {
            var joystick = HudManager.Instance.joystick;
            if (joystick == null || joystick.TryCast<KeyboardJoystick>() == null)
            {
                HudManager.Instance.SetTouchType(ControlTypes.Keyboard);
            }
        }

        if (oldType != ActiveInputManager.InputType.Keyboard)
        {
            ActiveInputManager.CurrentInputSourceChanged?.Invoke();
        }
    }

    public static string GetLobby()
    {
        return Marshal.PtrToStringUTF8(get_lobby()) ?? string.Empty;
    }
    
    public static string GetString(string key)
    {
        return Marshal.PtrToStringUTF8(get_string(key)) ?? string.Empty;
    }

    public override void Load()
    {
        Instance = this;

        KeyboardMode = Config.Bind("General", "Keyboard Mode", false, "Improves keyboard support, but may break regular touch behaviour.");
        _useKeyboard = KeyboardMode.Value;

        var coroutines = AddComponent<Coroutines>();

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), Id);
        ServerManager.DefaultRegions = new Il2CppReferenceArray<IRegionInfo>([]);
        
        SceneManager.add_sceneLoaded((Action<Scene, LoadSceneMode>) ((scene, _) =>
        {
            if (scene.name == "MainMenu")
            {
                ModManager.Instance.ShowModStamp();

                GameObject exitGameButton = FindInactiveByName("ExitGameButton");

                if (exitGameButton != null)
                {
                    exitGameButton.GetComponent<ConditionalHide>().enabled = false;
                    exitGameButton.SetActive(true);
                }

                GameObject adsButton = FindInactiveByName("AdsButton");

                // No Standalone script to manage it, it's managed somewhere else.
                if (adsButton != null)
                {
                    adsButton.transform.localScale = Vector3.zero;
                    adsButton.GetComponent<PassiveButton>().enabled = false;
                    adsButton.SetActive(false);
                }

                if (!_ranLobbyJoin)
                {
                    coroutines.StartCoroutine(WaitForLogin().WrapToIl2Cpp());
                }
            }
        }));
    }

    public System.Collections.IEnumerator WaitForLogin()
    {
        if (string.IsNullOrEmpty(GetLobby()))
        {
            yield break;
        }

        while (EOSManager.Instance == null)
        {
            yield return null;
        }

        var eos = EOSManager.Instance;

        while (string.IsNullOrEmpty(eos.FriendCode))
        {
            yield return null;
        }
        while (!StoreManager.Instance.FinishedInitializationFlow)
        {
            yield return null;
        }

        var parts = GetLobby().Split(["\r\n", "\n"], StringSplitOptions.None);

        if (parts.Length < 2)
        {
            yield break;
        }

        string region = parts[0];
        string code = parts[1];

        var normalizedCandidates = new[]
        {
            NormalizeHost(region)
        }.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedRegion = ServerManager.Instance.AvailableRegions.ToList().FirstOrDefault(r =>
            r.Servers.Any(s => normalizedCandidates.Contains(NormalizeHost(s.Ip))) || normalizedCandidates.Contains(NormalizeHost(r.PingServer)));

        string regions = "";

        foreach (var regionname in ServerManager.Instance.AvailableRegions)
        {
            regions += $"{regionname.Name} ({regionname.PingServer}),";
        }

        if (selectedRegion != null)
        {
            ServerManager.Instance.SetRegion(selectedRegion);
            AmongUsClient.Instance.StartCoroutine(AmongUsClient.Instance.CoFindGameInfoFromCodeAndJoin(GameCode.GameNameToInt(code)));
        }
        else
        {
            Log.LogWarning($"selected region {region} is null :( (how?)");
            Log.LogWarning(regions);
        }
        _ranLobbyJoin = true;
    }

    static string NormalizeHost(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        input = input.Trim();

        if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            input = "http://" + input;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            input = uri.Host;
        }

        if (input.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            input = input[4..];

        return input.ToLowerInvariant();
    }

    public static GameObject FindInactiveByName(string name)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        Il2CppSystem.Collections.Generic.List<GameObject> rootObjects = new Il2CppSystem.Collections.Generic.List<GameObject>();
        activeScene.GetRootGameObjects(rootObjects);

        foreach (GameObject root in rootObjects)
        {
            // GetComponentsInChildren<Transform>(true) finds all children, active or not
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == name) return child.gameObject;
            }
        }
        return null;
    }

    [HarmonyPatch(typeof(ActiveInputManager), nameof(ActiveInputManager.OnLastActiveControllerChanged))]
    static class ActiveInputManager_LastControllerPatch
    {
        public static bool Prefix()
        {
            if (!UseKeyboardMode)
            {
                return true;
            }

            if (ActiveInputManager.Instance != null && ActiveInputManager.Instance.lastUsedController != null)
            {
                ActiveInputManager.Instance.lastUsedController = null;
            }

            ForceKeyboardMode();
            return false;
        }
    }

    [HarmonyPatch(typeof(ActiveInputManager), nameof(ActiveInputManager.UpdateActiveControlType))]
    static class ActiveInputManager_UpdatePatch
    {
        public static bool Prefix()
        {
            if (!UseKeyboardMode)
            {
                return true;
            }

            if (ActiveInputManager.Instance != null && ActiveInputManager.Instance.lastUsedController != null)
            {
                ActiveInputManager.Instance.lastUsedController = null;
            }

            ForceKeyboardMode();
            return false;
        }
    }

    [HarmonyPatch(typeof(ActiveInputManager), nameof(ActiveInputManager.ShouldEnableTouch))]
    static class ActiveInputManager_ShouldEnableTouchPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!UseKeyboardMode)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.SetTouchType))]
    static class HudManager_SetTouchTypePatch
    {
        public static void Prefix(ref ControlTypes type)
        {
            if (!UseKeyboardMode)
            {
                return;
            }

            type = ControlTypes.Keyboard;
        }
    }

    [HarmonyPatch(typeof(SceneChanger), nameof(SceneChanger.ExitGame))]
    public static class ExitPatch
    {
        public static bool Prefix()
        {
            // Call our custom quit implementation through JNI
            quit_app();
            return false;
        }
    }

    [HarmonyPatch(typeof(AdsManager), nameof(AdsManager.InitLevelPlay))]
    public static class DisableAdsPatch
    {
        public static bool Prefix()
        {
            // WebView loaded by LevelPlay can cause crashing and lag.
            return false;
        }
    }

    [HarmonyPatch(typeof(SplashManager), nameof(SplashManager.Update))]
    public static class SkipIntroPatch
    {
        public static void Prefix(SplashManager __instance)
        {
            if (__instance.doneLoadingRefdata && !__instance.startedSceneLoad)
            {
                __instance.sceneChanger.AllowFinishLoadingScene();
                __instance.startedSceneLoad = true;
            }
        }
    }

    [HarmonyPatch(typeof(ServerDropdown), nameof(ServerDropdown.FillServerOptions))]
    public static class ServerDropdownPatch
    {
        private static bool RegionEquals(IRegionInfo region, IRegionInfo other)
        {
            return region.Name == other.Name &&
                   region.TranslateName == other.TranslateName &&
                   region.PingServer == other.PingServer &&
                   region.TargetServer == other.TargetServer &&
                   region.Servers.All(s => other.Servers.Any(x => x.Equals(s)));
        }

        public static bool Prefix(ServerDropdown __instance)
        {
            if (ServerManager.Instance.AvailableRegions.Count <= 3)
            {
                // Don't adjust for small region lists
                return true;
            }
            
            var num = 0;
            __instance.background.size = new Vector2(8.4f, 4.8f);

            foreach (var regionInfo in ServerManager.Instance.AvailableRegions)
            {
                var findingGame = SceneManager.GetActiveScene().name is "FindAGame";

                if (RegionEquals(ServerManager.Instance.CurrentRegion, regionInfo))
                {
                    __instance.defaultButtonSelected = __instance.firstOption;
                    __instance.firstOption.ChangeButtonText(
                        TranslationController.Instance.GetStringWithDefault(
                            regionInfo.TranslateName,
                            regionInfo.Name));
                }
                else
                {
                    var region = regionInfo;
                    var serverListButton = __instance.ButtonPool.Get<ServerListButton>();
                    var x = num % 2 == 0 ? -2 : 2;
                    if (findingGame)
                    {
                        x += 2;
                    }

                    var y = -0.55f * (num / 2f);
                    serverListButton.transform.localPosition = new Vector3(x, __instance.y_posButton + y, -1f);
                    serverListButton.transform.localScale = Vector3.one;
                    serverListButton.Text.text =
                        TranslationController.Instance.GetStringWithDefault(
                            regionInfo.TranslateName,
                            regionInfo.Name);
                    serverListButton.Text.ForceMeshUpdate();
                    serverListButton.Button.OnClick.RemoveAllListeners();
                    serverListButton.Button.OnClick.AddListener(
                        (UnityAction)(() => { __instance.ChooseOption(region); }));
                    __instance.controllerSelectable.Add(serverListButton.Button);
                    __instance.background.transform.localPosition = new Vector3(
                        findingGame ? 2f : 0f,
                        __instance.initialYPos + -0.3f * (num / 2f),
                        0f);
                    __instance.background.size = new Vector2(__instance.background.size.x, 1.2f + 0.6f * (num / 2f));
                    num++;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(EOSManager), nameof(EOSManager.LoginWithCorrectPlatformImpl))]
    public static class AuthPatch
    {
        // ReSharper disable once InconsistentNaming
        public static bool Prefix(EOSManager __instance, [HarmonyArgument(0)] OnLoginCallback successCallbackIn)
        {
            var loginOptions = new LoginOptions();
            var credentials = new Credentials();
            credentials.Token = new Utf8String("DUMMY");
            credentials.Type = ExternalCredentialType.ItchioKey;
            loginOptions.Credentials = new Il2CppSystem.Nullable<Credentials>(credentials);

            // Callback must be pinned to avoid GC collecting it
            GCHandle.Alloc(successCallbackIn, GCHandleType.Pinned);
            __instance.PlatformInterface.GetConnectInterface().Login(ref loginOptions, null, successCallbackIn);
            __instance.stopTimeOutCheck = true;

            return false;
        }
    }

    [HarmonyPatch(typeof(StoreManager), nameof(StoreManager.InitiateStorePurchaseStar))]
    public static class DisableStarBuyPatch
    {
        public static bool Prefix()
        {
            var purchasePopUp = StoreMenu.Instance.plsWaitModal;
            purchasePopUp.waitingText.gameObject.SetActive(false);
            purchasePopUp.titleText.text = GetString("starlight_iap_not_supported_title");
            purchasePopUp.infoText.text = GetString("starlight_iap_not_supported_desc");
            purchasePopUp.infoText.gameObject.SetActive(true);
            purchasePopUp.controllerFocusHolder.gameObject.SetActive(true);
            purchasePopUp.closeButton.gameObject.SetActive(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Constants), nameof(Constants.OpenURL))]
    public static class OpenURLPatch
    {
        public static bool Prefix([HarmonyArgument(0)] string url)
        {
            open_url(url);
            return false;
        }
    }
}

public class Coroutines : MonoBehaviour;
