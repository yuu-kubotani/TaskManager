Set-ExecutionPolicy RemoteSigned -Scope Process -Force
# ===================================================================
# メインスクリプトファイル (v12.1)
# UIの定義とイベントハンドリング
# ===================================================================

# --- スクリプトの初期設定 ---
# スクリプトのパスを基準にカレントディレクトリを設定
# $MyInvocation.MyCommand.Definition は実行環境によって空になることがあるため、
# PSScriptRoot や現在のカレントディレクトリをフォールバックとして使う。
$scriptRoot = $null
try {
    if ($MyInvocation -and $MyInvocation.MyCommand -and $MyInvocation.MyCommand.Definition) {
        $scriptRoot = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
    }
} catch {}

if (-not $scriptRoot -or [string]::IsNullOrWhiteSpace($scriptRoot)) {
    if ($PSScriptRoot -and -not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $scriptRoot = $PSScriptRoot
    } else {
        try { $scriptRoot = (Get-Location).ProviderPath } catch { $scriptRoot = '.' }
    }
}

try { Set-Location -Path $scriptRoot } catch { $scriptRoot = "."; Set-Location -Path $scriptRoot }
$script:AppRoot = $scriptRoot

# --- .NETアセンブリの読み込みとWin32 APIの定義 ---
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName Microsoft.VisualBasic
try {
    Add-Type -AssemblyName System.Windows.Forms.DataVisualization
} catch {
    $logMessage = "致命的なエラー: グラフ描画ライブラリの読み込みに失敗しました。レポート機能は使用できません。`n$($_.Exception.Message)"
    Write-Warning $logMessage
    Add-Content -Path (Join-Path -Path $scriptRoot -ChildPath "debug_day_info.log") -Value $logMessage -Encoding UTF8
    [System.Windows.Forms.MessageBox]::Show($logMessage, "ライブラリ読み込みエラー", "OK", "Error")
}

# --- Win32 APIの定義 (修正・統合版) ---
try {
    Add-Type -TypeDefinition @'
    using System;
    using System.Runtime.InteropServices;

    namespace TaskManager.WinAPI {
        public class User32 {
            [StructLayout(LayoutKind.Sequential)]
            public struct LASTINPUTINFO {
                public uint cbSize;
                public uint dwTime;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
            public struct SHFILEINFO {
                public IntPtr hIcon;
                public int iIcon;
                public uint dwAttributes;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string szDisplayName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
                public string szTypeName;
            }

            [DllImport("user32.dll")]
            public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

            [DllImport("shell32.dll", CharSet=CharSet.Auto)]
            public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DestroyIcon(IntPtr hIcon);
        }

        public class Dwmapi {
            [DllImport("dwmapi.dll", PreserveSig = true)]
            public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        }

        public class UxTheme {
            [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
            public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);
        }
    }
'@ -PassThru -ErrorAction Stop
} catch [System.Management.Automation.RuntimeException] {
    if ($_.Exception.Message -notlike "*型名*は既に存在しています*") { throw }
}

# --- ダークモード用レンダラーの定義 ---
try {
    Add-Type -TypeDefinition @'
    using System.Windows.Forms;
    using System.Drawing;

    public class DarkModeColorTable : ProfessionalColorTable {
        public override Color MenuItemSelected { get { return Color.FromArgb(80, 80, 80); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(80, 80, 80); } }
        public override Color MenuBorder { get { return Color.FromArgb(50, 50, 50); } }
        public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(60, 60, 60); } }
        public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(60, 60, 60); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(30, 30, 30); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(30, 30, 30); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(30, 30, 30); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(30, 30, 30); } }
        public override Color ButtonSelectedHighlight { get { return Color.FromArgb(80, 80, 80); } }
        public override Color ButtonPressedHighlight { get { return Color.FromArgb(100, 100, 100); } }
        public override Color ButtonCheckedHighlight { get { return Color.FromArgb(100, 100, 100); } }
        public override Color ButtonSelectedBorder { get { return Color.FromArgb(100, 100, 100); } }
        public override Color SeparatorDark { get { return Color.FromArgb(80, 80, 80); } }
        public override Color SeparatorLight { get { return Color.FromArgb(80, 80, 80); } }
    }

    public class DarkModeRenderer : ToolStripProfessionalRenderer {
        public DarkModeRenderer() : base(new DarkModeColorTable()) {}
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Color.White; base.OnRenderItemText(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Color.White; base.OnRenderArrow(e); }
    }
'@ -ReferencedAssemblies System.Windows.Forms, System.Drawing -ErrorAction Stop
} catch {}

# --- グローバル変数の定義 ---
$script:TasksFile = Join-Path -Path $scriptRoot -ChildPath "tasks.csv"
$script:ProjectsFile = Join-Path -Path $scriptRoot -ChildPath "projects.json"
$script:CategoriesFile = Join-Path -Path $scriptRoot -ChildPath "categories.json"
$script:TemplatesFile = Join-Path -Path $scriptRoot -ChildPath "templates.json"
$script:SettingsFile = Join-Path -Path $scriptRoot -ChildPath "config.json"
$script:BackupsFolder = Join-Path -Path $scriptRoot -ChildPath "backup"
$script:notifiedLogPath = Join-Path -Path $scriptRoot -ChildPath "notified.log"
$script:EventsFile = Join-Path -Path $scriptRoot -ChildPath "events.json"
$script:TimeLogsFile = Join-Path -Path $scriptRoot -ChildPath "timelogs.json"
$script:StatusLogsFile = Join-Path -Path $scriptRoot -ChildPath "status_logs.json"

# -----------------------------------------------------------------
# ユーザーが自由に編集できる「分析ルール設定」
# -----------------------------------------------------------------
$script:AnalysisSettings = @{
    # 1. 時間配分の閾値 (%)
    "UnclassifiedCriticalPercent" = 40  # 「未分類」がこれ以上だと"最重要課題"
    "UnclassifiedWarningPercent"  = 15  # 「未分類」がこれ以上だと"課題"
    "WorkLowPercent"              = 5   # 「仕事」がこれ未満だと指摘
    
    # 2. タスク効率の閾値 (日数)
    "TaskLongTermDays"      = 60  # これ以上だと"長期停滞タスク"
    "TaskMediumTermDays"    = 21  # これ以上だと"やや遅いタスク"
    "TaskEfficientDays"     = 14  # 全タスクがこれ未満なら"効率的"
    "CategorySlowDays"      = 30  # カテゴリ平均がこれ以上だと"カテゴリ停滞"
    
    # 3. データ信頼性の閾値
    "UnknownCriticalPercent" = 30  # 「不明」がこれ以上だと"最重要課題"
    "UnknownWarningPercent"  = 10  # 「不明」がこれ以上だと"課題"
    "ZeroDayTaskCount"       = 5   # 0.0日タスクがこれ以上あれば指摘
}

$script:AllTasks = @()
$script:Projects = @()
$script:Categories = @{}
$script:Templates = @{}
$script:Settings = @{}
$script:AllTimeLogs = @() # 時間ログオブジェクトの配列
$script:TaskStatuses = @("未実施", "保留", "実施中", "確認待ち", "完了済み")
$script:CurrentCategoryFilter = "(すべて)"
$script:isClearingSelections = $false
$script:AllEvents = [PSCustomObject]@{}

$script:kanbanDragTask = $null
$script:kanbanDragStartPoint = New-Object System.Drawing.Point
$script:ProjectExpansionStates = @{}
$script:CategoryExpansionStates = @{}
$script:groupByProject = $true
$script:isCalendarNavEventsAttached = $false
$script:isCalendarViewDirty = $true
$script:isDarkMode = $false # 初期値。設定読み込み後に更新される

# タイムライン関連の変数
$script:timelinePaintHandler = $null
$script:isTimelineDragging = $false
$script:timelineDragStartY = 0
$script:timelineDragCurrentY = 0
$script:selectedTimeLog = $null
$script:selectedEvent = $null
$script:isDraggingFromPlan = $false
$script:dragItem = $null
$script:isResizingTimeLog = $false
$script:isResizingEvent = $false
$script:logToResize = $null
$script:resizeEdge = '' # 'top' or 'bottom'

$script:trackingTimer = New-Object System.Windows.Forms.Timer
$script:trackingTimer.Interval = 1000 # 1秒ごと
$script:currentlyTrackingTaskID = $null
$script:longTaskCheckSeconds = 0
$script:longTaskNotificationShown = $false

$script:idleCheckTimer = New-Object System.Windows.Forms.Timer
$script:idleCheckTimer.Interval = 30000 # 30秒ごと
$script:idleMessageShown = $false
$script:currentProjectStaticTime = $null

# --- UI用のフォント定義 (Disposeが必要) ---
$script:globalImageList = New-Object System.Windows.Forms.ImageList
$script:kanbanHeaderFont = New-Object System.Drawing.Font("Meiryo UI", 10, [System.Drawing.FontStyle]::Bold)
$script:calendarHeaderFont = New-Object System.Drawing.Font("Meiryo UI", 12, [System.Drawing.FontStyle]::Bold)
$script:previewFont = New-Object System.Drawing.Font("Consolas", 10)
$script:datagridRegularFont = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Regular)
$script:datagridStrikeoutFont = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Strikeout)
$script:calendarDayFont = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Regular) # Unused, but keep for consistency
$script:calendarDayBoldFont = New-Object System.Drawing.Font("Meiryo UI", 9.5, [System.Drawing.FontStyle]::Regular)
$script:calendarItemFont = New-Object System.Drawing.Font("Meiryo UI", 8.25, [System.Drawing.FontStyle]::Regular)
$script:calendarItemBoldFont = New-Object System.Drawing.Font("Meiryo UI", 8.25, [System.Drawing.FontStyle]::Bold)
$script:calendarItemStrikeoutFont = New-Object System.Drawing.Font("Meiryo UI", 8.25, [System.Drawing.FontStyle]::Strikeout)
$script:calendarGridHeaderFont = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Regular)
$script:dayInfoBoldFont = New-Object System.Drawing.Font("Meiryo UI", 11, [System.Drawing.FontStyle]::Bold)
$script:dayInfoRegularFont = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Regular)
$script:dayInfoItalicFont = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Italic)
$script:dayInfoCardTypeFont = New-Object System.Drawing.Font("Meiryo UI", 8, [System.Drawing.FontStyle]::Bold)
$script:dayInfoCardTitleFont = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Regular)
$script:dayInfoCardDetailsFont = New-Object System.Drawing.Font("Meiryo UI", 8, [System.Drawing.FontStyle]::Italic)

# --- 機能関数の読み込み ---
# 複数候補を試し、空パスを渡さないように堅牢化。詳細デバッグ出力を残す。
$script:functionsFile = $null
$script:isModule = $false

# Debug: dump loader state to log for troubleshooting
$logRoot = if ($scriptRoot -and -not [string]::IsNullOrWhiteSpace($scriptRoot)) { $scriptRoot } else { try { (Get-Location).ProviderPath } catch { '.' } }
$debugLog = Join-Path -Path $logRoot -ChildPath 'functions_loader_debug.log'
"--- Loader run at $(Get-Date -Format o) ---" | Out-File -FilePath $debugLog -Encoding UTF8 -Append
try { "scriptRoot: $scriptRoot" | Out-File -FilePath $debugLog -Encoding UTF8 -Append } catch {}
try { $loc = (Get-Location).ProviderPath; "cwd: $loc" | Out-File -FilePath $debugLog -Encoding UTF8 -Append } catch { "cwd: (Get-Location failed)" | Out-File -FilePath $debugLog -Encoding UTF8 -Append }

# 候補パスのリストを作成 (.psm1 を優先)
$candidatePaths = @(
    (Join-Path -Path $scriptRoot -ChildPath "task_manager_functions.psm1"),
    (Join-Path -Path $scriptRoot -ChildPath "task_manager_functions.ps1"),
    '.\task_manager_functions.psm1',
    '.\task_manager_functions.ps1'
) | Select-Object -Unique

# 候補パスを順番に試す
foreach ($path in $candidatePaths) {
    if (Test-Path -LiteralPath $path) {
        $script:functionsFile = $path
        $script:isModule = $path.EndsWith(".psm1")
        break
    }
}

try {
    if (-not $script:functionsFile) {
        throw "関数ファイル (task_manager_functions.psm1 または .ps1) が見つかりませんでした。"
    }

    if ($script:isModule) {
        # モジュールとして読み込む (.psm1)
        Import-Module -Name $script:functionsFile -Force
    } else {
        # スクリプトとしてドットソースで読み込む (.ps1)
        $functionsFullPath = (Resolve-Path -LiteralPath $script:functionsFile -ErrorAction Stop).ProviderPath

        # 読み込みロジックの修正: .NETクラスを使用してBOMとエンコーディングをより確実に処理
        try {
            $content = [System.IO.File]::ReadAllText($functionsFullPath, [System.Text.Encoding]::UTF8)
        } catch { $content = "" }

        if ($content -notmatch 'function|Get-Settings|通知設定|期日') {
            try {
                $content = [System.IO.File]::ReadAllText($functionsFullPath, [System.Text.Encoding]::Default)
            } catch { $content = "" }
        }

        if (-not [string]::IsNullOrWhiteSpace($content)) {
            # BOMやゴミ文字の除去
            $content = $content.Trim([char]0xfeff).Trim()
            if ($content.StartsWith("ï»¿")) { $content = $content.Substring(3) }
            . ([scriptblock]::Create($content))
        }
    }

} catch {
    # 詳細な例外情報をデバッグログに出力
    try {
        "--- Loader Exception at $(Get-Date -Format o) ---" | Out-File -FilePath $debugLog -Encoding UTF8 -Append
        "functionsFile: $script:functionsFile" | Out-File -FilePath $debugLog -Encoding UTF8 -Append
        try { ($_.Exception | Out-String) | Out-File -FilePath $debugLog -Encoding UTF8 -Append } catch {}
        try { ($_.Exception.StackTrace | Out-String) | Out-File -FilePath $debugLog -Encoding UTF8 -Append } catch {}
        if ($_.Exception.InnerException) { try { ($_.Exception.InnerException | Out-String) | Out-File -FilePath $debugLog -Encoding UTF8 -Append } catch {} }
    } catch {}

    $errorMessage = "関数ファイル '$($script:functionsFile)' の読み込みに失敗しました。`n`n$($_.Exception.Message)"
    Write-Warning $errorMessage
    try { [System.Windows.Forms.MessageBox]::Show($errorMessage, "読み込みエラー", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) } catch {}
    exit 1
}

# --- データの読み込み ---
try {
    $script:Settings = Get-Settings
    Start-AutomaticBackup
    Compress-OldArchives # 起動時に古いアーカイブを圧縮
    $script:Projects = Get-Projects
    $script:isDarkMode = if ($script:Settings.IsDarkMode) { $script:Settings.IsDarkMode } else { $false }

    # すべてのプロジェクトをデフォルトで展開状態にする
    foreach ($project in $script:Projects) {
        if ($project.ProjectID) { # 念のためProjectIDの存在を確認
            $script:ProjectExpansionStates[$project.ProjectID] = $true
        }
    }

    $script:Categories = Get-Categories
    $script:Templates = Get-Templates
    $script:AllEvents = Get-Events
    $script:AllTimeLogs = Get-TimeLogs
    $script:AllTasks = Read-TasksFromCsv -filePath $script:TasksFile

    if ($null -eq $script:AllTasks) {
        throw "タスクデータの読み込みに失敗しました。"
    }
} catch {
    $errorMessage = $_.Exception.Message
    $result = [System.Windows.Forms.MessageBox]::Show("データの読み込み中にエラーが発生しました。`n`nエラー内容:`n$errorMessage`n`nバックアップから復元を試みますか？", "起動エラー", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Error)
    
    if ($result -eq 'Yes') {
        if (Invoke-RestoreFromBackup) {
            # 復元後にデータを再読み込み
            try {
                $script:Settings = Get-Settings
                $script:Projects = Get-Projects
                $script:Categories = Get-Categories
                $script:Templates = Get-Templates
                $script:AllEvents = Get-Events
                $script:AllTimeLogs = Get-TimeLogs
                $script:AllTasks = Read-TasksFromCsv -filePath $script:TasksFile
                $script:isDarkMode = if ($script:Settings.IsDarkMode) { $script:Settings.IsDarkMode } else { $false }
                
                if ($null -eq $script:AllTasks) { throw "復元後のタスクデータ読み込みに失敗しました。" }
            } catch {
                [System.Windows.Forms.MessageBox]::Show("復元したデータの読み込みにも失敗しました。アプリケーションを終了します。`n$($_.Exception.Message)", "致命的なエラー", "OK", "Error")
                exit
            }
        } else {
            exit
        }
    } else {
        exit
    }
}
Invoke-AutoArchiving
Invoke-ProjectAutoArchiving
# --- メインフォームの作成 ---
$mainForm = New-Object System.Windows.Forms.Form
$mainForm.Text = "タスク管理マネージャー v12.0"
$mainForm.Width = 1280
# 変更点 1: フォームの高さを増やし、下部パネルの初期表示領域を確保
$mainForm.Height = 1024
$mainForm.StartPosition = "CenterScreen"

# --- メインメニューの定義 ---
$mainMenu = New-Object System.Windows.Forms.MenuStrip

# ファイル メニュー
$fileMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("ファイル(&F)")
$addNewTaskMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("プロジェクト／タスクの新規追加(&N)")
$addNewTaskMenuItem.ShortcutKeys = [System.Windows.Forms.Keys]::Control -bor [System.Windows.Forms.Keys]::N
$addNewEventMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("イベントの追加(&A)")
$backupRestoreMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("バックアップと復元(&B)")
# レポート関連のメニュー項目を定義
$reportMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("レポート(&R)")

$icsExchangeMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("カレンダー連携 (ICS)...")

$globalSettingsMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("全体設定(&O)")
$separator1 = New-Object System.Windows.Forms.ToolStripSeparator
$separator2 = New-Object System.Windows.Forms.ToolStripSeparator
$exitMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("終了(&X)")
$fileMenuItem.DropDownItems.AddRange(@($addNewTaskMenuItem, $addNewEventMenuItem, $separator1, $backupRestoreMenuItem, $reportMenuItem, $icsExchangeMenuItem, $globalSettingsMenuItem, $separator2, $exitMenuItem))

# 編集 メニュー
$editMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("編集(&E)")
$editCategoriesMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("カテゴリの編集(&C)")
$editTemplatesMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("テンプレートの編集(&M)")
$editMenuItem.DropDownItems.AddRange(@($editCategoriesMenuItem, $editTemplatesMenuItem))

# 表示 メニュー

$viewMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("表示(&V)")
$toggleFilesPanelMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("関連ファイルパネルの表示/非表示")

$script:hideCompletedMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("完了したタスクを隠す")
$script:hideCompletedMenuItem.CheckOnClick = $true
$script:hideCompletedMenuItem.Add_Click({
    $script:Settings | Add-Member -MemberType NoteProperty -Name 'HideCompletedTasks' -Value $script:hideCompletedMenuItem.Checked -Force
    Save-DataFile -filePath $script:SettingsFile -dataObject $script:Settings
    Update-AllViews
})

$script:hideCompletedMenuItem.Checked = [bool]$script:Settings.HideCompletedTasks

$darkModeMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("ダークモード")
$darkModeMenuItem.Checked = $script:isDarkMode
$darkModeMenuItem.CheckOnClick = $true
$darkModeMenuItem.Add_Click({
    $script:isDarkMode = $darkModeMenuItem.Checked
    Update-Theme -isDarkMode $script:isDarkMode
})

$groupingMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("表示方法の切替 (プロジェクト/カテゴリ)")
$groupingMenuItem.CheckOnClick = $true
$groupingMenuItem.Checked = $script:groupByProject
$groupingMenuItem.Add_Click({
    $script:groupByProject = $groupingMenuItem.Checked
    Update-AllViews
})

$script:showKanbanDoneMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("カンバンの完了列を表示")
$script:showKanbanDoneMenuItem.CheckOnClick = $true
$script:showKanbanDoneMenuItem.Checked = if ($script:Settings.PSObject.Properties.Name -contains 'ShowKanbanDone') { $script:Settings.ShowKanbanDone } else { $true }
$script:showKanbanDoneMenuItem.Add_Click({
    $script:Settings | Add-Member -MemberType NoteProperty -Name 'ShowKanbanDone' -Value $script:showKanbanDoneMenuItem.Checked -Force
    Save-DataFile -filePath $script:SettingsFile -dataObject $script:Settings
    Update-AllViews
})

$viewArchiveMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("アーカイブビューを開く...")
$viewArchiveMenuItem.Add_Click({
    if (Show-ArchiveViewForm -parentForm $mainForm -eq "RELOAD") {
        # Reload all data and update views
        $script:Projects = Get-Projects
        $script:AllTasks = Read-TasksFromCsv -filePath $script:TasksFile
        Update-AllViews
    }
})

$viewMenuItem.DropDownItems.AddRange(@($toggleFilesPanelMenuItem, $groupingMenuItem, (New-Object System.Windows.Forms.ToolStripSeparator), $script:hideCompletedMenuItem, $script:showKanbanDoneMenuItem, $darkModeMenuItem, (New-Object System.Windows.Forms.ToolStripSeparator), $viewArchiveMenuItem))

$mainMenu.Items.AddRange(@($fileMenuItem, $editMenuItem, $viewMenuItem))

# --- ツールバーの定義 ---
$toolStrip = New-Object System.Windows.Forms.ToolStrip
$toolStrip.ImageScalingSize = New-Object System.Drawing.Size(24, 24)
$toolStrip.AutoSize = $false
$toolStrip.Height = 48

$btnAdd = New-Object System.Windows.Forms.ToolStripButton "新規追加"
$btnAdd.Name = "新規追加"
$btnAddFromTemplate = New-Object System.Windows.Forms.ToolStripButton "テンプレートから追加"
$btnAddFromTemplate.Name = "テンプレートから追加"
$toolStripSeparator1 = New-Object System.Windows.Forms.ToolStripSeparator
$btnNotifications = New-Object System.Windows.Forms.ToolStripButton "🔔 通知"
$btnNotifications.Name = "通知"
$btnLatestReport = New-Object System.Windows.Forms.ToolStripButton "最新のレポート"
$btnLatestReport.Name = "最新のレポート"
$toolStripSeparator2 = New-Object System.Windows.Forms.ToolStripSeparator

# 右寄せするアイテム
$categoryFilterComboBox = New-Object System.Windows.Forms.ToolStripComboBox
$categoryFilterComboBox.Alignment = [System.Windows.Forms.ToolStripItemAlignment]::Right
$categoryFilterComboBox.Width = 150
$lblCategoryFilter = New-Object System.Windows.Forms.ToolStripLabel "カテゴリ絞り込み:"
$lblCategoryFilter.Alignment = [System.Windows.Forms.ToolStripItemAlignment]::Right

$toolStrip.Items.AddRange(@($btnAdd, $btnAddFromTemplate, $toolStripSeparator1, $btnNotifications, $btnLatestReport, $toolStripSeparator2, $categoryFilterComboBox, $lblCategoryFilter))

# コントロールの追加順序を入れ替え、ツールバーを一番上に表示する
$mainForm.Controls.Add($toolStrip)
$mainForm.Controls.Add($mainMenu)

# ツールバーボタンのアイコン設定 (AddRangeの後)
$btnNew = $toolStrip.Items["新規追加"]
$btnNew.Image = [System.Drawing.SystemIcons]::Application.ToBitmap()
$btnNew.DisplayStyle = [System.Windows.Forms.ToolStripItemDisplayStyle]::ImageAndText
$btnNew.TextImageRelation = [System.Windows.Forms.TextImageRelation]::ImageAboveText
$btnNew.AutoSize = $false
$btnNew.Size = New-Object System.Drawing.Size(70, 45)

$btnTemplate = $toolStrip.Items["テンプレートから追加"]
$btnTemplate.Image = [System.Drawing.SystemIcons]::Application.ToBitmap()
$btnTemplate.DisplayStyle = [System.Windows.Forms.ToolStripItemDisplayStyle]::ImageAndText
$btnTemplate.TextImageRelation = [System.Windows.Forms.TextImageRelation]::ImageAboveText
$btnTemplate.AutoSize = $false
$btnTemplate.Size = New-Object System.Drawing.Size(130, 45)

$btnNotify = $toolStrip.Items["通知"]
$btnNotify.Image = [System.Drawing.SystemIcons]::Information.ToBitmap()
$btnNotify.DisplayStyle = [System.Windows.Forms.ToolStripItemDisplayStyle]::ImageAndText
$btnNotify.TextImageRelation = [System.Windows.Forms.TextImageRelation]::ImageAboveText
$btnNotify.AutoSize = $false
$btnNotify.Size = New-Object System.Drawing.Size(70, 45)

$btnReport = $toolStrip.Items["最新のレポート"]
$btnReport.Image = [System.Drawing.SystemIcons]::Application.ToBitmap() # Placeholder icon
$btnReport.DisplayStyle = [System.Windows.Forms.ToolStripItemDisplayStyle]::ImageAndText
$btnReport.TextImageRelation = [System.Windows.Forms.TextImageRelation]::ImageAboveText
$btnReport.AutoSize = $false
$btnReport.Size = New-Object System.Drawing.Size(100, 45)

# --- ステータスバー ---
$statusBar = New-Object System.Windows.Forms.StatusStrip
$script:statusLabel = New-Object System.Windows.Forms.ToolStripStatusLabel "読み込み中..."
$statusBar.Items.Add($script:statusLabel)
$mainForm.Controls.Add($statusBar)

# --- メインコンテナ ---
$mainContainer = New-Object System.Windows.Forms.SplitContainer
$mainContainer.Dock = "Fill"
$mainContainer.Orientation = "Horizontal"
# 変更点 2: SplitterDistanceの初期値を調整。最終的な値はForm_Loadで設定。
$mainContainer.SplitterDistance = 600
$mainForm.Controls.Add($mainContainer)
$mainContainer.BringToFront()

# --- 上部パネル (タブコントロール) ---
$tabControl = New-Object System.Windows.Forms.TabControl
$tabControl.Dock = "Fill"
$tabControl.DrawMode = [System.Windows.Forms.TabDrawMode]::OwnerDrawFixed
$tabControl.Add_DrawItem({
    param($source, $e)
    $g = $e.Graphics
    $tabs = $source
    if ($e.Index -ge $tabs.TabPages.Count) { return }
    $tabPage = $tabs.TabPages[$e.Index]
    $tabRect = $tabs.GetTabRect($e.Index)
    
    $isSelected = ($e.State -band [System.Windows.Forms.DrawItemState]::Selected) -eq [System.Windows.Forms.DrawItemState]::Selected
    
    if ($script:isDarkMode) {
        $bgColor = if ($isSelected) { [System.Drawing.Color]::FromArgb(80, 80, 80) } else { [System.Drawing.Color]::FromArgb(45, 45, 48) }
        $textColor = [System.Drawing.Color]::White
    } else {
        $bgColor = if ($isSelected) { [System.Drawing.Color]::White } else { [System.Drawing.Color]::FromArgb(240, 240, 240) }
        $textColor = [System.Drawing.Color]::Black
    }
    
    $bgBrush = New-Object System.Drawing.SolidBrush($bgColor)
    $textBrush = New-Object System.Drawing.SolidBrush($textColor)
    
    $g.FillRectangle($bgBrush, $tabRect)
    
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    
    $g.DrawString($tabPage.Text, $tabs.Font, $textBrush, [System.Drawing.RectangleF]$tabRect, $sf)
    
    $bgBrush.Dispose()
    $textBrush.Dispose()
    $sf.Dispose()
})
$mainContainer.Panel1.Controls.Add($tabControl)

# --- リスト表示タブ ---
$listTabPage = New-Object System.Windows.Forms.TabPage "リスト表示"
$tabControl.TabPages.Add($listTabPage) | Out-Null

$script:taskDataGridView = New-Object System.Windows.Forms.DataGridView
$script:taskDataGridView.Dock = "Fill"; $script:taskDataGridView.AllowUserToAddRows = $false; $script:taskDataGridView.RowHeadersVisible = $false; $script:taskDataGridView.SelectionMode = "FullRowSelect"; $script:taskDataGridView.MultiSelect = $false; $script:taskDataGridView.ReadOnly = $true; $script:taskDataGridView.AllowUserToResizeRows = $false; $script:taskDataGridView.ColumnHeadersHeightSizeMode = "AutoSize"; $script:taskDataGridView.CellBorderStyle = "SingleHorizontal"
$listTabPage.Controls.Add($script:taskDataGridView)

# --- DataGridView 右クリックメニュー (機能復元) ---
$dgvContextMenu = New-Object System.Windows.Forms.ContextMenuStrip
$dgvEditMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("編集")
$dgvDeleteMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("削除")

# 進捗度変更メニューの追加
$dgvChangeStatusMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("進捗度の変更")
foreach ($status in $script:TaskStatuses) {
    $subMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem($status)
    $subMenuItem.Tag = $status
    $subMenuItem.Add_Click({
        param($source, $e)
        if ($script:taskDataGridView.SelectedRows.Count -gt 0) {
            $task = $script:taskDataGridView.SelectedRows[0].Tag
            $newStatus = $source.Tag
            if ($task -and $task.PSObject.Properties['ID']) { # プロジェクトでないことを確認
                Set-TaskStatus -task $task -newStatus $newStatus
                Update-AllViews
            }
        }
    })
    $dgvChangeStatusMenuItem.DropDownItems.Add($subMenuItem) | Out-Null
}

$dgvProjectPropertiesMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("プロパティの編集")
$dgvProjectDeleteMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("削除")
$dgvArchiveMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("アーカイブ")

$dgvContextMenu.Items.AddRange(@($dgvEditMenuItem, $dgvDeleteMenuItem, $dgvChangeStatusMenuItem, $dgvProjectPropertiesMenuItem, $dgvProjectDeleteMenuItem, $dgvArchiveMenuItem))
$script:taskDataGridView.ContextMenuStrip = $dgvContextMenu

$dgvContextMenu.Add_Opening({
    param($source, $e)
    if ($script:taskDataGridView.SelectedRows.Count -eq 0) {
        $e.Cancel = $true
        return
    }
    $item = $script:taskDataGridView.SelectedRows[0].Tag
    if ($null -eq $item) {
        $e.Cancel = $true
        return
    }

    $isProject = $item.PSObject.Properties.Name -contains 'ProjectName'
    $dgvEditMenuItem.Visible = -not $isProject
    $dgvDeleteMenuItem.Visible = -not $isProject
    $dgvChangeStatusMenuItem.Visible = -not $isProject
    $dgvProjectPropertiesMenuItem.Visible = $isProject
    $dgvProjectDeleteMenuItem.Visible = $isProject
    $dgvArchiveMenuItem.Visible = $true # Archive is always visible if an item is selected

    # タスクの場合、現在のステータスをメニューから非表示にする
    if (-not $isProject) {
        $currentStatus = $item.進捗度
        foreach ($subMenuItem in $dgvChangeStatusMenuItem.DropDownItems) {
            $subMenuItem.Visible = ($subMenuItem.Tag -ne $currentStatus)
        }
    }
})

$dgvEditMenuItem.Add_Click({
    if ($script:taskDataGridView.SelectedRows.Count -gt 0) {
        $task = $script:taskDataGridView.SelectedRows[0].Tag
        if ($task -and $task.PSObject.Properties['ID']) {
            # Start-EditTaskがTrue(保存)を返した時のみ画面更新
            if (Start-EditTask -task $task) {
                Update-AllViews
            }
        }
    }
})
$dgvDeleteMenuItem.Add_Click({
    if ($script:taskDataGridView.SelectedRows.Count -gt 0) {
        $task = $script:taskDataGridView.SelectedRows[0].Tag
        if ($task -and $task.PSObject.Properties['ID']) {
            # 修正: 編集コマンドを削除し、削除コマンドのみを実行
            Start-DeleteTask -task $task
            Update-AllViews
        }
    }
})
$dgvProjectPropertiesMenuItem.Add_Click({
    if ($script:taskDataGridView.SelectedRows.Count -gt 0) {
        $project = $script:taskDataGridView.SelectedRows[0].Tag
        if ($project -and $project.PSObject.Properties.Name -contains 'ProjectName') { # プロジェクトか確認
            Show-EditProjectPropertiesForm -projectObject $project -parentForm $mainForm
            Update-AllViews
        }
    }
})

$dgvProjectDeleteMenuItem.Add_Click({
    if ($script:taskDataGridView.SelectedRows.Count -gt 0) {
        $item = $script:taskDataGridView.SelectedRows[0].Tag
        if ($item -and $item.PSObject.Properties.Name -contains 'ProjectName') { # プロジェクトか確認
            $confirmResult = [System.Windows.Forms.MessageBox]::Show("プロジェクト '$($item.ProjectName)' を削除します。`n関連するすべてのタスクも削除されます。`n`nよろしいですか？", "プロジェクトの削除の確認", "YesNo", "Warning")
            if ($confirmResult -eq 'Yes') {
                $script:Projects = $script:Projects | Where-Object { $_.ProjectID -ne $item.ProjectID }
                $script:AllTasks = $script:AllTasks | Where-Object { $_.ProjectID -ne $item.ProjectID }
                Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
                Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
                Update-AllViews
            }
        }
    }
})

$dgvArchiveMenuItem.Add_Click({
    if ($script:taskDataGridView.SelectedRows.Count -eq 0) { return }
    $item = $script:taskDataGridView.SelectedRows[0].Tag
    if (-not $item) { return }

    # Check if it's a project
    if ($item.PSObject.Properties.Name.Contains('ProjectName')) {
        $confirmResult = [System.Windows.Forms.MessageBox]::Show(
            "プロジェクト '$($item.ProjectName)' をアーカイブしますか？`n関連するすべてのタスクも一緒にアーカイブされます。",
            "アーカイブの確認",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        )
        if ($confirmResult -eq 'Yes') {
            Move-ProjectToArchive -projectToArchive $item
            Update-AllViews
        }
    }
    # Check if it's a task
    elseif ($item.PSObject.Properties.Name.Contains('ID')) {
         $confirmResult = [System.Windows.Forms.MessageBox]::Show(
            "タスク '$($item.タスク)' をアーカイブしますか？",
            "アーカイブの確認",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question
        )
        if ($confirmResult -eq 'Yes') {
            Move-TaskToArchive -tasksToArchive @($item)
            Update-AllViews
        }
    }
})

# --- カンバン表示タブ ---
$kanbanTabPage = New-Object System.Windows.Forms.TabPage "カンバンボード"
$tabControl.TabPages.Add($kanbanTabPage) | Out-Null

$script:kanbanLayout = New-Object System.Windows.Forms.TableLayoutPanel
$script:kanbanLayout.Dock = "Fill"; $script:kanbanLayout.ColumnCount = $script:TaskStatuses.Count; $script:kanbanLayout.RowCount = 2
$kanbanTabPage.Controls.Add($script:kanbanLayout)

foreach ($i in 1..$script:kanbanLayout.ColumnCount) { $script:kanbanLayout.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, (100 / $script:kanbanLayout.ColumnCount)))) | Out-Null }
$script:kanbanLayout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize))) | Out-Null
$script:kanbanLayout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 100))) | Out-Null

# 視認性を向上させた新しい描画ロジック
$kanbanListBox_DrawItem = {
    param($source, $e)
    if ($e.Index -lt 0) { 
        $e.Graphics.Clear($source.BackColor)
        return 
    }

    # --- Setup ---
    $g = $e.Graphics
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $listBox = $source
    $task = $listBox.Items[$e.Index]
    $isSelected = ($e.State -band [System.Windows.Forms.DrawItemState]::Selected) -eq [System.Windows.Forms.DrawItemState]::Selected

    # --- Project Info ---
    $project = $script:Projects | Where-Object { $_.ProjectID -eq $task.ProjectID } | Select-Object -First 1
    $projectName = if ($project) { $project.ProjectName } else { "(プロジェクト未設定)" }
    $projectColorString = if ($project) { $project.ProjectColor } else { "#D3D3D3" }
    try {
        $projectColor = [System.Drawing.ColorTranslator]::FromHtml($projectColorString)
    } catch {
        $projectColor = [System.Drawing.Color]::LightGray
    }

    # --- Brushes and Pens ---
    $listBackColor = if ($isSelected) { [System.Drawing.SystemColors]::Highlight } else { $listBox.BackColor }
    $textColor = if ($isSelected) { [System.Drawing.SystemColors]::HighlightText } else { $listBox.ForeColor }
    $subTextColor = if ($isSelected) { [System.Drawing.SystemColors]::HighlightText } else { if ($script:isDarkMode) { [System.Drawing.Color]::Silver } else { [System.Drawing.Color]::DimGray } }
    
    $backBrush = New-Object System.Drawing.SolidBrush($listBackColor)
    $textBrush = New-Object System.Drawing.SolidBrush($textColor)
    $subTextBrush = New-Object System.Drawing.SolidBrush($subTextColor)
    $projectColorBrush = New-Object System.Drawing.SolidBrush($projectColor)
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 220, 220))

    # --- Fonts ---
    $taskFont = [System.Drawing.Font]::new("Meiryo UI", 9, [System.Drawing.FontStyle]::Bold)
    $projectFont = [System.Drawing.Font]::new("Meiryo UI", 8)
    $priorityFont = [System.Drawing.Font]::new("Meiryo UI", 8, [System.Drawing.FontStyle]::Bold)

    # --- Drawing ---
    
    # 1. Draw Background for the entire item
    $g.FillRectangle($backBrush, $e.Bounds)

    # 2. Draw Card Body
    $colors = Get-ThemeColors -IsDarkMode $script:isDarkMode
    $cardBounds = [System.Drawing.Rectangle]::new($e.Bounds.X + 2, $e.Bounds.Y + 2, $e.Bounds.Width - 4, $e.Bounds.Height - 4)
    # Only draw a white background if the item is NOT selected
    if (-not $isSelected) {
        $cardBackBrush = New-Object System.Drawing.SolidBrush($colors.ControlBack)
        $g.FillRectangle($cardBackBrush, $cardBounds)
        $cardBackBrush.Dispose()
    }
    $g.DrawRectangle($borderPen, $cardBounds) # Card border

    # 3. Draw Project Color Bar
    $g.FillRectangle($projectColorBrush, $cardBounds.X, $cardBounds.Y, 4, $cardBounds.Height)

    # --- Text and Content ---
    $leftMargin = $cardBounds.X + 10
    $topMargin = $cardBounds.Y + 5
    $contentWidth = [System.Math]::Max(1, $cardBounds.Width - 15)

    # 4. Draw Task Name
    $taskRect = [System.Drawing.RectangleF]::new($leftMargin, $topMargin, $contentWidth, 18)
    $g.DrawString($task.タスク, $taskFont, $textBrush, $taskRect)

    # 5. Draw Project Name
    $projectRect = [System.Drawing.RectangleF]::new($leftMargin, $topMargin + 16, $contentWidth, 15)
    $g.DrawString($projectName, $projectFont, $subTextBrush, $projectRect)

    # 6. Draw Priority and Due Date at the bottom
    $priorityText = "優先度: $($task.優先度)"
    $priorityColor = switch ($task.優先度) {
        "高" { [System.Drawing.Color]::Red }
        "中" { [System.Drawing.Color]::Orange }
        "低" { [System.Drawing.Color]::Green }
        default { $subTextColor }
    }
    # On selection, the priority color should also be the highlight text color to be visible
    $actualPriorityBrush = if ($isSelected) { $textBrush } else { New-Object System.Drawing.SolidBrush($priorityColor) }
    
    $prioritySize = $g.MeasureString($priorityText, $priorityFont)
    $priorityY = $cardBounds.Bottom - $prioritySize.Height - 3
    $g.DrawString($priorityText, $priorityFont, $actualPriorityBrush, $leftMargin, $priorityY)
    if (-not $isSelected) { $actualPriorityBrush.Dispose() } # Dispose only if we created a new brush

    if (-not [string]::IsNullOrWhiteSpace($task.期日)) {
        $dueDateText = "期日: $($task.期日)"
        $dueDateSize = $g.MeasureString($dueDateText, $projectFont)
        $dateX = $cardBounds.Right - $dueDateSize.Width - 5
        $g.DrawString($dueDateText, $projectFont, $subTextBrush, $dateX, $priorityY)
    }

    # --- Cleanup ---
    $backBrush.Dispose(); $textBrush.Dispose(); $subTextBrush.Dispose(); $projectColorBrush.Dispose(); $borderPen.Dispose()
    $taskFont.Dispose(); $projectFont.Dispose(); $priorityFont.Dispose()
}

$kanbanContextMenu = New-Object System.Windows.Forms.ContextMenuStrip
$kanbanEditMenuItem = $kanbanContextMenu.Items.Add("編集")
$kanbanDeleteMenuItem = $kanbanContextMenu.Items.Add("削除")

$kanbanEditMenuItem.Add_Click({
    param($s, $e)
    $listBox = $kanbanContextMenu.SourceControl
    if ($listBox.SelectedItem) {
        if (Start-EditTask -task $listBox.SelectedItem) {
            Update-AllViews
        }
    }
})

$kanbanDeleteMenuItem.Add_Click({
    param($s, $e)
    $listBox = $kanbanContextMenu.SourceControl
    if ($listBox.SelectedItem) {
        Start-DeleteTask -task $listBox.SelectedItem
        Update-AllViews
    }
})

$script:kanbanLists = @{}
$col = 0
foreach ($status in $script:TaskStatuses) {
    $header = New-Object System.Windows.Forms.Label; $header.Text = $status; $header.Dock = "Fill"; $header.TextAlign = "MiddleCenter"; $header.Font = New-Object System.Drawing.Font("Meiryo UI", 10, [System.Drawing.FontStyle]::Bold); $header.MinimumSize = New-Object System.Drawing.Size(1, 1)
    $script:kanbanLayout.Controls.Add($header, $col, 0)
    
    $listBox = New-Object System.Windows.Forms.ListBox
    $listBox.Dock = "Fill"
    $listBox.AllowDrop = $true
    $listBox.DisplayMember = "タスク"
    $listBox.SelectionMode = [System.Windows.Forms.SelectionMode]::One
    $listBox.DrawMode = [System.Windows.Forms.DrawMode]::OwnerDrawFixed
    $listBox.ItemHeight = 60
    $listBox.Tag = $status # ドロップ先を識別するためにステータスをTagに設定
    $listBox.ContextMenuStrip = $kanbanContextMenu

    # オーナー描画イベント (既存)
    $listBox.Add_DrawItem($kanbanListBox_DrawItem)
    
    # ダブルクリック編集イベント (既存)
    $listBox.Add_DoubleClick({
        param($source, $e)
        if ($source.SelectedItem) {
            if (Start-EditTask -task $source.SelectedItem) {
                Update-AllViews
            }
        }
    })

    # 1. カンバンボード - 複数リスト間での単一選択機能
     $listBox.Add_SelectedIndexChanged({
         param($source, $e)
         if ($script:isClearingSelections) { return }
         $selectedListBox = $source
         if ($selectedListBox.SelectedItem -eq $null) { return }
         $script:isClearingSelections = $true
         try {
             foreach ($otherListBox in $script:kanbanLists.Values) {
                 if ($otherListBox -ne $selectedListBox) {
                     $otherListBox.ClearSelected()
                 }
             }
         }
         finally {
             $script:isClearingSelections = $false
         }
     })

    # 2-A. カンバンボードからのドラッグ開始 (MouseDown と MouseMove を使用)
    $listBox.Add_MouseDown({
        param($source, $e)
        if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
            $lb = $source
            $index = $lb.IndexFromPoint($e.Location)
            if ($index -ne [System.Windows.Forms.ListBox]::NoMatches) {
                # ドラッグ操作の準備として、タスクと開始点を保存
                $script:kanbanDragTask = $lb.Items[$index]
                $script:kanbanDragStartPoint = $e.Location
            } else {
                # クリックがアイテム上でない場合はリセット
                $script:kanbanDragTask = $null
            }
        } elseif ($e.Button -eq [System.Windows.Forms.MouseButtons]::Right) {
            $lb = $source
            $index = $lb.IndexFromPoint($e.Location)
            if ($index -ne [System.Windows.Forms.ListBox]::NoMatches) {
                $lb.SelectedIndex = $index
            }
        }
    })

    $listBox.Add_MouseMove({
        param($source, $e)
        # 左クリック中で、かつドラッグ対象のタスクが存在する場合
        if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left -and $script:kanbanDragTask) {
            $dragThreshold = [System.Windows.Forms.SystemInformation]::DragSize
            # マウスが一定距離以上移動したか確認
            if (([Math]::Abs($e.X - $script:kanbanDragStartPoint.X) -gt $dragThreshold.Width) -or 
                ([Math]::Abs($e.Y - $script:kanbanDragStartPoint.Y) -gt $dragThreshold.Height)) {
                
                # DoDragDrop操作を開始
                $source.DoDragDrop($script:kanbanDragTask, [System.Windows.Forms.DragDropEffects]::Move)
                
                # ドラッグ開始後は、次のドラッグに備えてリセット
                $script:kanbanDragTask = $null
            }
        }
    })

    # 2-B. ドロップの受付 (DragEnter)
    $listBox.Add_DragEnter({
        param($source, $e)
        $isTask = $false
        # 利用可能な全フォーマットをチェックする
        foreach ($format in $e.Data.GetFormats()) {
            try {
                $data = $e.Data.GetData($format)
                # PSCustomObjectであり、かつIDプロパティがあればタスクとみなす
                if ($data -is [psobject] -and $data.PSObject.Properties['ID']) {
                    $isTask = $true
                    break
                }
            } catch {
                # GetDataが失敗する可能性もあるため、Catchで握りつぶす
            }
        }

        if ($isTask) {
            $e.Effect = [System.Windows.Forms.DragDropEffects]::Move
        } else {
            $e.Effect = [System.Windows.Forms.DragDropEffects]::None
        }
    })

    # 2-B. ドロップ処理 (DragDrop)
    $listBox.Add_DragDrop({
        param($source, $e)
        $task = $null
        # DragEnterと同様のロジックでタスクオブジェクトを再度取得する
        foreach ($format in $e.Data.GetFormats()) {
            try {
                $data = $e.Data.GetData($format)
                if ($data -is [psobject] -and $data.PSObject.Properties['ID']) {
                    $task = $data
                    break
                }
            } catch {}
        }

        if ($task) {
            $targetListBox = $source
            $newStatus = $targetListBox.Tag
            
            # ステータスが変更されている場合のみ処理を実行
            if ($task.進捗度 -ne $newStatus) {
                # 既存の関数を利用してステータス変更と保存を行う
                Set-TaskStatus -task $task -newStatus $newStatus
                # 全ビューを更新
                Update-AllViews
            }
        }
    })

    # Deleteキーによる削除機能
    $listBox.Add_KeyDown({
        param($source, $e)
        if ($e.KeyCode -eq [System.Windows.Forms.Keys]::Delete) {
            $lb = $source
            if ($lb.SelectedItem) {
                Start-DeleteTask -task $lb.SelectedItem
                Update-AllViews
            }
        }
    })

    $script:kanbanLayout.Controls.Add($listBox, $col, 1)
    $script:kanbanLists[$status] = $listBox
    $col++
}

# --- カレンダー表示タブ ---
$calendarTabPage = New-Object System.Windows.Forms.TabPage "カレンダー表示"
$tabControl.TabPages.Add($calendarTabPage) | Out-Null

# --- カレンダー表示タブのレイアウト変更 (左右分割) ---
$calendarSplitContainer = New-Object System.Windows.Forms.SplitContainer
$calendarSplitContainer.Dock = "Fill"
$calendarSplitContainer.Orientation = "Vertical" # 左右分割
$calendarSplitContainer.SplitterDistance = $mainForm.Width * 2 / 3 # 左側を広めに
$calendarTabPage.Controls.Add($calendarSplitContainer)

# --- 左パネル (カレンダー + 日付詳細) ---
$calendarLeftSplitContainer = New-Object System.Windows.Forms.SplitContainer
$calendarLeftSplitContainer.Dock = "Fill"
$calendarLeftSplitContainer.Orientation = "Horizontal" # 上下分割
$calendarLeftSplitContainer.SplitterDistance = 400 # カレンダーの高さを固定
$calendarSplitContainer.Panel1.Controls.Add($calendarLeftSplitContainer)

# 左上パネル：カレンダーグリッド
$calendarGridPanel = New-Object System.Windows.Forms.Panel; $calendarGridPanel.Dock = "Fill"
$calendarLeftSplitContainer.Panel1.Controls.Add($calendarGridPanel)

$navPanel = New-Object System.Windows.Forms.Panel; $navPanel.Dock = "Top"; $navPanel.Height = 40; $calendarGridPanel.Controls.Add($navPanel)
$navFlowLayout = New-Object System.Windows.Forms.FlowLayoutPanel; $navFlowLayout.FlowDirection = "LeftToRight"; $navFlowLayout.Padding = "0,8,0,0"; $navFlowLayout.AutoSize = $true; $navFlowLayout.WrapContents = $false
$navPanel.Controls.Add($navFlowLayout)
$navPanel.Add_Resize({
    param($source, $e)
    # フォームが最小化されていない場合のみ、位置調整を実行する
    if ($mainForm.WindowState -ne [System.Windows.Forms.FormWindowState]::Minimized) {
        $p = $source; $c = $p.Controls[0]
        $c.Left = ($p.Width - $c.Width) / 2
    }
})

$script:btnPrevYear = New-Object System.Windows.Forms.Button; $script:btnPrevYear.Text = "<<"; $script:btnPrevMonth = New-Object System.Windows.Forms.Button; $script:btnPrevMonth.Text = "<"; $script:lblMonthYear = New-Object System.Windows.Forms.Label; $script:lblMonthYear.Text = "2025年 10月"; $script:lblMonthYear.Font = New-Object System.Drawing.Font("Meiryo UI", 12, [System.Drawing.FontStyle]::Bold); $script:lblMonthYear.Margin = "20,0,20,0"; $script:lblMonthYear.AutoSize = $true; $script:btnNextMonth = New-Object System.Windows.Forms.Button; $script:btnNextMonth.Text = ">"; $script:btnNextYear = New-Object System.Windows.Forms.Button; $script:btnNextYear.Text = ">>"
$navFlowLayout.Controls.AddRange(@($script:btnPrevYear, $script:btnPrevMonth, $script:lblMonthYear, $script:btnNextMonth, $script:btnNextYear))

$script:calendarGrid = New-Object System.Windows.Forms.TableLayoutPanel; $script:calendarGrid.Dock = "Fill"; $script:calendarGrid.ColumnCount = 7; $script:calendarGrid.RowCount = 7
for ($i = 0; $i -lt 7; $i++) { $script:calendarGrid.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 14.28))) | Out-Null }
$script:calendarGrid.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 30))) | Out-Null; for ($i = 0; $i -lt 6; $i++) { $script:calendarGrid.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 16.66))) | Out-Null }
$calendarGridPanel.Controls.Add($script:calendarGrid); $script:calendarGrid.BringToFront()

# 左下パネル：日付詳細表示
$dayInfoGroupBox = New-Object System.Windows.Forms.GroupBox
$dayInfoGroupBox.Text = "選択日の詳細"
$dayInfoGroupBox.Dock = "Fill"
$calendarLeftSplitContainer.Panel2.Controls.Add($dayInfoGroupBox)

$script:dayInfoTableLayoutPanel = New-Object System.Windows.Forms.TableLayoutPanel
$script:dayInfoTableLayoutPanel.Dock = "Fill"
$script:dayInfoTableLayoutPanel.ColumnCount = 2
$script:dayInfoTableLayoutPanel.RowCount = 1
$script:dayInfoTableLayoutPanel.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 50))) | Out-Null
$script:dayInfoTableLayoutPanel.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 50))) | Out-Null

# Left: Events
$script:dayInfoEventsGroup = New-Object System.Windows.Forms.GroupBox; $script:dayInfoEventsGroup.Text = "イベント"; $script:dayInfoEventsGroup.Dock = "Fill"
$script:dayInfoEventsPanel = New-Object System.Windows.Forms.FlowLayoutPanel
$script:dayInfoEventsPanel.Dock = "Fill"
$script:dayInfoEventsPanel.AutoScroll = $true
$script:dayInfoEventsPanel.FlowDirection = 'TopDown'
$script:dayInfoEventsPanel.WrapContents = $false
$script:dayInfoEventsGroup.Controls.Add($script:dayInfoEventsPanel)
$script:dayInfoTableLayoutPanel.Controls.Add($script:dayInfoEventsGroup, 0, 0)

# Right: Tasks/Projects
$script:dayInfoTasksGroup = New-Object System.Windows.Forms.GroupBox; $script:dayInfoTasksGroup.Text = "期日 (プロジェクト/タスク)"; $script:dayInfoTasksGroup.Dock = "Fill"
$script:dayInfoTasksPanel = New-Object System.Windows.Forms.FlowLayoutPanel
$script:dayInfoTasksPanel.Dock = "Fill"
$script:dayInfoTasksPanel.AutoScroll = $true
$script:dayInfoTasksPanel.FlowDirection = 'TopDown'
$script:dayInfoTasksPanel.WrapContents = $false
$script:dayInfoTasksGroup.Controls.Add($script:dayInfoTasksPanel)
$script:dayInfoTableLayoutPanel.Controls.Add($script:dayInfoTasksGroup, 1, 0)

$dayInfoGroupBox.Controls.Add($script:dayInfoTableLayoutPanel)

# --- 右パネル (タイムライン) ---
$script:timelinePanel = New-Object System.Windows.Forms.Panel
$script:timelinePanel.Dock = "Fill"
$script:timelinePanel.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$calendarSplitContainer.Panel2.Controls.Add($script:timelinePanel)

# --- タイムラインのインタラクションイベントハンドラ ---

# マウスボタン押下イベント
$script:timelinePanel.Add_MouseDown({
    param($source, $e)

    if ($e.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }

    $panel = $source
    $centerX = [int]($panel.Width * 0.55)
    $topMargin = 30
    $startHour = if ($script:Settings.TimelineStartHour) { [int]$script:Settings.TimelineStartHour } else { 8 }
    $endHour = if ($script:Settings.TimelineEndHour) { [int]$script:Settings.TimelineEndHour } else { 24 }
    $totalHours = $endHour - $startHour
    $viewHeight = $panel.Height - $topMargin - 10
    if ($viewHeight -le 0) { return }
    $pixelsPerMinute = $viewHeight / ($totalHours * 60)

    $selectedDate = if ($panel.Tag -is [datetime]) { $panel.Tag } else { (Get-Date).Date }
    $clickedTime = $selectedDate.Date.AddHours($startHour).AddMinutes([Math]::Max(0, ($e.Y - $topMargin) / $pixelsPerMinute))

    $script:dragStartPoint = $e.Location
    $script:ghostRect = [System.Drawing.RectangleF]::Empty
    $script:snapLineY = -1
    $panel.Capture = $true

    $resizeHandleSize = 8 # ハンドルを掴みやすくする

    if ($e.X -le $centerX) {
        # --- 左側 (予定) エリア ---
        $dateString = $selectedDate.ToString("yyyy-MM-dd")
        $eventUnderCursor = if ($script:AllEvents.PSObject.Properties[$dateString]) { 
            $script:AllEvents.PSObject.Properties[$dateString].Value | Where-Object { 
                -not $_.IsAllDay -and $clickedTime -ge [datetime]$_.StartTime -and $clickedTime -le [datetime]$_.EndTime 
            } | Select-Object -First 1 
        } else { $null }

        if ($eventUnderCursor) {
            $script:dragItemType = 'Event'
            $script:draggedItem = $eventUnderCursor
            $script:dragItemOriginalStartTime = [datetime]$eventUnderCursor.StartTime
            $script:dragItemOriginalEndTime = [datetime]$eventUnderCursor.EndTime

            $evtStartMin = (([datetime]$eventUnderCursor.StartTime).Hour - $startHour) * 60 + ([datetime]$eventUnderCursor.StartTime).Minute
            $evtEndMin = (([datetime]$eventUnderCursor.EndTime).Hour - $startHour) * 60 + ([datetime]$eventUnderCursor.EndTime).Minute
            $evtY = $topMargin + ($evtStartMin * $pixelsPerMinute)
            $evtH = ($evtEndMin - $evtStartMin) * $pixelsPerMinute

            if ($e.Y -ge $evtY -and $e.Y -le ($evtY + $resizeHandleSize)) { $script:dragMode = 'resizeTop' }
            elseif ($e.Y -ge ($evtY + $evtH - $resizeHandleSize) -and $e.Y -le ($evtY + $evtH)) { $script:dragMode = 'resizeBottom' }
            else { $script:dragMode = 'move' }
        } else {
            $script:dragMode = 'createEvent'
        }
    } else {
        # --- 右側 (実績) エリア ---
        $logUnderCursor = $script:AllTimeLogs | Where-Object { 
            $_.StartTime -and $_.EndTime -and 
            ([datetime]$_.StartTime).Date -eq $selectedDate.Date -and 
            $clickedTime -ge ([datetime]$_.StartTime) -and $clickedTime -le ([datetime]$_.EndTime) 
        } | Select-Object -First 1
        
        if ($logUnderCursor) {
            $script:dragItemType = 'TimeLog'
            $script:draggedItem = $logUnderCursor
            $script:dragItemOriginalStartTime = [datetime]$logUnderCursor.StartTime
            $script:dragItemOriginalEndTime = [datetime]$logUnderCursor.EndTime

            $logStartMin = (([datetime]$logUnderCursor.StartTime).Hour - $startHour) * 60 + ([datetime]$logUnderCursor.StartTime).Minute
            $logEndMin = (([datetime]$logUnderCursor.EndTime).Hour - $startHour) * 60 + ([datetime]$logUnderCursor.EndTime).Minute
            $logY = $topMargin + ($logStartMin * $pixelsPerMinute)
            $logH = ($logEndMin - $logStartMin) * $pixelsPerMinute

            if ($e.Y -ge $logY -and $e.Y -le ($logY + $resizeHandleSize)) { $script:dragMode = 'resizeTop' }
            elseif ($e.Y -ge ($logY + $logH - $resizeHandleSize) -and $e.Y -le ($logY + $logH)) { $script:dragMode = 'resizeBottom' }
            else { $script:dragMode = 'move' }
        } else {
            $script:dragMode = 'createLog'
        }
    }
    $panel.Invalidate()
})

# マウス移動イベント
$script:timelinePanel.Add_MouseMove({
    param($source, $e)
    if (-not $source.Capture) { return }

    $panel = $source
    $centerX = [int]($panel.Width * 0.55)
    $leftMargin = 50
    $topMargin = 30
    $startHour = if ($script:Settings.TimelineStartHour) { [int]$script:Settings.TimelineStartHour } else { 8 }
    $endHour = if ($script:Settings.TimelineEndHour) { [int]$script:Settings.TimelineEndHour } else { 24 }
    $viewHeight = $panel.Height - $topMargin - 10
    if ($viewHeight -le 0) { return }
    $pixelsPerMinute = $viewHeight / (($endHour - $startHour) * 60)
    $selectedDate = if ($panel.Tag -is [datetime]) { $panel.Tag } else { (Get-Date).Date }
    $mouseTime = $selectedDate.Date.AddHours($startHour).AddMinutes([Math]::Max(0, ($e.Y - $topMargin) / $pixelsPerMinute))

    $script:snapLineY = -1 # Reset snap line

    $ghostX = 0; $ghostWidth = 0
    if ($script:dragItemType -eq 'Event' -or $script:dragMode -eq 'createEvent') {
        $ghostX = $leftMargin
        $ghostWidth = $centerX - $leftMargin
    } else { # TimeLog or createLog
        $ghostX = $centerX
        $ghostWidth = $panel.Width - $centerX
    }

    switch ($script:dragMode) {
        { $_ -in 'createLog', 'createEvent' } {
            $startY = [Math]::Min($script:dragStartPoint.Y, $e.Y)
            $endY = [Math]::Max($script:dragStartPoint.Y, $e.Y)
            $script:ghostRect = [System.Drawing.RectangleF]::new($ghostX, $startY, $ghostWidth, $endY - $startY)
            $source.Cursor = [System.Windows.Forms.Cursors]::Cross
        }
        'resizeTop' {
            $snappedTime = Get-SnappedTime -Time $mouseTime -Date $selectedDate -ExcludeLog $script:draggedItem
            $newY = $topMargin + (($snappedTime.Hour - $startHour) * 60 + $snappedTime.Minute) * $pixelsPerMinute
            $script:snapLineY = $newY

            $endY = $topMargin + ((($script:dragItemOriginalEndTime.Hour - $startHour) * 60 + $script:dragItemOriginalEndTime.Minute) * $pixelsPerMinute)
            $script:ghostRect = [System.Drawing.RectangleF]::FromLTRB($ghostX, [Math]::Min($newY, $endY), $ghostX + $ghostWidth, [Math]::Max($newY, $endY))
            $source.Cursor = [System.Windows.Forms.Cursors]::SizeNS
        }
        'resizeBottom' {
            $snappedTime = Get-SnappedTime -Time $mouseTime -Date $selectedDate -ExcludeLog $script:draggedItem
            $newY = $topMargin + (($snappedTime.Hour - $startHour) * 60 + $snappedTime.Minute) * $pixelsPerMinute
            $script:snapLineY = $newY
            
            $startY = $topMargin + ((($script:dragItemOriginalStartTime.Hour - $startHour) * 60 + $script:dragItemOriginalStartTime.Minute) * $pixelsPerMinute)
            $script:ghostRect = [System.Drawing.RectangleF]::FromLTRB($ghostX, [Math]::Min($startY, $newY), $ghostX + $ghostWidth, [Math]::Max($startY, $newY))
            $source.Cursor = [System.Windows.Forms.Cursors]::SizeNS
        }
        'move' {
            $offsetMinutes = (($e.Y - $script:dragStartPoint.Y) / $pixelsPerMinute)
            $newStartTimeCandidate = $script:dragItemOriginalStartTime.AddMinutes($offsetMinutes)

            $snappedTime = Get-SnappedTime -Time $newStartTimeCandidate -Date $selectedDate -ExcludeLog $script:draggedItem
            $script:snapLineY = $topMargin + (($snappedTime.Hour - $startHour) * 60 + $snappedTime.Minute) * $pixelsPerMinute

            $duration = $script:dragItemOriginalEndTime - $script:dragItemOriginalStartTime
            $finalStartTime = $snappedTime
            $finalEndTime = $finalStartTime.Add($duration)

            $startY = $topMargin + ((($finalStartTime.Hour - $startHour) * 60 + $finalStartTime.Minute) * $pixelsPerMinute)
            $endY = $topMargin + ((($finalEndTime.Hour - $startHour) * 60 + $finalEndTime.Minute) * $pixelsPerMinute)
            $script:ghostRect = [System.Drawing.RectangleF]::new($ghostX, $startY, $ghostWidth, $endY - $startY)
            $source.Cursor = [System.Windows.Forms.Cursors]::SizeAll
        }
    }
    $source.Invalidate()
})

# マウスボタン解放イベント
$script:timelinePanel.Add_MouseUp({
    param($source, $e)
    if (-not $source.Capture) { return }
    $source.Capture = $false
    $source.Cursor = [System.Windows.Forms.Cursors]::Default

    $panel = $source
    $selectedDate = if ($panel.Tag -is [datetime]) { $panel.Tag } else { (Get-Date).Date }
    $topMargin = 30
    $startHour = if ($script:Settings.TimelineStartHour) { [int]$script:Settings.TimelineStartHour } else { 8 }
    $endHour = if ($script:Settings.TimelineEndHour) { [int]$script:Settings.TimelineEndHour } else { 24 }
    $viewHeight = $panel.Height - $topMargin - 10
    if ($viewHeight -le 0) { $panel.Invalidate(); return }
    $pixelsPerMinute = $viewHeight / (($endHour - $startHour) * 60)
    $mouseTime = $selectedDate.Date.AddHours($startHour).AddMinutes([Math]::Max(0, ($e.Y - $topMargin) / $pixelsPerMinute))

    # --- クリック判定 (ドラッグ距離が短い場合) ---
    $dragDistance = [Math]::Abs($e.Y - $script:dragStartPoint.Y)
    if ($dragDistance -lt 5) {
        if ($script:dragMode -eq 'move') {
            # アイテムをクリックした場合、選択状態にする
            if ($script:dragItemType -eq 'Event') {
                $script:selectedEvent = $script:draggedItem
                $script:selectedTimeLog = $null
            } elseif ($script:dragItemType -eq 'TimeLog') {
                $script:selectedTimeLog = $script:draggedItem
                $script:selectedEvent = $null
            }
            $panel.Focus()
        } else {
            # 空白をクリックした場合、選択解除
            $script:selectedEvent = $null
            $script:selectedTimeLog = $null
        }
        # ドラッグ状態をリセットして再描画
        $script:dragMode = 'none'
        $script:draggedItem = $null
        $panel.Invalidate()
        return
    }

    switch ($script:dragMode) {
        'createLog' {
            $dragDistance = [Math]::Abs($e.Y - $script:dragStartPoint.Y)
            if ($dragDistance -gt 5) {
                $startY = [Math]::Min($script:dragStartPoint.Y, $e.Y); $endY = [Math]::Max($script:dragStartPoint.Y, $e.Y)
                $startTime = $selectedDate.Date.AddHours($startHour).AddMinutes(($startY - $topMargin) / $pixelsPerMinute)
                $endTime = $selectedDate.Date.AddHours($startHour).AddMinutes(($endY - $topMargin) / $pixelsPerMinute)
                $startTime = Get-SnappedTime -Time $startTime -Date $selectedDate
                $endTime = Get-SnappedTime -Time $endTime -Date $selectedDate
                if ($endTime -gt $startTime) {
                    $result = Show-TimeLogEntryForm -InitialStartTime $startTime -InitialEndTime $endTime -projects $script:Projects -tasks $script:AllTasks
                    if ($result -and (Resolve-TimeLogOverlap -NewStartTime $result.StartTime -NewEndTime $result.EndTime)) {
                        $newLog = [PSCustomObject]@{ ID = [guid]::NewGuid().ToString(); TaskID = if ($result.Task) { $result.Task.ID } else { $null }; Memo = if (-not $result.Task) { $result.Memo } else { $null }; StartTime = $result.StartTime.ToString("o"); EndTime = $result.EndTime.ToString("o") }
                        $script:AllTimeLogs += $newLog; Save-TimeLogs
                    }
                }
            }
        }
        'createEvent' {
            $dragDistance = [Math]::Abs($e.Y - $script:dragStartPoint.Y)
            if ($dragDistance -gt 5) {
                $startY = [Math]::Min($script:dragStartPoint.Y, $e.Y); $endY = [Math]::Max($script:dragStartPoint.Y, $e.Y)
                $startTime = $selectedDate.Date.AddHours($startHour).AddMinutes(($startY - $topMargin) / $pixelsPerMinute)
                $endTime = $selectedDate.Date.AddHours($startHour).AddMinutes(($endY - $topMargin) / $pixelsPerMinute)
                $startTime = Get-SnappedTime -Time $startTime -Date $selectedDate
                $endTime = Get-SnappedTime -Time $endTime -Date $selectedDate
                if ($endTime -gt $startTime) {
                    $eventData = Show-EventInputForm -initialDate $startTime -initialEndTime $endTime
                    if ($eventData) {
                        $dateString = $eventData.StartTime.ToString("yyyy-MM-dd")
                        if (-not $script:AllEvents.PSObject.Properties[$dateString]) {
                            $script:AllEvents | Add-Member -MemberType NoteProperty -Name $dateString -Value @()
                        }
                        $newEvent = [PSCustomObject]@{
                            ID        = [guid]::NewGuid().ToString()
                            Title     = $eventData.Title
                            StartTime = $eventData.StartTime.ToString("o")
                            EndTime   = $eventData.EndTime.ToString("o")
                            IsAllDay  = $eventData.IsAllDay
                        }
                        $currentEvents = [System.Collections.ArrayList]@($script:AllEvents.$dateString)
                        $currentEvents.Add($newEvent)
                        $script:AllEvents.$dateString = $currentEvents
                        Save-Events
                    }
                }
            }
        }
        'resizeTop' {
            $snappedTime = Get-SnappedTime -Time $mouseTime -Date $selectedDate -ExcludeLog $script:draggedItem
            $newStart = $snappedTime
            $newEnd = $script:dragItemOriginalEndTime
            if ($newEnd -gt $newStart) {
                if ($script:dragItemType -eq 'TimeLog' -and (Resolve-TimeLogOverlap -NewStartTime $newStart -NewEndTime $newEnd -LogToExclude $script:draggedItem)) {
                    $script:draggedItem.StartTime = $newStart.ToString("o"); Save-TimeLogs
                } elseif ($script:dragItemType -eq 'Event') {
                    $script:draggedItem.StartTime = $newStart.ToString("o"); Save-Events
                }
            }
        }
        'resizeBottom' {
            $snappedTime = Get-SnappedTime -Time $mouseTime -Date $selectedDate -ExcludeLog $script:draggedItem
            $newStart = $script:dragItemOriginalStartTime
            $newEnd = $snappedTime
            if ($newEnd -gt $newStart) {
                if ($script:dragItemType -eq 'TimeLog' -and (Resolve-TimeLogOverlap -NewStartTime $newStart -NewEndTime $newEnd -LogToExclude $script:draggedItem)) {
                    $script:draggedItem.EndTime = $newEnd.ToString("o"); Save-TimeLogs
                } elseif ($script:dragItemType -eq 'Event') {
                    $script:draggedItem.EndTime = $newEnd.ToString("o"); Save-Events
                }
            }
        }
        'move' {
            $offsetMinutes = (($e.Y - $script:dragStartPoint.Y) / $pixelsPerMinute)
            $newStartTimeCandidate = $script:dragItemOriginalStartTime.AddMinutes($offsetMinutes)
            
            $newStart = Get-SnappedTime -Time $newStartTimeCandidate -Date $selectedDate -ExcludeLog $script:draggedItem
            $duration = $script:dragItemOriginalEndTime - $script:dragItemOriginalStartTime
            $newEnd = $newStart.Add($duration)

            if ($script:dragItemType -eq 'TimeLog') {
                if (Resolve-TimeLogOverlap -NewStartTime $newStart -NewEndTime $newEnd -LogToExclude $script:draggedItem) {
                    $script:draggedItem.StartTime = $newStart.ToString("o")
                    $script:draggedItem.EndTime = $newEnd.ToString("o")
                    Save-TimeLogs
                }
            } elseif ($script:dragItemType -eq 'Event') {
                $script:draggedItem.StartTime = $newStart.ToString("o")
                $script:draggedItem.EndTime = $newEnd.ToString("o")
                Save-Events
            }
        }
    }

    # ドラッグ状態をリセット
    $script:dragMode = 'none'
    $script:dragItemType = $null
    $script:draggedItem = $null
    $script:ghostRect = [System.Drawing.RectangleF]::Empty
    $script:snapLineY = -1

    # ビューを更新
    Update-AllViews
})

# マウスダブルクリックイベント (編集・新規追加用)
$script:timelinePanel.Add_DoubleClick({
    param($source, $e)

    if ($e.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }

    $panel = $source
    $centerX = [int]($panel.Width * 0.55)
    $topMargin = 30
    $startHour = if ($script:Settings.TimelineStartHour) { [int]$script:Settings.TimelineStartHour } else { 8 }
    $endHour = if ($script:Settings.TimelineEndHour) { [int]$script:Settings.TimelineEndHour } else { 24 }
    $totalHours = $endHour - $startHour
    $viewHeight = $panel.Height - $topMargin - 10
    if ($viewHeight -le 0) { return }
    $pixelsPerMinute = $viewHeight / ($totalHours * 60)

    $clickMinutes = ($e.Y - $topMargin) / $pixelsPerMinute
    $selectedDate = if ($script:timelinePanel.Tag -is [datetime]) { $script:timelinePanel.Tag } else { (Get-Date).Date }
    $clickedTime = $selectedDate.Date.AddHours($startHour).AddMinutes($clickMinutes)

    if ($e.X -le $centerX) {
        # --- 左側 (予定): イベントの追加/編集 ---
        $dateString = $selectedDate.ToString("yyyy-MM-dd")
        $eventUnderCursor = if ($script:AllEvents.PSObject.Properties[$dateString]) { 
            $script:AllEvents.PSObject.Properties[$dateString].Value | Where-Object { 
                -not $_.IsAllDay -and $clickedTime -ge [datetime]$_.StartTime -and $clickedTime -le [datetime]$_.EndTime 
            } | Select-Object -First 1 
        } else { $null }
        
        if ($eventUnderCursor) { Start-EditEvent -eventToEdit $eventUnderCursor -eventDate $selectedDate }
        else { Start-AddNewEvent -initialDate $clickedTime }
    } else {
        # --- 右側 (実績): 実績の追加/編集 ---
        $logUnderCursor = $script:AllTimeLogs | Where-Object { $_.StartTime -and $_.EndTime -and ([datetime]$_.StartTime).Date -eq $selectedDate.Date -and $clickedTime -ge ([datetime]$_.StartTime) -and $clickedTime -le ([datetime]$_.EndTime) } | Select-Object -First 1
        if ($logUnderCursor) {
            $result = Show-TimeLogEntryForm -log $logUnderCursor -tasks $script:AllTasks -projects $script:Projects
            if ($result -and (Resolve-TimeLogOverlap -NewStartTime $result.StartTime -NewEndTime $result.EndTime -LogToExclude $logUnderCursor)) {
                $logUnderCursor.StartTime = $result.StartTime.ToString("o"); $logUnderCursor.EndTime = $result.EndTime.ToString("o"); $logUnderCursor.TaskID = if ($result.Task) { $result.Task.ID } else { $null }; $logUnderCursor.Memo = if (-not $result.Task) { $result.Memo } else { $null }
                Save-TimeLogs
            }
        } else {
            $startTime = $clickedTime.AddMinutes(-($clickedTime.Minute % 15)); $endTime = $startTime.AddMinutes(30)
            $result = Show-TimeLogEntryForm -InitialStartTime $startTime -InitialEndTime $endTime -projects $script:Projects -tasks $script:AllTasks
            if ($result -and (Resolve-TimeLogOverlap -NewStartTime $result.StartTime -NewEndTime $result.EndTime)) {
                $newLog = [PSCustomObject]@{ ID = [guid]::NewGuid().ToString(); TaskID = if ($result.Task) { $result.Task.ID } else { $null }; Memo = if (-not $result.Task) { $result.Memo } else { $null }; StartTime = $result.StartTime.ToString("o"); EndTime = $result.EndTime.ToString("o") }
                $script:AllTimeLogs += $newLog; Save-TimeLogs
            }
        }
    }
    Update-AllViews
})

$script:timelinePanel.Add_MouseClick({
    param($source, $e)
    if ($e.Button -ne [System.Windows.Forms.MouseButtons]::Right) { return }

    $panel = $source
    $centerX = [int]($panel.Width * 0.55)
    $selectedDate = if ($panel.Tag -is [datetime]) { $panel.Tag } else { (Get-Date).Date }
    $topMargin = 30
    $startHour = if ($script:Settings.TimelineStartHour) { [int]$script:Settings.TimelineStartHour } else { 8 }
    $endHour = if ($script:Settings.TimelineEndHour) { [int]$script:Settings.TimelineEndHour } else { 24 }
    $totalHours = $endHour - $startHour
    $viewHeight = $panel.Height - $topMargin - 10
    if ($viewHeight -le 0) { return }
    $pixelsPerMinute = $viewHeight / ($totalHours * 60)

    $clickMinutes = ($e.Y - $topMargin) / $pixelsPerMinute
    $clickedTime = $selectedDate.Date.AddHours($startHour).AddMinutes($clickMinutes)

    if ($e.X -le $centerX) {
        # --- 左側 (予定) の右クリック ---
        $dateString = $selectedDate.ToString("yyyy-MM-dd")
        $eventUnderCursor = if ($script:AllEvents.PSObject.Properties[$dateString]) { 
            $script:AllEvents.PSObject.Properties[$dateString].Value | Where-Object { 
                -not $_.IsAllDay -and $clickedTime -ge [datetime]$_.StartTime -and $clickedTime -le [datetime]$_.EndTime 
            } | Select-Object -First 1 
        } else { $null }

        $contextMenu = New-Object System.Windows.Forms.ContextMenuStrip
        if ($script:isDarkMode) { $contextMenu.Renderer = New-Object DarkModeRenderer }

        if ($eventUnderCursor) {
            # 既存イベント上の右クリック
            $script:selectedEvent = $eventUnderCursor
            $script:selectedTimeLog = $null
            $panel.Invalidate()
            $panel.Focus()

            $editEventMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("イベントを編集")
            $editEventMenuItem.Add_Click({ Start-EditEvent -eventToEdit $eventUnderCursor -eventDate $selectedDate }.GetNewClosure())
            $contextMenu.Items.Add($editEventMenuItem) | Out-Null

            $copyToActualMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("実績へコピー")
            $copyToActualMenuItem.Add_Click({
                if (Show-EventToTimeLogForm -eventObject $eventUnderCursor -date $selectedDate -eq [System.Windows.Forms.DialogResult]::OK) {
                    Update-AllViews
                }
            }.GetNewClosure())
            $contextMenu.Items.Add($copyToActualMenuItem) | Out-Null

            $deleteEventMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("イベントを削除")
            $deleteEventMenuItem.Tag = [PSCustomObject]@{ Event = $eventUnderCursor; DateString = $dateString }
            $deleteEventMenuItem.Add_Click({
                param($s, $ea)
                $data = $s.Tag
                $evt = $data.Event
                $dStr = $data.DateString

                if ($null -eq $script:AllEvents) { $script:AllEvents = Get-Events }
                if ([System.Windows.Forms.MessageBox]::Show("予定「$($evt.Title)」を削除しますか？", "削除の確認", "YesNo", "Warning") -eq "Yes") {
                    $prop = $script:AllEvents.PSObject.Properties | Where-Object { $_.Name -eq $dStr } | Select-Object -First 1
                    if (-not $prop) { return }
                    $eventsForDay = [System.Collections.ArrayList]@($prop.Value)
                    $itemToRemove = $null
                    foreach($item in $eventsForDay) { if ($item.ID -eq $evt.ID) { $itemToRemove = $item; break } }
                    if ($itemToRemove) {
                        $eventsForDay.Remove($itemToRemove)
                        $prop.Value = $eventsForDay.ToArray()
                        Save-Events; $script:selectedEvent = $null; Update-AllViews
                    }
                }
            })
            $contextMenu.Items.Add($deleteEventMenuItem) | Out-Null
        } else {
            # 空き地の右クリック
            $addEventMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("イベントを追加")
            $addEventMenuItem.Add_Click({ Start-AddNewEvent -initialDate $clickedTime }.GetNewClosure())
            $contextMenu.Items.Add($addEventMenuItem) | Out-Null
        }

        $contextMenu.Show($panel, $e.Location)
        return
    }

    $logUnderCursor = $null
    $logsForDay = $script:AllTimeLogs | Where-Object { $_.StartTime -and $_.EndTime -and ([datetime]$_.StartTime).Date -eq $selectedDate.Date }
    foreach ($log in $logsForDay) {
        if ($clickedTime -ge ([datetime]$log.StartTime) -and $clickedTime -le ([datetime]$log.EndTime)) {
            $logUnderCursor = $log
            break
        }
    }

    # 右クリックされたアイテムを選択状態にする
    $script:selectedTimeLog = $logUnderCursor
    $panel.Invalidate()
    $panel.Focus()

    if ($logUnderCursor) {
        $contextMenu = New-Object System.Windows.Forms.ContextMenuStrip
        if ($script:isDarkMode) { $contextMenu.Renderer = New-Object DarkModeRenderer }
        
        $logIndex = -1
        for ($i = 0; $i -lt $script:AllTimeLogs.Count; $i++) {
            if ([object]::ReferenceEquals($script:AllTimeLogs[$i], $logUnderCursor)) {
                $logIndex = $i
                break
            }
        }

        if ($logIndex -eq -1) { return }

        $adjustMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("時間の詳細調整...")
        $adjustMenuItem.Tag = $logIndex
        $adjustMenuItem.Add_Click({
            param($sourceItem, $e)
            $idx = $sourceItem.Tag
            $logToAdjust = $script:AllTimeLogs[$idx]

            $newTimes = Show-TimeLogEntryForm -log $logToAdjust -tasks $script:AllTasks -projects $script:Projects
            if ($newTimes) {
                if (Resolve-TimeLogOverlap -NewStartTime $newTimes.StartTime -NewEndTime $newTimes.EndTime -LogToExclude $logToAdjust) {
                    # Overwriteモードでログが再構築された場合の対策
                    if ($script:AllTimeLogs -notcontains $logToAdjust) { $script:AllTimeLogs += $logToAdjust }
                    $logToAdjust.StartTime = $newTimes.StartTime.ToString("o")
                    $logToAdjust.EndTime = $newTimes.EndTime.ToString("o")
                    Save-TimeLogs
                    $selectedDate = if ($script:timelinePanel.Tag -is [datetime]) { $script:timelinePanel.Tag } else { (Get-Date).Date }
                    Update-TimelineView -date $selectedDate
                    $script:selectedTimeLog = $logToAdjust
                    $script:timelinePanel.Refresh()
                    Update-DataGridView
                }
            }
        })
        $contextMenu.Items.Add($adjustMenuItem)

        # --- 次の記録まで延長 ---
        $extendMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("次の記録まで延長")
        $extendMenuItem.Tag = $logIndex
        $extendMenuItem.Add_Click({
            param($sourceItem, $e)
            $idx = $sourceItem.Tag
            $logToExtend = $script:AllTimeLogs[$idx]
            $logDate = ([datetime]$logToExtend.StartTime).Date
            $currentEndTime = [datetime]$logToExtend.EndTime

            # 修正: 現在のログの終了時刻以降に開始する、同じ日の次のログを検索する
            # これにより、重複しているログや開始時刻が同じログによる「短縮/最小化」を防ぐ
            $nextLog = $script:AllTimeLogs | 
                Where-Object { 
                    $_.StartTime -and 
                    ([datetime]$_.StartTime).Date -eq $logDate -and 
                    [datetime]$_.StartTime -ge $currentEndTime -and
                    -not ([object]::ReferenceEquals($_, $logToExtend))
                } | 
                Sort-Object { [datetime]$_.StartTime } | 
                Select-Object -First 1

            if ($nextLog) {
                $newEndTime = [datetime]$nextLog.StartTime
                
                if ($newEndTime -le $currentEndTime) {
                     [System.Windows.Forms.MessageBox]::Show("次の記録と既に接しているか、延長できる隙間がありません。", "情報", "OK", "Information")
                     return
                }

                if (Resolve-TimeLogOverlap -NewStartTime ([datetime]$logToExtend.StartTime) -NewEndTime $newEndTime -LogToExclude $logToExtend) {
                    $logToExtend.EndTime = $newEndTime.ToString("o")
                    Save-TimeLogs
                    Update-AllViews
                }
            } else {
                [System.Windows.Forms.MessageBox]::Show("この後に延長できる記録はありません。", "情報", "OK", "Information")
            }
        })
        $contextMenu.Items.Add($extendMenuItem)

        # --- 開始時間の調整メニュー ---
        $adjustStartMenu = New-Object System.Windows.Forms.ToolStripMenuItem("開始時間の調整")

        $startEarlierItem = New-Object System.Windows.Forms.ToolStripMenuItem("5分延長 (早める)")
        $startEarlierItem.Tag = $logIndex
        $startEarlierItem.Add_Click({
            param($sourceItem, $e)
            $idx = $sourceItem.Tag
            $logToAdjust = $script:AllTimeLogs[$idx]
            if ($logToAdjust.StartTime) {
                $newStart = ([datetime]$logToAdjust.StartTime).AddMinutes(-5)
                if (Resolve-TimeLogOverlap -NewStartTime $newStart -NewEndTime ([datetime]$logToAdjust.EndTime) -LogToExclude $logToAdjust) {
                    if ($script:AllTimeLogs -notcontains $logToAdjust) { $script:AllTimeLogs += $logToAdjust }
                    $logToAdjust.StartTime = $newStart.ToString("o")
                    Save-TimeLogs
                    $selectedDate = if ($script:timelinePanel.Tag -is [datetime]) { $script:timelinePanel.Tag } else { (Get-Date).Date }
                    Update-TimelineView -date $selectedDate
                    $script:selectedTimeLog = $logToAdjust
                    $script:timelinePanel.Refresh()
                    Update-DataGridView
                }
            }
        })
        $adjustStartMenu.DropDownItems.Add($startEarlierItem)

        $startLaterItem = New-Object System.Windows.Forms.ToolStripMenuItem("5分短縮 (遅らせる)")
        $startLaterItem.Tag = $logIndex
        $startLaterItem.Add_Click({
            param($sourceItem, $e)
            $idx = $sourceItem.Tag
            $logToAdjust = $script:AllTimeLogs[$idx]
            if ($logToAdjust.StartTime -and $logToAdjust.EndTime) {
                $newStart = ([datetime]$logToAdjust.StartTime).AddMinutes(5)
                if ($newStart -lt ([datetime]$logToAdjust.EndTime)) {
                    if (Resolve-TimeLogOverlap -NewStartTime $newStart -NewEndTime ([datetime]$logToAdjust.EndTime) -LogToExclude $logToAdjust) {
                        if ($script:AllTimeLogs -notcontains $logToAdjust) { $script:AllTimeLogs += $logToAdjust }
                        $logToAdjust.StartTime = $newStart.ToString("o")
                        Save-TimeLogs
                        $selectedDate = if ($script:timelinePanel.Tag -is [datetime]) { $script:timelinePanel.Tag } else { (Get-Date).Date }
                        Update-TimelineView -date $selectedDate
                        $script:selectedTimeLog = $logToAdjust
                        $script:timelinePanel.Refresh()
                        Update-DataGridView
                    }
                }
            }
        })
        $adjustStartMenu.DropDownItems.Add($startLaterItem)
        $contextMenu.Items.Add($adjustStartMenu)

        # --- 終了時間の調整メニュー ---
        $adjustEndMenu = New-Object System.Windows.Forms.ToolStripMenuItem("終了時間の調整")

        $endLaterItem = New-Object System.Windows.Forms.ToolStripMenuItem("5分延長 (遅らせる)")
        $endLaterItem.Tag = $logIndex
        $endLaterItem.Add_Click({
            param($sourceItem, $e)
            $idx = $sourceItem.Tag
            $logToAdjust = $script:AllTimeLogs[$idx]
            if ($logToAdjust.EndTime) {
                $newEnd = ([datetime]$logToAdjust.EndTime).AddMinutes(5)
                if (Resolve-TimeLogOverlap -NewStartTime ([datetime]$logToAdjust.StartTime) -NewEndTime $newEnd -LogToExclude $logToAdjust) {
                    if ($script:AllTimeLogs -notcontains $logToAdjust) { $script:AllTimeLogs += $logToAdjust }
                    $logToAdjust.EndTime = $newEnd.ToString("o")
                    Save-TimeLogs
                    $selectedDate = if ($script:timelinePanel.Tag -is [datetime]) { $script:timelinePanel.Tag } else { (Get-Date).Date }
                    Update-TimelineView -date $selectedDate
                    $script:selectedTimeLog = $logToAdjust
                    $script:timelinePanel.Refresh()
                    Update-DataGridView
                }
            }
        })
        $adjustEndMenu.DropDownItems.Add($endLaterItem)

        $endEarlierItem = New-Object System.Windows.Forms.ToolStripMenuItem("5分短縮 (早める)")
        $endEarlierItem.Tag = $logIndex
        $endEarlierItem.Add_Click({
            param($sourceItem, $e)
            $idx = $sourceItem.Tag
            $logToAdjust = $script:AllTimeLogs[$idx]
            if ($logToAdjust.StartTime -and $logToAdjust.EndTime) {
                $newEnd = ([datetime]$logToAdjust.EndTime).AddMinutes(-5)
                if ($newEnd -gt ([datetime]$logToAdjust.StartTime)) {
                    if (Resolve-TimeLogOverlap -NewStartTime ([datetime]$logToAdjust.StartTime) -NewEndTime $newEnd -LogToExclude $logToAdjust) {
                        if ($script:AllTimeLogs -notcontains $logToAdjust) { $script:AllTimeLogs += $logToAdjust }
                        $logToAdjust.EndTime = $newEnd.ToString("o")
                        Save-TimeLogs
                        $selectedDate = if ($script:timelinePanel.Tag -is [datetime]) { $script:timelinePanel.Tag } else { (Get-Date).Date }
                        Update-TimelineView -date $selectedDate
                        $script:selectedTimeLog = $logToAdjust
                        $script:timelinePanel.Refresh()
                        Update-DataGridView
                    }
                }
            }
        })
        $adjustEndMenu.DropDownItems.Add($endEarlierItem)
        $contextMenu.Items.Add($adjustEndMenu)

        $deleteMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("この記録を削除")
        $deleteMenuItem.Tag = $logIndex
        $deleteMenuItem.Add_Click({
            param($sourceItem, $e)
            if ([System.Windows.Forms.MessageBox]::Show("この時間記録を削除しますか？", "確認", "YesNo", "Question") -eq "Yes") {
                $indexToDelete = $sourceItem.Tag

                if ($indexToDelete -ge 0 -and $indexToDelete -lt $script:AllTimeLogs.Count) {
                    # 削除対象が選択中のログであれば、選択を解除
                    if ($script:selectedTimeLog -and [object]::ReferenceEquals($script:AllTimeLogs[$indexToDelete], $script:selectedTimeLog)) {
                        $script:selectedTimeLog = $null
                    }
                    $newLogs = [System.Collections.ArrayList]::new()
                    $newLogs.AddRange($script:AllTimeLogs)
                    $newLogs.RemoveAt($indexToDelete)
                    $script:AllTimeLogs = $newLogs.ToArray()
                } else {
                    Write-Warning "削除しようとしたログのインデックスが無効です: $indexToDelete"
                }
       
                Save-TimeLogs
                $selectedDate = if ($script:timelinePanel.Tag -is [datetime]) { $script:timelinePanel.Tag } else { (Get-Date).Date }
                Update-TimelineView -date $selectedDate
                $script:timelinePanel.Invalidate()
            }
        })
        $contextMenu.Items.Add($deleteMenuItem)

        $contextMenu.Show($panel, $e.Location)
    } else {
        # --- 右側 (実績) 空き地の右クリック ---
        $contextMenu = New-Object System.Windows.Forms.ContextMenuStrip
        if ($script:isDarkMode) { $contextMenu.Renderer = New-Object DarkModeRenderer }
        $addLogMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("時間記録を追加")
        $startTime = $clickedTime.AddMinutes(-($clickedTime.Minute % 15))
        $localProjects = $script:Projects
        $localTasks = $script:AllTasks
        $addLogMenuItem.Add_Click({
            $endTime = $startTime.AddMinutes(30)
            $result = Show-TimeLogEntryForm -InitialStartTime $startTime -InitialEndTime $endTime -projects $localProjects -tasks $localTasks
            if ($result -and (Resolve-TimeLogOverlap -NewStartTime $result.StartTime -NewEndTime $result.EndTime)) {
                $newLog = [PSCustomObject]@{ ID = [guid]::NewGuid().ToString(); TaskID = if ($result.Task) { $result.Task.ID } else { $null }; Memo = if (-not $result.Task) { $result.Memo } else { $null }; StartTime = $result.StartTime.ToString("o"); EndTime = $result.EndTime.ToString("o") }
                $script:AllTimeLogs += $newLog; Save-TimeLogs; Update-AllViews
            }
        }.GetNewClosure())
        $contextMenu.Items.Add($addLogMenuItem) | Out-Null

        # --- この空き時間を埋める ---
        $fillGapMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("この空き時間を埋める")
        $fillGapMenuItem.Tag = $clickedTime
        $fillGapMenuItem.Add_Click({
            param($sourceItem, $e)
            $clickTime = $sourceItem.Tag
            $logDate = $clickTime.Date

            $logsOnDay = $script:AllTimeLogs | 
                Where-Object { $_.StartTime -and ([datetime]$_.StartTime).Date -eq $logDate } | 
                Sort-Object { [datetime]$_.StartTime }

            $prevLog = $logsOnDay | Where-Object { [datetime]$_.EndTime -le $clickTime } | Select-Object -Last 1
            $nextLog = $logsOnDay | Where-Object { [datetime]$_.StartTime -ge $clickTime } | Select-Object -First 1

            $startTime = if ($prevLog) { [datetime]$prevLog.EndTime } else { $logDate.AddHours(8) }
            $endTime = if ($nextLog) { [datetime]$nextLog.StartTime } else { $logDate.AddHours(24) }

            if ($endTime -gt $startTime) {
                $result = Show-TimeLogEntryForm -InitialStartTime $startTime -InitialEndTime $endTime -projects $script:Projects -tasks $script:AllTasks
                if ($result -and (Resolve-TimeLogOverlap -NewStartTime $result.StartTime -NewEndTime $result.EndTime)) {
                    $newLog = [PSCustomObject]@{ 
                        ID = [guid]::NewGuid().ToString(); 
                        TaskID = if ($result.Task) { $result.Task.ID } else { $null }; 
                        Memo = if (-not $result.Task) { $result.Memo } else { $null }; 
                        StartTime = $result.StartTime.ToString("o"); 
                        EndTime = $result.EndTime.ToString("o") 
                    }
                    $script:AllTimeLogs += $newLog; Save-TimeLogs; Update-AllViews
                }
            }
        }.GetNewClosure())
        $contextMenu.Items.Add($fillGapMenuItem) | Out-Null

        $contextMenu.Show($panel, $e.Location)
    }
})

$script:timelinePanel.Add_KeyDown({
    param($source, $e)

    if ($e.KeyCode -eq [System.Windows.Forms.Keys]::Delete) {
        if ($null -ne $script:selectedTimeLog) {
            $logToDelete = $script:selectedTimeLog
            $confirmResult = [System.Windows.Forms.MessageBox]::Show("選択した時間記録を削除しますか？", "削除の確認", "YesNo", "Warning")

            if ($confirmResult -eq 'Yes') {
                $newLogs = [System.Collections.ArrayList]::new()
                $newLogs.AddRange($script:AllTimeLogs)
                
                $logToRemove = $null
                foreach($log in $newLogs){
                    if([object]::ReferenceEquals($log, $logToDelete)){
                        $logToRemove = $log
                        break
                    }
                }
                if($logToRemove){
                    $newLogs.Remove($logToRemove)
                }

                $script:AllTimeLogs = $newLogs.ToArray()
                $script:selectedTimeLog = $null

                Save-TimeLogs

                $selectedDate = if ($script:timelinePanel.Tag -is [datetime]) { $script:timelinePanel.Tag } else { (Get-Date).Date }
                Update-TimelineView -date $selectedDate
                Update-DataGridView 
            }
        } elseif ($null -ne $script:selectedEvent) {
            $eventToDelete = $script:selectedEvent
            $selectedDate = if ($script:timelinePanel.Tag -is [datetime]) { $script:timelinePanel.Tag } else { (Get-Date).Date }
            $dateString = $selectedDate.ToString("yyyy-MM-dd")

            if ([System.Windows.Forms.MessageBox]::Show("予定「$($eventToDelete.Title)」を削除しますか？", "削除の確認", "YesNo", "Warning") -eq "Yes") {
                if ($null -eq $script:AllEvents) { $script:AllEvents = Get-Events }
                $prop = $script:AllEvents.PSObject.Properties | Where-Object { $_.Name -eq $dateString } | Select-Object -First 1
                if (-not $prop) { return }
                $eventsForDay = [System.Collections.ArrayList]@($prop.Value)
                $itemToRemove = $null
                foreach($item in $eventsForDay) {
                    if ($item.ID -eq $eventToDelete.ID) { $itemToRemove = $item; break }
                }

                if ($itemToRemove) {
                    $eventsForDay.Remove($itemToRemove)
                    $prop.Value = $eventsForDay.ToArray()

                    Save-Events
                    $script:selectedEvent = $null
                    Update-AllViews
                }
            }
        }
    }
})

# --- タイムラインの描画イベントハンドラ ---
$script:timelinePaintHandler = {
    param($source, $e)

    $panel = $source
    $g = $e.Graphics
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # --- Drawing Area and Time Scale ---
    $topMargin = 35; $bottomMargin = 10
    $leftMargin = 50 # Space for time labels
    $startHour = if ($script:Settings.TimelineStartHour) { [int]$script:Settings.TimelineStartHour } else { 8 }
    $endHour = if ($script:Settings.TimelineEndHour) { [int]$script:Settings.TimelineEndHour } else { 24 }
    $totalHours = $endHour - $startHour

    $viewHeight = $panel.Height - $topMargin - $bottomMargin
    $viewWidth = $panel.Width
    $centerX = $viewWidth / 2

    # Clear background
    $bgColor = if ($script:isDarkMode) { [System.Drawing.Color]::FromArgb(45, 45, 48) } else { [System.Drawing.SystemColors]::Window }
    $g.Clear($bgColor)

    if ($viewHeight -le 0) { return }

    $pixelsPerMinute = $viewHeight / ($totalHours * 60)

    # --- フォントとブラシ ---
    $hourFont = New-Object System.Drawing.Font("Meiryo UI", 8)
    $headerFont = New-Object System.Drawing.Font("Meiryo UI", 10, [System.Drawing.FontStyle]::Bold)
    $itemFont = New-Object System.Drawing.Font("Meiryo UI", 8)
    $itemTextBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $lineColor = if ($script:isDarkMode) { [System.Drawing.Color]::FromArgb(70, 70, 70) } else { [System.Drawing.Color]::LightGray }
    $linePen = New-Object System.Drawing.Pen($lineColor)
    $sepColor = if ($script:isDarkMode) { [System.Drawing.Color]::Gray } else { [System.Drawing.Color]::DarkGray }
    $separatorPen = New-Object System.Drawing.Pen($sepColor, 2)
    $textColor = if ($script:isDarkMode) { [System.Drawing.Color]::Silver } else { [System.Drawing.Color]::DimGray }
    $textBrush = New-Object System.Drawing.SolidBrush($textColor)

    # --- 1. 時間グリッドとラベルを描画 (全体) ---
    for ($hour = $startHour; $hour -le $endHour; $hour++) {
        $y = $topMargin + (($hour - $startHour) * 60 * $pixelsPerMinute)
        $g.DrawLine($linePen, $leftMargin - 5, $y, $viewWidth, $y)
        $timeString = "{0:D2}:00" -f $hour
        $g.DrawString($timeString, $hourFont, $textBrush, 5, $y - 7)
        if ($hour -lt $endHour) {
            $halfHourY = $y + (30 * $pixelsPerMinute)
            $g.DrawLine($linePen, $leftMargin - 2, $halfHourY, $viewWidth, $halfHourY)
        }
    }

    # --- 2. 垂直区切り線とヘッダーを描画 ---
    $g.DrawLine($separatorPen, $centerX, $topMargin - 10, $centerX, $panel.Height - $bottomMargin)
    $g.DrawString("予定", $headerFont, $textBrush, $leftMargin + ($centerX - $leftMargin) / 2 - 20, 10)
    $g.DrawString("実績", $headerFont, $textBrush, $centerX + ($viewWidth - $centerX) / 2 - 20, 10)

    # --- 選択日の取得 ---
    # --- 選択日の取得と現在時刻線の描画 ---
    $selectedDate = if ($panel.Tag -is [datetime]) { $panel.Tag.Date } else { (Get-Date).Date }
    $dateString = $selectedDate.ToString("yyyy-MM-dd")

    # 現在時刻線を描画 (今日の場合のみ)
    if ($selectedDate -eq (Get-Date).Date) {
        $now = Get-Date
        if ($now.Hour -ge $startHour -and $now.Hour -lt $endHour) {
            $nowMinutes = ($now.Hour - $startHour) * 60 + $now.Minute
            $nowY = $topMargin + ($nowMinutes * $pixelsPerMinute)
            $nowLinePen = New-Object System.Drawing.Pen([System.Drawing.Color]::Red, 2)
            $g.DrawLine($nowLinePen, $leftMargin, $nowY, $viewWidth, $nowY)
            $nowLinePen.Dispose()
        }
    }

    # --- 3. 左側: 予定 (Plan) エリア ---
    $eventsOnDay = if ($script:AllEvents.PSObject.Properties[$dateString]) { @($script:AllEvents.PSObject.Properties[$dateString].Value) } else { @() }
    $eventBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(180, 15, 123, 255))

    foreach ($evt in $eventsOnDay) {
        if ($evt.IsAllDay -or -not $evt.StartTime -or -not $evt.EndTime) { continue }
        $startTime = [datetime]$evt.StartTime
        $endTime = [datetime]$evt.EndTime
        if ($startTime.Date -gt $selectedDate -or $endTime.Date -lt $selectedDate) { continue }

        $startMin = if ($startTime.Date -lt $selectedDate) { 0 } else { ($startTime.Hour - $startHour) * 60 + $startTime.Minute }
        $endMin = if ($endTime.Date -gt $selectedDate) { $totalHours * 60 } else { ($endTime.Hour - $startHour) * 60 + $endTime.Minute }

        $itemY = $topMargin + ($startMin * $pixelsPerMinute)
        $itemHeight = ($endMin - $startMin) * $pixelsPerMinute
        if ($itemHeight -lt 1) { continue }

        $itemRect = [System.Drawing.RectangleF]::new($leftMargin + 2, $itemY, $centerX - $leftMargin - 4, $itemHeight)
        $g.FillRectangle($eventBrush, $itemRect)

        $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
        if ($itemHeight -gt 15) {
            $g.DrawString($evt.Title, $itemFont, $itemTextBrush, $itemRect, $sf)
        }
        $sf.Dispose()
    }
    $eventBrush.Dispose()

    # --- 4. 右側: 実績 (Actual) エリア ---
    $logsForDay = $script:AllTimeLogs | Where-Object { $_.StartTime -and $_.EndTime -and ([datetime]$_.StartTime).Date -eq $selectedDate }

    foreach ($log in $logsForDay) {
        $startTime = [datetime]$log.StartTime
        $endTime = [datetime]$log.EndTime

        $logStartMin = ($startTime.Hour - $startHour) * 60 + $startTime.Minute
        $logEndMin = ($endTime.Hour - $startHour) * 60 + $endTime.Minute
        $logY = $topMargin + ($logStartMin * $pixelsPerMinute)
        $logHeight = ($logEndMin - $logStartMin) * $pixelsPerMinute
        if ($logHeight -lt 1) { $logHeight = 1 }

        $task = $script:AllTasks | Where-Object { $_.ID -eq $log.TaskID } | Select-Object -First 1
        $logText = ""
        $projectColor = [System.Drawing.Color]::Gray

        if ($task) {
            $project = $script:Projects | Where-Object { $_.ProjectID -eq $task.ProjectID } | Select-Object -First 1
            $logText = if ($project) { "$($project.ProjectName) - $($task.タスク)" } else { $task.タスク }
            if ($project -and $project.ProjectColor) {
                try { $projectColor = [System.Drawing.ColorTranslator]::FromHtml($project.ProjectColor) } catch {}
            }
        } elseif ($log.Memo) {
            $logText = "[実績] " + $log.Memo
            $projectColor = [System.Drawing.Color]::SlateGray
        }

        $logBrush = New-Object System.Drawing.SolidBrush($projectColor)
        $logRect = [System.Drawing.RectangleF]::new($centerX + 2, $logY, $viewWidth - $centerX - 4, $logHeight)
        $g.FillRectangle($logBrush, $logRect)

        if ($script:selectedTimeLog -and [object]::ReferenceEquals($script:selectedTimeLog, $log)) {
            $selectionPen = New-Object System.Drawing.Pen([System.Drawing.Color]::Black, 2)
            $selectionPen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dot
            $g.DrawRectangle($selectionPen, $logRect.X, $logRect.Y, $logRect.Width, $logRect.Height)
            $selectionPen.Dispose()
        }

        $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
        if ($logRect.Height -gt 15) {
            $textRectWidth = [System.Math]::Max(1, $logRect.Width - 8)
            $textRect = [System.Drawing.RectangleF]::new($logRect.X + 4, $logRect.Y + 2, $textRectWidth, $logRect.Height - 4)
            $g.DrawString($logText, $itemFont, $itemTextBrush, $textRect, $sf)
        }
        $sf.Dispose()

        $logBrush.Dispose()
    }

    # --- 5. ドラッグ中のゴーストとスナップ線を描画 ---
    if ($panel.Capture -and -not $script:ghostRect.IsEmpty) {
        $ghostBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100, 0, 120, 215))
        $g.FillRectangle($ghostBrush, $script:ghostRect)
        $ghostBrush.Dispose()
    }
    if ($script:snapLineY -gt -1) {
        $snapPen = New-Object System.Drawing.Pen([System.Drawing.Color]::Red, 1)
        $snapPen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash
        $g.DrawLine($snapPen, $centerX, $script:snapLineY, $panel.Width, $script:snapLineY)
        $snapPen.Dispose()
    }

    # --- Cleanup ---
    $hourFont.Dispose()
    $headerFont.Dispose()
    $itemFont.Dispose()
    $itemTextBrush.Dispose()
    $linePen.Dispose()
    $separatorPen.Dispose()

    $textBrush.Dispose()
}
$script:timelinePanel.Add_Paint($script:timelinePaintHandler)


# --- 下部パネル (関連ファイルとプレビュー) ---
$mainContainer.Panel2.Padding = "0, 5, 0, 0"
$associatedFilesSplitContainer = New-Object System.Windows.Forms.SplitContainer
$associatedFilesSplitContainer.Dock = "Fill"; $associatedFilesSplitContainer.Orientation = "Vertical"
$mainContainer.Panel2.Controls.Add($associatedFilesSplitContainer)

$associatedFilesGroup = New-Object System.Windows.Forms.GroupBox; $associatedFilesGroup.Text = "関連ファイル"; $associatedFilesGroup.Dock = "Fill"
$associatedFilesSplitContainer.Panel1.Controls.Add($associatedFilesGroup)

$script:fileListView = New-Object System.Windows.Forms.ListView; $script:fileListView.Dock = "Fill"; $script:fileListView.View = "Details"; $script:fileListView.AllowDrop = $true; $script:fileListView.FullRowSelect = $true
$script:fileListView.Columns.Add("ファイル名", 300) | Out-Null; $script:fileListView.Columns.Add("種類", 100) | Out-Null; $script:fileListView.Columns.Add("追加日", 150) | Out-Null; $script:fileListView.SmallImageList = $script:globalImageList; $script:fileListView.MultiSelect = $true
$associatedFilesGroup.Controls.Add($script:fileListView)

$previewGroup = New-Object System.Windows.Forms.GroupBox; $previewGroup.Text = "プレビュー"; $previewGroup.Dock = "Fill"
$associatedFilesSplitContainer.Panel2.Controls.Add($previewGroup)
$script:previewPanel = New-Object System.Windows.Forms.Panel; $script:previewPanel.Dock = "Fill"; $script:previewPanel.AutoScroll = $true
$previewGroup.Controls.Add($script:previewPanel)

# 変更点 3: フォームのLoadイベントハンドラを追加して、SplitterDistanceを動的に設定
$mainForm.Add_Load({
    param($source, $e)
    try {
        # フォームが表示された後に、実際のクライアントサイズに基づいて分割位置を決定
        if (-not $mainContainer.IsDisposed) {
            $mainContainer.SplitterDistance = [int]($mainForm.ClientSize.Height * 0.65)
        }
        if (-not $associatedFilesSplitContainer.IsDisposed) {
            # 左右分割コンテナの分割位置を中央に設定
            $associatedFilesSplitContainer.SplitterDistance = [int]($associatedFilesSplitContainer.Width / 2)
        }
    } catch {
        # フォームクロージング中にエラーが発生するのを防ぐ
    }
})

# --- 関連ファイルリストの右クリックメニュー ---
$fileListContextMenu = New-Object System.Windows.Forms.ContextMenuStrip
$renameFileMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("名前の変更"); $openLocationMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("ファイルの場所を開く"); $copyPathMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("パスをコピー"); $addUrlMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("URLを追加..."); $addMemoMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("メモを追加..."); $fileMenuSeparator = New-Object System.Windows.Forms.ToolStripSeparator; $deleteFileMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("削除")
$fileListContextMenu.Items.AddRange(@($renameFileMenuItem, $openLocationMenuItem, $copyPathMenuItem, $addUrlMenuItem, $addMemoMenuItem, $fileMenuSeparator, $deleteFileMenuItem))
$script:fileListView.ContextMenuStrip = $fileListContextMenu

# --- 全ビュー更新用関数 ---
function Update-KanbanColumnsVisibility {
    if (-not $script:kanbanLayout -or $script:kanbanLayout.IsDisposed) { return }
    
    $showCompleted = $true
    if ($script:Settings.PSObject.Properties.Name -contains 'ShowKanbanDone') {
        $showCompleted = $script:Settings.ShowKanbanDone
    }

    $completedIndex = $script:TaskStatuses.IndexOf("完了済み")
    if ($completedIndex -lt 0) { return }

    $script:kanbanLayout.SuspendLayout()
    try {
        $visibleCount = if ($showCompleted) { $script:TaskStatuses.Count } else { $script:TaskStatuses.Count - 1 }
        if ($visibleCount -lt 1) { $visibleCount = 1 }
        $percentWidth = 100.0 / $visibleCount

        for ($i = 0; $i -lt $script:kanbanLayout.ColumnStyles.Count; $i++) {
            if ($i -eq $completedIndex) {
                if ($showCompleted) {
                    $script:kanbanLayout.ColumnStyles[$i].SizeType = [System.Windows.Forms.SizeType]::Percent
                    $script:kanbanLayout.ColumnStyles[$i].Width = $percentWidth
                } else {
                    $script:kanbanLayout.ColumnStyles[$i].SizeType = [System.Windows.Forms.SizeType]::Absolute
                    $script:kanbanLayout.ColumnStyles[$i].Width = 0
                }
            } else {
                $script:kanbanLayout.ColumnStyles[$i].SizeType = [System.Windows.Forms.SizeType]::Percent
                $script:kanbanLayout.ColumnStyles[$i].Width = $percentWidth
            }
        }
    } finally {
        $script:kanbanLayout.ResumeLayout($true)
    }
}

function Update-Theme {
    param([bool]$isDarkMode)
    
    $script:isDarkMode = $isDarkMode
    if ($script:Settings -and $script:Settings.PSObject.Properties.Name -contains "IsDarkMode") {
        $script:Settings.IsDarkMode = $isDarkMode
    } elseif ($script:Settings -is [System.Management.Automation.PSCustomObject]) {
        $script:Settings | Add-Member -MemberType NoteProperty -Name "IsDarkMode" -Value $isDarkMode -Force -ErrorAction SilentlyContinue
    }
    Save-DataFile -filePath $script:SettingsFile -dataObject $script:Settings

    # --- ウィンドウ枠（タイトルバー）のダークモード適用 (Windows 10/11) ---
    try {
        $hwnd = $mainForm.Handle
        $attr = 20 # DWMWA_USE_IMMERSIVE_DARK_MODE
        $val = if ($isDarkMode) { 1 } else { 0 }
        $size = [System.Runtime.InteropServices.Marshal]::SizeOf($val)
        [void][TaskManager.WinAPI.Dwmapi]::DwmSetWindowAttribute($hwnd, $attr, [ref]$val, $size)
    } catch {}

    # メインフォームのコントロール（スクロールバー等）にテーマを適用
    Set-Theme -form $mainForm -IsDarkMode $isDarkMode

    # --- メインフォームのUI要素へのテーマ適用 ---
    if ($isDarkMode) {
        # ダークモード
        $darkBack = [System.Drawing.Color]::FromArgb(30, 30, 30)
        $darkControl = [System.Drawing.Color]::FromArgb(45, 45, 48)
        $darkFore = [System.Drawing.Color]::White
        
        $mainForm.BackColor = $darkBack
        $mainForm.ForeColor = $darkFore
        
        try {
            if ($mainMenu.Renderer -isnot [DarkModeRenderer]) {
                $mainMenu.Renderer = New-Object DarkModeRenderer
                $toolStrip.Renderer = New-Object DarkModeRenderer
                $statusBar.Renderer = New-Object DarkModeRenderer
            }
        } catch { Write-Warning "DarkModeRendererの適用に失敗しました: $($_.Exception.Message)" }
        
        $mainMenu.BackColor = $darkControl
        $mainMenu.ForeColor = $darkFore
        $toolStrip.BackColor = $darkControl
        $toolStrip.ForeColor = $darkFore
        $statusBar.BackColor = $darkControl
        $statusBar.ForeColor = $darkFore
        
        # コンテナの背景色を設定 (TabControlのヘッダー背景対策)
        $mainContainer.BackColor = $darkBack
        $mainContainer.Panel1.BackColor = $darkBack
        $mainContainer.Panel2.BackColor = $darkBack
        $associatedFilesSplitContainer.BackColor = $darkBack
        $associatedFilesSplitContainer.Panel1.BackColor = $darkBack
        $associatedFilesSplitContainer.Panel2.BackColor = $darkBack
        $calendarSplitContainer.BackColor = $darkBack
        $calendarLeftSplitContainer.BackColor = $darkBack

        $tabControl.BackColor = $darkBack
        $tabControl.ForeColor = $darkFore
        foreach ($p in $tabControl.TabPages) { $p.BackColor = $darkBack; $p.ForeColor = $darkFore }
        
        $script:taskDataGridView.BackgroundColor = $darkBack
        $script:taskDataGridView.DefaultCellStyle.BackColor = $darkBack
        $script:taskDataGridView.DefaultCellStyle.ForeColor = $darkFore
        $script:taskDataGridView.ColumnHeadersDefaultCellStyle.BackColor = $darkControl
        $script:taskDataGridView.ColumnHeadersDefaultCellStyle.ForeColor = $darkFore
        $script:taskDataGridView.GridColor = $darkControl
        $script:taskDataGridView.EnableHeadersVisualStyles = $false
        
        $script:kanbanLayout.BackColor = $darkBack
        foreach ($lb in $script:kanbanLists.Values) { $lb.BackColor = $darkBack; $lb.ForeColor = $darkFore }
        
        $script:calendarGrid.BackColor = $darkBack
        
        $script:fileListView.BackColor = $darkBack
        $script:fileListView.ForeColor = $darkFore
        $associatedFilesGroup.BackColor = $darkBack
        $script:previewPanel.BackColor = $darkBack
        $script:previewPanel.ForeColor = $darkFore
        $associatedFilesGroup.ForeColor = $darkFore
        $previewGroup.ForeColor = $darkFore
        $dayInfoGroupBox.ForeColor = $darkFore
        $script:dayInfoEventsGroup.ForeColor = $darkFore
        $script:dayInfoTasksGroup.ForeColor = $darkFore

        
        # Calendar Navigation
        $script:lblMonthYear.ForeColor = $darkFore
        foreach ($btn in @($script:btnPrevYear, $script:btnPrevMonth, $script:btnNextMonth, $script:btnNextYear)) {
            $btn.BackColor = $darkControl
            $btn.ForeColor = $darkFore
            $btn.FlatStyle = 'Flat'
        }
        
    } else {
        # ライトモード
        $mainForm.BackColor = [System.Drawing.SystemColors]::Control
        $mainForm.ForeColor = [System.Drawing.SystemColors]::ControlText
        
        $mainMenu.Renderer = $null
        $mainMenu.RenderMode = [System.Windows.Forms.ToolStripRenderMode]::System
        $mainMenu.BackColor = [System.Drawing.SystemColors]::Control
        $mainMenu.ForeColor = [System.Drawing.SystemColors]::ControlText
        
        $toolStrip.Renderer = $null
        $toolStrip.RenderMode = [System.Windows.Forms.ToolStripRenderMode]::System
        $toolStrip.BackColor = [System.Drawing.SystemColors]::Control
        $toolStrip.ForeColor = [System.Drawing.SystemColors]::ControlText

        $statusBar.Renderer = $null
        $statusBar.RenderMode = [System.Windows.Forms.ToolStripRenderMode]::System
        $statusBar.BackColor = [System.Drawing.SystemColors]::Control
        $statusBar.ForeColor = [System.Drawing.SystemColors]::ControlText
        
        # コンテナの背景色をリセット
        $mainContainer.BackColor = [System.Drawing.SystemColors]::Control
        $mainContainer.Panel1.BackColor = [System.Drawing.SystemColors]::Control
        $mainContainer.Panel2.BackColor = [System.Drawing.SystemColors]::Control
        $associatedFilesSplitContainer.BackColor = [System.Drawing.SystemColors]::Control
        $associatedFilesSplitContainer.Panel1.BackColor = [System.Drawing.SystemColors]::Control
        $associatedFilesSplitContainer.Panel2.BackColor = [System.Drawing.SystemColors]::Control
        $calendarSplitContainer.BackColor = [System.Drawing.SystemColors]::Control
        $calendarLeftSplitContainer.BackColor = [System.Drawing.SystemColors]::Control

        $tabControl.BackColor = [System.Drawing.SystemColors]::Control
        $tabControl.ForeColor = [System.Drawing.SystemColors]::ControlText
        foreach ($p in $tabControl.TabPages) { $p.BackColor = [System.Drawing.Color]::White; $p.ForeColor = [System.Drawing.SystemColors]::ControlText }
        
        $script:taskDataGridView.BackgroundColor = [System.Drawing.SystemColors]::AppWorkspace
        $script:taskDataGridView.DefaultCellStyle.BackColor = [System.Drawing.SystemColors]::Window
        $script:taskDataGridView.DefaultCellStyle.ForeColor = [System.Drawing.SystemColors]::ControlText
        $script:taskDataGridView.ColumnHeadersDefaultCellStyle.BackColor = [System.Drawing.SystemColors]::Control
        $script:taskDataGridView.ColumnHeadersDefaultCellStyle.ForeColor = [System.Drawing.SystemColors]::WindowText
        $script:taskDataGridView.GridColor = [System.Drawing.Color]::LightGray
        $script:taskDataGridView.EnableHeadersVisualStyles = $true
        
        $script:kanbanLayout.BackColor = [System.Drawing.SystemColors]::Control
        foreach ($lb in $script:kanbanLists.Values) { $lb.BackColor = [System.Drawing.SystemColors]::Window; $lb.ForeColor = [System.Drawing.SystemColors]::WindowText }
        
        $script:calendarGrid.BackColor = [System.Drawing.SystemColors]::Control
        
        $script:fileListView.BackColor = [System.Drawing.SystemColors]::Window
        $script:fileListView.ForeColor = [System.Drawing.SystemColors]::WindowText
        $associatedFilesGroup.BackColor = [System.Drawing.SystemColors]::Control
        $script:previewPanel.BackColor = [System.Drawing.SystemColors]::Control
        $script:previewPanel.ForeColor = [System.Drawing.SystemColors]::ControlText
        $associatedFilesGroup.ForeColor = [System.Drawing.SystemColors]::ControlText
        $previewGroup.ForeColor = [System.Drawing.SystemColors]::ControlText
        $dayInfoGroupBox.ForeColor = [System.Drawing.SystemColors]::ControlText
        $script:dayInfoEventsGroup.ForeColor = [System.Drawing.SystemColors]::ControlText
        $script:dayInfoTasksGroup.ForeColor = [System.Drawing.SystemColors]::ControlText

        # Calendar Navigation
        $script:lblMonthYear.ForeColor = [System.Drawing.SystemColors]::ControlText
        foreach ($btn in @($script:btnPrevYear, $script:btnPrevMonth, $script:btnNextMonth, $script:btnNextYear)) {
            $btn.BackColor = [System.Drawing.SystemColors]::Control
            $btn.ForeColor = [System.Drawing.SystemColors]::ControlText
            $btn.FlatStyle = 'Standard'
        }
    }
    
    $tabControl.Invalidate()
    $script:fileListView.Invalidate()

    foreach ($f in [System.Windows.Forms.Application]::OpenForms) {
        Set-Theme -form $f -IsDarkMode $isDarkMode
        # サブフォームにも枠のテーマを適用
        try {
            $hwnd = $f.Handle
            $val = if ($isDarkMode) { 1 } else { 0 }
            $size = [System.Runtime.InteropServices.Marshal]::SizeOf($val)
            [void][TaskManager.WinAPI.Dwmapi]::DwmSetWindowAttribute($hwnd, 20, [ref]$val, $size)
        } catch {}
    }
    
    Update-AllViews
}

function Update-AllViews { 
    Update-KanbanColumnsVisibility
    Update-DataGridView
    Update-AssociatedFilesView
    Update-KanbanView
    if ($script:currentCalendarDate) {
        $script:calendarGrid.Refresh()
        Update-CalendarGrid -dateInMonth $script:currentCalendarDate
    }
    if ($script:selectedCalendarDate) {
        Update-DayInfoPanel -date $script:selectedCalendarDate
        Update-TimelineView -date $script:selectedCalendarDate
    }
}

# --- イベントハンドラ ---

# 共通のアクション：新規タスク追加
$addNewTaskAction = {
    $selectedRow = $script:taskDataGridView.SelectedRows | Select-Object -First 1
    $projectIDForNew = $null
    if ($selectedRow -and $selectedRow.Tag) {
        if ($selectedRow.Tag.PSObject.Properties.Name -contains 'ProjectName') {
            $projectIDForNew = $selectedRow.Tag.ProjectID
        } else {
            $projectIDForNew = $selectedRow.Tag.ProjectID
        }
    }
    $newTask = Show-TaskInputForm -projectIDForNew $projectIDForNew
    if ($newTask) {
        $script:AllTasks += $newTask
        Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
        # フォームで新しいプロジェクトが作成された可能性があるので、プロジェクトリストを再読み込みする
        $script:Projects = Get-Projects
        Update-AllViews
    }
}

# メニュー項目イベントハンドラ
$addNewTaskMenuItem.Add_Click($addNewTaskAction)
$addNewEventMenuItem.Add_Click({
    $selectedDate = if ($script:selectedCalendarDate) { $script:selectedCalendarDate } else { (Get-Date) }
    Start-AddNewEvent -initialDate $selectedDate
})
$exitMenuItem.Add_Click({ $script:forceExit = $true; $mainForm.Close() })
$backupRestoreMenuItem.Add_Click({ 
    if (Invoke-RestoreFromBackup) {
        $script:Settings = Get-Settings
        $script:Projects = Get-Projects
        $script:Categories = Get-Categories
        $script:Templates = Get-Templates
        $script:AllTasks = Read-TasksFromCsv -filePath $script:TasksFile
        $script:AllEvents = Get-Events
        $script:AllTimeLogs = Get-TimeLogs
        Update-AllViews
    }
})
$reportMenuItem.Add_Click({ Show-ReportForm -parentForm $mainForm })
$icsExchangeMenuItem.Add_Click({
    Show-IcsExchangeForm -parentForm $mainForm
})

$editCategoriesMenuItem.Add_Click({ Show-CategoryEditorForm; $currentFilter = $categoryFilterComboBox.SelectedItem; $categoryFilterComboBox.Items.Clear(); $categoryFilterComboBox.Items.Add("(すべて)"); $categoryFilterComboBox.Items.AddRange(@($script:Categories.PSObject.Properties.Name | Sort-Object)); if ($categoryFilterComboBox.Items.Contains($currentFilter)) { $categoryFilterComboBox.SelectedItem = $currentFilter } else { $categoryFilterComboBox.SelectedIndex = 0 }; Update-AllViews })
$editTemplatesMenuItem.Add_Click({ if (Show-TemplateEditorForm -parentForm $mainForm) { $script:Templates = Get-Templates } })
$toggleFilesPanelMenuItem.Add_Click({ $mainContainer.Panel2Collapsed = -not $mainContainer.Panel2Collapsed })
$globalSettingsMenuItem.Add_Click({ Show-SettingsForm -parentForm $mainForm })
# ツールバー項目イベントハンドラ
$btnAdd.Add_Click($addNewTaskAction)
$btnLatestReport.Add_Click({ Open-LatestReport -parentForm $mainForm })
$btnAddFromTemplate.Add_Click({ $newTasks = Show-TemplateForm -parentForm $mainForm; if ($newTasks) { $script:AllTasks += $newTasks; Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks; $script:Projects = Get-Projects; Update-AllViews } })
$btnNotifications.Add_Click({ 
    try {
        if ($this.Tag) { Show-NotificationForm -notifications $this.Tag }
    } catch {
        [System.Windows.Forms.MessageBox]::Show("通知画面の表示中にエラーが発生しました:`n$($_.Exception.Message)", "エラー", "OK", "Error")
    }
})
$categoryFilterComboBox.Items.Add("(すべて)") | Out-Null; $categoryFilterComboBox.Items.AddRange(@($script:Categories.PSObject.Properties.Name | Sort-Object)); $categoryFilterComboBox.SelectedItem = $script:CurrentCategoryFilter
$categoryFilterComboBox.add_SelectedIndexChanged({ $script:CurrentCategoryFilter = $categoryFilterComboBox.SelectedItem; Update-AllViews })

# タブコントロール イベント
$tabControl.Add_SelectedIndexChanged({
    param($source, $e)
    $selectedTab = $source.SelectedTab
    if ($selectedTab.Text -eq "リスト表示") {
        $mainContainer.Panel2Collapsed = $false
    } else {
        $mainContainer.Panel2Collapsed = $true
    }

    # カレンダータブが選択されたときに、ビューを更新する
    if ($selectedTab.Text -eq "カレンダー表示") {
        # Refresh the grid to ensure it's sized correctly before populating.
        $script:calendarGrid.Refresh()
        Update-CalendarGrid -dateInMonth $script:currentCalendarDate
        Update-DayInfoPanel -date $script:selectedCalendarDate
        Update-TimelineView -date $script:selectedCalendarDate
    }
})

# DataGridView イベント
$script:taskDataGridView.Add_SelectionChanged({ Update-AssociatedFilesView })

# 時間記録ボタンのクリックイベント
$script:taskDataGridView.Add_CellContentClick({
    param($source, $e)
    if ($e.RowIndex -lt 0) { return }
    
    $dataGridView = $source
    $columnName = $dataGridView.Columns[$e.ColumnIndex].Name
    
    if ($columnName -eq "RecordAction") {
        $clickedRow = $dataGridView.Rows[$e.RowIndex]
        $task = $clickedRow.Tag
        
        # タスク行でのみ動作
        if ($task -and $task.PSObject.Properties['ID']) {
            $cellValue = $clickedRow.Cells["RecordAction"].Value
            
            if ($cellValue -eq "▶ 開始") {
                # 完了済みのタスクは記録を開始しない
                if ($task.進捗度 -eq '完了済み') {
                    return
                }

                # ステータスが「未実施」または「保留」の場合、「実施中」に変更する
                if ($task.進捗度 -in @('未実施', '保留')) {
                    Set-TaskStatus -task $task -newStatus "実施中"
                    $task.進捗度 = "実施中" # ローカルのタスクオブジェクトも更新
                }

                # 他に記録中のタスクがあれば停止する
                if ($script:currentlyTrackingTaskID) {
                    $logToStop = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $script:currentlyTrackingTaskID -and -not $_.EndTime } | Select-Object -Last 1
                    if ($logToStop) {
                        $logToStop.EndTime = (Get-Date -Format 'o')
                        Save-TimeLogs # 先に前のタスクのログを保存
                    }
                }
                
                # 新しいタスクの記録を開始
                $script:currentlyTrackingTaskID = $task.ID
                $startTime = (Get-Date)
                $script:currentTaskStartTime = $startTime
                $newLog = [PSCustomObject]@{
                    TaskID = $task.ID;
                    StartTime = $startTime.ToString('o');
                    EndTime = $null
                }
                $script:AllTimeLogs = @($script:AllTimeLogs) + $newLog
                # Save-TimeLogs はタイマー停止時や切り替え時に行う

                $script:trackingTimer.Start()
                $script:longTaskCheckSeconds = 0 # カウンターをリセット
                $script:longTaskNotificationShown = $false
                
            } elseif ($cellValue -eq "■ 停止") {
                # 記録を停止
                $logToStop = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $script:currentlyTrackingTaskID -and -not $_.EndTime } | Select-Object -Last 1
                if ($logToStop) {
                    $endTime = (Get-Date)
                    $startTime = if ($script:currentTaskStartTime) { $script:currentTaskStartTime } else { [datetime]$logToStop.StartTime }
                    
                    if (Resolve-TimeLogOverlap -NewStartTime $startTime -NewEndTime $endTime -LogToExclude $null) {
                        $logToStop.EndTime = $endTime.ToString('o')
                        Save-TimeLogs
                        
                        $script:trackingTimer.Stop()
                        $script:currentlyTrackingTaskID = $null
                        $script:currentTaskStartTime = $null
                        $script:longTaskCheckSeconds = 0
                        $script:longTaskNotificationShown = $false
                    }
                } else {
                    # ログが見つからない場合のフォールバック
                    $script:trackingTimer.Stop()
                    $script:currentlyTrackingTaskID = $null
                }
            }
            
            # グリッドビューを更新して表示を切り替える
            Update-DataGridView
        }
    }
})

# タイマーのTickイベント
$tickEventHandler = {
    if ($script:currentlyTrackingTaskID) {
        $task = $script:AllTasks | Where-Object { $_.ID -eq $script:currentlyTrackingTaskID } | Select-Object -First 1
        if ($task) {
            # 1秒ごとにUIを更新して、実行中のタイマーの表示を更新する
            # Update-DataGridViewは負荷が高いため、特定のセルだけを更新する
            try {
                $taskRow = $script:taskDataGridView.Rows | Where-Object { $_.Tag -is [PSCustomObject] -and $_.Tag.ID -eq $task.ID } | Select-Object -First 1
                if ($taskRow) {
                    # --- タスクの合計時間を更新 ---
                    $taskLogs = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $task.ID -and $_.EndTime }
                    $totalTaskSeconds = 0
                    if($taskLogs){ $totalTaskSeconds = ($taskLogs | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum }
                    $trackingLog = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $task.ID -and -not $_.EndTime } | Select-Object -Last 1
                    if ($trackingLog) {
                        $totalTaskSeconds += (New-TimeSpan -Start ([datetime]$trackingLog.StartTime) -End (Get-Date)).TotalSeconds
                    }
                    $taskRow.Cells["TrackedTime"].Value = Format-TimeSpanFromSeconds -totalSeconds $totalTaskSeconds

                    # --- ★ここから修正: プロジェクトの合計時間もリアルタイムで更新 ---
                    $projectRow = $script:taskDataGridView.Rows | Where-Object { $_.Tag -is [PSCustomObject] -and $_.Tag.PSObject.Properties.Name -contains 'ProjectName' -and $_.Tag.ProjectID -eq $task.ProjectID } | Select-Object -First 1
                    if ($projectRow) {
                        $projectTasks = $script:AllTasks | Where-Object { $_.ProjectID -eq $task.ProjectID }
                        $taskIdsInProject = $projectTasks | ForEach-Object { $_.ID }
                        
                        $totalProjectSeconds = 0
                        $projectLogs = $script:AllTimeLogs | Where-Object { $taskIdsInProject -contains $_.TaskID -and $_.EndTime }
                        if ($projectLogs) {
                            $totalProjectSeconds = ($projectLogs | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum
                        }

                        # 現在実行中のタスクの時間を加算 (上記で計算済みの$trackingLogを再利用)
                        if ($trackingLog) {
                            $totalProjectSeconds += (New-TimeSpan -Start ([datetime]$trackingLog.StartTime) -End (Get-Date)).TotalSeconds
                        }
                        
                        $projectRow.Cells["TrackedTime"].Value = Format-TimeSpanFromSeconds -totalSeconds $totalProjectSeconds
                    }
                    # --- ★ここまで修正 ---
                }
            } catch {}

            $script:longTaskCheckSeconds += 1

            # 長時間記録のチェック
            $notificationMinutes = $script:Settings.LongTaskNotificationMinutes
            if ($notificationMinutes -gt 0) {
                $notificationSeconds = $notificationMinutes * 60
                if ($script:longTaskCheckSeconds -ge $notificationSeconds -and -not $script:longTaskNotificationShown) {
                    # 通知前に最新のタスク情報を取得
                    $currentTaskForNotification = $script:AllTasks | Where-Object { $_.ID -eq $script:currentlyTrackingTaskID } | Select-Object -First 1
                    if ($currentTaskForNotification) {
                        $script:longTaskNotificationShown = $true
                        [System.Windows.Forms.MessageBox]::Show("タスク「$($currentTaskForNotification.タスク)」を $($notificationMinutes)分継続中です。まだ作業中ですか？", "長時間作業の確認", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Question) | Out-Null
                    }
                    $script:longTaskCheckSeconds = 0 # カウンターをリセット
                }
            }
        }
    }
    # イベント通知チェック
    Invoke-EventNotificationCheck
}
# 既存のハンドラを念のため削除してから追加することで、重複登録を確実に防ぐ
$script:trackingTimer.remove_Tick($tickEventHandler)
$script:trackingTimer.add_Tick($tickEventHandler)

# 非アクティブ検知タイマーのTickイベント
$idleCheckTimerTick = {
    if ($null -eq $script:currentlyTrackingTaskID) { return }
    try {
        $lastInputInfo = New-Object -TypeName TaskManager.WinAPI.User32+LASTINPUTINFO
        $lastInputInfo.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($lastInputInfo)
        if ([TaskManager.WinAPI.User32]::GetLastInputInfo([ref]$lastInputInfo)) {
            $lastInputTicks = $lastInputInfo.dwTime
            $idleTimeMs = [Environment]::TickCount - $lastInputTicks
            $idleTimeoutMs = $script:Settings.IdleTimeoutMinutes * 60 * 1000

            if ($idleTimeMs -gt $idleTimeoutMs) {
                if ($script:trackingTimer.Enabled -and -not $script:idleMessageShown) {
                    $script:trackingTimer.Stop()
                    $script:idleMessageShown = $true
                    [System.Windows.Forms.MessageBox]::Show("$($script:Settings.IdleTimeoutMinutes)分間操作がありませんでした。記録を一時停止しました。", "非アクティブ検知", "OK", "Information")
                }
            } else {
                if (-not $script:trackingTimer.Enabled) {
                    $script:trackingTimer.Start()
                    $script:idleMessageShown = $false
                }
            }
        }
    } catch {
        Write-Warning "非アクティブ検知中にエラーが発生しました: $($_.Exception.Message)"
    }
}
$script:idleCheckTimer.Add_Tick($idleCheckTimerTick)

$script:taskDataGridView.Add_CellClick({
    param($source, $e)
    # ヘッダー行は無視
    if ($e.RowIndex -lt 0) { return }

    # 行に紐づくアイテムを取得
    $item = $source.Rows[$e.RowIndex].Tag
    if ($null -eq $item) { return }

    # 最初の列がクリックされた場合のみ処理
    if ($e.ColumnIndex -eq 0) {
        # アイテムがプロジェクトの場合
        if ($item.PSObject.Properties.Name -contains 'ProjectName') {
            $projectId = $item.ProjectID
            if ($script:ProjectExpansionStates.ContainsKey($projectId)) {
                $script:ProjectExpansionStates[$projectId] = -not $script:ProjectExpansionStates[$projectId]
            } else {
                $script:ProjectExpansionStates[$projectId] = $true
            }
            Update-DataGridView # ビューを更新
        }
        # アイテムがカテゴリの場合
        elseif ($item.PSObject.Properties.Name -contains 'CategoryName') {
            $categoryName = $item.CategoryName
            if ($script:CategoryExpansionStates.ContainsKey($categoryName)) {
                $script:CategoryExpansionStates[$categoryName] = -not $script:CategoryExpansionStates[$categoryName]
            } else {
                $script:CategoryExpansionStates[$categoryName] = $true
            }
            Update-DataGridView # ビューを更新
        }
    }
})
$script:taskDataGridView.Add_CellPainting({
    param($source, $e)
    
    # ヘッダー行や範囲外の場合は何もしない
    if ($e.RowIndex -lt 0 -or $e.ColumnIndex -lt 0) { return }

    # 「進捗」列の場合
    if ($e.ColumnIndex -eq $script:taskDataGridView.Columns["Progress"].Index) {
        $item = $source.Rows[$e.RowIndex].Tag
        if ($item -and $item.PSObject.Properties.Name -contains 'ProjectName') {
            $e.PaintBackground($e.ClipBounds, $true)
            $percentage = 0
            if ($e.Value -is [int] -or $e.Value -is [double]) { $percentage = [int]$e.Value }
            if ($percentage -gt 0) {
                $barWidth = [int](($e.CellBounds.Width - 4) * ($percentage / 100.0))
                $barBounds = [System.Drawing.Rectangle]::new($e.CellBounds.X + 2, $e.CellBounds.Y + 2, $barWidth, $e.CellBounds.Height - 5)
                $barColor = if ($percentage -eq 100) { [System.Drawing.Color]::MediumSeaGreen } else { [System.Drawing.Color]::SteelBlue }
                $brush = New-Object System.Drawing.SolidBrush($barColor)
                $e.Graphics.FillRectangle($brush, $barBounds)
                $brush.Dispose()
            }
            $text = "$percentage %"
            $font = $e.CellStyle.Font
            $textSize = $e.Graphics.MeasureString($text, $font)
            $textX = $e.CellBounds.Left + ($e.CellBounds.Width - $textSize.Width) / 2
            $textY = $e.CellBounds.Top + ($e.CellBounds.Height - $textSize.Height) / 2
            $e.Graphics.DrawString($text, $font, [System.Drawing.Brushes]::White, $textX + 1, $textY + 1)
            $e.Graphics.DrawString($text, $font, [System.Drawing.Brushes]::Black, $textX, $textY)
            $e.Handled = $true
        }
    } 
    # 「記録操作」列の場合
    elseif ($e.ColumnIndex -eq $script:taskDataGridView.Columns["RecordAction"].Index) {
        $e.PaintBackground($e.ClipBounds, $true)
        $cellValue = $e.Value
        if ([string]::IsNullOrEmpty($cellValue)) { 
            $e.Handled = $true
            return 
        }
        [System.Windows.Forms.ControlPaint]::DrawBorder3D($e.Graphics, $e.CellBounds, [System.Windows.Forms.Border3DStyle]::Raised)
        $textColor = if (($e.State -band [System.Windows.Forms.DataGridViewElementStates]::Selected) -eq [System.Windows.Forms.DataGridViewElementStates]::Selected) {
            $e.CellStyle.SelectionForeColor
        } else {
            $e.CellStyle.ForeColor
        }
        $textFormat = [System.Windows.Forms.TextFormatFlags]::HorizontalCenter -bor [System.Windows.Forms.TextFormatFlags]::VerticalCenter
        [System.Windows.Forms.TextRenderer]::DrawText($e.Graphics, $cellValue, $e.CellStyle.Font, $e.CellBounds, $textColor, $textFormat)
        $e.Handled = $true
    }
    # 「タスク/プロジェクト」列の場合
    elseif ($e.ColumnIndex -eq $script:taskDataGridView.Columns["Name"].Index) {
        $e.PaintBackground($e.ClipBounds, $true)
        $item = $source.Rows[$e.RowIndex].Tag
        if ($null -eq $item) { $e.Handled = $true; return }

        $text = if ($e.FormattedValue) { $e.FormattedValue.ToString() } else { "" }
        $hasFiles = ($item.PSObject.Properties["WorkFiles"] -and $item.WorkFiles.Count -gt 0)
        $isProject = $item.PSObject.Properties.Name -contains 'ProjectName'
        
        $textColor = if (($e.State -band [System.Windows.Forms.DataGridViewElementStates]::Selected) -eq [System.Windows.Forms.DataGridViewElementStates]::Selected) { $e.CellStyle.SelectionForeColor } else { if ($script:isDarkMode) { [System.Drawing.Color]::White } else { $e.CellStyle.ForeColor } }
        $cellFont = $e.CellStyle.Font
        $emojiFont = New-Object System.Drawing.Font("Segoe UI Emoji", $cellFont.Size)

        # Define constant widths for icons and margins
        $clipIconWidth = 20
        $expanderWidth = 20
        $textMargin = 4

        # The starting bounds for drawing is the entire cell
        $currentBounds = $e.CellBounds
        $namePart = $text.Trim()

        # 1. Draw Clip Icon (if it exists) in the reserved space
        if ($hasFiles) {
            $iconText = "🔗"
            $iconY = [int]($currentBounds.Y + ($currentBounds.Height - $emojiFont.Height) / 2)
            $iconRect = [System.Drawing.Rectangle]::new($currentBounds.X, $iconY, $clipIconWidth, $currentBounds.Height)
            [System.Windows.Forms.TextRenderer]::DrawText($e.Graphics, $iconText, $emojiFont, $iconRect, $textColor)
        }
        # ALWAYS shrink the bounds by the clip icon width to maintain alignment for the expander
        $currentBounds.X += $clipIconWidth
        $currentBounds.Width -= $clipIconWidth

        # 2. Draw Expander Icon (if it's a project) or indent, and shrink remaining bounds
        if ($isProject) {
            $match = $text -match "^(\s*\[[+-]\])"
            if ($match) {
                $expanderSymbol = $matches[1].Trim()
                $namePart = $text.Substring($matches[0].Length).Trim()

                $expanderRect = [System.Drawing.Rectangle]::new($currentBounds.X, $currentBounds.Y, $expanderWidth, $currentBounds.Height)
                [System.Windows.Forms.TextRenderer]::DrawText($e.Graphics, $expanderSymbol, $cellFont, $expanderRect, $textColor, ([System.Windows.Forms.TextFormatFlags]::HorizontalCenter -bor [System.Windows.Forms.TextFormatFlags]::VerticalCenter))
            }
            $currentBounds.X += $expanderWidth
            $currentBounds.Width -= $expanderWidth
        } else {
            # For tasks, just indent by the same amount
            $currentBounds.X += $expanderWidth
            $currentBounds.Width -= $expanderWidth
        }

        # 3. Draw the final text with Ellipsis
        $currentBounds.X += $textMargin
        $currentBounds.Width -= $textMargin

        $textFormat = [System.Windows.Forms.TextFormatFlags]::VerticalCenter -bor [System.Windows.Forms.TextFormatFlags]::EndEllipsis -bor [System.Windows.Forms.TextFormatFlags]::SingleLine

        if ($currentBounds.Width -gt 0) {
            [System.Windows.Forms.TextRenderer]::DrawText($e.Graphics, $namePart, $cellFont, $currentBounds, $textColor, $textFormat)
        }

        $emojiFont.Dispose()
        $e.Handled = $true
    }
})
$script:taskDataGridView.Add_CellDoubleClick({
    param($source, $e)
    if ($e.RowIndex -lt 0) { return }
    $item = $source.Rows[$e.RowIndex].Tag
    if ($null -eq $item) { return }

    if ($item.PSObject.Properties.Name -contains 'ProjectName') {
        Show-EditProjectPropertiesForm -projectObject $item -parentForm $mainForm
        Update-AllViews
    } else {
        $action = if ($script:Settings.DoubleClickAction) { $script:Settings.DoubleClickAction } else { "Edit" }
        if ($action -eq "ToggleStatus") {
            $newStatus = if ($item.進捗度 -eq '完了済み') { '未実施' } else { '完了済み' }
            Set-TaskStatus -task $item -newStatus $newStatus
            Update-AllViews
        } else {
            if (Start-EditTask -task $item) {
                Update-AllViews
            }
        }
    }
})
$script:taskDataGridView.Add_KeyDown({ param($source, $e); if ($e.KeyCode -ne [System.Windows.Forms.Keys]::Delete -or $source.SelectedRows.Count -eq 0) { return }; $item = $source.SelectedRows[0].Tag; if ($null -eq $item) { return }; if ($item.PSObject.Properties.Name -contains 'ProjectName') { $confirmResult = [System.Windows.Forms.MessageBox]::Show("プロジェクト '$($item.ProjectName)' を削除します。`n関連するすべてのタスクも削除されます。`n`nよろしいですか？", "プロジェクトの削除の確認", "YesNo", "Warning"); if ($confirmResult -eq 'Yes') { $script:Projects = $script:Projects | Where-Object { $_.ProjectID -ne $item.ProjectID }; $script:AllTasks = $script:AllTasks | Where-Object { $_.ProjectID -ne $item.ProjectID }; Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects; Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks; Update-AllViews } } else { Start-DeleteTask -task $item; Update-AllViews } })

# --- DataGridView ドラッグ＆ドロップ イベントハンドラ ---
$script:dragStartPoint = New-Object System.Drawing.Point

# 2-A. ドラッグの開始処理 (MouseDown) と右クリック選択 - リスト表示から
$script:taskDataGridView.Add_MouseDown({
    param($source, $e)
    # 左クリックの場合: ドラッグ開始処理
    if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
        $hitTest = $script:taskDataGridView.HitTest($e.X, $e.Y)
        # セルがクリックされた場合、ドラッグ開始点を記録
        if ($hitTest.Type -eq [System.Windows.Forms.DataGridViewHitTestType]::Cell -and $hitTest.RowIndex -ge 0) {
            $script:dragStartPoint = $e.Location
            # クリックされた行を選択状態にする
            $script:taskDataGridView.Rows[$hitTest.RowIndex].Selected = $true
        }
    }
})

# 右クリック時の行選択 (CellMouseDown) - 確実にコンテキストメニューを表示するために追加
$script:taskDataGridView.Add_CellMouseDown({
    param($source, $e)
    if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Right) {
        if ($e.RowIndex -ge 0) {
            if (-not $source.Rows[$e.RowIndex].Selected) {
                $source.ClearSelection()
                $source.Rows[$e.RowIndex].Selected = $true
                try { $source.CurrentCell = $source.Rows[$e.RowIndex].Cells[$e.ColumnIndex] } catch {}
            }
        }
    }
})

# 2-A. ドラッグの開始処理 (MouseMove) - リスト表示から
$script:taskDataGridView.Add_MouseMove({
    param($source, $e)
    # 左クリックしながらマウスが移動した場合
    if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
        $dragThreshold = [System.Windows.Forms.SystemInformation]::DragSize
        # マウスが一定距離以上移動したか確認
        if (([Math]::Abs($e.X - $script:dragStartPoint.X) -gt $dragThreshold.Width) -or ([Math]::Abs($e.Y - $script:dragStartPoint.Y) -gt $dragThreshold.Height)) {
            if ($script:taskDataGridView.SelectedRows.Count -gt 0) {
                $row = $script:taskDataGridView.SelectedRows[0]
                $item = $row.Tag
                # ドラッグ対象がタスクであり、プロジェクトではないことを確認
                if ($item -and $item.PSObject.Properties['ID'] -and -not ($item.PSObject.Properties.Name -contains 'ProjectName')) {
                    # DoDragDrop操作を開始
                    $script:taskDataGridView.DoDragDrop($item, [System.Windows.Forms.DragDropEffects]::Move)
                }
            }
        }
    }
})

# 関連ファイルリスト イベント
$script:fileListView.Add_DragEnter({ param($source, $e) if ($e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)) { $e.Effect = [System.Windows.Forms.DragDropEffects]::Copy } })
$script:fileListView.Add_DragDrop({
    param($source, $e)
    if ($script:taskDataGridView.SelectedRows.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show("ファイルを追加する先のタスクまたはプロジェクトを選択してください。", "情報", "OK", "Information"); return }
    $selectedObject = $script:taskDataGridView.SelectedRows[0].Tag
    
    # マスターデータを検索して取得
    $selectedTag = $script:taskDataGridView.SelectedRows[0].Tag
    $selectedObject = $null
    if ($selectedTag.PSObject.Properties.Name -contains 'ProjectName') {
        $selectedObject = $script:Projects | Where-Object { $_.ProjectID -eq $selectedTag.ProjectID } | Select-Object -First 1
    } else {
        $selectedObject = $script:AllTasks | Where-Object { $_.ID -eq $selectedTag.ID } | Select-Object -First 1
    }
    if ($null -eq $selectedObject) { return }
    $droppedFiles = $e.Data.GetData([System.Windows.Forms.DataFormats]::FileDrop)

    # Robustly handle the WorkFiles array
    $currentFiles = @($selectedObject.WorkFiles | Where-Object { $_ -is [psobject] -and $_.PSObject.Properties.Match('Content').Count -gt 0 })
    $existingContents = $currentFiles.Content
    $filesToAdd = @()

    foreach ($file in $droppedFiles) {
        if ($existingContents -notcontains $file) {
            $fileType = 'File'
            $ext = [System.IO.Path]::GetExtension($file).ToLower()
            if (@('.png', '.jpg', '.jpeg', '.bmp', '.gif') -contains $ext) { $fileType = 'Image' }
            $filesToAdd += [PSCustomObject]@{ DisplayName = [System.IO.Path]::GetFileName($file); Type = $fileType; Content = $file; DateAdded = (Get-Date).ToString("yyyy-MM-dd HH:mm") }
        }
    }

    if ($filesToAdd.Count -gt 0) {
        $selectedObject.WorkFiles = $currentFiles + $filesToAdd
        if ($selectedObject.PSObject.Properties.Name -contains 'ProjectName') { Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects }
        else { Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks }
        Update-AllViews
    }
})
$script:fileListView.Add_DoubleClick({ 
    param($source, $e); 
    if ($source.SelectedItems.Count -eq 0) { return }; 
    $fileObject = $source.SelectedItems[0].Tag; 
    if (-not $fileObject) { return } # Safety check for corrupted data
    try { 
        switch ($fileObject.Type) { 
            'File' { if(Test-Path -LiteralPath $fileObject.Content){ Start-Process -FilePath $fileObject.Content } else { [System.Windows.Forms.MessageBox]::Show("ファイルが見つかりません: $($fileObject.Content)", "エラー", "OK", "Error") } }; 
            'Image' { if(Test-Path -LiteralPath $fileObject.Content){ Start-Process -FilePath $fileObject.Content } else { [System.Windows.Forms.MessageBox]::Show("ファイルが見つかりません: $($fileObject.Content)", "エラー", "OK", "Error") } };
            'URL'  { Start-Process -FilePath $fileObject.Content }; 
            'Memo' { 
                $newMemo = Show-MemoInputForm -existingText $fileObject.Content; 
                if ($null -ne $newMemo) { 
                    $fileObject.Content = $newMemo; 
                    $fileObject.DisplayName = "[メモ] " + ($newMemo -split "`r`n|`n|`r")[0] + "..."
                    # マスターデータを特定して保存
                    $objToSave = $script:taskDataGridView.SelectedRows[0].Tag
                    if ($objToSave.PSObject.Properties.Name -contains 'ProjectName') { 
                        Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
                    } else {
                        Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
                    }
                    Update-AllViews
                } else {
                    # Cancel was pressed. Do nothing to prevent any side effects.
                }
            } 
        } 
    } catch { 
        [System.Windows.Forms.MessageBox]::Show("項目を開けませんでした: $($_.Exception.Message)", "エラー", "OK", "Error") 
    } 
})
$script:fileListView.Add_SelectedIndexChanged({
    param($source, $e)
    $script:previewPanel.Controls.Clear()
    if ($source.SelectedItems.Count -eq 0) { return }
    $fileObject = $source.SelectedItems[0].Tag
    if ($null -eq $fileObject) { return }
    try {
        if ($fileObject.Type -in @('File', 'Image')) {
            $filePath = $fileObject.Content
            if (-not (Test-Path -LiteralPath $filePath)) {
                $lbl = New-Object System.Windows.Forms.Label; $lbl.Text = "ファイルが見つかりません: `n$filePath"; $lbl.Dock = "Fill"; $lbl.TextAlign = "MiddleCenter"
                $script:previewPanel.Controls.Add($lbl)
                return
            }
            $extension = [System.IO.Path]::GetExtension($filePath).ToLower()
            switch ($extension) {
                { @('.txt', '.log', '.csv', '.ps1', '.json') -contains $_ } {
                    $textBox = New-Object System.Windows.Forms.TextBox
                    $textBox.Dock = "Fill"; $textBox.Multiline = $true; $textBox.ReadOnly = $true; $textBox.ScrollBars = "Both"; $textBox.Font = $script:previewFont
                    $textBox.Text = Get-Content -LiteralPath $filePath -Raw -Encoding UTF8
                    
                    $colors = Get-ThemeColors -IsDarkMode $script:isDarkMode
                    $textBox.BackColor = $colors.ControlBack
                    $textBox.ForeColor = $colors.ControlFore
                    
                    $script:previewPanel.Controls.Add($textBox)
                }
                { @('.png', '.jpg', '.jpeg', '.bmp', '.gif') -contains $_ } {
                    $pictureBox = New-Object System.Windows.Forms.PictureBox
                    $pictureBox.Dock = "Fill"; $pictureBox.SizeMode = "Zoom"
                    $fileStream = New-Object System.IO.FileStream($filePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read)
                    $pictureBox.Image = [System.Drawing.Image]::FromStream($fileStream)
                    $fileStream.Close()
                    $fileStream.Dispose()
                    $script:previewPanel.Controls.Add($pictureBox)
                }
                default {
                    $lbl = New-Object System.Windows.Forms.Label; $lbl.Text = "プレビュー非対応のファイル形式です `n($extension)"; $lbl.Dock = "Fill"; $lbl.TextAlign = "MiddleCenter"
                    $script:previewPanel.Controls.Add($lbl)
                }
            }
        } elseif ($fileObject.Type -eq 'URL') {
            $url = $fileObject.Content
            
            # Display temporary message
            $tempLabel = New-Object System.Windows.Forms.Label
            $tempLabel.Text = "ページのタイトルを取得中..."
            $tempLabel.Dock = "Fill"
            $tempLabel.TextAlign = "MiddleCenter"
            $script:previewPanel.Controls.Clear()
            $script:previewPanel.Controls.Add($tempLabel)

            # Use .NET WebClient for asynchronous download
            $webClient = New-Object System.Net.WebClient
            # Set User-Agent and Encoding
            $webClient.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.0.0 Safari/537.36")
            $webClient.Encoding = [System.Text.Encoding]::UTF8
            
            # Set up the event handler for when the download completes
            $webClient.add_DownloadStringCompleted({
                param($s, $ev)
                
                $finalLabel = New-Object System.Windows.Forms.Label
                $finalLabel.Dock = "Fill"
                $finalLabel.TextAlign = "MiddleCenter"
                $finalLabel.Padding = New-Object System.Windows.Forms.Padding(10)

                if ($ev.Cancelled) {
                    $finalLabel.Text = "ページの取得がキャンセルされました。"
                } elseif ($ev.Error -ne $null) {
                    $finalLabel.Text = "ページの取得に失敗しました: `n$($ev.Error.Message)"
                } else {
                    $htmlContent = $ev.Result
                    if ($htmlContent -match '(?s)<title.*?>(.*?)</title>') {
                        $title = [System.Net.WebUtility]::HtmlDecode($matches[1].Trim())
                        $finalLabel.Text = "URL: `n$url`n`nタイトル: `n$title"
                    } else {
                        $finalLabel.Text = "URL: `n$url`n`nページのタイトルが見つかりませんでした。"
                    }
                }
                
                # This event handler runs on the UI thread, so we can safely update the controls
                $script:previewPanel.Controls.Clear()
                $script:previewPanel.Controls.Add($finalLabel)
                
                # Dispose of the WebClient
                $s.Dispose()
            })

            # Start the asynchronous download
            try {
                $webClient.DownloadStringAsync([System.Uri]$url)
            } catch {
                $script:previewPanel.Controls.Clear()
                $errorLabel = New-Object System.Windows.Forms.Label
                $errorLabel.Dock = "Fill"
                $errorLabel.TextAlign = "MiddleCenter"
                $errorLabel.Text = "URLの形式が正しくない可能性があります: `n$($_.Exception.Message)"
                $script:previewPanel.Controls.Add($errorLabel)
                $webClient.Dispose()
            }
        } elseif ($fileObject.Type -eq 'Memo') {
            $textBox = New-Object System.Windows.Forms.TextBox
            $textBox.Dock = "Fill"; $textBox.Multiline = $true; $textBox.ReadOnly = $true; $textBox.ScrollBars = "Vertical"
            $textBox.Text = $fileObject.Content
            
            $colors = Get-ThemeColors -IsDarkMode $script:isDarkMode
            $textBox.BackColor = $colors.ControlBack
            $textBox.ForeColor = $colors.ControlFore
            
            $script:previewPanel.Controls.Add($textBox)
        }
    } catch {
        $lbl = New-Object System.Windows.Forms.Label; $lbl.Text = "プレビュー中にエラーが発生しました: `n$($_.Exception.Message)"; $lbl.Dock = "Fill"; $lbl.TextAlign = "MiddleCenter"; $lbl.ForeColor = "Red"
        $script:previewPanel.Controls.Add($lbl)
    }
})

# 関連ファイル右クリック イベント
$script:fileListView.Add_MouseClick({ param($source, $e) if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Right) { if ($item = $source.GetItemAt($e.X, $e.Y)) { $item.Selected = $true } } })

$fileListContextMenu.Add_Opening({
    param($s, $e)
    if ($script:fileListView.SelectedItems.Count -eq 0) {
        $renameFileMenuItem.Visible = $false
        $openLocationMenuItem.Visible = $false
        $copyPathMenuItem.Visible = $false
        $deleteFileMenuItem.Visible = $false
    } else {
        $fileObject = $script:fileListView.SelectedItems[0].Tag
        $isLocalFile = $fileObject -and ($fileObject.Type -in @('File', 'Image'))
        
        $renameFileMenuItem.Visible = $isLocalFile
        $openLocationMenuItem.Visible = $isLocalFile
        $copyPathMenuItem.Visible = $true
        $deleteFileMenuItem.Visible = $true
    }
})

$renameFileMenuItem.Add_Click({
    if ($script:fileListView.SelectedItems.Count -eq 0) { return }
    $fileObject = $script:fileListView.SelectedItems[0].Tag
    
    $selectedObject = $script:taskDataGridView.SelectedRows[0].Tag
    if (-not $fileObject -or -not $selectedObject) { return }

    $oldPath = $fileObject.Content
    $oldName = [System.IO.Path]::GetFileName($oldPath)
    $directory = [System.IO.Path]::GetDirectoryName($oldPath)

    $newName = [Microsoft.VisualBasic.Interaction]::InputBox("新しいファイル名を入力してください:", "名前の変更", $oldName)

    if ([string]::IsNullOrWhiteSpace($newName) -or $newName -eq $oldName) { return }
    if ($newName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ne -1) {
        [System.Windows.Forms.MessageBox]::Show("ファイル名に使用できない文字が含まれています。", "エラー", "OK", "Error"); return
    }

    $newPath = Join-Path -Path $directory -ChildPath $newName
    if (Test-Path -LiteralPath $newPath) {
        [System.Windows.Forms.MessageBox]::Show("同じ名前のファイルが既に存在します。", "エラー", "OK", "Error"); return
    }

    try {
        Rename-Item -LiteralPath $oldPath -NewName $newName -ErrorAction Stop
        $fileObject.Content = $newPath; $fileObject.DisplayName = $newName
        if ($selectedObject.PSObject.Properties.Name -contains 'ProjectName') { Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects }
        else { Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks }
        Update-AllViews
    } catch {
        [System.Windows.Forms.MessageBox]::Show("ファイル名の変更中にエラーが発生しました:`n$($_.Exception.Message)", "エラー", "OK", "Error")
    }
})

$openLocationMenuItem.Add_Click({ if ($script:fileListView.SelectedItems.Count -eq 0) { return }; $fileObject = $script:fileListView.SelectedItems[0].Tag; if ($fileObject.Type -in @('File', 'Image') -and (Test-Path -LiteralPath $fileObject.Content)) { Invoke-Item -Path (Split-Path -Path $fileObject.Content -Parent) } })
$copyPathMenuItem.Add_Click({ if ($script:fileListView.SelectedItems.Count -eq 0) { return }; [System.Windows.Forms.Clipboard]::SetText($script:fileListView.SelectedItems[0].Tag.Content) })
$addUrlMenuItem.Add_Click({
    if ($script:taskDataGridView.SelectedRows.Count -eq 0) { return }
    $url = [Microsoft.VisualBasic.Interaction]::InputBox("追加するURLを入力してください:", "URLの追加")
    if (-not [string]::IsNullOrWhiteSpace($url)) {
        $selectedObject = $script:taskDataGridView.SelectedRows[0].Tag
        if ($null -eq $selectedObject) { return }

        $currentFiles = [System.Collections.ArrayList]::new()
        if ($selectedObject.WorkFiles) {
            $currentFiles.AddRange(@($selectedObject.WorkFiles))
        }
        $newUrlObject = [PSCustomObject]@{ DisplayName = $url; Type = 'URL'; Content = $url; DateAdded = (Get-Date).ToString("yyyy-MM-dd HH:mm") }
        $currentFiles.Add($newUrlObject) | Out-Null
        $selectedObject.WorkFiles = $currentFiles.ToArray()

        if ($selectedObject.PSObject.Properties.Name -contains 'ProjectName') { Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects }
        else { Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks }
        Update-AllViews
    }
})
$addMemoMenuItem.Add_Click({
    if ($script:taskDataGridView.SelectedRows.Count -eq 0) { return }
    $memo = Show-MemoInputForm
    if ($null -ne $memo) {
        $selectedObject = $script:taskDataGridView.SelectedRows[0].Tag
        if ($null -eq $selectedObject) { return }

        $currentFiles = [System.Collections.ArrayList]::new()
        if ($selectedObject.WorkFiles) {
            $currentFiles.AddRange(@($selectedObject.WorkFiles))
        }
        $newMemoObject = [PSCustomObject]@{ DisplayName = "[メモ] " + ($memo -split "`r`n|`n|`r")[0] + "..."; Type = 'Memo'; Content = $memo; DateAdded = (Get-Date).ToString("yyyy-MM-dd HH:mm") }
        $currentFiles.Add($newMemoObject) | Out-Null
        $selectedObject.WorkFiles = $currentFiles.ToArray()

        if ($selectedObject.PSObject.Properties.Name -contains 'ProjectName') {
            for ($i = 0; $i -lt $script:Projects.Count; $i++) {
                if ($script:Projects[$i].ProjectID -eq $selectedObject.ProjectID) {
                    $script:Projects[$i].WorkFiles = $selectedObject.WorkFiles; break
                }
            }
            Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
        } else {
            for ($i = 0; $i -lt $script:AllTasks.Count; $i++) {
                if ($script:AllTasks[$i].ID -eq $selectedObject.ID) {
                    $script:AllTasks[$i].WorkFiles = $selectedObject.WorkFiles; break
                }
            }
            Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
        }
        Update-AllViews
    }
})
$deleteFileMenuItem.Add_Click({
    if ($script:fileListView.SelectedItems.Count -eq 0 -or $script:taskDataGridView.SelectedRows.Count -eq 0) { return }
    $selectedObject = $script:taskDataGridView.SelectedRows[0].Tag
    if ($null -eq $selectedObject) { return }

    $filesToRemove = $script:fileListView.SelectedItems | ForEach-Object { $_.Tag }
    
    $currentFiles = [System.Collections.ArrayList]::new()
    if ($selectedObject.WorkFiles) { $currentFiles.AddRange(@($selectedObject.WorkFiles)) }
    
    $filesToRemove | ForEach-Object { $currentFiles.Remove($_) }
    $selectedObject.WorkFiles = $currentFiles.ToArray()

    if ($selectedObject.PSObject.Properties.Name -contains 'ProjectName') { Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects }
    else { Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks }
    Update-AllViews
})

# --- アプリケーションの初期化と表示 ---
Initialize-ApplicationAssets
Initialize-DataGridViewColumns -dataGridView $script:taskDataGridView
Initialize-CalendarView
# Initialize-TaskDrag # 内部で実装したため不要
Invoke-NotificationCheck
Update-Theme -isDarkMode $script:isDarkMode
$script:idleCheckTimer.Start()
Update-AllViews

# --- 起動時の設定反映と制御 ---

# 1. パスコードチェック
if (-not [string]::IsNullOrEmpty($script:Settings.Passcode)) {
    if (-not (Show-LoginDialog)) {
        exit # 認証失敗またはキャンセルで終了
    }
}

# 2. 初期表示タブの切り替え
switch ($script:Settings.StartupView) {
    "Kanban"   { $tabControl.SelectedTab = $kanbanTabPage }
    "Calendar" { $tabControl.SelectedTab = $calendarTabPage }
    "List"     { $tabControl.SelectedTab = $listTabPage }
    default    { $tabControl.SelectedTab = $listTabPage }
}

# 3. ウィンドウ設定の適用
if ($script:Settings.WindowOpacity) {
    $val = [double]$script:Settings.WindowOpacity
    if ($val -ge 0.2 -and $val -le 1.0) { $mainForm.Opacity = $val }
}
if ($script:Settings.AlwaysOnTop) {
    $mainForm.TopMost = $true
}

# 4. スタートアップ登録の同期
Update-StartupShortcut

# 5. タスクトレイアイコンの準備
$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
$notifyIcon.Icon = [System.Drawing.SystemIcons]::Application
$notifyIcon.Text = "Task Manager"
$notifyIcon.Visible = $true

$notifyIcon.Add_DoubleClick({
    $mainForm.Show()
    $mainForm.WindowState = [System.Windows.Forms.FormWindowState]::Normal
    $mainForm.Activate()
})

# トレイアイコンのコンテキストメニュー
$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
$trayExitItem = $trayMenu.Items.Add("終了")
$trayExitItem.Add_Click({
    $script:forceExit = $true
    $mainForm.Close()
})
$notifyIcon.ContextMenuStrip = $trayMenu

# フォームを閉じる際の処理
$mainForm.Add_FormClosing({
    param($source, $e)
    
    # 最小化設定が有効で、ユーザー操作による閉じる場合、かつ強制終了フラグがない場合
    if ($script:Settings.MinimizeToTray -and $e.CloseReason -eq [System.Windows.Forms.CloseReason]::UserClosing -and -not $script:forceExit) {
        $e.Cancel = $true
        $mainForm.Hide()
        $notifyIcon.ShowBalloonTip(1000, "Task Manager", "最小化しました。トレイアイコンから復帰できます。", [System.Windows.Forms.ToolTipIcon]::Info)
    } else {
        # 実際に終了する場合の保存処理
        Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
        Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
        Save-DataFile -filePath $script:SettingsFile -dataObject $script:Settings
        Save-DataFile -filePath $script:EventsFile -dataObject $script:AllEvents
        Save-DataFile -filePath $script:TimeLogsFile -dataObject $script:AllTimeLogs
        Save-DataFile -filePath $script:CategoriesFile -dataObject $script:Categories
        
        $notifyIcon.Dispose()
    }
})

$powerModeHandler = {
    param($source, $e)
    
    # スリープに入る時
    if ($e.Mode -eq [Microsoft.Win32.PowerModes]::Suspend) {
        # もしタスクを記録中なら、ログを閉じて保存する
        if ($script:currentlyTrackingTaskID) {
            $script:trackingTimer.Stop()
            
            $logToStop = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $script:currentlyTrackingTaskID -and -not $_.EndTime } | Select-Object -Last 1
            if ($logToStop) {
                $logToStop.EndTime = (Get-Date -Format 'o')
                Save-TimeLogs
            }

            # どのタスクを中断したか、一時的にIDを保存しておく
            $script:suspendedTaskID = $script:currentlyTrackingTaskID
            $script:currentlyTrackingTaskID = $null
            Update-DataGridView
        }
    }
    # スリープから復帰した時
    elseif ($e.Mode -eq [Microsoft.Win32.PowerModes]::Resume) {
        # 中断したタスクがある場合
        if ($script:suspendedTaskID) {
            $taskToResume = $script:AllTasks | Where-Object { $_.ID -eq $script:suspendedTaskID } | Select-Object -First 1
            if ($taskToResume) {
                $confirmResult = [System.Windows.Forms.MessageBox]::Show(
                    "PCがスリープから復帰しました。`n`nタスク「$($taskToResume.タスク)」の記録を再開しますか？",
                    "記録の再開",
                    [System.Windows.Forms.MessageBoxButtons]::YesNo,
                    [System.Windows.Forms.MessageBoxIcon]::Question
                )
                
                if ($confirmResult -eq 'Yes') {
                    # 新しいログエントリを作成して記録を再開する
                    $script:currentlyTrackingTaskID = $script:suspendedTaskID
                    $newLog = [PSCustomObject]@{ 
                        TaskID = $script:currentlyTrackingTaskID;
                        StartTime = (Get-Date -Format 'o');
                        EndTime = $null
                    }
                    $script:AllTimeLogs = @($script:AllTimeLogs) + $newLog
                    $script:trackingTimer.Start()
                }
            }
            # 確認後は一時保存したIDをクリア
            $script:suspendedTaskID = $null
            Update-DataGridView # 表示を更新
        }
    }
}
[Microsoft.Win32.SystemEvents]::add_PowerModeChanged($powerModeHandler)

# フォームが閉じられるときにイベントハンドラを解除する処理を追加
$mainForm.Add_FormClosed({
    [Microsoft.Win32.SystemEvents]::remove_PowerModeChanged($powerModeHandler)
})

# フォームリサイズ時のエラーを回避するためのイベントハンドラ
$mainForm.Add_Resize({
    param($source, $e)
    
    $isMinimized = ($mainForm.WindowState -eq [System.Windows.Forms.FormWindowState]::Minimized)

    # 最小化時にレイアウトが崩れてエラーになる可能性のある、複雑なレイアウトパネルを直接非表示にする
    if ($script:calendarGrid) {
        $script:calendarGrid.Visible = -not $isMinimized
    }
    if ($script:kanbanLayout) {
        $script:kanbanLayout.Visible = -not $isMinimized
    }
})

[void]$mainForm.ShowDialog()

# --- 終了処理 ---
$script:trackingTimer.Dispose()
$script:idleCheckTimer.Dispose()
$script:globalImageList.Dispose()
$script:kanbanHeaderFont.Dispose()
$script:calendarHeaderFont.Dispose()
$script:previewFont.Dispose()
$script:datagridRegularFont.Dispose()
$script:datagridStrikeoutFont.Dispose()
$script:calendarDayFont.Dispose()
$script:calendarDayBoldFont.Dispose()
$script:calendarItemFont.Dispose()
$script:calendarItemBoldFont.Dispose()
$script:calendarItemStrikeoutFont.Dispose()
$script:calendarGridHeaderFont.Dispose()
$script:dayInfoBoldFont.Dispose()
$script:dayInfoRegularFont.Dispose()
$script:dayInfoItalicFont.Dispose()
$script:dayInfoCardTypeFont.Dispose()
$script:dayInfoCardTitleFont.Dispose()
$script:dayInfoCardDetailsFont.Dispose()
$script:dayInfoCardDetailsFont.Dispose()