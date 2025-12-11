using UnityEngine;
using UnityEngine.InputSystem; 

public class PlayerController : MonoBehaviour
{
    private GameManager _manager;

    [Header("입력 민감도 설정")]
    public float initialDelay = 0.3f; 
    public float repeatRate = 0.15f; 
    private float _nextInputTime = 0f; 

    [Header("모바일 스와이프 설정")]
    public float minSwipeDistance = 50f; 
    private Vector2 _touchStartPos;
    private bool _isSwiping = false;
    private bool _isControlCharacter = true; 

    public void Init(GameManager manager)
    {
        _manager = manager;
    }

    void Update()
    {
        if (_manager == null) return;

        if (Keyboard.current != null)
        {
            HandleKeyboardInput();
        }

        if (Pointer.current != null)
        {
            HandleSwipeInput();
        }
    }

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

    void HandleSwipeInput()
    {
        if (Pointer.current.press.wasPressedThisFrame)
        {
            _touchStartPos = Pointer.current.position.ReadValue();
            
            // 화면 분할 로직
            if (_touchStartPos.x > Screen.width / 2f)
            {
                _isControlCharacter = true;
            }
            else
            {
                _isControlCharacter = false;
            }

            _isSwiping = true;
        }
        
        else if (Pointer.current.press.isPressed && _isSwiping)
        {
            Vector2 currentPos = Pointer.current.position.ReadValue();
            Vector2 swipeVector = currentPos - _touchStartPos;

            if (swipeVector.magnitude >= minSwipeDistance)
            {
                if (Time.time >= _nextInputTime)
                {
                    if (_isControlCharacter)
                    {
                        ProcessCharacterMove(swipeVector);
                    }
                    else
                    {
                        ProcessMapShift(swipeVector);
                    }
                    
                    _touchStartPos = currentPos; 
                    _nextInputTime = Time.time + repeatRate;
                }
            }
        }
        else if (Pointer.current.press.wasReleasedThisFrame)
        {
            _isSwiping = false;
        }
    }

    void ProcessCharacterMove(Vector2 swipeVector)
    {
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

    // ★ [수정됨] 맵 회전 방향 반전
    void ProcessMapShift(Vector2 swipeVector)
    {
        if (Mathf.Abs(swipeVector.x) > Mathf.Abs(swipeVector.y))
        {
            // 가로 스와이프
            // 오른쪽(x>0)으로 밀면 -> 왼쪽(-1)으로 이동
            if (swipeVector.x > 0) _manager.TryPushRow(-1); // 기존 1 -> -1 변경
            else                   _manager.TryPushRow(1);  // 기존 -1 -> 1 변경
        }
        else
        {
            // 세로 스와이프
            // 위로(y>0) 밀면 -> 아래쪽(-1)으로 이동 (또는 반대)
            // (만약 이것도 반대라면 -1과 1을 서로 또 바꿔주세요)
            if (swipeVector.y > 0) _manager.TryPushCol(-1); // 기존 1 -> -1 변경
            else                   _manager.TryPushCol(1);  // 기존 -1 -> 1 변경
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