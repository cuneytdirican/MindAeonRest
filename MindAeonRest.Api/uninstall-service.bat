@echo off
REM MindAeonRest Mobil API Windows Hizmetini durdurur ve kaldirir.
REM YONETICI olarak calistirin.
echo MindAeonRest Mobil API hizmeti kaldiriliyor...
sc stop MindAeonRestApi
sc delete MindAeonRestApi
echo.
echo Tamamlandi.
pause
