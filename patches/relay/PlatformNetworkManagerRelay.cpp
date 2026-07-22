#include "stdafx.h"
#include "..\..\..\..\Minecraft.World\Socket.h"
#include "..\..\..\..\Minecraft.World\SharedConstants.h"
#include "..\GameNetworkManager.h"
#include "PlatformNetworkManagerRelay.h"

#include <string.h>

typedef char RelayRequiresProtocol39[(SharedConstants::NETWORK_PROTOCOL_VERSION == 39) ? 1 : -1];
typedef char RelayRequiresNetVersion495[(MINECRAFT_NET_VERSION == 495) ? 1 : -1];

static const wchar_t *RELAY_SESSION_LABEL = L"Legacy Crossplay - LCE 1.2.3";

static void SetRelaySessionId(SessionID *sessionId)
{
	static const unsigned char relayId[] = { 0x58, 0x41, 0x11, 0xF7, 0x01, 0x00, 0x10, 0x00 };
	memset(sessionId, 0, sizeof(SessionID));
	const unsigned int copySize = sizeof(SessionID) < sizeof(relayId) ? sizeof(SessionID) : sizeof(relayId);
	memcpy(sessionId, relayId, copySize);
}

static bool IsRelaySessionId(const SessionID *sessionId)
{
	SessionID expected;
	SetRelaySessionId(&expected);
	return memcmp(sessionId, &expected, sizeof(SessionID)) == 0;
}

static void PopulateRelaySession(FriendSessionInfo *sessionInfo)
{
	SetRelaySessionId(&sessionInfo->sessionId);
	sessionInfo->data = GameSessionData();
	sessionInfo->data.netVersion = MINECRAFT_NET_VERSION;
#if defined(_XBOX)
	strncpy(sessionInfo->data.hostName, "Legacy Crossplay", XUSER_NAME_SIZE - 1);
	sessionInfo->data.hostName[XUSER_NAME_SIZE - 1] = 0;
	sessionInfo->data.isJoinable = true;
#elif defined(__PS3__) || defined(__ORBIS__) || defined(__PSVITA__)
	sessionInfo->data.isJoinable = true;
	sessionInfo->data.isReadyToJoin = true;
#else
	sessionInfo->data.isReadyToJoin = true;
#endif

	const unsigned int labelLength = (unsigned int)wcslen(RELAY_SESSION_LABEL);
	sessionInfo->displayLabel = new wchar_t[labelLength + 1];
	memcpy(sessionInfo->displayLabel, RELAY_SESSION_LABEL, (labelLength + 1) * sizeof(wchar_t));
	sessionInfo->displayLabelLength = (unsigned char)labelLength;
	sessionInfo->displayLabelViewableStartIndex = 0;
	sessionInfo->hasPartyMember = false;
}

bool CPlatformNetworkManagerRelay::Initialise(CGameNetworkManager *pGameNetworkManager, int flagIndexSize)
{
	m_pGameNetworkManager = pGameNetworkManager;
	m_flagIndexSize = flagIndexSize;
	m_localPlayer = NULL;
	m_remotePlayer = NULL;
	m_bLeavingGame = false;
	m_bIsOfflineGame = false;
	m_bIsPrivateGame = false;
	m_bHost = false;
	m_bInSession = false;
	m_bInGameplay = false;
	m_bRelayConnectAttempted = false;
	m_bJoinStartNotified = false;
	m_SessionsUpdatedCallback = NULL;
	m_pSearchParam = NULL;

	for (int i = 0; i < XUSER_MAX_COUNT; ++i)
	{
		playerChangedCallback[i] = NULL;
		playerChangedCallbackParam[i] = NULL;
	}

	return true;
}

void CPlatformNetworkManagerRelay::Terminate()
{
	m_transport.Close();
	for (unsigned int i = 0; i < m_players.size(); ++i)
	{
		delete m_players[i];
	}
	m_players.clear();
	for (unsigned int i = 0; i < m_retiredPlayers.size(); ++i)
	{
		delete m_retiredPlayers[i];
	}
	m_retiredPlayers.clear();
	SystemFlagReset();
}

int CPlatformNetworkManagerRelay::GetJoiningReadyPercentage()
{
	return m_transport.IsConnected() ? 100 : 0;
}

int CPlatformNetworkManagerRelay::CorrectErrorIDS(int IDS)
{
	return IDS;
}

void CPlatformNetworkManagerRelay::DoWork()
{
	if (m_bInSession && !m_transport.IsConnected() && !m_bRelayConnectAttempted)
	{
		ConnectRelay(m_bHost ? "host" : "client");
	}

	m_transport.Pump(&CPlatformNetworkManagerRelay::StaticReceiveCallback, this);

	if (m_bInSession && !m_bHost && m_transport.IsReady() && m_remotePlayer == NULL)
	{
		EnsureRemotePlayer();
	}

	if (m_bInSession && !m_bHost && m_transport.IsReady() && !m_bJoinStartNotified)
	{
		m_bJoinStartNotified = true;
		g_NetworkManager.StateChange_AnyToStarting();
	}
}

int CPlatformNetworkManagerRelay::GetPlayerCount()
{
	return (int)m_players.size();
}

int CPlatformNetworkManagerRelay::GetOnlinePlayerCount()
{
	return GetPlayerCount();
}

int CPlatformNetworkManagerRelay::GetLocalPlayerMask(int playerIndex)
{
	return 1 << playerIndex;
}

bool CPlatformNetworkManagerRelay::AddLocalPlayerByUserIndex(int userIndex)
{
	EnsureLocalPlayer();
	(void)userIndex;
	return true;
}

bool CPlatformNetworkManagerRelay::RemoveLocalPlayerByUserIndex(int userIndex)
{
	(void)userIndex;
	return true;
}

INetworkPlayer *CPlatformNetworkManagerRelay::GetLocalPlayerByUserIndex(int userIndex)
{
	if (m_localPlayer == NULL || m_localPlayer->GetUserIndex() != userIndex)
	{
		return NULL;
	}
	return m_localPlayer;
}

INetworkPlayer *CPlatformNetworkManagerRelay::GetPlayerByIndex(int playerIndex)
{
	if (playerIndex < 0 || playerIndex >= (int)m_players.size())
	{
		return NULL;
	}
	return m_players[playerIndex];
}

INetworkPlayer *CPlatformNetworkManagerRelay::GetPlayerByXuid(PlayerUID xuid)
{
	for (unsigned int i = 0; i < m_players.size(); ++i)
	{
		PlayerUID uid = m_players[i]->GetUID();
		if (memcmp(&uid, &xuid, sizeof(PlayerUID)) == 0)
		{
			return m_players[i];
		}
	}
	return NULL;
}

INetworkPlayer *CPlatformNetworkManagerRelay::GetPlayerBySmallId(unsigned char smallId)
{
	for (unsigned int i = 0; i < m_players.size(); ++i)
	{
		if (m_players[i]->GetSmallId() == smallId)
		{
			return m_players[i];
		}
	}
	return NULL;
}

bool CPlatformNetworkManagerRelay::ShouldMessageForFullSession()
{
	return false;
}

INetworkPlayer *CPlatformNetworkManagerRelay::GetHostPlayer()
{
	return m_bHost ? (INetworkPlayer *)m_localPlayer : (INetworkPlayer *)m_remotePlayer;
}

bool CPlatformNetworkManagerRelay::IsHost()
{
	return m_bHost;
}

bool CPlatformNetworkManagerRelay::JoinGameFromInviteInfo(int userIndex, int userMask, const INVITE_INFO *pInviteInfo)
{
	(void)pInviteInfo;
	return JoinGame(NULL, userMask, userIndex) == CGameNetworkManager::JOINGAME_SUCCESS;
}

bool CPlatformNetworkManagerRelay::LeaveGame(bool bMigrateHost)
{
	(void)bMigrateHost;
	m_bLeavingGame = true;
	m_bInGameplay = false;
	m_bInSession = false;
	m_bJoinStartNotified = false;
	m_transport.Close();
	return true;
}

bool CPlatformNetworkManagerRelay::_LeaveGame(bool bMigrateHost, bool bLeaveRoom)
{
	(void)bLeaveRoom;
	return LeaveGame(bMigrateHost);
}

bool CPlatformNetworkManagerRelay::IsInSession()
{
	return m_bInSession;
}

bool CPlatformNetworkManagerRelay::IsInGameplay()
{
	return m_bInGameplay;
}

bool CPlatformNetworkManagerRelay::IsReadyToPlayOrIdle()
{
	return !m_bInSession || m_bHost || m_transport.IsReady();
}

bool CPlatformNetworkManagerRelay::IsInStatsEnabledSession()
{
	return true;
}

bool CPlatformNetworkManagerRelay::SessionHasSpace(unsigned int spaceRequired)
{
	return GetPlayerCount() + (int)spaceRequired <= MINECRAFT_NET_MAX_PLAYERS;
}

void CPlatformNetworkManagerRelay::SendInviteGUI(int quadrant)
{
	(void)quadrant;
}

bool CPlatformNetworkManagerRelay::IsAddingPlayer()
{
	return false;
}

void CPlatformNetworkManagerRelay::HostGame(int localUsersMask, bool bOnlineGame, bool bIsPrivate, unsigned char publicSlots, unsigned char privateSlots)
{
	(void)localUsersMask;
	(void)publicSlots;
	(void)privateSlots;
	SetLocalGame(!bOnlineGame);
	SetPrivateGame(bIsPrivate);
	SystemFlagReset();
	m_bHost = true;
	m_bInSession = true;
	m_bLeavingGame = false;
	m_bRelayConnectAttempted = false;
	m_bJoinStartNotified = false;
	EnsureLocalPlayer();
	UpdateAndSetGameSessionData();
	ConnectRelay("host");
}

void CPlatformNetworkManagerRelay::_HostGame(int usersMask, unsigned char publicSlots, unsigned char privateSlots)
{
	(void)usersMask;
	(void)publicSlots;
	(void)privateSlots;
}

int CPlatformNetworkManagerRelay::JoinGame(FriendSessionInfo *searchResult, int localUsersMask, int primaryUserIndex)
{
	(void)localUsersMask;
	(void)primaryUserIndex;
	if (searchResult != NULL &&
		(searchResult->data.netVersion != MINECRAFT_NET_VERSION || !IsRelaySessionId(&searchResult->sessionId)))
	{
		return CGameNetworkManager::JOINGAME_FAIL_GENERAL;
	}
	m_bHost = false;
	m_bInSession = true;
	m_bLeavingGame = false;
	m_bRelayConnectAttempted = false;
	m_bJoinStartNotified = false;
	g_NetworkManager.StateChange_AnyToJoining();
	EnsureRemotePlayer();
	EnsureLocalPlayer();
	ConnectRelay("client");
	return CGameNetworkManager::JOINGAME_SUCCESS;
}

bool CPlatformNetworkManagerRelay::_StartGame()
{
	m_bInGameplay = true;
	return true;
}

bool CPlatformNetworkManagerRelay::SetLocalGame(bool isLocal)
{
	m_bIsOfflineGame = isLocal;
	return true;
}

void CPlatformNetworkManagerRelay::SetPrivateGame(bool isPrivate)
{
	m_bIsPrivateGame = isPrivate;
}

void CPlatformNetworkManagerRelay::RegisterPlayerChangedCallback(int iPad, void (*callback)(void *callbackParam, INetworkPlayer *pPlayer, bool leaving), void *callbackParam)
{
	playerChangedCallback[iPad] = callback;
	playerChangedCallbackParam[iPad] = callbackParam;
}

void CPlatformNetworkManagerRelay::UnRegisterPlayerChangedCallback(int iPad, void (*callback)(void *callbackParam, INetworkPlayer *pPlayer, bool leaving), void *callbackParam)
{
	(void)callback;
	if (playerChangedCallbackParam[iPad] == callbackParam)
	{
		playerChangedCallback[iPad] = NULL;
		playerChangedCallbackParam[iPad] = NULL;
	}
}

void CPlatformNetworkManagerRelay::HandleSignInChange()
{
}

bool CPlatformNetworkManagerRelay::_RunNetworkGame()
{
	m_bInGameplay = true;
	if (m_bHost)
	{
		for (unsigned int i = 0; i < m_players.size(); ++i)
		{
			NetworkPlayerRelay *player = m_players[i];
			if (!player->IsLocal() && player->GetSocket() != NULL && !player->IsSocketAdded())
			{
				Socket::addIncomingSocket(player->GetSocket());
				player->SetSocketAdded(true);
			}
		}
	}
	return true;
}

void CPlatformNetworkManagerRelay::UpdateAndSetGameSessionData(INetworkPlayer *pNetworkPlayerLeaving)
{
	(void)pNetworkPlayerLeaving;
	m_hostGameSessionData.netVersion = MINECRAFT_NET_VERSION;
}

bool CPlatformNetworkManagerRelay::RemoveLocalPlayer(INetworkPlayer *pNetworkPlayer)
{
	(void)pNetworkPlayer;
	return true;
}

CPlatformNetworkManagerRelay::PlayerFlags::PlayerFlags(INetworkPlayer *pNetworkPlayer, unsigned int flagCount)
{
	flagCount = (flagCount + 8 - 1) & ~(8 - 1);
	m_pNetworkPlayer = pNetworkPlayer;
	flags = new unsigned char[flagCount / 8];
	memset(flags, 0, flagCount / 8);
	count = flagCount;
}

CPlatformNetworkManagerRelay::PlayerFlags::~PlayerFlags()
{
	delete[] flags;
}

void CPlatformNetworkManagerRelay::SystemFlagAddPlayer(INetworkPlayer *pNetworkPlayer)
{
	PlayerFlags *newPlayerFlags = new PlayerFlags(pNetworkPlayer, m_flagIndexSize);
	for (unsigned int i = 0; i < m_playerFlags.size(); ++i)
	{
		if (pNetworkPlayer->IsSameSystem(m_playerFlags[i]->m_pNetworkPlayer))
		{
			memcpy(newPlayerFlags->flags, m_playerFlags[i]->flags, m_playerFlags[i]->count / 8);
			break;
		}
	}
	m_playerFlags.push_back(newPlayerFlags);
}

void CPlatformNetworkManagerRelay::SystemFlagRemovePlayer(INetworkPlayer *pNetworkPlayer)
{
	for (unsigned int i = 0; i < m_playerFlags.size(); ++i)
	{
		if (m_playerFlags[i]->m_pNetworkPlayer == pNetworkPlayer)
		{
			delete m_playerFlags[i];
			m_playerFlags[i] = m_playerFlags.back();
			m_playerFlags.pop_back();
			return;
		}
	}
}

void CPlatformNetworkManagerRelay::SystemFlagReset()
{
	for (unsigned int i = 0; i < m_playerFlags.size(); ++i)
	{
		delete m_playerFlags[i];
	}
	m_playerFlags.clear();
}

void CPlatformNetworkManagerRelay::SystemFlagSet(INetworkPlayer *pNetworkPlayer, int index)
{
	if (index < 0 || index >= m_flagIndexSize || pNetworkPlayer == NULL)
	{
		return;
	}

	for (unsigned int i = 0; i < m_playerFlags.size(); ++i)
	{
		if (pNetworkPlayer->IsSameSystem(m_playerFlags[i]->m_pNetworkPlayer))
		{
			m_playerFlags[i]->flags[index / 8] |= (128 >> (index % 8));
		}
	}
}

bool CPlatformNetworkManagerRelay::SystemFlagGet(INetworkPlayer *pNetworkPlayer, int index)
{
	if (index < 0 || index >= m_flagIndexSize || pNetworkPlayer == NULL)
	{
		return false;
	}

	for (unsigned int i = 0; i < m_playerFlags.size(); ++i)
	{
		if (m_playerFlags[i]->m_pNetworkPlayer == pNetworkPlayer)
		{
			return (m_playerFlags[i]->flags[index / 8] & (128 >> (index % 8))) != 0;
		}
	}
	return false;
}

std::wstring CPlatformNetworkManagerRelay::GatherStats()
{
	return m_transport.IsConnected() ? L"Relay connected" : L"Relay disconnected";
}

std::wstring CPlatformNetworkManagerRelay::GatherRTTStats()
{
	return L"Relay RTT: n/a";
}

void CPlatformNetworkManagerRelay::SetSessionTexturePackParentId(int id)
{
	m_hostGameSessionData.texturePackParentId = id;
}

void CPlatformNetworkManagerRelay::SetSessionSubTexturePackId(int id)
{
	m_hostGameSessionData.subTexturePackId = (unsigned char)id;
}

void CPlatformNetworkManagerRelay::Notify(int ID, ULONG_PTR Param)
{
	(void)ID;
	(void)Param;
}

std::vector<FriendSessionInfo *> *CPlatformNetworkManagerRelay::GetSessionList(int iPad, int localPlayers, bool partyOnly)
{
	(void)iPad;
	(void)partyOnly;
	std::vector<FriendSessionInfo *> *sessions = new std::vector<FriendSessionInfo *>();
	if (!m_bInSession && localPlayers > 0 && localPlayers <= MINECRAFT_NET_MAX_PLAYERS)
	{
		FriendSessionInfo *relaySession = new FriendSessionInfo();
		PopulateRelaySession(relaySession);
		sessions->push_back(relaySession);
	}
	return sessions;
}

bool CPlatformNetworkManagerRelay::GetGameSessionInfo(int iPad, SessionID sessionId, FriendSessionInfo *foundSession)
{
	(void)iPad;
	if (foundSession == NULL || !IsRelaySessionId(&sessionId))
	{
		return false;
	}

	if (foundSession->displayLabel != NULL)
	{
		delete foundSession->displayLabel;
		foundSession->displayLabel = NULL;
	}
	PopulateRelaySession(foundSession);
	return true;
}

void CPlatformNetworkManagerRelay::SetSessionsUpdatedCallback(void (*SessionsUpdatedCallback)(LPVOID pParam), LPVOID pSearchParam)
{
	m_SessionsUpdatedCallback = SessionsUpdatedCallback;
	m_pSearchParam = pSearchParam;
}

void CPlatformNetworkManagerRelay::GetFullFriendSessionInfo(FriendSessionInfo *foundSession, void (*FriendSessionUpdatedFn)(bool success, void *pParam), void *pParam)
{
	(void)foundSession;
	if (FriendSessionUpdatedFn != NULL)
	{
		FriendSessionUpdatedFn(true, pParam);
	}
}

void CPlatformNetworkManagerRelay::ForceFriendsSessionRefresh()
{
	if (m_SessionsUpdatedCallback != NULL)
	{
		m_SessionsUpdatedCallback(m_pSearchParam);
	}
}

void CPlatformNetworkManagerRelay::EnsureLocalPlayer()
{
	if (m_localPlayer != NULL)
	{
		return;
	}

	unsigned char smallId = m_bHost ? 1 : 2;
	m_localPlayer = new NetworkPlayerRelay(smallId, m_bHost ? L"Relay Host" : L"Relay Client", m_bHost, true, 0, &m_transport, 0, (int)m_players.size());
	m_players.push_back(m_localPlayer);
	NotifyPlayerJoined(m_localPlayer, !m_bHost, true);
}

void CPlatformNetworkManagerRelay::EnsureRemotePlayer()
{
	if (m_remotePlayer != NULL)
	{
		return;
	}

	unsigned char smallId = m_bHost ? 2 : 1;
	m_remotePlayer = new NetworkPlayerRelay(smallId, m_bHost ? L"Relay Client" : L"Relay Host", !m_bHost, false, 0, &m_transport, 0, (int)m_players.size());
	m_players.push_back(m_remotePlayer);
	NotifyPlayerJoined(m_remotePlayer, m_bHost, false);
}

NetworkPlayerRelay *CPlatformNetworkManagerRelay::FindRemotePlayer(unsigned int peerId)
{
	for (unsigned int i = 0; i < m_players.size(); ++i)
	{
		if (!m_players[i]->IsLocal() && m_players[i]->GetRelayPeerId() == peerId)
		{
			return m_players[i];
		}
	}
	return NULL;
}

NetworkPlayerRelay *CPlatformNetworkManagerRelay::EnsureRemotePlayer(unsigned int peerId)
{
	NetworkPlayerRelay *player = FindRemotePlayer(peerId);
	if (player != NULL || !m_bHost || peerId < 2 || peerId > 255)
	{
		return player;
	}

	player = new NetworkPlayerRelay((unsigned char)peerId, L"Relay Client", false, false, 0, &m_transport, peerId, (int)m_players.size());
	m_players.push_back(player);
	if (m_remotePlayer == NULL)
	{
		m_remotePlayer = player;
	}
	NotifyPlayerJoined(player, true, false);
	return player;
}

void CPlatformNetworkManagerRelay::RemoveRemotePlayer(unsigned int peerId)
{
	NetworkPlayerRelay *player = FindRemotePlayer(peerId);
	if (player == NULL)
	{
		return;
	}

	SystemFlagRemovePlayer(player);
	g_NetworkManager.PlayerLeaving(player);
	for (int i = 0; i < XUSER_MAX_COUNT; ++i)
	{
		if (playerChangedCallback[i] != NULL)
		{
			playerChangedCallback[i](playerChangedCallbackParam[i], player, true);
		}
	}

	for (std::vector<NetworkPlayerRelay *>::iterator it = m_players.begin(); it != m_players.end(); ++it)
	{
		if (*it == player)
		{
			m_players.erase(it);
			break;
		}
	}
	for (unsigned int i = 0; i < m_players.size(); ++i)
	{
		m_players[i]->SetSessionIndex((int)i);
	}
	if (m_remotePlayer == player)
	{
		m_remotePlayer = NULL;
		for (unsigned int i = 0; i < m_players.size(); ++i)
		{
			if (!m_players[i]->IsLocal())
			{
				m_remotePlayer = m_players[i];
				break;
			}
		}
	}
	if (player->GetSocket() != NULL)
	{
		player->GetSocket()->close(false);
	}
	player->SetJoined(false);
	m_retiredPlayers.push_back(player);
}

void CPlatformNetworkManagerRelay::NotifyPlayerJoined(NetworkPlayerRelay *networkPlayer, bool createSocket, bool localPlayer)
{
	if (networkPlayer == NULL || networkPlayer->HasJoined())
	{
		return;
	}

	networkPlayer->SetJoined(true);
	g_NetworkManager.PlayerJoining(networkPlayer);

	if (createSocket)
	{
		g_NetworkManager.CreateSocket(networkPlayer, localPlayer);
		if (m_bHost && !networkPlayer->IsLocal() && g_NetworkManager.IsInGameplay())
		{
			// CreateSocket registers late joiners immediately. Remember that so
			// _RunNetworkGame does not attach a second reader to the same stream.
			networkPlayer->SetSocketAdded(true);
		}
	}

	SystemFlagAddPlayer(networkPlayer);

	for (int i = 0; i < XUSER_MAX_COUNT; ++i)
	{
		if (playerChangedCallback[i] != NULL)
		{
			playerChangedCallback[i](playerChangedCallbackParam[i], networkPlayer, false);
		}
	}
}

void CPlatformNetworkManagerRelay::ConnectRelay(const char *roleName)
{
	m_bRelayConnectAttempted = true;
	if (m_transport.IsConnected())
	{
		return;
	}

	if (!m_transport.ConnectFromEnvironment(roleName))
	{
		app.DebugPrintf("Legacy relay connection failed for %s\n", roleName);
	}
}

void CPlatformNetworkManagerRelay::HandleRelayEvent(RelayTransport::ReceiveEvent event, unsigned int peerId, const unsigned char *data, int dataSize)
{
	if (event == RelayTransport::PEER_JOINED)
	{
		EnsureRemotePlayer(peerId);
		return;
	}
	if (event == RelayTransport::PEER_LEFT)
	{
		RemoveRemotePlayer(peerId);
		return;
	}

	Socket *socket = NULL;
	bool fromHost = true;

	if (m_bHost)
	{
		NetworkPlayerRelay *remotePlayer = EnsureRemotePlayer(peerId);
		socket = remotePlayer != NULL ? remotePlayer->GetSocket() : NULL;
		fromHost = false;
	}
	else
	{
		EnsureLocalPlayer();
		socket = m_localPlayer != NULL ? m_localPlayer->GetSocket() : NULL;
		fromHost = true;
	}

	if (socket != NULL)
	{
		socket->pushDataToQueue(data, dataSize, fromHost);
	}
}

void CPlatformNetworkManagerRelay::StaticReceiveCallback(RelayTransport::ReceiveEvent event, unsigned int peerId, const unsigned char *data, int dataSize, void *context)
{
	CPlatformNetworkManagerRelay *manager = (CPlatformNetworkManagerRelay *)context;
	if (manager != NULL)
	{
		manager->HandleRelayEvent(event, peerId, data, dataSize);
	}
}
