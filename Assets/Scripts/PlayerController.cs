using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    // ★ RubikManager -> GameManager로 변경됨
    private GameManager _manager;

    [Header("입력 민감도 설정")]
    public float initialDelay = 0.3f; 
    public float repeatRate = 0.1f;

    private float _nextInputTime = 0f; 

    // ★ 초기화 함수도 GameManager를 받도록 수정
    public void Init(GameManager manager)
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
        else if (CheckKey(Key.D)) _manager.TryPushRow(-1);
        else if (CheckKey(Key.A)) _manager.TryPushRow(1);
        else if (CheckKey(Key.S)) _manager.TryPushCol(1);
        else if (CheckKey(Key.W)) _manager.TryPushCol(-1);
        
        // 3. 기능 입력 (Undo / Reset)
        else if (Keyboard.current[Key.Z].wasPressedThisFrame) _manager.OnClickUndo();
        else if (Keyboard.current[Key.R].wasPressedThisFrame) _manager.OnClickReset();
    }

    bool CheckKey(Key key)
    {
        var keyControl = Keyboard.current[key];

        // 1. 방금 막 눌렀을 때
        if (keyControl.wasPressedThisFrame)
        {
            _nextInputTime = Time.time + initialDelay; 
            return true;
        }

        // 2. 꾹 누르고 있을 때
        if (keyControl.isPressed && Time.time >= _nextInputTime)
        {
            _nextInputTime = Time.time + repeatRate; 
            return true;
        }

        return false;
    }
}