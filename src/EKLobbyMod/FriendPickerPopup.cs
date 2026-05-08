using System.Linq;
using EKLobbyShared;
using UnityEngine;
using UnityEngine.UI;
using UObj = UnityEngine.Object;

namespace EKLobbyMod;

public static class FriendPickerPopup
{
    public static void Open(LobbyManager manager, Transform canvasTransform)
    {
        var existing = canvasTransform.Find("EKFriendPicker");
        if (existing != null) UObj.Destroy(existing.gameObject);

        var root = new GameObject("EKFriendPicker");
        root.transform.SetParent(canvasTransform, false);

        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(260, 340);
        rt.anchoredPosition = Vector2.zero;

        root.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.97f);

        var title = CreateText(root.transform, "Add Friend", 14);
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchoredPosition = new Vector2(0, 150);
        titleRt.sizeDelta = new Vector2(240, 24);

        // Lambda closures can't be cast directly to IL2CPP UnityAction — wrap in System.Action first
        var closeRoot = root;
        System.Action closeAction = () => UObj.Destroy(closeRoot);
        var closeBtn = CreateButton(root.transform, "X", 12,
            new Vector2(110, 150), new Vector2(24, 24));
        closeBtn.onClick.AddListener((UnityEngine.Events.UnityAction)closeAction);

        var scrollContent = new GameObject("ScrollContent");
        scrollContent.transform.SetParent(root.transform, false);
        var scrollRt = scrollContent.AddComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0, 0);
        scrollRt.anchorMax = new Vector2(1, 1);
        scrollRt.offsetMin = new Vector2(5, 40);
        scrollRt.offsetMax = new Vector2(-5, -130);

        var alreadySaved = manager.Config.Friends
            .Select(f => f.Steam64Id)
            .ToHashSet();

        float y = 0f;
        foreach (var friend in SteamInviter.GetAllSteamFriends())
        {
            if (alreadySaved.Contains(friend.Steam64Id)) continue;

            var captured = friend;
            var capturedRoot = root;
            var row = new GameObject($"Row_{friend.DisplayName}");
            row.transform.SetParent(scrollContent.transform, false);
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.anchoredPosition = new Vector2(0, -y);
            rowRt.sizeDelta = new Vector2(240, 26);

            var nameText = CreateText(row.transform, friend.DisplayName, 11);
            nameText.GetComponent<RectTransform>().sizeDelta = new Vector2(180, 22);

            var addBtn = CreateButton(row.transform, "+", 12,
                new Vector2(100, 0), new Vector2(26, 22));
            System.Action addAction = () =>
            {
                manager.AddFriend(captured);
                UObj.Destroy(capturedRoot);
            };
            addBtn.onClick.AddListener((UnityEngine.Events.UnityAction)addAction);

            y += 28f;
        }
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
        Vector2 pos, Vector2 size)
    {
        var go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f);
        var btn = go.AddComponent<Button>();
        CreateText(go.transform, label, fontSize);
        return btn;
    }
}
