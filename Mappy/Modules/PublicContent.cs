using System;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Mappy.Classes;
using Mappy.Extensions;

namespace Mappy.Modules;

public class PublicContent : ModuleBase {
    public override unsafe bool ProcessMarker(MarkerInfo markerInfo) {
        if (EventFramework.Instance()->GetPublicContentDirector() is null) return false;
        var occult = PublicContentOccultCrescent.GetInstance();
        var bozja = PublicContentBozja.GetInstance();
        DynamicEventContainer? container = occult != null
            ? occult->DynamicEventContainer
            : bozja != null
                ? bozja->DynamicEventContainer
                : null;

        if (container is null) return false;

        foreach (var e in container.Value.Events) {
            if (e.State != DynamicEventState.Inactive && e.MapMarker.DataId == markerInfo.DataId) {
                var timeRemaining = e.GetTimeRemaining();
                switch (e.State) {
                    case DynamicEventState.Inactive:
                        return false;
                    case DynamicEventState.Register:
                        markerInfo.SecondaryText = () =>
                            $"Recruiting Members: {timeRemaining.Subtract(TimeSpan.FromSeconds(20)):mm\\:ss}\n" +
                            $"Participants: {e.Participants}/{e.MaxParticipants}";
                        break;
                    case DynamicEventState.Warmup:
                        markerInfo.SecondaryText = () =>
                            $"Preparing for Battle: {timeRemaining.Subtract(TimeSpan.FromSeconds(20)):mm\\:ss}\n" +
                            $"Participants: {e.Participants}/{e.MaxParticipants}";
                        break;
                    case DynamicEventState.Battle:
                        markerInfo.SecondaryText = () =>
                            $"Battle Underway: {e.GetTimeRemaining():mm\\:ss}\n" +
                            $"Participants: {e.Participants}/{e.MaxParticipants}\n" +
                            $"Progress: {e.Progress}%";
                        break;
                    default:
                        return false;
                }
            }
        }

        return true;
    }
}