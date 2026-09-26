using UnrealBuildTool;

public class RtsBridgeEditorTarget : TargetRules
{
	public RtsBridgeEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V7;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("RtsBridge");
	}
}
