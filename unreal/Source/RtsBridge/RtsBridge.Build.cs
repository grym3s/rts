// RtsBridge: Unreal presentation shell for the RTS. Owns no game rules; the
// .NET sim host process is the sole simulation authority (see docs/superpowers/specs).
using UnrealBuildTool;

public class RtsBridge : ModuleRules
{
	public RtsBridge(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "Json", "JsonUtilities", "Sockets" });
		if (Target.bBuildEditor)
		{
			PrivateDependencyModuleNames.Add("UnrealEd");
		}
	}
}
