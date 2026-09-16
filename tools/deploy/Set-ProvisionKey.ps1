<#
.SYNOPSIS
  Portal (Pos:ProvisionKey) ile POS Web (Provisioning:ApiKey) arasındaki PAYLAŞILAN provisioning
  anahtarını üretir/döndürür ve iki sitenin appsettings.Production.json dosyasına yazar.

.DESCRIPTION
  - Anahtar kripto-RNG ile üretilir; ekrana/loga YAZILMAZ.
  - Portal'ın mevcut appsettings.Production.json içeriği korunur (yalnız "Pos" bölümü eklenir/güncellenir),
    önce .bak-<zaman> yedeği alınır. Web tarafında "Provisioning" bölümü eklenir/güncellenir.
  - appsettings.Production.json publish çıktısında yoktur → yeni sürüm kopyalandığında EZİLMEZ.
  - Uygulamalar reloadOnChange ile anahtarı yeniden başlatma olmadan alır.
  - Doğrulama: anahtarsız 401, anahtarlı 200 beklenir (503 = POS tarafında anahtar tanımsız).
  - Sunucuda (C:\inetpub\vhosts\...) ya da sunucu diski map'lenmiş makinede (-Root Z:\inetpub\vhosts\...) çalışır.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Set-ProvisionKey.ps1
  powershell -ExecutionPolicy Bypass -File .\Set-ProvisionKey.ps1 -Root "Z:\inetpub\vhosts\mobilteg.com"
#>
[CmdletBinding()]
param(
    [string]$Root       = "C:\inetpub\vhosts\mobilteg.com",
    [string]$WebHost    = "mindaeonrestweb.mobilteg.com",
    [string]$PortalHost = "mindaeonrest.mobilteg.com"
)

$ErrorActionPreference = "Stop"
$webDir    = Join-Path $Root $WebHost
$portalDir = Join-Path $Root $PortalHost
foreach ($d in $webDir, $portalDir) { if (-not (Test-Path $d)) { throw "Klasör bulunamadı: $d" } }

$bytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$key = [Convert]::ToBase64String($bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')
$utf8 = New-Object Text.UTF8Encoding($false)
$stamp = Get-Date -Format yyyyMMddHHmmss

function Set-Section([string]$dir, [string]$section, [pscustomobject]$value) {
    $file = Join-Path $dir "appsettings.Production.json"
    $json = [pscustomobject]@{}
    if (Test-Path $file) {
        Copy-Item $file "$file.bak-$stamp"
        $json = Get-Content $file -Raw | ConvertFrom-Json
    }
    $json | Add-Member -NotePropertyName $section -NotePropertyValue $value -Force
    [IO.File]::WriteAllText($file, ($json | ConvertTo-Json -Depth 10), $utf8)
    Write-Host "  [OK] $file -> $section" -ForegroundColor Green
}

Set-Section $webDir    "Provisioning" ([pscustomobject]@{ ApiKey = $key })
Set-Section $portalDir "Pos"          ([pscustomobject]@{ ProvisionUrl = "https://$WebHost"; ProvisionKey = $key })

Start-Sleep -Seconds 3
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$url = "https://$WebHost/api/provision/tenants"
function Get-Status([hashtable]$headers) {
    try { return [int](Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 60 -Headers $headers).StatusCode }
    catch { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode } else { return -1 } }
}
$noKey   = Get-Status @{}
$withKey = Get-Status @{ "X-Provision-Key" = $key }
$key = $null

Write-Host "`nAnahtarsız istek : $noKey (beklenen 401)"
Write-Host "Anahtarlı istek  : $withKey (beklenen 200)"
if ($noKey -ne 401 -or $withKey -ne 200) {
    Write-Host "Doğrulama başarısız. Gerekirse site app pool'unu geri dönüştürüp tekrar deneyin." -ForegroundColor Yellow
    exit 1
}
Write-Host "BAŞARILI: https://$PortalHost/Admin/NewTenant tenant oluşturabilir." -ForegroundColor Green
