using System.Collections.Generic;
using EKLobbyShared;
using UnityEngine;
using UnityEngine.UI;

namespace EKLobbyMod;

public class OverlayPanel : MonoBehaviour
{
    private LobbyManager _manager = null!;
    private bool _expanded = false;

    private GameObject _expandedPanel = null!;
    private Text _codeText = null!;
    private Transform _friendListContainer = null!;
    private Button _rejoinButton = null!;
    private Text _rejoinPromptLabel = null!;

    private GameObject _minTab = null!;

    public static OverlayPanel Inject(LobbyManager manager)
    {
        var canvas = FindTopCanvas();
        if (canvas == null)
        {
            Plugin.Log.LogWarning("No Canvas found — overlay not injected");
            return null;
        }

        var existing = canvas.GetComponentInChildren<OverlayPanel>();
        if (existing != null)
        {
            existing._manager = manager;
            return existing;
        }

        var go = new GameObject("EKLobbyOverlay");
        go.transform.SetParent(canvas.transform, false);
        var panel = go.AddComponent<OverlayPanel>();
        panel._manager = manager;
        panel.Build();
        manager.RejoinAvailable += (System.Action)panel.ShowRejoinPrompt;
        manager.RejoinConfirmed += (System.Action)panel.HideRejoinPrompt;
        return panel;
    }

    private void Build()
    {
        BuildMinTab();
        BuildExpandedPanel();
        SetExpanded(false);
    }

    private void BuildMinTab()
    {
        _minTab = CreatePanel(transform, new Vector2(160, 32),
            new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, 16));

        _minTab.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

        var tabButton = _minTab.AddComponent<Button>();
        tabButton.onClick.AddListener((UnityEngine.Events.UnityAction)ToggleExpanded);

        _codeText = CreateText(_minTab.transform, "", 12);
        RefreshCodeLabel();
    }

    private void BuildExpandedPanel()
    {
        _expandedPanel = CreatePanel(transform, new Vector2(220, 300),
            new Vector2(0, 0), new Vector2(0, 0), new Vector2(110, 150));

        _expandedPanel.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

        var header = CreateText(_expandedPanel.transform, "EK Lobby", 14);
        PositionRect(header.rectTransform, new Vector2(0, 278), new Vector2(180, 20));

        var collapseBtn = CreateButton(_expandedPanel.transform, "[_]", 11,
            new Vector2(185, 278), new Vector2(30, 20));
        collapseBtn.onClick.AddListener((UnityEngine.Events.UnityAction)ToggleExpanded);

        var codeLabel = CreateText(_expandedPanel.transform,
            $"Code: {_manager.Config.LobbyRoomName}", 12);
        PositionRect(codeLabel.rectTransform, new Vector2(5, 253), new Vector2(160, 18));

        var copyBtn = CreateButton(_expandedPanel.transform, "Copy", 11,
            new Vector2(170, 253), new Vector2(40, 18));
        copyBtn.onClick.AddListener((UnityEngine.Events.UnityAction)CopyCodeToClipboard);

        var scroll = new GameObject("FriendListScroll");
        scroll.transform.SetParent(_expandedPanel.transform, false);
        PositionRect(scroll.GetOrAddComponent<RectTransform>(),
            new Vector2(5, 80), new Vector2(210, 163));
        _friendListContainer = scroll.transform;

        var addBtn = CreateButton(_expandedPanel.transform, "+ Add", 11,
            new Vector2(160, 60), new Vector2(50, 18));
        addBtn.onClick.AddListener((UnityEngine.Events.UnityAction)OpenFriendPicker);

        // Amber "game over" label — hidden until OnLeftRoom fires
        _rejoinPromptLabel = CreateText(_expandedPanel.transform,
            "Game over — return to your lobby?", 10);
        PositionRect(_rejoinPromptLabel.rectTransform, new Vector2(5, 48), new Vector2(210, 18));
        _rejoinPromptLabel.color = new Color(1f, 0.85f, 0.3f, 1f);
        _rejoinPromptLabel.gameObject.SetActive(false);

        var inviteAllBtn = CreateButton(_expandedPanel.transform, "Invite All", 12,
            new Vector2(5, 5), new Vector2(90, 28));
        inviteAllBtn.onClick.AddListener((UnityEngine.Events.UnityAction)InviteAll);

        _rejoinButton = CreateButton(_expandedPanel.transform, "Rejoin", 12,
            new Vector2(115, 5), new Vector2(90, 28));
        _rejoinButton.onClick.AddListener((UnityEngine.Events.UnityAction)DoRejoin);
    }

    public void ShowRejoinPrompt()
    {
        SetExpanded(true);
        _rejoinPromptLabel.gameObject.SetActive(true);
        var colors = _rejoinButton.colors;
        colors.normalColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        _rejoinButton.colors = colors;
    }

    public void HideRejoinPrompt()
    {
        _rejoinPromptLabel.gameObject.SetActive(false);
        var colors = _rejoinButton.colors;
        colors.normalColor = ColorBlock.defaultColorBlock.normalColor;
        _rejoinButton.colors = colors;
    }

    private void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        _expandedPanel.SetActive(expanded);
        _minTab.SetActive(!expanded);
        if (expanded) RefreshFriendList();
    }

    private void ToggleExpanded() => SetExpanded(!_expanded);

    private void RefreshCodeLabel()
    {
        if (_codeText != null)
            _codeText.text = _manager.Config.LobbyRoomName;
    }

    private void RefreshFriendList()
    {
        foreach (Transform child in _friendListContainer)
            Destroy(child.gameObject);

        float y = 0f;
        foreach (var friend in _manager.Config.Friends)
        {
            bool online = SteamInviter.IsOnline(friend.Steam64Id);
            var row = CreateText(_friendListContainer,
                $"{(online ? "●" : "○")} {friend.DisplayName}", 11);
            PositionRect(row.rectTransform, new Vector2(0, -y), new Vector2(200, 18));
            y += 20f;
        }
    }

    private void CopyCodeToClipboard() =>
        GUIUtility.systemCopyBuffer = _manager.Config.LobbyRoomName;

    private void InviteAll() =>
        SteamInviter.InviteAll(_manager.Config.Friends.ConvertAll(f => f.Steam64Id));

    private void DoRejoin() => _manager.JoinOrCreateHomeLobby();

    private void OpenFriendPicker() =>
        FriendPickerPopup.Open(_manager, transform.parent);

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
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
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
        t.color = Color.white;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return t;
    }

    private static Button CreateButton(Transform parent, string label, int fontSize,
        Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
        var btn = go.AddComponent<Button>();
        CreateText(go.transform, label, fontSize);
        return btn;
    }

    private static void PositionRect(RectTransform rt, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }
}

internal static class GameObjectExt
{
    public static T GetOrAddComponent<T>(this GameObject go) where T : Component =>
        go.GetComponent<T>() ?? go.AddComponent<T>();
}
