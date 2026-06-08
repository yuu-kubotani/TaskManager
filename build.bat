@echo off
chcp 65001 >nul
setlocal

cd /d "%~dp0"

echo [0/4] 実行中の UniConsul.exe を終了しています...
taskkill /F /IM UniConsul.exe 2>nul
timeout /t 1 /nobreak >nul

echo [0.5/4] 以前のビルドキャッシュをクリアしています...
dotnet clean UniConsul.csproj -c Release

echo [1/2] アプリケーションを発行しています (自己完結型 単一ファイル)...
dotnet publish UniConsul.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

if %errorlevel% neq 0 (
    echo =========================================
    echo コンパイルに失敗しました。
    echo =========================================
    pause
    exit /b %errorlevel%
)

echo [2/2] 発行成功！ アプリケーションを起動します...
echo.

rem 完成したアプリケーションを起動します
start "" "bin\Release\net8.0-windows\win-x64\publish\UniConsul.exe"

pause