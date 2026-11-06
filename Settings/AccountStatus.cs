using MudBlazor;
using WigiDash.DiscordWidget.Discord;

namespace WigiDash.DiscordWidget.Settings;

/// <summary>Estado da conta em texto e cor, igual na página do plugin e no aviso da tela de cada widget.</summary>
internal static class AccountStatus
{
    public static string Text(DiscordSession session) => session.State switch
    {
        DiscordState.Ready => $"Conectado como {session.UserName}",
        DiscordState.DiscordNotRunning => "O Discord não está aberto neste PC",
        DiscordState.NeedsAuthorization => $"Discord aberto ({session.UserName}), falta autorizar",
        DiscordState.Authorizing => "Clique em Autorizar na janela do Discord",
        DiscordState.Failed => "Erro na conexão; tentando de novo",
        DiscordState.Stopped => "Parado",
        _ => "Conectando...",
    };

    public static Color StateColor(DiscordSession session) => session.State switch
    {
        DiscordState.Ready => Color.Success,
        DiscordState.Connecting or DiscordState.Authorizing => Color.Info,
        _ => Color.Warning,
    };

    public static Severity StateSeverity(DiscordSession session) => session.State switch
    {
        DiscordState.Ready => Severity.Success,
        DiscordState.Connecting or DiscordState.Authorizing => Severity.Info,
        _ => Severity.Warning,
    };
}
