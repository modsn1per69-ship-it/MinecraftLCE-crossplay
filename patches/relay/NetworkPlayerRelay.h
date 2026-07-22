#pragma once

#include <string>
#include "..\NetworkPlayerInterface.h"

class RelayTransport;

class NetworkPlayerRelay : public INetworkPlayer
{
public:
	NetworkPlayerRelay(unsigned char smallId, const wchar_t *name, bool host, bool local, int userIndex, RelayTransport *transport, unsigned int relayPeerId = 0, int sessionIndex = -1);

	virtual unsigned char GetSmallId();
	virtual void SendData(INetworkPlayer *player, const void *pvData, int dataSize, bool lowPriority);
	virtual bool IsSameSystem(INetworkPlayer *player);
	virtual int GetSendQueueSizeBytes(INetworkPlayer *player, bool lowPriority);
	virtual int GetSendQueueSizeMessages(INetworkPlayer *player, bool lowPriority);
	virtual int GetCurrentRtt();
	virtual bool IsHost();
	virtual bool IsGuest();
	virtual bool IsLocal();
	virtual int GetSessionIndex();
	virtual bool IsTalking();
	virtual bool IsMutedByLocalUser(int userIndex);
	virtual bool HasVoice();
	virtual bool HasCamera();
	virtual int GetUserIndex();
	virtual void SetSocket(Socket *pSocket);
	virtual Socket *GetSocket();
	virtual const wchar_t *GetOnlineName();
	virtual std::wstring GetDisplayName();
	virtual PlayerUID GetUID();

	void SetJoined(bool joined) { m_joined = joined; }
	bool HasJoined() const { return m_joined; }
	void SetUID(PlayerUID uid) { m_uid = uid; }
	unsigned int GetRelayPeerId() const { return m_relayPeerId; }
	void SetSessionIndex(int sessionIndex) { m_sessionIndex = sessionIndex; }
	void SetSocketAdded(bool added) { m_socketAdded = added; }
	bool IsSocketAdded() const { return m_socketAdded; }

private:
	unsigned char m_smallId;
	std::wstring m_name;
	bool m_host;
	bool m_local;
	bool m_joined;
	int m_userIndex;
	PlayerUID m_uid;
	Socket *m_pSocket;
	RelayTransport *m_transport;
	unsigned int m_relayPeerId;
	int m_sessionIndex;
	bool m_socketAdded;
};
