using IPA;
using System;
using System.Linq;
using UnityEngine;
using IPALogger = IPA.Logging.Logger;
using BS_Utils.Utilities;
using UnityEngine.UI;
using IPA.Utilities;
using BeatSaberMarkupLanguage;
using HMUI;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using IPA.Config;
using IPA.Config.Stores;
using System.Text;
using SongRequest.Configuration;
using BS_Utils.Utilities;
using Config = IPA.Config.Config;

namespace SongRequest
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static Plugin Instance { get; private set; }
        internal static IPALogger Log { get; private set; }

        internal static WebClient WebClient;
        internal static GameMode gameMode;
        public static Button requestButton;
        public static bool SongBrowserPluginPresent;
        public static RequestFlowCoordinator requestFlowCoordinator;
        public static StringNormalization normalize = new StringNormalization();
        public static string DataPath = Path.Combine(UnityGame.UserDataPath, "SongRequests");

        [Init]
        public void Init(IPALogger logger, Config conf)
        {
            Log = logger;
            Log.Info("SongRequest initialized.");
            PluginConfig.Instance = conf.Generated<PluginConfig>();
        }


        [OnStart]
        public void OnApplicationStart()
        {
            BSEvents.OnLoad();
            BSEvents.lateMenuSceneLoadedFresh += OnLateMenuSceneLoadedFresh;

            if (!Directory.Exists(DataPath))
            {
                Directory.CreateDirectory(DataPath);
            }

            Dispatcher.Initialize();

            SongRequests.MapDatabase.LoadDatabase();
            RequestQueue.Read();
            RequestHistory.Read();

            WebClient = new WebClient();

            Log.Info(PluginConfig.Instance.PrivateKey);

            fetchSongs();

            SongBrowserPluginPresent = IPA.Loader.PluginManager.GetPlugin("Song Browser") != null;
        }

        private void OnLateMenuSceneLoadedFresh(ScenesTransitionSetupDataSO scenesTransitionSetupData)
        {
            //BSMLSettings.instance.AddSettingsMenu("Song requests", "SongRequest.Views.Menu.bsml", Instance);
            requestFlowCoordinator = BeatSaberUI.CreateFlowCoordinator<RequestFlowCoordinator>();

            try
            {
                SelectLevelCategoryViewController levelListViewController = Resources.FindObjectsOfTypeAll<SelectLevelCategoryViewController>().Last();

                if (levelListViewController)
                {
                    var iconSegmentedControl = levelListViewController.GetField<IconSegmentedControl, SelectLevelCategoryViewController>("_levelFilterCategoryIconSegmentedControl");
                    ((RectTransform)iconSegmentedControl.transform).anchoredPosition = new Vector2(0, 4.5f);

                    requestButton = levelListViewController.CreateUIButton("ReqButton", "PracticeButton", new Vector2(-8.5f, -4.5f), new Vector2(23.5f, 105f), () =>
                    {
                        FlowCoordinator flowCoordinator;

                        if (gameMode == GameMode.Solo)
                        {
                            flowCoordinator = Resources.FindObjectsOfTypeAll<SoloFreePlayFlowCoordinator>().First();
                        }
                        else
                        {
                            flowCoordinator = Resources.FindObjectsOfTypeAll<MultiplayerLevelSelectionFlowCoordinator>().First();
                        }

                        flowCoordinator.InvokeMethod<object, FlowCoordinator>("PresentFlowCoordinator", requestFlowCoordinator, null, ViewController.AnimationDirection.Horizontal, false, false);
                    }, "Requests");

                    requestButton.interactable = true;
                    requestButton.SetButtonTextSize(5f);
                    requestButton.ToggleWordWrapping(false);

                    if (RequestQueue.Songs.Count == 0)
                    {
                        requestButton.SetButtonUnderlineColor(Color.red);
                    }
                    else
                    {
                        requestButton.SetButtonUnderlineColor(Color.green);
                    }

                    if (PluginConfig.Instance.PrivateKey.Equals("ENTER KEY"))
                    {
                        requestButton.enabled = false;
                        UIHelper.AddHintText(requestButton.transform as RectTransform, "Please set up your config first!");
                    }
                    else
                    {
                        UIHelper.AddHintText(requestButton.transform as RectTransform, "View song requests");

                    }

                    
                }
            }
            catch
            {
                Log.Debug("Unable to create request button");
            }

            var campaignButton = Resources.FindObjectsOfTypeAll<Button>().First(x => x.name == "CampaignButton");
            var onlinePlayButton = Resources.FindObjectsOfTypeAll<Button>().First(x => x.name == "OnlineButton");
            var soloFreePlayButton = Resources.FindObjectsOfTypeAll<Button>().First(x => x.name == "SoloButton");
            var partyFreePlayButton = Resources.FindObjectsOfTypeAll<Button>().First(x => x.name == "PartyButton");

            campaignButton.onClick.AddListener(() => { gameMode = GameMode.Solo; });
            onlinePlayButton.onClick.AddListener(() => { gameMode = GameMode.Online; });
            soloFreePlayButton.onClick.AddListener(() => { gameMode = GameMode.Solo; });
            partyFreePlayButton.onClick.AddListener(() => { gameMode = GameMode.Solo; });
        }

        public static async Task<int> fetchSongs()
        {
            if (PluginConfig.Instance.PrivateKey.Equals("ENTER KEY"))
            {
                return -1;
            }
            
            var res = await WebClient.GetAsync("https://bsaber.vanishedmc.com/app/requests/fetch?private_key=" + PluginConfig.Instance.PrivateKey, System.Threading.CancellationToken.None);
            JSONNode result;

            if (res.IsSuccessStatusCode)
            {
                result = res.ConvertToJsonNode();
            }
            else
            {
                Log.Info($"Error {res.ReasonPhrase} occured when trying to fetch requests!");
                return -1;
            }

            JSONArray arr = result["songs"].AsArray;

            for (int i = 0; i < arr.Count; i++)
            {
                JSONNode data = JSONNode.Parse(arr[i].ToString());

                List<JSONObject> songs = SongRequests.GetSongListFromResults(data, data["key"]);
                foreach (JSONObject song in songs)
                {
                    RequestQueue.Songs.Add(new SongRequest(song, DateTime.UtcNow, RequestStatus.SongSearch, "search result"));
                }
            }

            SongRequests.MapDatabase.SaveDatabase();
            RequestQueue.Write();
            RequestHistory.Write();

            Log.Info($"Fetched {arr.Count} songs");

            return -1;
        }

        // TODO Move to utils class
        public static void SongBrowserCancelFilter()
        {
            if (SongBrowserPluginPresent)
            {
                SongBrowser.UI.SongBrowserUI songBrowserUI = SongBrowser.SongBrowserApplication.Instance.GetField<SongBrowser.UI.SongBrowserUI, SongBrowser.SongBrowserApplication>("_songBrowserUI");
                if (songBrowserUI)
                {
                    if (songBrowserUI.Model.Settings.filterMode != SongBrowser.DataAccess.SongFilterMode.None && songBrowserUI.Model.Settings.sortMode != SongBrowser.DataAccess.SongSortMode.Original)
                    {
                        songBrowserUI.CancelFilter();
                    }
                }
                else
                {
                    Plugin.Log.Info("There was a problem obtaining SongBrowserUI object, unable to reset filters");
                }
            }
        }

        [OnExit]
        public void OnApplicationQuit()
        {
            Log.Debug("OnApplicationQuit");
        }

        // TODO  Move to Enum class
        public enum RequestStatus
        {
            Invalid,
            Queued,
            Blacklisted,
            Skipped,
            Played,
            Wrongsong,
            SongSearch,
        }

        internal enum GameMode
        {
            Solo,
            Online
        }

        // TODO Move to own class
        public class StringNormalization
        {
            public static HashSet<string> BeatsaverBadWords = new HashSet<string>();

            public void ReplaceSymbols(StringBuilder text, char[] mask)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c < 128) text[i] = mask[c];
                }
            }

            public string RemoveSymbols(ref string text, char[] mask)
            {
                var o = new StringBuilder(text.Length);

                foreach (var c in text)
                {
                    if (c > 127 || mask[c] != ' ') o.Append(c);
                }
                return o.ToString();
            }

            public string RemoveDirectorySymbols(ref string text)
            {
                var mask = _SymbolsValidDirectory;
                var o = new StringBuilder(text.Length);

                foreach (var c in text)
                {
                    if (c > 127 || mask[c] != '\0') o.Append(c);
                }
                return o.ToString();
            }

            // This function takes a user search string, and fixes it for beatsaber.
            public string NormalizeBeatSaverString(string text)
            {
                var words = Split(text);
                StringBuilder result = new StringBuilder();
                foreach (var word in words)
                {
                    if (word.Length < 3) continue;
                    if (BeatsaverBadWords.Contains(word.ToLower())) continue;
                    result.Append(word);
                    result.Append(' ');
                }

                //RequestBot.Instance.QueueChatMessage($"Search string: {result.ToString()}");


                if (result.Length == 0) return "qwesartysasasdsdaa";
                return result.ToString().Trim();
            }

            public string[] Split(string text)
            {
                var sb = new StringBuilder(text);
                ReplaceSymbols(sb, _SymbolsMap);
                string[] result = sb.ToString().ToLower().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                return result;
            }

            public char[] _SymbolsMap = new char[128];
            public char[] _SymbolsNoDash = new char[128];
            public char[] _SymbolsValidDirectory = new char[128];

            public StringNormalization()
            {
                for (char i = (char)0; i < 128; i++)
                {
                    _SymbolsMap[i] = i;
                    _SymbolsNoDash[i] = i;
                    _SymbolsValidDirectory[i] = i;
                }

                foreach (var c in new char[] { '@', '*', '+', ':', '-', '<', '~', '>', '(', ')', '[', ']', '/', '\\', '.', ',' }) if (c < 128) _SymbolsMap[c] = ' ';
                foreach (var c in new char[] { '@', '*', '+', ':', '<', '~', '>', '(', ')', '[', ']', '/', '\\', '.', ',' }) if (c < 128) _SymbolsNoDash[c] = ' ';
                foreach (var c in Path.GetInvalidPathChars()) if (c < 128) _SymbolsValidDirectory[c] = '\0';
                _SymbolsValidDirectory[':'] = '\0';
                _SymbolsValidDirectory['\\'] = '\0';
                _SymbolsValidDirectory['/'] = '\0';
                _SymbolsValidDirectory['+'] = '\0';
                _SymbolsValidDirectory['*'] = '\0';
                _SymbolsValidDirectory['?'] = '\0';
                _SymbolsValidDirectory[';'] = '\0';
                _SymbolsValidDirectory['$'] = '\0';
                _SymbolsValidDirectory['.'] = '\0';

                // Incomplete list of words that BeatSaver.com filters out for no good reason. No longer applies!
                foreach (var word in new string[] { "pp" })
                {
                    BeatsaverBadWords.Add(word.ToLower());
                }
            }
        }
    }
}
