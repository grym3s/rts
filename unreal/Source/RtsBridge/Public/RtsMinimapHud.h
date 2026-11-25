// Minimap (presentation only), drawn with the HUD canvas so it needs no UMG assets:
// map field, one dot per unit in its faction colour (selected = white outline), and the
// camera's view footprint. The controller asks it to turn minimap clicks into world points.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "RtsMinimapHud.generated.h"

class UTexture2D;
class UFont;

UCLASS()
class RTSBRIDGE_API ARtsMinimapHud : public AHUD
{
	GENERATED_BODY()

public:
	virtual void DrawHUD() override;

	/** If ScreenPos (viewport px) is on the minimap, return the world ground point under it. */
	bool MinimapToWorld(const FVector2D& ScreenPos, FVector2D& OutWorldXY) const;

	/** Order feedback (pillar 1: an acknowledgement on the same frame as the order). */
	enum class EOrderMarker : uint8 { Move, AttackMove, Attack, Stop };
	/** Ground marker at a world point (also shown on the minimap). */
	void AddGroundMarker(const FVector2D& WorldXY, EOrderMarker Kind);
	/** Marker that follows a unit (attack target, stopped units). */
	void AddUnitMarker(int32 SimUnitId, EOrderMarker Kind);

	/** T16 (pillar 4): one shot from a `shot` event — world points of shooter and target at
	 *  that tick, coloured by the shooter's faction. Lives TracerSeconds. */
	void AddTracer(const FVector& From, const FVector& To, int32 ShooterFaction);

	/** T19 result screen layout in viewport px: centred panel and its buttons. Static and
	 *  pure so the hit test is unit-tested. Without restart only Quit is laid out. */
	struct FMatchScreenLayout
	{
		FBox2D Panel;
		FBox2D PlayAgain; // invalid (IsValid == false) when restart is not offered
		FBox2D Quit;
	};
	static FMatchScreenLayout MatchScreenLayout(const FVector2D& Viewport, bool bCanRestart);
	enum class EMatchScreenButton : uint8 { None, PlayAgain, Quit };
	static EMatchScreenButton MatchScreenHit(const FVector2D& Viewport, const FVector2D& Pos, bool bCanRestart);

	/** T35 top bar text: credits with thousands separators, power as used / produced, and the
	 *  low-power flag (draw > supply: the bar turns red). Static and pure for the test. */
	struct FEconomyBarText
	{
		FString Credits;
		FString Power;
		bool bLowPower = false;
	};
	static FEconomyBarText EconomyBarText(int32 Credits, int32 PowerSupply, int32 PowerDraw);

private:
	struct FOrderMarker
	{
		FVector2D WorldXY = FVector2D::ZeroVector;
		int32 UnitId = INDEX_NONE; // != INDEX_NONE: follows that unit
		EOrderMarker Kind = EOrderMarker::Move;
		double StartTime = 0.0;
	};
	TArray<FOrderMarker> Markers;
	struct FTracer
	{
		FVector From = FVector::ZeroVector;
		FVector To = FVector::ZeroVector;
		FLinearColor Color = FLinearColor::White;
		double StartTime = 0.0;
	};
	TArray<FTracer> Tracers;
	static constexpr double TracerSeconds = 0.2;
	static constexpr int32 MaxTracers = 256; // a 500-unit brawl stays bounded
	void DrawTracers();
	void DrawMatchScreen();
	/** T20 F1-F4 overlays (reads the game mode's debug rows; draws nothing when off). */
	void DrawDebugOverlays();
	double FpsAvg = 0.0;
	void DrawEconomyBar();
	int32 LastCredits = INT32_MIN; // T35: the +/- flash beside credits
	int32 CreditsDelta = 0;
	double CreditsDeltaUntil = 0.0;
	void DrawButton(const FBox2D& R, const FString& Label, bool bHover, float Alpha);
	void DrawCentredText(const FString& Text, UFont* Font, float CentreX, float Y, float Scale, const FLinearColor& Color);
	/** Terrain image, one texel per cell, built once from the game mode's tile rows. */
	UPROPERTY() TObjectPtr<UTexture2D> TerrainTex;
	UTexture2D* GetTerrainTexture();
	static constexpr double MarkerSeconds = 0.6;
	static FLinearColor MarkerColor(EOrderMarker Kind);
	void DrawMarkers(const FBox2D& MinimapR);
	/** T15: bar over every damaged or selected unit (screen space, under the minimap). */
	void DrawHealthBars();
	/** Ground-plane circle projected to the screen (skipped if any point is off-view). */
	void DrawGroundCircle(const FVector2D& CenterXY, float RadiusUU, const FLinearColor& Color, float Thickness);

	/** Minimap rectangle in viewport pixels for a viewport of ViewportSize (bottom-left). */
	static FBox2D MinimapRect(const FVector2D& ViewportSize);
	FVector2D WorldToMinimap(const FBox2D& Rect, const FVector2D& WorldXY) const;
	FVector2D ViewportSize() const;
	/** Clip a world-XY polygon to a box (camera footprint vs map edges). */
	static TArray<FVector2D> ClipToBox(const TArray<FVector2D>& In, const FBox2D& Box);
};
