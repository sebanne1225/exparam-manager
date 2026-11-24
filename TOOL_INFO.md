# TOOL_INFO

このファイルは、`ExParam Manager` の repo 補助文書です。README の代わりではなく、公開準備や listing 反映時に確認したい情報を短くまとめています。

## 基本情報

- ツール名: `ExParam Manager`
- package名: `com.sebanne.exparam-manager`
- 表示名: `ExParam Manager`
- Runtime asmdef: `Sebanne.ExParamManager`
- Editor asmdef: `Sebanne.ExParamManager.Editor`
- Optional MA asmdef: `Sebanne.ExParamManager.Editor.ModularAvatar`
- 現在 version: `0.1.0`

## 公開メタ情報

- GitHub repo: `https://github.com/sebanne1225/exparam-manager`
- changelogUrl: `https://github.com/sebanne1225/exparam-manager/blob/main/CHANGELOG.md`
- listing repo: `https://github.com/sebanne1225/sebanne-listing`
- 参考 listing page (`VCC` 追加先ではない): `https://sebanne1225.github.io/sebanne-listing/`
- VCC に追加する URL: `https://sebanne1225.github.io/sebanne-listing/index.json`
- listing 側に追加する `githubRepos`: `sebanne1225/exparam-manager`
- BOOTH 販売名: TBD

## 公開スコープの要約

- VRChat アバターの Expression Parameters 可視化・Prefab 別 bit 集計
- メニュー（Extract Menu 済み GameObject 直接ジャンプ / 未実行アセット両対応）・Animator 5 か所・Driver・PhysBone・Contact・MA Reactive 逆引き
- NDMF 非破壊 2 パス方式による削除（`defaultValue` 書き戻し＋メニュー項目非表示）・非同期化・可変 bit 幅縮小（実験的機能ラベル付き）
- VRChat SDK `3.10.5` 以下と `3.10.6` 以降の両対応、NDMF `>= 1.5.4` 対応、Modular Avatar `>= 1.11.2` 任意分離

## 導入導線の前提

- 主導線は VCC / VPM
- Git URL / local package 導入は補助扱い

## 既知の制限

- MA `internalParameter` の変更操作は初版対象外（可視化・逆引きのみ、v1.1 解禁予定）
- `ModularAvatarSyncParameterSequence` 検出時は既定で同期列変更を安全ブロック
- `ResizeBitWidth` は 2 クライアント同期未検証の実験的機能ラベル付き提供
- VRCFury 等 `IParameterProvider` 未実装ツールのビルド時生成パラメータは Edit Mode 不可視
