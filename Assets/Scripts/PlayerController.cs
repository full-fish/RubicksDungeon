using UnityEngine;
using UnityEngine.InputSystem; 

public class PlayerController : MonoBehaviour
{
    private GameManager _manager;

    [Header("입력 민감도 설정")]
    public float initialDelay = 0.3f; 
    public float repeatRate = 0.15f; // 연속 이동 시 쿨타임 (너무 빠르면 늘리세요)
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

    // --- [Mobile/PC] 연속 스와이프 로직 ---
    void HandleSwipeInput()
    {
        // 1. 누르는 순간 (기준점 잡기)
        if (Pointer.current.press.wasPressedThisFrame)
        {
            _touchStartPos = Pointer.current.position.ReadValue();
            _isSwiping = true;
        }
        
        // 2. 누르고 있는 동안 (실시간 거리 체크)
        else if (Pointer.current.press.isPressed && _isSwiping)
        {
            Vector2 currentPos = Pointer.current.position.ReadValue();
            Vector2 swipeVector = currentPos - _touchStartPos;

            // 최소 거리 이상 움직였는지 확인
            if (swipeVector.magnitude >= minSwipeDistance)
            {
                // 쿨타임 체크 (너무 빠른 연속 입력 방지)
                if (Time.time >= _nextInputTime)
                {
                    ProcessSwipe(swipeVector);
                    
                    // ★ [핵심] 이동했으면 기준점을 현재 위치로 갱신!
                    // 그래야 손을 안 떼고 계속 밀었을 때 다음 이동이 발동됨
                    _touchStartPos = currentPos; 
                    
                    // 쿨타임 적용
                    _nextInputTime = Time.time + repeatRate;
                }
            }
        }

        // 3. 떼는 순간 (초기화)
        else if (Pointer.current.press.wasReleasedThisFrame)
        {
            _isSwiping = false;
        }
    }

    void ProcessSwipe(Vector2 swipeVector)
    {
        // X축 이동량이 Y축보다 크면 -> 가로 이동
        if (Mathf.Abs(swipeVector.x) > Mathf.Abs(swipeVector.y))
        {
            if (swipeVector.x > 0) _manager.TryMovePlayer(1, 0); 
            else                   _manager.TryMovePlayer(-1, 0); 
        }
        // Y축 이동량이 더 크면 -> 세로 이동
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