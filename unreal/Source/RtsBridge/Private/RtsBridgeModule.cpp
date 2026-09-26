// RtsBridge module. Owns no game rules — the .NET sim host process is the
// sole simulation authority (docs/superpowers/specs/2026-09-26-unreal-rts-migration-design.md).
#include "Modules/ModuleManager.h"

class FRtsBridgeModule : public IModuleInterface
{
public:
	virtual void StartupModule() override {}
	virtual void ShutdownModule() override {}
};

IMPLEMENT_MODULE(FRtsBridgeModule, RtsBridge)
