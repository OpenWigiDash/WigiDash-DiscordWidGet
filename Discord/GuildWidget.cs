using System.Text.Json;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>Status de quem está online, como vem no widget do servidor.</summary>
public enum MemberStatus
{
    Online,
    Idle,
    DoNotDisturb,
}

/// <summary>
/// Alguém online no servidor, como vem no widget público (<c>guilds/{id}/widget.json</c>). O widget não traz o id
/// de verdade nem o apelido do servidor: só o nome de exibição, o avatar (pelo proxy do widget), o status, o jogo e
/// o canal de voz.
/// </summary>
public sealed record OnlineMember(string Name, MemberStatus Status, string? AvatarUrl, string? Game, string? VoiceChannel);

/// <summary>
/// O widget público do servidor (Configurações do servidor &gt; Widget). Lista até uns 100 online, sem os
/// invisíveis; <see cref="PresenceCount"/> é o total online, que pode passar do tamanho da lista.
/// </summary>
public sealed record GuildWidget(string Name, int PresenceCount, IReadOnlyList<OnlineMember> Members)
{
    public static GuildWidget Parse(JsonElement data)
    {
        var channels = new Dictionary<string, string>();
        if (data.TryGetProperty("channels", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var channel in list.EnumerateArray())
            {
                if (DiscordNotification.Str(channel, "id") is { } id)
                    channels[id] = DiscordNotification.Str(channel, "name") ?? string.Empty;
            }
        }

        var members = new List<OnlineMember>();
        if (data.TryGetProperty("members", out var people) && people.ValueKind == JsonValueKind.Array)
        {
            foreach (var member in people.EnumerateArray())
            {
                var game = member.TryGetProperty("game", out var g) && g.ValueKind == JsonValueKind.Object
                    ? DiscordNotification.Str(g, "name")
                    : null;
                var voice = DiscordNotification.Str(member, "channel_id") is { } channelId
                    ? channels.GetValueOrDefault(channelId)
                    : null;
                members.Add(new OnlineMember(
                    DiscordNotification.Str(member, "username") ?? "?",
                    DiscordNotification.Str(member, "status") switch
                    {
                        "idle" => MemberStatus.Idle,
                        "dnd" => MemberStatus.DoNotDisturb,
                        _ => MemberStatus.Online,
                    },
                    DiscordNotification.Str(member, "avatar_url"),
                    string.IsNullOrWhiteSpace(game) ? null : game,
                    string.IsNullOrEmpty(voice) ? null : voice));
            }
        }

        // Quem está em call primeiro, depois online, ausente e não perturbe; dentro de cada grupo, pelo nome.
        members.Sort((a, b) =>
        {
            var byVoice = (a.VoiceChannel is null).CompareTo(b.VoiceChannel is null);
            if (byVoice != 0)
                return byVoice;
            var byStatus = a.Status.CompareTo(b.Status);
            return byStatus != 0 ? byStatus : StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
        });

        return new GuildWidget(
            DiscordNotification.Str(data, "name") ?? string.Empty,
            data.TryGetProperty("presence_count", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : members.Count,
            members);
    }
}

/// <summary>O widget do servidor está desligado (erro 50004): só um admin liga, em Configurações do servidor &gt; Widget.</summary>
public sealed class GuildWidgetDisabledException() : InvalidOperationException("O widget do servidor está desligado.");
