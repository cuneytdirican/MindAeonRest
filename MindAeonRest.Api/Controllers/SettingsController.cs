using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

[Route("api/settings")]
public class SettingsController : PosControllerBase
{
    /// <summary>Şirketin ekran sürümü (Adisyon/Mutfak: Varsayilan|V2). MAUI hangi ekranları göstereceğini buradan öğrenir.</summary>
    [HttpGet("ui")]
    public ActionResult<object> Ui()
    {
        var s = new CompanyUiSettingsService().Get();
        return Ok(new
        {
            adisyonVersion = s.AdisyonVersion,
            mutfakVersion = s.MutfakVersion,
            adisyonV2 = s.IsAdisyonV2,
            mutfakV2 = s.IsMutfakV2
        });
    }

    /// <summary>Masa Notu hızlı chip ön-tanımları (genel + alerji).</summary>
    [HttpGet("note-presets")]
    public ActionResult<object> NotePresets() =>
        Ok(new { general = MindAeonRest.Models.NotePresets.General, allergy = MindAeonRest.Models.NotePresets.Allergy });
}
