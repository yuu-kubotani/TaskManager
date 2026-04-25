# 🚀 TaskManager for Windows (No Admin Required)

**管理者権限不要！** PowerShellだけで動く、高機能タスク管理ツール。
インストール不要、レジストリ汚染なし。USBメモリや共有フォルダに置くだけで、どこでも「個人の業務効率化」環境が手に入ります。

![Demo](https://via.placeholder.com/800x400.png?text=Put+Screenshot+Here)
*(ここにツールのメイン画面や、動いている様子のGIFを貼ってください)*

## 💡 特徴 (Features)

企業PCの「勝手にソフトを入れられない」という悩みを解決するために開発されました。

* **⚡ 完全ポータブル**: インストール作業は一切不要。フォルダをコピーするだけで動作します。
* **🛡️ 管理者権限不要 (No Admin)**: PowerShellが動くWindowsならどこでも動作します。
* **📊 多彩なビュー**:
  * **リスト表示**: 従来のタスク管理。
  * **カンバンボード**: ドラッグ＆ドロップで直感的にステータス管理。
  * **カレンダー & タイムライン**: スケジュールと実績時間を可視化。
* **⏱️ 時間計測 (Time Tracking)**: タスクごとの作業時間を記録し、予実管理が可能。
* **📈 自動レポート**: HTML形式で作業時間の推移やカテゴリ別集計をグラフ化。
* **🎨 ダークモード対応**: 長時間の作業でも目に優しい設計。

## 📦 必要要件 (Requirements)

* **OS**: Windows 10 / 11
* **Runtime**: PowerShell 5.1 (Windowsに標準搭載されています)

## 🚀 使い方 (Usage)

1. このリポジトリをダウンロード（ZIP解凍）します。
2. フォルダ内の **start.bat** をダブルクリックしてください。
3. ツールが起動します。

> **Note:** 初回起動時、Windowsのセキュリティ警告が出ることがありますが、「詳細情報」→「実行」を選択してください（署名なしスクリプトのため）。

## 📁 フォルダ構成

```text
TaskManager/
├── TaskManager.sln                  # ソリューションファイル（プロジェクト全体を管理）
├── Program.cs                       # アプリケーションのエントリポイント（メイン処理）
├── appsettings.json                 # アプリケーションの基本設定（DB接続情報など）
├── appsettings.Development.json     # 開発環境用の設定（Git管理対象）
├── Properties/                      # プロジェクトのプロパティ設定
│   └── launchSettings.json          # デバッグ実行時の起動プロファイル設定
├── master_data.csv                  # アプリで利用する初期マスターデータ
├── .gitignore                       # Git管理から除外するファイル・フォルダのルール
├── Docs/                            # ドキュメント類を格納するディレクトリ
│   └── readme.md                    # プロジェクトの概要と使い方（このファイル）
├── backup/                          # バックアップデータ保存用（※実行時に自動生成される）
├── (レポートやログ類)               # *.html や *.log など（※実行時に自動生成される）
└── (taskmanager.db 等)              # SQLiteなどのローカルDBファイル（※実行時に自動生成される）
```

## 🤝 開発に参加する (Contribution)

このプロジェクトは、**「現場レベルでの業務効率化」** を目指す全ての人のためにオープンにされています。
機能追加、バグ報告、ドキュメントの改善など、あらゆる貢献を歓迎します！

### 初心者の方へ

GitHubに慣れていない方でも、以下の方法で参加できます：

* **使ってみる**: バグを見つけたら Issues で教えてください。
* **アイデアを出す**: 「こんな機能が欲しい」という要望もIssuesで受け付けています。

### プルリクエストの手順

1. このリポジトリを **Fork** してください。
2. 新しい Branch を作成します (`git checkout -b feature/AmazingFeature`)。
3. 変更を Commit します (`git commit -m 'Add some AmazingFeature'`)。
4. Branch に Push します (`git push origin feature/AmazingFeature`)。
5. **Pull Request** を作成してください。