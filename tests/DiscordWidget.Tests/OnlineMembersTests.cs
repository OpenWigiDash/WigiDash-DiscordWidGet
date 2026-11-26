using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Widgets;

namespace DiscordWidget.Tests;

/// <summary>Quem está online (widget público do servidor) e a tela cheia do Server Stats.</summary>
public sealed class OnlineMembersTests
{
    internal const string WidgetJson = """
        {
          "id": "g1", "name": "Servidor 🎮", "instant_invite": null, "presence_count": 5,
          "channels": [ { "id": "v1", "name": "Geral", "position": 0 } ],
          "members": [
            { "id": "0", "username": "zeca", "discriminator": "0000", "avatar": null, "status": "dnd",
              "avatar_url": "https://cdn.discordapp.com/widget-avatars/a/b" },
            { "id": "1", "username": "Ana", "discriminator": "0000", "avatar": null, "status": "idle",
              "avatar_url": "https://cdn.discordapp.com/widget-avatars/c/d", "game": { "name": "Minecraft" } },
            { "id": "2", "username": "bruno", "discriminator": "0000", "avatar": null, "status": "online",
              "avatar_url": "https://cdn.discordapp.com/widget-avatars/e/f", "channel_id": "v1", "self_mute": true },
            { "id": "3", "username": "Carla", "discriminator": "0000", "avatar": null, "status": "online",
              "avatar_url": "https://cdn.discordapp.com/widget-avatars/g/h", "game": { "name": " " } }
          ]
        }
        """;

    [Fact]
    public void Parse_le_nome_status_jogo_e_canal_e_ordena_call_primeiro_depois_status_e_nome()
    {
        using var json = JsonDocument.Parse(WidgetJson);
        var widget = GuildWidget.Parse(json.RootElement);

        Assert.Equal("Servidor 🎮", widget.Name);
        Assert.Equal(5, widget.PresenceCount);
        Assert.Equal(["bruno", "Carla", "Ana", "zeca"], widget.Members.Select(m => m.Name));
        var bruno = widget.Members[0];
        Assert.Equal("Geral", bruno.VoiceChannel);
        Assert.Equal(MemberStatus.Online, bruno.Status);
        Assert.Null(widget.Members[1].Game); // jogo só com espaço não conta
        Assert.Equal("Minecraft", widget.Members[2].Game);
        Assert.Equal(MemberStatus.Idle, widget.Members[2].Status);
        Assert.Equal(MemberStatus.DoNotDisturb, widget.Members[3].Status);
        Assert.Equal("https://cdn.discordapp.com/widget-avatars/e/f", bruno.AvatarUrl);
    }

    [Fact]
    public void Parse_sem_lista_fica_vazio_e_conta_pelo_que_veio()
    {
        using var json = JsonDocument.Parse("""{ "name": "X" }""");
        var widget = GuildWidget.Parse(json.RootElement);
        Assert.Empty(widget.Members);
        Assert.Equal(0, widget.PresenceCount);
    }

    [Fact]
    public void Tela_cheia_desenha_lista_paginas_e_avisos_sem_erro()
    {
        using var json = JsonDocument.Parse(WidgetJson);
        var widget = GuildWidget.Parse(json.RootElement);
        var many = widget with
        {
            PresenceCount = 140,
            Members = [.. Enumerable.Range(0, 100).Select(i => new OnlineMember($"Pessoa {i} com um nome bem comprido", (MemberStatus)(i % 3),
                null, i % 2 == 0 ? "Counter-Strike 2" : null, i % 5 == 0 ? "Sala de reunião 🎧" : null))],
        };
        MemberListVisual[] visuals =
        [
            new("Servidor", null),
            new("Servidor", null) { Message = "Widget do servidor desligado", Hint = ServerStatsInstance.WidgetDisabledHint },
            new("Servidor 🎮", null) { Widget = widget },
            new("Servidor", null) { Widget = widget with { Members = [] } },
            new("Servidor", null) { Widget = many, Page = 3 },
            new("Servidor", null) { Widget = many, Page = 99 },
        ];
        foreach (var visual in visuals)
        {
            var frame = MemberListRenderer.Render(WidgetGrid.Screen, new WidgetTheme(), visual);
            Assert.Equal(WidgetGrid.Screen, frame.Size);
        }
    }

    [Fact]
    public void Tela_cheia_cabe_4_colunas_por_7_linhas_e_pagina_o_resto()
    {
        Assert.Equal((4, 7), MemberListRenderer.Layout(WidgetGrid.Screen));
        Assert.Equal(1, MemberListRenderer.PageCount(WidgetGrid.Screen, 0));
        Assert.Equal(1, MemberListRenderer.PageCount(WidgetGrid.Screen, 28));
        Assert.Equal(2, MemberListRenderer.PageCount(WidgetGrid.Screen, 29));
        Assert.Equal(4, MemberListRenderer.PageCount(WidgetGrid.Screen, 100));
    }
}

/// <summary>Server Stats em tela cheia contra o Discord simulado e um widget.json servido localmente.</summary>
[Collection(nameof(SessionOverrideCollection))]
public sealed class ServerStatsFullscreenTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "discordwidget-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDiscord _discord = new();
    private readonly HttpListener _api = new();
    private readonly CancellationTokenSource _stop = new();
    private DiscordSession _session = null!;
    private string _apiBase = null!;
    private volatile int _status = 200;
    private volatile string _body = OnlineMembersTests.WidgetJson;
    private int _widgetRequests;

    public Task InitializeAsync()
    {
        _discord.Handler = (cmd, _) => cmd switch
        {
            "GET_VOICE_SETTINGS" => Json.VoiceSettings(),
            "GET_GUILDS" => JsonNode.Parse("""{ "guilds": [ { "id": "g1", "name": "Servidor" } ] }"""),
            _ => new JsonObject(),
        };
        var store = new AccountStore(Path.Combine(_dir, "account.json"));
        store.SaveToken(new DiscordToken("access", "refresh", DateTimeOffset.UtcNow.AddDays(1), "rpc"));

        var port = FreePort();
        _apiBase = $"http://127.0.0.1:{port}/api";
        _api.Prefixes.Add($"http://127.0.0.1:{port}/");
        _api.Start();
        _ = Task.Run(ServeAsync);

        _session = new DiscordSession(store, TimeProvider.System, _discord.Prefix) { ApiBase = _apiBase };
        DiscordButtonInstance.SessionOverride = _session;
        return Task.CompletedTask;
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _api.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }
            var path = context.Request.Url!.AbsolutePath;
            var (status, body) = path.EndsWith("/widget.json", StringComparison.Ordinal)
                ? (_status, _body)
                : (200, """[ { "id": "g1", "name": "Servidor", "approximate_member_count": 10, "approximate_presence_count": 5 } ]""");
            if (path.EndsWith("/widget.json", StringComparison.Ordinal))
                Interlocked.Increment(ref _widgetRequests);
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private async Task<(ServerStatsInstance Instance, FakeContext Context)> CreateAsync()
    {
        var context = new FakeContext(WidgetSize.Small) { AllowFullscreen = true };
        var instance = (ServerStatsInstance)await new ServerStatsWidget().CreateInstanceAsync(context, CancellationToken.None);
        context.Instance = instance;
        await FakeDiscord.WaitUntilAsync(() => _session.State == DiscordState.Ready);
        await instance.SaveOptionsAsync(new ServerStatsOptions { GuildId = "g1", GuildName = "Servidor" });
        return (instance, context);
    }

    private static ValueTask Touch(IWidgetInstance instance, TouchKind kind) =>
        instance.OnTouchAsync(new TouchEvent(kind, 10, 10, TimeSpan.Zero), CancellationToken.None);

    [Fact]
    public void Manifesto_aceita_tela_cheia() => Assert.True(new ServerStatsWidget().Manifest.SupportsFullscreen);

    [Fact]
    public async Task Toque_abre_a_tela_cheia_com_quem_esta_online_e_outro_toque_fecha()
    {
        var (instance, context) = await CreateAsync();
        await using var _ = instance;

        await Touch(instance, TouchKind.Tap);
        Assert.True(context.IsFullscreen);
        await FakeDiscord.WaitUntilAsync(() => instance.Members is not null);
        Assert.Equal(4, instance.Members!.Members.Count);
        Assert.Equal("bruno", instance.Members.Members[0].Name);

        await Touch(instance, TouchKind.Tap);
        Assert.False(context.IsFullscreen);
    }

    [Fact]
    public async Task Swipe_passa_de_pagina_so_quando_tem_mais_de_uma()
    {
        var members = string.Join(",", Enumerable.Range(0, 30).Select(i =>
            $$"""{ "id": "{{i}}", "username": "p{{i:00}}", "status": "online" }"""));
        _body = $$"""{ "name": "Grande", "presence_count": 30, "channels": [], "members": [ {{members}} ] }""";
        var (instance, context) = await CreateAsync();
        await using var _ = instance;

        await Touch(instance, TouchKind.Tap);
        await FakeDiscord.WaitUntilAsync(() => instance.Members is not null);
        await Touch(instance, TouchKind.SwipeLeft);
        Assert.Equal(1, instance.Page);
        await Touch(instance, TouchKind.SwipeLeft); // não passa da última
        Assert.Equal(1, instance.Page);
        await Touch(instance, TouchKind.SwipeRight);
        Assert.Equal(0, instance.Page);

        await Touch(instance, TouchKind.SwipeDown);
        Assert.False(context.IsFullscreen);
    }

    [Fact]
    public async Task Widget_desligado_avisa_onde_ligar_e_tenta_de_novo_no_proximo_pedido()
    {
        _status = 403;
        _body = """{ "message": "Widget Disabled", "code": 50004 }""";

        await Assert.ThrowsAsync<GuildWidgetDisabledException>(() => _session.GetGuildWidgetAsync("g1"));

        // Falha não fica no cache: depois de ligar, o próximo pedido já traz a lista.
        _status = 200;
        _body = OnlineMembersTests.WidgetJson;
        var widget = await _session.GetGuildWidgetAsync("g1");
        Assert.Equal(4, widget.Members.Count);
        Assert.Equal(2, Volatile.Read(ref _widgetRequests));
    }

    [Fact]
    public async Task Lista_fica_em_cache_por_30_segundos()
    {
        await _session.GetGuildWidgetAsync("g1");
        await _session.GetGuildWidgetAsync("g1");
        Assert.Equal(1, Volatile.Read(ref _widgetRequests));
    }

    [Fact]
    public async Task Sem_tela_cheia_no_host_o_toque_rele_os_numeros_como_antes()
    {
        var (instance, context) = await CreateAsync();
        await using var _ = instance;
        context.AllowFullscreen = false;

        await Touch(instance, TouchKind.Tap);

        Assert.False(context.IsFullscreen);
        Assert.Null(instance.Members);
    }

    public async Task DisposeAsync()
    {
        DiscordButtonInstance.SessionOverride = null;
        await FakeDiscord.WaitUntilAsync(() => _session.State == DiscordState.Stopped);
        await _discord.DisposeAsync();
        _stop.Cancel();
        _api.Close();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
