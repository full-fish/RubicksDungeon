using UnityEngine;
using TMPro;
public class UIManager : MonoBehaviour
{
    public GameObject failText;
    public GameObject clearText;
    public TextMeshProUGUI textShiftCount;
    public void HideAll()
    {
        if (failText != null) failText.SetActive(false);
        if (clearText != null) clearText.SetActive(false);
    }

    public void ShowFail()
    {
        if (failText != null) failText.SetActive(true);
    }

    public void ShowClear()
    {
        if (clearText != null) clearText.SetActive(true);
    }

    public void UpdateShiftText(int current, int max)
    {
        if (textShiftCount != null)
        {
            textShiftCount.text = $"{current}"; 
        }
    }
}