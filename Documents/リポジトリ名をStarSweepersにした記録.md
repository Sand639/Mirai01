# 宇宙ごみを別リポジトリに分ける案（**見送り。名前を変えて、このまま続ける**）

| 項目 | 内容 |
| --- | --- |
| 担当者 | 大槻 海斗（調査：Claude Code） |
| 作成日 | 2026年9月24日（木） |
| 最終更新日 | 2026年9月28日（月） |
| 状態 | **見送り。** リポジトリを**このまま使い続け、名前だけ `StarSweepers` に変えた** |

---

## 結論（2026/9/28）

**リポジトリは分けない。**

一度 `StarSweepers` という別のリポジトリを作って中身を移したが、
**メンバー全員が新しいリポジトリへ乗り換える手間が大きい**ため取りやめた（大槻さんの判断）。
別に作ったリポジトリは**削除済み**。

代わりに、**いまのリポジトリの名前を `Mirai01` → `StarSweepers` に変えた。**
中身はそのまま（釣り・ロボット・レースなどのプロトタイプも同じ場所にある）。

| 変えたところ | 変えた後 |
| --- | --- |
| GitHubのリポジトリ名 | `Sand639/StarSweepers` |
| Unityのプロジェクトフォルダ | `StarSweepers/`（中身・`.meta` はそのまま） |
| Unityのメニュー | **`Tools > StarSweepers`**（37か所） |
| プロジェクト名・シェーダー名 | `StarSweepers` ／ `StarSweepers/Glass` |
| ロビーの合言葉の元になる名前（`sessionName`） | `StarSweepers`（シーン3つ） |

**釣りと共用しているフックの操作の部品（`HookController` など）は、引き続き共用のまま。**
直すときは、**釣り側でも動くかを見ること**（`FishingHookTest` など）。

---

## 何を調べたか（また分けたくなったとき用に残す）

### 宇宙ごみだけでは動かない

宇宙ごみ専用のファイルは79個あるが、動かすには**釣り側の部品が必要**。

| 使っているもの | 何に使うか |
| --- | --- |
| `HookController` / `ThrowController` / `HookProjectile` / `HookLine` / `HookChargeUI` / `HookAimAssist` | フックの操作そのもの |
| `HookableObject` / `FishingNetSupply` | 引っ掛けられる物と、その通信 |
| `FishingNetPlayer` / `FishingPlayerController` / `PlayerAimController` / `TopDownCameraFollow` / `PlayerStun` | プレイヤーの体・移動・狙い・カメラ |
| `FishingSceneBuilder` / `FishingObjectSpawner` / `FishingMatch` / `FishingTeams` | シーンを作るツールと、ルールの一部 |
| `Scripts/Common/` `Scripts/UI/`（`GamePause` `PauseMenu` `GamepadInput` `DraggableGuiPanel` など） | ポーズ・入力・画面の共通部品 |

**「宇宙ごみのフォルダだけコピー」では動かない。**

### 分けるなら、どうやるのがよいか（実際に一度やって確かめた）

**リポジトリを丸ごと複製してから、要らないものを削る。**

- `.meta` の番号（GUID）がそのまま残るので、**シーンとプレハブの参照が壊れない**
- Unityの設定（入力方式・パッケージ・レイヤー・ビルドの一覧・Unityのバージョン）ごと移せる
- 名前を変えるところ … Unityのプロジェクトフォルダ、メニュー、プロジェクト名、シェーダー名、
  ロビーの `sessionName`、ドキュメントのパス
- `Assets/` の削除は**必ずUnity上で**行う（OSで消すと `.meta` との対応が壊れる）

### 分けるときに気をつけること

- **分けた時点で、共用部品は別物になる。** 片方で直しても、もう片方には入ってこない
- **メンバー全員がクローンし直す手間がかかる**（今回はこれが理由で見送った）
- どちらが最新か分からなくなるので、**使わないほうを止める**決めごとが要る

---

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/9/24 | Claude Code | 新規作成。依存関係の調査と、移行のしかたの候補をまとめた（当時は未決定） |
| 2026/9/28 | Claude Code | **別リポジトリへの移行は見送り**（大槻さん。メンバーの乗り換えの手間が大きいため）。作った `StarSweepers` リポジトリは削除。代わりに**このリポジトリの名前を StarSweepers に変えた**ことを記録した |
