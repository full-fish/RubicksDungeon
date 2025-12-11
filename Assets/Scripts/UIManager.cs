using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    public GameObject failText;
    public GameObject clearText;
    public GameObject allClearPanel; 
    public TextMeshProUGUI textShiftCount;

    public void HideAll() {
        if (failText) failText.SetActive(false);
        if (clearText) clearText.SetActive(false);
        if (allClearPanel) allClearPanel.SetActive(false);
    }
    public void ShowFail() => failText?.SetActive(true);
    public void ShowClear() => clearText?.SetActive(true);
    public void ShowAllClear() { HideAll(); allClearPanel?.SetActive(true); }
    public void UpdateShiftText(int current, int max) {
        if (textShiftCount) textShiftCount.text = $"{current}"; 
    }
}