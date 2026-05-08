using EKLobbyShared;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UObj = UnityEngine.Object;

namespace EKLobbyMod;

public class FriendPickerPopup : MonoBehaviour
{
    private LobbyManager _manager;
    private System.Action _onFriendAdded;
    private InputField _searchField;
    private Transform _listContainer;
    private RectTransform _listContentRt;
    private string _lastSearch = "";
    private float _s = 1f;

    // Exploding Kittens brand palette
    private static readonly Color EkBlack    = new Color(0.059f, 0.059f, 0.059f, 0.97f);
    private static readonly Color EkRed      = new Color(0.506f, 0.141f, 0.176f, 1f);
    private static readonly Color EkOffWhite = new Color(0.988f, 0.972f, 0.933f, 1f);
    private static readonly Color EkDark     = new Color(0.14f,  0.14f,  0.14f,  1f);

    public static void Open(LobbyManager manager, Transform canvasTransform, System.Action onFriendAdded = null)
    {
        var existing = canvasTransform.Find("EKFriendPicker");
        if (existing != null) UObj.Destroy(existing.gameObject);

        float s = Mathf.Clamp(Mathf.Sqrt(Screen.width * (float)Screen.height) / 1152f, 0.6f, 2.5f);

        var root = new GameObject("EKFriendPicker");
        root.transform.SetParent(canvasTransform, false);

        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(300 * s, 400 * s);
        rt.anchoredPosition = Vector2.zero;

        root.AddComponent<Image>().color = EkBlack;

        var popup = root.AddComponent<FriendPickerPopup>();
        popup._manager = manager;
        popup._onFriendAdded = onFriendAdded;
        popup._s = s;
        popup.Build();
    }

    private void Build()
    {
        float s = _s;

        // EK-red header strip
        var headerStrip = new GameObject("HeaderStrip");
        headerStrip.transform.SetParent(transform, false);
        var hsRt = headerStrip.AddComponent<RectTransform>();
        PositionRect(hsRt, new Vector2(0, 356 * s), new Vector2(300 * s, 44 * s));
        headerStrip.AddComponent<Image>().color = EkRed;

        var title = CreateText(transform, "ADD FRIEND", (int)(15 * s));
        title.fontStyle = FontStyle.Bold;
        PositionRect(title.rectTransform, new Vector2(10 * s, 358 * s), new Vector2(240 * s, 40 * s));
        title.alignment = TextAnchor.MiddleLeft;

        var capturedGo = gameObject;
        System.Action closeAction = () => UObj.Destroy(capturedGo);
        var closeBtn = CreateButton(transform, "X", (int)(12 * s),
            new Vector2(266 * s, 362 * s), new Vector2(28 * s, 32 * s));
        closeBtn.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
        closeBtn.onClick.AddListener((UnityEngine.Events.UnityAction)closeAction);

        // Search field
        var searchGo = new GameObject("Search");
        searchGo.transform.SetParent(transform, false);
        PositionRect(searchGo.AddComponent<RectTransform>(),
            new Vector2(6 * s, 322 * s), new Vector2(288 * s, 26 * s));
        searchGo.AddComponent<Image>().color = EkDark;

        var phText = CreateText(searchGo.transform, "Search friends...", (int)(11 * s));
        phText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        phText.rectTransform.anchorMin = Vector2.zero;
        phText.rectTransform.anchorMax = Vector2.one;
        phText.rectTransform.offsetMin = new Vector2(4 * s, 0);
        phText.rectTransform.offsetMax = Vector2.zero;

        var inputText = CreateText(searchGo.transform, "", (int)(11 * s));
        inputText.rectTransform.anchorMin = Vector2.zero;
        inputText.rectTransform.anchorMax = Vector2.one;
        inputText.rectTransform.offsetMin = new Vector2(4 * s, 0);
        inputText.rectTransform.offsetMax = Vector2.zero;

        _searchField = searchGo.AddComponent<InputField>();
        _searchField.textComponent = inputText;
        _searchField.placeholder = phText;

        // Scrollable friend list
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(transform, false);
        var vpRt = viewport.AddComponent<RectTransform>();
        PositionRect(vpRt, new Vector2(6 * s, 6 * s), new Vector2(288 * s, 308 * s));
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        _listContentRt = content.AddComponent<RectTransform>();
        _listContentRt.anchorMin = new Vector2(0, 1);
        _listContentRt.anchorMax = new Vector2(1, 1);
        _listContentRt.pivot = new Vector2(0, 1);
        _listContentRt.offsetMin = Vector2.zero;
        _listContentRt.offsetMax = Vector2.zero;
        _listContentRt.sizeDelta = Vector2.zero;
        _listContainer = content.transform;

        var sr = viewport.AddComponent<ScrollRect>();
        sr.content = _listContentRt;
        sr.vertical = true;
        sr.horizontal = false;
        sr.scrollSensitivity = 20 * s;
        sr.movementType = ScrollRect.MovementType.Clamped;

        RefreshList("");
    }

    private void Update()
    {
        if (_searchField == null) return;
        var text = _searchField.text ?? "";
        if (text != _lastSearch)
        {
            _lastSearch = text;
            RefreshList(text);
        }
    }

    private void RefreshList(string query)
    {
        for (int i = _listContainer.childCount - 1; i >= 0; i--)
            Destroy(_listContainer.GetChild(i).gameObject);

        float s = _s;
        float rowH = 28 * s;
        float y = 0f;
        int idx = 0;

        var saved = new HashSet<string>(_manager.Config.Friends.ConvertAll(f => f.Steam64Id));

        foreach (var friend in SteamInviter.GetAllSteamFriends())
        {
            if (saved.Contains(friend.Steam64Id)) continue;
            if (!FuzzyMatch(friend.DisplayName, query)) continue;

            var captured = friend;
            var capturedGo = gameObject;

            var row = new GameObject($"Row_{friend.DisplayName}");
            row.transform.SetParent(_listContainer, false);
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0, 1);
            rowRt.anchorMax = new Vector2(1, 1);
            rowRt.pivot = new Vector2(0, 1);
            rowRt.anchoredPosition = new Vector2(0, -y);
            rowRt.sizeDelta = new Vector2(0, rowH);

            row.AddComponent<Image>().color = (idx % 2 == 0)
                ? new Color(0.16f, 0.16f, 0.16f, 1f)
                : new Color(0.11f, 0.11f, 0.11f, 1f);

            var nameText = CreateText(row.transform, friend.DisplayName, (int)(11 * s));
            PositionRect(nameText.rectTransform,
                new Vector2(4 * s, 2 * s), new Vector2(224 * s, rowH - 4 * s));

            var addBtn = CreateButton(row.transform, "+", (int)(13 * s),
                new Vector2(252 * s, 2 * s), new Vector2(32 * s, rowH - 4 * s));
            addBtn.GetComponent<Image>().color = EkRed;
            System.Action addAction = () =>
            {
                _manager.AddFriend(captured);
                _onFriendAdded?.Invoke();
                UObj.Destroy(capturedGo);
            };
            addBtn.onClick.AddListener((UnityEngine.Events.UnityAction)addAction);

            y += rowH;
            idx++;
        }

        if (_listContentRt != null)
            _listContentRt.sizeDelta = new Vector2(0, y);
    }

    private static bool FuzzyMatch(string target, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        var t = target.ToLower();
        var q = query.ToLower();
        int qi = 0;
        for (int ti = 0; ti < t.Length && qi < q.Length; ti++)
            if (t[ti] == q[qi]) qi++;
        return qi == q.Length;
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
        t.rectTransform.anchorMin = Vector2.zero;
        t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = Vector2.zero;
        t.rectTransform.offsetMax = Vector2.zero;
        t.alignment = TextAnchor.MiddleCenter;
        return btn;
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
