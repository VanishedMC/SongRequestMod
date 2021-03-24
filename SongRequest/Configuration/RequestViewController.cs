using HMUI;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using IPA.Utilities;
using BeatSaberMarkupLanguage;
using System.Collections.Concurrent;
using System.Text;
using static SongRequest.Plugin;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using SongCore;
using System.Collections;

namespace SongRequest {
    class RequestViewController : ViewController, TableView.IDataSource {
        public static RequestViewController Instance;

        // Elements
        private Button playButton;
        private Button fetchButton;
        private Button pageUpButton;
        private Button historyButton;
        private Button pageDownButton;

        private TableView songListView;
        private LevelListTableCell requestList;

        private SongPreviewPlayer songPreviewPlayer;
        private static Dictionary<string, Texture2D> _cachedTextures = new Dictionary<string, Texture2D>();

        private int _requestRow = 0;
        private int _historyRow = 0;
        private int _lastSelection = -1;

        private bool isShowingHistory = false;

        private int _selectedRow {
            get { return isShowingHistory ? _historyRow : _requestRow; }
            set {
                if (isShowingHistory)
                    _historyRow = value;
                else
                    _requestRow = value;
            }
        }

        public void Awake() {
            Instance = this;
        }

        static public SongRequest currentsong = null;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
            if (firstActivation) {
                if (!SongCore.Loader.AreSongsLoaded) {
                    SongCore.Loader.SongsLoadedEvent += SongLoader_SongsLoadedEvent;
                }

                // get table cell instance
                requestList = Resources.FindObjectsOfTypeAll<LevelListTableCell>().First((LevelListTableCell x) => x.name == "LevelListTableCell");


                songPreviewPlayer = Resources.FindObjectsOfTypeAll<SongPreviewPlayer>().FirstOrDefault();

                RectTransform container = new GameObject("RequestContainer", typeof(RectTransform)).transform as RectTransform;
                container.SetParent(rectTransform, false);

                #region TableView Setup and Initialization
                var go = new GameObject("SongRequestTableView", typeof(RectTransform));
                go.SetActive(false);

                go.AddComponent<ScrollRect>();
                go.AddComponent<Touchable>();
                go.AddComponent<EventSystemListener>();

                ScrollView scrollView = go.AddComponent<ScrollView>();

                songListView = go.AddComponent<TableView>();
                go.AddComponent<RectMask2D>();
                songListView.transform.SetParent(container, false);

                songListView.SetField("_preallocatedCells", new TableView.CellsGroup[0]);
                songListView.SetField("_isInitialized", false);
                songListView.SetField("_scrollView", scrollView);

                var viewport = new GameObject("Viewport").AddComponent<RectTransform>();
                viewport.SetParent(go.GetComponent<RectTransform>(), false);
                go.GetComponent<ScrollRect>().viewport = viewport;
                (viewport.transform as RectTransform).sizeDelta = new Vector2(70f, 70f);

                RectTransform content = new GameObject("Content").AddComponent<RectTransform>();
                content.SetParent(viewport, false);

                scrollView.SetField("_contentRectTransform", content);
                scrollView.SetField("_viewport", viewport);

                songListView.SetDataSource(this, false);

                songListView.LazyInit();

                go.SetActive(true);

                (songListView.transform as RectTransform).sizeDelta = new Vector2(70f, 70f);
                (songListView.transform as RectTransform).anchoredPosition = new Vector2(3f, 0f);

                songListView.didSelectCellWithIdxEvent += DidSelectRow;

                pageUpButton = UIHelper.CreateUIButton("SRPageUpButton",
                    container,
                    "PracticeButton",
                    new Vector2(0f, 38.5f),
                    new Vector2(15f, 7f),
                    () => { scrollView.PageUpButtonPressed(); },
                    "˄");
                Destroy(pageUpButton.GetComponentsInChildren<ImageView>().FirstOrDefault(x => x.name == "Underline"));

                pageDownButton = UIHelper.CreateUIButton("SRPageDownButton",
                    container,
                    "PracticeButton",
                    new Vector2(0f, -38.5f),
                    new Vector2(15f, 7f),
                    () => { scrollView.PageDownButtonPressed(); },
                    "˅");
                Destroy(pageDownButton.GetComponentsInChildren<ImageView>().FirstOrDefault(x => x.name == "Underline"));
                #endregion

                #region History button
                // History button
                historyButton = UIHelper.CreateUIButton("SRMHistory", container, "PracticeButton", new Vector2(53f, 30f),
                    new Vector2(25f, 15f),
                    () => {
                        isShowingHistory = !isShowingHistory;
                        Plugin.requestFlowCoordinator.SetTitle(isShowingHistory ? "Song Request History" : "Song Requests");

                        if (NumberOfCells() > 0) {
                            songListView.ScrollToCellWithIdx(0, TableView.ScrollPositionType.Beginning, false);
                            songListView.SelectCellWithIdx(0);
                            _selectedRow = 0;
                        } else {
                            _selectedRow = -1;
                        }

                        UpdateRequestUI(true);
                        SetUIInteractivity();
                        _lastSelection = -1;
                    }, "History");

                historyButton.ToggleWordWrapping(false);
                UIHelper.AddHintText(historyButton.transform as RectTransform, "");
                #endregion

                #region Play button
                // Play button
                playButton = UIHelper.CreateUIButton("SRPlay", container, "ActionButton", new Vector2(53f, -10f),
                    new Vector2(25f, 15f),
                    () => {
                        if (NumberOfCells() > 0) {
                            currentsong = SongInfoForRow(_selectedRow);
                            //RequestBot.played.Add(currentsong.song);
                            //RequestBot.WriteJSON(RequestBot.playedfilename, ref RequestBot.played);

                            SetUIInteractivity(false);
                            ProcessSongRequest(_selectedRow, isShowingHistory);
                            _selectedRow = -1;
                        }
                    }, "Play");

                playButton.ToggleWordWrapping(false);
                playButton.interactable = ((isShowingHistory && RequestHistory.Songs.Count > 0) || (!isShowingHistory && RequestQueue.Songs.Count > 0));
                UIHelper.AddHintText(playButton.transform as RectTransform, "Download and scroll to the currently selected request.");
                #endregion

                #region Fetch button
                fetchButton = UIHelper.CreateUIButton("SRFetch", container, "ActionButton", new Vector2(53f, 0f),
                    new Vector2(25f, 15f),
                    async () => {
                        await Plugin.fetchSongs();
                        songListView.ReloadData();

                        if (_selectedRow == -1) return;

                        if (NumberOfCells() > _selectedRow) {
                            songListView.SelectCellWithIdx(_selectedRow, false);
                            songListView.ScrollToCellWithIdx(_selectedRow, TableView.ScrollPositionType.Beginning, true);
                        }

                        SetUIInteractivity(true);
                    }, "Fetch");

                fetchButton.ToggleWordWrapping(false);
                UIHelper.AddHintText(fetchButton.transform as RectTransform, "Fetch latest requests");
                #endregion

            }
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

            //if (addedToHierarchy) {
                _selectedRow = -1;
                songListView.ClearSelection();
            //}

            UpdateRequestUI();
            SetUIInteractivity(true);
        }

        public static void SetRequestStatus(int index, RequestStatus status, bool fromHistory = false) {
            if (!fromHistory)
                RequestQueue.Songs[index].status = status;
            else
                RequestHistory.Songs[index].status = status;
        }

        private async void ProcessSongRequest(int index, bool fromHistory = false) {
            if ((RequestQueue.Songs.Count > 0 && !fromHistory) || (RequestHistory.Songs.Count > 0 && fromHistory)) {
                SongRequest request = null;
                if (!fromHistory) {
                    SetRequestStatus(index, RequestStatus.Played);
                    request = DequeueRequest(index);
                } else {
                    request = RequestHistory.Songs.ElementAt(index);
                }

                if (request == null) {
                    Plugin.Log.Info("Can't process a null request! Aborting!");
                    return;
                } else
                    Plugin.Log.Info($"Processing song request {request.song["songName"].Value}");


                string songName = request.song["songName"].Value;
                string songIndex = $"{request.song["id"].Value} ({request.song["songName"].Value} - {request.song["levelAuthor"].Value})";
                songIndex = normalize.RemoveDirectorySymbols(ref songIndex); // Remove invalid characters.

                string currentSongDirectory = Path.Combine(Environment.CurrentDirectory, "Beat Saber_Data\\CustomLevels", songIndex);
                string songHash = request.song["hash"].Value.ToUpper();

                var rat = SongCore.Collections.levelIDsForHash(songHash);
                bool mapexists = (rat.Count > 0) && (rat[0] != "");


                if (!SongCore.Loader.CustomLevels.ContainsKey(currentSongDirectory) && !mapexists) {

                    EmptyDirectory(".requestcache", false);

                    if (Directory.Exists(currentSongDirectory)) {
                        EmptyDirectory(currentSongDirectory, true);
                        Plugin.Log.Info($"Deleting {currentSongDirectory}");
                    }

                    string localPath = Path.Combine(Environment.CurrentDirectory, ".requestcache", $"{request.song["id"].Value}.zip");

                    byte[] songZip = null;

                    if (songZip == null) {

                        songZip = await Plugin.WebClient.DownloadSong($"https://beatsaver.com{request.song["downloadURL"].Value}", System.Threading.CancellationToken.None);
                    }

                    Stream zipStream = new MemoryStream(songZip);
                    try {
                        // open zip archive from memory stream
                        ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                        archive.ExtractToDirectory(currentSongDirectory);
                        archive.Dispose();
                    } catch (Exception e) {
                        Plugin.Log.Info($"Unable to extract ZIP! Exception: {e}");
                        return;
                    }
                    zipStream.Close();

                    here:

                    await Task.Run(async () => {
                        while (!SongCore.Loader.AreSongsLoaded && SongCore.Loader.AreSongsLoading) await Task.Delay(25);
                    });

                    Loader.Instance.RefreshSongs();

                    await Task.Run(async () => {
                        while (!SongCore.Loader.AreSongsLoaded && SongCore.Loader.AreSongsLoading) await Task.Delay(25);
                    });

                    EmptyDirectory(".requestcache", true);
                    //levels = SongLoader.CustomLevels.Where(l => l.levelID.StartsWith(songHash)).ToArray();
                } else {
                    //Instance.QueueChatMessage($"Directory exists: {currentSongDirectory}");

                    Plugin.Log.Info($"Song {songName} already exists!");
                }

                // Dismiss the song request viewcontroller now
                //_songRequestMenu.Dismiss();
                requestFlowCoordinator.Dismiss();

                if (true) {
                    //Plugin.Log.Info($"Scrolling to level {levels[0].levelID}");

                    Plugin.Log.Info($"Scrolling to level {request.song["name"].Value}");

                    bool success = false;

                    Dispatcher.RunCoroutine(ScrollToLevel(request.song["hash"].Value.ToUpper(), (s) => success = s, false));

                    // Redownload the song if we failed to scroll to it
                } else {
                    Plugin.Log.Info("Failed to find new level!");
                }
            }
        }

        private static IEnumerator SelectCustomSongPack() {
            // get the select Level category view controller
            var selectLevelCategoryViewController = Resources.FindObjectsOfTypeAll<SelectLevelCategoryViewController>().First();

            // check if the selected level category is the custom category
            if (selectLevelCategoryViewController.selectedLevelCategory != SelectLevelCategoryViewController.LevelCategory.CustomSongs) {
                // get the icon segmented controller
                var iconSegmentedControl = selectLevelCategoryViewController.GetField<IconSegmentedControl, SelectLevelCategoryViewController>("_levelFilterCategoryIconSegmentedControl");

                // get the current level categories listed
                var levelCategoryInfos = selectLevelCategoryViewController.GetField<SelectLevelCategoryViewController.LevelCategoryInfo[], SelectLevelCategoryViewController>("_levelCategoryInfos").ToList();

                // get the index of the custom category
                var idx = levelCategoryInfos.FindIndex(lci => lci.levelCategory == SelectLevelCategoryViewController.LevelCategory.CustomSongs);

                // select the custom category
                iconSegmentedControl.SelectCellWithNumber(idx);
            }

            // get the level filtering nev controller
            var levelFilteringNavigationController = Resources.FindObjectsOfTypeAll<LevelFilteringNavigationController>().First();

            // update custom songs
            levelFilteringNavigationController.UpdateCustomSongs();

            // arbitrary wait for catch-up
            yield return new WaitForSeconds(0.1f);
        }

        public static IEnumerator ScrollToLevel(string levelID, Action<bool> callback, bool animated, bool isRetry = false) {
            LevelCollectionViewController _levelCollectionViewController = Resources.FindObjectsOfTypeAll<LevelCollectionViewController>().FirstOrDefault();
            if (_levelCollectionViewController) {
                Plugin.Log.Info($"Scrolling to {levelID}! Retry={isRetry}");

                // handle if song browser is present
                if (Plugin.SongBrowserPluginPresent) {
                    Plugin.SongBrowserCancelFilter();
                }

                // Make sure our custom songpack is selected
                yield return SelectCustomSongPack();

                yield return null;

                int songIndex = 0;

                // get the table view
                var levelsTableView = _levelCollectionViewController.GetField<LevelCollectionTableView, LevelCollectionViewController>("_levelCollectionTableView");

                //RequestBot.Instance.QueueChatMessage($"selecting song: {levelID} pack: {packIndex}");
                yield return null;

                // get the table view
                var tableView = levelsTableView.GetField<TableView, LevelCollectionTableView>("_tableView");

                // get list of beatmaps, this is pre-sorted, etc
                var beatmaps = levelsTableView.GetField<IPreviewBeatmapLevel[], LevelCollectionTableView>("_previewBeatmapLevels").ToList();

                // get the row number for the song we want
                songIndex = beatmaps.FindIndex(x => (x.levelID.Split('_')[2] == levelID));

                // bail if song is not found, shouldn't happen
                if (songIndex >= 0) {
                    // if header is being shown, increment row
                    if (levelsTableView.GetField<bool, LevelCollectionTableView>("_showLevelPackHeader")) {
                        songIndex++;
                    }

                    Plugin.Log.Info($"Selecting row {songIndex}");

                    // scroll to song
                    tableView.ScrollToCellWithIdx(songIndex, TableView.ScrollPositionType.Beginning, animated);

                    // select song, and fire the event
                    tableView.SelectCellWithIdx(songIndex, true);

                    Plugin.Log.Info("Selected song with index " + songIndex);
                    callback?.Invoke(true);
                    yield break;
                }
            } else {
                Plugin.Log.Info("Unable to scroll to level!");
            }

            if (!isRetry) {
                yield return ScrollToLevel(levelID, callback, animated, true);
                yield break;
            }

            Plugin.Log.Info($"Failed to scroll to {levelID}!");
            callback?.Invoke(false);
        }

        public SongRequest DequeueRequest(int index, bool updateUI = true) {
            SongRequest request = RequestQueue.Songs.ElementAt(index);

            if (request != null)
                DequeueRequest(request, updateUI);

            return request;
        }

        public void DequeueRequest(SongRequest request, bool updateUI = true) {
            if (request.status != RequestStatus.Wrongsong && request.status != RequestStatus.SongSearch) RequestHistory.Songs.Insert(0, request); // Wrong song requests are not logged into history, is it possible that other status states shouldn't be moved either?

            if (RequestHistory.Songs.Count > 10) {
                int diff = RequestHistory.Songs.Count - 10;
                RequestHistory.Songs.RemoveRange(RequestHistory.Songs.Count - diff - 1, diff);
            }
            RequestQueue.Songs.Remove(request);
            RequestHistory.Write();
            RequestQueue.Write();

            if (updateUI == false) return;

            UpdateRequestUI();
        }

        public static void EmptyDirectory(string directory, bool delete = true) {
            if (Directory.Exists(directory)) {
                var directoryInfo = new DirectoryInfo(directory);
                foreach (System.IO.FileInfo file in directoryInfo.GetFiles()) file.Delete();
                foreach (System.IO.DirectoryInfo subDirectory in directoryInfo.GetDirectories()) subDirectory.Delete(true);

                if (delete) Directory.Delete(directory);
            }
        }

        private void DidSelectRow(TableView table, int row) {
            _selectedRow = row;
            if (row != _lastSelection) {
                _lastSelection = row;
            }

            // if not in history, disable play button if request is a challenge
            if (!isShowingHistory) {
                var request = SongInfoForRow(row);
                var isChallenge = request.requestInfo.IndexOf("!challenge", StringComparison.OrdinalIgnoreCase) >= 0;
                playButton.interactable = !isChallenge;
            }

            UpdateRequestUI();
            SetUIInteractivity();
        }

        public void UpdateRequestUI(bool selectRowCallback = false) {
            playButton.interactable = ((isShowingHistory && RequestHistory.Songs.Count > 0) || (!isShowingHistory && RequestQueue.Songs.Count > 0));

            playButton.SetButtonText(isShowingHistory ? "Replay" : "Play");

            songListView.ReloadData();

            if (_selectedRow == -1) return;

            if (NumberOfCells() > _selectedRow) {
                songListView.SelectCellWithIdx(_selectedRow, selectRowCallback);
                songListView.ScrollToCellWithIdx(_selectedRow, TableView.ScrollPositionType.Beginning, true);
            }
        }
        private List<SongRequest> Songs => isShowingHistory ? RequestHistory.Songs : RequestQueue.Songs;

        public void SetUIInteractivity(bool interactive = true) {
            var toggled = interactive;

            if (_selectedRow >= (isShowingHistory ? RequestHistory.Songs : RequestQueue.Songs).Count()) _selectedRow = -1;

            if (NumberOfCells() == 0 || _selectedRow == -1 || _selectedRow >= Songs.Count()) {
                Plugin.Log.Info("Nothing selected, or empty list, buttons should be off");
                toggled = false;
            }

            var playButtonEnabled = toggled;
            if (toggled && !isShowingHistory) {
                var request = SongInfoForRow(_selectedRow);
                var isChallenge = request.requestInfo.IndexOf("!challenge", StringComparison.OrdinalIgnoreCase) >= 0;
                playButtonEnabled = isChallenge ? false : toggled;
            }

            playButton.interactable = playButtonEnabled;

            // history button can be enabled even if others are disabled
            historyButton.interactable = true;
        }


        private SongRequest SongInfoForRow(int row) {
            return isShowingHistory ? RequestHistory.Songs.ElementAt(row) : RequestQueue.Songs.ElementAt(row);
        }

        private void SongLoader_SongsLoadedEvent(SongCore.Loader arg1, ConcurrentDictionary<string, CustomPreviewBeatmapLevel> arg2) {
            songListView?.ReloadData();
        }

        public TableCell CellForIdx(TableView tableView, int row) {
            LevelListTableCell _tableCell = Instantiate(requestList);
            _tableCell.reuseIdentifier = "RequestBotSongCell";
            _tableCell.SetField("_notOwned", false);

            SongRequest request = SongInfoForRow(row);
            SetDataFromLevelAsync(request, _tableCell, row);

            return _tableCell;
        }

        public float CellSize() {
            return 10f;
        }

        public int NumberOfCells() {
            return isShowingHistory ? RequestHistory.Songs.Count() : RequestQueue.Songs.Count();
        }

        private CustomPreviewBeatmapLevel CustomLevelForRow(int row) {
            // get level id from hash
            var levelIds = SongCore.Collections.levelIDsForHash(SongInfoForRow(row).song["hash"]);
            if (levelIds.Count == 0) return null;

            // lookup song from level id
            return SongCore.Loader.CustomLevels.FirstOrDefault(s => string.Equals(s.Value.levelID, levelIds.First(), StringComparison.OrdinalIgnoreCase)).Value ?? null;
        }

        private async void SetDataFromLevelAsync(SongRequest request, LevelListTableCell _tableCell, int row) {
            var favouritesBadge = _tableCell.GetField<Image, LevelListTableCell>("_favoritesBadgeImage");
            favouritesBadge.enabled = false;

            var highlight = (request.requestInfo.Length > 0) && (request.requestInfo[0] == '!');

            var msg = highlight ? "MSG" : "";

            var hasMessage = (request.requestInfo.Length > 0) && (request.requestInfo[0] == '!');
            var isChallenge = request.requestInfo.IndexOf("!challenge", StringComparison.OrdinalIgnoreCase) >= 0;

            var pp = "";
            var ppvalue = request.song["pp"].AsInt;
            if (ppvalue > 0) pp = $" {ppvalue} PP";

            var dt = new DynamicText().AddSong(request.song);
            dt.Add("Status", request.status.ToString());
            dt.Add("Info", (request.requestInfo != "") ? " / " + request.requestInfo : "");
            dt.Add("RequestTime", request.requestTime.ToLocalTime().ToString("hh:mm"));

            var songDurationText = _tableCell.GetField<TextMeshProUGUI, LevelListTableCell>("_songDurationText");
            songDurationText.text = request.song["songlength"].Value;

            var songBpm = _tableCell.GetField<TextMeshProUGUI, LevelListTableCell>("_songBpmText");
            (songBpm.transform as RectTransform).anchoredPosition = new Vector2(-2.5f, -1.8f);
            (songBpm.transform as RectTransform).sizeDelta += new Vector2(15f, 0f);

            var k = new List<string>();
            if (hasMessage) k.Add("MSG");
            if (isChallenge) k.Add("VS");
            k.Add(request.song["id"]);
            songBpm.text = string.Join(" - ", k);

            var songBmpIcon = _tableCell.GetComponentsInChildren<Image>().LastOrDefault(c => string.Equals(c.name, "BpmIcon", StringComparison.OrdinalIgnoreCase));
            if (songBmpIcon != null) {
                Destroy(songBmpIcon);
            }

            var songName = _tableCell.GetField<TextMeshProUGUI, LevelListTableCell>("_songNameText");
            songName.richText = true;
            //songName.text = $"{request.song["songName"].Value} <size=50%>{RequestBot.GetRating(ref request.song)} <color=#3fff3f>{pp}</color></size>";
            songName.text = $"{request.song["songName"].Value} <color=#3fff3f>{pp}</color>";

            var author = _tableCell.GetField<TextMeshProUGUI, LevelListTableCell>("_songAuthorText");
            author.richText = true;
            author.text = dt.Parse("%levelAuthor%");

            var image = _tableCell.GetField<Image, LevelListTableCell>("_coverImage");
            var imageSet = false;

            if (SongCore.Loader.AreSongsLoaded) {
                var level = CustomLevelForRow(row);
                if (level != null) {
                    // set image from song's cover image
                    var sprite = await level.GetCoverImageAsync(System.Threading.CancellationToken.None);
                    image.sprite = sprite;
                    imageSet = true;
                }
            }

            if (!imageSet) {
                image.sprite = Base64Sprites.Base64ToSprite(request.song["coverIMG"].Value);
            }

            //UIHelper.AddHintText(_tableCell.transform as RectTransform, dt.Parse(RequestBot.SongHintText));
        }
    }


    public class DynamicText {
        public Dictionary<string, string> dynamicvariables = new Dictionary<string, string>();  // A list of the variables available to us, we're using a list of pairs because the match we use uses BeginsWith,since the name of the string is unknown. The list is very short, so no biggie

        public bool AllowLinks = true;


        public DynamicText Add(string key, string value) {
            dynamicvariables.Add(key, value); // Make the code slower but more readable :(
            return this;
        }

        public DynamicText() {
            Add("|", ""); // This is the official section separator character, its used in help to separate usage from extended help, and because its easy to detect when parsing, being one character long

            // BUG: Note -- Its my intent to allow sections to be used as a form of conditional. If a result failure occurs within a section, we should be able to rollback the entire section, and continue to the next. Its a better way of handline missing dynamic fields without excessive scripting
            // This isn't implemented yet.

            AddLinks();

            DateTime Now = DateTime.Now; //"MM/dd/yyyy hh:mm:ss.fffffff";         
            Add("SRM", "Song Request Manager");
            Add("Time", Now.ToString("hh:mm"));
            Add("LongTime", Now.ToString("hh:mm:ss"));
            Add("Date", Now.ToString("yyyy/MM/dd"));
            Add("LF", "\n"); // Allow carriage return

        }

        public DynamicText AddLinks() {
            if (AllowLinks) {
                Add("beatsaver", "https://beatsaver.com");
                Add("beatsaber", "https://beatsaber.com");
                Add("scoresaber", "https://scoresaber.com");
            } else {
                Add("beatsaver", "beatsaver site");
                Add("beatsaver", "beatsaber site");
                Add("scoresaber", "scoresaber site");
            }

            return this;
        }

        // Adds a JSON object to the dictionary. You can define a prefix to make the object identifiers unique if needed.
        public DynamicText AddJSON(ref JSONObject json, string prefix = "") {
            foreach (var element in json) Add(prefix + element.Key, element.Value);
            return this;
        }

        public DynamicText AddSong(JSONObject json, string prefix = "") // Alternate call for direct object
        {
            return AddSong(ref json, prefix);
        }

        public DynamicText AddSong(ref JSONObject song, string prefix = "") {
            AddJSON(ref song, prefix); // Add the song JSON

            //SongMap map;
            //if (MapDatabase.MapLibrary.TryGetValue(song["version"].Value, out map) && map.pp>0)
            //{
            //    Add("pp", map.pp.ToString());
            //}
            //else
            //{
            //    Add("pp", "");
            //}


            if (song["pp"].AsFloat > 0) Add("PP", song["pp"].AsInt.ToString() + " PP"); else Add("PP", "");

            Add("StarRating", GetStarRating(ref song)); // Add additional dynamic properties
            Add("Rating", GetRating(ref song));
            Add("BeatsaverLink", $"https://beatsaver.com/beatmap/{song["id"].Value}");
            Add("BeatsaberLink", $"https://bsaber.com/songs/{song["id"].Value}");
            return this;
        }

        public string Parse(string text, bool parselong = false) // We implement a path for ref or nonref
        {
            return Parse(ref text, parselong);
        }

        public string Parse(StringBuilder text, bool parselong = false) // We implement a path for ref or nonref
        {
            return Parse(text.ToString(), parselong);
        }

        // Refactor, supports %variable%, and no longer uses split, should be closer to c++ speed.
        public string Parse(ref string text, bool parselong = false) {
            StringBuilder output = new StringBuilder(text.Length); // We assume a starting capacity at LEAST = to length of original string;

            for (int p = 0; p < text.Length; p++) // P is pointer, that's good enough for me
            {
                char c = text[p];
                if (c == '%') {
                    int keywordstart = p + 1;
                    int keywordlength = 0;

                    int end = Math.Min(p + 32, text.Length); // Limit the scan for the 2nd % to 32 characters, or the end of the string
                    for (int k = keywordstart; k < end; k++) // Pretty sure there's a function for this, I'll look it up later
                    {
                        if (text[k] == '%') {
                            keywordlength = k - keywordstart;
                            break;
                        }
                    }

                    string substitutetext;

                    if (keywordlength > 0 && keywordlength != 0 && dynamicvariables.TryGetValue(text.Substring(keywordstart, keywordlength), out substitutetext)) {

                        if (keywordlength == 1 && !parselong) return output.ToString(); // Return at first sepearator on first 1 character code. 

                        output.Append(substitutetext);

                        p += keywordlength + 1; // Reset regular text
                        continue;
                    }
                }
                output.Append(c);
            }

            return output.ToString();
        }

        public static string GetRating(ref JSONObject song, bool mode = true) {
            if (!mode) return "";

            string rating = song["rating"].AsInt.ToString();
            if (rating == "0") return "";
            return rating + '%';
        }

        public static string GetStarRating(ref JSONObject song, bool mode = true) {
            if (!mode) return "";

            string stars = "******";
            float rating = song["rating"].AsFloat;
            if (rating < 0 || rating > 100) rating = 0;
            string starrating = stars.Substring(0, (int)(rating / 17)); // 17 is used to produce a 5 star rating from 80ish to 100.
            return starrating;
        }
    }
}
