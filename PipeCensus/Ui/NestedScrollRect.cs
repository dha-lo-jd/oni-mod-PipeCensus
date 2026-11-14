using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PipeCensus;

// A scroll area inside another one. A ScrollRect swallows every wheel event over it, so once it is at the end the wheel
// is handed to the scroll area around it; otherwise the list would stop scrolling whenever the pointer sits on this one.
internal sealed class NestedScrollRect : ScrollRect
{
    public override void OnScroll(PointerEventData data)
    {
        var delta = data.scrollDelta.y;
        var atEnd = delta > 0f ? verticalNormalizedPosition >= 0.999f : verticalNormalizedPosition <= 0.001f;
        if (Mathf.Approximately(delta, 0f) || !atEnd || transform.parent == null)
        {
            base.OnScroll(data);
            return;
        }

        ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, data, ExecuteEvents.scrollHandler);
    }
}
