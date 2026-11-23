// Selection state + control groups 1–3 (presentation only). Port of
// game/selection/SelectionController.cs: stores sim unit ids (ints), no actors, no sim
// types, so it is unit-testable without a world. Click vs box is decided by the caller
// using BoxDragThreshold.
#pragma once

#include "CoreMinimal.h"

struct RTSBRIDGE_API FRtsSelection
{
	static constexpr int32 ControlGroupCount = 3;
	/** A drag counts as a box (instead of a click) past this screen distance (px). */
	static constexpr float BoxDragThreshold = 6.0f;

	/** Click: Id == INDEX_NONE is a click on empty ground. Shift toggles membership;
	 *  plain click selects only that unit (or clears, on empty ground). */
	void Click(int32 Id, bool bShift)
	{
		if (bShift)
		{
			if (Id != INDEX_NONE && Selected.Remove(Id) == 0)
				Selected.Add(Id);
			return;
		}
		Selected.Reset();
		if (Id != INDEX_NONE)
			Selected.Add(Id);
	}

	/** Box select: replace (or shift-add) the selection with every id in the rect. */
	void BoxSelect(const TArray<int32>& IdsInRect, bool bShift)
	{
		if (!bShift)
			Selected.Reset();
		for (const int32 Id : IdsInRect)
			Selected.Add(Id);
	}

	void SetAll(const TArray<int32>& Ids)
	{
		Selected.Reset();
		Selected.Append(Ids);
	}

	/** Ctrl+N: group N := current selection. */
	void AssignGroup(int32 Group)
	{
		if (Group >= 0 && Group < ControlGroupCount)
			Groups[Group] = Selected;
	}

	/** N: selection := group N. Returns false (selection untouched) for an empty group. */
	bool RecallGroup(int32 Group)
	{
		if (Group < 0 || Group >= ControlGroupCount || Groups[Group].Num() == 0)
			return false;
		Selected = Groups[Group];
		return true;
	}

	/** Drop ids that no longer exist (unit deaths) from the selection and every group. */
	template <typename PredT>
	void Prune(PredT StillExists)
	{
		auto PruneSet = [&](TSet<int32>& S)
		{
			for (auto It = S.CreateIterator(); It; ++It)
				if (!StillExists(*It))
					It.RemoveCurrent();
		};
		PruneSet(Selected);
		for (TSet<int32>& G : Groups)
			PruneSet(G);
	}

	bool Contains(int32 Id) const { return Selected.Contains(Id); }
	const TSet<int32>& GetSelected() const { return Selected; }
	const TSet<int32>& GetGroup(int32 Group) const { return Groups[FMath::Clamp(Group, 0, ControlGroupCount - 1)]; }

private:
	TSet<int32> Selected;
	TSet<int32> Groups[ControlGroupCount];
};
