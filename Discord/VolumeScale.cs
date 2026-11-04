namespace WigiDash.DiscordWidget.Discord;

/// <summary>
/// O RPC do Discord fala o volume em amplitude, e o controle deslizante do Discord mostra outra escala (perceptual).
/// Até 100%, o controle cobre de -50 dB a 0 dB em linha; de 100% a 200%, de 0 a +6 dB. Por isso 200% chega como
/// 199,5 e 50% chega como 5,6. Os widgets mostram e ajustam no percentual do controle e mandam a amplitude.
/// </summary>
public static class VolumeScale
{
    private const double Normal = 100;

    /// <summary>Amplitude do RPC → percentual do controle do Discord.</summary>
    public static double ToPercent(double amplitude)
    {
        if (amplitude <= 0)
            return 0;
        var db = 20 * Math.Log10(amplitude / Normal);
        var percent = db > 0 ? db / 6 * Normal + Normal : (50 + db) / 50 * Normal;
        return Math.Clamp(percent, 0, 2 * Normal);
    }

    /// <summary>Percentual do controle do Discord → amplitude do RPC.</summary>
    public static double ToAmplitude(double percent)
    {
        if (percent <= 0)
            return 0;
        var db = percent > Normal ? (percent - Normal) / Normal * 6 : percent / Normal * 50 - 50;
        return Normal * Math.Pow(10, db / 20);
    }
}
