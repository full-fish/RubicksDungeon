using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public GameObject failText;
    public GameObject clearText;
    
    // ★ [추가] 모든 스테이지 클리어 시 띄울 패널 (축하 메시지 + 재시작 버튼 등)
    public GameObject allClearPanel; 

    public TextMeshProUGUI textShiftCount;

    public void HideAll()
    {
        if (failText != null) failText.SetActive(false);
        if (clearText != null) clearText.SetActive(false);
        // ★ [추가] 엔딩 패널도 숨기기
        if (allClearPanel != null) allClearPanel.SetActive(false);
    }

    public void ShowFail()
    {
        if (failText != null) failText.SetActive(true);
    }

    public void ShowClear()
    {
        if (clearText != null) clearText.SetActive(true);
    }

    // ★ [추가] 엔딩 화면 보여주기 함수
    public void ShowAllClear()
    {
        HideAll(); // 다른 텍스트는 다 끄고
        if (allClearPanel != null) allClearPanel.SetActive(true);
    }

    public void UpdateShiftText(int current, int max)
    {
        if (textShiftCount != null)
        {
            textShiftCount.text = $"{current}"; 
        }
    }
}