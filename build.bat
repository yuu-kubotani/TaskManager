@echo off
chcp 65001 >nul
setlocal

cd /d "%~dp0"

echo [0/4] 実行中の TaskManager.exe を終了しています...
taskkill /F /IM TaskManager.exe 2>nul
timeout /t 1 /nobreak >nul

echo [1/4] C#コンパイラ (csc.exe) を準備しています...

rem 完全にオフラインでビルドするため、Windows標準搭載のコンパイラを使用します
set CSC="%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% set CSC="%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist %CSC% (
    echo エラー: C#コンパイラが見つかりません。Windowsの標準機能が不足しています。
    pause
    exit /b 1
)

echo 使用するコンパイラ: %CSC%
set TOOLS_DIR=%~dp0Tools
if not exist "%TOOLS_DIR%" mkdir "%TOOLS_DIR%"

echo [2/4] 不要なファイルや重複する古いソースコードをクリーンアップしています...

rem 使われなくなった古い画面ファイル
if exist "ArchiveViewForm.cs" del /f /q "ArchiveViewForm.cs"
if exist "EventInputForm.cs" del /f /q "EventInputForm.cs"
if exist "MemoInputForm.cs" del /f /q "MemoInputForm.cs"
if exist "ReportForm.cs" del /f /q "ReportForm.cs"
if exist "SettingsForm.cs" del /f /q "SettingsForm.cs"
if exist "TaskInputForm.cs" del /f /q "TaskInputForm.cs"
if exist "FormTimeLogInput.cs" del /f /q "FormTimeLogInput.cs"

rem 命名規則の統一 (リネーム)
if exist "Src\Forms\TimeLogEntryForm.cs" move /Y "Src\Forms\TimeLogEntryForm.cs" "Src\Forms\FormTimeLogEntry.cs"

rem 移行済みの古いデータモデル・リポジトリ
if exist "RecurringRule.cs" del /f /q "RecurringRule.cs"
if exist "TaskItem.cs" del /f /q "TaskItem.cs"
if exist "TimeLog.cs" del /f /q "TimeLog.cs"
if exist "WorkFile.cs" del /f /q "WorkFile.cs"
if exist "EventItem.cs" del /f /q "EventItem.cs"
if exist "ProjectItem.cs" del /f /q "ProjectItem.cs"
if exist "CsvRepository.cs" del /f /q "CsvRepository.cs"
if exist "JsonRepository.cs" del /f /q "JsonRepository.cs"

rem ゴミファイル・不要なバッチ
if exist "MainForm.cs" del /f /q "MainForm.cs"
if exist "0" del /f /q "0"
if exist "file_list.txt" del /f /q "file_list.txt"
if exist "start.bat" del /f /q "start.bat"
if exist "UndoCommands.cs" del /f /q "UndoCommands.cs"
if exist "NativeMethods.cs" del /f /q "NativeMethods.cs"
if exist "Run.bat" del /f /q "Run.bat"

rem 古いFormMain.csがルートに残っていると上書きされてしまうため削除
if exist "FormMain.cs" del /f /q "FormMain.cs"

rem 古い環境のルート直下にあったツール・中間ファイルをクリーンアップ
if exist "nuget.exe" del /f /q "nuget.exe"
if exist "packages" rmdir /s /q "packages"
if exist "compiled_files_list.txt" del /f /q "compiled_files_list.txt"

echo [2.5/4] アプリケーションのメタデータ (AssemblyInfo) を生成しています...
if not exist "Src\Properties" mkdir "Src\Properties"

echo using System.Reflection; > "Src\Properties\AssemblyInfo.cs"
echo using System.Runtime.InteropServices; >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyTitle("TaskManager")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyDescription("Task Management Application")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyConfiguration("")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyCompany("Personal Project")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyProduct("TaskManager")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyCopyright("Copyright 2024")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyTrademark("")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyCulture("")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: ComVisible(false)] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: Guid("A1B2C3D4-E5F6-7890-1234-567890ABCDEF")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyVersion("1.0.0.0")] >> "Src\Properties\AssemblyInfo.cs"
echo [assembly: AssemblyFileVersion("1.0.0.0")] >> "Src\Properties\AssemblyInfo.cs"

echo [3/4] コンパイル対象のC#ファイル一覧を出力しています...

dir /s /b Src\*.cs > "%TOOLS_DIR%\compiled_files_list.txt"

echo [4/4] アプリケーションをビルドしています...
%CSC% /nologo /codepage:65001 /target:winexe /out:TaskManager.exe /reference:System.Windows.Forms.dll,System.Drawing.dll,System.Web.Extensions.dll,System.IO.Compression.FileSystem.dll,System.Windows.Forms.DataVisualization.dll /recurse:Src\*.cs

if %errorlevel% neq 0 (
    echo =========================================
    echo コンパイルに失敗しました。
    echo =========================================
    pause
    exit /b %errorlevel%
)

echo [5/5] セキュリティのブロック状態を自動解除しています...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Unblock-File -LiteralPath '%~dp0TaskManager.exe' -ErrorAction SilentlyContinue"

echo ビルド成功！ TaskManager.exe が作成されました。
echo.
echo アプリケーションを自動起動します...
rem エクスプローラー経由で起動することで、SmartScreenの過剰な警告を回避しやすくします
explorer.exe "%~dp0TaskManager.exe"

pause