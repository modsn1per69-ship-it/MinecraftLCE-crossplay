#include "stdafx.h"
#include "RelayTransport.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#if defined(_XBOX)
#include <winsockx.h>
#elif defined(_WIN32)
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "Ws2_32.lib")
#elif defined(__PS3__)
#if defined(CONSOLE_LEGACY_PSL1GHT)
#include <net/net.h>
#include <arpa/inet.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>
#else
#include <netex/net.h>
#include <arpa/inet.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <sys/select.h>
#include <sys/socket.h>
#include <sys/time.h>
#endif
#ifndef _TRUNCATE
#define _TRUNCATE ((size_t)-1)
#endif
#define _snprintf_s(buffer, size, count, format, ...) snprintf(buffer, size, format, __VA_ARGS__)
#elif defined(__PSVITA__) || defined(__ORBIS__)
#include <net.h>
#ifndef _TRUNCATE
#define _TRUNCATE ((size_t)-1)
#endif
#define _snprintf_s(buffer, size, count, format, ...) snprintf(buffer, size, format, __VA_ARGS__)
#endif

static const unsigned short RELAY_DEFAULT_PORT = 61000;
static const unsigned int PS3_NETWORK_MEMORY_SIZE = 0x20000;

#if defined(__PS3__)
static void ConfigureRelaySocket(uintptr_t socketValue)
{
	int enabled = 1;
	int bufferSize = 256 * 1024;
	int socketHandle = (int)socketValue;
	setsockopt(socketHandle, IPPROTO_TCP, TCP_NODELAY, &enabled, sizeof(enabled));
	setsockopt(socketHandle, SOL_SOCKET, SO_RCVBUF, &bufferSize, sizeof(bufferSize));
	setsockopt(socketHandle, SOL_SOCKET, SO_SNDBUF, &bufferSize, sizeof(bufferSize));
}
#endif

#if defined(__PS3__)
static bool ParseRelayIpv4(const char *host, in_addr *address)
{
	unsigned int a = 0;
	unsigned int b = 0;
	unsigned int c = 0;
	unsigned int d = 0;
	char tail = 0;
	if (host == NULL || address == NULL || sscanf(host, "%u.%u.%u.%u%c", &a, &b, &c, &d, &tail) != 4 ||
		a > 255 || b > 255 || c > 255 || d > 255)
	{
		return false;
	}
	address->s_addr = htonl((a << 24) | (b << 16) | (c << 8) | d);
	return true;
}
#endif

#if defined(__PSVITA__) || defined(__ORBIS__)
static bool IsSceWouldBlock(int result)
{
	int *error = sceNetErrnoLoc();
	return (unsigned int)result == (unsigned int)SCE_NET_ERROR_EAGAIN ||
		(error != NULL && (*error == SCE_NET_EAGAIN ||
			(unsigned int)*error == (unsigned int)SCE_NET_ERROR_EAGAIN));
}
#endif

RelayTransport::RelayTransport()
{
	InitializeCriticalSection(&m_sendLock);
	#if defined(_WIN32) || defined(_XBOX)
	m_socket = (uintptr_t)INVALID_SOCKET;
	#elif defined(__PS3__) || defined(__PSVITA__) || defined(__ORBIS__)
	m_socket = (uintptr_t)-1;
#else
	m_socket = 0;
#endif
	m_connected = false;
	m_ready = false;
	m_localHandshake = false;
	m_framedHost = false;
	m_networkStarted = false;
	m_networkMemory = NULL;
	m_handshakeLine[0] = 0;
	m_handshakeLineLength = 0;
	m_frameHeaderLength = 0;
	m_framePeerId = 0;
	m_framePayloadRemaining = 0;
	m_frameType = 0;
}

RelayTransport::~RelayTransport()
{
	Close();
	DeleteCriticalSection(&m_sendLock);
}

bool RelayTransport::ConnectFromEnvironment(const char *roleName)
{
#if defined(_XBOX) || defined(__PS3__) || defined(__PSVITA__) || defined(__ORBIS__)
#ifndef CONSOLE_LEGACY_RELAY_HOST_DEFAULT
#if defined(__PS3__)
#define CONSOLE_LEGACY_RELAY_HOST_DEFAULT "127.0.0.1"
#else
#define CONSOLE_LEGACY_RELAY_HOST_DEFAULT "127.0.0.1"
#endif
#endif
#ifndef CONSOLE_LEGACY_RELAY_ADDR_DEFAULT
#if defined(__PS3__)
#define CONSOLE_LEGACY_RELAY_ADDR_DEFAULT "127.0.0.1:61000"
#else
#define CONSOLE_LEGACY_RELAY_ADDR_DEFAULT "127.0.0.1:61000"
#endif
#endif
#ifndef CONSOLE_LEGACY_RELAY_MODE_DEFAULT
#define CONSOLE_LEGACY_RELAY_MODE_DEFAULT "local"
#endif
#ifndef CONSOLE_LEGACY_RELAY_SESSION_DEFAULT
#define CONSOLE_LEGACY_RELAY_SESSION_DEFAULT "local-test"
#endif
#ifndef CONSOLE_LEGACY_RELAY_BUILD_DEFAULT
#define CONSOLE_LEGACY_RELAY_BUILD_DEFAULT "584111F7-1.0.10.0-lce1.2.3-net495-proto39"
#endif
#ifndef CONSOLE_LEGACY_RELAY_TOKEN_DEFAULT
#define CONSOLE_LEGACY_RELAY_TOKEN_DEFAULT ""
#endif
	const char *host = CONSOLE_LEGACY_RELAY_HOST_DEFAULT;
	const char *portText = NULL;
	const char *addr = CONSOLE_LEGACY_RELAY_ADDR_DEFAULT;
	const char *mode = CONSOLE_LEGACY_RELAY_MODE_DEFAULT;
	const char *session = CONSOLE_LEGACY_RELAY_SESSION_DEFAULT;
	const char *build = CONSOLE_LEGACY_RELAY_BUILD_DEFAULT;
	const char *token = CONSOLE_LEGACY_RELAY_TOKEN_DEFAULT;
#else
	const char *host = getenv("CONSOLE_LEGACY_RELAY_HOST");
	const char *portText = getenv("CONSOLE_LEGACY_RELAY_PORT");
	const char *addr = getenv("CONSOLE_LEGACY_RELAY_ADDR");
	const char *mode = getenv("CONSOLE_LEGACY_RELAY_MODE");
	const char *session = getenv("CONSOLE_LEGACY_RELAY_SESSION");
	const char *build = getenv("CONSOLE_LEGACY_RELAY_BUILD_ID");
	const char *token = getenv("CONSOLE_LEGACY_RELAY_TOKEN");
#endif
	char hostBuffer[256];
	char line[512];
	unsigned short port = RELAY_DEFAULT_PORT;

	if (host == NULL || host[0] == 0)
	{
		host = "127.0.0.1";
	}
	if (mode == NULL || mode[0] == 0)
	{
		mode = "local";
	}

	if (portText != NULL && portText[0] != 0)
	{
		int parsedPort = atoi(portText);
		if (parsedPort > 0 && parsedPort <= 65535)
		{
			port = (unsigned short)parsedPort;
		}
	}

	if (addr != NULL && addr[0] != 0)
	{
		const char *colon = strrchr(addr, ':');
		if (colon != NULL && colon != addr)
		{
			size_t hostLen = (size_t)(colon - addr);
			if (hostLen >= sizeof(hostBuffer))
			{
				hostLen = sizeof(hostBuffer) - 1;
			}
			memcpy(hostBuffer, addr, hostLen);
			hostBuffer[hostLen] = 0;
			host = hostBuffer;

			int parsedPort = atoi(colon + 1);
			if (parsedPort > 0 && parsedPort <= 65535)
			{
				port = (unsigned short)parsedPort;
			}
		}
		else
		{
			host = addr;
		}
	}

	if (!Connect(host, port))
	{
		return false;
	}

	const bool isHost = roleName != NULL && strcmp(roleName, "host") == 0;
	const char *buildId = build != NULL && build[0] != 0 ? build : "584111F7-1.0.10.0-lce1.2.3-net495-proto39";

	if (mode != NULL && strcmp(mode, "local") == 0)
	{
		const char *sessionId = session != NULL && session[0] != 0 ? session : "local-test";
		const char *accessToken = token != NULL ? token : "";
#if (defined(__PS3__) && defined(CONSOLE_LEGACY_PSL1GHT)) || defined(__PSVITA__) || defined(__ORBIS__)
		if (accessToken[0] != 0)
		{
			snprintf(line, sizeof(line), "%s %s %s V2 %s\n", isHost ? "HOST" : "JOIN", sessionId, buildId, accessToken);
		}
		else
		{
			snprintf(line, sizeof(line), "%s %s %s V2\n", isHost ? "HOST" : "JOIN", sessionId, buildId);
		}
#else
		if (accessToken[0] != 0)
		{
			_snprintf_s(line, sizeof(line), _TRUNCATE, "%s %s %s V2 %s\n", isHost ? "HOST" : "JOIN", sessionId, buildId, accessToken);
		}
		else
		{
			_snprintf_s(line, sizeof(line), _TRUNCATE, "%s %s %s V2\n", isHost ? "HOST" : "JOIN", sessionId, buildId);
		}
#endif
		line[sizeof(line) - 1] = 0;
		if (!SendLine(line) || !SetNonBlocking())
		{
			Close();
			return false;
		}
		m_localHandshake = true;
		m_framedHost = isHost;
		m_ready = false;
		return true;
	}

	const char *authToken = token != NULL ? token : "";
	if (isHost)
	{
		_snprintf_s(line, sizeof(line), _TRUNCATE, "HOST %s %s\n", authToken, buildId);
		line[sizeof(line) - 1] = 0;
		if (!SendLine(line) || !ReceiveLine(line, sizeof(line), 30000))
		{
			Close();
			return false;
		}

		char clientId[128];
		memset(clientId, 0, sizeof(clientId));
		if (sscanf(line, "CLIENT %127s", clientId) != 1)
		{
			Close();
			return false;
		}

		Close();
		if (!Connect(host, port))
		{
			return false;
		}

		_snprintf_s(line, sizeof(line), _TRUNCATE, "ACCEPT %s %s %s\n", authToken, buildId, clientId);
		line[sizeof(line) - 1] = 0;
		if (!SendLine(line) || !SetNonBlocking())
		{
			Close();
			return false;
		}
		m_ready = true;
		return true;
	}

	const char *sessionId = session != NULL && session[0] != 0 ? session : "";
	_snprintf_s(line, sizeof(line), _TRUNCATE, "JOIN %s %s %s\n", authToken, buildId, sessionId);
	line[sizeof(line) - 1] = 0;
	if (!SendLine(line) || !SetNonBlocking())
	{
		Close();
		return false;
	}
	m_ready = true;
	return true;
}

bool RelayTransport::Connect(const char *host, unsigned short port)
{
#if defined(_WIN32) || defined(_XBOX)
#if defined(_XBOX)
	if (!m_networkStarted)
	{
		if (XNetStartup(NULL) != 0)
		{
			return false;
		}
		m_networkStarted = true;
	}
#endif
	WSADATA wsaData;
#if defined(_WIN32)
	if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0)
	{
		return false;
	}
#endif

	SOCKET s = INVALID_SOCKET;
#if defined(_XBOX)
	sockaddr_in address;
	memset(&address, 0, sizeof(address));
	address.sin_family = AF_INET;
	address.sin_port = htons(port);
	address.sin_addr.s_addr = inet_addr(host);
	if (address.sin_addr.s_addr == INADDR_NONE)
	{
		return false;
	}

	s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (s == INVALID_SOCKET || connect(s, (sockaddr *)&address, sizeof(address)) == SOCKET_ERROR)
	{
		if (s != INVALID_SOCKET)
		{
			closesocket(s);
		}
		return false;
	}
#else
	char portBuffer[16];
	_snprintf_s(portBuffer, sizeof(portBuffer), _TRUNCATE, "%hu", port);
	portBuffer[sizeof(portBuffer) - 1] = 0;

	addrinfo hints;
	memset(&hints, 0, sizeof(hints));
	hints.ai_family = AF_INET;
	hints.ai_socktype = SOCK_STREAM;
	hints.ai_protocol = IPPROTO_TCP;

	addrinfo *result = NULL;
	if (getaddrinfo(host, portBuffer, &hints, &result) != 0)
	{
		return false;
	}

	for (addrinfo *ptr = result; ptr != NULL; ptr = ptr->ai_next)
	{
		s = socket(ptr->ai_family, ptr->ai_socktype, ptr->ai_protocol);
		if (s == INVALID_SOCKET)
		{
			continue;
		}

		if (connect(s, ptr->ai_addr, (int)ptr->ai_addrlen) == SOCKET_ERROR)
		{
			closesocket(s);
			s = INVALID_SOCKET;
			continue;
		}

		break;
	}

	freeaddrinfo(result);

	if (s == INVALID_SOCKET)
	{
		return false;
	}
#endif

	m_socket = (uintptr_t)s;
	m_connected = true;
	m_ready = false;
	m_localHandshake = false;
	m_handshakeLineLength = 0;
	m_frameHeaderLength = 0;
	m_framePayloadRemaining = 0;
	return true;
#elif defined(__PS3__)
#if defined(CONSOLE_LEGACY_PSL1GHT)
	if (!m_networkStarted)
	{
		netInitParam params;
		memset(&params, 0, sizeof(params));
		m_networkMemory = malloc(PS3_NETWORK_MEMORY_SIZE);
		if (m_networkMemory == NULL)
		{
			return false;
		}
		params.memory = (u32)(u64)m_networkMemory;
		params.memory_size = PS3_NETWORK_MEMORY_SIZE;
		if (netInitializeNetworkEx(&params) < 0)
		{
			free(m_networkMemory);
			m_networkMemory = NULL;
			return false;
		}
		m_networkStarted = true;
	}

	int s = sysNetSocket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (s < 0)
	{
		Close();
		return false;
	}

	sockaddr_in address;
	memset(&address, 0, sizeof(address));
	address.sin_len = sizeof(address);
	address.sin_family = AF_INET;
	address.sin_port = htons(port);
	if (!ParseRelayIpv4(host, &address.sin_addr) ||
		sysNetConnect(s, (sockaddr *)&address, sizeof(address)) != 0)
	{
		sysNetClose(s);
		Close();
		return false;
	}

	m_socket = (uintptr_t)s;
	ConfigureRelaySocket(m_socket);
	m_connected = true;
	m_ready = false;
	m_localHandshake = false;
	m_handshakeLineLength = 0;
	m_frameHeaderLength = 0;
	m_framePayloadRemaining = 0;
	return true;
#else
	if (!m_networkStarted)
	{
		if (sys_net_initialize_network() < 0)
		{
			return false;
		}
		m_networkStarted = true;
	}

	int s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (s < 0)
	{
		Close();
		return false;
	}

	sockaddr_in address;
	memset(&address, 0, sizeof(address));
	address.sin_len = sizeof(address);
	address.sin_family = AF_INET;
	address.sin_port = htons(port);
	if (!ParseRelayIpv4(host, &address.sin_addr) ||
		connect(s, (sockaddr *)&address, sizeof(address)) != 0)
	{
		socketclose(s);
		Close();
		return false;
	}

	m_socket = (uintptr_t)s;
	ConfigureRelaySocket(m_socket);
	m_connected = true;
	m_ready = false;
	m_localHandshake = false;
	m_handshakeLineLength = 0;
	m_frameHeaderLength = 0;
	m_framePayloadRemaining = 0;
	return true;
#endif
#elif defined(__PSVITA__) || defined(__ORBIS__)
	int s = sceNetSocket("LegacyRelay", SCE_NET_AF_INET, SCE_NET_SOCK_STREAM, 0);
	if (s < 0)
	{
		return false;
	}

	SceNetSockaddrIn address;
	memset(&address, 0, sizeof(address));
	address.sin_len = sizeof(address);
	address.sin_family = SCE_NET_AF_INET;
	address.sin_port = sceNetHtons(port);
	if (sceNetInetPton(SCE_NET_AF_INET, host, &address.sin_addr) != 1 ||
		sceNetConnect(s, (SceNetSockaddr *)&address, sizeof(address)) < 0)
	{
		sceNetSocketClose(s);
		return false;
	}

	m_socket = (uintptr_t)s;
	m_connected = true;
	m_ready = false;
	m_localHandshake = false;
	m_handshakeLineLength = 0;
	m_frameHeaderLength = 0;
	m_framePayloadRemaining = 0;
	return true;
#else
	(void)host;
	(void)port;
	return false;
#endif
}

bool RelayTransport::SendBytes(const void *data, int dataSize)
{
#if defined(_WIN32) || defined(_XBOX) || defined(__PS3__) || defined(__PSVITA__) || defined(__ORBIS__)
	if (!m_connected || data == NULL || dataSize <= 0)
	{
		return false;
	}
	const char *cursor = (const char *)data;
	int remaining = dataSize;
#if defined(_WIN32) || defined(_XBOX)
	SOCKET s = (SOCKET)m_socket;
#endif
	while (remaining > 0)
	{
#if defined(_WIN32) || defined(_XBOX)
		int sent = send(s, cursor, remaining, 0);
		if (sent == SOCKET_ERROR)
		{
			int error = WSAGetLastError();
			if (error == WSAEWOULDBLOCK)
			{
				Sleep(1);
				continue;
			}
			return false;
		}
#elif defined(CONSOLE_LEGACY_PSL1GHT)
		int sent = sysNetSendto((int)m_socket, cursor, remaining, 0, NULL, 0);
#elif defined(__PSVITA__) || defined(__ORBIS__)
		int sent = sceNetSend((int)m_socket, cursor, (unsigned int)remaining, 0);
		if (sent < 0 && IsSceWouldBlock(sent))
		{
			Sleep(1);
			continue;
		}
#else
		int sent = send((int)m_socket, cursor, remaining, 0);
#endif
		if (sent <= 0)
		{
			return false;
		}
		cursor += sent;
		remaining -= sent;
	}
	return true;
#else
	(void)data;
	(void)dataSize;
	return false;
#endif
}

bool RelayTransport::SendLine(const char *line)
{
	return line != NULL && SendBytes(line, (int)strlen(line));
}

bool RelayTransport::ReceiveLine(char *line, int lineCapacity, int timeoutMs)
{
#if defined(_WIN32) || defined(_XBOX)
	if (!m_connected || line == NULL || lineCapacity < 2)
	{
		return false;
	}

	SOCKET s = (SOCKET)m_socket;
	int length = 0;
	int waitedMs = 0;
	while (length < lineCapacity - 1 && waitedMs < timeoutMs)
	{
		fd_set readSet;
		FD_ZERO(&readSet);
		FD_SET(s, &readSet);
		timeval waitTime;
		waitTime.tv_sec = 0;
		waitTime.tv_usec = 100000;
#if defined(_WIN32)
		int ready = select(0, &readSet, NULL, NULL, &waitTime);
#else
		int ready = select((int)s + 1, &readSet, NULL, NULL, &waitTime);
#endif
		if (ready == SOCKET_ERROR)
		{
			return false;
		}
		if (ready == 0)
		{
			waitedMs += 100;
			continue;
		}

		char byte = 0;
		int received = recv(s, &byte, 1, 0);
		if (received <= 0)
		{
			return false;
		}
		if (byte == '\n')
		{
			break;
		}
		if (byte != '\r')
		{
			line[length++] = byte;
		}
	}

	line[length] = 0;
	return length > 0 && length < lineCapacity - 1;
#elif defined(__PS3__)
	if (!m_connected || line == NULL || lineCapacity < 2)
	{
		return false;
	}
	int length = 0;
	int waitedMs = 0;
	while (length < lineCapacity - 1 && waitedMs < timeoutMs)
	{
		fd_set readSet;
		FD_ZERO(&readSet);
		FD_SET((int)m_socket, &readSet);
		timeval waitTime;
		waitTime.tv_sec = 0;
		waitTime.tv_usec = 100000;
#if defined(CONSOLE_LEGACY_PSL1GHT)
		int ready = sysNetSelect((int)m_socket + 1, &readSet, NULL, NULL, &waitTime);
#else
		int ready = socketselect((int)m_socket + 1, &readSet, NULL, NULL, &waitTime);
#endif
		if (ready < 0)
		{
			return false;
		}
		if (ready == 0)
		{
			waitedMs += 100;
			continue;
		}
		char value = 0;
#if defined(CONSOLE_LEGACY_PSL1GHT)
		int received = sysNetRecvfrom((int)m_socket, &value, 1, 0, NULL, NULL);
#else
		int received = recv((int)m_socket, &value, 1, 0);
#endif
		if (received <= 0)
		{
			return false;
		}
		if (value == '\n')
		{
			break;
		}
		if (value != '\r')
		{
			line[length++] = value;
		}
	}
	line[length] = 0;
	return length > 0 && length < lineCapacity - 1;
#elif defined(__PSVITA__) || defined(__ORBIS__)
	if (!m_connected || line == NULL || lineCapacity < 2)
	{
		return false;
	}
	int length = 0;
	int waitedMs = 0;
	while (length < lineCapacity - 1 && waitedMs < timeoutMs)
	{
		char value = 0;
		int received = sceNetRecv((int)m_socket, &value, 1, SCE_NET_MSG_DONTWAIT);
		if (received < 0 && IsSceWouldBlock(received))
		{
			Sleep(1);
			++waitedMs;
			continue;
		}
		if (received <= 0)
		{
			return false;
		}
		if (value == '\n')
		{
			break;
		}
		if (value != '\r')
		{
			line[length++] = value;
		}
	}
	line[length] = 0;
	return length > 0 && length < lineCapacity - 1;
#else
	(void)line;
	(void)lineCapacity;
	(void)timeoutMs;
	return false;
#endif
}

bool RelayTransport::SetNonBlocking()
{
#if defined(_WIN32) || defined(_XBOX)
	if (!m_connected)
	{
		return false;
	}
	u_long nonBlocking = 1;
	return ioctlsocket((SOCKET)m_socket, FIONBIO, &nonBlocking) == 0;
#elif defined(__PS3__)
	// PS3 reads are guarded by select, so the socket itself can remain blocking.
	return m_connected;
#elif defined(__PSVITA__) || defined(__ORBIS__)
	if (!m_connected)
	{
		return false;
	}
	int enabled = 1;
	return sceNetSetsockopt((int)m_socket, SCE_NET_SOL_SOCKET, SCE_NET_SO_NBIO,
		&enabled, sizeof(enabled)) == 0;
#else
	return false;
#endif
}

void RelayTransport::Close()
{
#if defined(_WIN32) || defined(_XBOX)
	SOCKET s = (SOCKET)m_socket;
	if (s != INVALID_SOCKET)
	{
		closesocket(s);
		m_socket = (uintptr_t)INVALID_SOCKET;
#if defined(_WIN32)
		WSACleanup();
#endif
	}
#if defined(_XBOX)
	if (m_networkStarted)
	{
		XNetCleanup();
		m_networkStarted = false;
	}
#endif
#endif
#if defined(__PS3__)
	if ((int)m_socket >= 0)
	{
#if defined(CONSOLE_LEGACY_PSL1GHT)
		sysNetShutdown((int)m_socket, SHUT_RDWR);
		sysNetClose((int)m_socket);
#else
		shutdown((int)m_socket, SHUT_RDWR);
		socketclose((int)m_socket);
#endif
		m_socket = (uintptr_t)-1;
	}
	if (m_networkStarted)
	{
#if defined(CONSOLE_LEGACY_PSL1GHT)
		netFinalizeNetwork();
#else
		sys_net_finalize_network();
#endif
		m_networkStarted = false;
	}
#if defined(CONSOLE_LEGACY_PSL1GHT)
	if (m_networkMemory != NULL)
	{
		free(m_networkMemory);
		m_networkMemory = NULL;
	}
#endif
#endif
#if defined(__PSVITA__) || defined(__ORBIS__)
	if ((int)m_socket >= 0)
	{
		sceNetShutdown((int)m_socket, SCE_NET_SHUT_RDWR);
		sceNetSocketClose((int)m_socket);
		m_socket = (uintptr_t)-1;
	}
#endif
	m_connected = false;
	m_ready = false;
	m_localHandshake = false;
	m_framedHost = false;
	m_handshakeLineLength = 0;
	m_frameHeaderLength = 0;
	m_framePayloadRemaining = 0;
}

bool RelayTransport::Send(const void *data, int dataSize, unsigned int peerId)
{
#if defined(_WIN32) || defined(_XBOX) || defined(__PS3__) || defined(__PSVITA__) || defined(__ORBIS__)
	if (!m_connected || !m_ready || data == NULL || dataSize <= 0)
	{
		return false;
	}

	EnterCriticalSection(&m_sendLock);
	bool sent = true;
	unsigned char header[9];
	if (m_framedHost)
	{
		header[0] = 'D';
		header[1] = (unsigned char)(peerId >> 24);
		header[2] = (unsigned char)(peerId >> 16);
		header[3] = (unsigned char)(peerId >> 8);
		header[4] = (unsigned char)peerId;
		header[5] = (unsigned char)((unsigned int)dataSize >> 24);
		header[6] = (unsigned char)((unsigned int)dataSize >> 16);
		header[7] = (unsigned char)((unsigned int)dataSize >> 8);
		header[8] = (unsigned char)dataSize;
		sent = SendBytes(header, sizeof(header));
	}
	if (sent)
	{
		sent = SendBytes(data, dataSize);
	}
	if (!sent)
	{
		Close();
	}
	LeaveCriticalSection(&m_sendLock);
	return sent;
#else
	(void)data;
	(void)dataSize;
	return false;
#endif
}

bool RelayTransport::ProcessReceived(const unsigned char *data, int dataSize, ReceiveCallback callback, void *context)
{
	int cursor = 0;
	while (cursor < dataSize)
	{
		if (m_localHandshake && !m_ready)
		{
			const char value = (char)data[cursor++];
			if (value == '\n')
			{
				m_handshakeLine[m_handshakeLineLength] = 0;
				if (strcmp(m_handshakeLine, "READY") == 0)
				{
					m_ready = true;
					m_localHandshake = false;
				}
				else if (strcmp(m_handshakeLine, "WAITING") != 0)
				{
					return false;
				}
				m_handshakeLineLength = 0;
			}
			else if (value != '\r')
			{
				if (m_handshakeLineLength >= (int)sizeof(m_handshakeLine) - 1)
				{
					return false;
				}
				m_handshakeLine[m_handshakeLineLength++] = value;
			}
			continue;
		}

		if (!m_ready)
		{
			return true;
		}

		if (!m_framedHost)
		{
			if (callback != NULL)
			{
				callback(RECEIVE_DATA, 0, data + cursor, dataSize - cursor, context);
			}
			return true;
		}

		if (m_frameHeaderLength < (int)sizeof(m_frameHeader))
		{
			int copySize = (int)sizeof(m_frameHeader) - m_frameHeaderLength;
			if (copySize > dataSize - cursor)
			{
				copySize = dataSize - cursor;
			}
			memcpy(m_frameHeader + m_frameHeaderLength, data + cursor, copySize);
			m_frameHeaderLength += copySize;
			cursor += copySize;
			if (m_frameHeaderLength < (int)sizeof(m_frameHeader))
			{
				continue;
			}

			m_frameType = m_frameHeader[0];
			m_framePeerId = ((unsigned int)m_frameHeader[1] << 24) |
				((unsigned int)m_frameHeader[2] << 16) |
				((unsigned int)m_frameHeader[3] << 8) |
				(unsigned int)m_frameHeader[4];
			m_framePayloadRemaining = ((unsigned int)m_frameHeader[5] << 24) |
				((unsigned int)m_frameHeader[6] << 16) |
				((unsigned int)m_frameHeader[7] << 8) |
				(unsigned int)m_frameHeader[8];

			if (m_framePeerId == 0 || m_framePayloadRemaining > 16 * 1024 * 1024)
			{
				return false;
			}
			if (m_frameType == 'J' || m_frameType == 'L')
			{
				if (m_framePayloadRemaining != 0)
				{
					return false;
				}
				if (callback != NULL)
				{
					callback(m_frameType == 'J' ? PEER_JOINED : PEER_LEFT, m_framePeerId, NULL, 0, context);
				}
				m_frameHeaderLength = 0;
				continue;
			}
			if (m_frameType != 'D')
			{
				return false;
			}
			if (m_framePayloadRemaining == 0)
			{
				m_frameHeaderLength = 0;
				continue;
			}
		}

		unsigned int chunkSize = m_framePayloadRemaining;
		if (chunkSize > (unsigned int)(dataSize - cursor))
		{
			chunkSize = (unsigned int)(dataSize - cursor);
		}
		if (chunkSize > 0 && callback != NULL)
		{
			callback(RECEIVE_DATA, m_framePeerId, data + cursor, (int)chunkSize, context);
		}
		cursor += (int)chunkSize;
		m_framePayloadRemaining -= chunkSize;
		if (m_framePayloadRemaining == 0)
		{
			m_frameHeaderLength = 0;
		}
	}
	return true;
}

bool RelayTransport::Pump(ReceiveCallback callback, void *context)
{
#if defined(_WIN32) || defined(_XBOX)
	if (!m_connected)
	{
		return false;
	}

	unsigned char buffer[8192];
	SOCKET s = (SOCKET)m_socket;

	for (;;)
	{
		int received = recv(s, (char *)buffer, sizeof(buffer), 0);
		if (received > 0)
		{
			if (!ProcessReceived(buffer, received, callback, context))
			{
				Close();
				return false;
			}
			continue;
		}

		if (received == 0)
		{
			Close();
			return false;
		}

		int error = WSAGetLastError();
		if (error == WSAEWOULDBLOCK)
		{
			return true;
		}

#if defined(_XBOX)
		// Xenia may report another transient status for an empty nonblocking read.
		// Keep the relay alive; a graceful close is still handled by received == 0.
		return true;
#endif

		Close();
		return false;
	}
#elif defined(__PS3__)
	if (!m_connected)
	{
		return false;
	}
	unsigned char buffer[8192];
	int receiveIterations = 0;
	for (;;)
	{
		fd_set readSet;
		FD_ZERO(&readSet);
		FD_SET((int)m_socket, &readSet);
		timeval waitTime;
		waitTime.tv_sec = 0;
		waitTime.tv_usec = 0;
#if defined(CONSOLE_LEGACY_PSL1GHT)
		int ready = sysNetSelect((int)m_socket + 1, &readSet, NULL, NULL, &waitTime);
#else
		int ready = socketselect((int)m_socket + 1, &readSet, NULL, NULL, &waitTime);
#endif
		if (ready < 0)
		{
			Close();
			return false;
		}
		if (ready == 0)
		{
			return true;
		}
#if defined(CONSOLE_LEGACY_PSL1GHT)
		int received = sysNetRecvfrom((int)m_socket, buffer, sizeof(buffer), 0, NULL, NULL);
#else
		int received = recv((int)m_socket, buffer, sizeof(buffer), 0);
#endif
		if (received <= 0)
		{
			Close();
			return false;
		}
		if (!ProcessReceived(buffer, received, callback, context))
		{
			Close();
			return false;
		}
		// A joining client can receive world data continuously. Yield back to
		// Minecraft so the main loop keeps rendering and consuming its queues.
		if (++receiveIterations >= 16)
		{
			return true;
		}
	}
#elif defined(__PSVITA__) || defined(__ORBIS__)
	if (!m_connected)
	{
		return false;
	}
	unsigned char buffer[8192];
	int receiveIterations = 0;
	for (;;)
	{
		int received = sceNetRecv((int)m_socket, buffer, sizeof(buffer), 0);
		if (received > 0)
		{
			if (!ProcessReceived(buffer, received, callback, context))
			{
				Close();
				return false;
			}
			if (++receiveIterations >= 16)
			{
				return true;
			}
			continue;
		}
		if (received < 0 && IsSceWouldBlock(received))
		{
			return true;
		}
		Close();
		return false;
	}
#else
	(void)callback;
	(void)context;
	return false;
#endif
}
