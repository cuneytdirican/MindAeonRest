using System.Speech.Recognition;
using System.Speech.Synthesis;

namespace MindAeonRest.Helpers;

/// <summary>Sesli yanıt (TTS) ve çevrimdışı konuşma tanıma (STT) — Windows System.Speech (offline).
/// TTS: Türkçe ses varsa onu seçer. STT: sistemde dikte motoru (tercihen tr-TR) yüklüyse çalışır,
/// yoksa best-effort olarak çökmeden onError ile geri döner.</summary>
public static class SpeechService
{
    private static SpeechSynthesizer? _tts;
    public static bool Available { get; private set; } = true;

    // ---- STT (konuşma→metin, çevrimdışı) ----
    private static SpeechRecognitionEngine? _stt;
    private static Action<string>? _onResult;

    /// <summary>Şu an mikrofon dinleniyor mu?</summary>
    public static bool IsListening => _stt != null;

    public static void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            _tts ??= Init();
            _tts.SpeakAsyncCancelAll();
            _tts.SpeakAsync(text);
        }
        catch { Available = false; /* ses motoru yoksa sessiz geç */ }
    }

    public static void Stop()
    {
        try { _tts?.SpeakAsyncCancelAll(); } catch { }
    }

    private static SpeechSynthesizer Init()
    {
        var s = new SpeechSynthesizer();
        try
        {
            var tr = s.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("tr", StringComparison.OrdinalIgnoreCase));
            if (tr != null) s.SelectVoice(tr.VoiceInfo.Name);
        }
        catch { /* varsayılan ses */ }
        return s;
    }

    /// <summary>Çevrimdışı konuşma tanımayı başlatır. Tanınan metin <paramref name="onResult"/> ile
    /// (arka plan iş parçacığından) döner. Uygun dikte motoru yoksa veya başlatma başarısızsa
    /// <paramref name="onError"/> ile kibar bir mesaj döner ve ÇÖKMEZ. true = dinleme başladı.</summary>
    public static bool StartListening(Action<string> onResult, Action<string>? onError = null)
    {
        StopListening();
        try
        {
            var engine = TryCreateRecognizer();
            if (engine == null)
            {
                onError?.Invoke("Çevrimdışı konuşma tanıma için sistemde uygun bir dikte motoru bulunamadı. " +
                                "Windows 'Konuşma' ayarlarından (tercihen Türkçe) dil paketini yükleyin.");
                return false;
            }
            _onResult = onResult;
            _stt = engine;
            _stt.SetInputToDefaultAudioDevice();
            _stt.LoadGrammar(new DictationGrammar());
            _stt.SpeechRecognized += Stt_Recognized;
            _stt.RecognizeAsync(RecognizeMode.Multiple);
            return true;
        }
        catch (Exception ex)
        {
            StopListening();
            onError?.Invoke("Konuşma tanıma başlatılamadı: " + ex.Message);
            return false;
        }
    }

    /// <summary>Dinlemeyi durdurur ve tanıma motorunu serbest bırakır. Her zaman güvenli çağrılabilir.</summary>
    public static void StopListening()
    {
        _onResult = null;
        var engine = _stt;
        _stt = null;
        if (engine == null) return;
        try { engine.SpeechRecognized -= Stt_Recognized; } catch { }
        try { engine.RecognizeAsyncCancel(); } catch { }
        try { engine.Dispose(); } catch { }
    }

    private static void Stt_Recognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var cb = _onResult;
        var text = e.Result?.Text;
        if (cb != null && !string.IsNullOrWhiteSpace(text)) cb(text!);
    }

    private static SpeechRecognitionEngine? TryCreateRecognizer()
    {
        try
        {
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            if (installed == null || installed.Count == 0) return null;
            var info = installed.FirstOrDefault(r => r.Culture.Name.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
                       ?? installed[0];
            return new SpeechRecognitionEngine(info);
        }
        catch { return null; }
    }
}
