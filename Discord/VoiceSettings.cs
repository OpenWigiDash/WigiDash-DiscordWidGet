using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>Como reconhecer o dispositivo salvo num widget na lista do Discord.</summary>
public enum DeviceMatching
{
    /// <summary>Pelo id e, se ele mudou, pelo nome sem o prefixo "2- " que o Windows põe quando o dongle troca de porta.</summary>
    Fuzzy,
    /// <summary>Só pelo id do Windows.</summary>
    Exact,
}

/// <summary>Dispositivo de áudio como o Discord lista em <c>available_devices</c>.</summary>
public sealed record AudioDevice(string Id, string Name);

/// <summary>Entrada ou saída de áudio: dispositivo atual, volume (0-100 entrada, 0-200 saída) e opções.</summary>
public sealed record AudioIo(string DeviceId, double Volume, IReadOnlyList<AudioDevice> Devices)
{
    public static AudioIo Parse(JsonElement element)
    {
        var devices = new List<AudioDevice>();
        if (element.TryGetProperty("available_devices", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var device in list.EnumerateArray())
                devices.Add(new AudioDevice(device.GetProperty("id").GetString() ?? string.Empty,
                    device.GetProperty("name").GetString() ?? string.Empty));
        }
        return new AudioIo(
            element.TryGetProperty("device_id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("volume", out var volume) && volume.ValueKind == JsonValueKind.Number ? volume.GetDouble() : 0,
            devices);
    }

    /// <summary>
    /// O dispositivo guardado num widget, na lista atual do Discord. Procura pelo id e, no <see cref="DeviceMatching.Fuzzy"/>,
    /// pelo nome se o id sumiu: o Windows troca o id do endpoint quando recria o dispositivo (dongle em outra porta USB,
    /// driver reinstalado) e põe ou tira o "2- " do nome ("Microfone do Headset (2- JBL Quantum 800 Chat)" vira
    /// "Microfone do Headset (JBL Quantum 800 Chat)"). <c>null</c> quando não acha (desligado ou removido).
    /// Mesma regra do "Reconhecer o dispositivo" do Audio Device do plugin System.
    /// </summary>
    public AudioDevice? Find(string? id, string? name, DeviceMatching matching = DeviceMatching.Fuzzy)
    {
        if (!string.IsNullOrEmpty(id) && Devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) is { } byId)
            return byId;
        if (matching == DeviceMatching.Exact || string.IsNullOrWhiteSpace(name))
            return null;
        var normalized = NormalizeName(name);
        return Devices.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? Devices.FirstOrDefault(d => NormalizeName(d.Name) == normalized);
    }

    /// <summary>O id a mandar ao Discord: o do dispositivo achado (<see cref="Find"/>), ou o guardado se não achou.</summary>
    public string? ResolveId(string? id, string? name, DeviceMatching matching = DeviceMatching.Fuzzy) =>
        Find(id, name, matching)?.Id ?? id;

    // "(2- JBL Quantum 800)", "2- Alto-falantes": o número que o Windows põe quando cria outro endpoint igual.
    private static readonly Regex Counter = new(@"(^|\()\s*\d+\s*-\s*", RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>Nome sem o "N- " do Windows, sem espaços repetidos e em minúsculas.</summary>
    public static string NormalizeName(string? name) => string.IsNullOrWhiteSpace(name)
        ? ""
        : Spaces.Replace(Counter.Replace(name.Trim(), "$1"), " ").Trim().ToLowerInvariant();
}

/// <summary>Modo de entrada de voz do Discord.</summary>
public enum VoiceMode
{
    VoiceActivity,
    PushToTalk,
}

/// <summary>Resultado de GET_VOICE_SETTINGS e do evento VOICE_SETTINGS_UPDATE.</summary>
public sealed record VoiceSettings(bool Mute, bool Deaf, VoiceMode Mode, AudioIo Input, AudioIo Output)
{
    /// <summary>
    /// O microfone está cortado: mutado, ou ensurdecido (no Discord, ensurdecer também muta, mesmo com
    /// <see cref="Mute"/> falso).
    /// </summary>
    public bool MicMuted => Mute || Deaf;

    /// <summary>
    /// Argumentos para abrir ou cortar o microfone. Abrir com o áudio ensurdecido também tira o ensurdecer,
    /// como o botão de microfone do próprio Discord.
    /// </summary>
    public JsonObject MicArgs(bool mute) => !mute && Deaf
        ? new JsonObject { ["mute"] = false, ["deaf"] = false }
        : new JsonObject { ["mute"] = mute };

    public static VoiceSettings Parse(JsonElement data)
    {
        var mode = data.TryGetProperty("mode", out var m) && m.TryGetProperty("type", out var type)
            && type.GetString() == "PUSH_TO_TALK"
            ? VoiceMode.PushToTalk
            : VoiceMode.VoiceActivity;
        return new VoiceSettings(
            data.TryGetProperty("mute", out var mute) && mute.GetBoolean(),
            data.TryGetProperty("deaf", out var deaf) && deaf.GetBoolean(),
            mode,
            data.TryGetProperty("input", out var input) ? AudioIo.Parse(input) : new AudioIo(string.Empty, 0, []),
            data.TryGetProperty("output", out var output) ? AudioIo.Parse(output) : new AudioIo(string.Empty, 0, []));
    }

    /// <summary>
    /// O evento VOICE_SETTINGS_UPDATE pode vir sem a lista de dispositivos; mantém a última conhecida.
    /// </summary>
    public VoiceSettings MergeDevices(VoiceSettings? previous) => previous is null
        ? this
        : this with
        {
            Input = Input.Devices.Count > 0 ? Input : Input with { Devices = previous.Input.Devices },
            Output = Output.Devices.Count > 0 ? Output : Output with { Devices = previous.Output.Devices },
        };
}
