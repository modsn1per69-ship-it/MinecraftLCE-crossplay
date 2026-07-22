#include "stdafx.h"
#include "NetworkPlayerRelay.h"
#include "RelayTransport.h"

#include <string.h>

NetworkPlayerRelay::NetworkPlayerRelay(unsigned char smallId, const wchar_t *name, bool host, bool local, int userIndex, RelayTransport *transport, unsigned int relayPeerId, int sessionIndex)
{
	m_smallId = smallId;
	m_name = name != NULL ? name : L"Console Legacy Player";
	m_host = host;
	m_local = local;
	m_joined = false;
	m_userIndex = userIndex;
#if defined(__PS3__)
	m_uid = PlayerUID();
	m_uid.setUserID(0x4c430000u | smallId);
	m_uid.setCurrentMacAddress();
#else
	m_uid = (PlayerUID)(0x4c43450000000000ULL | (unsigned long long)smallId);
#endif
	m_pSocket = NULL;
	m_transport = transport;
	m_relayPeerId = relayPeerId;
	m_sessionIndex = sessionIndex >= 0 ? sessionIndex : smallId - 1;
	m_socketAdded = false;
}

unsigned char NetworkPlayerRelay::GetSmallId()
{
	return m_smallId;
}

void NetworkPlayerRelay::SendData(INetworkPlayer *player, const void *pvData, int dataSize, bool lowPriority)
{
	(void)lowPriority;

	if (m_transport != NULL)
	{
		// SendData is invoked on the sender and receives the destination player.
		// Hosts therefore need the destination's relay id, not the local id.
		NetworkPlayerRelay *destination = (NetworkPlayerRelay *)player;
		unsigned int peerId = destination != NULL ? destination->m_relayPeerId : m_relayPeerId;
		m_transport->Send(pvData, dataSize, peerId);
	}
}

bool NetworkPlayerRelay::IsSameSystem(INetworkPlayer *player)
{
	return player == this;
}

int NetworkPlayerRelay::GetSendQueueSizeBytes(INetworkPlayer *player, bool lowPriority)
{
	(void)player;
	(void)lowPriority;
	return 0;
}

int NetworkPlayerRelay::GetSendQueueSizeMessages(INetworkPlayer *player, bool lowPriority)
{
	(void)player;
	(void)lowPriority;
	return 0;
}

int NetworkPlayerRelay::GetCurrentRtt()
{
	return 0;
}

bool NetworkPlayerRelay::IsHost()
{
	return m_host;
}

bool NetworkPlayerRelay::IsGuest()
{
	return !m_host;
}

bool NetworkPlayerRelay::IsLocal()
{
	return m_local;
}

int NetworkPlayerRelay::GetSessionIndex()
{
	return m_sessionIndex;
}

bool NetworkPlayerRelay::IsTalking()
{
	return false;
}

bool NetworkPlayerRelay::IsMutedByLocalUser(int userIndex)
{
	(void)userIndex;
	return false;
}

bool NetworkPlayerRelay::HasVoice()
{
	return false;
}

bool NetworkPlayerRelay::HasCamera()
{
	return false;
}

int NetworkPlayerRelay::GetUserIndex()
{
	return m_userIndex;
}

void NetworkPlayerRelay::SetSocket(Socket *pSocket)
{
	m_pSocket = pSocket;
}

Socket *NetworkPlayerRelay::GetSocket()
{
	return m_pSocket;
}

const wchar_t *NetworkPlayerRelay::GetOnlineName()
{
	return m_name.c_str();
}

std::wstring NetworkPlayerRelay::GetDisplayName()
{
	return m_name;
}

PlayerUID NetworkPlayerRelay::GetUID()
{
	return m_uid;
}
