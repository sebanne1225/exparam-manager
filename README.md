# ExParam Manager

VRChat アバターの Expression Parameters を可視化・逆引きし、削除・非同期化・bit 幅縮小を非破壊で行う NDMF ツールです。

## Overview (EN)

ExParam Manager is a non-destructive NDMF tool for VRChat avatars (`com.sebanne.exparam-manager`).
It visualizes Expression Parameters consumption against the 256-bit limit, traces parameter references across Menus, Animators, Parameter Drivers, PhysBones, Contacts, and Modular Avatar components, and non-destructively applies `Remove`, `MakeLocal`, or experimental `ResizeBitWidth` (2–8 bit on SDK 3.10.6+) during avatar build without modifying original assets.

## 主な機能（何ができるか）

- **合計 bit バー ＋ Prefab / サブツリー別 bit 集計**: 256 bit 上限に対する変更前→変更後の消費 bit をプレビューし、Prefab / サブツリー単位の消費内訳を把握できます。
- **パラメータ逆引き（4 面スキャン）**:
  - **メニュー**: Extract Menu 済みの `MA Menu Group` / `MA Menu Item` は対象 GameObject 参照、未実行の場合は `VRCExpressionsMenu` アセット参照を表示します。
  - **Animator（5 か所）**: `Transition Conditions` / `AnyState Transitions` / `BlendTree` 再帰 / `State` の有効な `*ParameterActive` / `VRCAvatarParameterDriver`（`Copy` 元 `sourceParam` 含む）を走査します（`AnimatorOverrideController` 対応）。
  - **VRChat コンポーネント**: `VRCPhysBone`（派生 5 サフィックス `_IsGrabbed`, `_IsPosed`, `_Angle`, `_Stretch`, `_Squish` 完全一致）と `VRCContactReceiver` の参照を検出します。
  - **Modular Avatar 連携**: スコープ解決（`GetParameterRemappingsAt`）および MA Reactive 系コンポーネント（`ObjectToggle` / `ShapeChanger` / `MaterialSetter`）の参照を検出します。
- **非破壊の削減操作 3 種（NDMF コンポーネント方式）**:
  - **削除（`Remove`）**: ビルド時にパラメータを除去し、最終 `defaultValue` を Playable Layer 初期値へ非破壊で書き戻します。専用メニュー項目も既定でビルド時非表示にし、今回の除去で空になった SubMenu は自動で隠します（元から空の SubMenu は残します）。
  - **非同期化（`MakeLocal`）**: `networkSynced = false` に変更して 0 bit 化します。
  - **bit 幅縮小（`ResizeBitWidth`・実験的機能・SDK 3.10.6 以降）**: Int / Float を 2〜8 bit へ縮小し、`flippedRepresentation` に合わせた `defaultValue` クランプを行います（※ VRChat 2 クライアント間の同期挙動は未検証の実験的機能です）。
- **安全側の自動提案・保護警告**:
  - 厳格条件を満たす Int の推奨 bit 幅バッジ（`StrictIntRecommended`）、Radial Puppet 専用 Float の参考フィルタ（`RadialFloatReference`）、未使用候補（`UnusedCandidate`）、保護警告フラグ（`BuiltIn`, `SdkDefaultTemplate`, `ExternalPluginManaged`, `HasParameterDriverWrite`, `HasPhysBoneOrContact`, `UsedInMultiAxisControl`, `InternalParameter`, `ExternalInputSuspected`）を表示します。
- **NDMF Build Console 出力**: ビルド時に適用サマリ（bit 変化・各操作件数）、不完全になったマルチ軸メニュー警告、256 bit 超過時のエラー／警告を NDMF Build Console へ出力します。

## 非破壊の仕組み（2 パス NDMF ビルド）

- **Pass 1（`Transforming`）**: コンポーネントの設定・ルールおよび `ModularAvatarSyncParameterSequence` の有無を `BuildState` へ退避します。
- **Pass 2（`Optimizing`）**: 元の `VRCExpressionParameters` / `VRCExpressionsMenu` / `AnimatorController` アセットを直接書き換えず、一時クローンを作成して `ObjectRegistry.RegisterReplacedObject` で差し替えます（変更 0 件のメニューは複製しません）。
- **自動除外と復旧**: `ExParamManagerComponent` は `INDMFEditorOnly`（NDMF 1.5.4 互換環境では `IEditorOnly`）を実装しており、ビルド時に自動除外されます。コンポーネントを外すだけで元の状態に完全に復元できます。

## スクリーンショット

※ 準備中（`Documentation~/images/` に追加予定）

## 対応環境

- Unity `2022.3`
- VRChat SDK（Avatars）`>= 3.5.0`（可変 bit 幅縮小は `>= 3.10.6` で有効、3.10.5 以下でも可視化・逆引き・削除・非同期化が動作）
- NDMF `>= 1.5.4`
- Modular Avatar `>= 1.11.2`（任意：未導入環境・旧 MA 環境でもコンパイル・動作）
- VCC / VPM ベースの VRChat プロジェクトを推奨します
- **検証済み組み合わせ例**:
  - VRChat SDK `3.10.6` / NDMF `1.15.1` / Modular Avatar `1.19.0`
  - VRChat SDK `3.10.5` / NDMF `1.14.8` / Modular Avatar `1.18.7`
  - VRChat SDK `3.7.1` / NDMF `1.5.4` / Modular Avatar `1.10.3`

## VCC / VPM での導入

### 推奨: VCC / VPM から導入

1. VCC に追加する URL として `https://sebanne1225.github.io/sebanne-listing/index.json` を追加します（VPM listing への反映後に利用可能になります）。
2. package 一覧から `ExParam Manager` (`com.sebanne.exparam-manager`) を追加します。
3. Unity を開き、package が導入されていることを確認します。

参考ページ (`VCC` 追加先ではありません): `https://sebanne1225.github.io/sebanne-listing/`

### 補助: Git URL / Release zip から導入

- repo: `https://github.com/sebanne1225/exparam-manager`
- Git URL や local package での導入は、開発確認や手動検証向けの補助導線です
- GitHub Release の zip も補助導線として使えます。`com.sebanne.exparam-manager-<version>.zip` を展開すると、直下に `package.json` が見える package 構成です

## 使い方

1. アバター（アバタールート推奨）に `Add Component` → `ExParam Manager/ExParam Manager` を追加します（またはメニューバー `Tools/Sebanne/ExParam Manager` を開いて「ルートに追加」ボタンからも追加できます）。
2. Inspector の「ExParam Manager ウィンドウを開く」またはメニューバー `Tools/Sebanne/ExParam Manager` から専用ウィンドウを開きます。
3. 検索・型フィルタ・状態フィルタ（`StrictIntRecommended` / `RadialFloatReference` / `UnusedCandidate` / `Protected` 等）・Prefab 集計チップを使って、パラメータ一覧と右パネルの逆引き参照を確認します。
4. 各行の操作（`Keep` / `Remove` / `MakeLocal` / `ResizeBitWidth`）や、右パネルの推奨適用ボタン・個別設定（「専用メニュー項目も隠す」「目標 bit 幅（実験）」）を設定します。
5. 通常通りビルド／アップロードすると NDMF が非破壊で適用し、NDMF Build Console に結果サマリを出力します。

## 制限事項

- VRCFury 等、`IParameterProvider` 未実装のツールがビルド時に生成する動的パラメータは Edit Mode 一覧には表示されません（ビルド後の NDMF Build Console で最終合計 bit を確認できます）。
- Modular Avatar の `internalParameter` は初版では可視化・逆引きのみ対応し、変更操作は v1.1 以降で対応予定です。
- `ModularAvatarSyncParameterSequence` 併用時は PC/Quest 同期順序ズレを防ぐため、既定で同期列変更を安全ブロックします（詳細設定の「SyncParameterSequence 上書き許可」で解除可能）。
- `ResizeBitWidth`（2〜8 bit 縮小）は VRChat 2 クライアント間同期の実機検証が未了のため、実験的機能として提供します。
- `Remove` で削除したパラメータは同期対象から外れるため、`Saved`（VRChat アバター間の状態保存）も失われます（`defaultValue` は Animator 初期値へ書き戻されます）。

## ライセンス

MIT License です。詳細は `LICENSE` を参照してください。
