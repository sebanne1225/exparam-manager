# Changelog

このファイルは `ExParam Manager` の変更履歴を管理します。

## [0.1.0] - 2026-11-24

### Added

- 4 `.asmdef` 分離構成（Runtime / Editor / Optional Modular Avatar `>= 1.11.2` / Editor Tests）と `HAS_VRCSDK3_AVATARS_3_10_6` / `HAS_NDMF_1_15` / `HAS_NDMF_EDITOR_ONLY` / `HAS_VRCSDK_BASE` による SDK・NDMF 互換分岐を実装
- 解析・逆引きコア（`ParameterSnapshotCollector`, `ReferenceScanner`, `PrefabBitGroup` 集計、MA `GetParameterRemappingsAt` スコープ解決、Menu / Animator 5 か所 / `VRCAvatarParameterDriver` / `VRCPhysBone` / `VRCContactReceiver` / MA Reactive 走査）を実装
- 安全ガード・提案エンジン（`SafetyAndSuggestionAnalyzer`: 保護警告フラグ、厳格 Int 推奨 bit 幅、Radial 専用 Float 参考フィルタ、未使用候補判定）を実装
- NDMF 2 パス非破壊ビルドパイプライン（`Transforming` `BuildState` 退避 → `Optimizing` 削除・`defaultValue` 書き戻し・非同期化・bit 幅縮小・循環安全メニュー非表示・`SyncParameterSequence` 安全ブロック・256 bit 監視・`ErrorReport` 出力）を実装
- Inspector（`ExParamManagerEditor`）および専用ウィンドウ（`ExParamManagerWindow` / `BitBarDrawer`）を実装
- EditMode NUnit テスト一式および 3 環境（SDK 3.10.6 / NDMF 1.15.1、SDK 3.10.5 / NDMF 1.14.8、SDK 3.7.1 / NDMF 1.5.4 / MA 1.10.3）での互換・非破壊ビルド検証を実施
