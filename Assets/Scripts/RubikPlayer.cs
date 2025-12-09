using UnityEngine;
using UnityEngine.InputSystem;

public class RubikPlayer : MonoBehaviour
{
    private RubikManager _manager;

    public void Init(RubikManager manager)
    {
        _manager = manager;
    }

    void Update()
    {
        if (_manager == null || Keyboard.current == null) return;

        HandleMovement();
        HandleInteraction();
    }

    void HandleMovement()
    {
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame) _manager.TryMovePlayer(1, 0);
        else if (Keyboard.current.leftArrowKey.wasPressedThisFrame) _manager.TryMovePlayer(-1, 0);
        else if (Keyboard.current.upArrowKey.wasPressedThisFrame) _manager.TryMovePlayer(0, 1);
        else if (Keyboard.current.downArrowKey.wasPressedThisFrame) _manager.TryMovePlayer(0, -1);
    }

    void HandleInteraction()
    {
        // 맵 회전 (W, S 부호 반대로 설정됨)
        if (Keyboard.current.dKey.wasPressedThisFrame) _manager.TryPushRow(-1);
        else if (Keyboard.current.aKey.wasPressedThisFrame) _manager.TryPushRow(1);
        else if (Keyboard.current.sKey.wasPressedThisFrame) _manager.TryPushCol(1);
        else if (Keyboard.current.wKey.wasPressedThisFrame) _manager.TryPushCol(-1);
    }
}