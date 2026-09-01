# プレイヤーの操作とTPS/FPS切り替え

| 項目 | 内容 |
| --- | --- |
| 担当者 | Claude Code（依頼：大槻 海斗） |
| 作成日 | 2026年9月1日（火） |
| 最終更新日 | 2026年9月1日（火） |
| 状態 | 完成 |

---

## この機能は何か

**どんなプロトタイプでも使い回せる、動かせるキャラクター。**

カプセルの形をしたキャラクターを、キーボードとマウスで動かせる。
カメラは**後ろから見る（TPS）**と**本人の目線（FPS）**を、キー1つで行き来できる。

企画がまだ決まっていないため、「とりあえず動かして試す」ための土台として作った。
**プレハブを置くだけで動く**ので、新しいアイデアを試すシーンにそのまま持っていける。

---

## 遊ぶ人から見た動き

| 操作 | 結果 |
| --- | --- |
| **W / A / S / D** | 前・左・後ろ・右に歩く |
| **マウスを動かす** | 視点が回る。左右は体ごと、上下は首だけ動く |
| **Shift を押しながら** | 速く走る |
| **Space** | ジャンプ |
| **V** | **TPS（後ろから）と FPS（目線）を切り替える** |
| **Escape** | マウスカーソルを出す（操作をやめたいとき） |
| **画面をクリック** | カーソルを消して操作に戻る |

- **FPSにすると、画面の左右に自分の腕が出る**（マインクラフトのような見た目）。
  細長い「腕」の先に「手」が付いており、**色が違うので手の位置が分かる**
- FPSでは、カプセルの体は**まるごと自動で隠れる**（目の中から見ると視界が埋まるため）
- TPSでは腕が消え、カプセルが表示される。カプセルの前面には小さな箱が付いていて、
  **どちらを向いているか**が分かる

---

## すぐ試す方法

1. Unityで `Assets/Scenes/Test/PlayerRigTest.unity` を開く
2. **再生ボタンを押す**

これだけで動く。地面・坂・目印の箱が置いてあるので、歩き回って確認できる。

## 自分のシーンで使う方法

1. `Assets/Prefabs/PlayerRig.prefab` を、シーンにドラッグして置く
2. **そのシーンに元からある `Main Camera` を消す**（カメラが2つあると映らなくなるため）

以上。設定をつなぐ作業は要らない。

---

## 関係するファイル・シーン

| 種類 | パス |
| --- | --- |
| スクリプト | `Assets/Scripts/Player/PlayerController.cs` |
| スクリプト | `Assets/Scripts/Player/PlayerViewSwitcher.cs` |
| スクリプト（エディタ用） | `Assets/Scripts/Player/Editor/PlayerRigSetup.cs` |
| プレハブ | `Assets/Prefabs/PlayerRig.prefab` |
| シーン | `Assets/Scenes/Test/PlayerRigTest.unity` |
| マテリアル | `Assets/Art/Materials/PlayerRigBody.mat`（体と腕）<br>`Assets/Art/Materials/PlayerRigSkin.mat`（手）<br>`Assets/Art/Materials/TestGround.mat`（地面） |
| 入力設定 | `Assets/InputSystem_Actions.inputactions`（**既存のものを使用。変更していない**） |

> シーン（`.unity`）とプレハブ（`.prefab`）は**同時に2人以上で編集できない**。
> `PlayerRigTest.unity` と `PlayerRig.prefab` を触るときは、担当者に一声かけること。

### プレハブの中身

```
PlayerRig                 ← CharacterController / PlayerController / PlayerViewSwitcher
├── Body                  ← カプセルの見た目。TPSのときだけ表示
│   └── FrontMark         ← 前を示す小さな箱
└── CameraPivot           ← 上下の首振りをする場所（目の高さ 1.6m）
    └── PlayerCamera      ← カメラ。TPSとFPSでこの位置が動く
        └── FirstPersonArms   ← FPSのときだけ表示
            ├── ArmLeft       ← Sleeve（腕）＋ Hand（手）
            └── ArmRight      ← Sleeve（腕）＋ Hand（手）
```

腕は**カメラの子**にしてある。視点を動かしても腕が画面に付いてくるのはこのため。

---

## 設定できる値（インスペクターの項目）

`PlayerRig` を選ぶと、右側のインスペクターで調整できる。**プログラムを触らずに感触を変えられる。**

### PlayerController（動きと視点）

| 項目名 | 意味 | 初期値 |
| --- | --- | --- |
| Walk Speed | 歩く速さ（1秒あたりのメートル） | 4 |
| Sprint Speed | Shiftを押している間の速さ | 7 |
| Jump Height | ジャンプで上がる高さ（メートル） | 1.2 |
| Gravity | 落ちる強さ。**マイナスの値にすること** | -20 |
| Mouse Sensitivity | マウスで回る量。大きいほど速い | 0.12 |
| Max Look Up / Down | 上下を向ける限界の角度 | 80 / 80 |
| Lock Cursor On Start | 再生したらカーソルを消すか | ON |

### PlayerViewSwitcher（カメラの切り替え）

| 項目名 | 意味 | 初期値 |
| --- | --- | --- |
| Start Mode | 再生したときにどちらで始めるか | Third Person |
| Switch Key | 切り替えに使うキー | V |
| Distance | TPSのときの、キャラクターからの距離 | 4 |
| Height Offset | TPSのときに上へずらす量 | 0.4 |
| Switch Smooth | 切り替わる滑らかさ。0で一瞬 | 12 |

---

## 仕組み（分かる人向け）

- 移動は **CharacterController** を使っている。物理演算（Rigidbody）より挙動が読みやすく、プロトタイプ向きのため
- 入力は **Input System**。既存の `InputSystem_Actions` の `Player` マップから
  `Move` / `Look` / `Jump` / `Sprint` を名前で取得している。**入力設定ファイルは変更していない**ので、
  他の人の作業とぶつからない
- 視点は、**左右＝キャラクター本体を回す／上下＝`CameraPivot` だけを回す**という分け方をしている。
  体ごと傾けないための定番の作り
- TPS/FPSの切り替えは、`PlayerCamera` の**ローカル座標を動かしているだけ**。
  カメラを2つ用意して切り替える方式にはしていない（片方の設定を変え忘れる事故を防ぐため）
- **他のオブジェクトを名前で探していない**（`GameObject.Find` を使っていない）ので、
  どのシーンに置いても動く（`Documents/シーンとプレハブの作り方.md` の方針どおり）

### 切り替えキーだけ、入力設定を使っていない理由

`InputSystem_Actions` には視点切り替え用のアクションが無い。
そこにアクションを足すと**入力設定ファイルを全員で共有している**ため衝突しやすいので、
切り替えキーだけはスクリプト側で直接キーを見ている（Input System の `Keyboard.current` を使用。
旧来の `Input.GetKey` は使っていない）。

---

## できていないこと・既知の問題

- **TPSのとき、カメラが壁をすり抜ける。** 後ろに壁があるとカメラが壁の中に入り、視界が抜けてしまう。
  プロトタイプ用途では困らないと判断して入れていない。必要になったら対応する（`リスクリスト.md` に登録済み）
- **段差を自動で乗り越える高さは 0.3m まで。** それ以上は登れない。インスペクターの
  `CharacterController > Step Offset` で変えられる
- **アニメーションは無い。** カプセルが滑るように動く。
  **FPSの腕も振られない**（歩いても止まったまま）。必要になったら足す
- **腕の位置は固定値で決め打ち。** 画面比率を大きく変えると見え方が変わる可能性がある。
  位置を変えたい場合は `PlayerRigSetup.cs` の `CreateArm` にある数値を調整して作り直す
- **ゲームパッドは未確認。** `Move` と `Look` にはゲームパッドの割り当ても入っているので動く可能性はあるが、
  キーボード・マウスでしか確認していない
- **音は鳴らない。** `PlayerCamera` に AudioListener が付いているだけ

---

## 作り直したいとき

プレハブやシーンを壊してしまった場合、Unityのメニューから作り直せる。

**`Tools > Mirai01 > プレイヤーの検証シーンを作り直す`**

`PlayerRig.prefab` と `PlayerRigTest.unity` が上書きで作り直される。
**手で調整した内容は消える**ので、実行する前に確認すること。

---

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/9/1 | Claude Code | 新規作成。WASD移動・マウス視点・TPS/FPS切り替え・プレハブ化まで |
| 2026/9/1 | Claude Code | FPSに腕（袖＋手）を追加。FPSで向き目印が映り込んでいた不具合を修正し、体をまるごと隠すようにした。マテリアルを `Assets/Art/Materials/` へ移動 |
| 2026/9/1 | Claude Code | マテリアル置き場のフォルダ名を `Matrials` から `Materials`（正しい綴り）に変更 |
