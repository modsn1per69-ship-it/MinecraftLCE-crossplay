#pragma once

#include <stdint.h>

class RelayTransport
{
public:
	enum ReceiveEvent
	{
		RECEIVE_DATA,
		PEER_JOINED,
		PEER_LEFT
	};

	typedef void (*ReceiveCallback)(ReceiveEvent event, unsigned int peerId, const unsigned char *data, int dataSize, void *context);

	RelayTransport();
	~RelayTransport();

	bool ConnectFromEnvironment(const char *roleName);
	bool Connect(const char *host, unsigned short port);
	void Close();
	bool IsConnected() const { return m_connected; }
	bool IsReady() const { return m_ready; }

	bool Send(const void *data, int dataSize, unsigned int peerId = 0);
	bool Pump(ReceiveCallback callback, void *context);

private:
	bool SendLine(const char *line);
	bool SendBytes(const void *data, int dataSize);
	bool ReceiveLine(char *line, int lineCapacity, int timeoutMs);
	bool SetNonBlocking();
	bool ProcessReceived(const unsigned char *data, int dataSize, ReceiveCallback callback, void *context);

	uintptr_t m_socket;
	bool m_connected;
	bool m_ready;
	bool m_localHandshake;
	bool m_framedHost;
	bool m_networkStarted;
	void *m_networkMemory;
	char m_handshakeLine[256];
	int m_handshakeLineLength;
	unsigned char m_frameHeader[9];
	int m_frameHeaderLength;
	unsigned int m_framePeerId;
	unsigned int m_framePayloadRemaining;
	unsigned char m_frameType;
	CRITICAL_SECTION m_sendLock;
};
