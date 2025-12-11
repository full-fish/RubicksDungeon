using UnityEngine;
using UnityEngine.InputSystem; // ★ 필수

public class PlayerController : MonoBehaviour
{
    private GameManager _manager;

    [Header("입력 민감도 설정")]
    public float initialDelay = 0.3f; 
    public float repeatRate = 0.1f;
    private float _nextInputTime = 0f; 

    [Header("모바일 스와이프 설정")]
    public float minSwipeDistance = 50f; 
    private Vector2 _touchStartPos;
    private bool _isSwiping = false;

    public void Init(GameManager manager)
    {
        _manager = manager;
    }

    void Update()
    {
        if (_manager == null) return;

        // 1. 키보드 입력
        if (Keyboard.current != null)
        {
            HandleKeyboardInput();
        }

        // 2. 포인터(마우스/터치) 스와이프 입력
        if (Pointer.current != null)
        {
            HandleSwipeInput();
        }
    }

    // --- [PC] 키보드 입력 처리 ---
    void HandleKeyboardInput()
    {
        if (CheckKey(Key.RightArrow))      _manager.TryMovePlayer(1, 0);
        else if (CheckKey(Key.LeftArrow))  _manager.TryMovePlayer(-1, 0);
        else if (CheckKey(Key.UpArrow))    _manager.TryMovePlayer(0, 1);
        else if (CheckKey(Key.DownArrow))  _manager.TryMovePlayer(0, -1);

        else if (Keyboard.current[Key.D].wasPressedThisFrame) _manager.TryPushRow(-1);
        else if (Keyboard.current[Key.A].wasPressedThisFrame) _manager.TryPushRow(1);
        else if (Keyboard.current[Key.S].wasPressedThisFrame) _manager.TryPushCol(1);
        else if (Keyboard.current[Key.W].wasPressedThisFrame) _manager.TryPushCol(-1);
        
        else if (Keyboard.current[Key.Z].wasPressedThisFrame) _manager.OnClickUndo();
        else if (Keyboard.current[Key.R].wasPressedThisFrame) _manager.OnClickReset();
    }

    // --- [Mobile/PC] New Input System 기반 스와이프 ---
    void HandleSwipeInput()
    {
        // Pointer.current는 마우스와 터치를 모두 포함합니다.
        
        // 누르는 순간 (wasPressedThisFrame)
        if (Pointer.current.press.wasPressedThisFrame)
        {
            _touchStartPos = Pointer.current.position.ReadValue();
            _isSwiping = true;
        }
        // 떼는 순간 (wasReleasedThisFrame)
        else if (Pointer.current.press.wasReleasedThisFrame && _isSwiping)
        {
            _isSwiping = false;
            Vector2 touchEndPos = Pointer.current.position.ReadValue();
            ProcessSwipe(_touchStartPos, touchEndPos);
        }
    }

    void ProcessSwipe(Vector2 start, Vector2 end)
    {
        Vector2 swipeVector = end - start;

        if (swipeVector.magnitude < minSwipeDistance) return;

        if (Mathf.Abs(swipeVector.x) > Mathf.Abs(swipeVector.y))
        {
            if (swipeVector.x > 0) _manager.TryMovePlayer(1, 0); 
            else                   _manager.TryMovePlayer(-1, 0); 
        }
        else
        {
            if (swipeVector.y > 0) _manager.TryMovePlayer(0, 1);  
            else                   _manager.TryMovePlayer(0, -1); 
        }
    }

    bool CheckKey(Key key)
    {
        var keyControl = Keyboard.current[key];
        if (keyControl.wasPressedThisFrame)
        {
            _nextInputTime = Time.time + initialDelay; 
            return true;
        }
        if (keyControl.isPressed && Time.time >= _nextInputTime)
        {
            _nextInputTime = Time.time + repeatRate; 
            return true;
        }
        return false;
    }
}