using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **電撃エフェクトの検証シーン用**の操作。
///
/// | キー | すること |
/// | --- | --- |
/// | Space | Burst（一瞬だけ激しくする。攻撃した瞬間のイメージ） |
/// | 1 | 電撃を出す／消す |
/// | 2 | キャラクターを円を描いて動かす／止める（電撃がついてくるかの確認） |
/// </summary>
public class LightningTestDriver : MonoBehaviour
{
    [SerializeField] private HelixLightning lightning;
    [SerializeField] private Transform character;

    [SerializeField] private float moveRadius = 3f;
    [SerializeField] private float moveSpeed = 0.6f;

    private bool moving;
    private float moveAngle;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && lightning != null)
        {
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                lightning.Burst();
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                lightning.IsOn = !lightning.IsOn;
            }

            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                moving = !moving;
            }
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
        GUI.Label(new Rect(16, 16, 600, 80),
            "Space：Burst（一瞬だけ激しく）\n1：電撃を出す／消す\n2：キャラクターを動かす／止める");
    }
}
