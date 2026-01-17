﻿# ===================================================================
# ===================================================================
# 機能関数ファイル (v12.1)
# ===================================================================
# Task Manager Functions (v12.1)

# --- 内部ヘルパー関数 ---
function Convert-ToStandardWorkFile {
    param($workFileObject)

    # 既に新しい形式（DisplayName, Type, Content, DateAdded を持つ）であればそのまま返す
    # ただし、Contentが配列（バグデータ）の場合は修復が必要なため、ここでのリターンはスキップする
    if ($workFileObject -is [psobject] -and $workFileObject.PSObject.Properties.Name -contains 'Type' -and $workFileObject.PSObject.Properties.Name -contains 'DisplayName' -and $workFileObject.PSObject.Properties.Name -contains 'DateAdded' -and -not ($workFileObject.Content -is [System.Array] -or $workFileObject.Content -is [System.Collections.ArrayList])) {
        return $workFileObject
    }

    $content = ""
    $type = "File" # デフォルト
    $dateAdded = ""

    if ($workFileObject -is [string]) {
        $content = $workFileObject
    } elseif ($workFileObject -is [psobject]) {
        if ($workFileObject.PSObject.Properties.Name -contains 'Content') {
            $content = $workFileObject.Content
            # バグデータの救済: Contentが配列になってしまっている場合、文字列要素を抽出する
            if ($content -is [System.Array] -or $content -is [System.Collections.ArrayList]) {
                $content = $content | Where-Object { $_ -is [string] } | Select-Object -First 1
            }
        }
        if ($workFileObject.PSObject.Properties.Name -contains 'Type') {
            $type = $workFileObject.Type # 既存のTypeを優先
        }
        if ($workFileObject.PSObject.Properties.Name -contains 'DateAdded') {
            $dateAdded = $workFileObject.DateAdded
        }
    }

    # Contentに基づいてTypeを推測（既存のTypeがない場合）
    if ($workFileObject -isnot [psobject] -or -not ($workFileObject.PSObject.Properties.Name -contains 'Type')) {
        if ($content.StartsWith("http")) {
            $type = 'URL'
        } else {
            $ext = [System.IO.Path]::GetExtension($content).ToLower()
            if (@('.png', '.jpg', '.jpeg', '.bmp', '.gif') -contains $ext) {
                $type = 'Image'
            }
        }
    }

    # DisplayNameを生成
    $displayName = ""
    switch ($type) {
        'File'  { $displayName = [System.IO.Path]::GetFileName($content) }
        'Image' { $displayName = [System.IO.Path]::GetFileName($content) }
        'URL'   { $displayName = $content }
        'Memo'  { $displayName = "[メモ] " + ($content -split "`r`n|`n|`r")[0] + "..." }
        default { $displayName = $content }
    }

    # DateAddedが空の場合の補完ロジック
    if ([string]::IsNullOrEmpty($dateAdded)) {
        if ($type -in @('File', 'Image') -and (Test-Path -LiteralPath $content)) {
            try {
                $fileItem = Get-Item -LiteralPath $content
                $dateAdded = $fileItem.CreationTime.ToString("yyyy-MM-dd HH:mm")
            } catch {
                $dateAdded = (Get-Date).ToString("yyyy-MM-dd HH:mm")
            }
        } else {
            $dateAdded = (Get-Date).ToString("yyyy-MM-dd HH:mm")
        }
    }

    return [PSCustomObject]@{
        DisplayName = $displayName
        Type        = $type
        Content     = $content
        DateAdded   = $dateAdded
    }
}

# --- テーマ管理機能 ---
function Get-ThemeColors {
    param([bool]$IsDarkMode)
    if ($IsDarkMode) {
        # VSCode Dark+ inspired theme for better contrast and visibility
        return [PSCustomObject]@{
            BackColor      = [System.Drawing.Color]::FromArgb(30, 30, 30)      # Main window background
            ForeColor      = [System.Drawing.Color]::FromArgb(220, 220, 220)  # Main text color (off-white)
            ControlBack    = [System.Drawing.Color]::FromArgb(51, 51, 55)      # Background for inputs, lists
            ControlFore    = [System.Drawing.Color]::FromArgb(220, 220, 220)  # Text color for inputs, lists
            HeaderBack     = [System.Drawing.Color]::FromArgb(58, 58, 62)      # Group headers in DataGridView
            GridLine       = [System.Drawing.Color]::FromArgb(68, 68, 68)      # Grid lines
            ButtonBack     = [System.Drawing.Color]::FromArgb(85, 85, 85)      # Button background
            ButtonBorder   = [System.Drawing.Color]::FromArgb(110, 110, 110)   # Button border
            HighlightBack  = [System.Drawing.Color]::FromArgb(0, 120, 215)     # Selection highlight
            TodayBack      = [System.Drawing.Color]::FromArgb(60, 60, 30)      # Calendar: Today's background
            WeekendBack    = [System.Drawing.Color]::FromArgb(45, 55, 70)      # Calendar: Weekend background
            SundayBack     = [System.Drawing.Color]::FromArgb(65, 40, 40)      # Calendar: Sunday background
            SaturdayBack   = [System.Drawing.Color]::FromArgb(40, 50, 65)      # Calendar: Saturday background
            ErrorFore      = [System.Drawing.Color]::FromArgb(244, 102, 102)   # Error text color
        }
    } else {
        return [PSCustomObject]@{
            BackColor = [System.Drawing.SystemColors]::Control
            ForeColor = [System.Drawing.SystemColors]::ControlText
            ControlBack = [System.Drawing.SystemColors]::Window
            ControlFore = [System.Drawing.SystemColors]::WindowText
            HeaderBack = [System.Drawing.Color]::LightGray
            GridLine = [System.Drawing.Color]::LightGray
            ButtonBack = [System.Drawing.SystemColors]::Control
            ButtonBorder = [System.Drawing.SystemColors]::ControlDark
            HighlightBack = [System.Drawing.SystemColors]::Highlight
            TodayBack = [System.Drawing.Color]::FromArgb(255, 255, 224)
            WeekendBack = [System.Drawing.Color]::FromArgb(230, 245, 255) # Same as Saturday
            SundayBack = [System.Drawing.Color]::FromArgb(255, 240, 240)
            SaturdayBack = [System.Drawing.Color]::FromArgb(230, 245, 255)
            ErrorFore = [System.Drawing.Color]::Red
        }
    }
}

function Set-Theme {
    param([System.Windows.Forms.Form]$form, [bool]$IsDarkMode)

    # --- ウィンドウ枠（タイトルバー）のダークモード適用 (Windows 10/11) ---
    try {
        $hwnd = $form.Handle
        # DWMWA_USE_IMMERSIVE_DARK_MODE (Attribute 20 for Win11, 19 for Win10)
        $attr = 20 
        $val = if ($IsDarkMode) { 1 } else { 0 }
        $size = [System.Runtime.InteropServices.Marshal]::SizeOf($val)
        $result = [TaskManager.WinAPI.Dwmapi]::DwmSetWindowAttribute($hwnd, $attr, [ref]$val, $size)
        if ($result -ne 0) {
            $attr = 19 # Fallback for older Win10 builds
            [void][TaskManager.WinAPI.Dwmapi]::DwmSetWindowAttribute($hwnd, $attr, [ref]$val, $size)
        }
    } catch {}

    $colors = Get-ThemeColors -IsDarkMode $IsDarkMode
    $form.BackColor = $colors.BackColor
    $form.ForeColor = $colors.ForeColor

    $applyToControls = {
        param($controls)
        foreach ($ctrl in $controls) {
            if ($ctrl -is [System.Windows.Forms.ToolStrip]) {
                if ($IsDarkMode) { $ctrl.Renderer = New-Object DarkModeRenderer }
                else { $ctrl.Renderer = New-Object System.Windows.Forms.ToolStripProfessionalRenderer }
            }
            if ($ctrl -is [System.Windows.Forms.TextBox] -or $ctrl -is [System.Windows.Forms.ListBox] -or $ctrl -is [System.Windows.Forms.ComboBox] -or $ctrl -is [System.Windows.Forms.NumericUpDown] -or $ctrl -is [System.Windows.Forms.ListView] -or $ctrl -is [System.Windows.Forms.DateTimePicker] -or $ctrl -is [System.Windows.Forms.DataGridView]) {
                $ctrl.BackColor = $colors.ControlBack
                $ctrl.ForeColor = $colors.ControlFore
                if ($ctrl -is [System.Windows.Forms.DataGridView]) {
                    $ctrl.GridColor = $colors.GridLine
                    $ctrl.ColumnHeadersDefaultCellStyle.BackColor = $colors.HeaderBack
                }
            } elseif ($ctrl -is [System.Windows.Forms.Button]) {
                $ctrl.BackColor = $colors.ButtonBack
                $ctrl.ForeColor = $colors.ControlFore
                $ctrl.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
                $ctrl.FlatAppearance.BorderColor = $colors.ButtonBorder
            } elseif ($ctrl -is [System.Windows.Forms.Label] -or $ctrl -is [System.Windows.Forms.CheckBox] -or $ctrl -is [System.Windows.Forms.RadioButton] -or $ctrl -is [System.Windows.Forms.GroupBox] -or $ctrl -is [System.Windows.Forms.TabPage]) {
                $ctrl.ForeColor = $colors.ForeColor
            } elseif ($ctrl -is [System.Windows.Forms.Panel] -or $ctrl -is [System.Windows.Forms.SplitContainer]) {
                $ctrl.BackColor = $colors.BackColor
                $ctrl.ForeColor = $colors.ForeColor
            }
            
            # スクロールバーのダークモード適用 (Windows 10/11)
            if ($ctrl -is [System.Windows.Forms.ListView] -or $ctrl -is [System.Windows.Forms.ListBox] -or $ctrl -is [System.Windows.Forms.TreeView] -or $ctrl -is [System.Windows.Forms.DataGridView] -or $ctrl -is [System.Windows.Forms.TextBox] -or ($ctrl -is [System.Windows.Forms.ScrollableControl] -and $ctrl.AutoScroll)) {
                try {
                    $themeName = if ($IsDarkMode) { "DarkMode_Explorer" } else { "Explorer" }
                    [void][TaskManager.WinAPI.UxTheme]::SetWindowTheme($ctrl.Handle, $themeName, $null)
                } catch {}
            }

            if ($ctrl.Controls.Count -gt 0) { & $applyToControls $ctrl.Controls }
        }
    }
    & $applyToControls $form.Controls
}

# --- アプリケーションの初期化処理 ---
function Initialize-ApplicationAssets {
    try {
        # Add folder icon
        if (-not $script:globalImageList.Images.ContainsKey("__folder__")) {
            $sfi = New-Object -TypeName TaskManager.WinAPI.User32+SHFILEINFO
            $flags = 0x100 -bor 0x400 # SHGFI_ICON | SHGFI_SMALLICON
            [void][TaskManager.WinAPI.User32]::SHGetFileInfo($env:SystemRoot, 0x10, ([ref]$sfi), ([System.Runtime.InteropServices.Marshal]::SizeOf($sfi)), $flags)
            if ($sfi.hIcon -ne [System.IntPtr]::Zero) {
                $folderIcon = [System.Drawing.Icon]::FromHandle($sfi.hIcon).Clone()
                [void][TaskManager.WinAPI.User32]::DestroyIcon($sfi.hIcon)
                $script:globalImageList.Images.Add("__folder__", $folderIcon) | Out-Null
            }
        }
        # Add a generic web icon
        if (-not $script:globalImageList.Images.ContainsKey("__url__")) {
            $sfi = New-Object -TypeName TaskManager.WinAPI.User32+SHFILEINFO
            $flags = 0x100 -bor 0x400 -bor 0x80 # SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES
            [void][TaskManager.WinAPI.User32]::SHGetFileInfo(".html", 0x80, ([ref]$sfi), ([System.Runtime.InteropServices.Marshal]::SizeOf($sfi)), $flags)
            if ($sfi.hIcon -ne [System.IntPtr]::Zero) {
                $urlIcon = [System.Drawing.Icon]::FromHandle($sfi.hIcon).Clone()
                [void][TaskManager.WinAPI.User32]::DestroyIcon($sfi.hIcon)
                $script:globalImageList.Images.Add("__url__", $urlIcon) | Out-Null
            }
        }
        # Add a generic memo icon
        if (-not $script:globalImageList.Images.ContainsKey("__memo__")) {
            $sfi = New-Object -TypeName TaskManager.WinAPI.User32+SHFILEINFO
            $flags = 0x100 -bor 0x400 -bor 0x80 # SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES
            [void][TaskManager.WinAPI.User32]::SHGetFileInfo(".txt", 0x80, ([ref]$sfi), ([System.Runtime.InteropServices.Marshal]::SizeOf($sfi)), $flags)
            if ($sfi.hIcon -ne [System.IntPtr]::Zero) {
                $memoIcon = [System.Drawing.Icon]::FromHandle($sfi.hIcon).Clone()
                [void][TaskManager.WinAPI.User32]::DestroyIcon($sfi.hIcon)
                $script:globalImageList.Images.Add("__memo__", $memoIcon) | Out-Null
            }
        }
        # Add a generic file icon
        if (-not $script:globalImageList.Images.ContainsKey("__file__")) {
            $sfi = New-Object -TypeName TaskManager.WinAPI.User32+SHFILEINFO
            $flags = 0x100 -bor 0x400 -bor 0x80 # SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES
            [void][TaskManager.WinAPI.User32]::SHGetFileInfo("dummy.dat", 0x80, ([ref]$sfi), ([System.Runtime.InteropServices.Marshal]::SizeOf($sfi)), $flags)
            if ($sfi.hIcon -ne [System.IntPtr]::Zero) {
                $fileIcon = [System.Drawing.Icon]::FromHandle($sfi.hIcon).Clone()
                [void][TaskManager.WinAPI.User32]::DestroyIcon($sfi.hIcon)
                $script:globalImageList.Images.Add("__file__", $fileIcon) | Out-Null
            }
        }
        # Add a generic image icon
        if (-not $script:globalImageList.Images.ContainsKey("__image__")) {
            $sfi = New-Object -TypeName TaskManager.WinAPI.User32+SHFILEINFO
            $flags = 0x100 -bor 0x400 -bor 0x80 # SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES
            [void][TaskManager.WinAPI.User32]::SHGetFileInfo("dummy.jpg", 0x80, ([ref]$sfi), ([System.Runtime.InteropServices.Marshal]::SizeOf($sfi)), $flags)
            if ($sfi.hIcon -ne [System.IntPtr]::Zero) {
                $imageIcon = [System.Drawing.Icon]::FromHandle($sfi.hIcon).Clone()
                [void][TaskManager.WinAPI.User32]::DestroyIcon($sfi.hIcon)
                $script:globalImageList.Images.Add("__image__", $imageIcon) | Out-Null
            }
        }
    } catch {
        [System.Windows.Forms.MessageBox]::Show("Initialize-ApplicationAssets エラー: $($_.Exception.Message)`n`nスタックトレース:`n$($_.ScriptStackTrace)", "エラー", "OK", "Error")
    }
    if(Test-Path $script:notifiedLogPath){
        $lastWrite = (Get-Item $script:notifiedLogPath).LastWriteTime.Date
        if($lastWrite -ne (Get-Date).Date){
            Remove-Item $script:notifiedLogPath -Force
        }
    }
}

# --- 通知機能 ---
function Invoke-NotificationCheck {
    $today = (Get-Date).Date
    $config = $script:Settings
    $projects = $script:Projects
    $tasks = $script:AllTasks
    $notifiedLog = if (Test-Path $script:notifiedLogPath) { Get-Content $script:notifiedLogPath -Encoding UTF8 } else { @() }
    if($config.NotificationButtonDays) { $script:notificationButtonDays = $config.NotificationButtonDays }
    $notificationEndDate = $today.AddDays($script:notificationButtonDays - 1)
    $notificationsForButton = @()
    $newNotificationsForPopup = @()
    $holidays = @()
    function Get-PreviousBusinessDay { param($date)
        $prevDay = $date.AddDays(-1)
        while($prevDay.DayOfWeek -in @([DayOfWeek]::Saturday, [DayOfWeek]::Sunday) -or $holidays -contains $prevDay.ToString("yyyy-MM-dd")){
            $prevDay = $prevDay.AddDays(-1)
        }
        return $prevDay
    }
    $allPotentialNotifications = @()
    foreach($task in $tasks | Where-Object { $_.進捗度 -ne '完了済み' -and $_.期日 }){
        try {
            $dueDate = [datetime]$task.期日; $notificationSetting = $task.通知設定
            if($notificationSetting -eq '全体設定に従う' -and $task.ProjectID){
                $project = $projects | Where-Object { $_.ProjectID -eq $task.ProjectID } | Select-Object -First 1
                $projectSetting = if($project){ $project.Notification } else { "全体設定に従う" }
                $notificationSetting = if($projectSetting -ne '全体設定に従う') { $projectSetting } else { $config.GlobalNotification }
            }
            $notifyDate = switch ($notificationSetting) {
                "当日" { $dueDate } "1日前" { $dueDate.AddDays(-1) } "3日前" { $dueDate.AddDays(-3) } "1週間前" { $dueDate.AddDays(-7) } "前の営業日" { Get-PreviousBusinessDay -date $dueDate }
            }
            if ($notifyDate) { $allPotentialNotifications += [PSCustomObject]@{ Type = 'Task'; NotifyDate = $notifyDate.Date; DueDate = $dueDate; ItemObject = $task } }
        } catch {}
    }
    foreach($project in $projects){
    if($project.ProjectDueDate){
        try {
            $notificationSetting = $project.Notification
            if($notificationSetting -eq '全体設定に従う'){
                $notificationSetting = $config.GlobalNotification
            }

            if ($notificationSetting -ne '通知しない') {
                $dueDate = [datetime]$project.ProjectDueDate
                $notifyDate = switch ($notificationSetting) {
                    "当日" { $dueDate } "1日前" { $dueDate.AddDays(-1) } "3日前" { $dueDate.AddDays(-3) } "1週間前" { $dueDate.AddDays(-7) } "前の営業日" { Get-PreviousBusinessDay -date $dueDate }
                }
                if ($notifyDate) { 
                    $itemObjectForLog = @{ Name = $project.ProjectName; Notification = $project.Notification }
                    $allPotentialNotifications += [PSCustomObject]@{ Type = 'Subject'; NotifyDate = $notifyDate.Date; DueDate = $dueDate; ItemObject = $itemObjectForLog } 
                }
            }
        } catch {}
    }
}
    foreach($notification in $allPotentialNotifications | Sort-Object NotifyDate) {
        if($notification.NotifyDate -ge $today -and $notification.NotifyDate -le $notificationEndDate) { $notificationsForButton += $notification }
        if($notification.NotifyDate -eq $today){
            $uniqueId = if($notification.Type -eq 'Task'){ "$($notification.ItemObject.ID)_$($notification.ItemObject.通知設定)" } else { "$($notification.ItemObject.Name)_$($notification.ItemObject.Notification)" }
            if($notifiedLog -notcontains $uniqueId){
                $newNotificationsForPopup += $notification
                Add-Content -Path $script:notifiedLogPath -Value $uniqueId -Encoding UTF8
            }
        }
    }
    $btnNotifications.Visible = $true
    if($notificationsForButton.Count -gt 0){
        $btnNotifications.Text = "🔔 通知 ($($notificationsForButton.Count))"; $btnNotifications.BackColor = [System.Drawing.Color]::Tomato; $btnNotifications.Tag = $notificationsForButton; $btnNotifications.Enabled = $true; $btnNotifications.Visible = $true
    } else {
        $btnNotifications.Text = "🔔 通知"; $btnNotifications.BackColor = [System.Drawing.SystemColors]::Control; $btnNotifications.Enabled = $false
    }
    if($newNotificationsForPopup.Count -gt 0){
        [System.Media.SystemSounds]::Beep.Play()
        
        $notifyStyle = if ($script:Settings.NotificationStyle) { $script:Settings.NotificationStyle } else { "Dialog" }
        
        if ($notifyStyle -eq "Balloon" -and $notifyIcon -and $notifyIcon.Visible) {
            $title = "タスク期限通知"
            $msg = "本日期限のタスク/プロジェクトが $($newNotificationsForPopup.Count) 件あります。"
            $notifyIcon.ShowBalloonTip(3000, $title, $msg, [System.Windows.Forms.ToolTipIcon]::Info)
        } else {
            Show-NotificationForm -notifications $newNotificationsForPopup
        }
        $btnNotifications.Visible = $true 
    }
}

# --- データ操作に関する関数 ---

# 堅牢化されたデータ読み込み/書き込み関数群
function Get-DataFileContent {
    param([string]$filePath)
    
    if (-not (Test-Path $filePath)) { return $null }

    $content = $null
    $retryCount = 3
    $success = $false
    $lastError = $null

    for ($i = 0; $i -lt $retryCount; $i++) {
        try {
            # 読み込みロジックの強化: .NETクラスを使用してBOMとエンコーディングをより確実に処理
            try {
                $content = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::UTF8)
            } catch {
                try {
                    $content = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::Default)
                } catch {
                    $content = Get-Content -Path $filePath -Raw -ErrorAction Stop
                }
            }
            $success = $true
            break
        } catch {
            $lastError = $_
            Start-Sleep -Milliseconds 200
        }
    }

    if (-not $success) {
        [System.Windows.Forms.MessageBox]::Show("ファイルの読み込みに失敗しました: `n$filePath`n`nエラー: $($lastError.Exception.Message)", "読み込みエラー", "OK", "Warning")
        return $null
    }

    if ([string]::IsNullOrWhiteSpace($content)) { return $null }
    
    # BOMやゴミ文字の除去
    $content = $content.Trim([char]0xfeff)
    if ($content.StartsWith("ï»¿")) { $content = $content.Substring(3) }

    try {
        return $content | ConvertFrom-Json -ErrorAction Stop
    } catch {
        [System.Windows.Forms.MessageBox]::Show("ファイルの解析(JSON)に失敗しました: `n$filePath`n`nエラー: $($_.Exception.Message)", "データ形式エラー", "OK", "Warning")
        return $null
    }
}

function Save-DataFile {
    param([string]$filePath, $dataObject)
    try {
        $json = $dataObject | ConvertTo-Json -Depth 5
        Set-Content -Path $filePath -Value $json -Encoding UTF8 -Force
    } catch {
        [System.Windows.Forms.MessageBox]::Show("ファイルの保存に失敗しました: `n$filePath`n`nエラー: $($_.Exception.Message)", "保存エラー", "OK", "Error")
    }
}

function Get-Settings {
    $defaultSettings = @{
        # --- アプリ動作 ---
        RunAtStartup       = $false
        MinimizeToTray     = $false
        AlwaysOnTop        = $false
        WindowOpacity      = 1.0
        Passcode           = ""
        EnableSoundEffects = $true

        # --- 表示・カレンダー ---
        StartupView       = "List"
        ShowTooltips      = $true
        DateFormat        = "yyyy/MM/dd"
        DayStartHour      = 0
        CalendarWeekStart = 0 # 0: Sunday, 1: Monday
        ColorWeekend      = $true
        TimelineStartHour = 8
        TimelineEndHour   = 24

        # --- タスク操作 ---
        ListDensity       = "Standard"
        ShowStrikethrough = $true
        ShowKanbanDone    = $true
        DefaultSort       = "DueDate"
        DoubleClickAction = "Edit"
        NotificationStyle = "Dialog"
        AlertDaysRed      = 0
        AlertDaysYellow   = 3
        DefaultPriority   = "中"
        DefaultDueOffset  = 0

        # --- データ・分析 ---
        BackupPath          = ""
        BackupIntervalDays  = 1
        AutoArchiveDays     = 30
        ArchiveTasksOnCompletion = $false
        AnalysisWarnPercent = 40
        PomodoroWorkMinutes = 25

        # --- 既存設定 (互換性維持) ---
        GlobalNotification              = "当日"
        NotificationButtonDays          = 7
        BackupRetentionDays             = 30
        ArchiveCompressionDays          = 90
        AutoArchiveProjectsDays         = 60
        ArchiveTasksOnProjectCompletion = $false
        LastBackupDate                  = ""
        LongTaskNotificationMinutes     = 180
        IdleTimeoutMinutes              = 5
        TimeLogOverlapBehavior          = "Error"
        EventNotificationEnabled        = $true
        EventNotificationMinutes        = 15
        EventOverlapWarning             = $true
        EnableEventOverlapWarning       = $true
        IsDarkMode                      = $false
        HideCompletedTasks              = $false
        LeadTimeExcludeStatuses         = @()
    }

    $settingsFromFile = Get-DataFileContent -filePath $script:SettingsFile

    # 常にPSCustomObjectとして処理するように、デフォルト設定をベースに新しいオブジェクトを作成する
    $finalSettings = [PSCustomObject]$defaultSettings.Clone()

    if ($settingsFromFile) {
        # ファイルから読み込んだ設定でデフォルト値を上書きする
        foreach ($prop in $settingsFromFile.PSObject.Properties) {
            if ($finalSettings.PSObject.Properties.Name -contains $prop.Name) {
                $finalSettings.($prop.Name) = $prop.Value
            }
        }
    } elseif (Test-Path $script:SettingsFile) {
        # ファイルが存在するのに読み込めなかった場合は、上書きを防ぐためにエラーにする
        if ((Get-Item $script:SettingsFile).Length -gt 0) {
            throw "設定ファイルの読み込みに失敗しました。データ保護のため処理を中断します。`n($script:SettingsFile)"
        }
    }

    # 常に最新のキーセットでファイルを保存し直すことで、古いキーの削除と新しいキーの追加を確実に行う
    Save-DataFile -filePath $script:SettingsFile -dataObject $finalSettings
    
    return $finalSettings
}

function Get-Projects {
    $data = Get-DataFileContent -filePath $script:ProjectsFile

    if (-not $data) {
        if (Test-Path $script:ProjectsFile) {
            # ファイルが存在するのに読み込めなかった場合は、上書きを防ぐためにエラーにする
            if ((Get-Item $script:ProjectsFile).Length -gt 0) {
                throw "プロジェクトデータの読み込みに失敗しました。データ保護のため処理を中断します。`n($script:ProjectsFile)"
            }
        }
        $defaultProject = @([PSCustomObject]@{ ProjectID = [guid]::NewGuid().ToString(); ProjectName = "未分類"; ProjectDueDate = $null; WorkFiles = @(); Notification = "全体設定に従う"; ProjectColor = "#D3D3D3"; AutoArchiveTasks = $true })
        Save-DataFile -filePath $script:ProjectsFile -dataObject $defaultProject
        return $defaultProject
    }

    # If the data is a single object, convert it to an array
    if ($data -isnot [array]) {
        $data = @($data)
    }

    if ($data -is [array]) {
        $validatedData = @($data | Where-Object { $_ -is [psobject] -and $_.PSObject.Properties['ProjectID'] -and $_.PSObject.Properties['ProjectName'] -and -not [string]::IsNullOrWhiteSpace($_.ProjectName) })
        
        # WorkFiles を新しい標準形式に変換する
        foreach ($project in $validatedData) {
            if ($project.WorkFiles) {
                $convertedWorkFiles = @()
                foreach ($file in $project.WorkFiles) {
                    $convertedWorkFiles += Convert-ToStandardWorkFile -workFileObject $file
                }
                $project.WorkFiles = $convertedWorkFiles
            }

            # AutoArchiveTasksプロパティの存在を確認し、なければデフォルト値($true)で追加
            if (-not $project.PSObject.Properties.Name.Contains('AutoArchiveTasks')) {
                $project | Add-Member -MemberType NoteProperty -Name 'AutoArchiveTasks' -Value $true
            }
        }

        if ($validatedData.Count -eq 0) {
            $defaultProject = @([PSCustomObject]@{ ProjectID = [guid]::NewGuid().ToString(); ProjectName = "未分類"; ProjectDueDate = $null; WorkFiles = @(); Notification = "全体設定に従う"; ProjectColor = "#D3D3D3"; AutoArchiveTasks = $true })
            Save-DataFile -filePath $script:ProjectsFile -dataObject @($defaultProject)
            return @($defaultProject)
        }

        return ,$validatedData
    }

    Write-Error "projects.json が予期せぬ形式です。"
    return @()
}


function Get-Categories {
    $data = Get-DataFileContent -filePath $script:CategoriesFile
    if ($null -eq $data) { 
        if (Test-Path $script:CategoriesFile) {
            if ((Get-Item $script:CategoriesFile).Length -gt 0) {
                throw "カテゴリデータの読み込みに失敗しました。データ保護のため処理を中断します。`n($script:CategoriesFile)"
            }
        }
        return @{} 
    }
    else { return $data }
}
function Get-Templates {
    $data = Get-DataFileContent -filePath $script:TemplatesFile
    if ($null -eq $data) { 
        if (Test-Path $script:TemplatesFile) {
            if ((Get-Item $script:TemplatesFile).Length -gt 0) {
                throw "テンプレートデータの読み込みに失敗しました。データ保護のため処理を中断します。`n($script:TemplatesFile)"
            }
        }
        return @{} 
    }
    else { return $data }
}

function Get-Events {
    $data = Get-DataFileContent -filePath $script:EventsFile
    if ($null -eq $data) {
        if (Test-Path $script:EventsFile) {
            if ((Get-Item $script:EventsFile).Length -gt 0) {
                throw "イベントデータの読み込みに失敗しました。データ保護のため処理を中断します。`n($script:EventsFile)"
            }
        }
        return [PSCustomObject]@{} 
    }
    if ($data -isnot [PSCustomObject]) { 
        # 予期しない形式（配列など）の場合はエラーとする
        throw "イベントデータの形式が不正です。データ保護のため処理を中断します。`n($script:EventsFile)"
    }
    else { return $data }
}

function Save-Events {
    Save-DataFile -filePath $script:EventsFile -dataObject $script:AllEvents
}

function Show-EventInputForm {
    # Updated as per request.
    param(
        [datetime]$initialDate,
        $initialEndTime = $null,
        [psobject]$existingEvent = $null
    )

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "イベントの追加/編集"
    $form.Size = New-Object System.Drawing.Size(400, 270)
    $form.StartPosition = 'CenterParent'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false

    # Start Time
    $labelStartDate = New-Object System.Windows.Forms.Label; $labelStartDate.Text = "開始日時:"; $labelStartDate.Location = "15, 15"; $labelStartDate.AutoSize = $true
    $timePickerStart = New-Object System.Windows.Forms.DateTimePicker; $timePickerStart.Location = "15, 35"; $timePickerStart.Width = 160
    $timePickerStart.Format = 'Custom'
    $timePickerStart.CustomFormat = "yyyy/MM/dd HH:mm"
    
    # End Time
    $labelEndDate = New-Object System.Windows.Forms.Label; $labelEndDate.Text = "終了日時:"; $labelEndDate.Location = "200, 15"; $labelEndDate.AutoSize = $true
    $timePickerEnd = New-Object System.Windows.Forms.DateTimePicker; $timePickerEnd.Location = "200, 35"; $timePickerEnd.Width = 160
    $timePickerEnd.Format = 'Custom'
    $timePickerEnd.CustomFormat = "yyyy/MM/dd HH:mm"

    # All-day CheckBox
    $checkAllDay = New-Object System.Windows.Forms.CheckBox; $checkAllDay.Text = "終日"; $checkAllDay.Location = "15, 70"; $checkAllDay.AutoSize = $true
    
    # Title TextBox
    $labelTitle = New-Object System.Windows.Forms.Label; $labelTitle.Text = "タイトル:"; $labelTitle.Location = "15, 110"; $labelTitle.AutoSize = $true
    $textTitle = New-Object System.Windows.Forms.TextBox; $textTitle.Location = "15, 130"; $textTitle.Size = "350, 25"

    # Event handler for All-day checkbox
    $checkAllDay.Add_CheckedChanged({
        $timePickerStart.Enabled = -not $checkAllDay.Checked
        $timePickerEnd.Enabled = -not $checkAllDay.Checked
    })

    # Load existing data
    if ($existingEvent) {
        $textTitle.Text = $existingEvent.Title
        if ($existingEvent.PSObject.Properties['StartTime'] -and $existingEvent.StartTime) {
            try { $timePickerStart.Value = [datetime]$existingEvent.StartTime } catch {}
        } elseif ($existingEvent.PSObject.Properties['EventDate'] -and $existingEvent.EventDate) {
            try { $timePickerStart.Value = [datetime]$existingEvent.EventDate } catch {}
        }
        
        if ($existingEvent.PSObject.Properties['EndTime'] -and $existingEvent.EndTime) {
            try { $timePickerEnd.Value = [datetime]$existingEvent.EndTime } catch {}
        } else {
            $timePickerEnd.Value = $timePickerStart.Value.AddHours(1)
        }
        
        if ($existingEvent.PSObject.Properties['IsAllDay'] -and $existingEvent.IsAllDay) {
            $checkAllDay.Checked = $existingEvent.IsAllDay
        }
    } else {
        $timePickerStart.Value = $initialDate
        if ($initialEndTime) {
            $timePickerEnd.Value = [datetime]$initialEndTime
        } else {
            $timePickerEnd.Value = $initialDate.AddHours(1)
        }
    }

    # Buttons
    $btnOK = New-Object System.Windows.Forms.Button; $btnOK.Text = "OK"; $btnOK.Location = "100, 190"; $btnOK.Size = "80, 25"
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "キャンセル"; $btnCancel.Location = "190, 190"; $btnCancel.Size = "80, 25"
    $form.AcceptButton = $btnOK; $form.CancelButton = $btnCancel

    $form.Controls.AddRange(@($labelStartDate, $timePickerStart, $labelEndDate, $timePickerEnd, $checkAllDay, $labelTitle, $textTitle, $btnOK, $btnCancel))

    $btnOK.Add_Click({
        if ([string]::IsNullOrWhiteSpace($textTitle.Text)) {
            [System.Windows.Forms.MessageBox]::Show("タイトルを入力してください。", "入力エラー", "OK", "Error")
            return
        }
        if ((-not $checkAllDay.Checked) -and ($timePickerEnd.Value -le $timePickerStart.Value)) {
            [System.Windows.Forms.MessageBox]::Show("終了日時は開始日時より後に設定してください。", "入力エラー", "OK", "Error")
            return
        }
        $form.Tag = [PSCustomObject]@{
            StartTime = $timePickerStart.Value
            EndTime   = $timePickerEnd.Value
            IsAllDay = $checkAllDay.Checked
            Title     = $textTitle.Text.Trim()
        }
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    })
    $btnCancel.Add_Click({ $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Close() })

    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    if ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        return $form.Tag
    }
    return $null
}

function Start-EditEvent {
    param(
        [psobject]$eventToEdit,
        [datetime]$eventDate # This is the original date of the event, used to find it
    )

    $originalDateString = $eventDate.ToString("yyyy-MM-dd")
    
    $initialFormDate = try { [datetime]$eventToEdit.StartTime } catch { $eventDate }
    
    $eventData = Show-EventInputForm -initialDate $initialFormDate -existingEvent $eventToEdit
    if ($null -eq $eventData) {
        return
    }
    # StartTimeプロパティの存在と値を念のため確認
    if (-not $eventData.PSObject.Properties.Name.Contains('StartTime') -or $null -eq $eventData.StartTime) {
        Write-Warning "Show-EventInputFormから無効なイベントデータが返されました。"
        return
    }

    if ($script:Settings.EnableEventOverlapWarning -and (Test-EventOverlap -start $eventData.StartTime -end $eventData.EndTime -excludeId $eventToEdit.ID)) {
        $msg = "指定された時間帯は他の予定と重複しています。保存しますか？"
        if ([System.Windows.Forms.MessageBox]::Show($msg, "重複の警告", "YesNo", "Warning") -eq "No") { return }
    }

    $newDateString = $eventData.StartTime.ToString("yyyy-MM-dd")

    $eventToEdit.Title     = $eventData.Title
    $eventToEdit.StartTime = $eventData.StartTime.ToString("o")
    $eventToEdit.EndTime   = $eventData.EndTime.ToString("o")
    $eventToEdit.IsAllDay  = $eventData.IsAllDay
    if ($eventToEdit.PSObject.Properties['EventDate']) { $eventToEdit.PSObject.Properties.Remove('EventDate') }
    if ($eventToEdit.PSObject.Properties['Date']) { $eventToEdit.PSObject.Properties.Remove('Date') }

    if ($newDateString -ne $originalDateString) {
        $eventsOnOldDate = [System.Collections.ArrayList]@($script:AllEvents.$originalDateString)
        $eventIndex = -1
        for($i = 0; $i -lt $eventsOnOldDate.Count; $i++) {
            if ($eventsOnOldDate[$i].ID -eq $eventToEdit.ID) {
                $eventIndex = $i
                break
            }
        }
        if ($eventIndex -ne -1) {
            $eventsOnOldDate.RemoveAt($eventIndex)
            $script:AllEvents.$originalDateString = $eventsOnOldDate
        }

        if (-not $script:AllEvents.PSObject.Properties[$newDateString]) {
            $script:AllEvents | Add-Member -MemberType NoteProperty -Name $newDateString -Value @()
        }
        $eventsOnNewDate = [System.Collections.ArrayList]@($script:AllEvents.$newDateString)
        $eventsOnNewDate.Add($eventToEdit)
        $script:AllEvents.$newDateString = $eventsOnNewDate
    }

    Save-Events
    Update-CalendarGrid -dateInMonth $script:currentCalendarDate
    Update-DayInfoPanel -date $script:selectedCalendarDate
    Update-TimelineView -date $script:selectedCalendarDate
}

function Start-AddNewEvent {
    param([datetime]$initialDate)

    # フォームでキャンセルされた場合に備え、戻り値をチェックする
    $eventData = Show-EventInputForm -initialDate $initialDate
    if ($null -eq $eventData) {
        return
    }
    # StartTimeプロパティの存在を念のため確認
    if (-not $eventData.PSObject.Properties.Name.Contains('StartTime')) {
        Write-Warning "Show-EventInputFormから無効なイベントデータが返されました。"
        return
    }

    if ($script:Settings.EnableEventOverlapWarning -and (Test-EventOverlap -start $eventData.StartTime -end $eventData.EndTime)) {
        $msg = "指定された時間帯は他の予定と重複しています。保存しますか？"
        if ([System.Windows.Forms.MessageBox]::Show($msg, "重複の警告", "YesNo", "Warning") -eq "No") { return }
    }

    $dateString = $eventData.StartTime.ToString("yyyy-MM-dd")

    # If there's no entry for this date, create one
    if (-not $script:AllEvents.PSObject.Properties[$dateString]) {
        $script:AllEvents | Add-Member -MemberType NoteProperty -Name $dateString -Value @()
    }

    # Add the new event object
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
    Update-CalendarGrid -dateInMonth $script:currentCalendarDate
    Update-DayInfoPanel -date $script:selectedCalendarDate
    Update-TimelineView -date $script:selectedCalendarDate
}

function Test-EventOverlap {
    param($start, $end, $excludeId)
    # nullが渡された場合にエラーになるのを防ぐ
    if (-not $start -or -not $end) { return $false }

    $dateStr = $start.ToString("yyyy-MM-dd")
    $evts = if ($script:AllEvents.PSObject.Properties[$dateStr]) { @($script:AllEvents.PSObject.Properties[$dateStr].Value) } else { @() }
    foreach ($e in $evts) {
        if ($excludeId -and $e.ID -eq $excludeId) { continue }
        if ($e.IsAllDay) { continue }

        # 新旧両方のデータ形式（StartTime と EventDate）に堅牢に対応する
        $eStart = $null
        if ($e.PSObject.Properties['StartTime']) {
            try { $eStart = [datetime]$e.StartTime } catch {}
        } elseif ($e.PSObject.Properties['EventDate']) {
            try { $eStart = [datetime]$e.EventDate } catch {}
        }
        if (-not $eStart) { continue } # 有効な開始時刻がなければスキップ

        $eEnd = $null
        if ($e.PSObject.Properties['EndTime']) {
            try { $eEnd = [datetime]$e.EndTime } catch {}
        }
        if (-not $eEnd -or $eEnd -le $eStart) { $eEnd = $eStart.AddHours(1) } # 終了時刻が無効な場合は1時間と仮定

        if ($start -lt $eEnd -and $end -gt $eStart) { return $true }
    }
    return $false
}

function Invoke-EventNotificationCheck {
    if (-not $script:Settings.EventNotificationEnabled) { return }

    if ($null -eq $script:NotifiedEventIds) {
        $script:NotifiedEventIds = New-Object System.Collections.ArrayList
    }

    $now = Get-Date
    $limit = $now.AddMinutes([int]$script:Settings.EventNotificationMinutes)
    $todayString = $now.ToString("yyyy-MM-dd")

    if ($script:AllEvents -and $script:AllEvents.PSObject.Properties.Name -contains $todayString) {
        $todaysEvents = @($script:AllEvents.$todayString) # 常に配列として扱う
        if ($todaysEvents) {
            foreach ($evt in $todaysEvents) {
                # 新しい 'StartTime' プロパティをチェックし、無効なオブジェクトや終日イベントはスキップ
                if ($evt -isnot [psobject] -or -not $evt.PSObject.Properties['StartTime'] -or $evt.IsAllDay) { continue }
                
                try {
                    $evtStartTime = [datetime]$evt.StartTime
                    if ($evtStartTime -ge $now -and $evtStartTime -le $limit -and -not $script:NotifiedEventIds.Contains($evt.ID)) {
                        [System.Windows.Forms.MessageBox]::Show("予定: $($evt.Title) ($($evtStartTime.ToString('HH:mm')))", "リマインダー", "OK", "Information")
                        [void]$script:NotifiedEventIds.Add($evt.ID)
                    }
                } catch {
                    # StartTime の日付変換エラーは無視
                }
            }
        }
    }
}

function Get-TimeLogs {
    $data = Get-DataFileContent -filePath $script:TimeLogsFile
    if ($null -eq $data) { 
        if (Test-Path $script:TimeLogsFile) {
            if ((Get-Item $script:TimeLogsFile).Length -gt 0) {
                throw "時間記録データの読み込みに失敗しました。データ保護のため処理を中断します。`n($script:TimeLogsFile)"
            }
        }
        return @() 
    }
    # ToDo: Add validation logic here if needed in the future.
    return @($data)
}

function Save-TimeLogs {
    Save-DataFile -filePath $script:TimeLogsFile -dataObject $script:AllTimeLogs
}

function Add-TimeLog {
    param(
        [psobject]$newLog,
        [psobject]$logToExclude = $null
    )

    $newStartTime = [datetime]$newLog.StartTime
    $newEndTime = [datetime]$newLog.EndTime

    # Create a temporary list of logs to check against
    $logsToCheck = $script:AllTimeLogs | Where-Object { -not ($logToExclude -and [object]::ReferenceEquals($_, $logToExclude)) }

    # Find overlapping logs on the same day
    $overlappingLogs = $logsToCheck | Where-Object {
        $_.StartTime -and $_.EndTime -and
        ([datetime]$_.StartTime).Date -eq $newStartTime.Date -and
        ($newStartTime -lt ([datetime]$_.EndTime) -and $newEndTime -gt ([datetime]$_.StartTime))
    }

    if ($overlappingLogs.Count -gt 0) {
        if ($script:Settings.TimeLogOverlapBehavior -eq "Error") {
            [System.Windows.Forms.MessageBox]::Show("指定された時間帯は既存の記録と重複しています。", "重複エラー", "OK", "Error")
            return $false
        }
        elseif ($script:Settings.TimeLogOverlapBehavior -eq "Overwrite") {
            $survivingLogs = [System.Collections.ArrayList]::new()
            # Keep all logs that aren't being modified
            $logsToCheck | Where-Object { $_ -notin $overlappingLogs } | ForEach-Object { $survivingLogs.Add($_) | Out-Null }

            foreach ($log in $overlappingLogs) {
                $existingStartTime = [datetime]$log.StartTime
                $existingEndTime = [datetime]$log.EndTime

                # Case 1: Existing log has a part before the new log starts.
                if ($existingStartTime -lt $newStartTime) {
                    $survivingPortion = $log.PSObject.Copy()
                    $survivingPortion.EndTime = $newStartTime.ToString("o")
                    if (([datetime]$survivingPortion.StartTime) -lt ([datetime]$survivingPortion.EndTime)) {
                         $survivingLogs.Add($survivingPortion) | Out-Null
                    }
                }
                # Case 2: Existing log has a part after the new log ends.
                if ($existingEndTime -gt $newEndTime) {
                    $survivingPortion = $log.PSObject.Copy()
                    $survivingPortion.StartTime = $newEndTime.ToString("o")
                     if (([datetime]$survivingPortion.StartTime) -lt ([datetime]$survivingPortion.EndTime)) {
                         $survivingLogs.Add($survivingPortion) | Out-Null
                    }
                }
                # The part of the existing log that was overlapped is implicitly deleted.
            }
            # Add the new log itself.
            $survivingLogs.Add($newLog) | Out-Null
            
            $script:AllTimeLogs = $survivingLogs.ToArray()
            return $true
        }
    } else {
        # No overlap, just add the new log
        $tempLogs = [System.Collections.ArrayList]::new()
        $tempLogs.AddRange($logsToCheck)
        $tempLogs.Add($newLog) | Out-Null
        $script:AllTimeLogs = $tempLogs.ToArray()
        return $true
    }

    return $false # Should not be reached
}



# 旧関数 (後方互換性のため残すが、新しい関数に置き換えていく)
function Write-JsonFile {
    param([string]$filePath, $dataObject)
    try {
        $dataObject | ConvertTo-Json -Depth 5 | Set-Content -Path $filePath -Encoding UTF8 -Force
    } catch {
        [System.Windows.Forms.MessageBox]::Show("JSON書き込みエラー ($filePath): $($_.Exception.Message)", "エラー", "OK", "Error")
    }
}

function Read-JsonFile {
    param([string]$filePath)
    if (-not (Test-Path $filePath)) { return [PSCustomObject]@{} }
    try {
        $content = Get-Content -Path $filePath -Raw -Encoding UTF8
        if ([string]::IsNullOrWhiteSpace($content)) { return [PSCustomObject]@{} }
        return $content | ConvertFrom-Json
    } catch {
        [System.Windows.Forms.MessageBox]::Show("JSON読み込みエラー ($filePath): $($_.Exception.Message)", "エラー", "OK", "Warning"); return [PSCustomObject]@{}
    }
}
function Write-TasksToCsv {
    param([string]$filePath, [array]$data)
    if ($null -eq $data) {
        return
    }
    try {
        # CSVに出力するプロパティの順序を定義
        $propertyOrder = @(
            "ID", "ProjectID", "タスク", "進捗度", "優先度", "期日",
            "カテゴリ", "サブカテゴリ", "通知設定", "保存日付", "完了日",
            "TrackedTimeSeconds", "WorkFiles"
        )

        $exportData = foreach ($task in $data) {
            $taskForCsv = New-Object PSCustomObject

            # 定義された順序でプロパティを追加
            foreach ($prop in $propertyOrder) {
                $value = if ($task.PSObject.Properties.Name -contains $prop) { $task.$prop } else { "" }
                $taskForCsv | Add-Member -MemberType NoteProperty -Name $prop -Value $value
            }

            if ($taskForCsv.WorkFiles -is [array] -and $taskForCsv.WorkFiles.Count -gt 0) {
                $taskForCsv.WorkFiles = $taskForCsv.WorkFiles | ConvertTo-Json -Compress -Depth 5
            } else {
                $taskForCsv.WorkFiles = ""
            }
            $taskForCsv
        }

        $exportData | Export-Csv -Path $filePath -NoTypeInformation -Encoding UTF8 -Force
    } catch {
        [System.Windows.Forms.MessageBox]::Show("CSV書き込みエラー: $($_.Exception.Message)", "エラー", "OK", "Error")
    }
}
function Read-TasksFromCsv {
    param([string]$filePath)
    if (-not (Test-Path $filePath) -or (Get-Item $filePath).Length -eq 0) { return @() }
    
    try {
        # 読み込みロジックの強化: BOM付きUTF-8やロック競合に強くするため、ReadAllTextを使用
        $content = $null
        try {
            $content = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::UTF8)
        } catch {
            $content = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::Default)
        }

        if ([string]::IsNullOrWhiteSpace($content)) { return @() }
        $content = $content.Trim([char]0xfeff)
        if ($content.StartsWith("ï»¿")) { $content = $content.Substring(3) }
        $tasks = @($content | ConvertFrom-Csv | Where-Object { ($_.PSObject.Properties | ForEach-Object { $_.Value }) -join '' -ne '' })

        foreach ($task in $tasks) {
            if ($null -eq $task) {
                Write-Warning "CSVファイル内に空の行が存在する可能性があります。スキップします。"
                continue
            }

            # 必須プロパティと新しいプロパティを保証する
            $requiredProperties = @{
                "ID"                   = { [guid]::NewGuid().ToString() };
                "ProjectID"            = $null;
                "期日"                 = "";
                "優先度"               = "中";
                "タスク"               = "";
                "進捗度"               = "未実施";
                "通知設定"             = "全体設定に従う";
                "カテゴリ"             = "";
                "サブカテゴリ"         = "";
                "保存日付"             = { (Get-Date).ToString("yyyy-MM-dd") };
                "完了日"               = "";
                "TrackedTimeSeconds"   = 0;
                "WorkFiles"            = "" # デフォルトを空文字列に変更
            }
            foreach ($propEntry in $requiredProperties.GetEnumerator()) {
                if (-not $task.PSObject.Properties.Name.Contains($propEntry.Name)) {
                    $defaultValue = if ($propEntry.Value -is [scriptblock]) { & $propEntry.Value } else { $propEntry.Value }
                    $task | Add-Member -MemberType NoteProperty -Name $propEntry.Name -Value $defaultValue
                }
            }

            # WorkFiles列をJSONからオブジェクトに変換し、新しい形式に統一する
            if ($task.PSObject.Properties['WorkFiles'] -and $task.WorkFiles -is [string] -and -not [string]::IsNullOrWhiteSpace($task.WorkFiles)) {
                try {
                    $workFilesRaw = $task.WorkFiles | ConvertFrom-Json -ErrorAction Stop
                    $convertedWorkFiles = @()
                    if ($workFilesRaw) {
                        foreach ($file in @($workFilesRaw)) { # 配列でない場合も考慮
                            $convertedWorkFiles += Convert-ToStandardWorkFile -workFileObject $file
                        }
                    }
                    $task.WorkFiles = $convertedWorkFiles
                } catch {
                    Write-Warning "タスク $($task.ID) のWorkFilesの解析に失敗しました。空のリストで上書きします。"
                    $task.WorkFiles = @()
                }
            } else {
                $task.WorkFiles = @()
            }
        }
        return $tasks

    } catch {
        [System.Windows.Forms.MessageBox]::Show("CSV読み込みエラー: $($_.Exception.Message)", "エラー", "OK", "Warning")
        return $null
    }
}

# --- 起動時処理 ---
function Start-AutomaticBackup {
    try {
        $today = (Get-Date).Date
        $intervalDays = [int]$script:Settings.BackupIntervalDays

        $shouldBackup = $false
        if ([string]::IsNullOrWhiteSpace($script:Settings.LastBackupDate)) {
            $shouldBackup = $true
        } else {
            try {
                $lastBackupDate = [datetime]::ParseExact($script:Settings.LastBackupDate, "yyyy-MM-dd", $null).Date
                if (($today - $lastBackupDate).Days -ge $intervalDays) {
                    $shouldBackup = $true
                }
            } catch {
                # LastBackupDate が不正な形式の場合、バックアップを実行する
                Write-Warning "設定ファイルの日付形式が不正なため、バックアップを実行します。"
                $shouldBackup = $true
            }
        }

        if (-not $shouldBackup) { return } # バックアップの必要なし

        # --- バックアップ実行処理 ---
        $backupRoot = $script:BackupsFolder
        if (-not [string]::IsNullOrEmpty($script:Settings.BackupPath)) {
            $backupRoot = $script:Settings.BackupPath
        }
        
        # フォルダ作成とフォールバック
        if (-not (Test-Path $backupRoot)) {
            try {
                New-Item -Path $backupRoot -ItemType Directory -ErrorAction Stop | Out-Null
            } catch {
                Write-Warning "指定されたバックアップ先($backupRoot)を作成できませんでした。デフォルトの場所を使用します。"
                $backupRoot = $script:BackupsFolder
                if (-not (Test-Path $backupRoot)) { New-Item -Path $backupRoot -ItemType Directory | Out-Null }
            }
        }

        $backupTimestamp = (Get-Date).ToString("yyyy-MM-dd")
        $backupSubFolder = Join-Path -Path $backupRoot -ChildPath $backupTimestamp
        if (-not (Test-Path $backupSubFolder)) {
            New-Item -Path $backupSubFolder -ItemType Directory | Out-Null
        }

        # バックアップ対象のファイルを明示的に指定（手動バックアップとロジックを統一）
        $filesToBackup = @(
            $script:TasksFile,
            $script:ProjectsFile,
            $script:CategoriesFile,
            $script:TemplatesFile,
            $script:SettingsFile,
            $script:EventsFile,
            $script:TimeLogsFile,
            $script:ArchivedTasksFile,
            $script:ArchivedProjectsFile,
            $script:StatusLogsFile
        )

        # 各ファイルをバックアップ先にコピー
        foreach ($sourcePath in $filesToBackup) {
            if (Test-Path $sourcePath) { Copy-Item -Path $sourcePath -Destination $backupSubFolder -Force }
        }

        # --- 古いバックアップの削除 ---
        $retentionDays = [int]$script:Settings.BackupRetentionDays
        if ($retentionDays -gt 0) {
             Get-ChildItem -Path $backupRoot -Directory | Where-Object { $_.CreationTime -lt (Get-Date).AddDays(-$retentionDays) } | Remove-Item -Recurse -Force
        }

        # --- 最終バックアップ日の更新 ---
        $script:Settings.LastBackupDate = $today.ToString("yyyy-MM-dd")
        Save-DataFile -filePath $script:SettingsFile -dataObject $script:Settings

    } catch {
        [System.Windows.Forms.MessageBox]::Show("自動バックアップ処理中にエラーが発生しました: `n$($_.Exception.Message)", "バックアップエラー", "OK", "Error")
    }
}

function Compress-OldArchives {
    try {
        # 設定から値を取得、なければデフォルト値90日を使用
        $compressionDays = if ($script:Settings.ArchiveCompressionDays) { [int]$script:Settings.ArchiveCompressionDays } else { 90 }
        if ($compressionDays -le 0) { return } # 0以下なら何もしない

        $archiveCutoffDate = (Get-Date).AddDays(-$compressionDays)
        
        # バックアップフォルダ内の日付形式のディレクトリを取得
        $backupDirs = Get-ChildItem -Path $script:BackupsFolder -Directory | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}$' }

        foreach ($dir in $backupDirs) {
            try {
                $dirDate = [datetime]::ParseExact($dir.Name, 'yyyy-MM-dd', $null)

                if ($dirDate -lt $archiveCutoffDate) {
                    $zipFilePath = Join-Path -Path $script:BackupsFolder -ChildPath ($dir.Name + ".zip")
                    
                    # 既に圧縮済みの場合はスキップ
                    if (Test-Path $zipFilePath) { continue }

                    Write-Host "Archiving backup folder: $($dir.FullName)"
                    Compress-Archive -Path $dir.FullName -DestinationPath $zipFilePath -CompressionLevel Optimal
                    
                    # 圧縮が成功したら元のフォルダを削除
                    if (Test-Path $zipFilePath) {
                        Remove-Item -Path $dir.FullName -Recurse -Force
                        Write-Host "Successfully archived and removed: $($dir.FullName)"
                    }
                }
            } catch {
                # 日付変換エラーなどは無視して次に進む
                Write-Warning "Could not process directory $($dir.Name). Error: $($_.Exception.Message)"
            }
        }
    } catch {
        # この関数全体のエラーは警告として表示するのみで、アプリの起動を妨げない
        Write-Warning "An error occurred in Compress-OldArchives: $($_.Exception.Message)"
    }
}

function Invoke-ManualBackup {
    try {
        $backupRoot = Join-Path -Path $script:AppRoot -ChildPath "backup"
        if (-not [string]::IsNullOrEmpty($script:Settings.BackupPath)) {
            $backupRoot = $script:Settings.BackupPath
        }

        # フォルダ作成とフォールバック
        if (-not (Test-Path $backupRoot)) {
            try {
                New-Item -Path $backupRoot -ItemType Directory -ErrorAction Stop | Out-Null
            } catch {
                Write-Warning "指定されたバックアップ先($backupRoot)を作成できませんでした。デフォルトの場所を使用します。"
                $backupRoot = Join-Path -Path $script:AppRoot -ChildPath "backup"
                if (-not (Test-Path $backupRoot)) { New-Item -Path $backupRoot -ItemType Directory | Out-Null }
            }
        }
        
        $timestamp = (Get-Date).ToString("yyyy-MM-dd_HH-mm-ss")
        $backupDestination = Join-Path -Path $backupRoot -ChildPath $timestamp
        New-Item -Path $backupDestination -ItemType Directory | Out-Null

        $filesToBackup = @(
            $script:TasksFile,
            $script:ProjectsFile,
            $script:CategoriesFile,
            $script:TemplatesFile,
            $script:SettingsFile,
            $script:EventsFile,
            $script:TimeLogsFile,
            $script:ArchivedTasksFile,
            $script:ArchivedProjectsFile,
            $script:StatusLogsFile
        )

        foreach ($sourcePath in $filesToBackup) {
            if (Test-Path $sourcePath) {
                Copy-Item -Path $sourcePath -Destination $backupDestination -Force
            }
        }

        [System.Windows.Forms.MessageBox]::Show("バックアップが完了しました。", "成功", "OK", "Information")

    } catch {
        [System.Windows.Forms.MessageBox]::Show("手動バックアップ中にエラーが発生しました: `n$($_.Exception.Message)", "バックアップエラー", "OK", "Error")
    }
}

function Invoke-RestoreFromBackup {
    # 1. Get the list of backup folders
    $backupRoot = Join-Path -Path $script:AppRoot -ChildPath "backup"
    if (-not (Test-Path $backupRoot)) {
        New-Item -Path $backupRoot -ItemType Directory | Out-Null
    }

    # 2. Create a form to let the user select a backup
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "バックアップの管理"
    $form.Size = New-Object System.Drawing.Size(400, 350) # 高さを増やす
    $form.StartPosition = 'CenterParent'

    $label = New-Object System.Windows.Forms.Label
    $label.Text = "復元したいバックアップを選択してください:"
    $label.Location = New-Object System.Drawing.Point(10, 10)
    $label.AutoSize = $true
    $form.Controls.Add($label)

    $listBox = New-Object System.Windows.Forms.ListBox
    $listBox.Location = New-Object System.Drawing.Point(10, 35)
    $listBox.Size = New-Object System.Drawing.Size(360, 210) # 高さを増やす
    $form.Controls.Add($listBox)

    # バックアップリストを読み込む関数
    $loadBackupList = {
        $listBox.Items.Clear()
        $backupDirs = Get-ChildItem -Path $backupRoot -Directory | Sort-Object Name -Descending
        if ($backupDirs) {
            foreach ($dir in $backupDirs) {
                $listBox.Items.Add($dir.Name) | Out-Null
            }
            if ($listBox.Items.Count -gt 0) {
                $listBox.SelectedIndex = 0
            }
        } else {
            [System.Windows.Forms.MessageBox]::Show("復元可能なバックアップが見つかりません。", "情報", "OK", "Information")
        }
    }

    # 初回読み込み
    & $loadBackupList

    # --- ボタンの定義 ---
    $btnManualBackup = New-Object System.Windows.Forms.Button
    $btnManualBackup.Text = "手動バックアップ"
    $btnManualBackup.Location = New-Object System.Drawing.Point(10, 260)
    $btnManualBackup.Size = New-Object System.Drawing.Size(120, 25) # 幅を調整
    $form.Controls.Add($btnManualBackup)

    $btnOK = New-Object System.Windows.Forms.Button
    $btnOK.Text = "復元"
    $btnOK.Location = New-Object System.Drawing.Point(205, 260) # 位置を調整
    $form.Controls.Add($btnOK)

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = "キャンセル"
    $btnCancel.Location = New-Object System.Drawing.Point(295, 260) # 位置を調整
    $form.Controls.Add($btnCancel)
    
    $form.AcceptButton = $btnOK
    $form.CancelButton = $btnCancel

    # --- イベントハンドラ ---
    $btnManualBackup.Add_Click({
        Invoke-ManualBackup
        # バックアップリストを再読み込みして新しいバックアップを表示
        & $loadBackupList
    })

    $btnOK.Add_Click({
        if ($listBox.SelectedItem) {
            $form.Tag = $listBox.SelectedItem
            $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        } else {
            [System.Windows.Forms.MessageBox]::Show("バックアップを選択してください。", "エラー", "OK", "Warning")
        }
    })
    $btnCancel.Add_Click({ $form.Close() })

    # Show the selection form
    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    if ($form.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return $false
    }

    $selectedBackup = $form.Tag
    $backupPath = Join-Path -Path $backupRoot -ChildPath $selectedBackup

    # 3. Confirmation
    $confirmResult = [System.Windows.Forms.MessageBox]::Show(
        "バックアップ '$selectedBackup' から復元します。`n`n現在のデータは上書きされます。よろしいですか？`n(この操作は元に戻せません)",
        "復元の確認",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning
    )

    if ($confirmResult -ne 'Yes') {
        return $false
    }

    # 4. Perform the restore
    try {
        $filesToRestore = @(
            $script:TasksFile,
            $script:ProjectsFile,
            $script:CategoriesFile,
            $script:TemplatesFile,
            $script:SettingsFile,
            $script:EventsFile,
            $script:TimeLogsFile,
            $script:ArchivedTasksFile,
            $script:ArchivedProjectsFile,
            $script:StatusLogsFile
        )
        
        foreach ($destinationPath in $filesToRestore) {
            $fileName = Split-Path -Path $destinationPath -Leaf
            $sourcePath = Join-Path -Path $backupPath -ChildPath $fileName
            if (Test-Path $sourcePath) {
                Copy-Item -Path $sourcePath -Destination $destinationPath -Force
                $restored = $true
            }
        }

        if ($restored) {
            [System.Windows.Forms.MessageBox]::Show("復元が完了しました。表示が更新されます。", "成功", "OK", "Information")
            return $true
        } else {
            [System.Windows.Forms.MessageBox]::Show("選択されたバックアップフォルダに復元対象のファイルが見つかりませんでした。", "エラー", "OK", "Error")
            return $false
        }
    } catch {
        [System.Windows.Forms.MessageBox]::Show("復元中にエラーが発生しました: `n$($_.Exception.Message)", "復元エラー", "OK", "Error")
        return $false
    }
}

function Format-PaddedString {
    param([string]$Text, [int]$TotalWidth)
    $text = if ($null -eq $Text) { "" } else { $Text }
    $bytes = [System.Text.Encoding]::GetEncoding('shift_jis').GetBytes($text)
    $visualWidth = $bytes.Length
    $paddingNeeded = $TotalWidth - $visualWidth
    if ($paddingNeeded -lt 0) {
        # 幅を超える場合は、超過分を考慮して末尾を切り詰める
        $cutBytes = New-Object byte[] $TotalWidth
        [array]::Copy($bytes, $cutBytes, $TotalWidth)
        return ([System.Text.Encoding]::GetEncoding('shift_jis').GetString($cutBytes)) + "..."
    }
    return $text + (' ' * $paddingNeeded)
}

# --- UI(画面)に関する関数 ---
function Show-SaveFeedbackAndClose {
    param(
        [System.Windows.Forms.Form]$FormToClose,
        [string]$Message
    )
    foreach($button in $FormToClose.Controls | Where-Object { $_ -is [System.Windows.Forms.Button] }) {
        $button.Enabled = $false
    }

    $feedbackLabel = New-Object System.Windows.Forms.Label
    $feedbackLabel.Text = $Message
    $feedbackLabel.Font = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Bold)
    $feedbackLabel.ForeColor = [System.Drawing.Color]::White
    $feedbackLabel.BackColor = [System.Drawing.Color]::FromArgb(180, [System.Drawing.Color]::Black)
    $feedbackLabel.AutoSize = $true
    $feedbackLabel.Padding = New-Object System.Windows.Forms.Padding(10)
    $FormToClose.Controls.Add($feedbackLabel)
    $xPos = ([int]$FormToClose.ClientSize.Width - [int]$feedbackLabel.Width) / 2
    $yPos = ([int]$FormToClose.ClientSize.Height - [int]$feedbackLabel.Height) / 2
    $feedbackLabel.Location = New-Object System.Drawing.Point($xPos, $yPos)
    $feedbackLabel.BringToFront()

    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 1200
    $FormToClose.Close()
}

function Show-MemoInputForm {
    param([string]$existingText = "")

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "メモ"
    $form.Width = 400
    $form.Height = 300
    $form.StartPosition = 'CenterParent'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false

    $textMemo = New-Object System.Windows.Forms.TextBox
    $textMemo.Multiline = $true
    $textMemo.ScrollBars = 'Vertical'
    $textMemo.Dock = 'Top'
    $textMemo.Height = 210
    $textMemo.Text = $existingText
    [void]$form.Controls.Add($textMemo)

    $btnOK = New-Object System.Windows.Forms.Button; $btnOK.Text = "OK"; $btnOK.Location = "110, 225"; $btnOK.DialogResult = [System.Windows.Forms.DialogResult]::OK; [void]$form.Controls.Add($btnOK)
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "キャンセル"; $btnCancel.Location = "200, 225"; $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; [void]$form.Controls.Add($btnCancel)

    $form.AcceptButton = $btnOK
    $form.CancelButton = $btnCancel
    $form.ActiveControl = $textMemo

    Set-Theme -form $form -IsDarkMode $script:isDarkMode

    if ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $textMemo.Text } else { return $null }
}

function Show-TaskInputForm {
    param ([psobject]$existingTask = $null, [string]$projectIDForNew = $null, $dateForNew = $null)
    $form = New-Object System.Windows.Forms.Form; $form.Width = 420; $form.Height = 480; $form.StartPosition = 'CenterScreen'
    if ($existingTask) { $form.Text = "タスクの編集" } else { $form.Text = "プロジェクト／タスクの新規追加" }

    # --- プロジェクト ---
    $labelProject=New-Object System.Windows.Forms.Label;$labelProject.Text="プロジェクト：";$labelProject.Location="10, 20";$form.Controls.Add($labelProject)
    $comboProject=New-Object System.Windows.Forms.ComboBox;$comboProject.Location="10, 40";$comboProject.Size="380, 25";$comboProject.DropDownStyle = "DropDown";$form.Controls.Add($comboProject) # 変更: DropDownList から DropDown へ
    $comboProject.DisplayMember = 'ProjectName'
    $comboProject.ValueMember = 'ProjectID'
    $script:Projects | Sort-Object ProjectName | ForEach-Object { $comboProject.Items.Add($_) } | Out-Null

    # --- 期日 ---
    $labelDue=New-Object System.Windows.Forms.Label;$labelDue.Text="期日：";$labelDue.Location="10, 80";$form.Controls.Add($labelDue)
    $datePicker=New-Object System.Windows.Forms.DateTimePicker;$datePicker.ShowCheckBox=$true;$datePicker.Location="10, 100";$datePicker.Width=180;$form.Controls.Add($datePicker)
    $datePicker.Format = 'Short'
    
    # --- 優先度 ---
    $labelPriority=New-Object System.Windows.Forms.Label;$labelPriority.Text="優先度：";$labelPriority.Location="220, 80";$form.Controls.Add($labelPriority)
    $comboPriority=New-Object System.Windows.Forms.ComboBox;$comboPriority.DropDownStyle="DropDownList";$comboPriority.Items.AddRange(@("高","中","低"));$comboPriority.Location="220, 100";$comboPriority.Width=100;$form.Controls.Add($comboPriority)
    
    # --- 進捗度 ---
    $labelStatus=New-Object System.Windows.Forms.Label;$labelStatus.Text="進捗度：";$labelStatus.Location="10, 140";$form.Controls.Add($labelStatus)
    $comboStatus=New-Object System.Windows.Forms.ComboBox;$comboStatus.DropDownStyle="DropDownList";$comboStatus.Items.AddRange($script:TaskStatuses);$comboStatus.Location="10, 160";$comboStatus.Width=100;$form.Controls.Add($comboStatus)
    
    # --- 通知設定 ---
    $notifyOptions = @("全体設定に従う","通知しない","当日","1日前","前の営業日","3日前","1週間前")
    $labelNotify=New-Object System.Windows.Forms.Label;$labelNotify.Text="通知設定：";$labelNotify.Location="220, 140";$form.Controls.Add($labelNotify)
    $comboNotify=New-Object System.Windows.Forms.ComboBox;$comboNotify.DropDownStyle="DropDownList";$comboNotify.Items.AddRange($notifyOptions);$comboNotify.Location="220, 160";$comboNotify.Width=150;$form.Controls.Add($comboNotify)

    # --- カテゴリ ---
    $labelCategory=New-Object System.Windows.Forms.Label;$labelCategory.Text="カテゴリ：";$labelCategory.Location="10, 200";$form.Controls.Add($labelCategory)
    $comboCategory=New-Object System.Windows.Forms.ComboBox;$comboCategory.Location="10, 220";$comboCategory.Size="180, 25";$comboCategory.DropDownStyle = "DropDownList";$form.Controls.Add($comboCategory)
    
    # --- サブカテゴリ ---
    $labelSubCategory=New-Object System.Windows.Forms.Label;$labelSubCategory.Text="サブカテゴリ：";$labelSubCategory.Location="220, 200";$form.Controls.Add($labelSubCategory)
    $comboSubCategory=New-Object System.Windows.Forms.ComboBox;$comboSubCategory.Location="220, 220";$comboSubCategory.Size="180, 25";$comboSubCategory.DropDownStyle = "DropDownList";$form.Controls.Add($comboSubCategory)

    # --- タスク内容 ---
    $labelTask=New-Object System.Windows.Forms.Label;$labelTask.Text="タスク内容：";$labelTask.Location="10, 260";$form.Controls.Add($labelTask)
    $textTask=New-Object System.Windows.Forms.TextBox;$textTask.Multiline=$true;$textTask.Location="10, 280";$textTask.Size="380, 80";$form.Controls.Add($textTask)
    
    # --- ボタン ---
    $buttonSave=New-Object System.Windows.Forms.Button;$buttonSave.Text="保存";$buttonSave.Location="120, 380";$form.Controls.Add($buttonSave)
    $buttonCancel=New-Object System.Windows.Forms.Button;$buttonCancel.Text="キャンセル";$buttonCancel.Location="210, 380";$form.Controls.Add($buttonCancel)

    # --- イベントハンドラとデータロード ---
    $updateSubCategories = {
        $comboSubCategory.Items.Clear()
        $selectedCategory = $comboCategory.SelectedItem
        if ($null -eq $selectedCategory) { return } # Guard against null key
        if ($script:Categories.$selectedCategory) {
            $subCategories = $script:Categories.$selectedCategory.PSObject.Properties.Name
            if ($subCategories) {
                $comboSubCategory.Items.AddRange($subCategories)
            }
        }
    }
    $comboCategory.Add_SelectedIndexChanged($updateSubCategories)

    $comboCategory.Items.AddRange($script:Categories.PSObject.Properties.Name)

    if ($existingTask) {
        $projectToSelect = $comboProject.Items | Where-Object { $_.ProjectID -eq $existingTask.ProjectID } | Select-Object -First 1
        if ($projectToSelect) { $comboProject.SelectedItem = $projectToSelect }

        if ($existingTask.期日) { [void]($datePicker.Checked = $true); try { $datePicker.Value = [datetime]$existingTask.期日 } catch {} } else { [void]($datePicker.Checked = $false) }
        $comboPriority.SelectedItem = $existingTask.優先度
        $comboStatus.SelectedItem = $existingTask.進捗度
        $textTask.Text = $existingTask.タスク
        $comboNotify.SelectedItem = $existingTask.通知設定
        if ($existingTask.カテゴリ -and $comboCategory.Items.Contains($existingTask.カテゴリ)) {
            $comboCategory.SelectedItem = $existingTask.カテゴリ
            & $updateSubCategories
            if ($existingTask.サブカテゴリ -and $comboSubCategory.Items.Contains($existingTask.サブカテゴリ)) {
                $comboSubCategory.SelectedItem = $existingTask.サブカテゴリ
            }
        }
    } else {
        if ($projectIDForNew) {
            $projectToSelect = $comboProject.Items | Where-Object { $_.ProjectID -eq $projectIDForNew } | Select-Object -First 1
            if ($projectToSelect) { $comboProject.SelectedItem = $projectToSelect }
        }
        if ($dateForNew -is [datetime]) { $datePicker.Value = $dateForNew; $datePicker.Checked = $true } else { [void]($datePicker.Checked = $true); $datePicker.Value = (Get-Date).AddDays(1) }
        $comboPriority.SelectedIndex = 1
        $comboStatus.SelectedIndex = 0
        $comboNotify.SelectedIndex = 0
    }
    
    $form.ActiveControl = $textTask; $form.AcceptButton = $buttonSave; $form.CancelButton = $buttonCancel
    
    $buttonSave.Add_Click({
        $projectName = $comboProject.Text.Trim()
        if ([string]::IsNullOrWhiteSpace($projectName)) { [System.Windows.Forms.MessageBox]::Show("プロジェクトは必須です。", "入力エラー", "OK", "Error"); return }
        if ([string]::IsNullOrWhiteSpace($textTask.Text)) { [System.Windows.Forms.MessageBox]::Show("タスク内容は必須です。", "入力エラー", "OK", "Error"); return }
        if ([string]::IsNullOrWhiteSpace($comboCategory.SelectedItem)) { [System.Windows.Forms.MessageBox]::Show("カテゴリは必須です。", "入力エラー", "OK", "Error"); return }

        $selectedProject = $script:Projects | Where-Object { $_.ProjectName -eq $projectName } | Select-Object -First 1

        if (-not $selectedProject) {
            $newProject = [PSCustomObject]@{ ProjectID = [guid]::NewGuid().ToString(); ProjectName = $projectName; ProjectDueDate = $null; WorkFiles = @(); Notification = "全体設定に従う"; ProjectColor = "#D3D3D3"; AutoArchiveTasks = $true }
            $script:Projects += $newProject
            Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
            $selectedProject = $newProject
        }
        
        $dueDate = if ($datePicker.Checked) { $datePicker.Value.ToString("yyyy-MM-dd") } else { "" }
        $newStatus = $comboStatus.SelectedItem
        $completionDate = if ($existingTask) { $existingTask.完了日 } else { "" }

        if ($newStatus -eq '完了済み' -and [string]::IsNullOrEmpty($completionDate)) { $completionDate = (Get-Date).ToString("yyyy-MM-dd") } 
        elseif ($newStatus -ne '完了済み' -and $existingTask -and $existingTask.進捗度 -eq '完了済み') { $completionDate = "" }

        $form.Tag = [PSCustomObject]@{ 
            "ID" = if ($existingTask -and $existingTask.ID) { $existingTask.ID } else { [guid]::NewGuid().ToString() }
            "ProjectID" = $selectedProject.ProjectID
            "期日" = $dueDate
            "優先度" = $comboPriority.SelectedItem
            "タスク" = $textTask.Text.Trim()
            "進捗度" = $newStatus
            "通知設定" = $comboNotify.SelectedItem
            "カテゴリ" = $comboCategory.SelectedItem
            "サブカテゴリ" = $comboSubCategory.SelectedItem
            "保存日付" = if ($existingTask -and $existingTask.保存日付) { $existingTask.保存日付 } else { (Get-Date).ToString("yyyy-MM-dd") }
            "完了日" = $completionDate
            "TrackedTimeSeconds" = if ($existingTask) { $existingTask.TrackedTimeSeconds } else { 0 }
            "WorkFiles" = if ($existingTask) { @($existingTask.WorkFiles) } else { @() }
        }
        
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        Show-SaveFeedbackAndClose -FormToClose $form -Message "タスクを保存しました"
    })
    $buttonCancel.Add_Click({ $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Close() })

    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    if ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $form.Tag } else { return $null }
}

function Show-ProjectSelectionForm {
    param([array]$projects)
    $form = New-Object System.Windows.Forms.Form; $form.Text = "プロジェクトを選択"; $form.Width = 350; $form.Height = 150; $form.StartPosition = 'CenterParent'
    $label = New-Object System.Windows.Forms.Label; $label.Text = "ファイルを追加するプロジェクトを選択してください:"; $label.Location = "15, 15"; $label.AutoSize = $true; $form.Controls.Add($label)
    $combo = New-Object System.Windows.Forms.ComboBox; $combo.Location = "15, 40"; $combo.Size = "300, 25"; $combo.DropDownStyle = "DropDownList"; $form.Controls.Add($combo)
    
    # データバインディングを使わず、手動でプロジェクト名を追加
    foreach ($project in $projects) {
        $combo.Items.Add($project.ProjectName) | Out-Null
    }
    if ($combo.Items.Count -gt 0) {
        $combo.SelectedIndex = 0
    }

    $btnOK = New-Object System.Windows.Forms.Button; $btnOK.Text = "OK"; $btnOK.Location = "80, 80"; $form.Controls.Add($btnOK)
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "キャンセル"; $btnCancel.Location = "170, 80"; $form.Controls.Add($btnCancel)
    $form.AcceptButton = $btnOK; $form.CancelButton = $btnCancel
    
    $btnOK.Add_Click({
        if ($combo.SelectedItem) {
            # 選択された名前から完全なプロジェクトオブジェクトを検索
            $selectedProjectName = $combo.SelectedItem
            $selectedProject = $projects | Where-Object { $_.ProjectName -eq $selectedProjectName } | Select-Object -First 1
            $form.Tag = $selectedProject
        }
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK; $form.Close()
    })
    $btnCancel.Add_Click({ $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Close() })
    
    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    if ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $form.Tag }
    return $null
}

function Invoke-ProjectAutoArchiving {
    $autoArchiveDays = $script:Settings.AutoArchiveProjectsDays
    if ($null -eq $autoArchiveDays) {
        return
    }

    $today = (Get-Date).Date

    foreach ($project in $script:Projects) {
        $tasksInProject = $script:AllTasks | Where-Object { $_.ProjectID -eq $project.ProjectID }
        if ($tasksInProject.Count -eq 0) {
            continue
        }

        $allTasksCompleted = $true
        $latestCompletionDate = [datetime]::MinValue

        foreach ($task in $tasksInProject) {
            if ($task.進捗度 -ne '完了済み') {
                $allTasksCompleted = $false
                break
            }
            if (-not [string]::IsNullOrEmpty($task.完了日)) {
                try {
                    $completionDate = [datetime]$task.完了日
                    if ($completionDate -gt $latestCompletionDate) {
                        $latestCompletionDate = $completionDate
                    }
                } catch {}
            }
        }

        if ($allTasksCompleted -and $latestCompletionDate -ne [datetime]::MinValue) {
            if ($autoArchiveDays -eq 0 -or ($today - $latestCompletionDate).Days -ge $autoArchiveDays) {
                Move-ProjectToArchive -projectToArchive $project
            }
        }
    }
}

function Add-GroupBox {
    param($targetPanel, $text, $height, [ref]$currentY, $width)
    $gb = New-Object System.Windows.Forms.GroupBox
    $gb.Text = $text
    $gb.Location = New-Object System.Drawing.Point(10, $currentY.Value)
    $gb.Size = New-Object System.Drawing.Size($width, $height)
    $targetPanel.Controls.Add($gb)
    $currentY.Value += $height + 15
    return $gb
}

function Show-SettingsForm {
    param($parentForm)
     # --- フォーム作成 ---
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "全体設定"
    $form.Size = New-Object System.Drawing.Size(550, 700)
    $form.StartPosition = 'CenterParent'
    $form.MinimizeBox = $false
    $form.MaximizeBox = $false
    $form.TopMost = $true

    # --- メインパネル (スクロール可能) ---
    $mainPanel = New-Object System.Windows.Forms.Panel
    $mainPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
    $mainPanel.AutoScroll = $true
    $mainPanel.Padding = New-Object System.Windows.Forms.Padding(10)
    $form.Controls.Add($mainPanel)

    # --- 下部パネル (保存/キャンセル) ---
    $bottomPanel = New-Object System.Windows.Forms.Panel
    $bottomPanel.Dock = [System.Windows.Forms.DockStyle]::Bottom
    $bottomPanel.Height = 50
    $form.Controls.Add($bottomPanel)
    $bottomPanel.BringToFront()

    $btnSave = New-Object System.Windows.Forms.Button
    $btnSave.Text = "保存"
    $btnSave.Location = New-Object System.Drawing.Point(330, 10)
    $btnSave.Size = New-Object System.Drawing.Size(90, 30)
    $btnSave.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $bottomPanel.Controls.Add($btnSave)

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = "キャンセル"
    $btnCancel.Location = New-Object System.Drawing.Point(430, 10)
    $btnCancel.Size = New-Object System.Drawing.Size(90, 30)
    $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $bottomPanel.Controls.Add($btnCancel)

    $form.AcceptButton = $btnSave
    $form.CancelButton = $btnCancel

    # --- 設定値のロード ---
    $s = $script:Settings

    # --- ヘルパー関数: グループボックス追加 ---
    $script:currentY = 10
    function Add-GroupBox {
        param($text, $height)
        $gb = New-Object System.Windows.Forms.GroupBox
        $gb.Text = $text
        $gb.Location = New-Object System.Drawing.Point(10, $script:currentY)
        $gb.Size = New-Object System.Drawing.Size(500, $height)
        $mainPanel.Controls.Add($gb)
        $script:currentY += $height + 15
        return $gb
    }

    # =================================================================
    # ① GB: [アプリケーション設定]
    # =================================================================
    $gbApp = Add-GroupBox "アプリケーション設定" 260
    
    $chkRunAtStartup = New-Object System.Windows.Forms.CheckBox
    $chkRunAtStartup.Text = "Windows起動時に実行"
    $chkRunAtStartup.Location = New-Object System.Drawing.Point(20, 25)
    $chkRunAtStartup.AutoSize = $true
    $chkRunAtStartup.Checked = [bool]$s.RunAtStartup
    $gbApp.Controls.Add($chkRunAtStartup)

    $chkMinimizeToTray = New-Object System.Windows.Forms.CheckBox
    $chkMinimizeToTray.Text = "閉じるボタンで最小化 (タスクトレイへ)"
    $chkMinimizeToTray.Location = New-Object System.Drawing.Point(20, 50)
    $chkMinimizeToTray.AutoSize = $true
    $chkMinimizeToTray.Checked = [bool]$s.MinimizeToTray
    $gbApp.Controls.Add($chkMinimizeToTray)

    $chkAlwaysOnTop = New-Object System.Windows.Forms.CheckBox
    $chkAlwaysOnTop.Text = "ウィンドウを常に手前に表示"
    $chkAlwaysOnTop.Location = New-Object System.Drawing.Point(20, 75)
    $chkAlwaysOnTop.AutoSize = $true
    $chkAlwaysOnTop.Checked = [bool]$s.AlwaysOnTop
    $gbApp.Controls.Add($chkAlwaysOnTop)

    $chkEnableSoundEffects = New-Object System.Windows.Forms.CheckBox
    $chkEnableSoundEffects.Text = "完了時に効果音を鳴らす"
    $chkEnableSoundEffects.Location = New-Object System.Drawing.Point(20, 100)
    $chkEnableSoundEffects.AutoSize = $true
    $chkEnableSoundEffects.Checked = [bool]$s.EnableSoundEffects
    $gbApp.Controls.Add($chkEnableSoundEffects)

    $lblPasscode = New-Object System.Windows.Forms.Label
    $lblPasscode.Text = "起動パスコード (空欄で無効):"
    $lblPasscode.Location = New-Object System.Drawing.Point(20, 130)
    $lblPasscode.AutoSize = $true
    $gbApp.Controls.Add($lblPasscode)

    $txtPasscode = New-Object System.Windows.Forms.TextBox
    $txtPasscode.Location = New-Object System.Drawing.Point(200, 127)
    $txtPasscode.Size = New-Object System.Drawing.Size(150, 23)
    $txtPasscode.Text = $s.Passcode
    $gbApp.Controls.Add($txtPasscode)

    $lblOpacity = New-Object System.Windows.Forms.Label
    $lblOpacity.Text = "ウィンドウ透明度:"
    $lblOpacity.Location = New-Object System.Drawing.Point(20, 160)
    $lblOpacity.AutoSize = $true
    $gbApp.Controls.Add($lblOpacity)

    $trackOpacity = New-Object System.Windows.Forms.TrackBar
    $trackOpacity.Location = New-Object System.Drawing.Point(190, 155)
    $trackOpacity.Size = New-Object System.Drawing.Size(200, 45)
    $trackOpacity.Minimum = 5
    $trackOpacity.Maximum = 10
    $valOpacity = if ($s.WindowOpacity) { [int]([double]$s.WindowOpacity * 10) } else { 10 }
    $trackOpacity.Value = $valOpacity
    $gbApp.Controls.Add($trackOpacity)

    $lblIdle = New-Object System.Windows.Forms.Label
    $lblIdle.Text = "非アクティブ判定 (分):"
    $lblIdle.Location = New-Object System.Drawing.Point(20, 195)
    $lblIdle.AutoSize = $true
    $gbApp.Controls.Add($lblIdle)

    $numIdle = New-Object System.Windows.Forms.NumericUpDown
    $numIdle.Location = New-Object System.Drawing.Point(150, 192)
    $numIdle.Width = 60
    $numIdle.Minimum = 1; $numIdle.Maximum = 120
    $numIdle.Value = if ($s.IdleTimeoutMinutes) { [int]$s.IdleTimeoutMinutes } else { 5 }
    $gbApp.Controls.Add($numIdle)

    $lblLongTask = New-Object System.Windows.Forms.Label
    $lblLongTask.Text = "長時間作業の警告 (分):"
    $lblLongTask.Location = New-Object System.Drawing.Point(20, 225)
    $lblLongTask.AutoSize = $true
    $gbApp.Controls.Add($lblLongTask)

    $numLongTask = New-Object System.Windows.Forms.NumericUpDown
    $numLongTask.Location = New-Object System.Drawing.Point(150, 222)
    $numLongTask.Width = 60
    $numLongTask.Minimum = 0; $numLongTask.Maximum = 1440
    $numLongTask.Value = if ($s.LongTaskNotificationMinutes) { [int]$s.LongTaskNotificationMinutes } else { 180 }
    $gbApp.Controls.Add($numLongTask)

    # =================================================================
    # ② GB: [表示・カレンダー設定]
    # =================================================================
    $gbView = Add-GroupBox "表示・カレンダー設定" 280

    $lblStartupView = New-Object System.Windows.Forms.Label
    $lblStartupView.Text = "起動時の画面:"
    $lblStartupView.Location = New-Object System.Drawing.Point(20, 30)
    $lblStartupView.AutoSize = $true
    $gbView.Controls.Add($lblStartupView)

    $cmbStartupView = New-Object System.Windows.Forms.ComboBox
    $cmbStartupView.Location = New-Object System.Drawing.Point(150, 27)
    $cmbStartupView.Width = 120
    $cmbStartupView.DropDownStyle = "DropDownList"
    $cmbStartupView.DisplayMember = "Label"; $cmbStartupView.ValueMember = "Value"
    $cmbStartupView.Items.Add([PSCustomObject]@{ Label = "リスト"; Value = "List" })
    $cmbStartupView.Items.Add([PSCustomObject]@{ Label = "カンバン"; Value = "Kanban" })
    $cmbStartupView.Items.Add([PSCustomObject]@{ Label = "カレンダー"; Value = "Calendar" })
    foreach($i in $cmbStartupView.Items){if($i.Value -eq $s.StartupView){$cmbStartupView.SelectedItem=$i;break}}
    $gbView.Controls.Add($cmbStartupView)

    $lblDateFormat = New-Object System.Windows.Forms.Label
    $lblDateFormat.Text = "日付形式:"
    $lblDateFormat.Location = New-Object System.Drawing.Point(290, 30)
    $lblDateFormat.AutoSize = $true
    $gbView.Controls.Add($lblDateFormat)

    $cmbDateFormat = New-Object System.Windows.Forms.ComboBox
    $cmbDateFormat.Location = New-Object System.Drawing.Point(360, 27)
    $cmbDateFormat.Width = 120
    $cmbDateFormat.DropDownStyle = "DropDownList"
    $cmbDateFormat.Items.AddRange(@("yyyy/MM/dd", "MM/dd", "yyyy-MM-dd"))
    $cmbDateFormat.SelectedItem = $s.DateFormat
    $gbView.Controls.Add($cmbDateFormat)

    $lblDayStartHour = New-Object System.Windows.Forms.Label
    $lblDayStartHour.Text = "日付の境界線 (時):"
    $lblDayStartHour.Location = New-Object System.Drawing.Point(20, 70)
    $lblDayStartHour.AutoSize = $true
    $gbView.Controls.Add($lblDayStartHour)

    $numDayStartHour = New-Object System.Windows.Forms.NumericUpDown
    $numDayStartHour.Location = New-Object System.Drawing.Point(150, 67)
    $numDayStartHour.Width = 60
    $numDayStartHour.Minimum = 0; $numDayStartHour.Maximum = 23
    $numDayStartHour.Value = [int]$s.DayStartHour
    $gbView.Controls.Add($numDayStartHour)

    $lblWeekStart = New-Object System.Windows.Forms.Label
    $lblWeekStart.Text = "週の始まり:"
    $lblWeekStart.Location = New-Object System.Drawing.Point(290, 70)
    $lblWeekStart.AutoSize = $true
    $gbView.Controls.Add($lblWeekStart)

    $cmbWeekStart = New-Object System.Windows.Forms.ComboBox
    $cmbWeekStart.Location = New-Object System.Drawing.Point(360, 67)
    $cmbWeekStart.Width = 120
    $cmbWeekStart.DropDownStyle = "DropDownList"
    $cmbWeekStart.Items.AddRange(@("日曜", "月曜"))
    $cmbWeekStart.SelectedIndex = if ($s.CalendarWeekStart -eq 1) { 1 } else { 0 }
    $gbView.Controls.Add($cmbWeekStart)

    $chkColorWeekend = New-Object System.Windows.Forms.CheckBox
    $chkColorWeekend.Text = "カレンダーの土日を色分けする"
    $chkColorWeekend.Location = New-Object System.Drawing.Point(20, 110)
    $chkColorWeekend.AutoSize = $true
    $chkColorWeekend.Checked = [bool]$s.ColorWeekend
    $gbView.Controls.Add($chkColorWeekend)

    $chkShowTooltips = New-Object System.Windows.Forms.CheckBox
    $chkShowTooltips.Text = "ツールチップを表示する"
    $chkShowTooltips.Location = New-Object System.Drawing.Point(290, 110)
    $chkShowTooltips.AutoSize = $true
    $chkShowTooltips.Checked = [bool]$s.ShowTooltips
    $gbView.Controls.Add($chkShowTooltips)

    $lblTimeline = New-Object System.Windows.Forms.Label
    $lblTimeline.Text = "タイムライン表示範囲 (時):"
    $lblTimeline.Location = New-Object System.Drawing.Point(20, 150)
    $lblTimeline.AutoSize = $true
    $gbView.Controls.Add($lblTimeline)

    $numTimelineStart = New-Object System.Windows.Forms.NumericUpDown
    $numTimelineStart.Location = New-Object System.Drawing.Point(180, 147)
    $numTimelineStart.Width = 50
    $numTimelineStart.Minimum = 0; $numTimelineStart.Maximum = 23
    $numTimelineStart.Value = [int]$s.TimelineStartHour
    $gbView.Controls.Add($numTimelineStart)

    $lblTimelineSep = New-Object System.Windows.Forms.Label
    $lblTimelineSep.Text = "～"
    $lblTimelineSep.Location = New-Object System.Drawing.Point(240, 150)
    $lblTimelineSep.AutoSize = $true
    $gbView.Controls.Add($lblTimelineSep)

    $numTimelineEnd = New-Object System.Windows.Forms.NumericUpDown
    $numTimelineEnd.Location = New-Object System.Drawing.Point(270, 147)
    $numTimelineEnd.Width = 50
    $numTimelineEnd.Minimum = 1; $numTimelineEnd.Maximum = 24
    $numTimelineEnd.Value = [int]$s.TimelineEndHour
    $gbView.Controls.Add($numTimelineEnd)

    $lblOverlap = New-Object System.Windows.Forms.Label
    $lblOverlap.Text = "時間記録の重複:"
    $lblOverlap.Location = New-Object System.Drawing.Point(20, 185)
    $lblOverlap.AutoSize = $true
    $gbView.Controls.Add($lblOverlap)

    $cmbOverlap = New-Object System.Windows.Forms.ComboBox
    $cmbOverlap.Location = New-Object System.Drawing.Point(150, 182)
    $cmbOverlap.Width = 120
    $cmbOverlap.DropDownStyle = "DropDownList"
    $cmbOverlap.DisplayMember = "Label"; $cmbOverlap.ValueMember = "Value"
    $cmbOverlap.Items.Add([PSCustomObject]@{ Label = "エラーを表示 (中断)"; Value = "Error" })
    $cmbOverlap.Items.Add([PSCustomObject]@{ Label = "上書き (修正)"; Value = "Overwrite" })
    foreach($i in $cmbOverlap.Items){if($i.Value -eq $s.TimeLogOverlapBehavior){$cmbOverlap.SelectedItem=$i;break}}
    if ($cmbOverlap.SelectedIndex -eq -1) { $cmbOverlap.SelectedIndex = 0 }
    $gbView.Controls.Add($cmbOverlap)

    $chkEventNotify = New-Object System.Windows.Forms.CheckBox
    $chkEventNotify.Text = "イベント通知 (分前):"
    $chkEventNotify.Location = New-Object System.Drawing.Point(20, 220)
    $chkEventNotify.AutoSize = $true
    $chkEventNotify.Checked = [bool]$s.EventNotificationEnabled
    $gbView.Controls.Add($chkEventNotify)

    $numEventNotify = New-Object System.Windows.Forms.NumericUpDown
    $numEventNotify.Location = New-Object System.Drawing.Point(180, 218)
    $numEventNotify.Width = 60
    $numEventNotify.Minimum = 0; $numEventNotify.Maximum = 1440
    $numEventNotify.Value = if ($s.EventNotificationMinutes) { [int]$s.EventNotificationMinutes } else { 15 }
    $gbView.Controls.Add($numEventNotify)

    # =================================================================
    # ③ GB: [タスク操作・リスト設定]
    # =================================================================
    $gbTask = Add-GroupBox "タスク操作・リスト設定" 300

    $lblDensity = New-Object System.Windows.Forms.Label
    $lblDensity.Text = "リスト行間:"
    $lblDensity.Location = New-Object System.Drawing.Point(20, 30)
    $lblDensity.AutoSize = $true
    $gbTask.Controls.Add($lblDensity)

    $cmbDensity = New-Object System.Windows.Forms.ComboBox
    $cmbDensity.Location = New-Object System.Drawing.Point(120, 27)
    $cmbDensity.Width = 100
    $cmbDensity.DropDownStyle = "DropDownList"
    $cmbDensity.DisplayMember = "Label"; $cmbDensity.ValueMember = "Value"
    $cmbDensity.Items.Add([PSCustomObject]@{ Label = "狭い"; Value = "Compact" })
    $cmbDensity.Items.Add([PSCustomObject]@{ Label = "標準"; Value = "Standard" })
    $cmbDensity.Items.Add([PSCustomObject]@{ Label = "広い"; Value = "Relaxed" })
    foreach($i in $cmbDensity.Items){if($i.Value -eq $s.ListDensity){$cmbDensity.SelectedItem=$i;break}}
    $gbTask.Controls.Add($cmbDensity)

    $chkStrike = New-Object System.Windows.Forms.CheckBox
    $chkStrike.Text = "完了タスク打ち消し線"
    $chkStrike.Location = New-Object System.Drawing.Point(250, 27)
    $chkStrike.AutoSize = $true
    $chkStrike.Checked = [bool]$s.ShowStrikethrough
    $gbTask.Controls.Add($chkStrike)

    $chkKanbanDone = New-Object System.Windows.Forms.CheckBox
    $chkKanbanDone.Text = "カンバンの完了列表示"
    $chkKanbanDone.Location = New-Object System.Drawing.Point(250, 55)
    $chkKanbanDone.AutoSize = $true
    $chkKanbanDone.Checked = [bool]$s.ShowKanbanDone
    $gbTask.Controls.Add($chkKanbanDone)

    $lblSort = New-Object System.Windows.Forms.Label
    $lblSort.Text = "デフォルト並び順:"
    $lblSort.Location = New-Object System.Drawing.Point(20, 70)
    $lblSort.AutoSize = $true
    $gbTask.Controls.Add($lblSort)

    $cmbSort = New-Object System.Windows.Forms.ComboBox
    $cmbSort.Location = New-Object System.Drawing.Point(120, 67)
    $cmbSort.Width = 100
    $cmbSort.DropDownStyle = "DropDownList"
    $cmbSort.DisplayMember = "Label"; $cmbSort.ValueMember = "Value"
    $cmbSort.Items.Add([PSCustomObject]@{ Label = "期日"; Value = "DueDate" })
    $cmbSort.Items.Add([PSCustomObject]@{ Label = "優先度"; Value = "Priority" })
    $cmbSort.Items.Add([PSCustomObject]@{ Label = "作成日"; Value = "CreatedDate" })
    foreach($i in $cmbSort.Items){if($i.Value -eq $s.DefaultSort){$cmbSort.SelectedItem=$i;break}}
    $gbTask.Controls.Add($cmbSort)

    $lblDblClick = New-Object System.Windows.Forms.Label
    $lblDblClick.Text = "ダブルクリック動作:"
    $lblDblClick.Location = New-Object System.Drawing.Point(20, 110)
    $lblDblClick.AutoSize = $true
    $gbTask.Controls.Add($lblDblClick)

    $cmbDblClick = New-Object System.Windows.Forms.ComboBox
    $cmbDblClick.Location = New-Object System.Drawing.Point(140, 107)
    $cmbDblClick.Width = 100
    $cmbDblClick.DropDownStyle = "DropDownList"
    $cmbDblClick.DisplayMember = "Label"; $cmbDblClick.ValueMember = "Value"
    $cmbDblClick.Items.Add([PSCustomObject]@{ Label = "編集"; Value = "Edit" })
    $cmbDblClick.Items.Add([PSCustomObject]@{ Label = "状態切替"; Value = "ToggleStatus" })
    foreach($i in $cmbDblClick.Items){if($i.Value -eq $s.DoubleClickAction){$cmbDblClick.SelectedItem=$i;break}}
    $gbTask.Controls.Add($cmbDblClick)

    $lblNotifyStyle = New-Object System.Windows.Forms.Label
    $lblNotifyStyle.Text = "通知スタイル:"
    $lblNotifyStyle.Location = New-Object System.Drawing.Point(260, 110)
    $lblNotifyStyle.AutoSize = $true
    $gbTask.Controls.Add($lblNotifyStyle)

    $cmbNotifyStyle = New-Object System.Windows.Forms.ComboBox
    $cmbNotifyStyle.Location = New-Object System.Drawing.Point(350, 107)
    $cmbNotifyStyle.Width = 100
    $cmbNotifyStyle.DropDownStyle = "DropDownList"
    $cmbNotifyStyle.DisplayMember = "Label"; $cmbNotifyStyle.ValueMember = "Value"
    $cmbNotifyStyle.Items.Add([PSCustomObject]@{ Label = "ダイアログ"; Value = "Dialog" })
    $cmbNotifyStyle.Items.Add([PSCustomObject]@{ Label = "バルーン"; Value = "Balloon" })
    foreach($i in $cmbNotifyStyle.Items){if($i.Value -eq $s.NotificationStyle){$cmbNotifyStyle.SelectedItem=$i;break}}
    $gbTask.Controls.Add($cmbNotifyStyle)

    $lblAlert = New-Object System.Windows.Forms.Label
    $lblAlert.Text = "期限アラート (赤/黄 日数):"
    $lblAlert.Location = New-Object System.Drawing.Point(20, 150)
    $lblAlert.AutoSize = $true
    $gbTask.Controls.Add($lblAlert)

    $numAlertRed = New-Object System.Windows.Forms.NumericUpDown
    $numAlertRed.Location = New-Object System.Drawing.Point(180, 147)
    $numAlertRed.Width = 50
    $numAlertRed.Value = [int]$s.AlertDaysRed
    $gbTask.Controls.Add($numAlertRed)

    $numAlertYellow = New-Object System.Windows.Forms.NumericUpDown
    $numAlertYellow.Location = New-Object System.Drawing.Point(240, 147)
    $numAlertYellow.Width = 50
    $numAlertYellow.Value = [int]$s.AlertDaysYellow
    $gbTask.Controls.Add($numAlertYellow)

    $lblNewTask = New-Object System.Windows.Forms.Label
    $lblNewTask.Text = "新規タスク既定値 (優先度/期限+日):"
    $lblNewTask.Location = New-Object System.Drawing.Point(20, 190)
    $lblNewTask.AutoSize = $true
    $gbTask.Controls.Add($lblNewTask)

    $cmbPriority = New-Object System.Windows.Forms.ComboBox
    $cmbPriority.Location = New-Object System.Drawing.Point(240, 187)
    $cmbPriority.Width = 60
    $cmbPriority.DropDownStyle = "DropDownList"
    $cmbPriority.Items.AddRange(@("高", "中", "低"))
    $cmbPriority.SelectedItem = $s.DefaultPriority
    if ($cmbPriority.SelectedIndex -eq -1) { $cmbPriority.SelectedItem = "中" }
    $gbTask.Controls.Add($cmbPriority)

    $numDueOffset = New-Object System.Windows.Forms.NumericUpDown
    $numDueOffset.Location = New-Object System.Drawing.Point(310, 187)
    $numDueOffset.Width = 50
    $numDueOffset.Value = [int]$s.DefaultDueOffset
    $gbTask.Controls.Add($numDueOffset)

    $lblGlobalNotify = New-Object System.Windows.Forms.Label
    $lblGlobalNotify.Text = "デフォルト通知:"
    $lblGlobalNotify.Location = New-Object System.Drawing.Point(20, 230)
    $lblGlobalNotify.AutoSize = $true
    $gbTask.Controls.Add($lblGlobalNotify)

    $cmbGlobalNotify = New-Object System.Windows.Forms.ComboBox
    $cmbGlobalNotify.Location = New-Object System.Drawing.Point(120, 227)
    $cmbGlobalNotify.Width = 100
    $cmbGlobalNotify.DropDownStyle = "DropDownList"
    $cmbGlobalNotify.DisplayMember = "Label"; $cmbGlobalNotify.ValueMember = "Value"
    $cmbGlobalNotify.Items.Add([PSCustomObject]@{ Label = "通知しない"; Value = "通知しない" })
    $cmbGlobalNotify.Items.Add([PSCustomObject]@{ Label = "当日"; Value = "当日" })
    $cmbGlobalNotify.Items.Add([PSCustomObject]@{ Label = "1日前"; Value = "1日前" })
    $cmbGlobalNotify.Items.Add([PSCustomObject]@{ Label = "前の営業日"; Value = "前の営業日" })
    $cmbGlobalNotify.Items.Add([PSCustomObject]@{ Label = "3日前"; Value = "3日前" })
    $cmbGlobalNotify.Items.Add([PSCustomObject]@{ Label = "1週間前"; Value = "1週間前" })
    foreach($i in $cmbGlobalNotify.Items){if($i.Value -eq $s.GlobalNotification){$cmbGlobalNotify.SelectedItem=$i;break}}
    if ($cmbGlobalNotify.SelectedIndex -eq -1) { $cmbGlobalNotify.SelectedItem = "当日" }
    $gbTask.Controls.Add($cmbGlobalNotify)

    $lblNotifyDays = New-Object System.Windows.Forms.Label
    $lblNotifyDays.Text = "通知ボタン表示期間 (日):"
    $lblNotifyDays.Location = New-Object System.Drawing.Point(240, 230)
    $lblNotifyDays.AutoSize = $true
    $gbTask.Controls.Add($lblNotifyDays)

    $numNotifyDays = New-Object System.Windows.Forms.NumericUpDown
    $numNotifyDays.Location = New-Object System.Drawing.Point(390, 227)
    $numNotifyDays.Width = 50
    $numNotifyDays.Minimum = 1; $numNotifyDays.Maximum = 30
    $numNotifyDays.Value = if ($s.NotificationButtonDays) { [int]$s.NotificationButtonDays } else { 7 }
    $gbTask.Controls.Add($numNotifyDays)

    # =================================================================
    # ④ GB: [データ・分析設定]
    # =================================================================
    $gbData = Add-GroupBox "データ・分析設定" 350

    $lblBackup = New-Object System.Windows.Forms.Label
    $lblBackup.Text = "バックアップ保存先:"
    $lblBackup.Location = New-Object System.Drawing.Point(20, 30)
    $lblBackup.AutoSize = $true
    $gbData.Controls.Add($lblBackup)

    $txtBackup = New-Object System.Windows.Forms.TextBox
    $txtBackup.Location = New-Object System.Drawing.Point(140, 27)
    $txtBackup.Size = New-Object System.Drawing.Size(250, 23)
    $txtBackup.Text = $s.BackupPath
    $gbData.Controls.Add($txtBackup)

    $btnBackup = New-Object System.Windows.Forms.Button
    $btnBackup.Text = "..."
    $btnBackup.Location = New-Object System.Drawing.Point(400, 25)
    $btnBackup.Size = New-Object System.Drawing.Size(40, 25)
    $gbData.Controls.Add($btnBackup)
    $btnBackup.Add_Click({
        $fbd = New-Object System.Windows.Forms.FolderBrowserDialog
        if ($fbd.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $txtBackup.Text = $fbd.SelectedPath
        }
    })

    $lblArchive = New-Object System.Windows.Forms.Label
    $lblArchive.Text = "自動アーカイブ日数:"
    $lblArchive.Location = New-Object System.Drawing.Point(20, 70)
    $lblArchive.AutoSize = $true
    $gbData.Controls.Add($lblArchive)

    $numArchive = New-Object System.Windows.Forms.NumericUpDown
    $numArchive.Location = New-Object System.Drawing.Point(150, 67)
    $numArchive.Width = 60
    $numArchive.Value = [int]$s.AutoArchiveDays
    $gbData.Controls.Add($numArchive)

    $lblWarn = New-Object System.Windows.Forms.Label
    $lblWarn.Text = "レポート警告基準 (%):"
    $lblWarn.Location = New-Object System.Drawing.Point(20, 110)
    $lblWarn.AutoSize = $true
    $gbData.Controls.Add($lblWarn)

    $numWarn = New-Object System.Windows.Forms.NumericUpDown
    $numWarn.Location = New-Object System.Drawing.Point(150, 107)
    $numWarn.Width = 60
    $numWarn.Value = [int]$s.AnalysisWarnPercent
    $gbData.Controls.Add($numWarn)

    $lblPomodoro = New-Object System.Windows.Forms.Label
    $lblPomodoro.Text = "ポモドーロ作業時間 (分):"
    $lblPomodoro.Location = New-Object System.Drawing.Point(250, 110)
    $lblPomodoro.AutoSize = $true
    $gbData.Controls.Add($lblPomodoro)

    $numPomodoro = New-Object System.Windows.Forms.NumericUpDown
    $numPomodoro.Location = New-Object System.Drawing.Point(400, 107)
    $numPomodoro.Width = 60
    $numPomodoro.Value = [int]$s.PomodoroWorkMinutes
    $gbData.Controls.Add($numPomodoro)

    $lblRetention = New-Object System.Windows.Forms.Label
    $lblRetention.Text = "バックアップ保持 (日):"
    $lblRetention.Location = New-Object System.Drawing.Point(20, 150)
    $lblRetention.AutoSize = $true
    $gbData.Controls.Add($lblRetention)

    $numRetention = New-Object System.Windows.Forms.NumericUpDown
    $numRetention.Location = New-Object System.Drawing.Point(150, 147)
    $numRetention.Width = 60
    $numRetention.Minimum = 1; $numRetention.Maximum = 3650
    $numRetention.Value = if ($s.BackupRetentionDays) { [int]$s.BackupRetentionDays } else { 30 }
    $gbData.Controls.Add($numRetention)

    $lblProjArchive = New-Object System.Windows.Forms.Label
    $lblProjArchive.Text = "PJ自動アーカイブ (日):"
    $lblProjArchive.Location = New-Object System.Drawing.Point(250, 150)
    $lblProjArchive.AutoSize = $true
    $gbData.Controls.Add($lblProjArchive)

    $numProjArchive = New-Object System.Windows.Forms.NumericUpDown
    $numProjArchive.Location = New-Object System.Drawing.Point(380, 147)
    $numProjArchive.Width = 60
    $numProjArchive.Minimum = 0; $numProjArchive.Maximum = 3650
    $numProjArchive.Value = if ($s.AutoArchiveProjectsDays) { [int]$s.AutoArchiveProjectsDays } else { 60 }
    $gbData.Controls.Add($numProjArchive)

    $chkArchiveOnComp = New-Object System.Windows.Forms.CheckBox
    $chkArchiveOnComp.Text = "PJ完了時にタスクを即時アーカイブ"
    $chkArchiveOnComp.Location = New-Object System.Drawing.Point(20, 190)
    $chkArchiveOnComp.AutoSize = $true
    $chkArchiveOnComp.Checked = [bool]$s.ArchiveTasksOnProjectCompletion
    $gbData.Controls.Add($chkArchiveOnComp)

    $chkArchiveOnTaskComp = New-Object System.Windows.Forms.CheckBox
    $chkArchiveOnTaskComp.Text = "タスク完了時に即時アーカイブ (PJ無関係)"
    $chkArchiveOnTaskComp.Location = New-Object System.Drawing.Point(20, 220)
    $chkArchiveOnTaskComp.AutoSize = $true
    $chkArchiveOnTaskComp.Checked = [bool]$s.ArchiveTasksOnCompletion
    $gbData.Controls.Add($chkArchiveOnTaskComp)

    $lblExclude = New-Object System.Windows.Forms.Label
    $lblExclude.Text = "完了スピード計算から除外するステータス:"
    $lblExclude.Location = New-Object System.Drawing.Point(20, 250)
    $lblExclude.AutoSize = $true
    $gbData.Controls.Add($lblExclude)
    
    $excludePanel = New-Object System.Windows.Forms.FlowLayoutPanel
    $excludePanel.Location = New-Object System.Drawing.Point(20, 270)
    $excludePanel.Size = New-Object System.Drawing.Size(460, 40)
    $gbData.Controls.Add($excludePanel)
    
    $excludeCheckboxes = @()
    $currentExcludes = if ($s.LeadTimeExcludeStatuses) { $s.LeadTimeExcludeStatuses } else { @() }
    foreach ($status in $script:TaskStatuses) {
        $cb = New-Object System.Windows.Forms.CheckBox
        $cb.Text = $status
        $cb.AutoSize = $true
        if ($currentExcludes -contains $status) { $cb.Checked = $true }
        $excludePanel.Controls.Add($cb)
        $excludeCheckboxes += $cb
    }

    # =================================================================
    # ⑤ GB: [メンテナンス]
    # =================================================================
    $gbMaint = Add-GroupBox "メンテナンス" 100

    $btnOpenData = New-Object System.Windows.Forms.Button
    $btnOpenData.Text = "📂 データフォルダを開く"
    $btnOpenData.Location = New-Object System.Drawing.Point(20, 30)
    $btnOpenData.Size = New-Object System.Drawing.Size(140, 30)
    $gbMaint.Controls.Add($btnOpenData)
    $btnOpenData.Add_Click({ Invoke-Item $script:AppRoot })

    $btnResetPos = New-Object System.Windows.Forms.Button
    $btnResetPos.Text = "🔄 ウィンドウ位置リセット"
    $btnResetPos.Location = New-Object System.Drawing.Point(170, 30)
    $btnResetPos.Size = New-Object System.Drawing.Size(150, 30)
    $gbMaint.Controls.Add($btnResetPos)
    $btnResetPos.Add_Click({
        if ($parentForm) {
            $parentForm.StartPosition = "Manual"
            $parentForm.Location = New-Object System.Drawing.Point(100, 100)
            [System.Windows.Forms.MessageBox]::Show("ウィンドウ位置をリセットしました。", "完了", "OK", "Information")
        }
    })

    $btnResetConfig = New-Object System.Windows.Forms.Button
    $btnResetConfig.Text = "⚠️ 設定を初期化"
    $btnResetConfig.Location = New-Object System.Drawing.Point(330, 30)
    $btnResetConfig.Size = New-Object System.Drawing.Size(120, 30)
    $btnResetConfig.ForeColor = [System.Drawing.Color]::Red
    $gbMaint.Controls.Add($btnResetConfig)
    $btnResetConfig.Add_Click({
        if ([System.Windows.Forms.MessageBox]::Show("すべての設定を初期化します。よろしいですか？`n(データは削除されません)", "確認", "YesNo", "Warning") -eq "Yes") {
            if (Test-Path $script:SettingsFile) { Remove-Item $script:SettingsFile }
            [System.Windows.Forms.MessageBox]::Show("設定を初期化しました。再起動してください。", "完了", "OK", "Information")
            $form.Close()
        }
    })

    # スクロール末尾の余白確保用ダミーラベル
    $lblSpacer = New-Object System.Windows.Forms.Label
    $lblSpacer.Location = New-Object System.Drawing.Point(10, $script:currentY)
    $lblSpacer.Size = New-Object System.Drawing.Size(10, 30)
    $mainPanel.Controls.Add($lblSpacer)

    # --- 保存処理 ---
    $btnSave.Add_Click({
        $script:Settings.RunAtStartup = $chkRunAtStartup.Checked
        $script:Settings.MinimizeToTray = $chkMinimizeToTray.Checked
        $script:Settings.AlwaysOnTop = $chkAlwaysOnTop.Checked
        $script:Settings.EnableSoundEffects = $chkEnableSoundEffects.Checked
        $script:Settings.Passcode = $txtPasscode.Text
        $script:Settings.WindowOpacity = $trackOpacity.Value / 10.0
        $script:Settings.IdleTimeoutMinutes = [int]$numIdle.Value

        $script:Settings.StartupView = $cmbStartupView.SelectedItem.Value
        $script:Settings.DateFormat = $cmbDateFormat.SelectedItem
        $script:Settings.DayStartHour = [int]$numDayStartHour.Value
        $script:Settings.CalendarWeekStart = if ($cmbWeekStart.SelectedIndex -eq 1) { 1 } else { 0 }
        $script:Settings.ColorWeekend = $chkColorWeekend.Checked
        $script:Settings.ShowTooltips = $chkShowTooltips.Checked
        $script:Settings.TimelineStartHour = [int]$numTimelineStart.Value
        $script:Settings.TimelineEndHour = [int]$numTimelineEnd.Value
        $script:Settings.TimeLogOverlapBehavior = $cmbOverlap.SelectedItem.Value
        $script:Settings.EventNotificationEnabled = $chkEventNotify.Checked
        $script:Settings.EventNotificationMinutes = [int]$numEventNotify.Value

        $script:Settings.ListDensity = $cmbDensity.SelectedItem.Value
        $script:Settings.ShowStrikethrough = $chkStrike.Checked
        $script:Settings.ShowKanbanDone = $chkKanbanDone.Checked
        $script:Settings.DefaultSort = $cmbSort.SelectedItem.Value
        $script:Settings.DoubleClickAction = $cmbDblClick.SelectedItem.Value
        $script:Settings.NotificationStyle = $cmbNotifyStyle.SelectedItem.Value
        $script:Settings.AlertDaysRed = [int]$numAlertRed.Value
        $script:Settings.AlertDaysYellow = [int]$numAlertYellow.Value
        $script:Settings.DefaultPriority = $cmbPriority.SelectedItem
        $script:Settings.DefaultDueOffset = [int]$numDueOffset.Value
        $script:Settings.GlobalNotification = if ($cmbGlobalNotify.SelectedItem.Value) { $cmbGlobalNotify.SelectedItem.Value } else { $cmbGlobalNotify.SelectedItem }
        $script:Settings.NotificationButtonDays = [int]$numNotifyDays.Value

        $script:Settings.BackupPath = $txtBackup.Text
        $script:Settings.AutoArchiveDays = [int]$numArchive.Value
        $script:Settings.AnalysisWarnPercent = [int]$numWarn.Value
        $script:Settings.PomodoroWorkMinutes = [int]$numPomodoro.Value
        $script:Settings.LongTaskNotificationMinutes = [int]$numLongTask.Value
        $script:Settings.BackupRetentionDays = [int]$numRetention.Value
        $script:Settings.AutoArchiveProjectsDays = [int]$numProjArchive.Value
        $script:Settings.ArchiveTasksOnProjectCompletion = $chkArchiveOnComp.Checked
        $script:Settings.ArchiveTasksOnCompletion = $chkArchiveOnTaskComp.Checked

        $newExcludes = @()
        foreach ($cb in $excludeCheckboxes) {
            if ($cb.Checked) { $newExcludes += $cb.Text }
        }
        $script:Settings.LeadTimeExcludeStatuses = $newExcludes

        Save-DataFile -filePath $script:SettingsFile -dataObject $script:Settings
             
     # 設定反映のための処理 (一部)
        if ($parentForm) {
            $parentForm.TopMost = $script:Settings.AlwaysOnTop
            $parentForm.Opacity = $script:Settings.WindowOpacity
        }
        Update-StartupShortcut
        Update-AllViews

        $form.Close()
    })
        $btnCancel.Add_Click({ $form.Close() })
     Set-Theme -form $form -IsDarkMode $script:isDarkMode
    $form.ShowDialog($parentForm)
}

function Set-FileIcon {
    param(
        [System.Windows.Forms.ListViewItem]$item, 
        [System.Windows.Forms.ImageList]$imageList, 
        [string]$fileType
    )
    
    $imageKey = "__file__" # Default
    switch ($fileType) {
        'Image' { $imageKey = "__image__" }
        'URL'   { $imageKey = "__url__" }
        'Memo'  { $imageKey = "__memo__" }
    }

    if ($fileType -in @('File', 'Image')) {
        $filePath = $item.Name 
        try {
            if (Test-Path -LiteralPath $filePath) {
                if (Test-Path -LiteralPath $filePath -PathType Container) {
                    $imageKey = "__folder__"
                } else {
                    $ext = [System.IO.Path]::GetExtension($filePath).ToLower()
                    if (-not [string]::IsNullOrEmpty($ext)) {
                        $extKey = "EXT_" + $ext
                        if (-not $imageList.Images.ContainsKey($extKey)) {
                            $sfi = New-Object -TypeName TaskManager.WinAPI.User32+SHFILEINFO
                            $flags = 0x100 -bor 0x400 -bor 0x80 # SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES
                            [void][TaskManager.WinAPI.User32]::SHGetFileInfo($filePath, 0x80, ([ref]$sfi), ([System.Runtime.InteropServices.Marshal]::SizeOf($sfi)), $flags)
                            if ($sfi.hIcon -ne [System.IntPtr]::Zero) {
                                $icon = [System.Drawing.Icon]::FromHandle($sfi.hIcon).Clone()
                                [void][TaskManager.WinAPI.User32]::DestroyIcon($sfi.hIcon)
                                $imageList.Images.Add($extKey, $icon) | Out-Null
                            }
                        }
                        if ($imageList.Images.ContainsKey($extKey)) {
                            $imageKey = $extKey
                        }
                    }
                }
            }
        } catch {
            # エラーが発生しても、デフォルトの $imageKey が使われる
        }
    }
    $item.ImageKey = $imageKey
}

function Show-NotificationForm {
    param([array]$notifications)
    if ($notifications.Count -eq 0) { return }
    $notifyForm = New-Object System.Windows.Forms.Form; $notifyForm.Text = "🔔 リマインダー"; $notifyForm.Width = 500; $notifyForm.Height = 350; $notifyForm.StartPosition = 'CenterScreen'; $notifyForm.FormBorderStyle = 'FixedDialog'; $notifyForm.MaximizeBox = $false; $notifyForm.MinimizeBox = $false
    $headerLabel = New-Object System.Windows.Forms.Label; $headerLabel.Text = "以下のアイテムが期日を迎えます："; $headerLabel.Location = "10, 10"; $headerLabel.Font = New-Object System.Drawing.Font("Meiryo UI", 10, [System.Drawing.FontStyle]::Bold); $headerLabel.AutoSize = $true; $notifyForm.Controls.Add($headerLabel)
    $contentPanel = New-Object System.Windows.Forms.Panel; $contentPanel.Location = "10, 35"; $contentPanel.Size = "465, 230"; $contentPanel.BorderStyle = "FixedSingle"; $contentPanel.AutoScroll = $true; $notifyForm.Controls.Add($contentPanel)
    $currentY = 5
    $subjectLabel = New-Object System.Windows.Forms.Label; $subjectLabel.Text = "【件名】"; $subjectLabel.Font = New-Object System.Drawing.Font($headerLabel.Font, [System.Drawing.FontStyle]::Bold); $subjectLabel.Location = "5, $currentY"; $subjectLabel.AutoSize = $true; $contentPanel.Controls.Add($subjectLabel); $currentY += 20
    $subjects = $notifications | Where-Object { $_.Type -eq 'Subject' }
    if ($subjects) {
        foreach ($s in $subjects) {
            $itemLabel = New-Object System.Windows.Forms.Label
            $itemText = if ($s.ItemObject) { $s.ItemObject.Name } else { $s.Name }
            $itemLabel.Text = "・ $($itemText) (期日: $($s.DueDate.ToString('yyyy-MM-dd')))"
            $itemLabel.Location = "15, $currentY"; $itemLabel.AutoSize = $true; $contentPanel.Controls.Add($itemLabel); $currentY += 20
        }
    } else { $noItemLabel = New-Object System.Windows.Forms.Label; $noItemLabel.Text = " (なし)"; $noItemLabel.Location = "15, $currentY"; $noItemLabel.AutoSize = $true; $contentPanel.Controls.Add($noItemLabel); $currentY += 20 }
    $currentY += 10
    $taskLabel = New-Object System.Windows.Forms.Label; $taskLabel.Text = "【タスク】"; $taskLabel.Font = New-Object System.Drawing.Font($headerLabel.Font, [System.Drawing.FontStyle]::Bold); $taskLabel.Location = "5, $currentY"; $taskLabel.AutoSize = $true; $contentPanel.Controls.Add($taskLabel); $currentY += 20
    $tasks = $notifications | Where-Object { $_.Type -eq 'Task' }
    if ($tasks) {
        foreach ($t in $tasks) {
            $itemLabel = New-Object System.Windows.Forms.Label
            $itemLabel.Text = "・ $($t.ItemObject.件名) - $($t.ItemObject.タスク) (期日: $($t.DueDate.ToString('yyyy-MM-dd')))"
            $itemLabel.Location = "15, $currentY"; $itemLabel.AutoSize = $true
            $contentPanel.Controls.Add($itemLabel); $currentY += 20
        }
    } else { $noTaskLabel = New-Object System.Windows.Forms.Label; $noTaskLabel.Text = " (なし)"; $noTaskLabel.Location = "15, $currentY"; $noTaskLabel.AutoSize = $true; $contentPanel.Controls.Add($noTaskLabel) }
    $okButton = New-Object System.Windows.Forms.Button; $okButton.Text = "閉じる"; $okButton.Location = "200, 275"; $okButton.Size = "80, 25"; $notifyForm.Controls.Add($okButton); $notifyForm.AcceptButton = $okButton; $okButton.Add_Click({ $notifyForm.Close() })
    Set-Theme -form $notifyForm -IsDarkMode $script:isDarkMode
    $notifyForm.ShowDialog()
}

function Show-EditProjectPropertiesForm {
    param(
        [PSCustomObject]$projectObject,
        [System.Windows.Forms.Form]$parentForm
    )

    if ($null -eq $projectObject) {
        [System.Windows.Forms.MessageBox]::Show("編集対象のプロジェクトが指定されていません。", "エラー", "OK", "Error")
        return
    }

    $propForm = New-Object System.Windows.Forms.Form; $propForm.Text = "プロジェクトのプロパティ"; $propForm.Width = 450; $propForm.Height = 360; $propForm.StartPosition = 'CenterParent'

    # --- Project Name ---
    $labelProjectName = New-Object System.Windows.Forms.Label; $labelProjectName.Text = "プロジェクト名:"; $labelProjectName.Location = "15, 15"; $propForm.Controls.Add($labelProjectName)
    $textProjectName = New-Object System.Windows.Forms.TextBox; $textProjectName.Location = "15, 35"; $textProjectName.Size = "400, 25"; $textProjectName.Text = $projectObject.ProjectName; $propForm.Controls.Add($textProjectName)

    # --- Due Date ---
    $labelDueDate = New-Object System.Windows.Forms.Label; $labelDueDate.Text = "プロジェクトの期日:"; $labelDueDate.Location = "15, 70"; $propForm.Controls.Add($labelDueDate)
    $datePicker = New-Object System.Windows.Forms.DateTimePicker; $datePicker.Format = 'Short'; $datePicker.ShowCheckBox = $true; $datePicker.Location = "15, 90"; $propForm.Controls.Add($datePicker)
    if ($projectObject.ProjectDueDate) {
        $datePicker.Checked = $true
        try { $datePicker.Value = [datetime]$projectObject.ProjectDueDate } catch {}
    } else {
        $datePicker.Value = (Get-Date).AddDays(1)
        $datePicker.Checked = $false
    }

    # --- Notification ---
    $labelNotify = New-Object System.Windows.Forms.Label; $labelNotify.Text = "期日の通知設定:"; $labelNotify.Location = "220, 70"; $propForm.Controls.Add($labelNotify)
    $notifyOptions = @("全体設定に従う", "通知しない", "当日", "1日前", "前の営業日", "3日前", "1週間前")
    $comboNotify = New-Object System.Windows.Forms.ComboBox; $comboNotify.DropDownStyle = "DropDownList"; $comboNotify.Items.AddRange($notifyOptions); $comboNotify.Location = "220, 90"; $comboNotify.Width = 150; $propForm.Controls.Add($comboNotify)
    $comboNotify.SelectedItem = $projectObject.Notification

    # --- Color Picker ---
    $labelColor = New-Object System.Windows.Forms.Label; $labelColor.Text = "タイムラインの色:"; $labelColor.Location = "15, 130"; $propForm.Controls.Add($labelColor)
    $panelColor = New-Object System.Windows.Forms.Panel; $panelColor.Location = "15, 150"; $panelColor.Size = "100, 25"; $panelColor.BorderStyle = "FixedSingle"
    try {
        $panelColor.BackColor = [System.Drawing.ColorTranslator]::FromHtml($projectObject.ProjectColor)
    } catch {
        $panelColor.BackColor = [System.Drawing.ColorTranslator]::FromHtml("#D3D3D3")
    }
    $panelColor.Tag = [System.Drawing.ColorTranslator]::ToHtml($panelColor.BackColor) # Store the hex string
    $propForm.Controls.Add($panelColor)
    $btnColor = New-Object System.Windows.Forms.Button; $btnColor.Text = "色を選択..."; $btnColor.Location = "120, 149"; $btnColor.Size = "80, 27"; $propForm.Controls.Add($btnColor)

    $btnColor.Add_Click({
        $colorDialog = New-Object System.Windows.Forms.ColorDialog
        $colorDialog.Color = $panelColor.BackColor
        if ($colorDialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $panelColor.BackColor = $colorDialog.Color
            $panelColor.Tag = [System.Drawing.ColorTranslator]::ToHtml($colorDialog.Color)
        }
        $colorDialog.Dispose()
    })

    # --- Auto Archive Tasks ---
    $checkAutoArchive = New-Object System.Windows.Forms.CheckBox; $checkAutoArchive.Text = "このプロジェクトの完了済みタスクを自動アーカイブする"; $checkAutoArchive.Location = "15, 190"; $checkAutoArchive.AutoSize = $true
    $checkAutoArchive.Checked = if ($projectObject.PSObject.Properties.Name -contains 'AutoArchiveTasks') { $projectObject.AutoArchiveTasks } else { $true }
    $propForm.Controls.Add($checkAutoArchive)

    # --- Buttons ---
    $btnSave = New-Object System.Windows.Forms.Button; $btnSave.Text = "保存"; $btnSave.Location = "120, 260"; $btnSave.Size = "80, 25"; $propForm.Controls.Add($btnSave)
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "キャンセル"; $btnCancel.Location = "230, 260"; $btnCancel.Size = "80, 25"; $propForm.Controls.Add($btnCancel)
    $propForm.AcceptButton = $btnSave
    $propForm.CancelButton = $btnCancel

    # --- Save Logic ---
    $btnSave.Add_Click({
        $newProjectName = $textProjectName.Text.Trim()
        if ([string]::IsNullOrEmpty($newProjectName)) {
            [System.Windows.Forms.MessageBox]::Show("プロジェクト名は必須です。", "入力エラー", "OK", "Error")
            return
        }

        # Find the project in the global array and update it
        $projectToUpdate = $script:Projects | Where-Object { $_.ProjectID -eq $projectObject.ProjectID } | Select-Object -First 1
        if ($projectToUpdate) {
            $projectToUpdate.ProjectName = $newProjectName
            # 日付のみを保存するように修正
            $projectToUpdate.ProjectDueDate = if ($datePicker.Checked) { $datePicker.Value.ToString("yyyy-MM-dd") } else { $null }
            $projectToUpdate.Notification = $comboNotify.SelectedItem
            $projectToUpdate.ProjectColor = $panelColor.Tag
            $projectToUpdate.AutoArchiveTasks = $checkAutoArchive.Checked
        }

        # Save the entire updated projects array
        Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
        
        $parentForm.Tag = "RELOAD" # Signal to the main form to update views
        Show-SaveFeedbackAndClose -FormToClose $propForm -Message "プロジェクトを保存しました"
    })

    $btnCancel.Add_Click({
        $propForm.Close()
    })

    Set-Theme -form $propForm -IsDarkMode $script:isDarkMode
    $propForm.ShowDialog($parentForm) | Out-Null
}

function Show-TemplateForm {
    param($parentForm)
    $templates = $script:Templates
    $templateNames = $templates.PSObject.Properties | ForEach-Object { $_.Name }
    if ($templateNames.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("利用できるテンプレートが 'templates.json' に定義されていません。", "情報", "OK", "Information")
        return
    }

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "テンプレートから新規プロジェクト作成"
    $form.Width = 400
    $form.Height = 220
    $form.StartPosition = 'CenterParent'

    # 1. Template Selection
    $labelTemplate = New-Object System.Windows.Forms.Label; $labelTemplate.Text = "1. 使用するテンプレートを選択:"; $labelTemplate.Location = "15, 15"; $labelTemplate.AutoSize = $true; $form.Controls.Add($labelTemplate)
    $comboTemplate = New-Object System.Windows.Forms.ComboBox; $comboTemplate.Location = "15, 40"; $comboTemplate.Size = "350, 25"; $comboTemplate.DropDownStyle = "DropDownList"; $comboTemplate.Items.AddRange($templateNames); $comboTemplate.SelectedIndex = 0; $form.Controls.Add($comboTemplate)

    # 2. New Project Name
    $labelProjectName = New-Object System.Windows.Forms.Label; $labelProjectName.Text = "2. 作成するプロジェクト名:"; $labelProjectName.Location = "15, 85"; $labelProjectName.AutoSize = $true; $form.Controls.Add($labelProjectName)
    $textProjectName = New-Object System.Windows.Forms.TextBox; $textProjectName.Location = "15, 110"; $textProjectName.Size = "350, 25"; $form.Controls.Add($textProjectName)

    # --- Placeholder Logic ---
    $updatePlaceholder = {
        if ([string]::IsNullOrWhiteSpace($textProjectName.Text)) {
            $textProjectName.ForeColor = [System.Drawing.Color]::Gray
            $textProjectName.Text = "$($comboTemplate.SelectedItem)_$((Get-Date).ToString('yyyy-MM-dd'))"
        }
    }
    $textProjectName.Add_Enter({
        if ($textProjectName.ForeColor -eq [System.Drawing.Color]::Gray) {
            $textProjectName.Text = ""
            $textProjectName.ForeColor = [System.Drawing.SystemColors]::WindowText
        }
    })
    $textProjectName.Add_Leave({ & $updatePlaceholder })
    $comboTemplate.Add_SelectedIndexChanged({ & $updatePlaceholder })
    
    # Initial placeholder
    & $updatePlaceholder

    # --- Buttons ---
    $btnImport = New-Object System.Windows.Forms.Button; $btnImport.Text = "取り込み"; $btnImport.Location = "100, 150"; $form.Controls.Add($btnImport)
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "キャンセル"; $btnCancel.Location = "200, 150"; $form.Controls.Add($btnCancel)
    $form.AcceptButton = $btnImport
    $form.CancelButton = $btnCancel

    # --- Event Handlers ---
    $btnImport.Add_Click({
        $projectName = $textProjectName.Text.Trim()
        if ($textProjectName.ForeColor -eq [System.Drawing.Color]::Gray) {
            # Placeholder is being used
            $projectName = "$($comboTemplate.SelectedItem)_$((Get-Date).ToString('yyyy-MM-dd'))"
        }

        if ([string]::IsNullOrWhiteSpace($projectName)) {
            [System.Windows.Forms.MessageBox]::Show("プロジェクト名を入力してください。", "入力エラー", "OK", "Error")
            return
        }
        
        if ($script:Projects | Where-Object { $_.ProjectName -eq $projectName }) {
            [System.Windows.Forms.MessageBox]::Show("同じ名前のプロジェクトが既に存在します。", "エラー", "OK", "Error")
            return
        }

        # Create new project
        $newProject = [PSCustomObject]@{ 
            ProjectID      = [guid]::NewGuid().ToString()
            ProjectName    = $projectName
            ProjectDueDate = $null
            WorkFiles      = @()
            Notification   = "全体設定に従う"
        }
        $script:Projects += $newProject
        Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects

        # Create tasks from template
        $selectedTemplateName = $comboTemplate.SelectedItem
        $templateTasks = $templates.$selectedTemplateName
        
        $newTasks = @()
        foreach($templateTask in $templateTasks){
            $progress = $templateTask.進捗度
            if ([string]::IsNullOrEmpty($progress) -or $progress -eq "未着手") {
                $progress = "未実施"
            }

            $newTask = [PSCustomObject]@{ 
                "ID"                 = [guid]::NewGuid().ToString()
                "ProjectID"          = $newProject.ProjectID
                "期日"               = ""
                "優先度"             = $templateTask.優先度
                "タスク"             = $templateTask.タスク
                "進捗度"             = $progress
                "通知設定"           = "全体設定に従う"
                "カテゴリ"           = $templateTask.カテゴリ
                "サブカテゴリ"       = $templateTask.サブカテゴリ
                "保存日付"           = (Get-Date).ToString("yyyy-MM-dd")
                "TrackedTimeSeconds" = 0
                "WorkFiles"          = @()
            }
            $newTasks += $newTask
        }
        $form.Tag = $newTasks
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        Show-SaveFeedbackAndClose -FormToClose $form -Message "プロジェクトとタスクを作成しました"
    })

    $btnCancel.Add_Click({ $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Close() })

    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    if ($form.ShowDialog($parentForm) -eq [System.Windows.Forms.DialogResult]::OK) {
        # Return new tasks so the main script can add them to the global list
        return $form.Tag
    } else {
        return $null
    }
}

function Show-TemplateTaskInputForm {
    param([psobject]$existingTask = $null, [System.Windows.Forms.Form]$parentForm)
    $form = New-Object System.Windows.Forms.Form; $form.Width = 350; $form.Height = 350; $form.StartPosition = 'CenterParent'; $form.FormBorderStyle = 'FixedDialog'
    if ($existingTask) { $form.Text = "タスクの編集" } else { $form.Text = "タスクの追加" }
    
    # --- タスク内容 ---
    $labelTask=New-Object System.Windows.Forms.Label;$labelTask.Text="タスク内容：";$labelTask.Location="15, 15";$form.Controls.Add($labelTask)
    $textTask=New-Object System.Windows.Forms.TextBox;$textTask.Multiline=$true;$textTask.Location="15, 35";$textTask.Size="300, 60";$form.Controls.Add($textTask)
    
    # --- カテゴリ ---
    $labelCategory=New-Object System.Windows.Forms.Label;$labelCategory.Text="カテゴリ：";$labelCategory.Location="15, 110";$form.Controls.Add($labelCategory)
    $comboCategory=New-Object System.Windows.Forms.ComboBox;$comboCategory.Location="15, 130";$comboCategory.Size="140, 25";$comboCategory.DropDownStyle = "DropDownList";$form.Controls.Add($comboCategory)
    
    # --- サブカテゴリ ---
    $labelSubCategory=New-Object System.Windows.Forms.Label;$labelSubCategory.Text="サブカテゴリ：";$labelSubCategory.Location="175, 110";$form.Controls.Add($labelSubCategory)
    $comboSubCategory=New-Object System.Windows.Forms.ComboBox;$comboSubCategory.Location="175, 130";$comboSubCategory.Size="140, 25";$comboSubCategory.DropDownStyle = "DropDownList";$form.Controls.Add($comboSubCategory)

    # --- 優先度 ---
    $labelPriority=New-Object System.Windows.Forms.Label;$labelPriority.Text="優先度：";$labelPriority.Location="15, 170";$form.Controls.Add($labelPriority)
    $comboPriority=New-Object System.Windows.Forms.ComboBox;$comboPriority.DropDownStyle="DropDownList";$comboPriority.Items.AddRange(@("高","中","低"));$comboPriority.Location="15, 190";$comboPriority.Width=100;$form.Controls.Add($comboPriority)
    
    # --- 進捗度 ---
    $labelStatus=New-Object System.Windows.Forms.Label;$labelStatus.Text="進捗度：";$labelStatus.Location="175, 170";$form.Controls.Add($labelStatus)
    $comboStatus=New-Object System.Windows.Forms.ComboBox;$comboStatus.DropDownStyle="DropDownList";$comboStatus.Items.AddRange($script:TaskStatuses);$comboStatus.Location="175, 190";$comboStatus.Width=100;$form.Controls.Add($comboStatus)
    
    # --- ボタン ---
    $buttonSave=New-Object System.Windows.Forms.Button;$buttonSave.Text="OK";$buttonSave.Location="80, 250";$form.Controls.Add($buttonSave)
    $buttonCancel=New-Object System.Windows.Forms.Button;$buttonCancel.Text="キャンセル";$buttonCancel.Location="170, 250";$form.Controls.Add($buttonCancel)

    # --- イベントハンドラとデータロード ---
    $updateSubCategories = {
        $comboSubCategory.Items.Clear()
        $selectedCategory = $comboCategory.SelectedItem
        if ($null -eq $selectedCategory) { return } # Guard against null key
        if ($script:Categories.$selectedCategory) {
            $subCategories = $script:Categories.$selectedCategory.PSObject.Properties.Name
            if ($subCategories) {
                $comboSubCategory.Items.AddRange($subCategories)
            }
        }
    }
    $comboCategory.Add_SelectedIndexChanged($updateSubCategories)
    $comboCategory.Items.AddRange($script:Categories.PSObject.Properties.Name)



    if($existingTask){
        $textTask.Text = $existingTask.タスク
        $comboPriority.SelectedItem = $existingTask.優先度
        $comboStatus.SelectedItem = $existingTask.進捗度
        if ($existingTask.カテゴリ -and $comboCategory.Items.Contains($existingTask.カテゴリ)) {
            $comboCategory.SelectedItem = $existingTask.カテゴリ
            & $updateSubCategories
            if ($existingTask.サブカテゴリ -and $comboSubCategory.Items.Contains($existingTask.サブカテゴリ)) {
                $comboSubCategory.SelectedItem = $existingTask.サブカテゴリ
            }
        }
    } else {
        $comboPriority.SelectedIndex = 1
        $comboStatus.SelectedIndex = 0
    }
    
    $form.AcceptButton = $buttonSave; $form.CancelButton = $buttonCancel
    
    $buttonSave.Add_Click({
        if ([string]::IsNullOrWhiteSpace($textTask.Text)) { [System.Windows.Forms.MessageBox]::Show("タスク内容は必須です。", "入力エラー", "OK", "Error"); return }
        $form.Tag = [PSCustomObject]@{ 
            "タスク" = $textTask.Text.Trim()
            "優先度" = $comboPriority.SelectedItem
            "進捗度" = $comboStatus.SelectedItem
            "カテゴリ" = $comboCategory.SelectedItem
            "サブカテゴリ" = $comboSubCategory.SelectedItem
        }
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK; $form.Close()
    })
    $buttonCancel.Add_Click({ $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Close() })
    
    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    if ($form.ShowDialog($parentForm) -eq [System.Windows.Forms.DialogResult]::OK) { return $form.Tag } else { return $null }
}

function Show-CategoryEditorForm {
    param($parentForm)

    # Ensure Categories is a PSCustomObject
    if ($script:Categories -is [System.Collections.Hashtable]) {
        $script:Categories = [PSCustomObject]$script:Categories
    }

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "カテゴリ編集"
    $form.Width = 500
    $form.Height = 450
    $form.StartPosition = 'CenterParent'

    $splitContainer = New-Object System.Windows.Forms.SplitContainer
    $splitContainer.Dock = "Fill"
    $splitContainer.SplitterDistance =　70
    $form.Controls.Add($splitContainer)

    # --- Left: Categories ---
    $groupCat = New-Object System.Windows.Forms.GroupBox; $groupCat.Text = "カテゴリ"; $groupCat.Dock = "Fill"
    $splitContainer.Panel1.Controls.Add($groupCat)
    
    $listCat = New-Object System.Windows.Forms.ListBox; $listCat.Dock = "Fill"
    $groupCat.Controls.Add($listCat)

    $panelCatBtns = New-Object System.Windows.Forms.FlowLayoutPanel; $panelCatBtns.Dock = "Bottom"; $panelCatBtns.Height = 35
    $btnAddCat = New-Object System.Windows.Forms.Button; $btnAddCat.Text = "追加"; $btnAddCat.Width = 55
    $btnRenCat = New-Object System.Windows.Forms.Button; $btnRenCat.Text = "名前変更"; $btnRenCat.Width = 70
    $btnDelCat = New-Object System.Windows.Forms.Button; $btnDelCat.Text = "削除"; $btnDelCat.Width = 55
    $panelCatBtns.Controls.AddRange(@($btnAddCat, $btnRenCat, $btnDelCat))
    $groupCat.Controls.Add($panelCatBtns)

    # --- Right: SubCategories ---
    $groupSub = New-Object System.Windows.Forms.GroupBox; $groupSub.Text = "サブカテゴリ"; $groupSub.Dock = "Fill"
    $splitContainer.Panel2.Controls.Add($groupSub)

    $listSub = New-Object System.Windows.Forms.ListBox; $listSub.Dock = "Fill"
    $groupSub.Controls.Add($listSub)

    $panelSubBtns = New-Object System.Windows.Forms.FlowLayoutPanel; $panelSubBtns.Dock = "Bottom"; $panelSubBtns.Height = 35
    $btnAddSub = New-Object System.Windows.Forms.Button; $btnAddSub.Text = "追加"; $btnAddSub.Width = 55
    $btnRenSub = New-Object System.Windows.Forms.Button; $btnRenSub.Text = "名前変更"; $btnRenSub.Width = 70
    $btnDelSub = New-Object System.Windows.Forms.Button; $btnDelSub.Text = "削除"; $btnDelSub.Width = 55
    $panelSubBtns.Controls.AddRange(@($btnAddSub, $btnRenSub, $btnDelSub))
    $groupSub.Controls.Add($panelSubBtns)

    # --- Bottom: Save ---
    $panelBottom = New-Object System.Windows.Forms.Panel; $panelBottom.Dock = "Bottom"; $panelBottom.Height = 40
    $btnSave = New-Object System.Windows.Forms.Button; $btnSave.Text = "保存して閉じる"; $btnSave.Width = 100; $btnSave.Location = "190, 5"
    $panelBottom.Controls.Add($btnSave)
    $form.Controls.Add($panelBottom)

    # --- Logic ---
    $refreshCategories = {
        $listCat.Items.Clear()
        $listSub.Items.Clear()
        if ($script:Categories) {
            foreach ($prop in $script:Categories.PSObject.Properties) {
                $listCat.Items.Add($prop.Name) | Out-Null
            }
        }
    }

    $refreshSubCategories = {
        $listSub.Items.Clear()
        $selectedCat = $listCat.SelectedItem
        if ($selectedCat) {
            $subCatsObj = $script:Categories.$selectedCat
            if ($subCatsObj -is [PSCustomObject]) {
                foreach ($prop in $subCatsObj.PSObject.Properties) {
                    $listSub.Items.Add($prop.Name) | Out-Null
                }
            }
        }
    }

    $listCat.Add_SelectedIndexChanged({ & $refreshSubCategories })

    # Category Actions
    $btnAddCat.Add_Click({
        $newName = [Microsoft.VisualBasic.Interaction]::InputBox("新しいカテゴリ名を入力してください:", "カテゴリ追加")
        if (-not [string]::IsNullOrWhiteSpace($newName)) {
            if ($script:Categories.PSObject.Properties[$newName]) {
                [System.Windows.Forms.MessageBox]::Show("そのカテゴリは既に存在します。", "エラー", "OK", "Error")
            } else {
                $script:Categories | Add-Member -MemberType NoteProperty -Name $newName -Value (New-Object PSCustomObject)
                & $refreshCategories
                $listCat.SelectedItem = $newName
            }
        }
    })

    $btnRenCat.Add_Click({
        $oldName = $listCat.SelectedItem
        if ($null -eq $oldName) { return }
        $newName = [Microsoft.VisualBasic.Interaction]::InputBox("新しいカテゴリ名を入力してください:", "カテゴリ名変更", $oldName)
        if (-not [string]::IsNullOrWhiteSpace($newName) -and $newName -ne $oldName) {
            if ($script:Categories.PSObject.Properties[$newName]) {
                [System.Windows.Forms.MessageBox]::Show("そのカテゴリ名は既に存在します。", "エラー", "OK", "Error")
            } else {
                $content = $script:Categories.$oldName
                $script:Categories.PSObject.Properties.Remove($oldName)
                $script:Categories | Add-Member -MemberType NoteProperty -Name $newName -Value $content
                & $refreshCategories
                $listCat.SelectedItem = $newName
            }
        }
    })

    $btnDelCat.Add_Click({
        $name = $listCat.SelectedItem
        if ($null -eq $name) { return }
        if ([System.Windows.Forms.MessageBox]::Show("カテゴリ '$name' を削除しますか？`n含まれるサブカテゴリも削除されます。", "確認", "YesNo", "Warning") -eq "Yes") {
            $script:Categories.PSObject.Properties.Remove($name)
            & $refreshCategories
        }
    })

    # SubCategory Actions
    $btnAddSub.Add_Click({
        $catName = $listCat.SelectedItem
        if ($null -eq $catName) { return }
        $subName = [Microsoft.VisualBasic.Interaction]::InputBox("新しいサブカテゴリ名を入力してください:", "サブカテゴリ追加")
        if (-not [string]::IsNullOrWhiteSpace($subName)) {
            $catObj = $script:Categories.$catName
            # Ensure catObj is PSCustomObject
            if ($catObj -isnot [PSCustomObject]) {
                $catObj = New-Object PSCustomObject
                $script:Categories.$catName = $catObj
            }

            if ($catObj.PSObject.Properties[$subName]) {
                [System.Windows.Forms.MessageBox]::Show("そのサブカテゴリは既に存在します。", "エラー", "OK", "Error")
            } else {
                $catObj | Add-Member -MemberType NoteProperty -Name $subName -Value ""
                & $refreshSubCategories
            }
        }
    })

    $btnRenSub.Add_Click({
        $catName = $listCat.SelectedItem
        $oldSubName = $listSub.SelectedItem
        if ($null -eq $catName -or $null -eq $oldSubName) { return }
        
        $newSubName = [Microsoft.VisualBasic.Interaction]::InputBox("新しいサブカテゴリ名を入力してください:", "サブカテゴリ名変更", $oldSubName)
        if (-not [string]::IsNullOrWhiteSpace($newSubName) -and $newSubName -ne $oldSubName) {
            $catObj = $script:Categories.$catName
            if ($catObj.PSObject.Properties[$newSubName]) {
                [System.Windows.Forms.MessageBox]::Show("そのサブカテゴリ名は既に存在します。", "エラー", "OK", "Error")
            } else {
                $catObj.PSObject.Properties.Remove($oldSubName)
                $catObj | Add-Member -MemberType NoteProperty -Name $newSubName -Value ""
                & $refreshSubCategories
                $listSub.SelectedItem = $newSubName
            }
        }
    })

    $btnDelSub.Add_Click({
        $catName = $listCat.SelectedItem
        $subName = $listSub.SelectedItem
        if ($null -eq $catName -or $null -eq $subName) { return }
        if ([System.Windows.Forms.MessageBox]::Show("サブカテゴリ '$subName' を削除しますか？", "確認", "YesNo", "Warning") -eq "Yes") {
            $script:Categories.$catName.PSObject.Properties.Remove($subName)
            & $refreshSubCategories
        }
    })

    $btnSave.Add_Click({
        Save-DataFile -filePath $script:CategoriesFile -dataObject $script:Categories
        Show-SaveFeedbackAndClose -FormToClose $form -Message "カテゴリを保存しました"
    })

    & $refreshCategories
    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    $form.ShowDialog($parentForm)
}

function Show-TemplateEditorForm {
    param($parentForm)
    $editorForm = New-Object System.Windows.Forms.Form; $editorForm.Text = "テンプレートエディタ"; $editorForm.Width = 650; $editorForm.Height = 420; $editorForm.StartPosition = 'CenterParent'
    $labelTemplateList = New-Object System.Windows.Forms.Label; $labelTemplateList.Text = "テンプレート一覧:"; $labelTemplateList.Location = "10, 10"; $labelTemplateList.AutoSize = $true; $editorForm.Controls.Add($labelTemplateList)
    $listTemplates = New-Object System.Windows.Forms.ListBox; $listTemplates.Location = "10, 30"; $listTemplates.Size = "200, 280"; $editorForm.Controls.Add($listTemplates)
    $btnNewTemplate = New-Object System.Windows.Forms.Button; $btnNewTemplate.Text = "新規..."; $btnNewTemplate.Location = "10, 315"; $btnNewTemplate.Size="60, 25"; $editorForm.Controls.Add($btnNewTemplate)
    $btnRenameTemplate = New-Object System.Windows.Forms.Button; $btnRenameTemplate.Text = "名前変更..."; $btnRenameTemplate.Location = "75, 315"; $btnRenameTemplate.Size="70, 25"; $editorForm.Controls.Add($btnRenameTemplate)
    $btnDeleteTemplate = New-Object System.Windows.Forms.Button; $btnDeleteTemplate.Text = "削除"; $btnDeleteTemplate.Location = "150, 315"; $btnDeleteTemplate.Size="60, 25"; $editorForm.Controls.Add($btnDeleteTemplate)
    $labelTaskList = New-Object System.Windows.Forms.Label; $labelTaskList.Text = "テンプレート内のタスク:"; $labelTaskList.Location = "230, 10"; $labelTaskList.AutoSize = $true; $editorForm.Controls.Add($labelTaskList)
    $listTasks = New-Object System.Windows.Forms.ListView; $listTasks.View = 'Details'; $listTasks.Location = "230, 30"; $listTasks.Size = "380, 280"; $listTasks.FullRowSelect = $true
    $listTasks.Columns.Add("タスク", 220); $listTasks.Columns.Add("優先度", 70); $listTasks.Columns.Add("進捗度", 80)
    $editorForm.Controls.Add($listTasks) # この関数は古いデータ構造に依存しているため、将来的に大幅な改修が必要です
    $btnAddTask = New-Object System.Windows.Forms.Button; $btnAddTask.Text = "タスク追加..."; $btnAddTask.Location = "330, 315"; $btnAddTask.Size="80, 25"; $editorForm.Controls.Add($btnAddTask)
    $btnEditTask = New-Object System.Windows.Forms.Button; $btnEditTask.Text = "タスク編集..."; $btnEditTask.Location = "415, 315"; $btnEditTask.Size="80, 25"; $editorForm.Controls.Add($btnEditTask)
    $btnDeleteTask = New-Object System.Windows.Forms.Button; $btnDeleteTask.Text = "タスク削除"; $btnDeleteTask.Location = "500, 315"; $btnDeleteTask.Size="80, 25"; $editorForm.Controls.Add($btnDeleteTask)
    $btnSave = New-Object System.Windows.Forms.Button; $btnSave.Text = "保存して閉じる"; $btnSave.Location = "260, 350"; $btnSave.Size="120, 25"; $editorForm.Controls.Add($btnSave)
    $templates = if (Test-Path $script:TemplatesFile) { Get-Content $script:TemplatesFile -Raw -Encoding UTF8 | ConvertFrom-Json } else { New-Object PSCustomObject }
    $loadTemplates = {
        $listTemplates.Items.Clear()
        $templates.PSObject.Properties | ForEach-Object { $listTemplates.Items.Add($_.Name) }
        if($listTemplates.Items.Count -gt 0){ $listTemplates.SelectedIndex = 0 }
    }
    $listTemplates.Add_SelectedIndexChanged({
        $listTasks.Items.Clear()
        $selectedTemplateName = $listTemplates.SelectedItem
        if($selectedTemplateName -and $templates.$selectedTemplateName){
            foreach($task in $templates.$selectedTemplateName){
                $item = New-Object System.Windows.Forms.ListViewItem($task.タスク)
                $item.SubItems.Add($task.優先度)
                $item.SubItems.Add($task.進捗度)
                $item.Tag = $task
                $listTasks.Items.Add($item) | Out-Null
            }
        }
    })
    $btnNewTemplate.Add_Click({
        $newName = [Microsoft.VisualBasic.Interaction]::InputBox("新しいテンプレート名を入力してください:", "新規テンプレート")
        if(-not [string]::IsNullOrWhiteSpace($newName) -and -not $templates.PSObject.Properties[$newName]){
            $templates | Add-Member -MemberType NoteProperty -Name $newName -Value @()
            $loadTemplates.Invoke()
            $listTemplates.SelectedItem = $newName
        }
    })
    $btnRenameTemplate.Add_Click({
        $oldName = $listTemplates.SelectedItem
        if(-not $oldName){ return }
        $newName = [Microsoft.VisualBasic.Interaction]::InputBox("新しいテンプレート名を入力してください:", "名前の変更", $oldName)
        if(-not [string]::IsNullOrWhiteSpace($newName) -and $newName -ne $oldName -and -not $templates.PSObject.Properties[$newName]){
            $content = $templates.$oldName
            $templates.PSObject.Properties.Remove($oldName)
            $templates | Add-Member -MemberType NoteProperty -Name $newName -Value $content
            $loadTemplates.Invoke()
            $listTemplates.SelectedItem = $newName
        }
    })
    $btnDeleteTemplate.Add_Click({
        $nameToDelete = $listTemplates.SelectedItem
        if(-not $nameToDelete){ return }
        if([System.Windows.Forms.MessageBox]::Show("テンプレート '$($nameToDelete)' を削除しますか？", "確認", "YesNo", "Warning") -eq "Yes"){
            $templates.PSObject.Properties.Remove($nameToDelete)
            $loadTemplates.Invoke()
        }
    })
    $addTask = {
        $selectedTemplateName = $listTemplates.SelectedItem
        if(-not $selectedTemplateName){ return }
        $newTask = Show-TemplateTaskInputForm -parentForm $editorForm
        if($newTask){
            $currentTasks = @($templates.$selectedTemplateName)
            $currentTasks += $newTask
            $templates.$selectedTemplateName = $currentTasks
            $listTemplates.SelectedItem = $null; $listTemplates.SelectedItem = $selectedTemplateName
        }
    }
    $btnAddTask.Add_Click($addTask)
    
    $editTaskActionInTemplate = {
        $selectedTemplateName = $listTemplates.SelectedItem
        if(-not $selectedTemplateName -or $listTasks.SelectedItems.Count -eq 0){ return }
        $taskToEdit = $listTasks.SelectedItems[0].Tag
        $editedTask = Show-TemplateTaskInputForm -existingTask $taskToEdit -parentForm $editorForm
        if($editedTask){
            $taskToEdit.タスク = $editedTask.タスク
            $taskToEdit.優先度 = $editedTask.優先度
            $taskToEdit.進捗度 = $editedTask.進捗度
            $taskToEdit.カテゴリ = $editedTask.カテゴリ
            $taskToEdit.サブカテゴリ = $editedTask.サブカテゴリ
            $listTemplates.SelectedItem = $null; $listTemplates.SelectedItem = $selectedTemplateName
        }
    }
    $listTasks.Add_DoubleClick($editTaskActionInTemplate)
    $btnEditTask.Add_Click($editTaskActionInTemplate)
    
    $btnDeleteTask.Add_Click({
        $selectedTemplateName = $listTemplates.SelectedItem
        if(-not $selectedTemplateName -or $listTasks.SelectedItems.Count -eq 0){ return }
        if([System.Windows.Forms.MessageBox]::Show("選択したタスクを削除しますか？", "確認", "YesNo", "Warning") -eq "Yes"){
            $tasksToDelete = @($listTasks.SelectedItems | ForEach-Object { $_.Tag })
            $newTaskList = @($templates.$selectedTemplateName | Where-Object { $_ -notin $tasksToDelete })
            $templates.$selectedTemplateName = $newTaskList
            $listTemplates.SelectedItem = $null; $listTemplates.SelectedItem = $selectedTemplateName
        }
    })
    $btnSave.Add_Click({
        try {
            $templates | ConvertTo-Json -Depth 5 | Set-Content -Path $script:TemplatesFile -Encoding UTF8
            $editorForm.Tag = "RELOAD"
            Show-SaveFeedbackAndClose -FormToClose $editorForm -Message "テンプレートを保存しました"
        } catch {
            [System.Windows.Forms.MessageBox]::Show("テンプレートの保存中にエラーが発生しました`n$($_.Exception.Message)", "エラー", "OK", "Error")
        }
    })
    $loadTemplates.Invoke()
    Set-Theme -form $editorForm -IsDarkMode $script:isDarkMode
    $editorForm.ShowDialog($parentForm) | Out-Null
    return $editorForm.Tag
}

function Export-ReportToHtml {
    param(
        [array]$data,
        [double]$totalHours,
        [string]$filePath,
        [datetime]$startDate,
        [datetime]$endDate,
        [array]$tasks,
        [array]$projects
    )

    # --- 1. 時間分析データ (既存ロジック) ---
    $dailyProjectData = if ($data) { $data | Group-Object Date, ProjectName | Select-Object @{N='date';E={$_.Values[0].ToString('yyyy-MM-dd')}}, @{N='label';E={$_.Values[1]}}, @{N='value';E={($_.Group|Measure-Object -Property Hours -Sum).Sum}} } else { @() }
    $dailyCategoryData = if ($data) { $data | Group-Object Date, Category | Select-Object @{N='date';E={$_.Values[0].ToString('yyyy-MM-dd')}}, @{N='label';E={$_.Values[1]}}, @{N='value';E={($_.Group|Measure-Object -Property Hours -Sum).Sum}} } else { @() }
    $dailyStatusData = if ($data) { $data | Group-Object Date, Status | Select-Object @{N='date';E={$_.Values[0].ToString('yyyy-MM-dd')}}, @{N='label';E={$_.Values[1]}}, @{N='value';E={($_.Group|Measure-Object -Property Hours -Sum).Sum}} } else { @() }
    $projectSummary = if ($data) { $data | Group-Object -Property ProjectName | Select-Object @{N="label";E={$_.Name}}, @{N="value";E={($_.Group|Measure-Object -Property Hours -Sum).Sum}} } else { @() }
    $categorySummary = if ($data) { $data | Group-Object -Property Category | Select-Object @{N="label";E={$_.Name}}, @{N="value";E={($_.Group|Measure-Object -Property Hours -Sum).Sum}} } else { @() }
    $statusSummary = if ($data) { $data | Group-Object -Property Status | Select-Object @{N="label";E={$_.Name}}, @{N="value";E={($_.Group|Measure-Object -Property Hours -Sum).Sum}} } else { @() }

    # --- 2. 完了スピード分析データ (新規ロジック) ---
    # Corrected function call from Read-ArchivedTasksFromCsv to Read-TasksFromCsv
    $archivedTasks = Read-TasksFromCsv -filePath $script:ArchivedTasksFile
    $allPotentialTasks = $tasks + $archivedTasks
    
    $projectLookup = @{}; $projects | ForEach-Object { $projectLookup[$_.ProjectID] = $_.ProjectName }
    
    # --- 履歴ログの読み込みと準備 ---
    $logsByTask = @{}
    if (Test-Path $script:StatusLogsFile) {
        try {
            $rawLogs = Get-Content -Path $script:StatusLogsFile -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($rawLogs) {
                if ($rawLogs -isnot [array]) { $rawLogs = @($rawLogs) }
                $logsByTask = $rawLogs | Group-Object TaskID -AsHashTable -AsString
            }
        } catch {}
    }
    $excludeStatuses = if ($script:Settings.LeadTimeExcludeStatuses) { $script:Settings.LeadTimeExcludeStatuses } else { @() }

    $targetTasks = $allPotentialTasks | Where-Object {
        $_.進捗度 -eq '完了済み' -and 
        -not [string]::IsNullOrWhiteSpace($_.完了日) -and 
        -not [string]::IsNullOrWhiteSpace($_.保存日付)
    } | Where-Object {
        $completionDate = $null
        $isParsable = try { $completionDate = [datetime]$_.完了日; $true } catch { $false }
        $isParsable -and $completionDate.Date -ge $startDate.Date -and $completionDate.Date -le $endDate.Date
    } | ForEach-Object {
        try {
            $completionDate = [datetime]$_.完了日
            $creationDate = [datetime]$_.保存日付
            $days = ($completionDate - $creationDate).TotalDays
            if ($days -ge 0) {
            
            # 基本の所要時間
            $totalDuration = $completionDate - $creationDate
            $excludedDuration = [timespan]::Zero

            # 除外設定があり、かつログが存在する場合に計算
            if ($excludeStatuses.Count -gt 0 -and $logsByTask.ContainsKey($_.ID)) {
                $taskLogs = $logsByTask[$_.ID] | Sort-Object { [datetime]$_.Timestamp }
                
                # タイムラインの構築と除外時間の積算
                # 初期状態: 作成日時点でのステータス（最初のログのOldStatus、なければ"未実施"と仮定）
                $currentStatus = if ($taskLogs.Count -gt 0 -and $taskLogs[0].OldStatus) { $taskLogs[0].OldStatus } else { "未実施" }
                $lastTime = $creationDate

                foreach ($log in $taskLogs) {
                    $logTime = [datetime]$log.Timestamp
                    
                    # ログの日時が作成日より前（異常値）なら作成日に補正、完了日より後なら完了日に補正
                    if ($logTime -lt $creationDate) { $logTime = $creationDate }
                    if ($logTime -gt $completionDate) { $logTime = $completionDate }

                    # 前回の時点から今回のログまでの期間を計算
                    if ($logTime -gt $lastTime) {
                        if ($excludeStatuses -contains $currentStatus) {
                            $excludedDuration += ($logTime - $lastTime)
                        }
                    }

                    # ステータスと時点を更新
                    $currentStatus = $log.NewStatus
                    $lastTime = $logTime
                    
                    if ($lastTime -ge $completionDate) { break }
                }

                # 最後のログから完了日までの期間
                if ($lastTime -lt $completionDate) {
                    if ($excludeStatuses -contains $currentStatus) {
                        $excludedDuration += ($completionDate - $lastTime)
                    }
                }
            }

            # 実質日数の計算 (最低0日)
            $days = ($totalDuration - $excludedDuration).TotalDays
            if ($days -lt 0) { $days = 0 }
            
            if ($days -ge 0) { # 念のため再確認
                $_ | Add-Member -MemberType NoteProperty -Name 'CompletionDays' -Value $days -PassThru -Force
            }
            }
        } catch {
            # 日付変換エラーは無視
        }

    }

    $speedProjectData = $targetTasks | Where-Object { $_.ProjectID -and $projectLookup.ContainsKey($_.ProjectID) } | Group-Object ProjectID | ForEach-Object {
        [PSCustomObject]@{
            label = $projectLookup[$_.Name]
            value = ($_.Group | Measure-Object -Property CompletionDays -Average).Average
        }
    } | Sort-Object value -Descending
    
    $speedCategoryData = $targetTasks | Where-Object { -not [string]::IsNullOrWhiteSpace($_.カテゴリ) } | Group-Object カテゴリ | ForEach-Object {
        [PSCustomObject]@{
            label = $_.Name
            value = ($_.Group | Measure-Object -Property CompletionDays -Average).Average
        }
    } | Sort-Object value -Descending

    # --- 3. JSONデータ生成 ---
    $jsonDailyProjectData = $dailyProjectData | ConvertTo-Json -Compress
    $jsonDailyCategoryData = $dailyCategoryData | ConvertTo-Json -Compress
    $jsonDailyStatusData = $dailyStatusData | ConvertTo-Json -Compress
    $jsonProjectSummary = $projectSummary | ConvertTo-Json -Compress
    $jsonCategorySummary = $categorySummary | ConvertTo-Json -Compress
    $jsonStatusSummary = $statusSummary | ConvertTo-Json -Compress
    $jsonSpeedProjectData = $speedProjectData | ConvertTo-Json -Compress
    $jsonSpeedCategoryData = $speedCategoryData | ConvertTo-Json -Compress

    $totalMinutes = $totalHours * 60
    $totalTimeStr = "$([math]::Floor($totalMinutes / 60))<small>時間</small> $([math]::Floor($totalMinutes % 60))<small>分</small>"

    # --- 4. HTML生成 (テンプレート利用) ---
    try {
        $templatePath = Join-Path -Path $script:AppRoot -ChildPath "report_template.html"
        if (-not (Test-Path $templatePath)) {
            [System.Windows.Forms.MessageBox]::Show("レポートテンプレート 'report_template.html' が見つかりません。", "エクスポートエラー", "OK", "Error")
            return $false
        }

        $htmlContent = (Get-Content -Path $templatePath -Raw -Encoding UTF8) `
            -replace '__START_DATE__', $startDate.ToString("yyyy/MM/dd") `
            -replace '__END_DATE__', $endDate.ToString("yyyy/MM/dd") `
            -replace '__TOTAL_TIME__', $totalTimeStr `
            -replace '__JSON_DAILY_PROJECT_DATA__', $jsonDailyProjectData `
            -replace '__JSON_DAILY_CATEGORY_DATA__', $jsonDailyCategoryData `
            -replace '__JSON_DAILY_STATUS_DATA__', $jsonDailyStatusData `
            -replace '__JSON_PROJECT_SUMMARY__', $jsonProjectSummary `
            -replace '__JSON_CATEGORY_SUMMARY__', $jsonCategorySummary `
            -replace '__JSON_STATUS_SUMMARY__', $jsonStatusSummary `
            -replace '__JSON_SPEED_PROJECT_DATA__', $jsonSpeedProjectData `
            -replace '__JSON_SPEED_CATEGORY_DATA__', $jsonSpeedCategoryData

        Set-Content -Path $filePath -Value $htmlContent -Encoding UTF8 -Force
        return $true
    } 
    catch {
        [System.Windows.Forms.MessageBox]::Show("HTMLレポートの生成に失敗しました: `n$($_.Exception.Message)", "エクスポートエラー", "OK", "Error")
        return $false
    }
}



function Show-ReportForm {
    param($parentForm)

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Windows.Forms.DataVisualization

    # --- 内部関数: 安全なチャート作成 ---
    function New-ReportChart { # PSScriptAnalyzerの警告を避けるため動詞を変更
        param([string]$name, [string]$title)
        $chart = New-Object System.Windows.Forms.DataVisualization.Charting.Chart
        $chart.Dock = "Fill"
        $chart.Name = $name
        
        $ca = New-Object System.Windows.Forms.DataVisualization.Charting.ChartArea
        $ca.Name = "MainArea"
        $ca.AxisX.LabelStyle.Angle = -45
        $ca.AxisX.Interval = 1
        $ca.AxisX.MajorGrid.LineColor = [System.Drawing.Color]::Gainsboro
        $ca.AxisY.MajorGrid.LineColor = [System.Drawing.Color]::Gainsboro
        $chart.ChartAreas.Add($ca)

        $t = $chart.Titles.Add($title)
        $t.Font = New-Object System.Drawing.Font("Meiryo UI", 10, [System.Drawing.FontStyle]::Bold)
        
        $lg = New-Object System.Windows.Forms.DataVisualization.Charting.Legend
        $lg.Name = "Default"
        $lg.Docking = [System.Windows.Forms.DataVisualization.Charting.Docking]::Top
        $lg.Font = New-Object System.Drawing.Font("Meiryo UI", 8)
        $chart.Legends.Add($lg)

        if ($script:isDarkMode) {
            $chart.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 30)
            $chart.ForeColor = [System.Drawing.Color]::White
            $ca.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 30)
            $ca.AxisX.LabelStyle.ForeColor = [System.Drawing.Color]::White
            $ca.AxisY.LabelStyle.ForeColor = [System.Drawing.Color]::White
            $ca.AxisX.LineColor = [System.Drawing.Color]::Gray
            $ca.AxisY.LineColor = [System.Drawing.Color]::Gray
            $ca.AxisX.MajorGrid.LineColor = [System.Drawing.Color]::FromArgb(60, 60, 60)
            $ca.AxisY.MajorGrid.LineColor = [System.Drawing.Color]::FromArgb(60, 60, 60)
            $t.ForeColor = [System.Drawing.Color]::White
            $lg.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 30)
            $lg.ForeColor = [System.Drawing.Color]::White
        }

        return $chart
    }

    # --- フォームとUIコントロールの作成 ---
    $reportForm = New-Object System.Windows.Forms.Form
    $reportForm.Text = "レポート分析"
    $reportForm.Width = 1200; $reportForm.Height = 850; $reportForm.StartPosition = 'CenterParent'; $reportForm.MinimumSize = New-Object System.Drawing.Size(800, 600)

    # 開始日の自動設定（最古のログを探す）
    $defaultStartDate = (Get-Date).Date
    if ($script:AllTimeLogs -and $script:AllTimeLogs.Count -gt 0) {
        $sortedLogs = $script:AllTimeLogs | Where-Object { $_.StartTime } | Sort-Object { [datetime]$_.StartTime }
        if ($sortedLogs) {
            $firstLog = $sortedLogs | Select-Object -First 1
            if ($firstLog.StartTime) {
                try { $defaultStartDate = ([datetime]$firstLog.StartTime).Date } catch {}
            }
        }
    }

    # 上部パネル
    $topPanel = New-Object System.Windows.Forms.Panel; $topPanel.Dock = "Top"; $topPanel.Height = 40
    $lblStartDate = New-Object System.Windows.Forms.Label; $lblStartDate.Text = "開始日:"; $lblStartDate.Location = "10, 12"; $lblStartDate.AutoSize = $true
    $dtpStart = New-Object System.Windows.Forms.DateTimePicker; $dtpStart.Location = "60, 10"; $dtpStart.Width = 120; $dtpStart.Value = $defaultStartDate
    $lblEndDate = New-Object System.Windows.Forms.Label; $lblEndDate.Text = "終了日:"; $lblEndDate.Location = "190, 12"; $lblEndDate.AutoSize = $true
    $dtpEnd = New-Object System.Windows.Forms.DateTimePicker; $dtpEnd.Location = "240, 10"; $dtpEnd.Width = 120
    $btnGenerate = New-Object System.Windows.Forms.Button; $btnGenerate.Text = "レポート生成"; $btnGenerate.Location = "380, 8"; $btnGenerate.Size = "100, 28"
    $btnExportHtml = New-Object System.Windows.Forms.Button; $btnExportHtml.Text = "HTMLへエクスポート"; $btnExportHtml.Location = "490, 8"; $btnExportHtml.Size = "130, 28"; $btnExportHtml.Enabled = $false
    $lblTotalTime = New-Object System.Windows.Forms.Label; $lblTotalTime.Name = "lblTotalTime"; $lblTotalTime.Text = "総時間: 0時間0分"; $lblTotalTime.Location = "630, 12"; $lblTotalTime.AutoSize = $true
    $lblTotalTime.Font = New-Object System.Drawing.Font("Meiryo UI", 10, [System.Drawing.FontStyle]::Bold)
    $topPanel.Controls.AddRange(@($lblStartDate, $dtpStart, $lblEndDate, $dtpEnd, $btnGenerate, $btnExportHtml, $lblTotalTime))

    # メインタブ
    $mainTabControl = New-Object System.Windows.Forms.TabControl; $mainTabControl.Dock = "Fill"
    $mainTabControl.DrawMode = [System.Windows.Forms.TabDrawMode]::OwnerDrawFixed
    $mainTabControl.SizeMode = [System.Windows.Forms.TabSizeMode]::Fixed
    $mainTabControl.ItemSize = New-Object System.Drawing.Size(150, 30)
    $mainTabControl.Add_DrawItem({
        param($source, $e)
        $g = $e.Graphics
        $tabs = $source
        if ($e.Index -ge $tabs.TabPages.Count) { return }
        $tabPage = $tabs.TabPages[$e.Index]
        $tabRect = $tabs.GetTabRect($e.Index)
        
        $isSelected = ($e.State -band [System.Windows.Forms.DrawItemState]::Selected) -eq [System.Windows.Forms.DrawItemState]::Selected
        
        $colors = Get-ThemeColors -IsDarkMode $script:isDarkMode
        $bgColor = if ($isSelected) { $colors.ControlBack } else { $colors.BackColor }
        $textColor = $colors.ForeColor
        
        $bgBrush = New-Object System.Drawing.SolidBrush($bgColor)
        $textBrush = New-Object System.Drawing.SolidBrush($textColor)
        
        $g.FillRectangle($bgBrush, $tabRect)
        $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'; $sf.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap
        $g.DrawString($tabPage.Text, $tabs.Font, $textBrush, [System.Drawing.RectangleF]$tabRect, $sf)
        
        $bgBrush.Dispose(); $textBrush.Dispose(); $sf.Dispose()
    })
    
    # --- タブ1: 日別推移 ---
    $tabDaily = New-Object System.Windows.Forms.TabPage "日別推移"
    $dailyMainLayout = New-Object System.Windows.Forms.TableLayoutPanel; $dailyMainLayout.Dock = "Fill"; $dailyMainLayout.RowCount = 2
    $dailyMainLayout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize)))
    $dailyMainLayout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 100)))

    $radioLine = New-Object System.Windows.Forms.RadioButton; $radioLine.Text = "📈 折れ線グラフ"; $radioLine.Checked = $true; $radioLine.AutoSize = $true
    $radioStacked = New-Object System.Windows.Forms.RadioButton; $radioStacked.Text = "📊 積み上げ棒グラフ"; $radioStacked.AutoSize = $true
    $radioFlowPanel = New-Object System.Windows.Forms.FlowLayoutPanel; $radioFlowPanel.Dock = "Top"; $radioFlowPanel.AutoSize = $true
    $radioFlowPanel.Controls.AddRange(@($radioLine, $radioStacked))
    $dailyMainLayout.Controls.Add($radioFlowPanel, 0, 0)
    
    $dailyChartLayout = New-Object System.Windows.Forms.TableLayoutPanel; $dailyChartLayout.Dock = "Fill"; $dailyChartLayout.RowCount = 3
    (1..3) | ForEach-Object { $dailyChartLayout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 33.33))) }
    $chartDailyProject = New-ReportChart "chartDailyProject" "プロジェクト別"
    $chartDailyCategory = New-ReportChart "chartDailyCategory" "カテゴリ別"
    $chartDailyStatus = New-ReportChart "chartDailyStatus" "ステータス別"
    $dailyChartLayout.Controls.AddRange(@($chartDailyProject, $chartDailyCategory, $chartDailyStatus))
    $dailyMainLayout.Controls.Add($dailyChartLayout, 0, 1)
    $tabDaily.Controls.Add($dailyMainLayout)

    # --- タブ2: 合計時間 ---
    $tabTotal = New-Object System.Windows.Forms.TabPage "合計時間"
    $totalLayout = New-Object System.Windows.Forms.TableLayoutPanel; $totalLayout.Dock = "Fill"; $totalLayout.ColumnCount = 3
    (1..3) | ForEach-Object { $totalLayout.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 33.33))) }
    $chartTotalProjectBar = New-ReportChart "chartTotalProjectBar" "棒グラフ"; $chartTotalProjectPie = New-ReportChart "chartTotalProjectPie" "円グラフ"
    $chartTotalCategoryBar = New-ReportChart "chartTotalCategoryBar" "棒グラフ"; $chartTotalCategoryPie = New-ReportChart "chartTotalCategoryPie" "円グラフ"
    $chartTotalStatusBar = New-ReportChart "chartTotalStatusBar" "棒グラフ"; $chartTotalStatusPie = New-ReportChart "chartTotalStatusPie" "円グラフ"
    @($chartTotalProjectBar, $chartTotalCategoryBar, $chartTotalStatusBar) | ForEach-Object { $_.Legends[0].Enabled = $false }
    $groupProject = New-Object System.Windows.Forms.GroupBox; $groupProject.Text = "プロジェクト別"; $groupProject.Dock = "Fill"
    $splitProject = New-Object System.Windows.Forms.SplitContainer; $splitProject.Dock = "Fill"; $splitProject.Orientation = "Horizontal"
    $splitProject.Panel1.Controls.Add($chartTotalProjectBar); $splitProject.Panel2.Controls.Add($chartTotalProjectPie)
    $groupProject.Controls.Add($splitProject); $totalLayout.Controls.Add($groupProject, 0, 0)
    $groupCategory = New-Object System.Windows.Forms.GroupBox; $groupCategory.Text = "カテゴリ別"; $groupCategory.Dock = "Fill"
    $splitCategory = New-Object System.Windows.Forms.SplitContainer; $splitCategory.Dock = "Fill"; $splitCategory.Orientation = "Horizontal"
    $splitCategory.Panel1.Controls.Add($chartTotalCategoryBar); $splitCategory.Panel2.Controls.Add($chartTotalCategoryPie)
    $groupCategory.Controls.Add($splitCategory); $totalLayout.Controls.Add($groupCategory, 1, 0)
    $groupStatus = New-Object System.Windows.Forms.GroupBox; $groupStatus.Text = "ステータス別"; $groupStatus.Dock = "Fill"
    $splitStatus = New-Object System.Windows.Forms.SplitContainer; $splitStatus.Dock = "Fill"; $splitStatus.Orientation = "Horizontal"
    $splitStatus.Panel1.Controls.Add($chartTotalStatusBar); $splitStatus.Panel2.Controls.Add($chartTotalStatusPie)
    $groupStatus.Controls.Add($splitStatus); $totalLayout.Controls.Add($groupStatus, 2, 0)
    $tabTotal.Controls.Add($totalLayout)

    # --- タブ3: 完了スピード ---
    $tabSpeed = New-Object System.Windows.Forms.TabPage "⏱️ 完了スピード"
    $speedLayout = New-Object System.Windows.Forms.TableLayoutPanel; $speedLayout.Dock = "Fill"; $speedLayout.ColumnCount = 2
    $speedLayout.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 50)))
    $speedLayout.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 50)))
    $chartSpeedProject = New-ReportChart "chartSpeedProject" "プロジェクト別 平均完了日数 (日)"
    $chartSpeedCategory = New-ReportChart "chartSpeedCategory" "カテゴリ別 平均完了日数 (日)"
    # Change to Bar chart and adjust axis
    @($chartSpeedProject, $chartSpeedCategory) | ForEach-Object {
        $_.Series.Clear()
        $_.ChartAreas[0].AxisX.LabelStyle.Angle = 0
        $_.Legends[0].Enabled = $false
    }
    $speedLayout.Controls.Add($chartSpeedProject, 0, 0)
    $speedLayout.Controls.Add($chartSpeedCategory, 1, 0)
    $tabSpeed.Controls.Add($speedLayout)

    $mainTabControl.TabPages.AddRange(@($tabDaily, $tabTotal, $tabSpeed))
    
    # --- アドバイス表示用エリア (SplitContainerで下部に追加) ---
    $reportSplitContainer = New-Object System.Windows.Forms.SplitContainer
    $reportSplitContainer.Dock = "Fill"
    $reportSplitContainer.Orientation = "Horizontal"
    $reportSplitContainer.SplitterDistance = 550
    
    $reportSplitContainer.Panel1.Controls.Add($mainTabControl)
    
    $grpInsights = New-Object System.Windows.Forms.GroupBox
    $grpInsights.Text = "💡 分析とアドバイス"
    $grpInsights.Dock = "Fill"
    
    $txtInsights = New-Object System.Windows.Forms.TextBox
    $txtInsights.Multiline = $true; $txtInsights.ScrollBars = "Vertical"; $txtInsights.ReadOnly = $true; $txtInsights.Dock = "Fill"; $txtInsights.Font = New-Object System.Drawing.Font("Meiryo UI", 10)
    
    $grpInsights.Controls.Add($txtInsights)
    $reportSplitContainer.Panel2.Controls.Add($grpInsights)
    
    $reportForm.Controls.AddRange(@($reportSplitContainer, $topPanel))
    
    $allCharts = @($chartDailyProject, $chartDailyCategory, $chartDailyStatus, $chartTotalProjectBar, $chartTotalProjectPie, $chartTotalCategoryBar, $chartTotalCategoryPie, $chartTotalStatusBar, $chartTotalStatusPie, $chartSpeedProject, $chartSpeedCategory)

    # --- レポート生成ロジック ---
    $reportData = $null # レポートデータを保持する変数
    $generateReport = {
        param([switch]$isManualTrigger)
        
        $btnExportHtml.Enabled = $false # 生成中は無効化
        $currentChartType = if ($radioStacked.Checked) { [System.Windows.Forms.DataVisualization.Charting.SeriesChartType]::StackedColumn } else { [System.Windows.Forms.DataVisualization.Charting.SeriesChartType]::Line }

        $allCharts | ForEach-Object { $_.Series.Clear() }
        $startDate = $dtpStart.Value.Date
        $endDate = $dtpEnd.Value.Date
        
        # --- データ処理の高速化 ---
        $taskLookup = @{}; $script:AllTasks | ForEach-Object { $taskLookup[$_.ID] = $_ }
        $projectLookup = @{}; $script:Projects | ForEach-Object { $projectLookup[$_.ProjectID] = $_.ProjectName }

        $filteredData = $script:AllTimeLogs | Where-Object {
            $_.TaskID -and $_.StartTime -and $_.EndTime -and 
            ([datetime]$_.StartTime).Date -le $endDate -and 
            ([datetime]$_.EndTime).Date -ge $startDate 
        } | ForEach-Object {
            $task = $taskLookup[$_.TaskID]
            if ($task -and $task.ProjectID -and $projectLookup.ContainsKey($task.ProjectID)) {
                [PSCustomObject]@{
                    ProjectName = $projectLookup[$task.ProjectID]
                    Category    = if ([string]::IsNullOrWhiteSpace($task.カテゴリ)) { "(未分類)" } else { $task.カテゴリ }
                    Status      = if ([string]::IsNullOrWhiteSpace($task.進捗度)) { "(未設定)" } else { $task.進捗度 }
                    Hours       = ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalHours
                    Date        = ([datetime]$_.StartTime).Date
                }
            }
        } | Where-Object { $_ }

        # 生成されたデータを変数に格納
        $script:reportData = @{
            StartDate = $startDate
            EndDate = $endDate
            TotalHours = ($filteredData | Measure-Object -Property 'Hours' -Sum).Sum
            Data = $filteredData
        }

        # 総時間表示 (データがない場合も考慮)
        $totalMinutes = $script:reportData.TotalHours * 60
        $lblTotalTime.Text = "総時間: $([int]($totalMinutes / 60))時間$([int]($totalMinutes % 60))分"

        # --- アドバイスの生成と表示 ---
        try {
            $groupedCat = @($script:reportData.Data | Group-Object Category | Select-Object Name, @{N='TotalSeconds';E={($_.Group | Measure-Object -Property Hours -Sum).Sum * 3600}})
            $totalSec = $script:reportData.TotalHours * 3600
            
            $insightData = [PSCustomObject]@{
                GroupedByCategory = $groupedCat
                TotalLoggedTimeSeconds = $totalSec
            }
            $txtInsights.Text = Get-ReportInsights -ReportData $insightData
        } catch {
            $txtInsights.Text = "アドバイスの生成中にエラーが発生しました: $($_.Exception.Message)"
        }

        if ($script:reportData.Data.Count -eq 0) {
             if ($isManualTrigger) { 
                #[System.Windows.Forms.MessageBox]::Show("対象期間に表示する実績データがありません。", "情報", "OK", "Information") 
             }
        }
        
        $dateRange = @(); for ($d = $startDate; $d -le $endDate; $d = $d.AddDays(1)) { $dateRange += $d }
        
        $populateTrendChart = {
            param($chart, [string]$groupByProperty)
            $groupedData = $script:reportData.Data | Group-Object -Property $groupByProperty, Date
            $allKeys = ($script:reportData.Data | Select-Object -Property $groupByProperty -Unique).$groupByProperty | Sort-Object
            foreach ($key in $allKeys) {
                $series = $chart.Series.Add($key)
                $series.ChartType = $currentChartType
                if ($currentChartType -eq [System.Windows.Forms.DataVisualization.Charting.SeriesChartType]::Line) { $series.BorderWidth = 2 }
                $hoursByDate = $groupedData | Where-Object { $_.Values[0] -eq $key } | Group-Object -Property { $_.Values[1] } -AsHashTable
                foreach ($day in $dateRange) {
                    $hoursOnDay = if ($hoursByDate.ContainsKey($day)) { ($hoursByDate[$day].Group | Measure-Object -Property Hours -Sum).Sum } else { 0 }
                    $series.Points.AddXY($day.ToString("MM/dd"), $hoursOnDay)
                }
            }
        }
        & $populateTrendChart $chartDailyProject "ProjectName"
        & $populateTrendChart $chartDailyCategory "Category"
        & $populateTrendChart $chartDailyStatus "Status"
        
        $populateTotalTimeCharts = {
            param($chartBar, $chartPie, [string]$groupByProperty)
            $summary = $script:reportData.Data | Group-Object -Property $groupByProperty | Select-Object @{N="Name";E={$_.Name}}, @{N="Sum";E={($_.Group|Measure-Object -Property Hours -Sum).Sum}} | Sort-Object -Property Sum -Descending
            $seriesBar = $chartBar.Series.Add("s"); $seriesBar.ChartType = "Column"
            $seriesPie = $chartPie.Series.Add("s"); $seriesPie.ChartType = "Pie"; $seriesPie["PieLabelStyle"] = "Outside"; $seriesPie.Label = "#VALX (#PERCENT{P1})"; 
            $seriesPie.LabelForeColor = (Get-ThemeColors -IsDarkMode $script:isDarkMode).ForeColor
            foreach ($item in $summary) {
                $val = if ($item.Sum) { [double]$item.Sum } else { 0 }
                if ($val -gt 0) {
                    $pt = $seriesBar.Points.AddXY($item.Name, $val)
                    $seriesBar.Points[$pt].IsValueShownAsLabel = $true
                    $seriesBar.Points[$pt].LabelForeColor = (Get-ThemeColors -IsDarkMode $script:isDarkMode).ForeColor
                    $seriesBar.Points[$pt].LabelFormat = "F2"
                    $seriesPie.Points.AddXY($item.Name, $val)
                }
            }
        }
        & $populateTotalTimeCharts $chartTotalProjectBar $chartTotalProjectPie "ProjectName"
        & $populateTotalTimeCharts $chartTotalCategoryBar $chartTotalCategoryPie "Category"
        & $populateTotalTimeCharts $chartTotalStatusBar $chartTotalStatusPie "Status"
        
        # --- 完了スピードの計算と描画 ---
        $archivedTasks = Read-TasksFromCsv -filePath $script:ArchivedTasksFile
        $allPotentialTasks = $script:AllTasks + $archivedTasks
        
        $targetTasks = $allPotentialTasks | Where-Object {
            $_.進捗度 -eq '完了済み' -and 
            -not [string]::IsNullOrWhiteSpace($_.完了日) -and 
            -not [string]::IsNullOrWhiteSpace($_.保存日付)
        } | Where-Object {
            $completionDate = $null
            $isParsable = try { $completionDate = [datetime]$_.完了日; $true } catch { $false }
            $isParsable -and $completionDate.Date -ge $startDate.Date -and $completionDate.Date -le $endDate.Date
        } | ForEach-Object {
            try {
                $completionDate = [datetime]$_.完了日
                $creationDate = [datetime]$_.保存日付
                $days = ($completionDate - $creationDate).TotalDays
                if ($days -ge 0) {
                    $_ | Add-Member -MemberType NoteProperty -Name 'CompletionDays' -Value $days -PassThru -Force
                }
            } catch { }
        }

        $populateSpeedChart = {
            param($chart, [string]$groupByProperty, $lookupTable = $null)
            $chart.Series.Clear()
            $series = $chart.Series.Add("s")
            $series.ChartType = [System.Windows.Forms.DataVisualization.Charting.SeriesChartType]::Bar
            $series.IsValueShownAsLabel = $true
            $series.LabelForeColor = (Get-ThemeColors -IsDarkMode $script:isDarkMode).ForeColor
            $series.LabelFormat = "F1"
            
            $data = $targetTasks | Where-Object { $_.$groupByProperty -and (-not [string]::IsNullOrWhiteSpace($_.$groupByProperty)) } | Group-Object $groupByProperty
            $avgData = foreach($group in $data) {
                $label = if ($lookupTable) { $lookupTable[$group.Name] } else { $group.Name }
                if ($label) {
                    [PSCustomObject]@{
                        Label = $label
                        Value = ($group.Group | Measure-Object -Property CompletionDays -Average).Average
                    }
                }
            }
            foreach ($item in ($avgData | Sort-Object Value -Descending)) {
                 $series.Points.AddXY($item.Label, $item.Value)
            }
        }
        & $populateSpeedChart $chartSpeedProject "ProjectID" $projectLookup
        & $populateSpeedChart $chartSpeedCategory "カテゴリ"

        $allCharts | ForEach-Object { $_.Invalidate() }
        $btnExportHtml.Enabled = $true # データがあればエクスポートボタンを有効化
    }

    # --- イベントハンドラの登録 ---
    $btnGenerate.Add_Click({ & $generateReport -isManualTrigger:$true })

    $btnExportHtml.Add_Click({
        if ($null -eq $script:reportData) {
            [System.Windows.Forms.MessageBox]::Show("エクスポートするデータがありません。先にレポートを生成してください。", "情報", "OK", "Information")
            return
        }

        $timestamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
        $fileName = "Report_$($timestamp).html"
        $filePath = Join-Path -Path $PSScriptRoot -ChildPath $fileName

        # Export-ReportToHtml に渡すデータを、実績時間データ($script:reportData.Data)に限定する
        $success = Export-ReportToHtml `
            -startDate $script:reportData.StartDate `
            -endDate $script:reportData.EndDate `
            -totalHours $script:reportData.TotalHours `
            -data $script:reportData.Data `
            -filePath $filePath `
            -projects $script:Projects `
            -tasks $script:AllTasks

        if ($success) {
            $result = [System.Windows.Forms.MessageBox]::Show("レポートが正常にエクスポートされました。`n`n$filePath`n`nファイルを開きますか？", "エクスポート完了", "YesNo", "Information")
            if ($result -eq 'Yes') {
                try {
                    Start-Process -FilePath $filePath
                } catch {
                    [System.Windows.Forms.MessageBox]::Show("ファイルを開けませんでした: `n$($_.Exception.Message)", "エラー", "OK", "Error")
                }
            }
        }
    })
    
    $radio_CheckedChanged = { 
        if ($this.Checked) { 
            try { & $generateReport } catch { Write-Warning "グラフの再描画に失敗しました: $($_.Exception.Message)" }
        } 
    }
    $radioLine.Add_CheckedChanged($radio_CheckedChanged)
    $radioStacked.Add_CheckedChanged($radio_CheckedChanged)

    $reportForm.Add_Shown({
        try { & $generateReport } catch { 
            [System.Windows.Forms.MessageBox]::Show("初期描画エラー: $($_.Exception.Message)", "エラー", "OK", "Warning") 
        }
    })
    
    Set-Theme -form $reportForm -IsDarkMode $script:isDarkMode
    $reportForm.ShowDialog($parentForm)
}

function Get-ProjectNameById {
    param([string]$ProjectID)
    $project = $script:Projects | Where-Object { $_.ProjectID -eq $ProjectID } | Select-Object -First 1
    if ($project) { return $project.ProjectName } else { return "(未設定)" }
}




function Initialize-TaskDrag {
    $itemMouseDown = {
        param($s, $e)
        if ($e.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
        
        $task = $null
        if ($s -is [System.Windows.Forms.DataGridView]) {
            $hitTest = $s.HitTest($e.X, $e.Y)
            if ($hitTest.RowIndex -ge 0) {
                $row = $s.Rows[$hitTest.RowIndex]
                # Only allow dragging tasks, not projects
                if ($row.Tag -and $row.Tag.PSObject.Properties.Name.Contains('ID')) {
                    $task = $row.Tag
                }
            }
        } elseif ($s -is [System.Windows.Forms.ListBox]) {
            $index = $s.IndexFromPoint($e.Location)
            if ($index -ne [System.Windows.Forms.ListBox]::NoMatches) {
                $task = $s.Items[$index]
            }
        }

        if ($task) {
            [void]$s.DoDragDrop($task, [System.Windows.Forms.DragDropEffects]::Move)
        }
    }

    # Attach MouseDown to the DataGridView
    $script:taskDataGridView.Add_MouseDown($itemMouseDown)

    # Attach MouseDown to Kanban list boxes
    if ($script:kanbanLists) {
        foreach($listBox in $script:kanbanLists.Values) {
            $listBox.Add_MouseDown($itemMouseDown)
        }
    }
}

# DataGridViewの列を初期化する新しい関数
function Initialize-DataGridViewColumns {
    param([System.Windows.Forms.DataGridView]$dataGridView)

    $dataGridView.Columns.Clear()
    
    $dataGridView.DefaultCellStyle.Alignment = [System.Windows.Forms.DataGridViewContentAlignment]::MiddleLeft

    # 1. タスク/プロジェクト
    $nameCol = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $nameCol.Name = "Name"
    $nameCol.HeaderText = "タスク/プロジェクト"
    $nameCol.AutoSizeMode = [System.Windows.Forms.DataGridViewAutoSizeColumnMode]::Fill
    $nameCol.FillWeight = 40
    $nameCol.MinimumWidth = 250
    $dataGridView.Columns.Add($nameCol) | Out-Null

    # 2. 期日
    $dueCol = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $dueCol.Name = "DueDate"
    $dueCol.HeaderText = "期日"
    $dueCol.AutoSizeMode = [System.Windows.Forms.DataGridViewAutoSizeColumnMode]::AllCells
    $dueCol.MinimumWidth = 100
    $dataGridView.Columns.Add($dueCol) | Out-Null

    # 3. 進捗
    $progressCol = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $progressCol.Name = "Progress"
    $progressCol.HeaderText = "進捗"
    $progressCol.AutoSizeMode = [System.Windows.Forms.DataGridViewAutoSizeColumnMode]::AllCells
    $progressCol.MinimumWidth = 100
    $dataGridView.Columns.Add($progressCol) | Out-Null

    # 4. 優先度
    $priorityCol = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $priorityCol.Name = "Priority"
    $priorityCol.HeaderText = "優先度"
    $priorityCol.AutoSizeMode = [System.Windows.Forms.DataGridViewAutoSizeColumnMode]::AllCells
    $priorityCol.MinimumWidth = 60
    $dataGridView.Columns.Add($priorityCol) | Out-Null

    # 5. 実績
    $trackedCol = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $trackedCol.Name = "TrackedTime"
    $trackedCol.HeaderText = "実績"
    $trackedCol.AutoSizeMode = [System.Windows.Forms.DataGridViewAutoSizeColumnMode]::AllCells
    $trackedCol.MinimumWidth = 80
    $dataGridView.Columns.Add($trackedCol) | Out-Null

    # 6. カテゴリ
    $catCol = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $catCol.Name = "Category"
    $catCol.HeaderText = "カテゴリ"
    $catCol.AutoSizeMode = [System.Windows.Forms.DataGridViewAutoSizeColumnMode]::Fill
    $catCol.FillWeight = 20
    $catCol.MinimumWidth = 120
    $dataGridView.Columns.Add($catCol) | Out-Null

    # 7. サブカテゴリ
    $subCatCol = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $subCatCol.Name = "SubCategory"
    $subCatCol.HeaderText = "サブカテゴリ"
    $subCatCol.AutoSizeMode = [System.Windows.Forms.DataGridViewAutoSizeColumnMode]::Fill
    $subCatCol.FillWeight = 20
    $subCatCol.MinimumWidth = 120
    $dataGridView.Columns.Add($subCatCol) | Out-Null

    # 8. 記録操作
    $recordActionCol = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $recordActionCol.Name = "RecordAction"
    $recordActionCol.HeaderText = "記録操作"
    $recordActionCol.AutoSizeMode = [System.Windows.Forms.DataGridViewAutoSizeColumnMode]::AllCells
    $recordActionCol.MinimumWidth = 80
    $dataGridView.Columns.Add($recordActionCol) | Out-Null
    
    # Set alignment for specific columns
    $dataGridView.Columns["DueDate"].DefaultCellStyle.Alignment = [System.Windows.Forms.DataGridViewContentAlignment]::MiddleCenter
    $dataGridView.Columns["Progress"].DefaultCellStyle.Alignment = [System.Windows.Forms.DataGridViewContentAlignment]::MiddleCenter
    $dataGridView.Columns["Priority"].DefaultCellStyle.Alignment = [System.Windows.Forms.DataGridViewContentAlignment]::MiddleCenter
    $dataGridView.Columns["TrackedTime"].DefaultCellStyle.Alignment = [System.Windows.Forms.DataGridViewContentAlignment]::MiddleCenter
    
    $dataGridView.Columns["Priority"].HeaderCell.Style.WrapMode = [System.Windows.Forms.DataGridViewTriState]::False
}

# 秒を HH:mm:ss 形式に変換する新しい関数
function Format-TimeSpanFromSeconds {
    param([int]$totalSeconds)
    if ($totalSeconds -le 0) { return "" }
    $timeSpan = [System.TimeSpan]::FromSeconds($totalSeconds)
    return "{0:D2}:{1:D2}:{2:D2}" -f [int]$timeSpan.TotalHours, $timeSpan.Minutes, $timeSpan.Seconds
}

# 機能拡張された新しいUpdate-DataGridView関数
function Update-DataGridView {
    # フォームが最小化されているか、描画するにはサイズが小さすぎる場合は処理を中断する
    if ($mainForm -and ($mainForm.WindowState -eq [System.Windows.Forms.FormWindowState]::Minimized -or $mainForm.ClientSize.Width -lt 100 -or $mainForm.ClientSize.Height -lt 100)) { return }
    # --- 1. 表示状態を保存 ---
    $selectedIdentifier = $null
    $identifierProperty = ""
    $firstDisplayedRowIndex = $script:taskDataGridView.FirstDisplayedScrollingRowIndex
    if ($firstDisplayedRowIndex -lt 0) { $firstDisplayedRowIndex = 0 }

    if ($script:taskDataGridView.SelectedRows.Count -gt 0) {
        $selectedTag = $script:taskDataGridView.SelectedRows[0].Tag
        if ($selectedTag) {
            if ($selectedTag.PSObject.Properties.Name.Contains('ID')) {
                # Task object
                $selectedIdentifier = $selectedTag.ID
                $identifierProperty = 'ID'
            } elseif ($script:groupByProject -and $selectedTag.PSObject.Properties.Name.Contains('ProjectID')) {
                # Project object
                $selectedIdentifier = $selectedTag.ProjectID
                $identifierProperty = 'ProjectID'
            } elseif (-not $script:groupByProject -and $selectedTag.PSObject.Properties.Name.Contains('CategoryName')) {
                # Category group object
                $selectedIdentifier = $selectedTag.CategoryName
                $identifierProperty = 'CategoryName'
            }
        }
    }

    # --- 設定の適用 ---
    $dateFormat = if ($script:Settings.DateFormat) { $script:Settings.DateFormat } else { "yyyy/MM/dd" }
    $density = if ($script:Settings.ListDensity) { $script:Settings.ListDensity } else { "Standard" }
    $rowHeight = switch ($density) { "Compact" { 20 } "Relaxed" { 35 } default { 25 } }
    $script:taskDataGridView.RowTemplate.Height = $rowHeight
    $showStrike = if ($script:Settings.PSObject.Properties.Name -contains 'ShowStrikethrough') { $script:Settings.ShowStrikethrough } else { $true }

    # --- 既存の更新ロジック ---
    if ($null -eq $script:ProjectExpansionStates) {
        $script:ProjectExpansionStates = @{}
    }
    if ($null -eq $script:CategoryExpansionStates) {
        $script:CategoryExpansionStates = @{}
    }

    $script:taskDataGridView.SuspendLayout()
    try {
        $script:taskDataGridView.Rows.Clear()

        [array]$tasksToDisplay = if ($script:hideCompletedMenuItem -and $script:hideCompletedMenuItem.Checked) {
            $script:AllTasks | Where-Object { $_.進捗度 -ne '完了済み' }
        } else {
            $script:AllTasks
        }
        if ($script:CurrentCategoryFilter -ne '(すべて)') {
            $tasksToDisplay = $tasksToDisplay | Where-Object { $_.カテゴリ -eq $script:CurrentCategoryFilter }
        }

        # 3. Get tasks for calculation (ignoring "Hide Completed", but respecting Category filter)
        [array]$tasksForCalculation = $script:AllTasks
        if ($script:CurrentCategoryFilter -ne '(すべて)') {
            $tasksForCalculation = $tasksForCalculation | Where-Object { $_.カテゴリ -eq $script:CurrentCategoryFilter }
        }

        $progressMapping = @{
            "未実施" = 0; "保留" = 0; "実施中" = 50
            "確認待ち" = 75; "完了済み" = 100
        }

        if ($script:groupByProject) {
            # --- GROUP BY PROJECT (Existing Logic) ---
            $tasksGroupedByProject = $tasksToDisplay | Group-Object -Property ProjectID
            $tasksForCalcGroupedByProject = $tasksForCalculation | Group-Object -Property ProjectID
            
            # 2026-01-05修正: .PSObject.Copy() をやめて、ソート用のラッパーオブジェクトを使用する。
            # これにより、DataGridViewのTagがマスターデータへの直接参照となり、データ不整合を防ぐ。
            $sortableProjectWrappers = $script:Projects | ForEach-Object {
                if ($null -ne $_) {
                    $projectTasks = $tasksGroupedByProject | Where-Object { $_.Name -eq $_.ProjectID } | Select-Object -ExpandProperty Group
                    $isCompleted = $true
                    if ($projectTasks) {
                        if ($projectTasks | Where-Object { $_.進捗度 -ne '完了済み' }) { $isCompleted = $false }
                    } else {
                        $isCompleted = $false
                    }
                    [PSCustomObject]@{
                        ProjectObject = $_ # 元のオブジェクトへの参照
                        IsCompleted = $isCompleted
                        SortableDueDate = $(if ($_.ProjectDueDate) { try { [datetime]$_.ProjectDueDate } catch { [datetime]::MaxValue } } else { [datetime]::MaxValue })
                        ProjectName = $_.ProjectName
                    }
                }
            }
            $sortedProjectWrappers = $sortableProjectWrappers | Sort-Object -Property @{E='IsCompleted'; A=$true}, @{E='SortableDueDate'; A=$true}, ProjectName

            foreach ($wrapper in $sortedProjectWrappers) {
                $project = $wrapper.ProjectObject # 元のオブジェクトへの参照を使用
                $projectTaskGroup = $tasksGroupedByProject | Where-Object { $_.Name -eq $project.ProjectID }
                $projectCalcGroup = $tasksForCalcGroupedByProject | Where-Object { $_.Name -eq $project.ProjectID }
                
                $totalProjectSeconds = 0
                $averageProgress = 0
                $projectTasks = @()

                if ($projectCalcGroup) {
                    $calcTasks = $projectCalcGroup.Group
                    $taskIdsInProject = $calcTasks | ForEach-Object { $_.ID }
                    $projectLogs = $script:AllTimeLogs | Where-Object { $taskIdsInProject -contains $_.TaskID -and $_.EndTime }
                    if ($projectLogs) {
                        $totalProjectSeconds = ($projectLogs | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum
                    }
                    if ($script:currentlyTrackingTaskID -and $taskIdsInProject -contains $script:currentlyTrackingTaskID) {
                        $trackingLog = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $script:currentlyTrackingTaskID -and -not $_.EndTime } | Select-Object -Last 1
                        if ($trackingLog) {
                            $totalProjectSeconds += (New-TimeSpan -Start ([datetime]$trackingLog.StartTime) -End (Get-Date)).TotalSeconds
                        }
                    }
                    $progressValues = $calcTasks | ForEach-Object {
                        if (-not [string]::IsNullOrEmpty($_.進捗度) -and $progressMapping.ContainsKey($_.進捗度)) { $progressMapping[$_.進捗度] } else { 0 }
                    }
                    if ($progressValues -and $progressValues.Count -gt 0) {
                        $averageProgress = [int]($progressValues | Measure-Object -Average).Average
                    }
                }

                if ($projectTaskGroup) {
                    $projectTasks = $projectTaskGroup.Group
                }

                $isExpanded = $script:ProjectExpansionStates.ContainsKey($project.ProjectID) -and $script:ProjectExpansionStates[$project.ProjectID]
                $prefix = if ($isExpanded) { "[-] " } else { "[+] " }
                $formattedDueDate = if ($project.ProjectDueDate) { ([datetime]$project.ProjectDueDate).ToString($dateFormat) } else { "" }
                $projectNameWithPrefix = $prefix + $project.ProjectName
                $projectRowIndex = $script:taskDataGridView.Rows.Add(@(
                    $projectNameWithPrefix, $formattedDueDate, $averageProgress, $null,
                    (Format-TimeSpanFromSeconds -totalSeconds $totalProjectSeconds), $null, $null, $null
                ))
                $projectRow = $script:taskDataGridView.Rows[$projectRowIndex]
                $projectRow.Tag = $project
                $projectRow.DefaultCellStyle = $script:taskDataGridView.DefaultCellStyle.Clone()
                $projectRow.DefaultCellStyle.Font = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Bold)
                $projectRow.DefaultCellStyle.BackColor = (Get-ThemeColors -IsDarkMode $script:isDarkMode).HeaderBack
                $projectRow.ReadOnly = $true

                if ($isExpanded) {
                    if ($projectTasks) {
                        $priorityOrder = @{ '高' = 1; '中' = 2; '低' = 3 }
                        $statusOrder = @{ '未実施' = 1; '保留' = 2; '実施中' = 3; '確認待ち' = 4; '完了済み' = 5 }
                        $sortedTasks = $projectTasks | Sort-Object -Property @{E = { $_.進捗度 -eq '完了済み' }; A = $true },
                                                                      @{E = { if ($_.期日) { [datetime]$_.期日 } else { [datetime]::MaxValue } }; A = $true },
                                                                      @{E = { $priorityOrder[$_.優先度] }; A = $true },
                                                                      @{E = { $statusOrder[$_.進捗度] }; A = $true },
                                                                      タスク
                        foreach ($task in $sortedTasks) {
                            $totalTaskSeconds = 0
                            $taskLogs = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $task.ID -and $_.EndTime }
                            if ($taskLogs) {
                                $totalTaskSeconds = ($taskLogs | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum
                            }
                            if ($task.ID -eq $script:currentlyTrackingTaskID) {
                                $trackingLog = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $task.ID -and -not $_.EndTime } | Select-Object -Last 1
                                if ($trackingLog) {
                                    $totalTaskSeconds += (New-TimeSpan -Start ([datetime]$trackingLog.StartTime) -End (Get-Date)).TotalSeconds
                                }
                            }
                            $taskRowIndex = $script:taskDataGridView.Rows.Add()
                            $newRow = $script:taskDataGridView.Rows[$taskRowIndex]
                            $newRow.Cells[0].Value = " " + $task.タスク
                            $newRow.Cells[1].Value = if ($task.期日) { ([datetime]$task.期日).ToString($dateFormat) } else { "" }
                            $newRow.Cells[2].Value = $task.進捗度
                            $newRow.Cells[3].Value = $task.優先度
                            $newRow.Cells[4].Value = (Format-TimeSpanFromSeconds -totalSeconds $totalTaskSeconds)
                            $newRow.Cells[5].Value = $task.カテゴリ
                            $newRow.Cells[6].Value = $task.サブカテゴリ
                            $newRow.Tag = $task
                            if ($task.進捗度 -eq '完了済み') {
                                $newRow.Cells[7].Value = ""
                                if ($showStrike) {
                                    $newRow.DefaultCellStyle.Font = $script:datagridStrikeoutFont
                                } else {
                                    $newRow.DefaultCellStyle.Font = $script:datagridRegularFont
                                }
                                $newRow.DefaultCellStyle.ForeColor = [System.Drawing.Color]::Gray
                            } else {
                                $newRow.DefaultCellStyle.Font = $script:datagridRegularFont
                                
                                # 期日を過ぎたタスクの文字色を赤くする
                                $isOverdue = $false
                                if (-not [string]::IsNullOrWhiteSpace($task.期日)) {
                                    try {
                                        if (([datetime]$task.期日).Date -lt (Get-Date).Date) { $isOverdue = $true }
                                    } catch {}
                                }
                                if ($isOverdue) {
                                    $newRow.DefaultCellStyle.ForeColor = (Get-ThemeColors -IsDarkMode $script:isDarkMode).ErrorFore
                                }

                                if ($task.ID -eq $script:currentlyTrackingTaskID) {
                                    $newRow.Cells[7].Value = "■ 停止"
                                    $newRow.DefaultCellStyle.BackColor = [System.Drawing.Color]::LightGreen
                                } else {
                                    $newRow.Cells[7].Value = "▶ 開始"
                                }
                            }
                        }
                    }
                }
            }
        } else {
            # --- GROUP BY CATEGORY (New Logic) ---
            $tasksGroupedByCategory = $tasksToDisplay | Group-Object -Property カテゴリ
            $tasksForCalcGroupedByCategory = $tasksForCalculation | Group-Object -Property カテゴリ
            $categoryNames = $tasksGroupedByCategory.Name | Sort-Object
            
            foreach ($categoryName in $categoryNames) {
                $categoryTaskGroup = $tasksGroupedByCategory | Where-Object { $_.Name -eq $categoryName }
                $categoryTasks = $categoryTaskGroup.Group
                
                if ($categoryTasks.Count -eq 0) { continue }

                $isExpanded = $script:CategoryExpansionStates.ContainsKey($categoryName) -and $script:CategoryExpansionStates[$categoryName]
                $prefix = if ($isExpanded) { "[-] " } else { "[+] " }
                $displayName = if ([string]::IsNullOrEmpty($categoryName)) { "(カテゴリ未設定)" } else { $categoryName }
                $categoryNameWithPrefix = $prefix + $displayName

                # Aggregations for category row
                $categoryCalcGroup = $tasksForCalcGroupedByCategory | Where-Object { $_.Name -eq $categoryName }
                $calcTasks = if ($categoryCalcGroup) { $categoryCalcGroup.Group } else { @() }
                $progressValues = $calcTasks | ForEach-Object { if ($progressMapping.ContainsKey($_.進捗度)) { $progressMapping[$_.進捗度] } else { 0 } }
                $averageProgress = if ($progressValues.Count -gt 0) { [int]($progressValues | Measure-Object -Average).Average } else { 0 }
                $taskIdsInCategory = if ($calcTasks) { $calcTasks.ID } else { @() }
                $categoryLogs = $script:AllTimeLogs | Where-Object { $taskIdsInCategory -contains $_.TaskID -and $_.EndTime }
                $totalCategorySeconds = if ($categoryLogs) { ($categoryLogs | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum } else { 0 }

                $categoryRowIndex = $script:taskDataGridView.Rows.Add(@(
                    $categoryNameWithPrefix, $null, $averageProgress, $null,
                    (Format-TimeSpanFromSeconds -totalSeconds $totalCategorySeconds), $null, $null, $null
                ))
                $categoryRow = $script:taskDataGridView.Rows[$categoryRowIndex]
                $categoryRow.Tag = [PSCustomObject]@{ CategoryName = $categoryName }
                $categoryRow.DefaultCellStyle = $script:taskDataGridView.DefaultCellStyle.Clone()
                $categoryRow.DefaultCellStyle.Font = New-Object System.Drawing.Font("Meiryo UI", 9, [System.Drawing.FontStyle]::Bold)
                $categoryRow.DefaultCellStyle.BackColor = (Get-ThemeColors -IsDarkMode $script:isDarkMode).HeaderBack
                $categoryRow.ReadOnly = $true

                if ($isExpanded) {
                    # Sort and add tasks (same logic as project view)
                    $priorityOrder = @{ '高' = 1; '中' = 2; '低' = 3 }
                    $statusOrder = @{ '未実施' = 1; '保留' = 2; '実施中' = 3; '確認待ち' = 4; '完了済み' = 5 }
                    $sortedTasks = $categoryTasks | Sort-Object -Property @{E = { $_.進捗度 -eq '完了済み' }; A = $true },
                                                                  @{E = { if ($_.期日) { [datetime]$_.期日 } else { [datetime]::MaxValue } }; A = $true },
                                                                  @{E = { $priorityOrder[$_.優先度] }; A = $true },
                                                                  @{E = { $statusOrder[$_.進捗度] }; A = $true },
                                                                  タスク
                    foreach ($task in $sortedTasks) {
                        $totalTaskSeconds = 0
                        $taskLogs = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $task.ID -and $_.EndTime }
                        if ($taskLogs) {
                            $totalTaskSeconds = ($taskLogs | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum
                        }
                        if ($task.ID -eq $script:currentlyTrackingTaskID) {
                            $trackingLog = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $task.ID -and -not $_.EndTime } | Select-Object -Last 1
                            if ($trackingLog) {
                                $totalTaskSeconds += (New-TimeSpan -Start ([datetime]$trackingLog.StartTime) -End (Get-Date)).TotalSeconds
                            }
                        }
                        $taskRowIndex = $script:taskDataGridView.Rows.Add()
                        $newRow = $script:taskDataGridView.Rows[$taskRowIndex]
                        $newRow.Cells[0].Value = "    " + $task.タスク
                        $newRow.Cells[1].Value = if ($task.期日) { ([datetime]$task.期日).ToString($dateFormat) } else { "" }
                        $newRow.Cells[2].Value = $task.進捗度
                        $newRow.Cells[3].Value = $task.優先度
                        $newRow.Cells[4].Value = (Format-TimeSpanFromSeconds -totalSeconds $totalTaskSeconds)
                        $newRow.Cells[5].Value = $task.カテゴリ
                        $newRow.Cells[6].Value = $task.サブカテゴリ
                        $newRow.Tag = $task
                        if ($task.進捗度 -eq '完了済み') {
                            $newRow.Cells[7].Value = ""
                            if ($showStrike) {
                                $newRow.DefaultCellStyle.Font = $script:datagridStrikeoutFont
                            } else {
                                $newRow.DefaultCellStyle.Font = $script:datagridRegularFont
                            }
                            $newRow.DefaultCellStyle.ForeColor = [System.Drawing.Color]::Gray
                        } else {
                            $newRow.DefaultCellStyle.Font = $script:datagridRegularFont

                            # 期日を過ぎたタスクの文字色を赤くする
                            $isOverdue = $false
                            if (-not [string]::IsNullOrWhiteSpace($task.期日)) {
                                try {
                                    if (([datetime]$task.期日).Date -lt (Get-Date).Date) { $isOverdue = $true }
                                } catch {}
                            }
                            if ($isOverdue) {
                                $newRow.DefaultCellStyle.ForeColor = (Get-ThemeColors -IsDarkMode $script:isDarkMode).ErrorFore
                            }

                            if ($task.ID -eq $script:currentlyTrackingTaskID) {
                                $newRow.Cells[7].Value = "■ 停止"
                                $newRow.DefaultCellStyle.BackColor = [System.Drawing.Color]::LightGreen
                            } else {
                                $newRow.Cells[7].Value = "▶ 開始"
                            }
                        }
                    }
                }
            }
        }
    } finally {
        # finallyブロックで確実にレイアウトロジックを再開する
    $script:taskDataGridView.ResumeLayout()
    }

    # --- 3. 表示状態を復元 ---
    $rowToRestore = $null
    if ($selectedIdentifier) {
        foreach ($row in $script:taskDataGridView.Rows) {
            $tag = $row.Tag
            if ($tag -and $identifierProperty -and $tag.PSObject.Properties.Name.Contains($identifierProperty)) {
                if ($tag.$identifierProperty -eq $selectedIdentifier) {
                    $rowToRestore = $row
                    break
                }
            }
        }
    }

    if ($rowToRestore) {
        $script:taskDataGridView.ClearSelection()
        $rowToRestore.Selected = $true
        $script:taskDataGridView.CurrentCell = $rowToRestore.Cells[0]
    }

    try {
        if ($firstDisplayedRowIndex -ge 0 -and $firstDisplayedRowIndex -lt $script:taskDataGridView.RowCount) {
            $script:taskDataGridView.FirstDisplayedScrollingRowIndex = $firstDisplayedRowIndex
        }
    } catch {
        # 行数が変わってインデックスが無効になった場合のエラーは無視する
    }

    if ($script:statusLabel) {
        $totalCount = $script:AllTasks.Count
        $completedCount = @($script:AllTasks | Where-Object { $_.進捗度 -eq '完了済み' }).Count
        $incompleteCount = $totalCount - $completedCount
        $script:statusLabel.Text = "総タスク: $($totalCount)件 | 完了済み: $($completedCount)件 | 未完了: $($incompleteCount)件"
    }
}

function Update-AssociatedFilesView {
    try {
    if ($null -eq $script:fileListView) { return }
    $script:fileListView.Items.Clear()
    $script:previewPanel.Controls.Clear() # プレビューもクリア

    if ($script:taskDataGridView.SelectedRows.Count -eq 0) { return }
    $selectedRow = $script:taskDataGridView.SelectedRows[0]
    $itemObject = $selectedRow.Tag
    if ($null -eq $itemObject) { return }

    $filesToShow = [System.Collections.ArrayList]::new()
    $isProject = $itemObject.PSObject.Properties.Name -contains 'ProjectName'

    if ($isProject) {
        # プロジェクトが選択されている場合
        if ($itemObject.WorkFiles) {
            $filesToShow.AddRange(@($itemObject.WorkFiles))
        }
    } else {
        # タスクが選択されている場合
        # 1. タスク自身のWorkFilesを追加
        if ($itemObject.WorkFiles) {
            $filesToShow.AddRange(@($itemObject.WorkFiles))
        }
        # 2. 親プロジェクトのWorkFilesを追加
        if ($itemObject.ProjectID) {
            $parentProject = $script:Projects | Where-Object { $_.ProjectID -eq $itemObject.ProjectID } | Select-Object -First 1
            if ($parentProject -and $parentProject.WorkFiles) {
                $filesToShow.AddRange(@($parentProject.WorkFiles))
            }
        }
    }
    
    # Contentプロパティに基づいて重複を除外し、最初のオブジェクトを選択してからソートする
    # フィルタリングロジックを緩和・堅牢化
    $validFiles = [System.Collections.ArrayList]::new()
    foreach ($f in $filesToShow) {
        if ($null -ne $f -and $f -is [psobject]) {
             # Contentプロパティが存在し、かつ配列でないことを確認
             if ($f.PSObject.Properties['Content'] -and $f.Content -isnot [array]) {
                 $validFiles.Add($f) | Out-Null
             }
        }
    }
    # Group-Object fails if Content is not comparable. Ensure we group by string.
    $uniqueFiles = $validFiles | Group-Object -Property { if ($_.Content) { $_.Content.ToString() } else { "" } } | ForEach-Object { $_.Group[0] } | Sort-Object -Property DisplayName

    foreach ($fileObject in $uniqueFiles) {
        $newItem = New-Object System.Windows.Forms.ListViewItem
        $newItem.Tag = $fileObject
        $newItem.Name = $fileObject.Content # NameプロパティにはContentを保持
        $newItem.Text = $fileObject.DisplayName
        $newItem.ToolTipText = $fileObject.Content
        $newItem.SubItems.Add($fileObject.Type) | Out-Null
        $newItem.SubItems.Add($fileObject.DateAdded) | Out-Null

        Set-FileIcon -item $newItem -imageList $script:fileListView.SmallImageList -fileType $fileObject.Type
        
        $script:fileListView.Items.Add($newItem) | Out-Null
    }
    } catch {
        Write-Warning "Update-AssociatedFilesView error: $($_.Exception.Message)"
    }
}

function Start-EditTask { 
    param([pscustomobject]$task)
    
    # 1. フォームを表示して編集結果を取得
    # キャンセルされた場合は $null が返る
    $editedTaskData = Show-TaskInputForm -existingTask $task
    
    # パイプライン汚染対策: 配列が返ってきた場合は最後の要素(本来の戻り値)を取得
    if ($editedTaskData -is [array]) {
        $editedTaskData = $editedTaskData | Select-Object -Last 1
    }

    # 2. 安全装置: キャンセル時は絶対に何もしない
    # フォームでキャンセルが押された場合、$editedTaskData は $null になります。
    # その場合はタスクの削除や変更を行わず、即座に処理を終了します。
    if ($null -eq $editedTaskData) {
        return $false
    }

    # 追加の安全装置: 返ってきたデータが正しい形式か確認
    # キャンセル時や予期せぬデータ(数値の0など)が返ってきた場合に、タスクが空データで上書きされるのを防ぐ
    if (-not ($editedTaskData -is [PSCustomObject]) -or -not $editedTaskData.PSObject.Properties['タスク']) {
        return $false
    }

    # 3. 保存処理: リストから対象タスクを探して更新
    $taskIDToEdit = $task.ID
    $taskIndex = -1
    
    # IDで対象のインデックスを検索
    for ($i = 0; $i -lt $script:AllTasks.Count; $i++) {
        if ($script:AllTasks[$i].ID -eq $taskIDToEdit) {
            $taskIndex = $i
            break
        }
    }

    # 対象が見つかった場合のみ更新
    if ($taskIndex -ne -1) {
        $taskToUpdate = $script:AllTasks[$taskIndex]
        
        # プロパティを一つずつ更新 (削除→追加ではなく、書き換えを行う)
        $taskToUpdate.ProjectID = $editedTaskData.ProjectID
        $taskToUpdate.期日 = $editedTaskData.期日
        $taskToUpdate.優先度 = $editedTaskData.優先度
        $taskToUpdate.タスク = $editedTaskData.タスク
        $taskToUpdate.進捗度 = $editedTaskData.進捗度
        $taskToUpdate.通知設定 = $editedTaskData.通知設定
        $taskToUpdate.カテゴリ = $editedTaskData.カテゴリ
        $taskToUpdate.サブカテゴリ = $editedTaskData.サブカテゴリ
        $taskToUpdate.完了日 = $editedTaskData.完了日
        
        # ファイルへ保存
        Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
        return $true # 更新成功
    }
    
    return $false # 対象が見つからなかった場合
}


function Start-DeleteTask { 
    param([pscustomobject]$task)
    if ([System.Windows.Forms.MessageBox]::Show("選択したタスクを削除しますか？", "確認", "YesNo", "Warning") -ne "Yes") { return }
    $taskIDToDelete = $task.ID
    $script:AllTasks = @($script:AllTasks | Where-Object { $_.ID -ne $taskIDToDelete })
    Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
}
function Set-TaskStatus {
    param([pscustomobject]$task, [string]$newStatus)
    
    $taskID = $task.ID
    $taskIndex = -1

    for ($i = 0; $i -lt $script:AllTasks.Count; $i++) {
        if ($script:AllTasks[$i].ID -eq $taskID) {
            $taskIndex = $i
            break
        }
    }

    if ($taskIndex -ne -1) {
        $taskToUpdate = $script:AllTasks[$taskIndex]
        $oldStatus = $taskToUpdate.進捗度
        
        # --- ステータス変更ログの記録 (変更があった場合のみ) ---
        if ($oldStatus -ne $newStatus) {
            $statusLogEntry = [PSCustomObject]@{
                TaskID    = $taskID
                OldStatus = $oldStatus
                NewStatus = $newStatus
                Timestamp = (Get-Date).ToString("o")
            }
            
            $currentStatusLogs = @()
            if (Test-Path $script:StatusLogsFile) {
                try {
                    $content = Get-Content -Path $script:StatusLogsFile -Raw -Encoding UTF8
                    if (-not [string]::IsNullOrWhiteSpace($content)) {
                        $currentStatusLogs = $content | ConvertFrom-Json
                        if ($currentStatusLogs -isnot [array]) { $currentStatusLogs = @($currentStatusLogs) }
                    }
                } catch {}
            }
            $currentStatusLogs += $statusLogEntry
            try {
                $currentStatusLogs | ConvertTo-Json -Depth 2 | Set-Content -Path $script:StatusLogsFile -Encoding UTF8 -Force
            } catch {
                Write-Warning "ステータスログの保存に失敗しました: $($_.Exception.Message)"
            }
        }
        
        # 進捗度が「完了済み」に変更された場合
        if ($newStatus -eq '完了済み') {
            # 完了日がまだ設定されていない場合のみ、現在の日付を設定
            if ([string]::IsNullOrEmpty($taskToUpdate.完了日)) {
                $taskToUpdate.完了日 = (Get-Date).ToString("yyyy-MM-dd")
            }
        } else {
            # 進捗度が「完了済み」以外に変更された場合は、完了日をリセット
            $taskToUpdate.完了日 = ""
        }
        
        $taskToUpdate.進捗度 = $newStatus

        # --- ここから修正 ---
        # 1. 該当タスクの合計記録時間を再計算
        $taskLogs = $script:AllTimeLogs | Where-Object { $_.TaskID -eq $taskID -and $_.EndTime }
        $totalSeconds = 0
        if ($taskLogs) {
            $totalSeconds = ($taskLogs | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum
        }
        $taskToUpdate.TrackedTimeSeconds = $totalSeconds
        # --- ここまで修正 ---

        # 変更をCSVファイルに保存して永続化する
        Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks

        # --- タスク完了時の即時アーカイブ機能 (PJ無関係) ---
        if ($newStatus -eq '完了済み' -and $oldStatus -ne '完了済み' -and $script:Settings.ArchiveTasksOnCompletion) {
            Move-TaskToArchive -tasksToArchive @($taskToUpdate)
            return
        }

        # --- プロジェクト完了時のタスク即時アーカイブ機能 ---
        if ($newStatus -eq '完了済み' -and $oldStatus -ne '完了済み' -and $script:Settings.ArchiveTasksOnProjectCompletion) {
            $projectID = $taskToUpdate.ProjectID
            if ($projectID) {
                $tasksInProject = $script:AllTasks | Where-Object { $_.ProjectID -eq $projectID }
                $allComplete = $true
                foreach ($t in $tasksInProject) {
                    if ($t.進捗度 -ne '完了済み') {
                        $allComplete = $false
                        break
                    }
                }

                if ($allComplete) {
                    # To avoid modifying the collection while iterating, create a copy
                    $tasksToArchive = @($tasksInProject)
                    Move-TaskToArchive -tasksToArchive $tasksToArchive
                }
            }
        }

        # プロジェクトの自動アーカイブ条件をチェック
        Invoke-ProjectAutoArchiving
    }
}

function Invoke-AutoArchiving {
    $autoArchiveDays = $script:Settings.AutoArchiveDays
    if ($null -eq $autoArchiveDays -or $autoArchiveDays -le 0) {
        return
    }

    $today = (Get-Date).Date
    $tasksToArchive = $script:AllTasks | Where-Object {
        $_.進捗度 -eq '完了済み' -and
        -not [string]::IsNullOrEmpty($_.完了日)
    }

    $tasksToMove = @()
    foreach ($task in $tasksToArchive) {
        try {
            $project = $script:Projects | Where-Object { $_.ProjectID -eq $task.ProjectID } | Select-Object -First 1
            $canArchive = $true
            if ($project -and $project.PSObject.Properties.Name -contains 'AutoArchiveTasks' -and -not $project.AutoArchiveTasks) {
                $canArchive = $false
            }

            if ($canArchive) {
                $completionDate = [datetime]$task.完了日
                if (($today - $completionDate).Days -ge $autoArchiveDays) {
                    $tasksToMove += $task
                }
            }
        } catch {
            # 無視する
        }
    }

    if ($tasksToMove.Count -gt 0) {
        Move-TaskToArchive -tasksToArchive $tasksToMove
    }
}
function Set-TaskPriority {
    param([string]$newPriority)
    if ($script:taskDataGridView.SelectedRows.Count -eq 0) { return }
    $taskIDToEdit = $script:taskDataGridView.SelectedRows[0].Cells["TaskID"].Value
    $taskToUpdate = $script:AllTasks | Where-Object { $_.ID -eq $taskIDToEdit }; if ($null -ne $taskToUpdate) { $taskToUpdate.優先度 = $newPriority; Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks } 
}
function Update-ProjectFiles {
    param([string]$projectID, [array]$newFileList)
    $projectToUpdate = $script:Projects | Where-Object { $_.ProjectID -eq $projectID } | Select-Object -First 1
    if ($projectToUpdate) {
        $projectToUpdate.WorkFiles = $newFileList
        Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
    }
    
    # DGVの選択状態を更新するロジック（必要に応じて）
}
function Remove-SelectedFilesFromProject {
    if($script:taskListView.SelectedItems.Count -eq 0 -or $script:fileListView.SelectedItems.Count -eq 0){ return }
    
    $selectedItem = $script:taskListView.SelectedItems[0]
    $projectID = $null

    if ($selectedItem.Group -and $selectedItem.Group.Tag) {
        $projectID = $selectedItem.Group.Tag.ProjectID
    }
    
    if (-not $projectID) { return }

    $projectToUpdate = $script:Projects | Where-Object { $_.ProjectID -eq $projectID } | Select-Object -First 1
    if (-not $projectToUpdate) { return }

    # Tagに保存された完全なオブジェクトを取得
    $selectedWorkFileObjects = @($script:fileListView.SelectedItems | ForEach-Object { $_.Tag })
    
    # 現在のファイルリストから選択されたオブジェクトを除外
    $currentFiles = @($projectToUpdate.WorkFiles)
    $newFiles = $currentFiles | Where-Object { $selectedWorkFileObjects -notcontains $_ }

    $projectToUpdate.WorkFiles = @($newFiles)
}

# ===================================================================
# アーカイブ機能関連
# ===================================================================

# --- アーカイブファイルパスの定義 ---
$script:ArchivedTasksFile = Join-Path -Path $script:AppRoot -ChildPath "archived_tasks.csv"
$script:ArchivedProjectsFile = Join-Path -Path $script:AppRoot -ChildPath "archived_projects.json"

# --- アーカイブファイルの読み込み関数 ---

function Read-ArchivedTasksFromCsv {
    param([string]$filePath)
    if (-not (Test-Path $filePath) -or (Get-Item $filePath).Length -eq 0) { return @() }
    
    try {
        # 読み込みロジックの強化: BOM付きUTF-8やロック競合に強くするため、ReadAllTextを使用
        $content = $null
        try {
            $content = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::UTF8)
        } catch {
            $content = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::Default)
        }

        if ([string]::IsNullOrWhiteSpace($content)) { return @() }
        $content = $content.Trim([char]0xfeff)
        if ($content.StartsWith("ï»¿")) { $content = $content.Substring(3) }
        
        $tasks = @($content | ConvertFrom-Csv | Where-Object { ($_.PSObject.Properties | ForEach-Object { $_.Value }) -join '' -ne '' })

        # WorkFiles列をJSONからオブジェクトに変換する (アーカイブデータは正規化済み前提)
        foreach ($task in $tasks) {
            if ($task.PSObject.Properties['WorkFiles'] -and $task.WorkFiles -is [string] -and -not [string]::IsNullOrWhiteSpace($task.WorkFiles)) {
                try {
                    $workFilesObject = $task.WorkFiles | ConvertFrom-Json -ErrorAction Stop
                    # ConvertFrom-Jsonが単一オブジェクトを返す場合でも配列として扱う
                    $task.WorkFiles = @($workFilesObject)
                } catch {
                    Write-Warning "アーカイブタスク $($task.ID) のWorkFilesの解析に失敗しました。空のリストで上書きします。"
                    $task.WorkFiles = @()
                }
            } else {
                $task.WorkFiles = @()
            }
        }
        return $tasks

    } catch {
        [System.Windows.Forms.MessageBox]::Show("アーカイブCSV ($filePath) の読み込みエラー: $($_.Exception.Message)", "エラー", "OK", "Warning")
        return @()
    }
}

function Read-ArchivedProjectsFromJson {
    param([string]$filePath)
    try {
        if (-not (Test-Path $filePath)) { return @() }
        $content = Get-Content -Path $filePath -Raw -Encoding UTF8
        if ([string]::IsNullOrWhiteSpace($content)) { return @() }
        $data = $content | ConvertFrom-Json -ErrorAction Stop
        # 常に配列として返すことを保証する
        return @($data)
    } catch {
        [System.Windows.Forms.MessageBox]::Show("アーカイブJSON ($filePath) の読み込みに失敗しました: `n$($_.Exception.Message)", "読み込みエラー", "OK", "Warning")
        return @()
    }
}


# --- アーカイブファイルの書き込み関数 ---

function Write-ArchivedTasksToCsv {
    param([string]$filePath, [array]$data)
    if ($null -eq $data) {
        Set-Content -Path $filePath -Value "" -Encoding UTF8 -Force
        return
    }
    try {
        # CSVに出力するプロパティの順序を定義
        $propertyOrder = @(
            "ID", "ProjectID", "ProjectName", "タスク", "進捗度", "優先度", "期日",
            "カテゴリ", "サブカテゴリ", "通知設定", "保存日付", "完了日",
            "TrackedTimeSeconds", "WorkFiles", "ArchivedDate"
        )

        $exportData = foreach ($task in $data) {
            $taskForCsv = New-Object PSCustomObject

            foreach ($prop in $propertyOrder) {
                $value = if ($task.PSObject.Properties.Name -contains $prop) { $task.$prop } else { "" }
                $taskForCsv | Add-Member -MemberType NoteProperty -Name $prop -Value $value
            }

            if ($taskForCsv.WorkFiles -is [array] -and $taskForCsv.WorkFiles.Count -gt 0) {
                $taskForCsv.WorkFiles = $taskForCsv.WorkFiles | ConvertTo-Json -Compress -Depth 5
            } else {
                $taskForCsv.WorkFiles = ""
            }
            $taskForCsv
        }

        $exportData | Export-Csv -Path $filePath -NoTypeInformation -Encoding UTF8 -Force
    } catch {
        [System.Windows.Forms.MessageBox]::Show("アーカイブCSV ($filePath) への書き込みエラー: $($_.Exception.Message)", "エラー", "OK", "Error")
    }
}

function Write-ArchivedProjectsToJson {
    param([string]$filePath, $dataObject)
    try {
        $json = $dataObject | ConvertTo-Json -Depth 5
        Set-Content -Path $filePath -Value $json -Encoding UTF8 -Force
    } catch {
        [System.Windows.Forms.MessageBox]::Show("アーカイブJSON ($filePath) の保存に失敗しました: `n$($_.Exception.Message)", "保存エラー", "OK", "Error")
    }
}

# ===================================================================
# アーカイブ実行ロジック
# ===================================================================

function Move-TaskToArchive {
    param(
        [Parameter(Mandatory=$true)]
        [psobject[]]$tasksToArchive
    )

    if ($tasksToArchive.Count -eq 0) { return }

    try {
        # 1. アーカイブ済みタスクを読み込む
        $archivedTasks = Read-ArchivedTasksFromCsv -filePath $script:ArchivedTasksFile
        if ($archivedTasks -isnot [array]) { $archivedTasks = @($archivedTasks) }

        $archiveDate = (Get-Date).ToString("yyyy-MM-dd")
        foreach ($task in $tasksToArchive) {
            # プロジェクト名を保存（復元時に親プロジェクトが消失している場合の対策）
            $projName = ""
            if ($task.ProjectID) {
                $parentProj = $script:Projects | Where-Object { $_.ProjectID -eq $task.ProjectID } | Select-Object -First 1
                if ($parentProj) { $projName = $parentProj.ProjectName }
            }
            # 2. タスクにアーカイブ日付を追加
            $task | Add-Member -MemberType NoteProperty -Name 'ArchivedDate' -Value $archiveDate -Force
            $task | Add-Member -MemberType NoteProperty -Name 'ProjectName' -Value $projName -Force
            # 3. アーカイブリストに追加
            $archivedTasks += $task
        }

        # 4. アクティブタスクリストから削除
        $idsToRemove = $tasksToArchive.ID
        $script:AllTasks = @($script:AllTasks | Where-Object { $_.ID -notin $idsToRemove })

        # 5. 両方のファイルを保存
        Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
        Write-ArchivedTasksToCsv -filePath $script:ArchivedTasksFile -data $archivedTasks

    } catch {
        [System.Windows.Forms.MessageBox]::Show("タスクのアーカイブ中にエラーが発生しました: `n$($_.Exception.Message)", "アーカイブエラー", "OK", "Error")
    }
}

function Move-ProjectToArchive {
    param(
        [Parameter(Mandatory=$true)]
        [psobject]$projectToArchive
    )

    try {
        # 1. 関連するすべてのタスクをアーカイブする
        # 一括処理に変更
        $tasksInProject = @($script:AllTasks | Where-Object { $_.ProjectID -eq $projectToArchive.ProjectID })
        
        if ($tasksInProject.Count -gt 0) {
            Move-TaskToArchive -tasksToArchive $tasksInProject
        }

        # 2. アーカイブ済みプロジェクトを読み込む
        $archivedProjects = Read-ArchivedProjectsFromJson -filePath $script:ArchivedProjectsFile

        # 3. プロジェクトにアーカイブ日付を追加
        $projectToArchive | Add-Member -MemberType NoteProperty -Name 'ArchivedDate' -Value (Get-Date).ToString("yyyy-MM-dd") -Force

        # 4. アーカイブリストに追加 (堅牢な方法に変更)
        $archivedProjects = @($archivedProjects) + $projectToArchive

        # 5. アクティブプロジェクトリストから削除
        $script:Projects = @($script:Projects | Where-Object { $_.ProjectID -ne $projectToArchive.ProjectID })

        # 6. 両方のファイルを保存
        Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
        Write-ArchivedProjectsToJson -filePath $script:ArchivedProjectsFile -dataObject $archivedProjects

    } catch {
        [System.Windows.Forms.MessageBox]::Show("プロジェクトのアーカイブ中にエラーが発生しました: `n$($_.Exception.Message)", "アーカイブエラー", "OK", "Error")
    }
}

# ===================================================================
# アーカイブ復元ロジック
# ===================================================================

function Restore-Task {
    param(
        [Parameter(Mandatory=$true)]
        [psobject[]]$tasksToRestore
    )
    try {
        # 0. 復元対象のタスクに関連するプロジェクトを確認
        # 親プロジェクトがアクティブでない場合、タスクを「未分類」に移動する (プロジェクトは復元しない)
        $activeProjectIds = @($script:Projects | ForEach-Object { $_.ProjectID })
        $inboxProject = $null
        
        foreach ($task in $tasksToRestore) {
            if ($task.ProjectID -notin $activeProjectIds) {
                # 親プロジェクトが存在しない/アーカイブされている場合
                
                # 未分類プロジェクトの取得（必要になった時点で取得・作成）
                if ($null -eq $inboxProject) {
                    $inboxProject = $script:Projects | Where-Object { $_.ProjectName -eq "未分類" } | Select-Object -First 1
                    if ($null -eq $inboxProject) {
                        $inboxProject = [PSCustomObject]@{
                            ProjectID = [guid]::NewGuid().ToString()
                            ProjectName = "未分類"
                            ProjectDueDate = $null
                            WorkFiles = @()
                            Notification = "全体設定に従う"
                            ProjectColor = "#D3D3D3"
                            AutoArchiveTasks = $true
                        }
                        $script:Projects += $inboxProject
                        Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
                    }
                }
                
                # タスクのプロジェクトIDを未分類に変更
                $task.ProjectID = $inboxProject.ProjectID
            }
        }

        # 1. 復元するタスクのプロパティを整理（ArchivedDate削除、完了日更新）
        foreach($task in $tasksToRestore){
            if ($task.PSObject.Properties['ArchivedDate']) {
                $task.PSObject.Properties.Remove('ArchivedDate')
            }
            if ($task.PSObject.Properties['ProjectName']) {
                $task.PSObject.Properties.Remove('ProjectName')
            }
            # 完了済みタスクの完了日を今日に更新して即時再アーカイブを防ぐ
            if ($task.進捗度 -eq '完了済み') {
                $task.完了日 = (Get-Date).ToString("yyyy-MM-dd")
            }
        }

        # 2. アクティブなタスクリストに追加して保存 (データ保護のため先に行う)
        $currentIds = $script:AllTasks.ID
        $realTasksToRestore = @($tasksToRestore | Where-Object { $_.ID -notin $currentIds })
        if ($realTasksToRestore.Count -gt 0) {
            $script:AllTasks = @($script:AllTasks) + $realTasksToRestore
            Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
            
            # 3. 保存に成功したらアーカイブファイルから削除
            $archivedTasks = Read-ArchivedTasksFromCsv -filePath $script:ArchivedTasksFile
            $taskIdsToRestore = $tasksToRestore.ID
            $updatedArchivedTasks = @($archivedTasks | Where-Object { $_.ID -notin $taskIdsToRestore })
            Write-ArchivedTasksToCsv -filePath $script:ArchivedTasksFile -data $updatedArchivedTasks
        }
    } catch {
        [System.Windows.Forms.MessageBox]::Show("タスクの復元中にエラーが発生しました: `n$($_.Exception.Message)", "復元エラー", "OK", "Error")
    }
}

function Restore-Project {
    param(
        [Parameter(Mandatory=$true)]
        [psobject]$projectToRestore
    )
    try {
        # 1. プロジェクトのプロパティ整理
        if ($projectToRestore.PSObject.Properties['ArchivedDate']) {
            $projectToRestore.PSObject.Properties.Remove('ArchivedDate')
        }

        # 2. アクティブなプロジェクトリストに追加して保存 (データ保護のため先に行う)
        if ($script:Projects.ProjectID -notcontains $projectToRestore.ProjectID) {
            $script:Projects += $projectToRestore
            Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
            
            # 3. 保存に成功したらアーカイブファイルから削除
            $archivedProjects = Read-ArchivedProjectsFromJson -filePath $script:ArchivedProjectsFile
            $updatedArchivedProjects = @($archivedProjects | Where-Object { $_.ProjectID -ne $projectToRestore.ProjectID })
            Write-ArchivedProjectsToJson -filePath $script:ArchivedProjectsFile -dataObject $updatedArchivedProjects
        }

        # 4. 関連するアーカイブ済みタスクをすべて復元 (バッチ処理)
        # Restore-Task内部でアーカイブからの削除も行われる
        $allArchivedTasks = Read-ArchivedTasksFromCsv -filePath $script:ArchivedTasksFile
        $tasksToRestore = @($allArchivedTasks | Where-Object { $_.ProjectID -eq $projectToRestore.ProjectID })
        
        if ($tasksToRestore.Count -gt 0) {
            Restore-Task -tasksToRestore $tasksToRestore
        }

    } catch {
        [System.Windows.Forms.MessageBox]::Show("プロジェクトの復元中にエラーが発生しました: `n$($_.Exception.Message)", "復元エラー", "OK", "Error")
    }
}

function Show-ArchiveViewForm {
    param($parentForm)

    # 1. フォームの基本設定
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "アーカイブビュー"
    $form.Size = New-Object System.Drawing.Size(800, 600)
    $form.StartPosition = 'CenterParent'
    $form.MinimumSize = New-Object System.Drawing.Size(600, 400)

    # 2. 上部パネル (検索エリア)
    $topPanel = New-Object System.Windows.Forms.Panel; $topPanel.Dock = "Top"; $topPanel.Height = 40; $topPanel.Padding = New-Object System.Windows.Forms.Padding(10, 8, 10, 0); $form.Controls.Add($topPanel)
    $btnSearch = New-Object System.Windows.Forms.Button; $btnSearch.Dock = "Right"; $btnSearch.Text = "検索"; $topPanel.Controls.Add($btnSearch)
    $textSearch = New-Object System.Windows.Forms.TextBox; $textSearch.Dock = "Fill"; $topPanel.Controls.Add($textSearch)
    
    # 3. 下部パネル (操作ボタン)
    $bottomPanel = New-Object System.Windows.Forms.Panel; $bottomPanel.Dock = "Bottom"; $bottomPanel.Height = 50; $bottomPanel.Padding = New-Object System.Windows.Forms.Padding(10); $form.Controls.Add($bottomPanel)
    $btnClose = New-Object System.Windows.Forms.Button; $btnClose.Dock = "Right"; $btnClose.Text = "閉じる"; $btnClose.Width = 100; $btnClose.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $bottomPanel.Controls.Add($btnClose); $form.CancelButton = $btnClose
    $btnRestore = New-Object System.Windows.Forms.Button; $btnRestore.Dock = "Left"; $btnRestore.Text = "選択項目を復元"; $btnRestore.Width = 150; $bottomPanel.Controls.Add($btnRestore)
    $btnDelete = New-Object System.Windows.Forms.Button; $btnDelete.Dock = "Left"; $btnDelete.Text = "完全に削除"; $btnDelete.Width = 120; $btnDelete.ForeColor = [System.Drawing.Color]::Red; $bottomPanel.Controls.Add($btnDelete)
    # 4. 中央リストビュー (表示エリア)
    $listArchiveItems = New-Object System.Windows.Forms.ListView; $listArchiveItems.Dock = "Fill"; $listArchiveItems.View = "Details"; $listArchiveItems.FullRowSelect = $true; $listArchiveItems.MultiSelect = $true; $listArchiveItems.GridLines = $true; $form.Controls.Add($listArchiveItems); $listArchiveItems.BringToFront()
    $listArchiveItems.HideSelection = $false # フォーカスが外れても選択状態を表示する
    $listArchiveItems.Columns.Add("種類", 80) | Out-Null; $listArchiveItems.Columns.Add("プロジェクト", 150) | Out-Null; $listArchiveItems.Columns.Add("タスク", 250) | Out-Null; $listArchiveItems.Columns.Add("アーカイブ日", 120) | Out-Null; $listArchiveItems.Columns.Add("元の期日", 120) | Out-Null; $listArchiveItems.Columns.Add("カテゴリ", 100) | Out-Null

    # 5. データ読み込みと表示ロジック (検索機能付き)
    $loadData = {
        param([string]$searchTerm = "")
        $listArchiveItems.BeginUpdate()
        $listArchiveItems.Items.Clear()
        
        $archivedTasks = Read-ArchivedTasksFromCsv -filePath $script:ArchivedTasksFile
        $archivedProjects = Read-ArchivedProjectsFromJson -filePath $script:ArchivedProjectsFile
        $allItems = @($archivedProjects) + @($archivedTasks)

        if (-not [string]::IsNullOrWhiteSpace($searchTerm)) {
            $allItems = $allItems | Where-Object {
                $isProject = $_.PSObject.Properties.Name -contains 'ProjectName'
                $name = if ($isProject) { $_.ProjectName } else { $_.タスク }
                $name -like "*$searchTerm*"
            }
        }
        
        $sortedItems = $allItems | Sort-Object ArchivedDate -Descending

        foreach ($item in $sortedItems) {
            if ($null -eq $item) { continue } # Skip null items

            $isTask = $item.PSObject.Properties.Name -contains 'タスク'
            
            if ($isTask) {
                $lvi = New-Object System.Windows.Forms.ListViewItem("タスク")
                $lvi.SubItems.Add([string]$item.ProjectName) | Out-Null
                $lvi.SubItems.Add([string]$item.タスク) | Out-Null
                $lvi.SubItems.Add([string]$item.ArchivedDate) | Out-Null
                $lvi.SubItems.Add([string]$item.期日) | Out-Null
                $lvi.SubItems.Add([string]$item.カテゴリ) | Out-Null
            } else {
                $lvi = New-Object System.Windows.Forms.ListViewItem("プロジェクト")
                $lvi.SubItems.Add([string]$item.ProjectName) | Out-Null
                $lvi.SubItems.Add("") | Out-Null # タスク列は空
                $lvi.SubItems.Add([string]$item.ArchivedDate) | Out-Null
                $lvi.SubItems.Add([string]$item.ProjectDueDate) | Out-Null
                $lvi.SubItems.Add("") | Out-Null
            }
            $lvi.Tag = $item
            $listArchiveItems.Items.Add($lvi) | Out-Null
        }
        $listArchiveItems.EndUpdate()
    }

    # 6. イベントハンドラ
    $form.Add_Load({ & $loadData })
    $btnSearch.Add_Click({ & $loadData -searchTerm $textSearch.Text })
    $textSearch.Add_KeyDown({ if ($_.KeyCode -eq 'Enter') { & $loadData -searchTerm $textSearch.Text }})

    $listArchiveItems.Add_DoubleClick({
        $btnRestore.PerformClick()
    })

    $btnRestore.Add_Click({
        if ($listArchiveItems.SelectedItems.Count -eq 0) {
            [System.Windows.Forms.MessageBox]::Show("復元するアイテムを選択してください。", "情報", "OK", "Information"); return
        }

        $itemsToRestore = $listArchiveItems.SelectedItems | ForEach-Object { $_.Tag }
        # タスクは 'タスク' プロパティを持つ。プロジェクトは 'ProjectName' を持つが 'タスク' は持たない。
        # この区別により、'ProjectName' プロパティを持つアーカイブ済みタスクを正しくタスクとして扱う。
        $tasksToRestore = $itemsToRestore | Where-Object { $_.PSObject.Properties.Name -contains 'タスク' }
        $projectsToRestore = $itemsToRestore | Where-Object { $_.PSObject.Properties.Name -contains 'ProjectName' -and -not ($_.PSObject.Properties.Name -contains 'タスク') }

        $projectNames = ($projectsToRestore | ForEach-Object { 
            $pName = if ($_.ProjectName) { $_.ProjectName } else { "(名称不明)" }
            "- " + $pName 
        }) -join "`n"
        $taskNames = ($tasksToRestore | ForEach-Object { 
            $tName = if ($_.タスク) { $_.タスク } else { "(名称不明)" }
            "- " + $tName 
        }) -join "`n"
        
        $message = "以下の項目を復元しますか？`n"
        if ($projectNames) { $message += "`n[プロジェクト]`n$projectNames`n" }
        if ($taskNames) { $message += "`n[タスク]`n$taskNames`n" }
        
        $confirmResult = [System.Windows.Forms.MessageBox]::Show($message, "復元の確認", "YesNo", "Question")
        if ($confirmResult -ne 'Yes') { return }

        $form.Tag = "RELOAD"

        # プロジェクトを復元 (関連タスクも内部で復元される)
        foreach ($project in $projectsToRestore) {
            Restore-Project -projectToRestore $project
        }

        # プロジェクトに属さないタスクを復元
        $standaloneTasks = @($tasksToRestore | Where-Object {
            $task = $_
            $isStandalone = $true
            foreach ($proj in $projectsToRestore) { if ($task.ProjectID -eq $proj.ProjectID) { $isStandalone = $false; break } }
            $isStandalone
        })
        if ($standaloneTasks.Count -gt 0) {
            Restore-Task -tasksToRestore $standaloneTasks
        }

        # After restore, reload the data in the archive view
        & $loadData -searchTerm $textSearch.Text
    })

    $btnDelete.Add_Click({
        if ($listArchiveItems.SelectedItems.Count -eq 0) { return }
        
        if ([System.Windows.Forms.MessageBox]::Show("選択した項目を完全に削除しますか？`nこの操作は取り消せません。", "削除の確認", "YesNo", "Warning") -ne 'Yes') { return }
        
        $itemsToDelete = $listArchiveItems.SelectedItems | ForEach-Object { $_.Tag }
        $tasksToDelete = $itemsToDelete | Where-Object { $_.PSObject.Properties.Name -contains 'タスク' }
        $projectsToDelete = $itemsToDelete | Where-Object { $_.PSObject.Properties.Name -contains 'ProjectName' -and -not ($_.PSObject.Properties.Name -contains 'タスク') }
        
        $archivedTasks = Read-ArchivedTasksFromCsv -filePath $script:ArchivedTasksFile
        $archivedProjects = Read-ArchivedProjectsFromJson -filePath $script:ArchivedProjectsFile
        
        $taskIdsToDelete = [System.Collections.ArrayList]::new()
        if ($tasksToDelete) { $taskIdsToDelete.AddRange(@($tasksToDelete.ID)) }
        
        $projectIdsToDelete = [System.Collections.ArrayList]::new()
        if ($projectsToDelete) { 
            $projectIdsToDelete.AddRange(@($projectsToDelete.ProjectID)) 
            # プロジェクトに含まれるタスクも削除対象に追加
            $projectTasks = $archivedTasks | Where-Object { $_.ProjectID -in @($projectsToDelete.ProjectID) }
            if ($projectTasks) { $taskIdsToDelete.AddRange(@($projectTasks.ID)) }
        }
        
        if ($taskIdsToDelete.Count -gt 0) {
            $updatedTasks = @($archivedTasks | Where-Object { $_.ID -notin $taskIdsToDelete })
            Write-ArchivedTasksToCsv -filePath $script:ArchivedTasksFile -data $updatedTasks
        }
        
        if ($projectIdsToDelete.Count -gt 0) {
            $updatedProjects = @($archivedProjects | Where-Object { $_.ProjectID -notin $projectIdsToDelete })
            Write-ArchivedProjectsToJson -filePath $script:ArchivedProjectsFile -dataObject $updatedProjects
        }
        
        & $loadData -searchTerm $textSearch.Text
    })

    # フォームを表示
    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    [void]$form.ShowDialog($parentForm)
    $tag = $form.Tag
    $form.Dispose()
    return $tag
}
# ===================================================================
# レポート機能関連 (v12.0)
# ===================================================================

function Open-LatestReport {
    param($parentForm)

    try {
        $reportFiles = Get-ChildItem -Path $PSScriptRoot -Filter "Report_*.html" | Sort-Object Name -Descending

        if ($reportFiles.Count -eq 0) {
            $confirmResult = [System.Windows.Forms.MessageBox]::Show(
                "レポートファイルが見つかりません。`n`n今すぐ新しいレポートを生成しますか？",
                "レポートの生成",
                [System.Windows.Forms.MessageBoxButtons]::YesNo,
                [System.Windows.Forms.MessageBoxIcon]::Question,
                [System.Windows.Forms.MessageBoxDefaultButton]::Button1,
                0,
                $parentForm
            )
            if ($confirmResult -eq 'Yes') {
                Show-ReportForm -parentForm $parentForm
            }
            return
        }

        $latestReport = $reportFiles[0]

        # データファイルがレポートより新しいか確認する
        $filesToCheck = @($script:TasksFile, $script:TimeLogsFile, $script:ProjectsFile, $script:ArchivedTasksFile, $script:ArchivedProjectsFile) | Where-Object { $_ }
        $latestDataTime = ($filesToCheck | Where-Object { Test-Path $_ } | ForEach-Object { (Get-Item $_).LastWriteTime } | Measure-Object -Maximum).Maximum

        if ($latestDataTime -and $latestDataTime -gt $latestReport.LastWriteTime) {
            $msg = "データが更新されています。最新レポートは古くなっています。今すぐ新しいレポートを生成しますか？"
            $confirm = [System.Windows.Forms.MessageBox]::Show($msg, "レポート更新確認", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Question)
            if ($confirm -eq 'Yes') {
                try {
                    # デフォルトの期間は全ログの範囲（無ければ過去30日）
                    if ($script:AllTimeLogs -and $script:AllTimeLogs.Count -gt 0) {
                        $earliest = ($script:AllTimeLogs | ForEach-Object { [datetime]$_.StartTime } | Sort-Object | Select-Object -First 1).Date
                        $startDate = $earliest
                    } else {
                        $startDate = (Get-Date).AddDays(-30).Date
                    }
                    $endDate = (Get-Date).Date

                    # フィルタ処理は既存の生成ロジックに準拠
                    $taskLookup = @{}; $script:AllTasks | ForEach-Object { $taskLookup[$_.ID] = $_ }
                    $projectLookup = @{}; $script:Projects | ForEach-Object { $projectLookup[$_.ProjectID] = $_.ProjectName }

                    $filteredData = $script:AllTimeLogs | Where-Object {
                        $_.TaskID -and $_.StartTime -and $_.EndTime -and
                        ([datetime]$_.StartTime).Date -le $endDate -and
                        ([datetime]$_.EndTime).Date -ge $startDate
                    } | ForEach-Object {
                        $task = $taskLookup[$_.TaskID]
                        if ($task -and $task.ProjectID -and $projectLookup.ContainsKey($task.ProjectID)) {
                            [PSCustomObject]@{
                                ProjectName = $projectLookup[$task.ProjectID]
                                Category    = if ([string]::IsNullOrWhiteSpace($task.カテゴリ)) { "(未分類)" } else { $task.カテゴリ }
                                Status      = if ([string]::IsNullOrWhiteSpace($task.進捗度)) { "(未設定)" } else { $task.進捗度 }
                                Hours       = ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalHours
                                Date        = ([datetime]$_.StartTime).Date
                            }
                        }
                    } | Where-Object { $_ }

                    $totalHours = ($filteredData | Measure-Object -Property 'Hours' -Sum).Sum
                    $timestamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
                    $fileName = "Report_$($timestamp).html"
                    $filePath = Join-Path -Path $script:AppRoot -ChildPath $fileName

                    $success = Export-ReportToHtml -startDate $startDate -endDate $endDate -totalHours $totalHours -data $filteredData -filePath $filePath -projects $script:Projects -tasks $script:AllTasks

                    if ($success) { Start-Process -FilePath $filePath; return }
                } catch {
                    [System.Windows.Forms.MessageBox]::Show("自動生成中にエラーが発生しました: `n$($_.Exception.Message)", "エラー", "OK", "Error")
                }
            }
        }

        Start-Process -FilePath $latestReport.FullName

    } catch {
        [System.Windows.Forms.MessageBox]::Show("レポートを開く際にエラーが発生しました: `n$($_.Exception.Message)", "エラー", "OK", "Error")
    }
}

function Get-GroupedTimeData {
    param([array]$logsWithInfo)

    $byProject = $logsWithInfo | Group-Object ProjectName | ForEach-Object {
        $totalSeconds = ($_.Group | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum
        @{ Name = $_.Name; TotalSeconds = $totalSeconds }
    }

    $byCategory = $logsWithInfo | Group-Object Category | ForEach-Object {
        $totalSeconds = ($_.Group | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum
        @{ Name = $_.Name; TotalSeconds = $totalSeconds }
    }

    $byTask = $logsWithInfo | Group-Object TaskName | ForEach-Object {
        $totalSeconds = ($_.Group | ForEach-Object { ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalSeconds } | Measure-Object -Sum).Sum
        @{ Name = $_.Name; TotalSeconds = $totalSeconds }
    }

    return [PSCustomObject]@{ 
        ByProject = $byProject
        ByCategory = $byCategory
        ByTask = $byTask
        Values = @($byProject.TotalSeconds) + @($byCategory.TotalSeconds) + @($byTask.TotalSeconds)
    }
}

function Add-SummarySection {
    param(
        [System.Windows.Forms.TextBox]$textBox,
        [string]$title,
        [string]$content
    )
    if ([string]::IsNullOrWhiteSpace($content)) { return }

    $separator = "`r`n" + ("-" * 60) + "`r`n"
    $textBox.AppendText("`r`n" + $title.ToUpper() + $separator)
    $textBox.AppendText($content + "`r`n")
}

function Get-ReportInsights {
    param(
        [psobject]$ReportData
    )

    $insights = "💡 詳細分析コメント (インサイト)`r`n"
    
    # Insight 1: [良好]
    if ($ReportData.GroupedByCategory.Count -gt 1) {
        $insights += "• [良好]  時間の分類が適切に行われています。時間の使い方の内訳が明確で、振り返りやすい状態です。`r`n"
    } else {
        $insights += "• [情報]  記録された時間のカテゴリが1つのみです。複数のカテゴリに分類すると、時間の使い方をより詳細に分析できます。`r`n"
    }

    # Insight 2: [傾向]
    $totalSeconds = $ReportData.TotalLoggedTimeSeconds
    if ($totalSeconds -gt 0) {
        # Find the top category
        $topCategory = $ReportData.GroupedByCategory | Sort-Object TotalSeconds -Descending | Select-Object -First 1
        if ($topCategory) {
            $percentage = ($topCategory.TotalSeconds / $totalSeconds) * 100
            $insights += "• [傾向]  「$($topCategory.Name)」の時間は全体の $($percentage.ToString('F1'))% を占めています。このカテゴリの時間を調整したい場合、タイムブロッキングなどの手法が有効です。`r`n"
        }
    }

    $hints = "`r`n🚀 改善のためのヒント`r`n"
    $hints += "• [工夫]「ポモドーロ・テクニック」: 大きなタスクに着手する際は、25分集中＋5分休憩のサイクルで区切ると、集中力が持続しやすくなります。`r`n"
    $hints += "• [工夫]「タイムブロッキング」: 意図的に時間を確保したいカテゴリがあれば、カレンダー上でそのタスク専用の時間をあらかじめブロックする手法も有効です。`r`n"

    return ($insights + $hints).Trim()
}



# ===================================================================
# カンバンとカレンダービューの関数
# ===================================================================

function Update-KanbanView {
    # 1. Clear all list boxes
    foreach ($listBox in $script:kanbanLists.Values) {
        $listBox.Items.Clear()
    }

    # 2. Get tasks to display, applying filters
    [array]$tasksToDisplay = if ($script:hideCompletedMenuItem -and $script:hideCompletedMenuItem.Checked) {
        $script:AllTasks | Where-Object { $_.進捗度 -ne '完了済み' }
    } else {
        $script:AllTasks
    }
    if ($script:CurrentCategoryFilter -ne '(すべて)') {
        $tasksToDisplay = $tasksToDisplay | Where-Object { $_.カテゴリ -eq $script:CurrentCategoryFilter }
    }

    # 3. Add tasks to the corresponding list box
    foreach ($task in $tasksToDisplay) {
        $status = $task.進捗度
        # 進捗度が null または空でないことを確認してから処理する
        if (-not [string]::IsNullOrEmpty($status) -and $script:kanbanLists.ContainsKey($status)) {
            $script:kanbanLists[$status].Items.Add($task) | Out-Null
        }
    }
}

function Update-CalendarGrid {
    param([datetime]$dateInMonth)

    # ToolTipコンポーネントがなければ作成する
    if ($null -eq $script:calendarToolTip) {
        $script:calendarToolTip = New-Object System.Windows.Forms.ToolTip
    }

    $colors = Get-ThemeColors -IsDarkMode $script:isDarkMode

    # Initialize globally filtered tasks for calendar
    [array]$globallyFilteredTasks = if ($script:hideCompletedMenuItem -and $script:hideCompletedMenuItem.Checked) {
        $script:AllTasks | Where-Object { $_.進捗度 -ne '完了済み' }
    } else {
        $script:AllTasks
    }
    if ($script:CurrentCategoryFilter -ne '(すべて)') {
        $globallyFilteredTasks = $globallyFilteredTasks | Where-Object { $_.カテゴリ -eq $script:CurrentCategoryFilter }
    }

    # GetNewClosure() 対策: スクリプトスコープの変数をローカル変数にキャプチャ
    $localCalendarDayBoldFont = $script:calendarDayBoldFont
    $localCalendarItemFont = $script:calendarItemFont
    $localAllEvents = $script:AllEvents
    $localProjects = $script:Projects
    $localIsDarkMode = $script:isDarkMode
    $localCalendarGrid = $script:calendarGrid

    # --- 設定値の取得 ---
    $weekStart = if ($null -ne $script:Settings.CalendarWeekStart) { [int]$script:Settings.CalendarWeekStart } else { 0 } # 0: Sunday, 1: Monday
    $colorWeekend = if ($null -ne $script:Settings.ColorWeekend) { [bool]$script:Settings.ColorWeekend } else { $true }

    $script:calendarGrid.SuspendLayout()

    # Dispose existing controls to prevent memory leaks
    foreach ($control in $script:calendarGrid.Controls) {
        $control.Dispose()
    }
    $script:calendarGrid.Controls.Clear()

    # --- 1. Add Day of Week Headers (CultureInfo-aware) ---
    $culture = [System.Globalization.CultureInfo]::CurrentCulture
    $daysOfWeek = $culture.DateTimeFormat.AbbreviatedDayNames
    # 設定に基づいて週の始まりを決定
    $firstDayOfWeek = $weekStart
    for ($i = 0; $i -lt 7; $i++) {
        $dayIndex = ($firstDayOfWeek + $i) % 7
        $lbl = New-Object System.Windows.Forms.Label
        $lbl.Text = $daysOfWeek[$dayIndex]
        $lbl.Dock = "Fill"
        $lbl.TextAlign = "MiddleCenter"
        $lbl.Font = $script:calendarGridHeaderFont # USE GLOBAL FONT
        if (-not $script:isDarkMode) {
            if ($dayIndex -eq [int][System.DayOfWeek]::Sunday) { $lbl.ForeColor = [System.Drawing.Color]::Red } 
            elseif ($dayIndex -eq [int][System.DayOfWeek]::Saturday) { $lbl.ForeColor = [System.Drawing.Color]::Blue }
        } else {
            if ($dayIndex -eq [int][System.DayOfWeek]::Sunday) { $lbl.ForeColor = [System.Drawing.Color]::FromArgb(255, 120, 120) } 
            elseif ($dayIndex -eq [int][System.DayOfWeek]::Saturday) { $lbl.ForeColor = [System.Drawing.Color]::FromArgb(120, 150, 255) }
            else { $lbl.ForeColor = $colors.ForeColor }
        }
        [void]$script:calendarGrid.Controls.Add($lbl, $i, 0)
    }

    # --- 2. Calculate Calendar Dates (CultureInfo-aware) ---
    $firstDayOfMonth = Get-Date -Year $dateInMonth.Year -Month $dateInMonth.Month -Day 1
    # 設定に基づいたオフセット計算
    $startOffset = [int]$firstDayOfMonth.DayOfWeek - $firstDayOfWeek
    if ($startOffset -lt 0) { $startOffset += 7 }
    $currentDate = $firstDayOfMonth.AddDays(-$startOffset)

    # --- 4. Populate Calendar Cells ---
    for ($row = 1; $row -le 6; $row++) {
        for ($col = 0; $col -lt 7; $col++) {

            if ($script:calendarGrid.Width -le 1) {
                $placeholderPanel = New-Object System.Windows.Forms.Panel
                $placeholderPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
                $placeholderPanel.Margin = New-Object System.Windows.Forms.Padding(1)
                [void]$script:calendarGrid.Controls.Add($placeholderPanel, $col, $row)
                $currentDate = $currentDate.AddDays(1)
                continue
            }
            $itemsPanel = New-Object System.Windows.Forms.Panel
            $itemsPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
            $itemsPanel.BorderStyle = [System.Windows.Forms.BorderStyle]::None
            $itemsPanel.Margin = New-Object System.Windows.Forms.Padding(0)
            $itemsPanel.Tag = $currentDate
            
            # --- Drag & Drop Support ---
            $itemsPanel.AllowDrop = $true
            $itemsPanel.Add_DragEnter({
                param($s, $e)
                if ($e.Data.GetDataPresent([PSCustomObject].GetType().FullName)) {
                    $e.Effect = [System.Windows.Forms.DragDropEffects]::Move
                } else {
                    $e.Effect = [System.Windows.Forms.DragDropEffects]::None
                }
            })
            $itemsPanel.Add_DragDrop({
                param($s, $e)
                $dropDate = $s.Tag
                $dragData = $e.Data.GetData([PSCustomObject].GetType().FullName)
                
                if ($dragData) {
                    $itemType = $dragData.ItemType # "Task", "Project", or "Event"
                    $itemData = $dragData.ItemData # The actual object
                    
                    if ($itemType -eq "Task") {
                        $task = $script:AllTasks | Where-Object { $_.ID -eq $itemData.ID } | Select-Object -First 1
                        if ($task) {
                            $originalTime = if ($task.期日) { try { [datetime]$task.期日 } catch { $dropDate } } else { $dropDate }
                            $newDate = $dropDate.Date.Add($originalTime.TimeOfDay)
                            $task.期日 = $newDate.ToString("yyyy-MM-dd HH:mm")
                            Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
                        }
                    } elseif ($itemType -eq "Project") {
                        $project = $script:Projects | Where-Object { $_.ProjectID -eq $itemData.ProjectID } | Select-Object -First 1
                        if ($project) {
                            $originalTime = if ($project.ProjectDueDate) { try { [datetime]$project.ProjectDueDate } catch { $dropDate } } else { $dropDate }
                            $newDate = $dropDate.Date.Add($originalTime.TimeOfDay)
                            $project.ProjectDueDate = $newDate.ToString("yyyy-MM-dd HH:mm")
                            Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
                        }
                    } elseif ($itemType -eq "Event") {
                         $originalDateString = if ($dragData.OriginalDate) { $dragData.OriginalDate.ToString("yyyy-MM-dd") } else { "" }
                         $newDateString = $dropDate.ToString("yyyy-MM-dd")
                         
                         if ($originalDateString -and $originalDateString -ne $newDateString) {
                             $oldProp = $script:AllEvents.PSObject.Properties | Where-Object { $_.Name -eq $originalDateString } | Select-Object -First 1
                             if (-not $oldProp) { return }
                             $eventsOnOld = [System.Collections.ArrayList]::new(); $eventsOnOld.AddRange($oldProp.Value)
                             
                             $evtToMove = $null
                             foreach($evt in $eventsOnOld) { if ($evt.ID -eq $itemData.ID) { $evtToMove = $evt; break } }
                             
                             if ($evtToMove) {
                                 $eventsOnOld.Remove($evtToMove)
                                 $oldProp.Value = $eventsOnOld.ToArray()
                                 
                                 if (-not ($script:AllEvents.PSObject.Properties | Where-Object { $_.Name -eq $newDateString })) {
                                     $script:AllEvents | Add-Member -MemberType NoteProperty -Name $newDateString -Value @()
                                 }
                                 $eventsOnNew = [System.Collections.ArrayList]::new(); $eventsOnNew.AddRange($script:AllEvents.PSObject.Properties[$newDateString].Value)
                                 
                                 if ($evtToMove.PSObject.Properties['StartTime']) {
                                     $originalStart = [datetime]$evtToMove.StartTime
                                     $newStart = $dropDate.Date.Add($originalStart.TimeOfDay)
                                     $diff = $newStart - $originalStart
                                     
                                     $evtToMove.StartTime = $newStart.ToString("o")
                                     if ($evtToMove.EndTime) { $evtToMove.EndTime = ([datetime]$evtToMove.EndTime).Add($diff).ToString("o") }
                                 }
                                 $eventsOnNew.Add($evtToMove)
                                 $script:AllEvents.PSObject.Properties[$newDateString].Value = $eventsOnNew
                                 Save-Events
                             }
                         }
                    }
                    Update-AllViews
                }
            })

            # --- Style based on the date ---
            $backColor = $colors.ControlBack

            if ($currentDate.Month -ne $dateInMonth.Month) {
                $backColor = $colors.BackColor
            } elseif ($currentDate.Date -eq (Get-Date).Date) {
                $backColor = $colors.TodayBack
            } else {
                # In-month days
                if ($colorWeekend) {
                    if ($currentDate.DayOfWeek -eq [System.DayOfWeek]::Sunday) {
                        $backColor = $colors.SundayBack
                    } elseif ($currentDate.DayOfWeek -eq [System.DayOfWeek]::Saturday) {
                        $backColor = $colors.SaturdayBack
                    }
                }
            }
            $itemsPanel.BackColor = $backColor

            # --- Add Click Events ---
            $clickAction = {
                param($source, $e)
                $control = $source
                while ($control -and -not ($control -is [System.Windows.Forms.Panel] -and $control.Tag -is [datetime])) {
                    $control = $control.Parent
                }
                if (-not $control) { return }

                $clickedDate = $control.Tag
                $script:selectedCalendarDate = $clickedDate
                if ($script:calendarGrid) { $script:calendarGrid.Tag = $clickedDate } # 選択状態を描画用に保存
                Update-DayInfoPanel -date $clickedDate
                Update-TimelineView -date $clickedDate
                
                foreach($c in $script:calendarGrid.Controls) {
                    if ($c -is [System.Windows.Forms.Panel]) {
                        $c.Invalidate() # 再描画を促して枠線を更新
                    }
                }
            }
            $itemsPanel.Add_Click($clickAction)

            # --- Right-click Context Menu for adding events ---
            $dayContextMenu = New-Object System.Windows.Forms.ContextMenuStrip
            if ($script:isDarkMode) {
                $dayContextMenu.Renderer = New-Object DarkModeRenderer
            } else {
                $dayContextMenu.Renderer = New-Object System.Windows.Forms.ToolStripProfessionalRenderer
            }
            $addEventMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem("イベントを追加")
            $addEventMenuItem.Add_Click({
                param($s, $ea)
                $menu = $s.Owner
                $panel = $menu.SourceControl
                while ($panel -and -not ($panel.Tag -is [datetime])) {
                    $panel = $panel.Parent
                }
                if ($panel) {
                    $date = $panel.Tag
                    Start-AddNewEvent -initialDate $date
                }
            })
            $dayContextMenu.Items.Add($addEventMenuItem)
            $itemsPanel.ContextMenuStrip = $dayContextMenu

            # --- Custom Drawing for Events and TimeLogs ---
            $itemsPanel.Add_Paint({
                param($s, $e)
                $g = $e.Graphics
                $rect = $s.ClientRectangle
                $date = $s.Tag

                # テーマカラーの取得（取得失敗時のフォールバックを追加）
                $colors = $null
                try { $colors = Get-ThemeColors -IsDarkMode $localIsDarkMode } catch {}
                if (-not $colors) {
                    if ($localIsDarkMode) {
                        $colors = [PSCustomObject]@{ GridLine = [System.Drawing.Color]::FromArgb(80, 80, 80); ForeColor = [System.Drawing.Color]::White }
                    } else {
                        $colors = [PSCustomObject]@{ GridLine = [System.Drawing.Color]::LightGray; ForeColor = [System.Drawing.Color]::Black }
                    }
                }

                $dateStr = $date.ToString("yyyy-MM-dd")
                
                # --- Draw Cell Border ---
                $isToday = $date.Date -eq (Get-Date).Date
                $currentSelected = if ($localCalendarGrid.Tag) { $localCalendarGrid.Tag } else { (Get-Date) }
                $isSelected = ($currentSelected -and $date.Date -eq $currentSelected.Date)
                if ($isSelected) {
                    $selectionColor = $colors.HighlightBack
                    $borderPen = New-Object System.Drawing.Pen($selectionColor, 3)
                    $g.DrawRectangle($borderPen, 1, 1, $rect.Width - 3, $rect.Height - 3)
                    $borderPen.Dispose()
                } elseif ($isToday) {
                    # 「今日」の日付を際立たせるための枠線を追加
                    $todayBorderColor = if ($localIsDarkMode) { [System.Drawing.Color]::Goldenrod } else { [System.Drawing.Color]::OrangeRed }
                    $borderPen = New-Object System.Drawing.Pen($todayBorderColor, 2)
                    # グリッド線の中に描画
                    $g.DrawRectangle($borderPen, 1, 1, $rect.Width - 3, $rect.Height - 3)
                    $borderPen.Dispose()
                } else {
                    $borderPen = New-Object System.Drawing.Pen($colors.GridLine)
                    $g.DrawLine($borderPen, 0, $rect.Height - 1, $rect.Width, $rect.Height - 1) # Bottom line
                    $g.DrawLine($borderPen, $rect.Width - 1, 0, $rect.Width - 1, $rect.Height) # Right line
                    $borderPen.Dispose()
                }
                
                # Text Drawing
                $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]$colors.ForeColor)
                
                # --- Items List (Events > Projects > Tasks) ---
                $currentY = 20
                $lineHeight = 14
                $sfItem = New-Object System.Drawing.StringFormat
                $sfItem.Trimming = [System.Drawing.StringTrimming]::EllipsisCharacter
                $sfItem.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap

                # 1. Events
                $evts = if ($localAllEvents.PSObject.Properties[$dateStr]) { @($localAllEvents.PSObject.Properties[$dateStr].Value) } else { @() }
                $eventBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100, 88, 153)) # Modern Purple
                foreach ($evt in $evts) {
                    if ($currentY + $lineHeight -gt $rect.Height) { break }
                    $itemRect = [System.Drawing.RectangleF]::new(5, $currentY, $rect.Width - 7, $lineHeight)
                    $g.DrawString("● " + $evt.Title, $localCalendarItemFont, $eventBrush, $itemRect, $sfItem)
                    $currentY += $lineHeight
                }
                $eventBrush.Dispose()

                # 2. Project Due Dates
                $projsDue = $script:Projects | Where-Object { -not [string]::IsNullOrWhiteSpace($_.ProjectDueDate) -and $(try { ([datetime]$_.ProjectDueDate).Date -eq $date.Date } catch { $false }) }
                $projBrush = if ($script:isDarkMode) { New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(218, 165, 32)) } else { New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(184, 134, 11)) } # Goldenrod / DarkGoldenrod
                $projsDue = $localProjects | Where-Object { -not [string]::IsNullOrWhiteSpace($_.ProjectDueDate) -and $(try { ([datetime]$_.ProjectDueDate).Date -eq $date.Date } catch { $false }) }
                $projBrush = if ($localIsDarkMode) { New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(218, 165, 32)) } else { New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(184, 134, 11)) } # Goldenrod / DarkGoldenrod
                foreach ($proj in $projsDue) {
                    if ($currentY + $lineHeight -gt $rect.Height) { break }
                    $itemRect = [System.Drawing.RectangleF]::new(5, $currentY, $rect.Width - 7, $lineHeight)
                    $g.DrawString("■ " + $proj.ProjectName, $localCalendarItemFont, $projBrush, $itemRect, $sfItem)
                    $currentY += $lineHeight
                }
                $projBrush.Dispose()

                # 3. Task Due Dates
                $tasksDue = $globallyFilteredTasks | Where-Object { -not [string]::IsNullOrWhiteSpace($_.期日) -and $(try { ([datetime]$_.期日).Date -eq $date.Date } catch { $false }) }
                $taskBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 130, 180)) # SteelBlue for both modes
                foreach ($task in $tasksDue) {
                    if ($currentY + $lineHeight -gt $rect.Height) { break }
                    $itemRect = [System.Drawing.RectangleF]::new(5, $currentY, $rect.Width - 7, $lineHeight)
                    $g.DrawString("✔ " + $task.タスク, $localCalendarItemFont, $taskBrush, $itemRect, $sfItem)
                    $currentY += $lineHeight
                }
                $taskBrush.Dispose()
                
                $sfItem.Dispose()
                
                # Day Number (背景の上に描画するために最後に移動)
                $dayColor = if ($date.Month -eq $dateInMonth.Month) { $colors.ForeColor } else { [System.Drawing.Color]::DimGray }
                $dayBrush = New-Object System.Drawing.SolidBrush($dayColor)
                $dayStr = $date.Day.ToString()
                $daySize = $g.MeasureString($dayStr, $localCalendarDayBoldFont)
                $g.DrawString($dayStr, $localCalendarDayBoldFont, $dayBrush, $rect.Width - $daySize.Width - 4, 2)
                $dayBrush.Dispose()

                $textBrush.Dispose()
            }.GetNewClosure())

                        $script:calendarGrid.Controls.Add($itemsPanel, $col, $row); $currentDate = $currentDate.AddDays(1)
        }
    }

    $script:calendarGrid.ResumeLayout($true)
}
function Initialize-CalendarView {
    $script:currentCalendarDate = (Get-Date)
    $script:selectedCalendarDate = (Get-Date)
    if ($script:calendarGrid) { $script:calendarGrid.Tag = $script:selectedCalendarDate }
    $script:lblMonthYear.Text = $script:currentCalendarDate.ToString("yyyy年 MM月")

    # Navigation button events
    if (-not $script:isCalendarNavEventsAttached) {
        $script:btnPrevYear.Add_Click({ 
            $script:currentCalendarDate = $script:currentCalendarDate.AddYears(-1)
            $script:lblMonthYear.Text = $script:currentCalendarDate.ToString("yyyy年 MM月")
            Update-CalendarGrid -dateInMonth $script:currentCalendarDate
        })
        
        $script:btnPrevMonth.Add_Click({ 
            $script:currentCalendarDate = $script:currentCalendarDate.AddMonths(-1)
            $script:lblMonthYear.Text = $script:currentCalendarDate.ToString("yyyy年 MM月")
            Update-CalendarGrid -dateInMonth $script:currentCalendarDate
        })

        $script:btnNextMonth.Add_Click({ 
            $script:currentCalendarDate = $script:currentCalendarDate.AddMonths(1)
            $script:lblMonthYear.Text = $script:currentCalendarDate.ToString("yyyy年 MM月")
            Update-CalendarGrid -dateInMonth $script:currentCalendarDate
        })

        $script:btnNextYear.Add_Click({ 
            $script:currentCalendarDate = $script:currentCalendarDate.AddYears(1)
            $script:lblMonthYear.Text = $script:currentCalendarDate.ToString("yyyy年 MM月")
            Update-CalendarGrid -dateInMonth $script:currentCalendarDate
        })
        
        $script:isCalendarNavEventsAttached = $true
    }
}

function New-DayInfoCard {
    param(
        [string]$ItemType,
        [string]$Title,
        [string]$Details,
        [System.Drawing.Color]$IndicatorColor,
        [System.Windows.Forms.Button]$EditButton
    )

    $cardHeight = 40
    if (-not [string]::IsNullOrEmpty($Details)) {
        $cardHeight = 55
    }

    # Main card panel
    $cardPanel = New-Object System.Windows.Forms.Panel
    $cardPanel.Size = New-Object System.Drawing.Size(300, $cardHeight) # Width will be adjusted by FlowLayoutPanel
    $cardPanel.Margin = "5, 5, 5, 0"
    $colors = Get-ThemeColors -IsDarkMode $script:isDarkMode
    $cardPanel.BackColor = $colors.ControlBack
    $cardPanel.BorderStyle = 'FixedSingle'

    # Color indicator bar
    $colorBar = New-Object System.Windows.Forms.Panel
    $colorBar.Dock = 'Left'
    $colorBar.Width = 5
    $colorBar.BackColor = $IndicatorColor
    $cardPanel.Controls.Add($colorBar) | Out-Null

    # Type label (e.g., [イベント])
    $typeLabel = New-Object System.Windows.Forms.Label
    $typeLabel.Text = "[$ItemType]"
    $typeLabel.Font = $script:dayInfoCardTypeFont
    $typeLabel.ForeColor = $IndicatorColor
    $typeLabel.Location = "10, 5"
    $typeLabel.AutoSize = $true
    $cardPanel.Controls.Add($typeLabel) | Out-Null

    # Title label
    $titleLabel = New-Object System.Windows.Forms.Label
    $titleLabel.Text = $Title
    $titleLabel.Font = $script:dayInfoCardTitleFont
    $titleLabel.ForeColor = if ($script:isDarkMode) { [System.Drawing.Color]::White } else { [System.Drawing.SystemColors]::ControlText }
    $titleLabel.Location = "15, 22"
    $titleLabel.AutoSize = $true
    $cardPanel.Controls.Add($titleLabel) | Out-Null

    # Details label (optional)
    if (-not [string]::IsNullOrEmpty($Details)) {
        $detailsLabel = New-Object System.Windows.Forms.Label
        $detailsLabel.Text = $Details
        $detailsLabel.Font = $script:dayInfoCardDetailsFont
        $detailsLabel.ForeColor = if ($script:isDarkMode) { [System.Drawing.Color]::Silver } else { [System.Drawing.Color]::DimGray }
        $detailsLabel.Location = "20, 38"
        $detailsLabel.AutoSize = $true
        $cardPanel.Controls.Add($detailsLabel) | Out-Null
    }

    # Edit button (optional)
    if ($EditButton) {
        $cardPanel.Controls.Add($EditButton) | Out-Null
    }

    return $cardPanel
}

function Update-DayInfoPanel {
    param([datetime]$date)

    if ($script:dayInfoEventsPanel.IsDisposed -or ($script:dayInfoTasksPanel -and $script:dayInfoTasksPanel.IsDisposed)) {
        return
    }

    $script:dayInfoEventsPanel.SuspendLayout()
    if ($script:dayInfoTasksPanel) { $script:dayInfoTasksPanel.SuspendLayout() }

    # Clear previous cards from both panels
    if ($script:dayInfoEventsPanel.Controls.Count -gt 0) {
        foreach ($control in $script:dayInfoEventsPanel.Controls) {
            $control.Dispose()
        }
        $script:dayInfoEventsPanel.Controls.Clear()
    }
    if ($script:dayInfoTasksPanel -and $script:dayInfoTasksPanel.Controls.Count -gt 0) {
        foreach ($control in $script:dayInfoTasksPanel.Controls) {
            $control.Dispose()
        }
        $script:dayInfoTasksPanel.Controls.Clear()
    }

    $dayInfoGroupBox.Text = "詳細: " + $date.ToString("yyyy/MM/dd (ddd)")
    
    # --- 1. Collect all items for the day ---
    $eventsList = [System.Collections.ArrayList]::new()
    $tasksList = [System.Collections.ArrayList]::new()

    # 1a. Events
    $dateStr = $date.ToString("yyyy-MM-dd")
    $eventsOnDay = if ($script:AllEvents.PSObject.Properties[$dateStr]) { $script:AllEvents.PSObject.Properties[$dateStr].Value } else { @() }
    if ($eventsOnDay -isnot [array]) { $eventsOnDay = @($eventsOnDay) }

    foreach($evt in $eventsOnDay) {
        $isAllDay = $evt.PSObject.Properties['IsAllDay'] -and $evt.IsAllDay
        $startTime = if (-not $isAllDay -and $evt.PSObject.Properties['StartTime']) { try { [datetime]$evt.StartTime } catch { $null } } else { $null }
        
        [void]$eventsList.Add([PSCustomObject]@{
            SortTime = if ($isAllDay) { $date.Date } elseif ($startTime) { $startTime } else { $date.Date.AddDays(1) }
            ItemType = "Event"
            ItemObject = $evt
        })
    }

    # 1b. Project Due Dates
    $projectsOnDay = $script:Projects | Where-Object { -not [string]::IsNullOrWhiteSpace($_.ProjectDueDate) -and $(try { ([datetime]$_.ProjectDueDate).Date -eq $date.Date } catch { $false }) }
    foreach($proj in $projectsOnDay) {
        [void]$tasksList.Add([PSCustomObject]@{
            SortTime = $date.Date.AddHours(1) # After all-day events
            ItemType = "Project"
            ItemObject = $proj
        })
    }

    # 1c. Task Due Dates
    $tasksOnDay = $script:AllTasks | Where-Object { -not [string]::IsNullOrWhiteSpace($_.期日) -and $(try { ([datetime]$_.期日).Date -eq $date.Date } catch { $false }) }
    
    if ($script:hideCompletedMenuItem -and $script:hideCompletedMenuItem.Checked) {
        $tasksOnDay = $tasksOnDay | Where-Object { $_.進捗度 -ne '完了済み' }
    }

    foreach($task in $tasksOnDay) {
        [void]$tasksList.Add([PSCustomObject]@{
            SortTime = $date.Date.AddHours(2) # After project due dates
            ItemType = "Task"
            ItemObject = $task
        })
    }

    # --- 2. Sort and display items ---
    $addCardToPanel = {
        param($panel, $item)
        $itemType = $item.ItemType
        $itemObject = $item.ItemObject
        $card = $null

        switch ($itemType) {
            "Event" {
                $evtTitle = $itemObject.Title
                $details = ""
                if ($itemObject.PSObject.Properties['IsAllDay'] -and $itemObject.IsAllDay) {
                    $details = "終日"
                } elseif ($itemObject.PSObject.Properties['StartTime']) {
                    try {
                        $startDt = [datetime]$itemObject.StartTime
                        $startTimeStr = $startDt.ToString("HH:mm")
                        if ($itemObject.PSObject.Properties['EndTime'] -and $itemObject.EndTime) {
                            $endDt = [datetime]$itemObject.EndTime
                            if ($startDt.Date -ne $endDt.Date) {
                                $details = "$startTimeStr - $($endDt.ToString("M/d HH:mm"))"
                            } else {
                                $details = "$startTimeStr - $($endDt.ToString("HH:mm"))"
                            }
                        } else {
                            $details = $startTimeStr
                        }
                    } catch {}
                }
                $card = New-DayInfoCard -ItemType "イベント" -Title $evtTitle -Details $details -IndicatorColor ([System.Drawing.Color]::DarkMagenta)
            }
            "Project" {
                $card = New-DayInfoCard -ItemType "プロジェクト期日" -Title $itemObject.ProjectName -Details "" -IndicatorColor ([System.Drawing.Color]::SaddleBrown)
            }
            "Task" {
                $card = New-DayInfoCard -ItemType "タスク期日" -Title $itemObject.タスク -Details "優先度: $($itemObject.優先度) | 進捗: $($itemObject.進捗度)" -IndicatorColor ([System.Drawing.Color]::SteelBlue)
            }
        }

        if ($card) {
            $card.Tag = [PSCustomObject]@{ Item = $itemObject; Type = $itemType; Date = $date }

            $doubleClickAction = {
                param($s, $e)
                $control = $s
                while ($control -and -not $control.Tag) { $control = $control.Parent }
                if ($control) {
                    $tag = $control.Tag
                    switch ($tag.Type) {
                        "Event"   { Start-EditEvent -eventToEdit $tag.Item -eventDate $tag.Date }
                        "Project" { Show-EditProjectPropertiesForm -projectObject $tag.Item -parentForm $mainForm; Update-AllViews }
                        "Task"    { 
                            if (Start-EditTask -task $tag.Item) { Update-AllViews }
                        }
                    }
                }
            }
            $card.Add_DoubleClick($doubleClickAction)
            foreach ($child in $card.Controls) { $child.Add_DoubleClick($doubleClickAction) }

            $card.Add_MouseDown({ param($s, $e)
                if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) { $script:dayInfoDragStart = $e.Location }
            })
            $card.Add_MouseMove({ param($s, $e)
                if ($e.Button -ne [System.Windows.Forms.MouseButtons]::Left -or $script:dayInfoDragStart.IsEmpty) { return }
                if ([Math]::Abs($e.X - $script:dayInfoDragStart.X) -lt 5 -and [Math]::Abs($e.Y - $script:dayInfoDragStart.Y) -lt 5) { return }
                
                $control = $s
                while ($control -and -not $control.Tag) { $control = $control.Parent }
                if ($control) {
                    $tag = $control.Tag
                    $dragData = [PSCustomObject]@{ ItemType = $tag.Type; ItemData = $tag.Item; OriginalDate = $tag.Date }
                    $s.DoDragDrop($dragData, [System.Windows.Forms.DragDropEffects]::Move)
                }
                $script:dayInfoDragStart = [System.Drawing.Point]::Empty
            })

            $ctxMenu = New-Object System.Windows.Forms.ContextMenuStrip
            if ($script:isDarkMode) { $ctxMenu.Renderer = New-Object DarkModeRenderer }
            
            $editItem = $ctxMenu.Items.Add("編集")
            $editItem.Tag = $card.Tag
            $editItem.Add_Click({ param($s, $ea)
                $tag = $s.Tag
                switch ($tag.Type) {
                    "Event"   { Start-EditEvent -eventToEdit $tag.Item -eventDate $tag.Date }
                    "Project" { Show-EditProjectPropertiesForm -projectObject $tag.Item -parentForm $mainForm; Update-AllViews }
                    "Task"    {
                        if (Start-EditTask -task $tag.Item) { Update-AllViews }
                    }
                }
            })

            $deleteItem = $ctxMenu.Items.Add("削除")
            $deleteItem.Tag = $card.Tag
            $deleteItem.Add_Click({ param($s, $ea)
                $tag = $s.Tag
                switch ($tag.Type) {
                    "Event" {
                        $eventToDelete = $tag.Item; $eventDateString = $tag.Date.ToString("yyyy-MM-dd")
                        if ([System.Windows.Forms.MessageBox]::Show("イベント「$($eventToDelete.Title)」を削除しますか？", "削除の確認", "YesNo", "Warning") -eq "Yes") {
                            $prop = $script:AllEvents.PSObject.Properties | Where-Object { $_.Name -eq $eventDateString } | Select-Object -First 1
                            if ($prop) {
                                $eventsForDay = [System.Collections.ArrayList]@($prop.Value)
                                $itemToRemove = $eventsForDay | Where-Object { $_.ID -eq $eventToDelete.ID } | Select-Object -First 1
                                if ($itemToRemove) { $eventsForDay.Remove($itemToRemove); $prop.Value = $eventsForDay.ToArray(); Save-Events; Update-AllViews }
                            }
                        }
                    }
                    "Project" {
                        $projectToDelete = $tag.Item
                        if ([System.Windows.Forms.MessageBox]::Show("プロジェクト '$($projectToDelete.ProjectName)' を削除しますか？`n関連するすべてのタスクも削除されます。", "削除の確認", "YesNo", "Warning") -eq "Yes") {
                            $script:Projects = $script:Projects | Where-Object { $_.ProjectID -ne $projectToDelete.ProjectID }
                            $script:AllTasks = $script:AllTasks | Where-Object { $_.ProjectID -ne $projectToDelete.ProjectID }
                            Save-DataFile -filePath $script:ProjectsFile -dataObject $script:Projects
                            Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
                            Update-AllViews
                        }
                    }
                    "Task" { Start-DeleteTask -task $tag.Item; Update-AllViews }
                }
            })
            
            if ($itemType -eq "Event") {
                $copyToActualItem = $ctxMenu.Items.Add("実績へコピー")
                $copyToActualItem.Tag = $card.Tag
                $copyToActualItem.Add_Click({ Show-EventToTimeLogForm -eventObject $this.Tag.Item -date $this.Tag.Date; Update-AllViews })
            }

            $card.ContextMenuStrip = $ctxMenu
            $panel.Controls.Add($card) | Out-Null
        }
    }

    if ($eventsList.Count -gt 0) {
        $sortedEvents = $eventsList | Sort-Object SortTime
        foreach($item in $sortedEvents) {
            & $addCardToPanel $script:dayInfoEventsPanel $item
        }
    } else {
        $lblNone = New-Object System.Windows.Forms.Label; $lblNone.Text = "（なし）"; $lblNone.Font = $script:dayInfoItalicFont; $lblNone.AutoSize = $true; $lblNone.Margin = "15, 10, 0, 5"
        $script:dayInfoEventsPanel.Controls.Add($lblNone) | Out-Null
    }

    if ($tasksList.Count -gt 0) {
        $sortedTasks = $tasksList | Sort-Object SortTime
        foreach($item in $sortedTasks) {
            & $addCardToPanel $script:dayInfoTasksPanel $item
        }
    } else {
        $lblNone = New-Object System.Windows.Forms.Label; $lblNone.Text = "（なし）"; $lblNone.Font = $script:dayInfoItalicFont; $lblNone.AutoSize = $true; $lblNone.Margin = "15, 10, 0, 5"
        if ($script:dayInfoTasksPanel) { $script:dayInfoTasksPanel.Controls.Add($lblNone) | Out-Null }
    }

    $script:dayInfoEventsPanel.ResumeLayout($true)
    if ($script:dayInfoTasksPanel) { $script:dayInfoTasksPanel.ResumeLayout($true) }
}

function Update-TimelineView {
    param([datetime]$date)
    $script:timelinePanel.Tag = $date.Date
    $script:timelinePanel.Invalidate()
}

function Show-TimeLogEntryForm {
    param(
        # For editing
        [psobject]$log = $null,
        # For creating
        [datetime]$InitialStartTime,
        [datetime]$InitialEndTime,
        # Context
        [array]$projects,
        [array]$tasks
    )

    $form = New-Object System.Windows.Forms.Form
    $form.Width = 400
    $form.Height = 300
    $form.StartPosition = 'CenterParent'
    $form.FormBorderStyle = 'FixedDialog'

    if ($log) {
        $form.Text = "時間記録の編集"
    } else {
        $form.Text = "時間記録の追加"
    }

    # --- Time Pickers ---
    $labelStart = New-Object System.Windows.Forms.Label; $labelStart.Text = "開始時刻:"; $labelStart.Location = "15, 15"; $labelStart.AutoSize = $true
    $timePickerStart = New-Object System.Windows.Forms.DateTimePicker; $timePickerStart.Location = "15, 35"; $timePickerStart.Width = 150; $timePickerStart.Format = 'Custom'; $timePickerStart.CustomFormat = "yyyy/MM/dd HH:mm"
    
    $labelEnd = New-Object System.Windows.Forms.Label; $labelEnd.Text = "終了時刻:"; $labelEnd.Location = "200, 15"; $labelEnd.AutoSize = $true
    $timePickerEnd = New-Object System.Windows.Forms.DateTimePicker; $timePickerEnd.Location = "200, 35"; $timePickerEnd.Width = 150; $timePickerEnd.Format = 'Custom'; $timePickerEnd.CustomFormat = "yyyy/MM/dd HH:mm"

    # --- Task/Memo Selection ---
    $radioTask = New-Object System.Windows.Forms.RadioButton; $radioTask.Text = "タスクに紐付ける"; $radioTask.Location = "15, 70"; $radioTask.AutoSize = $true; $radioTask.Checked = $true
    $radioMemo = New-Object System.Windows.Forms.RadioButton; $radioMemo.Text = "メモとして記録"; $radioMemo.Location = "15, 155"; $radioMemo.AutoSize = $true
    
    # --- Task Selection Controls (inside a panel) ---
    $panelTask = New-Object System.Windows.Forms.Panel; $panelTask.Location = "30, 95"; $panelTask.Size = "340, 55"
    $labelProject = New-Object System.Windows.Forms.Label; $labelProject.Text = "プロジェクト:"; $labelProject.Location = "0, 0"; $labelProject.AutoSize = $true
    $comboProject = New-Object System.Windows.Forms.ComboBox; $comboProject.Location = "0, 20"; $comboProject.Size = "150, 25"; $comboProject.DropDownStyle = "DropDownList"
    $labelTask = New-Object System.Windows.Forms.Label; $labelTask.Text = "タスク:"; $labelTask.Location = "170, 0"; $labelTask.AutoSize = $true
    $comboTask = New-Object System.Windows.Forms.ComboBox; $comboTask.Location = "170, 20"; $comboTask.Size = "170, 25"; $comboTask.DropDownStyle = "DropDownList"
    $panelTask.Controls.AddRange(@($labelProject, $comboProject, $labelTask, $comboTask))

    # --- Memo TextBox ---
    $textMemo = New-Object System.Windows.Forms.TextBox; $textMemo.Location = "30, 180"; $textMemo.Size = "340, 25"; $textMemo.Enabled = $false

    # --- Buttons ---
    $btnOK = New-Object System.Windows.Forms.Button; $btnOK.Text = "OK"; $btnOK.Location = "100, 220"; $btnOK.Size = "80, 25"
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "キャンセル"; $btnCancel.Location = "200, 220"; $btnCancel.Size = "80, 25"
    $form.AcceptButton = $btnOK; $form.CancelButton = $btnCancel

    $form.Controls.AddRange(@($labelStart, $timePickerStart, $labelEnd, $timePickerEnd, $radioTask, $radioMemo, $panelTask, $textMemo, $btnOK, $btnCancel))

    # --- Event Handlers & Logic ---
    $updateRadioButtons = {
        $panelTask.Enabled = $radioTask.Checked
        $textMemo.Enabled = -not $radioTask.Checked
    }
    $radioTask.Add_CheckedChanged($updateRadioButtons)
    $radioMemo.Add_CheckedChanged($updateRadioButtons)

    # Populate Project ComboBox
    $comboProject.DisplayMember = 'ProjectName'
    $comboProject.ValueMember = 'ProjectID'
    $projects | Sort-Object ProjectName | ForEach-Object { $comboProject.Items.Add($_) } | Out-Null

    # Update Task ComboBox when Project changes
    $comboProject.Add_SelectedIndexChanged({
        $comboTask.Items.Clear()
        $selectedProject = $comboProject.SelectedItem
        if ($selectedProject) {
            $tasksInProject = $tasks | Where-Object { $_.ProjectID -eq $selectedProject.ProjectID } | Sort-Object タスク
            $comboTask.DisplayMember = 'タスク'
            $tasksInProject | ForEach-Object { $comboTask.Items.Add($_) } | Out-Null
        }
    })

    # --- Load Data ---
    if ($log) {
        # Editing existing log
        $timePickerStart.Value = [datetime]$log.StartTime
        $timePickerEnd.Value = [datetime]$log.EndTime
        if ($log.TaskID) {
            $radioTask.Checked = $true
            $task = $tasks | Where-Object { $_.ID -eq $log.TaskID } | Select-Object -First 1
            if ($task) {
                $project = $projects | Where-Object { $_.ProjectID -eq $task.ProjectID } | Select-Object -First 1
                if ($project) {
                    $comboProject.SelectedItem = $comboProject.Items | Where-Object { $_.ProjectID -eq $project.ProjectID } | Select-Object -First 1
                    # Trigger event to populate tasks
                    $comboTask.SelectedItem = $comboTask.Items | Where-Object { $_.ID -eq $task.ID } | Select-Object -First 1
                }
            }
        } else {
            $radioMemo.Checked = $true
            $textMemo.Text = $log.Memo
        }
    } else {
        # Creating new log
        $timePickerStart.Value = $InitialStartTime
        $timePickerEnd.Value = $InitialEndTime
        if ($comboProject.Items.Count -gt 0) {
            $comboProject.SelectedIndex = 0
        }
    }

    # --- Button Clicks ---
    $btnOK.Add_Click({
        if ($timePickerEnd.Value -le $timePickerStart.Value) {
            [System.Windows.Forms.MessageBox]::Show("終了時刻は開始時刻より後に設定してください。", "入力エラー", "OK", "Warning")
            return
        }

        if ($script:Settings.EventOverlapWarning) {
            $newStart = $timePickerStart.Value
            $newEnd = $timePickerEnd.Value
            $overlappingEvent = $null
            foreach ($dateKey in $script:AllEvents.PSObject.Properties.Name) {
                if (([datetime]$dateKey).Date -ge $newStart.Date.AddDays(-1) -and ([datetime]$dateKey).Date -le $newEnd.Date.AddDays(1)) {
                    foreach ($evt in @($script:AllEvents.$dateKey)) {
                    # このコンテキストでは変数 $existingEvent は定義されていません。
                    # このチェックはコピー＆ペーストによるエラーの可能性が高いため、潜在的な問題を避けるために削除します。
                        if ($evt.IsAllDay) { continue }
                        $evtStart = [datetime]$evt.StartTime
                        $evtEnd = [datetime]$evt.EndTime
                        if ($newStart -lt $evtEnd -and $newEnd -gt $evtStart) {
                            $overlappingEvent = $evt; break
                        }
                    }
                }
                if ($overlappingEvent) { break }
            }
            if ($overlappingEvent) {
                $msg = "イベント時間が重複しています（対象: $($overlappingEvent.Title)）。このまま登録しますか？"
                if ([System.Windows.Forms.MessageBox]::Show($msg, "重複の警告", "YesNo", "Warning") -eq 'No') { return }
            }
        }
        $result = [PSCustomObject]@{
            StartTime = $timePickerStart.Value
            EndTime = $timePickerEnd.Value
            Task = $null
            Memo = $null
        }

        if ($radioTask.Checked) {
            if ($comboTask.SelectedItem) {
                $result.Task = $comboTask.SelectedItem
            } else {
                [System.Windows.Forms.MessageBox]::Show("タスクを選択してください。", "入力エラー", "OK", "Warning")
                return
            }
        } else {
            if ([string]::IsNullOrWhiteSpace($textMemo.Text)) {
                [System.Windows.Forms.MessageBox]::Show("メモを入力してください。", "入力エラー", "OK", "Warning")
                return
            }
            $result.Memo = $textMemo.Text.Trim()
        }
        
        $form.Tag = $result
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    })
    $btnCancel.Add_Click({ $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Close() })

    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    if ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        return $form.Tag
    }
    return $null
}

function Test-TimeLogOverlap {
    param(
        [datetime]$StartTime,
        [datetime]$EndTime,
        [psobject]$LogToExclude = $null
    )
    $logsToCheck = $script:AllTimeLogs
    if ($LogToExclude) {
        if ($LogToExclude.PSObject.Properties['ID'] -and $LogToExclude.ID) {
            $excludeId = $LogToExclude.ID
            $logsToCheck = $script:AllTimeLogs | Where-Object { $_.ID -ne $excludeId }
        } else {
            $logsToCheck = $script:AllTimeLogs | Where-Object { -not [object]::ReferenceEquals($_, $LogToExclude) }
        }
    }

    $overlappingLogs = @()

    foreach ($log in $logsToCheck) {
        if (-not $log.StartTime -or -not $log.EndTime) {
            continue # Skip logs without a start or end time.
        }
        $existingStart = [datetime]$log.StartTime
        $existingEnd = [datetime]$log.EndTime
        
        if ($StartTime -lt $existingEnd -and $EndTime -gt $existingStart) {
            $overlappingLogs += $log
        }
    }
    return @($overlappingLogs)
}

function Resolve-TimeLogOverlap {
    param(
        $NewStartTime,
        $NewEndTime,
        [psobject]$LogToExclude = $null
    )

    if (-not $NewStartTime -or -not $NewEndTime) {
        Write-Warning "Resolve-TimeLogOverlap was called with a null time value."
        # 予期せぬnullによるユーザー操作のブロックを防ぐため、重複なしとして扱う
        return $true
    }

    # nullでないことを確認したので、安全にdatetimeにキャストする
    $NewStartTime = [datetime]$NewStartTime
    $NewEndTime = [datetime]$NewEndTime

    $overlappingLogs = Test-TimeLogOverlap -StartTime $NewStartTime -EndTime $NewEndTime -LogToExclude $LogToExclude

    if ($overlappingLogs.Count -eq 0) {
        return $true
    }

    if ($script:Settings.TimeLogOverlapBehavior -eq "Error") {
        [System.Windows.Forms.MessageBox]::Show("指定された時間帯は既存の記録と重複しています。", "重複エラー", "OK", "Error")
        return $false
    }
    elseif ($script:Settings.TimeLogOverlapBehavior -eq "Overwrite") {
        $logsToRemove = @()
        $logsToAdd = @()

        foreach ($log in $overlappingLogs) {
            $oldStart = [datetime]$log.StartTime
            $oldEnd = [datetime]$log.EndTime

            # 1. 内包 (Enclosed): New log covers existing log completely
            if ($NewStartTime -le $oldStart -and $NewEndTime -ge $oldEnd) {
                $logsToRemove += $log
            }
            # 2. 分断 (Split): New log is strictly inside existing log
            elseif ($NewStartTime -gt $oldStart -and $NewEndTime -lt $oldEnd) {
                # Create the second part (tail)
                $secondPart = $log.PSObject.Copy()
                $secondPart.StartTime = $NewEndTime.ToString("yyyy-MM-ddTHH:mm:ss")
                $secondPart.EndTime = $oldEnd.ToString("yyyy-MM-ddTHH:mm:ss")
                $logsToAdd += $secondPart
                
                # Update existing log (head)
                $log.EndTime = $NewStartTime.ToString("yyyy-MM-ddTHH:mm:ss")
            }
            # 3. 後方重複 (Tail Overlap): New log overlaps the end of existing log
            elseif ($NewStartTime -gt $oldStart -and $NewStartTime -lt $oldEnd) {
                $log.EndTime = $NewStartTime.ToString("yyyy-MM-ddTHH:mm:ss")
            }
            # 4. 前方重複 (Head Overlap): New log overlaps the start of existing log
            elseif ($NewEndTime -gt $oldStart -and $NewEndTime -lt $oldEnd) {
                $log.StartTime = $NewEndTime.ToString("yyyy-MM-ddTHH:mm:ss")
            }
        }

        # Apply removals
        if ($logsToRemove.Count -gt 0) {
            $script:AllTimeLogs = @($script:AllTimeLogs | Where-Object { $_ -notin $logsToRemove })
        }
        
        # Apply additions
        if ($logsToAdd.Count -gt 0) { $script:AllTimeLogs += $logsToAdd }

        Save-TimeLogs
        return $true
    }
    return $true
}

function Get-DailyWorkSummary {
    param(
        [Parameter(Mandatory=$true)]
        [datetime]$Date,
        [Parameter(Mandatory=$true)]
        [array]$DailyLogs,
        [Parameter(Mandatory=$true)]
        [array]$AllTasks,
        [Parameter(Mandatory=$true)]
        [array]$AllProjects
    )

    # 1. Calculate total duration
    $dailyTotalDuration = [System.TimeSpan]::FromSeconds(($DailyLogs.Duration | ForEach-Object { $_.TotalSeconds } | Measure-Object -Sum).Sum)

    # 2. Calculate breakdown by category
    $timeByCategory = $DailyLogs | Group-Object { if ($_.TaskObject) { $_.TaskObject.カテゴリ } else { '(タスクなし)' } } | ForEach-Object {
        $duration = [System.TimeSpan]::FromSeconds(($_.Group.Duration | ForEach-Object { $_.TotalSeconds } | Measure-Object -Sum).Sum)
        [PSCustomObject]@{ Name = if ([string]::IsNullOrEmpty($_.Name)) { '(未設定)' } else { $_.Name }; Duration = $duration }
    } | Sort-Object Duration -Descending

    # 3. Calculate breakdown by project
    $timeByProject = $DailyLogs | Group-Object { if ($_.ProjectObject) { $_.ProjectObject.ProjectName } else { '(プロジェクトなし)' } } | ForEach-Object {
        $duration = [System.TimeSpan]::FromSeconds(($_.Group.Duration | ForEach-Object { $_.TotalSeconds } | Measure-Object -Sum).Sum)
        [PSCustomObject]@{ Name = if ([string]::IsNullOrEmpty($_.Name)) { '(未分類)' } else { $_.Name }; Duration = $duration }
    } | Sort-Object Duration -Descending

    # 4. Extract comments
    $comments = $DailyLogs | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Comment) } | ForEach-Object { $_.Comment }

    # 5. Build HTML
    $summaryHtml = "<div class='day-summary'>"
    $summaryHtml += "<div><h3>総作業時間: $([int]$dailyTotalDuration.TotalHours)時間$($dailyTotalDuration.Minutes)分</h3></div>"
    
    $categoryBreakdown = "<div><h3>カテゴリ別内訳</h3><ul>"
    $timeByCategory | ForEach-Object { $categoryBreakdown += "<li>$([System.Security.SecurityElement]::Escape($_.Name)): $([int]$_.Duration.TotalHours)h$($_.Duration.Minutes)m</li>" }
    $summaryHtml += $categoryBreakdown + "</ul></div>"

    $projectBreakdown = "<div><h3>プロジェクト別内訳</h3><ul>"
    $timeByProject | ForEach-Object { $projectBreakdown += "<li>$([System.Security.SecurityElement]::Escape($_.Name)): $([int]$_.Duration.TotalHours)h$($_.Duration.Minutes)m</li>" }
    $summaryHtml += $projectBreakdown + "</ul></div>"
    
    # Add comments section to the summary
    if ($comments.Count -gt 0) {
        $commentsHtml = "<div><h3>コメント</h3><ul>"
        $comments | ForEach-Object { $commentsHtml += "<li>$([System.Security.SecurityElement]::Escape($_))</li>" }
        $summaryHtml += $commentsHtml + "</ul></div>"
    }

    $summaryHtml += "</div>"

    return $summaryHtml
}

function Export-TasksToHtml {
    param(
        [datetime]$startDate,
        [datetime]$endDate,
        [System.Windows.Forms.WebBrowser]$wb = $null # Optional WebBrowser control
    )
    try {
        $reportDate = Get-Date -Format "yyyyMMdd"
        $outputFileName = "Report_$($reportDate).html"
        $outputFilePath = Join-Path -Path $script:AppRoot -ChildPath $outputFileName

        # --- データ収集と集計 ---
        # 処理を高速化するため、タスクとプロジェクトをハッシュテーブルに変換
        $taskLookup = @{}
        $script:AllTasks | ForEach-Object { $taskLookup[$_.ID] = $_ }
        $projectLookup = @{}
        $script:Projects | ForEach-Object { $projectLookup[$_.ProjectID] = $_ }

        $endDateForFilter = $endDate.Date.AddDays(1).AddSeconds(-1)
        $logsInPeriod = $script:AllTimeLogs | Where-Object {
            $_.StartTime -and $_.EndTime -and ([datetime]$_.StartTime) -le $endDateForFilter -and ([datetime]$_.EndTime) -ge $startDate.Date
        }

        $dataForCharts = $logsInPeriod | ForEach-Object {
            $task = $taskLookup[$_.TaskID]
            if ($task) {
                $project = $projectLookup[$task.ProjectID]
                [PSCustomObject]@{
                    ProjectName  = if ($project) { $project.ProjectName } else { '(不明なプロジェクト)' }
                    Category     = if ([string]::IsNullOrWhiteSpace($task.カテゴリ)) { '(未分類)' } else { $task.カテゴリ }
                    Status       = if ([string]::IsNullOrWhiteSpace($task.進捗度)) { '(未定義)' } else { $task.進捗度 }
                    Date         = ([datetime]$_.StartTime).Date
                    DurationHours = ([datetime]$_.EndTime - [datetime]$_.StartTime).TotalHours
                }
            }
        } | Where-Object { $_ }

        # --- JSONデータ生成 (堅牢化) ---
        # データがない場合は $null になる
        $projectSummary = if ($dataForCharts) { $dataForCharts | Group-Object ProjectName | Select-Object @{N='label';E={$_.Name}}, @{N='value';E={($_.Group.DurationHours | Measure-Object -Sum).Sum}} } else { $null }
        $categorySummary = if ($dataForCharts) { $dataForCharts | Group-Object Category | Select-Object @{N='label';E={$_.Name}}, @{N='value';E={($_.Group.DurationHours | Measure-Object -Sum).Sum}} } else { $null }
        $statusSummary = if ($dataForCharts) { $dataForCharts | Group-Object Status | Select-Object @{N='label';E={$_.Name}}, @{N='value';E={($_.Group.DurationHours | Measure-Object -Sum).Sum}} } else { $null }
        $trendData = if ($dataForCharts) { $dataForCharts | Group-Object Date, Category | Select-Object @{N='date';E={$_.Values[0].ToString('yyyy-MM-dd')}}, @{N='category';E={$_.Values[1]}}, @{N='value';E={($_.Group.DurationHours | Measure-Object -Sum).Sum}} } else { $null }

        # $null または空の場合に "[]" を代入する
        $jsonProjectData = if ($projectSummary) { $projectSummary | ConvertTo-Json -Compress } else { "[]" }
        $jsonCategoryData = if ($categorySummary) { $categorySummary | ConvertTo-Json -Compress } else { "[]" }
        $jsonStatusData = if ($statusSummary) { $statusSummary | ConvertTo-Json -Compress } else { "[]" }
        $jsonTrendData = if ($trendData) { $trendData | ConvertTo-Json -Compress } else { "[]" }

        $totalHours = if($dataForCharts){($dataForCharts.DurationHours | Measure-Object -Sum).Sum} else {0}
        $totalMinutes = $totalHours * 60

        # --- HTML生成 ---
        $htmlContent = @"
<!DOCTYPE html>
<html lang="ja">
<head>
    <meta http-equiv="X-UA-Compatible" content="IE=edge" />
    <meta charset="UTF-8">
    <title>タスク実績レポート ($($startDate.ToString("yyyy/MM/dd")) - $($endDate.ToString("yyyy/MM/dd")))</title>
    <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    <script src="https://cdn.jsdelivr.net/npm/chartjs-adapter-date-fns/dist/chartjs-adapter-date-fns.bundle.min.js"></script>
    <style>
        body { font-family: 'Meiryo UI', 'Segoe UI', 'Helvetica Neue', sans-serif; background-color: #f4f7f6; color: #333; margin: 0; padding: 20px; }
        .container { max-width: 1200px; margin: auto; background: white; padding: 20px; box-shadow: 0 0 15px rgba(0,0,0,0.1); border-radius: 8px; }
        h1, h2 { color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px; }
        .summary { display: flex; justify-content: space-around; background-color: #ecf0f1; padding: 20px; border-radius: 5px; margin-bottom: 20px; }
        .summary-item { text-align: center; }
        .summary-item h3 { margin: 0 0 5px 0; color: #7f8c8d; font-weight: normal; }
        .summary-item .value { font-size: 2em; font-weight: bold; color: #3498db; }
        .tabs { display: flex; border-bottom: 1px solid #ddd; margin-bottom: 20px; }
        .tab-link { padding: 10px 15px; cursor: pointer; border: 1px solid transparent; border-bottom: 0; margin-bottom: -1px; transition: background-color 0.2s; }
        .tab-link:hover { background-color: #f0f0f0; }
        .tab-link.active { border-color: #ddd; border-bottom: 1px solid white; background-color: white; border-radius: 5px 5px 0 0; }
        .tab-content { display: none; }
        .tab-content.active { display: block; animation: fadeIn 0.5s; }
        @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
        .chart-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(400px, 1fr)); gap: 20px; }
        .chart-container { position: relative; height: 40vh; min-height: 350px; width: 100%; }
    </style>
</head>
<body>
<div class="container">
    <h1>タスク実績レポート</h1>
    <h2>対象期間: $($startDate.ToString("yyyy/MM/dd")) - $($endDate.ToString("yyyy/MM/dd"))</h2>

    <div class="summary">
        <div class="summary-item">
            <h3>総作業時間</h3>
            <div class="value">$([math]::Floor($totalMinutes / 60))<small>時間</small> $([math]::Floor($totalMinutes % 60))<small>分</small></div>
        </div>
    </div>

    <div class="tabs">
        <div class="tab-link active" onclick="openTab(event, 'daily')">日別推移</div>
        <div class="tab-link" onclick="openTab(event, 'projects')">プロジェクト別</div>
        <div class="tab-link" onclick="openTab(event, 'categories')">カテゴリ別</div>
        <div class="tab-link" onclick="openTab(event, 'status')">ステータス別</div>
    </div>

    <div id="daily" class="tab-content active">
        <h2>日別作業時間の推移 (カテゴリ別)</h2>
        <div class="chart-container"><canvas id="dailyTrendChart"></canvas></div>
    </div>
    <div id="projects" class="tab-content">
        <h2>プロジェクト別作業時間</h2>
        <div class="chart-grid">
            <div class="chart-container"><canvas id="projectPieChart"></canvas></div>
            <div class="chart-container"><canvas id="projectBarChart"></canvas></div>
        </div>
    </div>
    <div id="categories" class="tab-content">
        <h2>カテゴリ別作業時間</h2>
        <div class="chart-grid">
            <div class="chart-container"><canvas id="categoryPieChart"></canvas></div>
            <div class="chart-container"><canvas id="categoryBarChart"></canvas></div>
        </div>
    </div>
    <div id="status" class="tab-content">
        <h2>ステータス別作業時間</h2>
        <div class="chart-grid">
             <div class="chart-container"><canvas id="statusPieChart"></canvas></div>
             <div class="chart-container"><canvas id="statusBarChart"></canvas></div>
        </div>
    </div>
</div>

<script>
    const projectData = $($jsonProjectData);
    const categoryData = $($jsonCategoryData);
    const statusData = $($jsonStatusData);
    const trendData = $($jsonTrendData);

    // --- Chart Color Palette ---
    const chartColors = ['#3498db', '#2ecc71', '#e74c3c', '#9b59b6', '#f1c40f', '#1abc9c', '#34495e', '#e67e22', '#7f8c8d', '#d35400', '#2980b9', '#27ae60'];
    const getColors = (count) => {
        let colors = [];
        for (let i = 0; i < count; i++) {
            colors.push(chartColors[i % chartColors.length]);
        }
        return colors;
    };

    // --- Helper for creating Pie/Bar charts ---
    const createSummaryChart = (ctxId, type, data, title) => {
        const ctx = document.getElementById(ctxId)?.getContext('2d');
        if (!ctx || !data || data.length === 0) return null;
        
        const labels = data.map(d => d.label);
        const values = data.map(d => d.value);

        return new Chart(ctx, {
            type: type,
            data: {
                labels: labels,
                datasets: [{
                    label: title,
                    data: values,
                    backgroundColor: getColors(labels.length),
                    borderColor: '#fff',
                    borderWidth: type === 'pie' ? 2 : 0
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        display: type === 'pie',
                        position: 'top',
                    },
                    title: {
                        display: true,
                        text: title
                    }
                }
            }
        });
    };
    
    // --- Helper for Daily Trend Chart ---
    const createDailyTrendChart = () => {
        const ctx = document.getElementById('dailyTrendChart')?.getContext('2d');
        if (!ctx || !trendData || trendData.length === 0) return null;

        // 1. Get all unique dates and categories
        const dateStrings = [...new Set(trendData.map(d => d.date))].sort();
        const categories = [...new Set(trendData.map(d => d.category))].sort();
        
        // 2. Map data for easy lookup: { 'category': { 'yyyy-mm-dd': value } }
        const dataMap = {};
        categories.forEach(cat => { dataMap[cat] = {}; });
        trendData.forEach(d => {
            if (dataMap[d.category]) {
                dataMap[d.category][d.date] = d.value;
            }
        });

        // 3. Create datasets for Chart.js
        const datasets = categories.map((cat, index) => {
            return {
                label: cat,
                data: dateStrings.map(date => dataMap[cat][date] || 0), // Fill missing dates with 0
                backgroundColor: chartColors[index % chartColors.length],
                borderColor: chartColors[index % chartColors.length],
                fill: false,
                borderWidth: 2
            };
        });

        return new Chart(ctx, {
            type: 'bar', // Start with bar, can be toggled
            data: {
                labels: dateStrings,
                datasets: datasets
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    x: {
                        type: 'time',
                        time: { unit: 'day' },
                        stacked: true,
                    },
                    y: {
                        stacked: true,
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: '作業時間 (h)'
                        }
                    }
                },
                plugins: {
                    legend: { position: 'top' },
                    title: { display: true, text: '日別・カテゴリ別 作業時間' }
                }
            }
        });
    };


    // --- Initialize all charts on load ---
    window.addEventListener('load', () => {
        createSummaryChart('projectPieChart', 'pie', projectData, 'プロジェクト別 (円グラフ)');
        createSummaryChart('projectBarChart', 'bar', projectData, 'プロジェクト別 (棒グラフ)');
        createSummaryChart('categoryPieChart', 'pie', categoryData, 'カテゴリ別 (円グラフ)');
        createSummaryChart('categoryBarChart', 'bar', categoryData, 'カテゴリ別 (棒グラフ)');
        createSummaryChart('statusPieChart', 'pie', statusData, 'ステータス別 (円グラフ)');
        createSummaryChart('statusBarChart', 'bar', statusData, 'ステータス別 (棒グラフ)');
        
        createDailyTrendChart();
    });

    // --- Tab Switching Logic ---
    function openTab(evt, tabName) {
        let i, tabcontent, tablinks;
        tabcontent = document.getElementsByClassName("tab-content");
        for (i = 0; i < tabcontent.length; i++) {
            tabcontent[i].style.display = "none";
        }
        tablinks = document.getElementsByClassName("tab-link");
        for (i = 0; i < tablinks.length; i++) {
            tablinks[i].className = tablinks[i].className.replace(" active", "");
        }
        document.getElementById(tabName).style.display = "block";
        evt.currentTarget.className += " active";
    }
</script>
</body>
</html>
"@
        Set-Content -Path $outputFilePath -Value $htmlContent -Encoding UTF8 -Force

        if ($wb) {
            # WebBrowserコントロールに直接HTMLをロード
            $wb.DocumentText = $htmlContent
        } else {
            # デフォルトのブラウザでファイルを開く
            Start-Process -FilePath $outputFilePath
        }

    } catch {
        [System.Windows.Forms.MessageBox]::Show("レポートのエクスポート中にエラーが発生しました: `n$($_.Exception.Message)`n`n$($_.ScriptStackTrace)", "エクスポートエラー", "OK", "Error")
    }
}

function Show-TimeLogAdjustmentForm {
    param(
        [datetime]$initialStartTime,
        [datetime]$initialEndTime,
        [string]$title = "",
        [string]$initialMemo = ""
    )

    # Form setup
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "実績時間の微調整"
    $form.Size = New-Object System.Drawing.Size(400, 350)
    $form.StartPosition = 'CenterParent'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false

    # Header Label
    $lblTitle = New-Object System.Windows.Forms.Label
    $lblTitle.Text = $title
    $lblTitle.Font = New-Object System.Drawing.Font("Meiryo UI", 10, [System.Drawing.FontStyle]::Bold)
    $lblTitle.Location = New-Object System.Drawing.Point(15, 15)
    $lblTitle.Size = New-Object System.Drawing.Size(350, 25)
    $form.Controls.Add($lblTitle)

    # --- Start Time Section ---
    $labelStart = New-Object System.Windows.Forms.Label; $labelStart.Text = "開始時間:"; $labelStart.Location = "15, 50"; $labelStart.AutoSize = $true
    $dtpStart = New-Object System.Windows.Forms.DateTimePicker; $dtpStart.Format = 'Custom'; $dtpStart.CustomFormat = "HH:mm"; $dtpStart.ShowUpDown = $true; $dtpStart.Value = $initialStartTime; $dtpStart.Location = "15, 70"; $dtpStart.Width = 80
    $form.Controls.Add($labelStart)
    $form.Controls.Add($dtpStart)

    $btnStartNow = New-Object System.Windows.Forms.Button
    $btnStartNow.Text = "今すぐ開始"
    $btnStartNow.Location = New-Object System.Drawing.Point(100, 18)
    $btnStartNow.Size = New-Object System.Drawing.Size(100, 25)
    $btnStartNow.Add_Click({
        $duration = $dtpEnd.Value - $dtpStart.Value
        $now = Get-Date
        $dtpStart.Value = $now
        $dtpEnd.Value = $now.Add($duration)
    })
    $form.Controls.Add($btnStartNow)

    # --- End Time Section ---
    $labelEnd = New-Object System.Windows.Forms.Label; $labelEnd.Text = "終了時間:"; $labelEnd.Location = "15, 120"; $labelEnd.AutoSize = $true
    $dtpEnd = New-Object System.Windows.Forms.DateTimePicker; $dtpEnd.Format = 'Custom'; $dtpEnd.CustomFormat = "HH:mm"; $dtpEnd.ShowUpDown = $true; $dtpEnd.Value = $initialEndTime; $dtpEnd.Location = "15, 140"; $dtpEnd.Width = 80
    $form.Controls.Add($labelEnd)
    $form.Controls.Add($dtpEnd)

    # --- Memo Section ---
    $lblMemo = New-Object System.Windows.Forms.Label
    $lblMemo.Text = "メモ:"
    $lblMemo.Location = New-Object System.Drawing.Point(15, 190)
    $lblMemo.AutoSize = $true
    $form.Controls.Add($lblMemo)

    $txtMemo = New-Object System.Windows.Forms.TextBox
    $txtMemo.Multiline = $true
    $txtMemo.ScrollBars = 'Vertical'
    $txtMemo.Location = New-Object System.Drawing.Point(15, 210)
    $txtMemo.Size = New-Object System.Drawing.Size(350, 50)
    $txtMemo.Text = $initialMemo
    $form.Controls.Add($txtMemo)

    # --- Buttons ---
    $btnRegister = New-Object System.Windows.Forms.Button
    $btnRegister.Text = "登録"
    $btnRegister.Location = New-Object System.Drawing.Point(100, 275)
    $btnRegister.Size = New-Object System.Drawing.Size(80, 30)
    $btnRegister.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.Controls.Add($btnRegister)

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = "キャンセル"
    $btnCancel.Location = New-Object System.Drawing.Point(200, 275)
    $btnCancel.Size = New-Object System.Drawing.Size(80, 30)
    $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.Add($btnCancel)

    $form.AcceptButton = $btnRegister
    $form.CancelButton = $btnCancel

    $btnRegister.Add_Click({
        if ($dtpEnd.Value -le $dtpStart.Value) {
            [System.Windows.Forms.MessageBox]::Show("終了時間は開始時間より後である必要があります。", "エラー", "OK", "Error")
            $form.DialogResult = [System.Windows.Forms.DialogResult]::None # Prevent form from closing
            return
        }
        $form.Tag = @{
            StartTime = $dtpStart.Value
            EndTime   = $dtpEnd.Value
            Memo      = $txtMemo.Text
        }
        $form.Close()
    })

    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    if ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        return $form.Tag
    }
    return $null
}

function Show-EventToTimeLogForm {
    param($eventObject, $date)
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "予定を実績へコピー"
    $form.Size = "450, 320"
    $form.StartPosition = "CenterParent"
    $form.FormBorderStyle = "FixedDialog"
    
    $lblStart = New-Object System.Windows.Forms.Label; $lblStart.Text = "開始:"; $lblStart.Location = "20, 20"; $lblStart.AutoSize = $true; $form.Controls.Add($lblStart)
    $dtpStart = New-Object System.Windows.Forms.DateTimePicker; $dtpStart.Format = "Custom"; $dtpStart.CustomFormat = "yyyy/MM/dd HH:mm"; $dtpStart.Location = "80, 18"; $dtpStart.Width = 250; $form.Controls.Add($dtpStart)
    
    $lblEnd = New-Object System.Windows.Forms.Label; $lblEnd.Text = "終了:"; $lblEnd.Location = "20, 50"; $lblEnd.AutoSize = $true; $form.Controls.Add($lblEnd)
    $dtpEnd = New-Object System.Windows.Forms.DateTimePicker; $dtpEnd.Format = "Custom"; $dtpEnd.CustomFormat = "yyyy/MM/dd HH:mm"; $dtpEnd.Location = "80, 48"; $dtpEnd.Width = 250; $form.Controls.Add($dtpEnd)
    
    $lblMemo = New-Object System.Windows.Forms.Label; $lblMemo.Text = "内容:"; $lblMemo.Location = "20, 80"; $lblMemo.AutoSize = $true; $form.Controls.Add($lblMemo)
    $txtMemo = New-Object System.Windows.Forms.TextBox; $txtMemo.Location = "80, 78"; $txtMemo.Width = 330; $form.Controls.Add($txtMemo)
    
    $dtpStart.Value = [datetime]$eventObject.StartTime
    $dtpEnd.Value = [datetime]$eventObject.EndTime
    $txtMemo.Text = $eventObject.Title
    
    $groupOverlap = New-Object System.Windows.Forms.GroupBox; $groupOverlap.Text = "重複時の処理"; $groupOverlap.Location = "20, 120"; $groupOverlap.Size = "390, 60"; $form.Controls.Add($groupOverlap)
    $radioOverwrite = New-Object System.Windows.Forms.RadioButton; $radioOverwrite.Text = "上書きする"; $radioOverwrite.Location = "20, 25"; $radioOverwrite.AutoSize = $true; $radioOverwrite.Checked = $true; $groupOverlap.Controls.Add($radioOverwrite)
    $radioFill = New-Object System.Windows.Forms.RadioButton; $radioFill.Text = "空き時間のみ埋める"; $radioFill.Location = "150, 25"; $radioFill.AutoSize = $true; $groupOverlap.Controls.Add($radioFill)
    
    $btnOk = New-Object System.Windows.Forms.Button; $btnOk.Text = "登録"; $btnOk.Location = "100, 230"; $form.Controls.Add($btnOk)
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "キャンセル"; $btnCancel.Location = "200, 230"; $form.Controls.Add($btnCancel)
    
    $btnOk.Add_Click({
        $newLog = [PSCustomObject]@{
            ID = [guid]::NewGuid().ToString()
            TaskID = $null
            Memo = $txtMemo.Text
            StartTime = $dtpStart.Value.ToString("o")
            EndTime = $dtpEnd.Value.ToString("o")
        }
        
        if ($radioOverwrite.Checked) {
            Resolve-TimeLogOverlap -NewStartTime $dtpStart.Value -NewEndTime $dtpEnd.Value
            $script:AllTimeLogs += $newLog
            $script:AllTimeLogs = @($script:AllTimeLogs) + $newLog
        } else {
            Add-TimeLogWithGaps -newLog $newLog
        }
        Save-TimeLogs
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    })
    $btnCancel.Add_Click({ $form.Close() })
    
    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    return $form.ShowDialog()
}

function Add-TimeLogWithGaps {
    param($newLog)
    $start = [datetime]$newLog.StartTime
    $end = [datetime]$newLog.EndTime
    
    $existing = $script:AllTimeLogs | Where-Object { 
        $_.StartTime -and $_.EndTime -and ([datetime]$_.StartTime).Date -eq $start.Date 
    } | Sort-Object { [datetime]$_.StartTime }
    
    $current = $start
    foreach ($log in $existing) {
        $lStart = [datetime]$log.StartTime
        $lEnd = [datetime]$log.EndTime
        
        if ($lStart -gt $current) {
            $gapEnd = if ($lStart -lt $end) { $lStart } else { $end }
            if ($gapEnd -gt $current) {
                $segment = $newLog.PSObject.Copy()
                $segment.StartTime = $current.ToString("o")
                $segment.EndTime = $gapEnd.ToString("o")
                $script:AllTimeLogs += $segment
                $script:AllTimeLogs = @($script:AllTimeLogs) + $segment
            }
        }
        if ($lEnd -gt $current) { $current = $lEnd }
        if ($current -ge $end) { break }
    }
    
    if ($current -lt $end) {
        $segment = $newLog.PSObject.Copy()
        $segment.StartTime = $current.ToString("o")
        $segment.EndTime = $end.ToString("o")
        $script:AllTimeLogs += $segment
        $script:AllTimeLogs = @($script:AllTimeLogs) + $segment
    }
}

function Get-SnappedTime {
    param(
        [datetime]$Time,
        [datetime]$Date,
        [psobject]$ExcludeLog,
        [int]$SnapThresholdMinutes = 10
    )
    $snappedTime = $Time
    $minDiff = $SnapThresholdMinutes

    # 1. 30分単位にスナップ
    $roundedMinute = [Math]::Round($Time.Minute / 30) * 30
    $gridTime = $Time.Date.AddHours($Time.Hour).AddMinutes($roundedMinute)
    $diff = [Math]::Abs(($Time - $gridTime).TotalMinutes)
    if ($diff -lt $minDiff) {
        $minDiff = $diff
        $snappedTime = $gridTime
    }

    # 2. 他のアイテムの端にスナップ
    $snapTargets = [System.Collections.Generic.List[datetime]]::new()
    # 実績
    $script:AllTimeLogs | Where-Object {
        $_.StartTime -and $_.EndTime -and ([datetime]$_.StartTime).Date -eq $Date.Date -and (-not [object]::ReferenceEquals($_, $ExcludeLog))
    } | ForEach-Object {
        $snapTargets.Add([datetime]$_.StartTime)
        $snapTargets.Add([datetime]$_.EndTime)
    }
    # 予定
    $dateStr = $Date.ToString("yyyy-MM-dd")
    if ($script:AllEvents.PSObject.Properties[$dateStr]) {
        @($script:AllEvents.PSObject.Properties[$dateStr].Value) | Where-Object { 
            -not $_.IsAllDay -and (-not [object]::ReferenceEquals($_, $ExcludeLog))
        } | ForEach-Object {
            $snapTargets.Add([datetime]$_.StartTime)
            $snapTargets.Add([datetime]$_.EndTime)
        }
    }

    foreach ($target in ($snapTargets | Select-Object -Unique)) {
        $diff = [Math]::Abs(($Time - $target).TotalMinutes)
        if ($diff -lt $minDiff) {
            $minDiff = $diff
            $snappedTime = $target
        }
    }
    
    return $snappedTime
}

# --- 補助機能 (設定反映・ユーティリティ) ---

function Update-StartupShortcut {
    $shortcutPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\TaskManager.lnk"
    $wshShell = New-Object -ComObject WScript.Shell
    
    if ($script:Settings.RunAtStartup) {
        try {
            $mainScriptPath = Join-Path -Path $script:AppRoot -ChildPath "task_manager_main.ps1"
            $shortcut = $wshShell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = "powershell.exe"
            $shortcut.Arguments = "-WindowStyle Hidden -ExecutionPolicy Bypass -NoProfile -File `"$mainScriptPath`""
            $shortcut.WorkingDirectory = $script:AppRoot
            $shortcut.Description = "Task Manager Application"
            $shortcut.IconLocation = "powershell.exe,0"
            $shortcut.Save()
        } catch {
            Write-Warning "スタートアップ登録に失敗しました: $($_.Exception.Message)"
        }
    } else {
        if (Test-Path $shortcutPath) {
            Remove-Item $shortcutPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Show-LoginDialog {
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "ログイン"
    $form.Size = New-Object System.Drawing.Size(300, 160)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false; $form.MinimizeBox = $false; $form.TopMost = $true

    $lbl = New-Object System.Windows.Forms.Label; $lbl.Text = "パスコードを入力してください:"; $lbl.Location = "10, 15"; $lbl.AutoSize = $true; $form.Controls.Add($lbl)
    $txt = New-Object System.Windows.Forms.TextBox; $txt.Location = "10, 40"; $txt.Size = "260, 25"; $txt.PasswordChar = '*'; $form.Controls.Add($txt)
    
    $btnOk = New-Object System.Windows.Forms.Button; $btnOk.Text = "OK"; $btnOk.Location = "90, 80"; $btnOk.DialogResult = [System.Windows.Forms.DialogResult]::OK; $form.Controls.Add($btnOk)
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "キャンセル"; $btnCancel.Location = "180, 80"; $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Controls.Add($btnCancel)
    $form.AcceptButton = $btnOk; $form.CancelButton = $btnCancel

    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    $form.Add_Shown({ $txt.Focus() })

    if ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        return ($txt.Text -eq $script:Settings.Passcode)
    }
    return $false
}

function Reset-WindowPosition {
    param($form)
    if ($form) {
        $form.StartPosition = "Manual"
        $form.Location = New-Object System.Drawing.Point(100, 100)
    }
}

function Show-IcsExchangeForm {
    param($parentForm, [string]$initialTab = "Export")

    # --- フォーム作成 ---
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "ICS連携 (インポート/エクスポート)"
    $form.Size = New-Object System.Drawing.Size(450, 400)
    $form.StartPosition = 'CenterParent'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false

    # --- タブコントロール ---
    $tabControl = New-Object System.Windows.Forms.TabControl
    $tabControl.Dock = 'Fill'
    $form.Controls.Add($tabControl)

    # =================================================================
    # タブ1: エクスポート
    # =================================================================
    $tabExport = New-Object System.Windows.Forms.TabPage "エクスポート"
    $tabControl.TabPages.Add($tabExport)

    $grpPeriod = New-Object System.Windows.Forms.GroupBox
    $grpPeriod.Text = "期間指定"
    $grpPeriod.Location = New-Object System.Drawing.Point(15, 15)
    $grpPeriod.Size = New-Object System.Drawing.Size(400, 140)
    $tabExport.Controls.Add($grpPeriod)

    $radioToday = New-Object System.Windows.Forms.RadioButton; $radioToday.Text = "今日"; $radioToday.Location = "15, 25"; $radioToday.AutoSize = $true
    $radioMonth = New-Object System.Windows.Forms.RadioButton; $radioMonth.Text = "今月"; $radioMonth.Location = "80, 25"; $radioMonth.AutoSize = $true
    $radioAll = New-Object System.Windows.Forms.RadioButton; $radioAll.Text = "全期間"; $radioAll.Location = "145, 25"; $radioAll.AutoSize = $true; $radioAll.Checked = $true
    $radioCustom = New-Object System.Windows.Forms.RadioButton; $radioCustom.Text = "カスタム"; $radioCustom.Location = "220, 25"; $radioCustom.AutoSize = $true
    
    $grpPeriod.Controls.AddRange(@($radioToday, $radioMonth, $radioAll, $radioCustom))

    $lblStart = New-Object System.Windows.Forms.Label; $lblStart.Text = "開始:"; $lblStart.Location = "30, 60"; $lblStart.AutoSize = $true
    $dtpStart = New-Object System.Windows.Forms.DateTimePicker; $dtpStart.Format = 'Short'; $dtpStart.Location = "70, 57"; $dtpStart.Width = 100; $dtpStart.Enabled = $false
    $lblEnd = New-Object System.Windows.Forms.Label; $lblEnd.Text = "終了:"; $lblEnd.Location = "190, 60"; $lblEnd.AutoSize = $true
    $dtpEnd = New-Object System.Windows.Forms.DateTimePicker; $dtpEnd.Format = 'Short'; $dtpEnd.Location = "230, 57"; $dtpEnd.Width = 100; $dtpEnd.Enabled = $false

    $grpPeriod.Controls.AddRange(@($lblStart, $dtpStart, $lblEnd, $dtpEnd))

    # ラジオボタンイベント
    $toggleCustomDate = {
        $dtpStart.Enabled = $radioCustom.Checked
        $dtpEnd.Enabled = $radioCustom.Checked
    }
    $radioToday.Add_CheckedChanged($toggleCustomDate)
    $radioMonth.Add_CheckedChanged($toggleCustomDate)
    $radioAll.Add_CheckedChanged($toggleCustomDate)
    $radioCustom.Add_CheckedChanged($toggleCustomDate)

    $grpTarget = New-Object System.Windows.Forms.GroupBox
    $grpTarget.Text = "出力対象"
    $grpTarget.Location = New-Object System.Drawing.Point(15, 170)
    $grpTarget.Size = New-Object System.Drawing.Size(400, 60)
    $tabExport.Controls.Add($grpTarget)

    $chkIncludeTasks = New-Object System.Windows.Forms.CheckBox; $chkIncludeTasks.Text = "タスクを含める"; $chkIncludeTasks.Location = "15, 25"; $chkIncludeTasks.AutoSize = $true; $chkIncludeTasks.Checked = $true
    $chkIncludeEvents = New-Object System.Windows.Forms.CheckBox; $chkIncludeEvents.Text = "予定（イベント）を含める"; $chkIncludeEvents.Location = "150, 25"; $chkIncludeEvents.AutoSize = $true; $chkIncludeEvents.Checked = $true
    $grpTarget.Controls.AddRange(@($chkIncludeTasks, $chkIncludeEvents))

    $btnExport = New-Object System.Windows.Forms.Button
    $btnExport.Text = "ICSファイルを保存してエクスポート"
    $btnExport.Location = New-Object System.Drawing.Point(80, 250)
    $btnExport.Size = New-Object System.Drawing.Size(250, 35)
    $tabExport.Controls.Add($btnExport)

    $btnExport.Add_Click({
        Write-Host "エクスポート処理開始"
        # 1. 期間の決定
        $startRange = [DateTime]::MinValue
        $endRange = [DateTime]::MaxValue
        $today = (Get-Date).Date

        if ($radioToday.Checked) {
            $startRange = $today
            $endRange = $today.AddDays(1).AddSeconds(-1)
        } elseif ($radioMonth.Checked) {
            $startRange = Get-Date -Year $today.Year -Month $today.Month -Day 1 -Hour 0 -Minute 0 -Second 0
            $endRange = $startRange.AddMonths(1).AddSeconds(-1)
        } elseif ($radioCustom.Checked) {
            $startRange = $dtpStart.Value.Date
            $endRange = $dtpEnd.Value.Date.AddDays(1).AddSeconds(-1)
        }
        # 全期間の場合は初期値のまま

        # 2. データの抽出
        $tasksToExport = @()
        if ($chkIncludeTasks.Checked) {
            $tasksToExport = $script:AllTasks | Where-Object {
                if (-not [string]::IsNullOrWhiteSpace($_.期日)) {
                    try {
                        $d = [datetime]$_.期日
                        return $d -ge $startRange -and $d -le $endRange
                    } catch { $false }
                } else { $false }
            }
        }

        $eventsToExport = @()
        if ($chkIncludeEvents.Checked) {
            if ($script:AllEvents) {
                foreach ($prop in $script:AllEvents.PSObject.Properties) {
                    $evts = $prop.Value
                    if ($evts -isnot [array]) { $evts = @($evts) }
                    foreach ($evt in $evts) {
                        $evtStart = $null
                        if ($evt.PSObject.Properties['StartTime']) {
                            try { $evtStart = [datetime]$evt.StartTime } catch {}
                        } elseif ($evt.PSObject.Properties['EventDate']) {
                            try { $evtStart = [datetime]$evt.EventDate } catch {}
                        }
                        if (-not $evtStart) { try { $evtStart = [datetime]$prop.Name } catch {} }

                        if ($evtStart) {
                            if ($evtStart -ge $startRange -and $evtStart -le $endRange) {
                                $eventsToExport += $evt
                            }
                        }
                    }
                }
            }
        }

        if ($tasksToExport.Count -eq 0 -and $eventsToExport.Count -eq 0) {
            [System.Windows.Forms.MessageBox]::Show("指定された条件に該当するデータがありません。", "情報", "OK", "Information")
            return
        }

        # 3. 保存先選択
        $sfd = New-Object System.Windows.Forms.SaveFileDialog
        $sfd.Filter = "iCalendar ファイル (*.ics)|*.ics"
        $sfd.FileName = "exported_tasks.ics"
        
        if ($sfd.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }

        # 4. ICS生成
        try {
            $sb = [System.Text.StringBuilder]::new()
            [void]$sb.AppendLine("BEGIN:VCALENDAR")
            [void]$sb.AppendLine("VERSION:2.0")
            [void]$sb.AppendLine("PRODID:-//PowerShellTaskManager//NONSGML v1.0//EN")
            $dtStamp = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ")

            foreach ($task in $tasksToExport) {
                try {
                    $rawDate = $task.期日; $dt = [DateTime]::Parse($rawDate); $isAllDay = $rawDate -match "^\d{4}[-/]\d{1,2}[-/]\d{1,2}$"
                    [void]$sb.AppendLine("BEGIN:VEVENT"); [void]$sb.AppendLine("SUMMARY:$($task.タスク)")
                    $desc = ""; if ($task.カテゴリ) { $desc += "カテゴリ: $($task.カテゴリ)\n" }; if ($task.進捗度) { $desc += "進捗: $($task.進捗度)\n" }; if ($task.優先度) { $desc += "優先度: $($task.優先度)" }
                    [void]$sb.AppendLine("DESCRIPTION:$desc")
                    if ($isAllDay) { [void]$sb.AppendLine("DTSTART;VALUE=DATE:$($dt.ToString('yyyyMMdd'))"); [void]$sb.AppendLine("DTEND;VALUE=DATE:$($dt.AddDays(1).ToString('yyyyMMdd'))") }
                    else { $utcStart = $dt.ToUniversalTime(); [void]$sb.AppendLine("DTSTART:$($utcStart.ToString('yyyyMMddTHHmmssZ'))"); [void]$sb.AppendLine("DTEND:$($utcStart.AddHours(1).ToString('yyyyMMddTHHmmssZ'))") }
                    [void]$sb.AppendLine("UID:$($task.ID)"); [void]$sb.AppendLine("DTSTAMP:$dtStamp"); [void]$sb.AppendLine("END:VEVENT")
                } catch {}
            }

            foreach ($evt in $eventsToExport) {
                try {
                    [void]$sb.AppendLine("BEGIN:VEVENT"); [void]$sb.AppendLine("SUMMARY:$($evt.Title)"); [void]$sb.AppendLine("UID:$($evt.ID)"); [void]$sb.AppendLine("DTSTAMP:$dtStamp")
                    if ($evt.IsAllDay) {
                        $s = [datetime]$evt.StartTime; $e = if ($evt.EndTime) { [datetime]$evt.EndTime } else { $s.AddDays(1) }
                        if ($e -le $s) { $e = $s.AddDays(1) }
                        [void]$sb.AppendLine("DTSTART;VALUE=DATE:$($s.ToString('yyyyMMdd'))"); [void]$sb.AppendLine("DTEND;VALUE=DATE:$($e.ToString('yyyyMMdd'))")
                    } else {
                        $s = ([datetime]$evt.StartTime).ToUniversalTime(); $e = ([datetime]$evt.EndTime).ToUniversalTime()
                        [void]$sb.AppendLine("DTSTART:$($s.ToString('yyyyMMddTHHmmssZ'))"); [void]$sb.AppendLine("DTEND:$($e.ToString('yyyyMMddTHHmmssZ'))")
                    }
                    [void]$sb.AppendLine("END:VEVENT")
                } catch {}
            }

            [void]$sb.AppendLine("END:VCALENDAR")
            $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
            [System.IO.File]::WriteAllText($sfd.FileName, $sb.ToString(), $utf8NoBom)
            $count = $tasksToExport.Count + $eventsToExport.Count
            [System.Windows.Forms.MessageBox]::Show("エクスポートが完了しました。`n出力件数: $count 件", "完了", "OK", "Information")
            $form.Close()
        } catch {
            [System.Windows.Forms.MessageBox]::Show("エクスポートに失敗しました: $($_.Exception.Message)", "エラー", "OK", "Error")
        }
    })

    # =================================================================
    # タブ2: インポート
    # =================================================================
    $tabImport = New-Object System.Windows.Forms.TabPage "インポート"
    $tabControl.TabPages.Add($tabImport)

    $grpFile = New-Object System.Windows.Forms.GroupBox
    $grpFile.Text = "ファイル選択"
    $grpFile.Location = New-Object System.Drawing.Point(15, 15)
    $grpFile.Size = New-Object System.Drawing.Size(400, 80)
    $tabImport.Controls.Add($grpFile)

    $txtFilePath = New-Object System.Windows.Forms.TextBox
    $txtFilePath.Location = New-Object System.Drawing.Point(15, 30)
    $txtFilePath.Size = New-Object System.Drawing.Size(280, 23)
    $grpFile.Controls.Add($txtFilePath)

    $btnBrowse = New-Object System.Windows.Forms.Button
    $btnBrowse.Text = "参照"
    $btnBrowse.Location = New-Object System.Drawing.Point(305, 28)
    $btnBrowse.Size = New-Object System.Drawing.Size(80, 27)
    $grpFile.Controls.Add($btnBrowse)

    $btnBrowse.Add_Click({
        $ofd = New-Object System.Windows.Forms.OpenFileDialog
        $ofd.Filter = "iCalendar ファイル (*.ics)|*.ics|すべてのファイル (*.*)|*.*"
        if ($ofd.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $txtFilePath.Text = $ofd.FileName
        }
    })

    $grpConflict = New-Object System.Windows.Forms.GroupBox
    $grpConflict.Text = "重複時の処理"
    $grpConflict.Location = New-Object System.Drawing.Point(15, 110)
    $grpConflict.Size = New-Object System.Drawing.Size(400, 80)
    $tabImport.Controls.Add($grpConflict)

    $radioSkip = New-Object System.Windows.Forms.RadioButton; $radioSkip.Text = "スキップ（既存優先）"; $radioSkip.Location = "15, 30"; $radioSkip.AutoSize = $true; $radioSkip.Checked = $true
    $radioOverwrite = New-Object System.Windows.Forms.RadioButton; $radioOverwrite.Text = "上書き（インポート優先）"; $radioOverwrite.Location = "180, 30"; $radioOverwrite.AutoSize = $true
    $grpConflict.Controls.AddRange(@($radioSkip, $radioOverwrite))

    $btnImport = New-Object System.Windows.Forms.Button
    $btnImport.Text = "インポート開始"
    $btnImport.Location = New-Object System.Drawing.Point(120, 220)
    $btnImport.Size = New-Object System.Drawing.Size(180, 35)
    $tabImport.Controls.Add($btnImport)

    $btnImport.Add_Click({
        $filePath = $txtFilePath.Text
        if ([string]::IsNullOrWhiteSpace($filePath) -or -not (Test-Path $filePath)) {
            [System.Windows.Forms.MessageBox]::Show("有効なファイルを選択してください。", "エラー", "OK", "Error")
            return
        }

        $mode = if ($radioOverwrite.Checked) { "Overwrite" } else { "Skip" }
        
        try {
            # 1. ファイル読み込みと行の折り返し(Folding)の復元
            $content = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::UTF8)
            $content = $content -replace "`r`n[ \t]", "" 

            # 2. VEVENTブロックの抽出
            $eventMatches = [regex]::Matches($content, "(?s)BEGIN:VEVENT(.*?)END:VEVENT")
            
            $countImported = 0; $countSkipped = 0; $countUpdated = 0
            
            # 新規タスク用のデフォルトプロジェクトID取得
            $defaultProject = $script:Projects | Where-Object { $_.ProjectName -eq "未分類" } | Select-Object -First 1
            $defaultProjectId = if ($defaultProject) { $defaultProject.ProjectID } else { 
                $newP = [PSCustomObject]@{ ProjectID = [guid]::NewGuid().ToString(); ProjectName = "未分類"; ProjectDueDate = $null; WorkFiles = @(); Notification = "全体設定に従う"; ProjectColor = "#D3D3D3"; AutoArchiveTasks = $true }
                $script:Projects += $newP; $newP.ProjectID
            }

            foreach ($match in $eventMatches) {
                $block = $match.Groups[1].Value
                $lines = $block -split "`r`n"
                $props = @{}
                foreach ($line in $lines) {
                    if ($line -match "^([^;:]+)(?:;[^:]*)?:(.*)$") {
                        $key = $matches[1].ToUpper(); $val = $matches[2]
                        if (-not $props.ContainsKey($key)) { $props[$key] = $val }
                    }
                }

                $uid = if ($props.ContainsKey("UID")) { $props["UID"] } else { [guid]::NewGuid().ToString() }
                $summary = if ($props.ContainsKey("SUMMARY")) { $props["SUMMARY"] } else { "(No Title)" }
                $description = if ($props.ContainsKey("DESCRIPTION")) { $props["DESCRIPTION"] -replace "\\n", "`r`n" } else { "" }
                
                # 日時の解析
                $dtStartStr = if ($props.ContainsKey("DTSTART")) { $props["DTSTART"] } else { $null }
                $dtEndStr = if ($props.ContainsKey("DTEND")) { $props["DTEND"] } else { $null }
                $startTime = $null; $endTime = $null; $isAllDay = $false

                if ($dtStartStr) {
                    if ($dtStartStr -match "^\d{8}$") { $isAllDay = $true; $startTime = [DateTime]::ParseExact($dtStartStr, "yyyyMMdd", $null) }
                    elseif ($dtStartStr -match "^\d{8}T\d{6}Z$") { $startTime = [DateTime]::ParseExact($dtStartStr, "yyyyMMddTHHmmssZ", $null, [System.Globalization.DateTimeStyles]::AssumeUniversal).ToLocalTime() }
                    elseif ($dtStartStr -match "^\d{8}T\d{6}$") { $startTime = [DateTime]::ParseExact($dtStartStr, "yyyyMMddTHHmmss", $null) }
                }
                if ($dtEndStr) {
                    if ($dtEndStr -match "^\d{8}$") { $endTime = [DateTime]::ParseExact($dtEndStr, "yyyyMMdd", $null) }
                    elseif ($dtEndStr -match "^\d{8}T\d{6}Z$") { $endTime = [DateTime]::ParseExact($dtEndStr, "yyyyMMddTHHmmssZ", $null, [System.Globalization.DateTimeStyles]::AssumeUniversal).ToLocalTime() }
                    elseif ($dtEndStr -match "^\d{8}T\d{6}$") { $endTime = [DateTime]::ParseExact($dtEndStr, "yyyyMMddTHHmmss", $null) }
                }
                if (-not $startTime) { continue }
                if (-not $endTime) { $endTime = $startTime.AddHours(1) }

                # 既存データの照合
                $existingTask = $script:AllTasks | Where-Object { $_.ID -eq $uid } | Select-Object -First 1
                $existingEvent = $null; $existingEventDateKey = $null
                if (-not $existingTask) {
                    foreach($key in $script:AllEvents.PSObject.Properties.Name) {
                        $list = $script:AllEvents.$key
                        if ($list) { foreach($evt in $list) { if ($evt.ID -eq $uid) { $existingEvent = $evt; $existingEventDateKey = $key; break } } }
                        if ($existingEvent) { break }
                    }
                }

                if ($existingTask) {
                    if ($mode -eq "Skip") { $countSkipped++; continue }
                    # タスク更新
                    $existingTask.タスク = $summary
                    $existingTask.期日 = if ($isAllDay) { $startTime.ToString("yyyy-MM-dd") } else { $startTime.ToString("yyyy-MM-dd HH:mm") }
                    if ($description -match "カテゴリ:\s*(.*?)(\r\n|$)") { $existingTask.カテゴリ = $matches[1].Trim() }
                    if ($description -match "進捗:\s*(.*?)(\r\n|$)") { $existingTask.進捗度 = $matches[1].Trim() }
                    if ($description -match "優先度:\s*(.*?)(\r\n|$)") { $existingTask.優先度 = $matches[1].Trim() }
                    $countUpdated++
                } elseif ($existingEvent) {
                    if ($mode -eq "Skip") { $countSkipped++; continue }
                    # イベント更新
                    $newDateKey = $startTime.ToString("yyyy-MM-dd")
                    $existingEvent.Title = $summary; $existingEvent.StartTime = $startTime.ToString("o"); $existingEvent.EndTime = $endTime.ToString("o"); $existingEvent.IsAllDay = $isAllDay
                    if ($newDateKey -ne $existingEventDateKey) {
                        $oldList = [System.Collections.ArrayList]@($script:AllEvents.$existingEventDateKey); $oldList.Remove($existingEvent); $script:AllEvents.$existingEventDateKey = $oldList.ToArray()
                        if (-not $script:AllEvents.PSObject.Properties[$newDateKey]) { $script:AllEvents | Add-Member -MemberType NoteProperty -Name $newDateKey -Value @() }
                        $newList = [System.Collections.ArrayList]@($script:AllEvents.$newDateKey); $newList.Add($existingEvent); $script:AllEvents.$newDateKey = $newList.ToArray()
                    }
                    $countUpdated++
                } else {
                    # 新規登録
                    $isTask = ($description -match "カテゴリ:" -or $description -match "進捗:")
                    if ($isTask) {
                        $cat = ""; $stat = "未実施"; $prio = "中"
                        if ($description -match "カテゴリ:\s*(.*?)(\r\n|$)") { $cat = $matches[1].Trim() }
                        if ($description -match "進捗:\s*(.*?)(\r\n|$)") { $stat = $matches[1].Trim() }
                        if ($description -match "優先度:\s*(.*?)(\r\n|$)") { $prio = $matches[1].Trim() }
                        $newTask = [PSCustomObject]@{
                            ID = $uid; ProjectID = $defaultProjectId; タスク = $summary; 進捗度 = $stat; 優先度 = $prio
                            期日 = if ($isAllDay) { $startTime.ToString("yyyy-MM-dd") } else { $startTime.ToString("yyyy-MM-dd HH:mm") }
                            カテゴリ = $cat; サブカテゴリ = ""; 通知設定 = "全体設定に従う"; 保存日付 = (Get-Date).ToString("yyyy-MM-dd")
                            完了日 = ""; TrackedTimeSeconds = 0; WorkFiles = ""
                        }
                        $script:AllTasks += $newTask
                    } else {
                        $newEvent = [PSCustomObject]@{ ID = $uid; Title = $summary; StartTime = $startTime.ToString("o"); EndTime = $endTime.ToString("o"); IsAllDay = $isAllDay }
                        $dateKey = $startTime.ToString("yyyy-MM-dd")
                        if (-not $script:AllEvents.PSObject.Properties[$dateKey]) { $script:AllEvents | Add-Member -MemberType NoteProperty -Name $dateKey -Value @() }
                        $evList = [System.Collections.ArrayList]@($script:AllEvents.$dateKey); $evList.Add($newEvent); $script:AllEvents.$dateKey = $evList.ToArray()
                    }
                    $countImported++
                }
            }

            # 保存と更新
            Write-TasksToCsv -filePath $script:TasksFile -data $script:AllTasks
            Save-Events
            Update-AllViews
            
            [System.Windows.Forms.MessageBox]::Show("インポートが完了しました。`n新規: $countImported 件`n更新: $countUpdated 件`nスキップ: $countSkipped 件", "完了", "OK", "Information")
            $form.Close()

        } catch {
            [System.Windows.Forms.MessageBox]::Show("インポート中にエラーが発生しました: $($_.Exception.Message)", "エラー", "OK", "Error")
        }
    })

    # --- 共通設定 ---
    Set-Theme -form $form -IsDarkMode $script:isDarkMode
    if ($initialTab -eq "Import") { $tabControl.SelectedTab = $tabImport }
    $form.ShowDialog($parentForm) | Out-Null
}