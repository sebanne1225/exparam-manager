# ExParam Manager

VRChat アバターの Expression Parameters を可視化・逆引きし、削除・非同期化・bit 幅縮小を非破壊で行う NDMF ツールです。（現在 v0.1.0 初期骨組み段階・開発中）

## 何ができるか（設計仕様）

- **合計 bit バー ＋ Prefab 別 bit 集計**: アバターの消費 bit と 256 bit 上限との差分を可視化し、Prefab / サブツリー単位の消費内訳を把握できます。
- **パラメータ逆引き**: メニュー項目（Extract Menu 済みの `MA Menu Group` なら対象 GameObject へ直接ジャンプ、未実行ならアセットへ Ping）・Animator 5 か所・`VRCAvatarParameterDriver`・`VRCPhysBone`・`VRCContactReceiver`・MA Reactive の参照を一覧表示します。
- **非破壊の削減適用（NDMF コンポーネント方式）**:
  - **削除**: ビルド時にパラメータを除去し、最終 `defaultValue` を Playable Layer 初期値へ非破壊書き戻し（専用メニュー項目も既定でビルド時非表示）。
  - **非同期化**: `networkSynced = false` に変更して 0 bit 化。
  - **bit 幅縮小（実験的機能・SDK 3.10.6 以降）**: Int / Float を 2〜8 bit へ縮小（`flippedRepresentation` 対応。※ 2 クライアント同期未検証の実験的機能ラベル付き）。
- **安全側の自動提案**: 厳格条件を満たす Int の推奨 bit 幅、Radial 専用 Float の参考フィルタ、保護対象（組み込み・SDK テンプレート・Driver・PhysBone・Contact 等）の警告を表示します。
- 元のアバター・元アセットには直接変更を加えない非破壊設計です。

## 対応環境

- Unity `2022.3`
- VRChat SDK（Avatars）`>= 3.5.0`（bit 幅縮小は `>= 3.10.6` で有効、3.10.5 以下でも可視化・逆引き・削除・非同期化が動作）
- NDMF `>= 1.5.4`
- Modular Avatar `>= 1.11.2`（任意：未導入環境でもコンパイル・動作）
- VCC / VPM ベースの VRChat プロジェクトを推奨します

## VCC / VPM での導入

### 推奨: VCC / VPM から導入

1. VCC に追加する URL として `https://sebanne1225.github.io/sebanne-listing/index.json` を追加します。
2. package 一覧から `ExParam Manager` (`com.sebanne.exparam-manager`) を追加します。
3. Unity を開き、package が導入されていることを確認します。

参考ページ (`VCC` 追加先ではありません): `https://sebanne1225.github.io/sebanne-listing/`

### 補助: Git URL / Release zip から導入

- repo: `https://github.com/sebanne1225/exparam-manager`（初版動作確認後に公開設定予定）
- Git URL や local package での導入は、開発確認や手動検証向けの補助導線です
- GitHub Release の zip も補助導線として使えます。`com.sebanne.exparam-manager-<version>.zip` を展開すると、直下に `package.json` が見える package 構成です

## 使い方

1. アバター（または子オブジェクト）に `ExParam Manager` コンポーネントを追加します。
2. Inspector の「ExParam Manager ウィンドウを開く」（またはメニューバー `Tools/Sebanne/ExParam Manager`）から管理ウィンドウを開きます。
3. パラメータ一覧と逆引き結果を確認し、各パラメータの操作（そのまま / 削除 / 非同期化 / bit 幅縮小）を設定してビルドします。

## 制限事項

- VRCFury 等、`IParameterProvider` 未実装のツールがビルド時に生成するパラメータは Edit Mode 一覧には表示されません（ビルド後の NDMF レポートで最終合計 bit を確認できます）。
- Modular Avatar の `internalParameter` は初版では可視化・逆引きのみ対応し、変更操作は v1.1 以降で対応予定です。
- `ModularAvatarSyncParameterSequence` 併用時は PC/Quest 同期順序ズレを防ぐため、既定で同期列変更を安全ブロックします。
- `ResizeBitWidth`（2〜8 bit 縮小）は VRChat 2 クライアント間同期の実機検証が未了のため、実験的機能として提供します。

## ライセンス

MIT License です。詳細は `LICENSE` を参照してください。
