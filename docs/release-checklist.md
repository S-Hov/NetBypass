# Release Checklist

## v1.0.0

Перед публикацией:

- убедиться, что рабочая копия Git чистая;
- выполнить `dotnet test`;
- выполнить `dotnet build`;
- выполнить `git diff --check`;
- собрать релиз через `scripts/build-release.ps1 -Version 1.0.0`;
- проверить, что в portable ZIP есть `NetBypass.exe`, `Profiles`, `RelayPools`
  и legacy-каталог `Modules`;
- приложить к GitHub Release portable ZIP, установщик и `SHA256SUMS.txt`;
- отметить релиз как pre-release, если нет code-signing сертификата;
- в описании релиза явно указать, что NetBypass v1.0.0 не является VPN и пока
  не содержит внешних Anti-DPI-движков.

После публикации:

- создать тег `v1.0.0`;
- проверить скачивание артефактов с GitHub Releases;
- сверить SHA256 из `SHA256SUMS.txt`;
- открыть приложение на чистой Windows x64 и проверить включение/отключение
  тестового набора сервисов.
