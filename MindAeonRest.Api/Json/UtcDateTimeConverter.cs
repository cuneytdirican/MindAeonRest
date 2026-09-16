using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindAeonRest.Api.Json;

/// <summary>
/// Tüm DateTime değerlerini JSON'a UTC ISO-8601 ('Z' son ekli) olarak yazar.
/// PROBLEM: DB'den okunan zaman damgaları (SYSUTCDATETIME → UTC) Kind=Unspecified gelir; System.Text.Json
/// bunları 'Z' EKLEMEDEN serileştirir. İstemci (PWA/MAUI JS) <c>new Date(iso)</c> ile 'Z'siz metni YEREL sanar →
/// GMT+3'te her yeni kayıt anında "+180 dk" gecikmiş görünür. Bu converter Unspecified'ı UTC kabul edip 'Z' ile
/// yazarak tüm geçen-süre/tarih hesaplarını tek noktadan düzeltir. (DB konvansiyonu: her zaman UTC saklanır.)
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    // 7 haneli saniye-altı + Z. Round-trip'e uygun, istemci Date parse'ı ile uyumlu.
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dt = reader.GetDateTime();
        // Gelen değeri UTC olarak normalize et ('Z' ile geldiyse zaten UTC'ye çevrilir; Unspecified'ı UTC say).
        return dt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : dt.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        DateTime utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Unspecified → DB konvansiyonu gereği UTC kabul edilir (yerel olarak yorumlanmaz).
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        writer.WriteStringValue(utc.ToString(Format, CultureInfo.InvariantCulture));
    }
}

/// <summary><see cref="UtcDateTimeConverter"/>'ın nullable karşılığı (DateTime? alanlar için).</summary>
public sealed class NullableUtcDateTimeConverter : JsonConverter<DateTime?>
{
    private static readonly UtcDateTimeConverter Inner = new();

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : Inner.Read(ref reader, typeof(DateTime), options);

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        Inner.Write(writer, value.Value, options);
    }
}
