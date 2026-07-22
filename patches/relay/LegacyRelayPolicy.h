#pragma once

// Relay builds use the local companion instead of Xbox Live for multiplayer.
class LegacyRelayPolicy
{
public:
	static bool IsSignedInLive(int playerIndex)
	{
#if defined(CONSOLE_LEGACY_RELAY)
		(void)playerIndex;
		return true;
#else
		return ProfileManager.IsSignedInLive(playerIndex);
#endif
	}

	static bool AllowedToPlayMultiplayer(int playerIndex)
	{
#if defined(CONSOLE_LEGACY_RELAY)
		(void)playerIndex;
		return true;
#else
		return ProfileManager.AllowedToPlayMultiplayer(playerIndex);
#endif
	}

	static void AllowedPlayerCreatedContent(int playerIndex, bool onlineCheck, BOOL *allowed, BOOL *friendsAllowed)
	{
#if defined(CONSOLE_LEGACY_RELAY)
		(void)playerIndex;
		(void)onlineCheck;
		if (allowed != NULL) *allowed = TRUE;
		if (friendsAllowed != NULL) *friendsAllowed = TRUE;
#else
		ProfileManager.AllowedPlayerCreatedContent(playerIndex, onlineCheck, allowed, friendsAllowed);
#endif
	}

	static bool OnlineGame(bool requested)
	{
#if defined(CONSOLE_LEGACY_RELAY)
		(void)requested;
		return true;
#else
		return requested;
#endif
	}
};
