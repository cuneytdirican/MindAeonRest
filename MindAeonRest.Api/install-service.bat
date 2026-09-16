@echo off
REM MindAeonRest Mobil API'yi Windows Hizmeti olarak kurar (otomatik baslar).
REM YONETICI olarak calistirin (sag tik -> Yonetici olarak calistir).
echo Guvenlik duvari (5000 portu, tum aglar) aciliyor...
netsh advfirewall firewall delete rule name="MindAeonRest.Api" >nul 2>&1
netsh advfirewall firewall delete rule name="MindAeonRest Mobil API" >nul 2>&1
netsh advfirewall firewall add rule name="MindAeonRest Mobil API" dir=in action=allow protocol=TCP localport=5000 profile=any
echo MindAeonRest Mobil API hizmeti kuruluyor...
sc create MindAeonRestApi binPath= "%~dp0MindAeonRest.Api.exe" start= auto DisplayName= "MindAeonRest Mobil API"
sc description MindAeonRestApi "MindAeonRest mobil uygulama (garson/mutfak/patron) API sunucusu"
sc start MindAeonRestApi
echo.
echo Tamamlandi. Hizmet durumu:
sc query MindAeonRestApi
pause
