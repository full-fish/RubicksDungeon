using UnityEngine;

public class UIManager : MonoBehaviour
{
    public GameObject failText;
    public GameObject clearText;

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
}