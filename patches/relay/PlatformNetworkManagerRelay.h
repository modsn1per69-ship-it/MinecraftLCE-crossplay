#pragma once

#include <vector>
#include "..\PlatformNetworkManagerInterface.h"
#include "NetworkPlayerRelay.h"
#include "RelayTransport.h"

class CPlatformNetworkManagerRelay : public CPlatformNetworkManager
{
	friend class CGameNetworkManager;
public:
	virtual bool Initialise(CGameNetworkManager *pGameNetworkManager, int flagIndexSize);
	virtual void Terminate();
	virtual int GetJoiningReadyPercentage();
	virtual int CorrectErrorIDS(int IDS);

	virtual void DoWork();
	virtual int GetPlayerCount();
	virtual int GetOnlinePlayerCount();
	virtual int GetLocalPlayerMask(int playerIndex);
	virtual bool AddLocalPlayerByUserIndex(int userIndex);
	virtual bool RemoveLocalPlayerByUserIndex(int userIndex);
	virtual INetworkPlayer *GetLocalPlayerByUserIndex(int userIndex);
	virtual INetworkPlayer *GetPlayerByIndex(int playerIndex);
	virtual INetworkPlayer *GetPlayerByXuid(PlayerUID xuid);
	virtual INetworkPlayer *GetPlayerBySmallId(unsigned char smallId);
	virtual bool ShouldMessageForFullSession();

	virtual INetworkPlayer *GetHostPlayer();
	virtual bool IsHost();
	virtual bool JoinGameFromInviteInfo(int userIndex, int userMask, const INVITE_INFO *pInviteInfo);
	virtual bool LeaveGame(bool bMigrateHost);

	virtual bool IsInSession();
	virtual bool IsInGameplay();
	virtual bool IsReadyToPlayOrIdle();
	virtual bool IsInStatsEnabledSession();
	virtual bool SessionHasSpace(unsigned int spaceRequired = 1);
	virtual void SendInviteGUI(int quadrant);
	virtual bool IsAddingPlayer();

	virtual void HostGame(int localUsersMask, bool bOnlineGame, bool bIsPrivate, unsigned char publicSlots = MINECRAFT_NET_MAX_PLAYERS, unsigned char privateSlots = 0);
	virtual int JoinGame(FriendSessionInfo *searchResult, int localUsersMask, int primaryUserIndex);
	virtual bool SetLocalGame(bool isLocal);
	virtual bool IsLocalGame() { return m_bIsOfflineGame; }
	virtual void SetPrivateGame(bool isPrivate);
	virtual bool IsPrivateGame() { return m_bIsPrivateGame; }
	virtual bool IsLeavingGame() { return m_bLeavingGame; }
	virtual void ResetLeavingGame() { m_bLeavingGame = false; }

	virtual void RegisterPlayerChangedCallback(int iPad, void (*callback)(void *callbackParam, INetworkPlayer *pPlayer, bool leaving), void *callbackParam);
	virtual void UnRegisterPlayerChangedCallback(int iPad, void (*callback)(void *callbackParam, INetworkPlayer *pPlayer, bool leaving), void *callbackParam);

	virtual void HandleSignInChange();
	virtual bool _RunNetworkGame();

	virtual void UpdateAndSetGameSessionData(INetworkPlayer *pNetworkPlayerLeaving = NULL);
	virtual void SystemFlagSet(INetworkPlayer *pNetworkPlayer, int index);
	virtual bool SystemFlagGet(INetworkPlayer *pNetworkPlayer, int index);
	virtual std::wstring GatherStats();
	virtual std::wstring GatherRTTStats();

	virtual std::vector<FriendSessionInfo *> *GetSessionList(int iPad, int localPlayers, bool partyOnly);
	virtual bool GetGameSessionInfo(int iPad, SessionID sessionId, FriendSessionInfo *foundSession);
	virtual void SetSessionsUpdatedCallback(void (*SessionsUpdatedCallback)(LPVOID pParam), LPVOID pSearchParam);
	virtual void GetFullFriendSessionInfo(FriendSessionInfo *foundSession, void (*FriendSessionUpdatedFn)(bool success, void *pParam), void *pParam);
	virtual void ForceFriendsSessionRefresh();

private:
	virtual bool _LeaveGame(bool bMigrateHost, bool bLeaveRoom);
	virtual void _HostGame(int usersMask, unsigned char publicSlots = MINECRAFT_NET_MAX_PLAYERS, unsigned char privateSlots = 0);
	virtual bool _StartGame();
	virtual bool RemoveLocalPlayer(INetworkPlayer *pNetworkPlayer);
	virtual void SetSessionTexturePackParentId(int id);
	virtual void SetSessionSubTexturePackId(int id);
	virtual void Notify(int ID, ULONG_PTR Param);

	void EnsureLocalPlayer();
	void EnsureRemotePlayer();
	NetworkPlayerRelay *EnsureRemotePlayer(unsigned int peerId);
	NetworkPlayerRelay *FindRemotePlayer(unsigned int peerId);
	void RemoveRemotePlayer(unsigned int peerId);
	void NotifyPlayerJoined(NetworkPlayerRelay *networkPlayer, bool createSocket, bool localPlayer);
	void ConnectRelay(const char *roleName);
	void HandleRelayEvent(RelayTransport::ReceiveEvent event, unsigned int peerId, const unsigned char *data, int dataSize);
	static void StaticReceiveCallback(RelayTransport::ReceiveEvent event, unsigned int peerId, const unsigned char *data, int dataSize, void *context);

	class PlayerFlags
	{
	public:
		INetworkPlayer *m_pNetworkPlayer;
		unsigned char *flags;
		unsigned int count;
		PlayerFlags(INetworkPlayer *pNetworkPlayer, unsigned int count);
		~PlayerFlags();
	};

	void SystemFlagAddPlayer(INetworkPlayer *pNetworkPlayer);
	void SystemFlagRemovePlayer(INetworkPlayer *pNetworkPlayer);
	void SystemFlagReset();

	CGameNetworkManager *m_pGameNetworkManager;
	RelayTransport m_transport;
	std::vector<NetworkPlayerRelay *> m_players;
	std::vector<NetworkPlayerRelay *> m_retiredPlayers;
	NetworkPlayerRelay *m_localPlayer;
	NetworkPlayerRelay *m_remotePlayer;
	std::vector<PlayerFlags *> m_playerFlags;
	GameSessionData m_hostGameSessionData;
	int m_flagIndexSize;
	bool m_bLeavingGame;
	bool m_bIsOfflineGame;
	bool m_bIsPrivateGame;
	bool m_bHost;
	bool m_bInSession;
	bool m_bInGameplay;
	bool m_bRelayConnectAttempted;
	bool m_bJoinStartNotified;
	void (*m_SessionsUpdatedCallback)(LPVOID pParam);
	LPVOID m_pSearchParam;
	void (*playerChangedCallback[XUSER_MAX_COUNT])(void *callbackParam, INetworkPlayer *pPlayer, bool leaving);
	void *playerChangedCallbackParam[XUSER_MAX_COUNT];
};
