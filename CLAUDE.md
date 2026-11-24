## Goal

VRChat アバターの 256 bit 同期パラメータ（`VRCExpressionParameters`・MA Parameters・他 NDMF プラグイン）の内訳・出所・参照先（メニュー / Animator / Driver / PhysBone / Contact / MA Reactive）を可視化・逆引きし、納得した上で「削除 / 非同期化 / bit 幅縮小（2〜8 bit）」を非破壊適用する独立 NDMF ツール（`ExParam Manager` / `com.sebanne.exparam-manager`）。

## Current State

- **T0〜T7 完了（v0.1.0 実装・ドキュメント整備・Public 公開版）**: 4 `.asmdef` 分離・Runtime・解析/逆引きコア・安全提案・NDMF 2 パスビルド・Inspector/EditorWindow UI・EditMode テスト・README/CHANGELOG/BOOTH_PACKAGE 整備まで完了。
- **検証状態**: 3 環境（SDK 3.10.6 / NDMF 1.15.1、SDK 3.10.5 / NDMF 1.14.8、SDK 3.7.1 / NDMF 1.5.4 / MA 1.10.3）で EditMode テスト全 PASS 確認済み。
- **公開状態**: GitHub Public リポジトリ（`https://github.com/sebanne1225/exparam-manager`）公開済み。

## Current Blocker

なし。

## Tasks

- v0.1.0 GitHub Release タグ作成・`sebanne-listing` への登録
- Unity Editor 実画面での GUI 目視確認・スクリーンショット追加
- v1.1 候補: MA `internalParameter` の変更操作対応、VRChat 2 クライアント間での可変 `bitWidth` 同期実機確認

## Rules

- 非破壊を最優先にし、元アセット（`ExpressionParameters` / `VRCExpressionsMenu` / `AnimatorController`）を直接書き換えず一時クローン ＋ `ObjectRegistry.RegisterReplacedObject` で差し替える
- `Editor/Sebanne.ExParamManager.Editor.asmdef` から Modular Avatar を直接参照しない（`IMaAdapter` ＋ `Editor/ModularAvatar/` 分離で `TD-T39` 型コンパイル不能を防ぐ）
- `HAS_VRCSDK3_AVATARS_3_10_6` / `HAS_NDMF_1_15` / `HAS_NDMF_EDITOR_ONLY` / `HAS_VRCSDK_BASE` により SDK 3.10.5 以下・3.10.6 以降・NDMF 1.5.4+ を両対応にする

## ファイル構成

- `Runtime/Sebanne.ExParamManager.asmdef` — `ExParamManagerComponent`, `ParameterRule`, `ParameterAction`
- `Editor/Sebanne.ExParamManager.Editor.asmdef` — 解析・逆引き・安全提案・NDMF 2 パス・UI（MA 非依存）
- `Editor/ModularAvatar/Sebanne.ExParamManager.Editor.ModularAvatar.asmdef` — MA `>= 1.11.2` 任意アダプタ
- `Tests/Editor/Sebanne.ExParamManager.Editor.Tests.asmdef` — EditMode NUnit テスト（および `Tests/Editor/ModularAvatar/`）
- 設計書・KB 仕事棚（正本）:
  - `Repos/sebanne-dev-knowledge-base/notes/artifacts/exparam-manager/INDEX.md`
  - `Repos/sebanne-dev-knowledge-base/notes/artifacts/exparam-manager/plans/first-loop/plan.md`

## 次回再開用の要点

1. v0.1.0 の GitHub Release 公開（タグ `0.1.0`）と `sebanne-listing` 登録を進める。
2. Unity Editor 上での実画面目視確認・スクリーンショット撮影（`Documentation~/images/`）を行う。
3. v1.1 以降は `internalParameter` の変更操作解禁と VRChat 2 クライアント間 `ResizeBitWidth` 実機確認を検討する。

## 技術知見（任意）

汎用 NDMF / SDK / MA 知見は `Repos/sebanne-dev-knowledge-base/notes/technical/unity-ndmf.md` へ昇格済み。

## 次フェーズ候補

knowledge-base `records/next-phase/tool-dev.md`（`[[TD-T60]]`）を参照。
