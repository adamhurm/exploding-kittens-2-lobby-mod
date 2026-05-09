using System;
using System.IO;
using EKLobbyMod;
using EKLobbyShared;
using Xunit;

namespace EKLobbyMod.Tests;

public class LobbyManagerStateMachineTests : IDisposable
{
    private readonly FakePhotonBridge _fake;
    private readonly LobbyManager _manager;

    public LobbyManagerStateMachineTests()
    {
        ConfigStore.OverridePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json");
        _fake = new FakePhotonBridge { CurrentRoomName = "MY-ROOM" };
        _manager = new LobbyManager(_fake);
        _manager.Config.LobbyRoomName = "MY-ROOM";
    }

    public void Dispose() => ConfigStore.OverridePath = null;

    // ── Home lobby join ───────────────────────────────────────────────────────

    [Fact]
    public void HandleJoinedRoom_WhenRoomMatchesConfig_SetsInHomeLobby()
    {
        _manager.HandleJoinedRoom();
        Assert.True(_manager.InHomeLobby);
        Assert.False(_manager.InGame);
    }

    [Fact]
    public void HandleJoinedRoom_HomeLobby_ClearsPendingRejoin()
    {
        _fake.CurrentRoomName = "GAME-ROOM";
        _manager.HandleJoinedRoom();   // enter game room
        _manager.HandleLeftRoom();     // game ends, PendingRejoin=true
        Assert.True(_manager.PendingRejoin);

        _fake.CurrentRoomName = "MY-ROOM";
        _manager.HandleJoinedRoom();   // rejoin home lobby
        Assert.False(_manager.PendingRejoin);
    }

    // ── Game room join ────────────────────────────────────────────────────────

    [Fact]
    public void HandleJoinedRoom_WhenRoomDiffersFromConfig_SetsInGame()
    {
        _fake.CurrentRoomName = "GAME-ROOM";
        _manager.HandleJoinedRoom();
        Assert.True(_manager.InGame);
        Assert.False(_manager.InHomeLobby);
    }

    [Fact]
    public void HandleJoinedRoom_GameRoom_FiresGameStarted()
    {
        bool fired = false;
        _manager.GameStarted += () => fired = true;
        _fake.CurrentRoomName = "GAME-ROOM";
        _manager.HandleJoinedRoom();
        Assert.True(fired);
    }

    // ── Game ends normally ────────────────────────────────────────────────────

    [Fact]
    public void HandleLeftRoom_AfterGame_SetsPendingRejoin()
    {
        _fake.CurrentRoomName = "GAME-ROOM";
        _manager.HandleJoinedRoom();
        _manager.HandleLeftRoom();
        Assert.True(_manager.PendingRejoin);
        Assert.False(_manager.InGame);
    }

    [Fact]
    public void HandleLeftRoom_AfterGame_FiresRejoinAvailable()
    {
        bool fired = false;
        _manager.RejoinAvailable += () => fired = true;
        _fake.CurrentRoomName = "GAME-ROOM";
        _manager.HandleJoinedRoom();
        _manager.HandleLeftRoom();
        Assert.True(fired);
    }

    // ── Mid-game leave ────────────────────────────────────────────────────────

    [Fact]
    public void LeaveToHomeLobby_MidGame_DoesNotSetPendingRejoin()
    {
        _manager.HandleJoinedRoom();           // enter home lobby
        _fake.CurrentRoomName = "GAME-ROOM";
        _manager.HandleJoinedRoom();           // enter game room
        _manager.LeaveToHomeLobby();           // explicit leave
        _manager.HandleLeftRoom();             // game leave completes
        Assert.False(_manager.PendingRejoin);
    }

    // ── HandleLeftRoom in unknown state ───────────────────────────────────────

    [Fact]
    public void HandleLeftRoom_WhenNeitherInGameNorInHomeLobby_ClearsPendingRejoin()
    {
        // Neither flag is set — unknown state; PendingRejoin should be cleared
        _manager.HandleLeftRoom();
        Assert.False(_manager.PendingRejoin);
    }

    // ── Invite join, host present ─────────────────────────────────────────────

    [Fact]
    public void JoinRoomByInvite_HostPresent_RestoresHomeLobbyName()
    {
        _fake.CurrentRoomName = "FRIEND-ROOM";
        _manager.JoinRoomByInvite("FRIEND-ROOM");
        _manager.HandleJoinedRoom();
        Assert.Equal("MY-ROOM", _manager.Config.LobbyRoomName);
    }

    [Fact]
    public void JoinRoomByInvite_HostPresent_SetsInHomeLobby()
    {
        _fake.CurrentRoomName = "FRIEND-ROOM";
        _manager.JoinRoomByInvite("FRIEND-ROOM");
        _manager.HandleJoinedRoom();
        Assert.True(_manager.InHomeLobby);
    }

    // ── Invite join, host absent (join fails → create) ────────────────────────

    [Fact]
    public void JoinRoomByInvite_HostAbsent_RestoresHomeLobbyName()
    {
        _manager.JoinRoomByInvite("FRIEND-ROOM");
        _manager.HandleJoinRoomFailed(32758, "Room not found");
        _fake.CurrentRoomName = "FRIEND-ROOM";
        _manager.HandleCreatedRoom();
        Assert.Equal("MY-ROOM", _manager.Config.LobbyRoomName);
    }

    [Fact]
    public void JoinRoomByInvite_HostAbsent_SetsInHomeLobby()
    {
        _manager.JoinRoomByInvite("FRIEND-ROOM");
        _manager.HandleJoinRoomFailed(32758, "Room not found");
        _fake.CurrentRoomName = "FRIEND-ROOM";
        _manager.HandleCreatedRoom();
        Assert.True(_manager.InHomeLobby);
    }

    // ── Double invite guard ───────────────────────────────────────────────────

    [Fact]
    public void JoinRoomByInvite_WhileInvitePending_IsIgnored()
    {
        _manager.JoinRoomByInvite("FRIEND-ROOM-1");
        _manager.JoinRoomByInvite("FRIEND-ROOM-2"); // ignored
        Assert.Equal("FRIEND-ROOM-1", _manager.Config.LobbyRoomName);
    }

    // ── JoinRoomFailed not pending → no-op ───────────────────────────────────

    [Fact]
    public void HandleJoinRoomFailed_WhenNotPending_IsIgnored()
    {
        _manager.HandleJoinRoomFailed(32758, "Room not found");
        Assert.False(_manager.InHomeLobby); // no state change
    }

    // ── Join-or-create escalation ─────────────────────────────────────────────

    [Fact]
    public void JoinOrCreate_WhenJoinFails_CanCompleteViaCreate()
    {
        _manager.JoinOrCreateHomeLobby();
        _manager.HandleJoinRoomFailed(32758, "Room not found");
        _manager.HandleCreatedRoom();
        Assert.True(_manager.InHomeLobby);
    }

    // ── Recreate home lobby ───────────────────────────────────────────────────

    [Fact]
    public void RecreateHomeLobby_ThenCreate_RestoresInHomeLobby()
    {
        _manager.HandleJoinedRoom();  // enter home lobby
        _manager.RecreateHomeLobby();
        _manager.HandleLeftRoom();    // old room left → triggers JoinOrCreateHomeLobby
        _manager.HandleCreatedRoom(); // new room created
        Assert.True(_manager.InHomeLobby);
    }

    // ── Player tracking ───────────────────────────────────────────────────────

    [Fact]
    public void PlayerEntered_WhileInHomeLobby_AddsToRoomSteamIds()
    {
        _manager.HandleJoinedRoom();
        _fake.FirePlayerEntered(new PlayerInfo("STEAM123"));
        Assert.Contains("STEAM123", _manager.RoomSteamIds);
    }

    [Fact]
    public void PlayerLeft_WhileInHomeLobby_RemovesFromRoomSteamIds()
    {
        _manager.HandleJoinedRoom();
        _fake.FirePlayerEntered(new PlayerInfo("STEAM123"));
        _fake.FirePlayerLeft(new PlayerInfo("STEAM123"));
        Assert.DoesNotContain("STEAM123", _manager.RoomSteamIds);
    }

    [Fact]
    public void PlayerEntered_WhileNotInHomeLobby_IsIgnored()
    {
        _fake.FirePlayerEntered(new PlayerInfo("STEAM123"));
        Assert.DoesNotContain("STEAM123", _manager.RoomSteamIds);
    }

    [Fact]
    public void PlayerListChanged_FiredOnPlayerEnter()
    {
        _manager.HandleJoinedRoom();
        bool fired = false;
        _manager.PlayerListChanged += () => fired = true;
        _fake.FirePlayerEntered(new PlayerInfo("STEAM123"));
        Assert.True(fired);
    }

    // ── Version drift ─────────────────────────────────────────────────────────

    [Fact]
    public void PlayerEntered_WithOlderVersion_SetsVersionDrift()
    {
        _manager.HandleJoinedRoom();
        _fake.FirePlayerEntered(new PlayerInfo("STEAM123", "1.0.0"));
        Assert.True(_manager.HasVersionDrift);
    }

    [Fact]
    public void PlayerEntered_WithCurrentVersion_NoVersionDrift()
    {
        _manager.HandleJoinedRoom();
        _fake.FirePlayerEntered(new PlayerInfo("STEAM123", Plugin.PluginVersion));
        Assert.False(_manager.HasVersionDrift);
    }

    [Fact]
    public void PlayerEntered_WithNoVersion_NotTrackedInVersionMap()
    {
        _manager.HandleJoinedRoom();
        _fake.FirePlayerEntered(new PlayerInfo("STEAM123")); // Version is null
        Assert.DoesNotContain("STEAM123", _manager.PeerVersions.Keys);
        Assert.False(_manager.HasVersionDrift);
    }

    [Fact]
    public void PlayerPropertiesChanged_UpdatesVersion()
    {
        _manager.HandleJoinedRoom();
        _fake.FirePlayerEntered(new PlayerInfo("STEAM123", "1.0.0"));
        Assert.True(_manager.HasVersionDrift);
        _fake.FirePlayerPropertiesChanged(new PlayerInfo("STEAM123", Plugin.PluginVersion));
        Assert.False(_manager.HasVersionDrift);
    }

    [Fact]
    public void VersionMapChanged_FiredOnPlayerPropertiesChanged()
    {
        _manager.HandleJoinedRoom();
        bool fired = false;
        _manager.VersionMapChanged += () => fired = true;
        _fake.FirePlayerPropertiesChanged(new PlayerInfo("STEAM123", "1.0.0"));
        Assert.True(fired);
    }

    [Fact]
    public void VersionMap_ClearedOnGameLeave()
    {
        _manager.HandleJoinedRoom();
        _fake.FirePlayerEntered(new PlayerInfo("STEAM123", "1.0.0"));
        Assert.True(_manager.HasVersionDrift);

        _fake.CurrentRoomName = "GAME-ROOM";
        _manager.HandleJoinedRoom(); // enter game
        _manager.HandleLeftRoom();   // leave game
        Assert.Empty(_manager.PeerVersions);
    }
}
