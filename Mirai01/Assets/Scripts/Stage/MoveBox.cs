using UnityEngine;

public class MoveBox : MonoBehaviour
{
    [SerializeField] private GameObject token;
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float waitTime = 1f;

    private float startY;
    private float targetY;
    private float timer;

    private int state = 0;

    void Start()
    {
        // Cubeの開始位置
        startY = transform.position.y;

        // ゲーム開始時の「Tokenの実際のワールド座標」を記憶
        targetY = token.transform.position.y;

        token.SetActive(false);
    }

    void Update()
    {
        // 上昇
        if (state == 0)
        {
            Vector3 target = new Vector3(
                transform.position.x,
                targetY,
                transform.position.z
            );

            transform.position = Vector3.MoveTowards(
                transform.position,
                target,
                moveSpeed * Time.deltaTime
            );

            if (Mathf.Abs(transform.position.y - targetY) < 0.01f)
            {
                transform.position = new Vector3(
                    transform.position.x,
                    targetY,
                    transform.position.z
                );

                timer = waitTime;
                state = 1;
            }
        }

        // 上で停止
        else if (state == 1)
        {
            timer -= Time.deltaTime;

            if (timer <= 0)
            {
                state = 2;
            }
        }

        // 下降
        else if (state == 2)
        {
            Vector3 target = new Vector3(
                transform.position.x,
                startY,
                transform.position.z
            );

            transform.position = Vector3.MoveTowards(
                transform.position,
                target,
                moveSpeed * Time.deltaTime
            );

            if (Mathf.Abs(transform.position.y - startY) < 0.01f)
            {
                transform.position = new Vector3(
                    transform.position.x,
                    startY,
                    transform.position.z
                );

                timer = waitTime;
                state = 3;
            }
        }

        // 下で停止
        else if (state == 3)
        {
            timer -= Time.deltaTime;

            if (timer <= 0)
            {
                state = 0;
            }
        }
    }
}