using UnrealBuildTool;

public class RtsBridgeTarget : TargetRules
{
	public RtsBridgeTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.V7;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("RtsBridge");
	}
}
