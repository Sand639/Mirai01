using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **電撃エフェクトの検証シーン用**の操作。
///
/// | キー | すること |
/// | --- | --- |
/// | F（長押し） | ビームを溜める。**離すと撃つ。長く押すほど遠くまで届く** |
/// | Space | Burst（体のまわりの電撃を一瞬だけ激しくする。攻撃した瞬間のイメージ） |
/// | 1 | 体のまわりの電撃を出す／消す |
/// | 2 | キャラクターを円を描いて動かす／止める（電撃がついてくるかの確認） |
/// </summary>
public class LightningTestDriver : MonoBehaviour
{
    [SerializeField] private HelixLightning lightning;
    [SerializeField] private HelixBeam beam;
    [SerializeField] private Transform character;

    [SerializeField] private float moveRadius = 3f;
    [SerializeField] private float moveSpeed = 0.6f;

    private bool moving;
    private float moveAngle;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null)
        {
            if (beam != null)
            {
                if (keyboard.fKey.wasPressedThisFrame)
                {
                    beam.BeginCharge();
                }

                if (keyboard.fKey.wasReleasedThisFrame)
                {
                    beam.Release();
                }
            }

            if (lightning != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame)
                {
                    lightning.Burst();
                }

                if (keyboard.digit1Key.wasPressedThisFrame)
                {
                    lightning.IsOn = !lightning.IsOn;
                }
            }

            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                moving = !moving;
            }
        }

        // 溜めている間は、体のまわりの電撃も激しくする（溜めている感じを出す）
        if (beam != null && lightning != null && beam.IsCharging)
        {
            lightning.Burst(0.15f);
        }

        if (moving && character != null)
        {
            moveAngle += moveSpeed * Time.deltaTime;
            character.position = new Vector3(Mathf.Cos(moveAngle) * moveRadius, 0f, Mathf.Sin(moveAngle) * moveRadius);
            character.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(moveAngle), 0f, Mathf.Cos(moveAngle)));
        }
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(16, 16, 600, 100),
            "F（長押し）：ビームを溜める → 離すと撃つ（長く押すほど遠くへ）\n" +
            "Space：体の電撃を一瞬だけ激しく\n1：体の電撃を出す／消す\n2：キャラクターを動かす／止める");

        if (beam != null && beam.IsCharging)
        {
            GUI.Label(new Rect(16, 110, 600, 24),
                $"溜め {beam.Charge01 * 100f:0}%　→　{beam.CurrentLength:0.0} m");
        }
    }
}
