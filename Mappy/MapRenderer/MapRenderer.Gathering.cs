using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Mappy.Extensions;

namespace Mappy.MapRenderer;

public partial class MapRenderer {
    private unsafe void DrawGatheringMarkers() {
        var agent = AgentMap.Instance();
        if (agent->SelectedMapId != agent->CurrentMapId) return;
        
        foreach (var marker in agent->MiniMapGatheringMarkers) {
            marker.Draw(DrawPosition, Scale);
        }
    }
}