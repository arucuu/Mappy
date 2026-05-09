using System;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace Mappy.Integrations;

/// <summary>
/// Lifestreamプラグインが公開するIPCを呼び出すための薄いラッパー。
/// </summary>
/// <remarks>
/// <para>
/// Dalamud標準の <see cref="ICallGateSubscriber"/> を直接利用しているため、
/// ECommonsなどの追加依存ライブラリは不要。
/// </para>
/// <para>
/// Lifestreamが未インストールの環境でも例外をすべて握りつぶし、
/// 安全な既定値（false / 0）を返す設計になっている。
/// </para>
/// <para>
/// IPCのサブスクライバは <see cref="Lazy{T}"/> で遅延初期化されるため、
/// 本クラスを利用するための明示的な初期化処理は不要。
/// </para>
/// </remarks>
public static class LifestreamIpc
{
    /// <summary>
    /// 「Lifestream.GetActiveAetheryte」 IPCのサブスクライバ。
    /// 戻り値は現在プレイヤーが範囲内にいる通常エーテライト/シャードのID（範囲外なら0）。
    /// </summary>
    /// <remarks>
    /// プレイヤーがメインエーテライトの近くにいるときはメインエーテライトのID、
    /// Aethernetシャードの近くにいるときはそのシャード自身のIDが返る点に注意。
    /// 同じ街か判定したい場合は、戻り値のIDからAethernetGroupを引いて比較すること。
    /// 住宅街/特殊エリア（クレセントアイル等）では0を返すため、
    /// それぞれ <see cref="GetActiveCustomAetheryte"/>、<see cref="GetActiveResidentialAetheryte"/> を併用すること。
    /// </remarks>
    private static readonly Lazy<ICallGateSubscriber<uint>> getActiveAetheryteSubscriber =
        new(() => Service.PluginInterface.GetIpcSubscriber<uint>("Lifestream.GetActiveAetheryte"));

    /// <summary>
    /// 「Lifestream.GetActiveCustomAetheryte」 IPCのサブスクライバ。
    /// クレセントアイル（オキュアル幻想）等の特殊エリア（CustomAethernet）範囲内かを判定する。
    /// </summary>
    /// <remarks>
    /// 戻り値が0以外なら、プレイヤーは Lifestream が認識する CustomAethernet ゾーンにいる。
    /// </remarks>
    private static readonly Lazy<ICallGateSubscriber<uint>> getActiveCustomAetheryteSubscriber =
        new(() => Service.PluginInterface.GetIpcSubscriber<uint>("Lifestream.GetActiveCustomAetheryte"));

    /// <summary>
    /// 「Lifestream.GetActiveResidentialAetheryte」 IPCのサブスクライバ。
    /// 住宅街（ザ・ラベンダーベッド、ゴブレット、エンピレウム、シロガネ）範囲内かを判定する。
    /// </summary>
    /// <remarks>
    /// 戻り値が0以外なら、プレイヤーは住宅街内の住宅エーテライトシャード範囲内にいる。
    /// </remarks>
    private static readonly Lazy<ICallGateSubscriber<uint>> getActiveResidentialAetheryteSubscriber =
        new(() => Service.PluginInterface.GetIpcSubscriber<uint>("Lifestream.GetActiveResidentialAetheryte"));

    /// <summary>
    /// 「Lifestream.ExecuteCommand」 IPCのサブスクライバ。
    /// 引数は「/li &lt;args&gt;」の args 部分（Aethernet宛先名など）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Action型のIPCのため <see cref="ICallGateSubscriber{T1, TRet}.InvokeAction"/> で呼び出す。
    /// 第二型引数の <c>object</c> はAction戻り値のダミー。
    /// </para>
    /// <para>
    /// プレイヤーが目的地のエーテライト範囲外にいる場合、Lifestreamが自動的に
    /// 通常テレポート→Aethernet移動の順で処理してくれる。
    /// </para>
    /// </remarks>
    private static readonly Lazy<ICallGateSubscriber<string, object>> executeCommandSubscriber =
        new(() => Service.PluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand"));

    /// <summary>
    /// 「Lifestream.AethernetTeleportById」 IPCのサブスクライバ。
    /// 引数は AetheryteシートのRowId（メインエーテライトIDも指定可能）。
    /// 戻り値は Lifestream が要求を受理したか（ビジー時はfalse）。
    /// </summary>
    /// <remarks>
    /// メインエーテライト前への同街内移動に使う。
    /// このIPCは現在のエーテライト範囲内でしか動作しないため、
    /// 別エリアからの呼び出しでは動かない。
    /// </remarks>
    private static readonly Lazy<ICallGateSubscriber<uint, bool>> aethernetTeleportByIdSubscriber =
        new(() => Service.PluginInterface.GetIpcSubscriber<uint, bool>("Lifestream.AethernetTeleportById"));

    /// <summary>
    /// 「Lifestream.IsBusy」 IPCのサブスクライバ。
    /// Lifestream が他のタスク処理中かを問い合わせる。
    /// </summary>
    private static readonly Lazy<ICallGateSubscriber<bool>> isBusySubscriber =
        new(() => Service.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy"));

    /// <summary>
    /// LifestreamのIPCが現在利用可能か（プラグインがロードされているか）を確認する。
    /// </summary>
    /// <returns>Lifestreamが応答可能な場合は true、それ以外は false。</returns>
    public static bool IsAvailable()
    {
        try
        {
            // 副作用のない軽量IPCを呼び出すことで、Provider側の存在を確認する
            _ = isBusySubscriber.Value.InvokeFunc();
            return true;
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
        catch (Exception ex)
        {
            Service.Log.Verbose(ex, "[Mappy] Lifestream IPCの可用性チェックで例外が発生しました");
            return false;
        }
    }

    /// <summary>
    /// 現在プレイヤーが範囲内にいる通常エーテライト/シャードのIDを取得する。
    /// </summary>
    /// <returns>
    /// 範囲内ならAetheryteシートのRowId（メインまたはシャード）。
    /// 範囲外、あるいは取得失敗時は 0。
    /// </returns>
    public static uint GetActiveAetheryte()
    {
        try
        {
            return getActiveAetheryteSubscriber.Value.InvokeFunc();
        }
        catch (IpcNotReadyError)
        {
            return 0u;
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "[Mappy] Lifestream.GetActiveAetheryteの呼び出しに失敗しました");
            return 0u;
        }
    }

    /// <summary>
    /// 現在プレイヤーがCustomAethernet（クレセントアイル等の特殊エリア）の範囲内にいるか取得する。
    /// </summary>
    /// <returns>
    /// 範囲内ならCustomAetheryteのID。範囲外、あるいは取得失敗時は 0。
    /// </returns>
    public static uint GetActiveCustomAetheryte()
    {
        try
        {
            return getActiveCustomAetheryteSubscriber.Value.InvokeFunc();
        }
        catch (IpcNotReadyError)
        {
            return 0u;
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "[Mappy] Lifestream.GetActiveCustomAetheryteの呼び出しに失敗しました");
            return 0u;
        }
    }

    /// <summary>
    /// 現在プレイヤーが住宅街エーテライトシャードの範囲内にいるか取得する。
    /// </summary>
    /// <returns>
    /// 範囲内なら住宅街シャードのID。範囲外、あるいは取得失敗時は 0。
    /// </returns>
    public static uint GetActiveResidentialAetheryte()
    {
        try
        {
            return getActiveResidentialAetheryteSubscriber.Value.InvokeFunc();
        }
        catch (IpcNotReadyError)
        {
            return 0u;
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "[Mappy] Lifestream.GetActiveResidentialAetheryteの呼び出しに失敗しました");
            return 0u;
        }
    }

    /// <summary>
    /// 「/li &lt;arguments&gt;」相当のコマンドをLifestreamに実行させる。
    /// </summary>
    /// <param name="arguments">
    /// 「/li」コマンドの後ろに付ける文字列。Aethernet宛先名（例: "Aftcastle"）など。
    /// </param>
    /// <remarks>
    /// プレイヤーが目的地のエーテライト範囲外にいる場合、
    /// Lifestreamが自動でテレポート→Aethernet移動の順で処理してくれる。
    /// 別エリアからの転送に使う。
    /// </remarks>
    public static void ExecuteCommand(string arguments)
    {
        try
        {
            executeCommandSubscriber.Value.InvokeAction(arguments);
        }
        catch (IpcNotReadyError)
        {
            // Lifestream未インストール時のため何もしない
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "[Mappy] Lifestream.ExecuteCommandの呼び出しに失敗しました");
        }
    }

    /// <summary>
    /// Aetheryte.RowId を指定して Aethernet テレポートを実行する（同街内のメインエーテライト移動向け）。
    /// </summary>
    /// <param name="aetheryteSheetRowId">
    /// AetheryteシートのRowId。Mappyの <c>MapMarkerInfo.DataKey</c>（DataType=3 Aetheryteの場合）と等価。
    /// </param>
    /// <returns>Lifestreamが要求を受理した場合は true、ビジー/未インストール時は false。</returns>
    /// <remarks>
    /// このIPCはエーテライト範囲内でしか動作しない。別エリアからの転送には
    /// <see cref="ExecuteCommand"/> を使うこと。
    /// </remarks>
    public static bool AethernetTeleportById(uint aetheryteSheetRowId)
    {
        try
        {
            return aethernetTeleportByIdSubscriber.Value.InvokeFunc(aetheryteSheetRowId);
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "[Mappy] Lifestream.AethernetTeleportByIdの呼び出しに失敗しました");
            return false;
        }
    }

    /// <summary>
    /// Lifestreamが現在他のタスクを処理中かを取得する。
    /// </summary>
    /// <returns>処理中なら true、それ以外/取得失敗時は false。</returns>
    public static bool IsBusy()
    {
        try
        {
            return isBusySubscriber.Value.InvokeFunc();
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
        catch (Exception ex)
        {
            Service.Log.Verbose(ex, "[Mappy] Lifestream.IsBusyの呼び出しに失敗しました");
            return false;
        }
    }
}
