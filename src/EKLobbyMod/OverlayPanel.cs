using EKLobbyShared;
using UnityEngine;
using UnityEngine.UI;

namespace EKLobbyMod;

public class OverlayPanel : MonoBehaviour
{
    private static OverlayPanel _instance;

    private LobbyManager _manager = null!;
    private bool _expanded = false;
    private float _s = 1f;

    private GameObject _expandedPanel = null!;
    private Text _codeText = null!;
    private Transform _friendListContainer = null!;
    private Button _rejoinButton = null!;
    private Image _rejoinBtnImage = null!;
    private Text _rejoinPromptLabel = null!;

    private GameObject _minTab = null!;
    private RectTransform _friendListContentRt = null!;
    private Text _codeLabelText = null!;
    private InputField _codeInputField = null!;
    private GameObject _editBtnGo = null!;
    private GameObject _saveBtnGo = null!;
    private bool _codeEditing = false;
    private Coroutine? _countdownCoroutine;
    private Image _partyDot = null!;
    private Text _partyText = null!;

    private GameObject _countdownOverlay = null!;
    private Text _countdownDigit = null!;
    private Text _countdownHint = null!;

    private GameObject _driftBand = null!;
    private Text _driftBandText = null!;
    private bool _showingRestartMsg = false;
    private float _restartMsgTimer = 0f;

    // Exploding Kittens brand palette
    private static readonly Color EkBlack    = new Color(0.059f, 0.059f, 0.059f, 0.97f);
    private static readonly Color EkRed      = new Color(0.506f, 0.141f, 0.176f, 1f);
    private static readonly Color EkOffWhite = new Color(0.988f, 0.972f, 0.933f, 1f);
    private static readonly Color EkDark     = new Color(0.14f,  0.14f,  0.14f,  1f);
    private static readonly Color EkRedDark  = new Color(0.32f,  0.08f,  0.10f,  1f);
    private static readonly Color EkGreen    = new Color(0.12f,  0.48f,  0.12f,  1f);
    private static readonly Color EkGray     = new Color(0.45f,  0.45f,  0.45f,  1f);

    public static OverlayPanel Inject(LobbyManager manager)
    {
        var canvas = FindTopCanvas();
        if (canvas == null)
        {
            Plugin.Log.LogWarning("No Canvas found — overlay not injected");
            return null;
        }

        if (_instance != null)
        {
            _instance._manager = manager;
            return _instance;
        }

        var go = new GameObject("EKLobbyOverlay");
        go.transform.SetParent(canvas.transform, false);
        var rootRt = go.AddComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        var panel = go.AddComponent<OverlayPanel>();
        _instance = panel;
        panel._manager = manager;
        panel.Build();
        manager.RejoinAvailable    += (System.Action)panel.ShowRejoinPrompt;
        manager.RejoinConfirmed    += (System.Action)panel.HideRejoinPrompt;
        manager.PlayerListChanged  += (System.Action)panel.OnPlayerListChanged;
        manager.AutoQueueCancelled += (System.Action)panel.OnAutoQueueCancelled;
        manager.VersionMapChanged  += (System.Action)panel.OnVersionMapChanged;
        return panel;
    }

    private void Build()
    {
        // Area-based scale: matches height/864 exactly on 16:9, scales up for ultrawide
        _s = Mathf.Clamp(Mathf.Sqrt(Screen.width * (float)Screen.height) / 1152f, 0.6f, 2.5f);
        BuildMinTab();
        BuildExpandedPanel();
        SetExpanded(false);
        RefreshPartyIndicator();
    }

    private void BuildMinTab()
    {
        float s = _s;
        _minTab = CreatePanel(transform, new Vector2(220 * s, 40 * s),
            Vector2.zero, Vector2.zero, new Vector2(110 * s, 20 * s));
        _minTab.AddComponent<Image>().color = EkBlack;

        // Left EK-red accent strip
        var accent = new GameObject("Accent");
        accent.transform.SetParent(_minTab.transform, false);
        var aRt = accent.AddComponent<RectTransform>();
        aRt.anchorMin = Vector2.zero;
        aRt.anchorMax = new Vector2(0, 1);
        aRt.offsetMin = Vector2.zero;
        aRt.offsetMax = new Vector2(4 * s, 0);
        accent.AddComponent<Image>().color = EkRed;

        var tabButton = _minTab.AddComponent<Button>();
        tabButton.onClick.AddListener((UnityEngine.Events.UnityAction)ToggleExpanded);

        // Party dot (solid colored square — visually dot-like at 10×10)
        var dotGo = new GameObject("PartyDot");
        dotGo.transform.SetParent(_minTab.transform, false);
        var dotRt = dotGo.AddComponent<RectTransform>();
        PositionRect(dotRt, new Vector2(8 * s, 15 * s), new Vector2(10 * s, 10 * s));
        _partyDot = dotGo.AddComponent<Image>();
        _partyDot.color = EkGray;

        // "X in party" label
        _partyText = CreateText(_minTab.transform, "—", (int)(11 * s));
        PositionRect(_partyText.rectTransform, new Vector2(26 * s, 12 * s), new Vector2(90 * s, 16 * s));
        _partyText.alignment = TextAnchor.MiddleLeft;

        // Lobby code — repositioned to right side of tab
        _codeText = CreateText(_minTab.transform, "", (int)(11 * s));
        PositionRect(_codeText.rectTransform, new Vector2(124 * s, 12 * s), new Vector2(88 * s, 16 * s));
        _codeText.alignment = TextAnchor.MiddleRight;
        RefreshCodeLabel();
    }

    private void BuildExpandedPanel()
    {
        float s = _s;
        _expandedPanel = CreatePanel(transform, new Vector2(300 * s, 400 * s),
            Vector2.zero, Vector2.zero, new Vector2(150 * s, 200 * s));
        _expandedPanel.AddComponent<Image>().color = EkBlack;

        // EK-red header strip
        var headerStrip = new GameObject("HeaderStrip");
        headerStrip.transform.SetParent(_expandedPanel.transform, false);
        var hsRt = headerStrip.AddComponent<RectTransform>();
        PositionRect(hsRt, new Vector2(0, 356 * s), new Vector2(300 * s, 44 * s));
        headerStrip.AddComponent<Image>().color = EkRed;

        var header = CreateText(_expandedPanel.transform, "MY LOBBY", (int)(17 * s));
        header.fontStyle = FontStyle.Bold;
        PositionRect(header.rectTransform, new Vector2(10 * s, 358 * s), new Vector2(240 * s, 40 * s));
        header.alignment = TextAnchor.MiddleLeft;

        var collapseBtn = CreateMinimizeButton(_expandedPanel.transform,
            new Vector2(262 * s, 361 * s), new Vector2(32 * s, 30 * s));
        collapseBtn.onClick.AddListener((UnityEngine.Events.UnityAction)ToggleExpanded);

        // Drift band — amber strip between header and code row, hidden by default
        _driftBand = new GameObject("DriftBand");
        _driftBand.transform.SetParent(_expandedPanel.transform, false);
        var driftRt = _driftBand.AddComponent<RectTransform>();
        PositionRect(driftRt, new Vector2(0, 338 * s), new Vector2(300 * s, 20 * s));
        _driftBand.AddComponent<Image>().color = new Color(1f, 0.55f, 0f, 1f); // amber

        _driftBandText = CreateText(_driftBand.transform, "⚠ Version mismatch", (int)(11 * s));
        _driftBandText.color = new Color(0f, 0f, 0f, 1f); // black text on amber
        var driftTextRt = _driftBandText.rectTransform;
        driftTextRt.anchorMin = Vector2.zero;
        driftTextRt.anchorMax = Vector2.one;
        driftTextRt.offsetMin = new Vector2(6 * s, 0);
        driftTextRt.offsetMax = new Vector2(-(58 * s), 0); // leave room for Update button

        var updateBtn = CreateButton(_driftBand.transform, "Update", (int)(10 * s),
            new Vector2(244 * s, 1 * s), new Vector2(52 * s, 18 * s));
        updateBtn.GetComponent<Image>().color = EkRedDark;
        System.Action onUpdateClick = OnUpdateClicked;
        updateBtn.onClick.AddListener((UnityEngine.Events.UnityAction)onUpdateClick);

        _driftBand.SetActive(false); // hidden until drift detected

        _codeLabelText = CreateText(_expandedPanel.transform,
            $"Code: {_manager.Config.LobbyRoomName}", (int)(13 * s));
        float codeLabelW = _codeLabelText.preferredWidth + 2 * s;
        PositionRect(_codeLabelText.rectTransform, new Vector2(6 * s, 322 * s), new Vector2(codeLabelW, 22 * s));

        // InputField at same position — visible only in edit mode
        var inputGo = new GameObject("CodeInputField");
        inputGo.transform.SetParent(_expandedPanel.transform, false);
        PositionRect(inputGo.AddComponent<RectTransform>(),
            new Vector2(6 * s, 322 * s), new Vector2(codeLabelW, 22 * s));
        inputGo.AddComponent<Image>().color = EkDark;
        var codePh = CreateText(inputGo.transform, "Enter room code...", (int)(10 * s));
        codePh.color = new Color(0.5f, 0.5f, 0.5f);
        codePh.rectTransform.anchorMin = Vector2.zero;
        codePh.rectTransform.anchorMax = Vector2.one;
        codePh.rectTransform.offsetMin = new Vector2(4 * s, 0);
        codePh.rectTransform.offsetMax = Vector2.zero;
        var codeInputText = CreateText(inputGo.transform, "", (int)(12 * s));
        codeInputText.rectTransform.anchorMin = Vector2.zero;
        codeInputText.rectTransform.anchorMax = Vector2.one;
        codeInputText.rectTransform.offsetMin = new Vector2(4 * s, 0);
        codeInputText.rectTransform.offsetMax = Vector2.zero;
        _codeInputField = inputGo.AddComponent<InputField>();
        _codeInputField.textComponent = codeInputText;
        _codeInputField.placeholder = codePh;
        inputGo.SetActive(false);

        var copyBtn = CreateButton(_expandedPanel.transform, "Copy", (int)(12 * s),
            new Vector2(6 * s + codeLabelW + 4 * s, 322 * s), new Vector2(52 * s, 22 * s));
        copyBtn.onClick.AddListener((UnityEngine.Events.UnityAction)CopyCodeToClipboard);

        // Edit (pencil) button — view mode
        float iconX = 6 * s + codeLabelW + 4 * s + 52 * s + 4 * s;
        float iconW = 28 * s;
        float iconH = 22 * s;

        _editBtnGo = new GameObject("Btn_Edit");
        _editBtnGo.transform.SetParent(_expandedPanel.transform, false);
        var editRt = _editBtnGo.AddComponent<RectTransform>();
        editRt.anchorMin = editRt.anchorMax = editRt.pivot = Vector2.zero;
        editRt.anchoredPosition = new Vector2(iconX, 322 * s);
        editRt.sizeDelta = new Vector2(iconW, iconH);
        _editBtnGo.AddComponent<Image>().color = EkDark;
        var editBtn = _editBtnGo.AddComponent<Button>();
        var pencilText = CreateText(_editBtnGo.transform, "✏", (int)(13 * s));
        pencilText.rectTransform.anchorMin = Vector2.zero;
        pencilText.rectTransform.anchorMax = Vector2.one;
        pencilText.rectTransform.offsetMin = pencilText.rectTransform.offsetMax = Vector2.zero;
        pencilText.alignment = TextAnchor.MiddleCenter;
        editBtn.onClick.AddListener((UnityEngine.Events.UnityAction)StartCodeEdit);

        // Save (floppy disk) button — edit mode, hidden initially
        _saveBtnGo = new GameObject("Btn_Save");
        _saveBtnGo.transform.SetParent(_expandedPanel.transform, false);
        var saveRt = _saveBtnGo.AddComponent<RectTransform>();
        saveRt.anchorMin = saveRt.anchorMax = saveRt.pivot = Vector2.zero;
        saveRt.anchoredPosition = new Vector2(iconX, 322 * s);
        saveRt.sizeDelta = new Vector2(iconW, iconH);
        _saveBtnGo.AddComponent<Image>().color = new Color(0.1f, 0.35f, 0.1f, 1f);
        var saveBtn = _saveBtnGo.AddComponent<Button>();
        CreateFloppyIcon(_saveBtnGo.transform);
        saveBtn.onClick.AddListener((UnityEngine.Events.UnityAction)CommitCodeEdit);
        _saveBtnGo.SetActive(false);

        // Scroll viewport
        var viewport = new GameObject("FriendListViewport");
        viewport.transform.SetParent(_expandedPanel.transform, false);
        var vpRt = viewport.AddComponent<RectTransform>();
        PositionRect(vpRt, new Vector2(6 * s, 100 * s), new Vector2(288 * s, 214 * s));
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("FriendListContent");
        content.transform.SetParent(viewport.transform, false);
        _friendListContentRt = content.AddComponent<RectTransform>();
        _friendListContentRt.anchorMin = new Vector2(0, 1);
        _friendListContentRt.anchorMax = new Vector2(1, 1);
        _friendListContentRt.pivot = new Vector2(0, 1);
        _friendListContentRt.offsetMin = Vector2.zero;
        _friendListContentRt.offsetMax = Vector2.zero;
        _friendListContentRt.sizeDelta = Vector2.zero;
        _friendListContainer = content.transform;

        var sr = viewport.AddComponent<ScrollRect>();
        sr.content = _friendListContentRt;
        sr.vertical = true;
        sr.horizontal = false;
        sr.scrollSensitivity = 20 * s;
        sr.movementType = ScrollRect.MovementType.Clamped;

        var addBtn = CreateButton(_expandedPanel.transform, "+ Add", (int)(12 * s),
            new Vector2(6 * s, 74 * s), new Vector2(66 * s, 22 * s));
        addBtn.onClick.AddListener((UnityEngine.Events.UnityAction)OpenFriendPicker);

        _rejoinPromptLabel = CreateText(_expandedPanel.transform,
            "Game over — return to your lobby?", (int)(11 * s));
        PositionRect(_rejoinPromptLabel.rectTransform, new Vector2(6 * s, 48 * s), new Vector2(288 * s, 22 * s));
        _rejoinPromptLabel.color = new Color(1f, 0.85f, 0.3f, 1f);
        _rejoinPromptLabel.gameObject.SetActive(false);

        // Countdown overlay — semi-opaque black panel covering interior below header
        var countdownOverlayGo = new GameObject("CountdownOverlay");
        countdownOverlayGo.transform.SetParent(_expandedPanel.transform, false);
        var countdownOverlayRt = countdownOverlayGo.AddComponent<RectTransform>();
        countdownOverlayRt.anchorMin = Vector2.zero;
        countdownOverlayRt.anchorMax = Vector2.zero;
        countdownOverlayRt.pivot = Vector2.zero;
        countdownOverlayRt.sizeDelta = new Vector2(300 * s, 356 * s);
        countdownOverlayRt.anchoredPosition = new Vector2(0, 0);
        var countdownOverlayImg = countdownOverlayGo.AddComponent<Image>();
        countdownOverlayImg.color = new Color(0f, 0f, 0f, 0.80f);
        _countdownOverlay = countdownOverlayGo;
        _countdownOverlay.SetActive(false);

        // Large countdown digit — centered horizontally, slightly above overlay center
        _countdownDigit = CreateText(countdownOverlayGo.transform, "5", (int)(64 * s));
        _countdownDigit.color = EkRed;
        _countdownDigit.fontStyle = FontStyle.Bold;
        _countdownDigit.alignment = TextAnchor.MiddleCenter;
        var digitRt = _countdownDigit.rectTransform;
        digitRt.anchorMin = new Vector2(0.5f, 0.5f);
        digitRt.anchorMax = new Vector2(0.5f, 0.5f);
        digitRt.pivot = new Vector2(0.5f, 0.5f);
        digitRt.sizeDelta = new Vector2(80 * s, 80 * s);
        digitRt.anchoredPosition = new Vector2(0, 40 * s);

        // Hint text — below the digit
        _countdownHint = CreateText(countdownOverlayGo.transform, "Click Leave to cancel", (int)(11 * s));
        _countdownHint.color = EkOffWhite;
        _countdownHint.alignment = TextAnchor.MiddleCenter;
        var hintRt = _countdownHint.rectTransform;
        hintRt.anchorMin = new Vector2(0.5f, 0.5f);
        hintRt.anchorMax = new Vector2(0.5f, 0.5f);
        hintRt.pivot = new Vector2(0.5f, 0.5f);
        hintRt.sizeDelta = new Vector2(200 * s, 20 * s);
        hintRt.anchoredPosition = new Vector2(0, -30 * s);

        var inviteAllBtn = CreateButton(_expandedPanel.transform, "INVITE ALL", (int)(13 * s),
            new Vector2(6 * s, 6 * s), new Vector2(136 * s, 38 * s));
        inviteAllBtn.GetComponent<Image>().color = EkRed;
        inviteAllBtn.onClick.AddListener((UnityEngine.Events.UnityAction)InviteAll);

        _rejoinButton = CreateButton(_expandedPanel.transform, "REJOIN", (int)(13 * s),
            new Vector2(152 * s, 6 * s), new Vector2(136 * s, 38 * s));
        _rejoinBtnImage = _rejoinButton.GetComponent<Image>();
        _rejoinButton.onClick.AddListener((UnityEngine.Events.UnityAction)DoRejoin);
    }

    public void ShowRejoinPrompt()
    {
        SetExpanded(true);
        _rejoinPromptLabel.gameObject.SetActive(true);
        _rejoinBtnImage.color = new Color(0.12f, 0.48f, 0.12f, 1f);

        if (_manager.AutoQueueActive)
        {
            if (_countdownCoroutine != null) StopCoroutine(_countdownCoroutine);
            _countdownOverlay.SetActive(true);
            _countdownDigit.text = "5";
            _countdownCoroutine = StartCoroutine("RunCountdown");
        }
    }

    public void HideRejoinPrompt()
    {
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
        _countdownOverlay.SetActive(false);
        _rejoinPromptLabel.gameObject.SetActive(false);
        _rejoinBtnImage.color = EkDark;
    }

    public void OnAutoQueueCancelled()
    {
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
        _countdownOverlay.SetActive(false);
        _rejoinPromptLabel.gameObject.SetActive(false);
        _rejoinBtnImage.color = EkDark;
        // Panel stays expanded — player explicitly left, show idle state
    }

    public void OnPlayerListChanged()
    {
        RefreshPartyIndicator();
        if (_expanded) RefreshFriendList();
    }

    public void OnVersionMapChanged()
    {
        var drift = _manager.HasVersionDrift;
        if (_driftBand != null)
            _driftBand.SetActive(drift);
        RefreshMinTabLabel();
    }

    private void OnUpdateClicked()
    {
        UnityEngine.Application.OpenURL(Plugin.ReleasesUrl);
        _showingRestartMsg = true;
        _restartMsgTimer = 5f;
        if (_driftBandText != null)
            _driftBandText.text = "⚠ Restart game after updating";
    }

    private void Update()
    {
        if (!_showingRestartMsg) return;
        _restartMsgTimer -= UnityEngine.Time.deltaTime;
        if (_restartMsgTimer <= 0f)
        {
            _showingRestartMsg = false;
            if (_driftBandText != null)
                _driftBandText.text = "⚠ Version mismatch";
        }
    }

    private void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        _expandedPanel.SetActive(expanded);
        _minTab.SetActive(!expanded);
        if (expanded) RefreshFriendList();
    }

    private void ToggleExpanded() => SetExpanded(!_expanded);

    private void RefreshPartyIndicator()
    {
        if (_partyDot == null || _partyText == null) return;
        int count = _manager.RoomSteamIds.Count;

        if (count == 0)
        {
            _partyText.text = "—";
            _partyDot.color = EkGray;
        }
        else
        {
            _partyText.text = $"{count} in party";
            _partyDot.color = count >= 2 ? EkGreen : EkGray;
        }
    }

    private void RefreshCodeLabel()
    {
        if (_codeLabelText != null && !_codeEditing)
            _codeLabelText.text = $"Code: {_manager.Config.LobbyRoomName}";
        RefreshMinTabLabel();
    }

    private void RefreshMinTabLabel()
    {
        if (_codeText == null) return;
        if (_manager.HasVersionDrift)
        {
            _codeText.text = $"{_manager.Config.LobbyRoomName} ●";
            _codeText.color = new Color(1f, 0.55f, 0f, 1f); // amber
        }
        else
        {
            _codeText.text = _manager.Config.LobbyRoomName;
            _codeText.color = EkOffWhite;
        }
    }

    private void StartCodeEdit()
    {
        _codeEditing = true;
        _codeLabelText.gameObject.SetActive(false);
        _codeInputField.gameObject.SetActive(true);
        _codeInputField.text = _manager.Config.LobbyRoomName;
        _codeInputField.ActivateInputField();
        _editBtnGo.SetActive(false);
        _saveBtnGo.SetActive(true);
    }

    private void CommitCodeEdit()
    {
        var newName = (_codeInputField.text ?? "").Trim();
        if (!string.IsNullOrEmpty(newName))
            _manager.UpdateRoomName(newName);

        _codeEditing = false;
        _codeLabelText.gameObject.SetActive(true);
        _codeInputField.gameObject.SetActive(false);
        _editBtnGo.SetActive(true);
        _saveBtnGo.SetActive(false);
        RefreshCodeLabel();
    }

    private void RefreshFriendList()
    {
        for (int i = _friendListContainer.childCount - 1; i >= 0; i--)
            Destroy(_friendListContainer.GetChild(i).gameObject);

        float rowH = 22 * _s;
        float y = 0f;
        int idx = 0;
        bool isMaster = _manager.IsMasterClient;

        foreach (var friend in _manager.Config.Friends)
        {
            bool online = SteamInviter.IsOnline(friend.Steam64Id);
            bool inRoom = _manager.RoomSteamIds.Contains(friend.Steam64Id);
            bool showKick = isMaster && inRoom;
            var captured = friend;

            var row = new GameObject($"Row_{friend.DisplayName}");
            row.transform.SetParent(_friendListContainer, false);
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0, 1);
            rowRt.anchorMax = new Vector2(1, 1);
            rowRt.pivot = new Vector2(0, 1);
            rowRt.anchoredPosition = new Vector2(0, -y);
            rowRt.sizeDelta = new Vector2(0, rowH);

            row.AddComponent<Image>().color = (idx % 2 == 0)
                ? new Color(0.16f, 0.16f, 0.16f, 1f)
                : new Color(0.11f, 0.11f, 0.11f, 1f);

            // Name column: shorter when kick button is present
            float nameW = showKick ? 190 * _s : 240 * _s;
            var nameText = CreateText(row.transform,
                $"{(online ? "●" : "○")} {friend.DisplayName}", (int)(11 * _s));
            PositionRect(nameText.rectTransform,
                new Vector2(4 * _s, 1 * _s), new Vector2(nameW, rowH - 2 * _s));
            if (!online) nameText.color = new Color(0.55f, 0.55f, 0.55f, 1f);

            // Kick button — leader only, only for players currently in the room
            if (showKick)
            {
                var kickBtn = CreateButton(row.transform, "kick", (int)(9 * _s),
                    new Vector2(196 * _s, 1 * _s), new Vector2(30 * _s, rowH - 2 * _s));
                kickBtn.GetComponent<Image>().color = EkRed;
                System.Action kickAction = () =>
                {
                    _manager.KickPlayer(captured.Steam64Id);
                    RefreshFriendList();
                };
                kickBtn.onClick.AddListener((UnityEngine.Events.UnityAction)kickAction);
            }

            // Remove button (always present; shifts left to make room for kick button)
            float removeBtnX = showKick ? 230 * _s : 250 * _s;
            var removeBtn = CreateButton(row.transform, "✕", (int)(10 * _s),
                new Vector2(removeBtnX, 1 * _s), new Vector2(26 * _s, rowH - 2 * _s));
            removeBtn.GetComponent<Image>().color = EkRedDark;
            System.Action removeAction = () =>
            {
                _manager.RemoveFriend(captured.Steam64Id);
                RefreshFriendList();
            };
            removeBtn.onClick.AddListener((UnityEngine.Events.UnityAction)removeAction);

            y += rowH;
            idx++;
        }

        if (_friendListContentRt != null)
            _friendListContentRt.sizeDelta = new Vector2(0, y);
    }

    // NOTE: The room code is written to the system clipboard. Any application in this
    // Windows session can read it. This is intentional — the code is non-secret by design
    // (it is shared to invite friends). Do not write Steam credentials or the Discord secret here.
    private void CopyCodeToClipboard() =>
        GUIUtility.systemCopyBuffer = _manager.Config.LobbyRoomName;

    private void InviteAll() =>
        SteamInviter.InviteAll(
            _manager.Config.Friends.ConvertAll(f => f.Steam64Id),
            _manager.Config.LobbyRoomName);

    private void DoRejoin() => _manager.JoinOrCreateHomeLobby();

    private void OpenFriendPicker() =>
        FriendPickerPopup.Open(_manager, transform.parent, RefreshFriendList);

    private System.Collections.IEnumerator RunCountdown()
    {
        for (int i = 5; i >= 1; i--)
        {
            _countdownDigit.text = i.ToString();
            yield return new WaitForSeconds(1f);

            if (!_manager.AutoQueueActive)
            {
                // Cancelled externally (AutoQueueCancelled event fired, flag already cleared)
                _countdownOverlay.SetActive(false);
                yield break;
            }
        }
        _countdownOverlay.SetActive(false);
        _manager.JoinOrCreateHomeLobby();
    }

    // ── uGUI helpers ──────────────────────────────────────────────────────────

    private static Canvas FindTopCanvas()
    {
        Canvas top = null;
        int maxOrder = int.MinValue;
        foreach (var c in FindObjectsOfType<Canvas>())
        {
            if (c.sortingOrder > maxOrder) { maxOrder = c.sortingOrder; top = c; }
        }
        return top;
    }

    private static GameObject CreatePanel(Transform parent, Vector2 size,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        return go;
    }

    private static Text CreateText(Transform parent, string content, int size)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var t = go.AddComponent<Text>();
        t.text = content;
        t.fontSize = size;
        t.color = EkOffWhite;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return t;
    }

    private static Button CreateMinimizeButton(Transform parent, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject("Btn_Minimize");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
        var btn = go.AddComponent<Button>();
        var bar = new GameObject("Bar");
        bar.transform.SetParent(go.transform, false);
        var barRt = bar.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0.18f, 0.27f);
        barRt.anchorMax = new Vector2(0.82f, 0.34f);
        barRt.offsetMin = Vector2.zero;
        barRt.offsetMax = Vector2.zero;
        bar.AddComponent<Image>().color = EkOffWhite;
        return btn;
    }

    private static Button CreateButton(Transform parent, string label, int fontSize,
        Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.AddComponent<Image>().color = EkDark;
        var btn = go.AddComponent<Button>();
        var t = CreateText(go.transform, label, fontSize);
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        t.alignment = TextAnchor.MiddleCenter;
        return btn;
    }

    private static void CreateFloppyIcon(Transform parent)
    {
        // Label slot: light rectangle across upper portion of button
        var slot = new GameObject("FloppySlot");
        slot.transform.SetParent(parent, false);
        var slotRt = slot.AddComponent<RectTransform>();
        slotRt.anchorMin = new Vector2(0.08f, 0.62f);
        slotRt.anchorMax = new Vector2(0.92f, 0.92f);
        slotRt.offsetMin = slotRt.offsetMax = Vector2.zero;
        slot.AddComponent<Image>().color = new Color(0.82f, 0.82f, 0.76f, 1f);

        // Metallic window: small dark square in lower-center
        var win = new GameObject("FloppyWindow");
        win.transform.SetParent(parent, false);
        var winRt = win.AddComponent<RectTransform>();
        winRt.anchorMin = new Vector2(0.28f, 0.12f);
        winRt.anchorMax = new Vector2(0.72f, 0.55f);
        winRt.offsetMin = winRt.offsetMax = Vector2.zero;
        win.AddComponent<Image>().color = new Color(0.28f, 0.28f, 0.28f, 1f);
    }

    private static void PositionRect(RectTransform rt, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }
}

internal static class GameObjectExt
{
    public static T GetOrAddComponent<T>(this GameObject go) where T : Component =>
        go.GetComponent<T>() ?? go.AddComponent<T>();
}
