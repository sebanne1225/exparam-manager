## Goal

VRChat アバターの 256 bit 同期パラメータ（`VRCExpressionParameters`・MA Parameters・他 NDMF プラグイン）の内訳・出所・参照先（メニュー / Animator / Driver / PhysBone / Contact / MA Reactive）を可視化・逆引きし、納得した上で「削除 / 非同期化 / bit 幅縮小（2〜8 bit）」を非破壊適用する独立 NDMF ツール（`ExParam Manager` / `com.sebanne.exparam-manager`）。

## Current State

- **T0 完了（2026-11-24）**: テンプレートからの初期骨組み作成・`.gitignore`（`/[Ll]ibrary/`）修正・4 `.asmdef` 構成と設計書 `plan.md` v2 の紐付けまで完了。C# 本体実装はこれから（T0.5 多アバター横断観測 → T1〜T6）。
- **公開状態**: ローカル repo のみ（GitHub private repo 作成・初回 push は初版動作確認後にせ承認で実施）。

## Current Blocker

なし。

## Tasks

- T0.5: 手元複数アバター（10〜20 体）のパラメータ横断統計観測（read-only YAML）
- T1〜T6: 4 `.asmdef`・Runtime・解析コア・安全提案・NDMF 2 パス・UI・NUnit/実機検証

## Rules

- 非破壊を最優先にし、元アセット（`ExpressionParameters` / `VRCExpressionsMenu` / `AnimatorController`）を直接書き換えず一時クローン ＋ `ObjectRegistry.RegisterReplacedObject` で差し替える
- `Editor/Sebanne.ExParamManager.Editor.asmdef` から Modular Avatar を直接参照しない（`IMaAdapter` ＋ `Editor/ModularAvatar/` 分離で `TD-T39` 型コンパイル不能を防ぐ）
- `HAS_VRCSDK3_AVATARS_3_10_6` と `HAS_NDMF_1_15` の 2 本の `versionDefines` で SDK 3.10.5 以下と 3.10.6 以降を両対応にする

## ファイル構成

- `Runtime/Sebanne.ExParamManager.asmdef` — `ExParamManagerComponent`（`INDMFEditorOnly`）配置予定
- `Editor/Sebanne.ExParamManager.Editor.asmdef` — 解析・逆引き・安全提案・NDMF 2 パス・UI（MA 非依存）
- `Editor/ModularAvatar/Sebanne.ExParamManager.Editor.ModularAvatar.asmdef` — MA `>= 1.11.2` 任意アダプタ（今後追加予定）
- `Tests/Editor/Sebanne.ExParamManager.Editor.Tests.asmdef` — EditMode NUnit テスト（今後追加予定）
- 設計書・KB 仕事棚（正本）:
  - `Repos/sebanne-dev-knowledge-base/notes/artifacts/exparam-manager/INDEX.md`
  - `Repos/sebanne-dev-knowledge-base/notes/artifacts/exparam-manager/plans/first-loop/plan.md`

## 次回再開用の要点

1. まず `T0.5` として手元複数アバター（10〜20 体）の Expression Parameters / MA Parameters を横断集計し、Float Radial や保護プレフィックス分布を確かめる。
2. `T1` で 4 `.asmdef` と `ExParamManagerComponent`（`Component` 直接参照主キー）を実装する。
3. 2 環境検証先は最新 SDK `3.10.6`（`ToolDevWorkspace` / `AFKkip`）と旧 SDK `3.10.5`（`Kaguya` / `Chiffon` 等）。

## 技術知見（任意）

汎用 NDMF / SDK / MA 知見は `Repos/sebanne-dev-knowledge-base/notes/technical/unity-ndmf.md` へ昇格済み。

## 次フェーズ候補

knowledge-base `records/next-phase/tool-dev.md`（`[[TD-T60]]`）を参照。
