// Reads the match setup from content/ (presentation side; no rules): the scenario becomes
// the host's init request, and the typed tile map (content/maps) becomes what we draw.
// The host still owns every rule — Unreal only forwards the scenario and draws tiles.
#pragma once

#include "CoreMinimal.h"

struct RTSBRIDGE_API FRtsMatchSetup
{
	/** One-line JSON init request for the host (scenario minus commands/ticks/assert). */
	FString InitRequest;
	int32 Width = 0;
	int32 Height = 0;
	/** Height rows of Width chars: '.' land, '~' water, '#' cliff, '=' bridge. */
	TArray<FString> Rows;
	/** True if Rows came from content/maps; false = derived from the scenario's blocked rects. */
	bool bTypedMap = false;
};

namespace RtsContent
{
	/** Root holding content/: $RTS_CONTENT_ROOT, else <project>/rts (packaged), else <project>/.. (repo). */
	RTSBRIDGE_API FString ContentRoot();

	/** Load content/scenarios/<ScenarioId>.json and (if present and the same size)
	 *  content/maps/<MapId>.json. Returns false with OutError on any malformed input. */
	RTSBRIDGE_API bool LoadMatch(const FString& ScenarioId, const FString& MapId, FRtsMatchSetup& Out, FString& OutError);

	/** The init request with every faction but `Human` played by the host's computer
	 *  opponents (scenario `ai`, sim/ai). Returns the input unchanged if it can't be parsed. */
	RTSBRIDGE_API FString WithComputerPlayers(const FString& InitRequest, int32 Human);

	inline bool IsWalkableTile(TCHAR T) { return T == TEXT('.') || T == TEXT('='); }
}
