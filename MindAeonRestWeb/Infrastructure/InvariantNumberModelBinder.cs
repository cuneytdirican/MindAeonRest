using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MindAeonRestWeb.Infrastructure;

/// <summary>
/// HTML5 <c>&lt;input type="number"&gt;</c> girdileri değeri HER ZAMAN InvariantCulture ile ('.' ondalık,
/// binlik ayraç YOK) POST eder. Sayfa kültürü tr-TR olduğundan, VARSAYILAN model binding '.'yi BİNLİK
/// ayraç sanar ve fiyatı ölçekler: "125.00" → 12500, "125.0000" → 1.250.000. Her kayıt-turunda ×10ⁿ
/// büyür (WPF'te girilen 125 web'de milyonlara çıkar).
///
/// Bu binder decimal/double/float alanlarını önce InvariantCulture ile ('.' ondalık) ayrıştırır; başarısızsa
/// (kullanıcı tr-TR metin alanına "125,50" ya da "1.250,50" yazmışsa) tr-TR ile ayrıştırır. Böylece
/// type="number" girdileri doğru bağlanır ve elle tr-TR giriş de desteklenir. Manuel parse yapan sayfalar
/// (PriceMatrix / BulkPrice / Dekont — type="text") model binding kullanmadığından bundan etkilenmez.
/// </summary>
public sealed class InvariantNumberModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext ctx)
    {
        var name = ctx.ModelName;
        var value = ctx.ValueProvider.GetValue(name);
        if (value == ValueProviderResult.None) return Task.CompletedTask;

        ctx.ModelState.SetModelValue(name, value);
        var raw = value.FirstValue;
        var target = Nullable.GetUnderlyingType(ctx.ModelType) ?? ctx.ModelType;
        bool nullable = Nullable.GetUnderlyingType(ctx.ModelType) is not null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            // Boş değer: nullable → null; değilse bağlama yok (varsayılan 0 kalır).
            if (nullable) ctx.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        object? parsed = null;
        bool ok = false;

        if (target == typeof(decimal))
        { ok = Num.TryParseDecimal(raw, out var d); parsed = d; }
        else if (target == typeof(double))
        { ok = Num.TryParseDouble(raw, out var d); parsed = d; }
        else if (target == typeof(float))
        { ok = Num.TryParseDouble(raw, out var d); parsed = (float)d; }

        if (ok) ctx.Result = ModelBindingResult.Success(parsed);
        else ctx.ModelState.TryAddModelError(name, "Geçersiz sayı biçimi.");
        return Task.CompletedTask;
    }
}

/// <summary>decimal/double/float (ve nullable karşılıkları) için <see cref="InvariantNumberModelBinder"/> sağlar.</summary>
public sealed class InvariantNumberModelBinderProvider : IModelBinderProvider
{
    private static readonly InvariantNumberModelBinder Binder = new();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var t = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) ? Binder : null;
    }
}
