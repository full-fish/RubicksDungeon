using UnityEngine;
using UnityEngine.InputSystem;

public class RubikPlayer : MonoBehaviour
{
    private RubikManager _manager;

    // ★ 키 반복 입력 설정 (인스펙터에서 조절 가능)
    [Header("입력 민감도 설정")]
    [Tooltip("꾹 눌렀을 때, 반복이 시작되기 전 대기 시간 (초)")]
    public float initialDelay = 0.3f; 

    [Tooltip("반복이 시작된 후, 입력이 들어가는 간격 (초) - 작을수록 빠름")]
    public float repeatRate = 0.1f;

    private float _nextInputTime = 0f; // 다음 입력 가능 시간

    public void Init(RubikManager manager)
    {
        _manager = manager;
    }

    void Update()
    {
        if (_manager == null || Keyboard.current == null) return;

        HandleInput();
    }

    void HandleInput()
    {
        // 1. 이동 입력 (화살표)
        if (CheckKey(Key.RightArrow))      _manager.TryMovePlayer(1, 0);
        else if (CheckKey(Key.LeftArrow))  _manager.TryMovePlayer(-1, 0);
        else if (CheckKey(Key.UpArrow))    _manager.TryMovePlayer(0, 1);
        else if (CheckKey(Key.DownArrow))  _manager.TryMovePlayer(0, -1);

        // 2. 회전 입력 (WASD)
        // else if를 써서 이동과 회전이 동시에 안 되게 막음 (원하시면 else 빼도 됨)
        else if (CheckKey(Key.D)) _manager.TryPushRow(-1);
        else if (CheckKey(Key.A)) _manager.TryPushRow(1);
        else if (CheckKey(Key.S)) _manager.TryPushCol(1);
        else if (CheckKey(Key.W)) _manager.TryPushCol(-1);
    }

    // ★ 키 반복 입력을 처리하는 핵심 함수
    bool CheckKey(Key key)
    {
        var keyControl = Keyboard.current[key];

        // 1. 방금 막 눌렀을 때 (즉시 실행)
        if (keyControl.wasPressedThisFrame)
        {
            _nextInputTime = Time.time + initialDelay; // 다음 입력은 initialDelay 뒤에
            return true;
        }

        // 2. 꾹 누르고 있을 때 (타이머 체크)
        if (keyControl.isPressed && Time.time >= _nextInputTime)
        {
            _nextInputTime = Time.time + repeatRate; // 다음 입력은 repeatRate 뒤에
            return true;
        }

        return false;
    }
}