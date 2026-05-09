using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace Mappy.Integrations;

/// <summary>
/// マップ上のエーテライト/Aethernetアイコンがクリックされた際に、
/// Lifestream IPC経由でAethernet転送を試みるハンドラ。
/// </summary>
/// <remarks>
/// <para>動作仕様：</para>
/// <list type="bullet">
///   <item><description>
///     Aethernetシャードクリック (<see cref="TryHandleAethernet"/>): 以下のいずれかを満たすときLifestreamに処理を委譲する。
///     <list type="bullet">
///       <item><description>通常街のAethernet（親エーテライトを持つ）</description></item>
///       <item><description>プレイヤーがCustomAethernet（クレセントアイル等）範囲内にいる</description></item>
///       <item><description>プレイヤーが住宅街エーテライト範囲内にいる</description></item>
///     </list>
///     どれにも該当しない場合（別エリアから住宅街/特殊エリアのAethernetクリック等）は
///     既定動作にフォールバックし、Lifestreamのエラー通知が出ないようにする。
///   </description></item>
///   <item><description>
///     メインエーテライトクリック (<see cref="TryHandleAetheryte"/>): 同街内ならLifestream経由で移動。
///     別エリアならMappyの既定動作（FF14通常テレポ）にフォールバックする。
///   </description></item>
///   <item><description>
///     特殊マーカークリック (<see cref="TryHandleSpecialMarker"/>): DataType=0で扱われるマーカー
///     （クレセントアイル等の簡易魔道通路）に対応する。プレイヤーがCustomAethernet/住宅街範囲内なら
///     ツールチップテキスト（Subtext）を宛先名としてLifestreamに渡す。
///   </description></item>
/// </list>
/// </remarks>
public static class LifestreamAethernetHandler
{
    /// <summary>
    /// Aethernetシャード（都市内転送網）のクリック処理を試みる。
    /// </summary>
    /// <param name="marker">クリックされたマップマーカー（DataTypeが4=Aethernetであることが前提）。</param>
    /// <returns>
    /// <para>true: Lifestreamに処理を委譲した。呼出側はフォールバックを実行しない。</para>
    /// <para>false: Lifestreamが利用不可。呼出側は既定動作を実行する。</para>
    /// </returns>
    public static bool TryHandleAethernet(ref MapMarkerInfo marker)
    {
        // Lifestream未インストール → 既定動作にフォールバック
        if (!LifestreamIpc.IsAvailable())
        {
            return false;
        }

        // Lifestreamで処理可能か事前判定する。下記いずれかを満たす必要がある：
        //   1. 通常街のAethernet（親エーテライトが存在する）
        //   2. プレイヤーがCustomAethernet（クレセントアイル等）範囲内にいる
        //   3. プレイヤーが住宅街エーテライト範囲内にいる
        // どれにも該当しない場合（例: 別エリアから住宅街/特殊エリアのAethernetをクリック等）は
        // Lifestreamが宛先名を解釈できずエラー通知を出すため、既定動作にフォールバックする
        var hasParentAetheryte = HasParentAetheryte(marker.DataKey);
        var isInCustomAethernetZone = LifestreamIpc.GetActiveCustomAetheryte() != 0;
        var isInResidentialAethernetZone = LifestreamIpc.GetActiveResidentialAetheryte() != 0;

        if (!hasParentAetheryte && !isInCustomAethernetZone && !isInResidentialAethernetZone)
        {
            return false;
        }

        // Lifestreamが他のタスクを処理中なら何もしない
        if (LifestreamIpc.IsBusy())
        {
            return true;
        }

        // クリックされたAethernetの宛先名（PlaceName）を取得する
        // marker.DataKey は PlaceName.RowId
        var placeNameSheet = Service.DataManager.GetExcelSheet<PlaceName>();
        if (!placeNameSheet.TryGetRow(marker.DataKey, out var placeName))
        {
            return true;
        }

        var destinationName = placeName.Name.ExtractText();
        if (string.IsNullOrEmpty(destinationName))
        {
            return true;
        }

        // 「/li <宛先名>」を実行する。Lifestream側で必要に応じてテレポート→Aethernet移動が実行される
        LifestreamIpc.ExecuteCommand(destinationName);
        return true;
    }

    /// <summary>
    /// 指定したPlaceNameに対応するAethernetが、通常街の親エーテライトを持つかを判定する。
    /// </summary>
    /// <param name="placeNameRowId">クリックされたAethernetの PlaceName.RowId。</param>
    /// <returns>親エーテライトを持つ場合は true（住宅街・特殊エリアでは false）。</returns>
    private static bool HasParentAetheryte(uint placeNameRowId)
    {
        var parentAetheryte = System.AetheryteAethernetCache.GetValue(placeNameRowId);
        return parentAetheryte is not null && parentAetheryte.Value.RowId != 0;
    }

    /// <summary>
    /// メインエーテライトのクリック処理を試みる。
    /// 同街内ならLifestream経由でAethernet移動、別の街ならMappyの既定動作（FF14通常テレポ）にフォールバックする。
    /// </summary>
    /// <param name="marker">クリックされたマップマーカー（DataTypeが3=Aetheryteであることが前提）。</param>
    /// <returns>
    /// <para>true: Lifestreamで同街内Aethernet移動を実行した。呼出側はフォールバックを実行しない。</para>
    /// <para>false: Lifestream利用不可、または別の街なので通常テレポを使うべき。呼出側は既定動作を実行する。</para>
    /// </returns>
    public static bool TryHandleAetheryte(ref MapMarkerInfo marker)
    {
        // 不正なIDは何もしない（呼出側のガードと同じ）
        if (marker.DataKey == 0)
        {
            return false;
        }

        // Lifestream未インストール → 既定動作（FF14通常テレポ）にフォールバック
        if (!LifestreamIpc.IsAvailable())
        {
            return false;
        }

        // 現在プレイヤーが範囲内にいるエーテライト/シャードIDを取得する
        var activeAetheryteId = LifestreamIpc.GetActiveAetheryte();
        if (activeAetheryteId == 0)
        {
            // 範囲外（同街内エーテライト離脱、または別エリアにいる）
            // 別エリアの可能性を考慮し、通常テレポにフォールバック
            return false;
        }

        // 同じ街（同じAethernetGroup）か判定する
        if (!IsInSameAethernetGroup(marker.DataKey, activeAetheryteId))
        {
            // 別の街なら通常テレポにフォールバック（FF14テレポは別エリアへの転送が可能なため）
            return false;
        }

        // Lifestreamが他のタスクを処理中なら何もしない
        if (LifestreamIpc.IsBusy())
        {
            return true;
        }

        // 同じ街なのでLifestream経由でAethernet移動を実行する
        // （FF14のテレポ機能は同エリア内では使えないため、Aethernet経由が必要）
        LifestreamIpc.AethernetTeleportById(marker.DataKey);
        return true;
    }

    /// <summary>
    /// クリックされたメインエーテライトと現在地のエーテライトが同じAethernetGroupに属するか判定する。
    /// </summary>
    /// <param name="clickedAetheryteId">クリックされたメインエーテライトの Aetheryte.RowId。</param>
    /// <param name="activeAetheryteId">現在プレイヤーが範囲内にいるエーテライト/シャードのID。</param>
    /// <returns>同じAethernetGroupに属する場合は true。</returns>
    /// <remarks>
    /// AethernetGroup が 0 の場合は「Aethernet機能を持たないエーテライト」を意味するため、
    /// 安全側に倒して常に false を返す。
    /// </remarks>
    private static bool IsInSameAethernetGroup(uint clickedAetheryteId, uint activeAetheryteId)
    {
        var aetheryteSheet = Service.DataManager.GetExcelSheet<Aetheryte>();

        if (!aetheryteSheet.TryGetRow(clickedAetheryteId, out var clickedAetheryte))
        {
            return false;
        }

        if (!aetheryteSheet.TryGetRow(activeAetheryteId, out var activeAetheryte))
        {
            return false;
        }

        var clickedGroup = clickedAetheryte.AethernetGroup;
        if (clickedGroup == 0)
        {
            return false;
        }

        return clickedGroup == activeAetheryte.AethernetGroup;
    }

    /// <summary>
    /// 特殊マーカー（DataType=0）クリック時の処理を試みる。
    /// クレセントアイル等のCustomAethernetや住宅街サブAethernetが該当する。
    /// </summary>
    /// <param name="marker">クリックされたマップマーカー（DataType=0が前提）。</param>
    /// <returns>
    /// <para>true: Lifestreamに処理を委譲した。</para>
    /// <para>false: 処理対象外（通常街のNPCマーカー等）。呼出側は何もしない。</para>
    /// </returns>
    /// <remarks>
    /// プレイヤーがCustomAethernetまたは住宅街エーテライト範囲内にいる場合だけ動作する。
    /// 通常街でDataType=0のマーカー（NPC等）をクリックした際の誤動作を防ぐ。
    /// 宛先名は <c>marker.MapMarker.Subtext</c>（ツールチップテキスト）から取得する。
    /// </remarks>
    public static bool TryHandleSpecialMarker(ref MapMarkerInfo marker)
    {
        // Lifestream未インストール → 何もしない
        if (!LifestreamIpc.IsAvailable())
        {
            return false;
        }

        // プレイヤーがCustomAethernet/Residential範囲内でなければ処理対象外
        // （通常街のDataType=0 NPCマーカー等の誤動作を防ぐ）
        var isInCustomAethernetZone = LifestreamIpc.GetActiveCustomAetheryte() != 0;
        var isInResidentialAethernetZone = LifestreamIpc.GetActiveResidentialAetheryte() != 0;

        if (!isInCustomAethernetZone && !isInResidentialAethernetZone)
        {
            return false;
        }

        // ツールチップ表示テキストから宛先名を取得
        var destinationName = marker.MapMarker.Subtext.AsDalamudSeString().TextValue;
        if (string.IsNullOrEmpty(destinationName))
        {
            return false;
        }

        // Lifestreamが他のタスクを処理中なら何もしない
        if (LifestreamIpc.IsBusy())
        {
            return true;
        }

        // 「/li <宛先名>」を実行する。Lifestreamが内部のCustomAethernet/ResidentialAethernet
        // データから対応するシャードを探してテレポート→Aethernet移動を行う
        LifestreamIpc.ExecuteCommand(destinationName);
        return true;
    }
}
