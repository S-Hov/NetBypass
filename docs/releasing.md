# Сборка и публикация релиза

## Требования

- Windows x64;
- .NET 10 SDK;
- Inno Setup 6;
- чистая рабочая копия проекта.

## Локальная сборка

Из корня проекта:

```powershell
.\scripts\build-release.ps1 -Version 0.2.0
```

Скрипт:

1. запускает тесты в конфигурации Release;
2. публикует self-contained приложение для `win-x64`;
3. создаёт portable ZIP;
4. собирает установщик через Inno Setup.

Готовые файлы появляются в `artifacts\release`:

```text
NetBypass-v0.2.0-win-x64-portable.zip
NetBypass-Setup-v0.2.0-win-x64.exe
SHA256SUMS.txt
```

Portable-архив нужно распаковать целиком: рядом с `NetBypass.exe` находятся
нативные WPF-библиотеки и каталог `Modules`.

## Сборка через интерфейс Inno Setup

1. Запустите Inno Setup Compiler.
2. Выберите **Open an existing script file**.
3. Нажмите **More files...**.
4. Откройте `installer\NetBypass.iss`.
5. Сначала выполните `scripts\build-release.ps1`, чтобы создать publish-папку.
6. Нажмите **Build → Compile** или клавишу `Ctrl+F9`.

## GitHub Release

Перед выпуском отправьте изменения:

```powershell
git push origin main
```

Создайте и отправьте тег:

```powershell
git tag -a v0.2.0 -m "NetBypass v0.2.0"
git push origin v0.2.0
```

На странице GitHub откройте **Releases → Draft a new release**, выберите тег и
добавьте оба файла из `artifacts\release`.
Также приложите `SHA256SUMS.txt`, чтобы скачанные сборки можно было проверить.

Ранние версии следует отмечать как **pre-release**.

Перед удалением установленной программы рекомендуется открыть NetBypass и
нажать **Отключить**. Установщик удаляет файлы приложения, но не должен
самостоятельно менять системный `hosts` без подтверждения пользователя.

## SmartScreen

Пока приложение не подписано code-signing сертификатом, Windows может показать
предупреждение о неизвестном издателе. Пользователь должен скачивать сборки
только со страницы Releases официального репозитория.
